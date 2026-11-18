using System;
using System.Diagnostics;

namespace PingTracer.Storage
{
	/// <summary>
	/// Process-wide monotonic clock expressed as Unix epoch milliseconds.
	/// <para>
	/// The clock is anchored to the wall clock once at startup and then advanced by a
	/// <see cref="Stopwatch"/>, so it never jumps backwards when the system clock is
	/// adjusted.  Every timestamp recorded by the monitoring engine and every "now" value
	/// sent to web clients comes from this clock, which keeps recorded data and the live
	/// edge of the graph consistent with each other.
	/// </para>
	/// </summary>
	public static class ServerClock
	{
		private static readonly long anchorUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		private static readonly Stopwatch stopwatch = Stopwatch.StartNew();

		/// <summary>Current time in Unix epoch milliseconds (monotonic).</summary>
		public static long NowUnixMs => anchorUnixMs + stopwatch.ElapsedMilliseconds;

		/// <summary>Converts Unix epoch milliseconds to a UTC <see cref="DateTime"/>.</summary>
		public static DateTime ToDateTime(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
	}
}
