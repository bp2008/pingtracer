using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PingTracer.Util
{
	/// <summary>
	/// Time sources for stamping individual pings more finely than <see cref="DateTime.Now"/>, which on .NET Framework only advances once per system timer tick (1 to 15.6 ms).
	/// </summary>
	public static class PreciseClock
	{
		[DllImport("kernel32.dll")]
		private static extern void GetSystemTimePreciseAsFileTime(out long fileTime);

		private static bool preciseAvailable = true;

		/// <summary>
		/// The current local wall-clock time at the highest resolution Windows offers (under a microsecond on Windows 8 and later).  Falls back to <see cref="DateTime.Now"/> on older systems.
		/// </summary>
		public static DateTime Now
		{
			get
			{
				if (preciseAvailable)
				{
					try
					{
						long fileTime;
						GetSystemTimePreciseAsFileTime(out fileTime);
						return DateTime.FromFileTime(fileTime);
					}
					catch (EntryPointNotFoundException)
					{
						preciseAvailable = false;
					}
				}
				return DateTime.Now;
			}
		}

		/// <summary>
		/// A monotonic timestamp for measuring intervals.  It is unaffected by adjustments to the system clock.  Compare two with <see cref="MicrosecondsBetween"/>.
		/// </summary>
		public static long Timestamp
		{
			get { return Stopwatch.GetTimestamp(); }
		}

		/// <summary>
		/// Returns the whole microseconds between two values of <see cref="Timestamp"/>, limited to the range of an int (about 35 minutes).
		/// </summary>
		public static int MicrosecondsBetween(long earlier, long later)
		{
			double microseconds = (later - earlier) * (1000000.0 / Stopwatch.Frequency);
			if (microseconds <= 0)
				return 0;
			return microseconds >= int.MaxValue ? int.MaxValue : (int)microseconds;
		}
	}
}
