using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PingTracer.Storage
{
	/// <summary>
	/// Constants and helpers for the .ptrc session file format.
	/// <para>
	/// A file is a header followed by a sequential, append-only log of length-prefixed blocks.  Blocks are never modified
	/// after being written, so a file can be read while it is being written, and a crash can at worst leave one truncated
	/// block at the end (which the reader ignores).
	/// </para>
	/// <code>
	/// Header:
	///   "PTRC"           4 bytes
	///   Version          u16  (= 3)
	///   SessionStartMs   i64  Unix epoch milliseconds
	///   Mode             u8   (1 = traceroute, 2 = direct)
	///   IntervalMs       i32
	///   TimeoutMs        i32
	///   TargetAddress    u8 length (4 or 16) + bytes
	///   DisplayName      u16 length + UTF-8
	/// Block:
	///   Type u8, BodyLength u32, Body
	///   0x10 Address     id u16, address (u8 length + bytes)
	///   0x11 Hostname    id u16, u16 length + UTF-8
	///   0x12 Records     ttl u8, baseTimeMs i64, count u16, count x { deltaMs u16, rtt u16, addressId u16 }
	///   0x13 RouteEvent  timeMs i64, kind u8, ttl u8, value u16, initial u8
	/// </code>
	/// All integers are little-endian.  Record times are session-relative milliseconds.
	/// </summary>
	public static class PingDataFile
	{
		public static readonly byte[] Magic = new byte[] { (byte)'P', (byte)'T', (byte)'R', (byte)'C' };
		public const ushort CurrentVersion = 3;
		public const string FileExtension = ".ptrc";

		public const byte BlockAddress = 0x10;
		public const byte BlockHostname = 0x11;
		public const byte BlockRecords = 0x12;
		public const byte BlockRouteEvent = 0x13;

		public const int MaxRecordsPerBlock = ushort.MaxValue;
		public const int RecordBytes = 6;

		/// <summary>Builds a file name for a new session file.</summary>
		public static string BuildSessionFileName(IPAddress target, long startUnixMs)
		{
			string addr = target == null ? "unknown" : SanitizeForFilename(target.ToString());
			DateTime start = ServerClock.ToDateTime(startUnixMs).ToLocalTime();
			return $"session-{start:yyyyMMdd-HHmmss}-{addr}{FileExtension}";
		}

		private static readonly Regex unsafeChars = new Regex(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);
		private static string SanitizeForFilename(string s) => unsafeChars.Replace(s, "-");

		/// <summary>Resolves the directory where session files live.</summary>
		public static string ResolveDataDirectory(string explicitDir, string settingsFolder)
		{
			if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;
			return Path.Combine(settingsFolder ?? string.Empty, "data");
		}

		public sealed class Header
		{
			public ushort Version;
			public long StartUnixMs;
			public SessionMode Mode;
			public int IntervalMs;
			public int TimeoutMs;
			public IPAddress Target;
			public string DisplayName;
		}

		public static void WriteHeader(Stream s, MonitoringSession session)
		{
			BinaryWriter w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
			w.Write(Magic);
			w.Write(CurrentVersion);
			w.Write(session.StartUnixMs);
			w.Write((byte)session.Mode);
			w.Write(session.IntervalMs);
			w.Write(session.TimeoutMs);
			WriteIp(w, session.TargetAddress);
			WriteString(w, session.DisplayName);
			w.Flush();
		}

		public static Header ReadHeader(BinaryReader r)
		{
			byte[] magic = r.ReadBytes(4);
			if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
				throw new InvalidDataException("Not a PingTracer session file.");
			Header h = new Header();
			h.Version = r.ReadUInt16();
			if (h.Version != CurrentVersion)
				throw new InvalidDataException("Unsupported session file version " + h.Version + ".");
			h.StartUnixMs = r.ReadInt64();
			h.Mode = (SessionMode)r.ReadByte();
			h.IntervalMs = r.ReadInt32();
			h.TimeoutMs = r.ReadInt32();
			h.Target = ReadIp(r) ?? IPAddress.Any;
			h.DisplayName = ReadString(r);
			return h;
		}

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

		/// <summary>Describes a session file without loading its data.</summary>
		public sealed class FileInfoSummary
		{
			public string FileName;
			public string FullPath;
			public long SizeBytes;
			public long StartUnixMs;
			public long LastWriteUnixMs;
			/// <summary>Time of the latest record or route event (the file's modification time is unreliable: copying or restoring a file changes it).</summary>
			public long EndUnixMs;
			public string DisplayName;
			public string Target;
			public SessionMode Mode;
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long size, long lastWrite, long end)> endTimeCache
			= new System.Collections.Concurrent.ConcurrentDictionary<string, (long, long, long)>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The session's end time, from a cache keyed on the file's size and modification time.</summary>
		private static long GetEndUnixMs(Stream afterHeader, string path, long size, long lastWrite, long startUnixMs)
		{
			if (endTimeCache.TryGetValue(path, out var c) && c.size == size && c.lastWrite == lastWrite)
				return c.end;
			long end = startUnixMs + ReadLastTimeMs(afterHeader);
			endTimeCache[path] = (size, lastWrite, end);
			return end;
		}

		/// <summary>
		/// Scans the blocks after the header for the latest record or route event time (session-relative milliseconds, 0 if
		/// none).  Records within a block are not necessarily in time order, so every record time is examined.  A truncated
		/// final block ends the scan.
		/// </summary>
		public static long ReadLastTimeMs(Stream s)
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
				if (len < 0) break;
				if (type != BlockRecords && type != BlockRouteEvent)
				{
					if (!Skip(bs, len)) break;
					continue;
				}
				if (body.Length < len) body = new byte[Math.Max(len, body.Length * 2)];
				if (bs.ReadAtLeast(body.AsSpan(0, len), len, throwOnEndOfStream: false) < len) break;
				if (type == BlockRouteEvent && len >= 8)
					last = Math.Max(last, BitConverter.ToInt64(body, 0));
				else if (type == BlockRecords && len >= 11)
				{
					long baseTime = BitConverter.ToInt64(body, 1);
					int count = BitConverter.ToUInt16(body, 9);
					int maxDt = -1;
					for (int i = 0, o = 11; i < count && o + 2 <= len; i++, o += RecordBytes)
						maxDt = Math.Max(maxDt, BitConverter.ToUInt16(body, o));
					if (maxDt >= 0) last = Math.Max(last, baseTime + maxDt);
				}
			}
			return last;
		}

		private static bool Skip(Stream s, int count)
		{
			if (s.CanSeek)
			{
				if (s.Position + count > s.Length) return false;
				s.Seek(count, SeekOrigin.Current);
				return true;
			}
			byte[] tmp = new byte[Math.Min(count, 8192)];
			while (count > 0)
			{
				int n = s.Read(tmp, 0, Math.Min(count, tmp.Length));
				if (n <= 0) return false;
				count -= n;
			}
			return true;
		}

		/// <summary>Lists readable session files in a directory, newest first.</summary>
		public static List<FileInfoSummary> ListSessionFiles(string directory)
		{
			List<FileInfoSummary> list = new List<FileInfoSummary>();
			if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
				return list;
			foreach (string path in Directory.GetFiles(directory, "*" + FileExtension))
			{
				try
				{
					FileInfo fi = new FileInfo(path);
					using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					using BinaryReader r = new BinaryReader(fs);
					Header h = ReadHeader(r);
					long lastWrite = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
					list.Add(new FileInfoSummary
					{
						FileName = fi.Name,
						FullPath = fi.FullName,
						SizeBytes = fi.Length,
						StartUnixMs = h.StartUnixMs,
						LastWriteUnixMs = lastWrite,
						EndUnixMs = GetEndUnixMs(fs, fi.FullName, fi.Length, lastWrite, h.StartUnixMs),
						DisplayName = h.DisplayName,
						Target = h.Target.ToString(),
						Mode = h.Mode,
					});
				}
				catch { }
			}
			list.Sort((a, b) => b.StartUnixMs.CompareTo(a.StartUnixMs));
			return list;
		}
	}
}
