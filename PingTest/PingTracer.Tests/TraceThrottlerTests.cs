using System.Threading;
using PingTracer.Services;
using Xunit;

namespace PingTracer.Tests
{
	public class TraceThrottlerTests
	{
		[Fact]
		public void NeverPinged_NotInSkipSet()
		{
			var throttler = new TraceThrottler();
			var skip = throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 1);
			Assert.Empty(skip);
		}

		[Fact]
		public void RecentlyPingedAndReplied_NotInSkipSet()
		{
			var throttler = new TraceThrottler();
			throttler.SentPingToHop(5);
			throttler.GotResponseFromHop(5);
			var skip = throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 0);
			Assert.DoesNotContain((byte)5, skip);
		}

		[Fact]
		public void Unresponsive_AfterThreshold_AppearsInSkipSet()
		{
			// Use a very short unresponsive threshold to keep test fast.
			var throttler = new TraceThrottler(unresponsiveThresholdMs: 50);
			throttler.SentPingToHop(7);
			Thread.Sleep(120);
			// pingCount=0 → no unresponsive hops are allowed through.
			var skip = throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 0);
			Assert.Contains((byte)7, skip);
		}

		[Fact]
		public void PingCountAllowance_LetsOldestUnresponsiveThrough()
		{
			var throttler = new TraceThrottler(unresponsiveThresholdMs: 50);
			throttler.SentPingToHop(3);
			Thread.Sleep(20);
			throttler.SentPingToHop(4);
			Thread.Sleep(20);
			throttler.SentPingToHop(5);
			Thread.Sleep(60); // all three are now unresponsive (no replies).

			var skip = throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 1);
			// Hop 3 was pinged longest ago → it gets the one allowance, 4 and 5 are skipped.
			Assert.DoesNotContain((byte)3, skip);
			Assert.Contains((byte)4, skip);
			Assert.Contains((byte)5, skip);
		}

		[Fact]
		public void ForgetHop_ClearsState()
		{
			var throttler = new TraceThrottler(unresponsiveThresholdMs: 50);
			throttler.SentPingToHop(9);
			Thread.Sleep(120);

			Assert.Contains((byte)9, throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 0));

			throttler.ForgetHop(9);
			Assert.DoesNotContain((byte)9, throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 0));
		}

		[Fact]
		public void HopNumberLimit_ExcludesAboveLimit()
		{
			var throttler = new TraceThrottler(unresponsiveThresholdMs: 50);
			throttler.SentPingToHop(50);
			Thread.Sleep(120);

			var skip = throttler.GetHopNumbersThatShouldNotBePingedRightNow(30, 0);
			Assert.DoesNotContain((byte)50, skip);
		}
	}
}
