using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PingTracer.Storage
{
	/// <summary>Compression applied to the body of a .ptrc file.  Stored in the file header.</summary>
	public enum PingDataCompression : byte
	{
		None = 0,
		/// <summary>Brotli (RFC 7932), as one stream that is flushed (not finished) at every disk flush.</summary>
		Brotli = 1,
	}

	/// <summary>
	/// Constants and helpers for the .ptrc file format.
	/// <para>
	/// A file holds one <b>segment</b>: the data recorded for one host of one ping configuration during one run, within one
	/// local calendar day.  A run that spans midnight continues in a new segment, and Start always begins a new segment, so
	/// old data can be discarded by deleting whole files and no file ever needs to be modified after it is closed.
	/// </para>
	/// <para>
	/// Version 4 layout.  The header is never compressed, so a file can be identified (and an unsupported compression method
	/// reported) without decoding the body:
	/// </para>
	/// <code>
	/// Header:
	///   "PTRC"           4 bytes
	///   Version          u16  (= 4)
	///   Compression      u8   (see PingDataCompression)
	///   Flags            u8   (reserved, 0)
	///   StartUnixMs      i64  when the segment began
	///   EndUnixMs        i64  latest record or event time written so far (0 = none).  Rewritten in place at each flush; the
	///                         only bytes of a file that are ever overwritten.
	///   InfoLength       u32  length of the info section that follows (readers skip fields they do not know)
	///   Info:
	///     Mode           u8   (1 = traceroute, 2 = direct)
	///     IntervalMs     i32
	///     TimeoutMs      i32
	///     TargetAddress  u8 length (4 or 16) + bytes
	///     DisplayName    u16 length + UTF-8
	///     ConfigGuid     u16 length + UTF-8   (the ping configuration this data belongs to)
	///     HostKey        u16 length + UTF-8   (the configuration's host entry, normalized; see MonitoringSession.HostKey)
	/// Body (compressed as one stream): a sequence of blocks
	///   Type u8, BodyLength varint, Body
	///   0x10 Address     id u16, address (u8 length + bytes)
	///   0x11 Hostname    id u16, u16 length + UTF-8
	///   0x12 Records     ttl u8, baseTimeUnixMs i64, count varint, then three columns of count varints each:
	///                    time:    zigzag(d[k] - d[k-1]) where d[k] = t[k] - t[k-1], t[-1] = baseTime, d[-1] = 0
	///                    rtt:     zigzag(c[k] - c[k-1]) where c = 0 for a lost ping, else rtt + 1; c[-1] = 0
	///                    address: (addressId + 1) mod 65536, so "no address" is 0
	///   0x13 RouteEvent  timeUnixMs i64, kind u8, ttl u8, value u16, initial u8
	///   0x14 State       destinationTtl u8   (written at the start of each segment so it stands on its own)
	/// </code>
	/// <para>
	/// All fixed-size integers are little-endian and all times are absolute Unix milliseconds.  Address ids are local to the
	/// file: every segment begins with the complete address table, so it can be read without the segments before it.  Unknown
	/// block types are skipped.  A crash can leave a partial block at the end, which readers ignore.
	/// </para>
	/// <para>
	/// The column layout with second-order time deltas turns a steady ping cadence into runs of zero bytes; with Brotli this
	/// stores about one byte per ping, versus six bytes per ping uncompressed in the version 3 format.
	/// </para>
	/// <para>Version 3 files (single-session files with session-relative times and no compression) can still be read.</para>
	/// </summary>
	public static class PingDataFile
	{
		public static readonly byte[] Magic = new byte[] { (byte)'P', (byte)'T', (byte)'R', (byte)'C' };
		public const ushort CurrentVersion = 4;
		public const ushort LegacyVersion = 3;
		public const string FileExtension = ".ptrc";

		/// <summary>Byte offset of the EndUnixMs header field in a version 4 file.</summary>
		public const int EndTimeOffset = 16;
		/// <summary>Brotli quality used when writing.  Quality 5 to 6 compressed PingTracer data best per CPU cost in testing.</summary>
		public const int BrotliQuality = 5;
		/// <summary>Brotli window (log2 bytes).</summary>
		public const int BrotliWindow = 22;

		public const byte BlockAddress = 0x10;
		public const byte BlockHostname = 0x11;
		public const byte BlockRecords = 0x12;
		public const byte BlockRouteEvent = 0x13;
		public const byte BlockState = 0x14;

		/// <summary>Records per block are limited only to keep blocks a reasonable size.</summary>
		public const int MaxRecordsPerBlock = 8192;
		/// <summary>Largest block body a reader accepts (protects against garbage lengths).</summary>
		public const int MaxBlockLength = 1 << 24;

		#region File names and directories

		private static readonly Regex unsafeChars = new Regex(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);
		private static string SanitizeForFilename(string s) => unsafeChars.Replace(s ?? "", "-");

		/// <summary>Builds a file name for a new segment.  Names sort by start time.</summary>
		public static string BuildSegmentFileName(string hostKey, SessionMode mode, long startUnixMs)
		{
			string host = SanitizeForFilename(hostKey);
			if (host.Length > 60) host = host.Substring(0, 60);
			if (host.Length == 0) host = "host";
			DateTime start = ServerClock.ToDateTime(startUnixMs).ToLocalTime();
			return $"{start:yyyyMMdd-HHmmss}-{host}-{(mode == SessionMode.Direct ? "direct" : "trace")}{FileExtension}";
		}

		/// <summary>Resolves the directory where data files live: an explicit directory, or "data" in the writable directory.</summary>
		public static string ResolveDataDirectory(string explicitDir, string writableDirectoryBase)
		{
			if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;
			return Path.Combine(writableDirectoryBase ?? string.Empty, "data");
		}

		/// <summary>The first instant of the local calendar day after the one containing <paramref name="unixMs"/>.</summary>
		public static long NextLocalMidnightUnixMs(long unixMs)
		{
			DateTime local = ServerClock.ToDateTime(unixMs).ToLocalTime();
			DateTime next = local.Date.AddDays(1);
			return new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Local)).ToUnixTimeMilliseconds();
		}

		#endregion

		#region Header

		public sealed class Header
		{
			public ushort Version;
			public PingDataCompression Compression;
			public long StartUnixMs;
			/// <summary>Latest time written (version 4 only; 0 if unknown).</summary>
			public long EndUnixMs;
			public SessionMode Mode;
			public int IntervalMs;
			public int TimeoutMs;
			public IPAddress Target;
			public string DisplayName;
			public string ConfigGuid = "";
			public string HostKey = "";
		}

		/// <summary>Writes a version 4 header.  The stream must be at position 0.</summary>
		public static void WriteHeader(Stream s, Header h)
		{
			MemoryStream info = new MemoryStream();
			BinaryWriter iw = new BinaryWriter(info, Encoding.UTF8);
			iw.Write((byte)h.Mode);
			iw.Write(h.IntervalMs);
			iw.Write(h.TimeoutMs);
			WriteIp(iw, h.Target);
			WriteString(iw, h.DisplayName);
			WriteString(iw, h.ConfigGuid);
			WriteString(iw, h.HostKey);
			iw.Flush();

			BinaryWriter w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
			w.Write(Magic);
			w.Write(CurrentVersion);
			w.Write((byte)h.Compression);
			w.Write((byte)0);
			w.Write(h.StartUnixMs);
			w.Write(h.EndUnixMs);
			w.Write((uint)info.Length);
			w.Write(info.GetBuffer(), 0, (int)info.Length);
			w.Flush();
		}

		/// <summary>
		/// Reads a header (version 3 or 4), leaving the stream at the start of the body.  Throws <see cref="InvalidDataException"/>
		/// for a file that is not a PingTracer data file, and <see cref="NotSupportedException"/> for a newer version or an
		/// unknown compression method.
		/// </summary>
		public static Header ReadHeader(BinaryReader r)
		{
			byte[] magic = r.ReadBytes(4);
			if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
				throw new InvalidDataException("Not a PingTracer data file.");
			Header h = new Header();
			h.Version = r.ReadUInt16();
			if (h.Version == LegacyVersion)
			{
				h.Compression = PingDataCompression.None;
				h.StartUnixMs = r.ReadInt64();
				h.Mode = (SessionMode)r.ReadByte();
				h.IntervalMs = r.ReadInt32();
				h.TimeoutMs = r.ReadInt32();
				h.Target = ReadIp(r) ?? IPAddress.Any;
				h.DisplayName = ReadString(r);
				return h;
			}
			if (h.Version != CurrentVersion)
				throw new NotSupportedException("This data file was written by a newer version of PingTracer (file format version " + h.Version + ").");
			h.Compression = (PingDataCompression)r.ReadByte();
			r.ReadByte(); // flags
			h.StartUnixMs = r.ReadInt64();
			h.EndUnixMs = r.ReadInt64();
			uint infoLength = r.ReadUInt32();
			if (infoLength > 1 << 20)
				throw new InvalidDataException("Corrupt data file header.");
			byte[] info = r.ReadBytes((int)infoLength);
			if (info.Length != infoLength)
				throw new EndOfStreamException();
			BinaryReader ir = new BinaryReader(new MemoryStream(info), Encoding.UTF8);
			h.Mode = (SessionMode)ir.ReadByte();
			h.IntervalMs = ir.ReadInt32();
			h.TimeoutMs = ir.ReadInt32();
			h.Target = ReadIp(ir) ?? IPAddress.Any;
			h.DisplayName = ReadString(ir);
			h.ConfigGuid = ReadString(ir);
			h.HostKey = ReadString(ir);
			if (h.Compression != PingDataCompression.None && h.Compression != PingDataCompression.Brotli)
				throw new NotSupportedException("This data file uses a compression method (" + (byte)h.Compression + ") that this version of PingTracer does not support.");
			return h;
		}

		/// <summary>Reads the header of a file.  Opens it with sharing that permits a writer.</summary>
		public static Header ReadHeader(string path)
		{
			using FileStream fs = OpenRead(path, 4096);
			using BinaryReader r = new BinaryReader(fs);
			return ReadHeader(r);
		}

		public static FileStream OpenRead(string path, int bufferSize = 65536)
			=> new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize);

		/// <summary>Wraps a stream positioned at the start of a file body with the decompressor named in the header.</summary>
		public static Stream OpenBody(Stream afterHeader, Header h)
		{
			switch (h.Compression)
			{
				case PingDataCompression.None: return afterHeader;
				case PingDataCompression.Brotli: return new BrotliStream(afterHeader, CompressionMode.Decompress, leaveOpen: true);
				default: throw new NotSupportedException("Unsupported compression method " + (byte)h.Compression + ".");
			}
		}

		#endregion

		#region Primitive encoding

		public static void WriteIp(BinaryWriter w, IPAddress ip)
		{
			byte[] b = ip?.GetAddressBytes() ?? Array.Empty<byte>();
			w.Write((byte)b.Length);
			w.Write(b);
		}

		public static IPAddress ReadIp(BinaryReader r)
		{
			int len = r.ReadByte();
			if (len == 0) return null;
			byte[] b = r.ReadBytes(len);
			if (b.Length != len) throw new EndOfStreamException();
			if (len != 4 && len != 16) throw new InvalidDataException("Invalid address length " + len + ".");
			return new IPAddress(b);
		}

		public static void WriteString(BinaryWriter w, string s)
		{
			byte[] b = Encoding.UTF8.GetBytes(s ?? "");
			if (b.Length > ushort.MaxValue)
				Array.Resize(ref b, ushort.MaxValue);
			w.Write((ushort)b.Length);
			w.Write(b);
		}

		public static string ReadString(BinaryReader r)
		{
			int len = r.ReadUInt16();
			byte[] b = r.ReadBytes(len);
			if (b.Length != len) throw new EndOfStreamException();
			return Encoding.UTF8.GetString(b);
		}

		public static void WriteVarUInt(Stream s, ulong v)
		{
			while (v >= 0x80)
			{
				s.WriteByte((byte)(v | 0x80));
				v >>= 7;
			}
			s.WriteByte((byte)v);
		}

		public static ulong ZigZag(long v) => (ulong)((v << 1) ^ (v >> 63));
		public static long UnZigZag(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);

		/// <summary>Reads a varint from a buffer.  Throws <see cref="EndOfStreamException"/> past <paramref name="end"/>.</summary>
		public static ulong ReadVarUInt(byte[] buf, ref int pos, int end)
		{
			ulong v = 0;
			int shift = 0;
			while (true)
			{
				if (pos >= end) throw new EndOfStreamException();
				byte b = buf[pos++];
				v |= (ulong)(b & 0x7F) << shift;
				if (b < 0x80) return v;
				shift += 7;
				if (shift > 63) throw new InvalidDataException("Invalid varint.");
			}
		}

		/// <summary>Reads a varint from a stream.  Returns false at a clean end of stream (no bytes read).</summary>
		public static bool TryReadVarUInt(Stream s, out ulong v)
		{
			v = 0;
			int shift = 0;
			for (int i = 0; ; i++)
			{
				int b = s.ReadByte();
				if (b < 0)
				{
					if (i == 0) return false;
					throw new EndOfStreamException();
				}
				v |= (ulong)(b & 0x7F) << shift;
				if (b < 0x80) return true;
				shift += 7;
				if (shift > 63) throw new InvalidDataException("Invalid varint.");
			}
		}

		#endregion

		#region Listing

		/// <summary>Describes a data file without loading its data.</summary>
		public sealed class FileInfoSummary
		{
			/// <summary>Path relative to the directory that was listed, with forward slashes.</summary>
			public string FileName;
			public string FullPath;
			public long SizeBytes;
			public long StartUnixMs;
			public long LastWriteUnixMs;
			/// <summary>Time of the latest record or route event.</summary>
			public long EndUnixMs;
			public string DisplayName;
			public string Target;
			public SessionMode Mode;
			public ushort Version;
			public string ConfigGuid;
			public string HostKey;
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long size, long lastWrite, long end)> endTimeCache
			= new System.Collections.Concurrent.ConcurrentDictionary<string, (long, long, long)>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Reads the header of one file into a summary.  For a version 4 file the end time comes from the header; a version 3
		/// file is scanned (with a cache keyed on size and modification time).  Returns null if the file cannot be read.
		/// </summary>
		public static FileInfoSummary Describe(string path, string relativeName = null)
		{
			try
			{
				FileInfo fi = new FileInfo(path);
				using FileStream fs = OpenRead(path);
				using BinaryReader r = new BinaryReader(fs);
				Header h = ReadHeader(r);
				long lastWrite = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
				long end;
				if (h.Version == LegacyVersion)
				{
					if (endTimeCache.TryGetValue(fi.FullName, out var c) && c.size == fi.Length && c.lastWrite == lastWrite)
						end = c.end;
					else
					{
						end = h.StartUnixMs + ReadLegacyLastTimeMs(fs);
						endTimeCache[fi.FullName] = (fi.Length, lastWrite, end);
					}
				}
				else
					end = h.EndUnixMs > 0 ? Math.Max(h.EndUnixMs, h.StartUnixMs) : h.StartUnixMs;
				return new FileInfoSummary
				{
					FileName = relativeName ?? fi.Name,
					FullPath = fi.FullName,
					SizeBytes = fi.Length,
					StartUnixMs = h.StartUnixMs,
					LastWriteUnixMs = lastWrite,
					EndUnixMs = end,
					DisplayName = h.DisplayName,
					Target = h.Target.ToString(),
					Mode = h.Mode,
					Version = h.Version,
					ConfigGuid = h.ConfigGuid,
					HostKey = h.HostKey,
				};
			}
			catch
			{
				return null;
			}
		}

		/// <summary>Lists readable data files in a directory (optionally including subdirectories), newest first.</summary>
		public static List<FileInfoSummary> ListSessionFiles(string directory, bool recursive = false)
		{
			List<FileInfoSummary> list = new List<FileInfoSummary>();
			if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
				return list;
			string root = Path.GetFullPath(directory);
			foreach (string path in Directory.GetFiles(root, "*" + FileExtension, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
			{
				string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
				FileInfoSummary s = Describe(path, rel);
				if (s != null)
					list.Add(s);
			}
			list.Sort((a, b) => b.StartUnixMs.CompareTo(a.StartUnixMs));
			return list;
		}

		/// <summary>
		/// Scans the blocks of a version 3 file body for the latest record or route event time (session-relative milliseconds,
		/// 0 if none).  A truncated final block ends the scan.
		/// </summary>
		private static long ReadLegacyLastTimeMs(Stream s)
		{
			long last = 0;
			byte[] head = new byte[5];
			byte[] body = Array.Empty<byte>();
			BufferedStream bs = new BufferedStream(s, 1 << 16);
			while (true)
			{
				if (bs.ReadAtLeast(head, 5, throwOnEndOfStream: false) < 5) break;
				byte type = head[0];
				int len = BitConverter.ToInt32(head, 1);
				if (len < 0 || len > MaxBlockLength) break;
				if (body.Length < len) body = new byte[Math.Max(len, body.Length * 2)];
				if (bs.ReadAtLeast(body.AsSpan(0, len), len, throwOnEndOfStream: false) < len) break;
				if (type == BlockRouteEvent && len >= 8)
					last = Math.Max(last, BitConverter.ToInt64(body, 0));
				else if (type == BlockRecords && len >= 11)
				{
					long baseTime = BitConverter.ToInt64(body, 1);
					int count = BitConverter.ToUInt16(body, 9);
					int maxDt = -1;
					for (int i = 0, o = 11; i < count && o + 2 <= len; i++, o += 6)
						maxDt = Math.Max(maxDt, BitConverter.ToUInt16(body, o));
					if (maxDt >= 0) last = Math.Max(last, baseTime + maxDt);
				}
			}
			return last;
		}

		#endregion
	}
}
