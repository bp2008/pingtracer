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
			public string DisplayName;
			public string Target;
			public SessionMode Mode;
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
					list.Add(new FileInfoSummary
					{
						FileName = fi.Name,
						FullPath = fi.FullName,
						SizeBytes = fi.Length,
						StartUnixMs = h.StartUnixMs,
						LastWriteUnixMs = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
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
