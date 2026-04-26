using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace PingTracer.Storage
{
	/// <summary>
	/// Constants, magic numbers, and shared helpers for the .ptrc on-disk session log
	/// format. The format is a sequential append-only log: a fixed header followed by
	/// length-prefixed blocks. See plan.md (Phase 5) for the full specification.
	/// </summary>
	public static class PingDataFile
	{
		public static readonly byte[] Magic = new byte[] { (byte)'P', (byte)'T', (byte)'R', (byte)'C' };
		public const ushort CurrentVersion = 1;
		public const string FileExtension = ".ptrc";

		public const byte BlockType_HopTimeSeriesStart = 0x01;
		public const byte BlockType_ReverseDnsResult = 0x02;
		public const byte BlockType_PingData = 0x03;

		/// <summary>Maximum records a single PingDataBlock may contain (uint16 count).</summary>
		public const int MaxRecordsPerBlock = ushort.MaxValue;

		/// <summary>Maximum span in ms a single PingDataBlock may cover (uint16 record offset).</summary>
		public const uint MaxBlockTimeSpanMs = ushort.MaxValue;

		public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static ulong ToUnixMs(DateTime utc)
		{
			DateTime u = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
			if (u < UnixEpoch) return 0;
			return (ulong)(u - UnixEpoch).TotalMilliseconds;
		}

		public static DateTime FromUnixMs(ulong ms)
		{
			return UnixEpoch.AddMilliseconds(ms);
		}

		/// <summary>
		/// Builds a filename for a new session file. Address segment is sanitized to
		/// be safe across both POSIX and Windows filesystems (IPv6 colons become dashes).
		/// </summary>
		public static string BuildSessionFileName(IPAddress target, DateTime startUtc)
		{
			string addr = target == null ? "unknown" : SanitizeForFilename(target.ToString());
			return $"session-{addr}-{startUtc:yyyyMMdd-HHmmssfff}{FileExtension}";
		}

		private static readonly Regex _unsafeChars = new Regex(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);
		private static string SanitizeForFilename(string s) => _unsafeChars.Replace(s, "-");

		/// <summary>
		/// Resolves the directory where session files live. Falls back to
		/// <c>{Settings.SettingsFolderPath}/data</c> when the explicit setting is null/empty.
		/// </summary>
		public static string ResolveDataDirectory(string explicitDir, string settingsFolder)
		{
			if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;
			return Path.Combine(settingsFolder ?? string.Empty, "data");
		}
	}
}
