using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PingTracer.Services
{
	/// <summary>
	/// Helps throttle pings to hops that have not responded in a while.
	/// Ported from PingTracer2.TraceThrottler.
	/// </summary>
	public class TraceThrottler
	{
		private class PingHistory
		{
			public readonly byte hopNumber;
			public long lastPing;
			public long lastReply;

			public PingHistory(byte hopNumber)
			{
				this.hopNumber = hopNumber;
			}
			public PingHistory(byte hopNumber, long lastPing, long lastReply) : this(hopNumber)
			{
				this.lastPing = lastPing;
				this.lastReply = lastReply;
			}
		}

		private static readonly Stopwatch timeSource = Stopwatch.StartNew();
		private readonly PingHistory[] history = new PingHistory[256];
		private readonly long howOldIsTooOldMs;

		/// <summary>
		/// Constructs a throttler.
		/// </summary>
		/// <param name="unresponsiveThresholdMs">A hop is considered unresponsive if more than this many milliseconds have elapsed since the last reply from it.</param>
		public TraceThrottler(long unresponsiveThresholdMs = 10000)
		{
			howOldIsTooOldMs = unresponsiveThresholdMs;
		}

		/// <summary>Call when you send a ping to a hop.</summary>
		/// <param name="hopNumber">Hop number from 1 to 255.</param>
		public void SentPingToHop(byte hopNumber)
		{
			long time = timeSource.ElapsedMilliseconds;
			PingHistory p = history[hopNumber];
			if (p == null)
				history[hopNumber] = new PingHistory(hopNumber, time, long.MinValue);
			else
				p.lastPing = time;
		}

		/// <summary>Call when you get a response from a hop.</summary>
		/// <param name="hopNumber">Hop number from 1 to 255.</param>
		public void GotResponseFromHop(byte hopNumber)
		{
			long time = timeSource.ElapsedMilliseconds;
			PingHistory p = history[hopNumber];
			if (p == null)
				history[hopNumber] = new PingHistory(hopNumber, time, time);
			else
				p.lastReply = time;
		}

		/// <summary>Call to forget a hop, making it as if that hop had never been pinged.</summary>
		public void ForgetHop(byte hopNumber)
		{
			history[hopNumber] = null;
		}

		/// <summary>
		/// Returns the set of hop numbers that should not be pinged right now.
		/// The caller can simply skip pinging any hop number in the returned set.
		/// </summary>
		/// <param name="hopNumberLimit">Hop numbers above this will not be considered.</param>
		/// <param name="pingCount">Maximum amount of unresponsive hops to allow to be pinged anyway (the oldest-pinged ones get to retry first).</param>
		public HashSet<byte> GetHopNumbersThatShouldNotBePingedRightNow(byte hopNumberLimit, byte pingCount)
		{
			long ageCutoff = timeSource.ElapsedMilliseconds - howOldIsTooOldMs;
			return history
				.Where(p => p != null && p.hopNumber <= hopNumberLimit && p.lastReply < ageCutoff)
				.OrderBy(p => p.lastPing)
				.Skip(pingCount)
				.Select(p => p.hopNumber)
				.ToHashSet();
		}
	}
}
