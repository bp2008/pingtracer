using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PingTracer.Services;
using PingTracer.Storage;
using PingTracer.TraceRoute;
using Xunit;

namespace PingTracer.Tests
{
	public class ContinuousRouteMonitorTests
	{
		private static readonly IPAddress Target = IPAddress.Parse("8.8.8.8");

		private sealed class FakeTraceRouter
		{
			public Func<byte, (bool success, IPAddress replyFrom, long rtt)?> ReplyForTtl;
			public int InvocationCount;
			public List<HashSet<byte>> SkipSetsObserved = new List<HashSet<byte>>();

			public Task TraceRoute(
				object token, IPAddress target, byte maxHops,
				HashSet<byte> skipHops,
				Action<byte, DateTime> onHopDispatching,
				Action<TraceRouteHostResult> onHostResult,
				int pingTimeoutMs)
			{
				InvocationCount++;
				SkipSetsObserved.Add(skipHops != null ? new HashSet<byte>(skipHops) : new HashSet<byte>());

				for (byte ttl = 1; ttl <= maxHops; ttl++)
				{
					if (skipHops != null && skipHops.Contains(ttl)) continue;
					DateTime sentAt = DateTime.UtcNow;
					onHopDispatching?.Invoke(ttl, sentAt);

					var reply = ReplyForTtl?.Invoke(ttl);
					if (reply.HasValue)
					{
						var r = reply.Value;
						onHostResult(new TraceRouteHostResult(token, r.success, r.rtt, r.replyFrom ?? IPAddress.Any, ttl, target, maxHops, pingTimeoutMs, sentAt));
					}
					else
					{
						onHostResult(new TraceRouteHostResult(token, false, 0, IPAddress.Any, ttl, target, maxHops, pingTimeoutMs, sentAt));
					}
				}
				return Task.CompletedTask;
			}
		}

		private static Task<TraceRouteHostResult> NoopProbe(IPAddress target, byte ttl, int timeoutMs)
			=> Task.FromResult(new TraceRouteHostResult(null, false, 0, IPAddress.Any, ttl, target, ttl, timeoutMs, DateTime.UtcNow));

		private static ContinuousRouteMonitor CreateMonitor(
			MonitoringSession session,
			FakeTraceRouter fake,
			byte initialMaxHops = 10,
			int destinationProbeMissThreshold = 10,
			byte allowedUnresponsiveDispatches = 1,
			ContinuousRouteMonitor.SendProbeDelegate probeFn = null,
			TraceThrottler throttler = null)
		{
			return new ContinuousRouteMonitor(
				Target, session,
				intervalMs: 1000, initialMaxHops: initialMaxHops,
				pingTimeoutMs: 5000,
				destinationProbeMissThreshold: destinationProbeMissThreshold,
				destinationProbeTtl: 128,
				allowedUnresponsiveDispatches: allowedUnresponsiveDispatches,
				traceRouteFn: fake.TraceRoute,
				probeFn: probeFn ?? NoopProbe,
				throttler: throttler);
		}

		[Fact]
		public async Task FirstCycle_AllHopsRespond_RecordsInsertedOnNewSeries()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L * ttl)
			};
			var monitor = CreateMonitor(session, fake, initialMaxHops: 4);

			await monitor.RunOneCycleAsync();

			for (byte ttl = 1; ttl <= 4; ttl++)
			{
				HopTimeSeries series;
				lock (session.HopData[ttl - 1])
					series = session.HopData[ttl - 1].ActiveSeries;

				Assert.NotNull(series);
				Assert.Equal(IPAddress.Parse($"10.0.0.{ttl}"), series.Address);
				Assert.Equal(1, series.RecordCount);
			}
		}

		[Fact]
		public async Task SteadyState_ThreeCycles_OneRecordPerCyclePerHop()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L * ttl)
			};
			var monitor = CreateMonitor(session, fake, initialMaxHops: 3);

			await monitor.RunOneCycleAsync();
			await monitor.RunOneCycleAsync();
			await monitor.RunOneCycleAsync();

			for (byte ttl = 1; ttl <= 3; ttl++)
			{
				var series = session.HopData[ttl - 1].ActiveSeries;
				Assert.NotNull(series);
				Assert.Equal(3, series.RecordCount);
			}
		}

		[Fact]
		public async Task AddressChange_OldSeriesGetsTimeout_NewSeriesGetsRecord()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter();

			fake.ReplyForTtl = ttl => ttl == 1
				? (true, IPAddress.Parse("1.1.1.1"), 10L)
				: (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L * ttl);
			var monitor = CreateMonitor(session, fake, initialMaxHops: 3);
			await monitor.RunOneCycleAsync();

			// Cycle 2: hop 1 changes address to 2.2.2.2.
			fake.ReplyForTtl = ttl => ttl == 1
				? (true, IPAddress.Parse("2.2.2.2"), 12L)
				: (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L * ttl);
			await monitor.RunOneCycleAsync();

			HopHistory history = session.HopData[0];
			Assert.Equal(2, history.Series.Count);

			HopTimeSeries oldSeries = history.Series[0];
			HopTimeSeries newSeries = history.Series[1];

			Assert.Equal(IPAddress.Parse("1.1.1.1"), oldSeries.Address);
			Assert.Equal(IPAddress.Parse("2.2.2.2"), newSeries.Address);

			// Old series: cycle 1 succeeded (rtt=10), cycle 2 was a Timeout-completion due to address mismatch.
			Assert.Equal(2, oldSeries.RecordCount);

			// New series: 1 reconciled record from cycle 2.
			Assert.Equal(1, newSeries.RecordCount);

			// Verify the old series's second record is the Timeout sentinel.
			var oldRecords = oldSeries.QueryRange(DateTime.MinValue, DateTime.MaxValue).ToList();
			Assert.Equal(2, oldRecords.Count);
			Assert.Equal(10, oldRecords[0].rtt);
			Assert.Equal(PingRecordStatus.Timeout, oldRecords[1].rtt);
		}

		[Fact]
		public async Task AdaptiveHops_DestinationAtLowTtl_ShrinksMaxHops()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				// Destination responds at TTL 5; intermediate hops respond too.
				ReplyForTtl = ttl => ttl <= 5
					? (true, ttl == 5 ? Target : IPAddress.Parse($"10.0.0.{ttl}"), 5L * ttl)
					: ((bool, IPAddress, long)?)null
			};
			var monitor = CreateMonitor(session, fake, initialMaxHops: 30);

			await monitor.RunOneCycleAsync();

			Assert.Equal(5, monitor.MaxHops);
		}

		[Fact]
		public async Task AdaptiveHops_NoDestinationResponse_GrowsMaxHops()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				// No hop returns the target address.
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};
			var monitor = CreateMonitor(session, fake, initialMaxHops: 20);

			await monitor.RunOneCycleAsync();
			Assert.Equal(30, monitor.MaxHops);

			await monitor.RunOneCycleAsync();
			Assert.Equal(40, monitor.MaxHops);
		}

		[Fact]
		public async Task AdaptiveHops_GrowCapsAt255()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};
			var monitor = CreateMonitor(session, fake, initialMaxHops: 250);

			await monitor.RunOneCycleAsync();
			Assert.Equal(255, monitor.MaxHops);
		}

		[Fact]
		public async Task DestinationProbe_TriggersAfterMissThreshold()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};
			int probeCount = 0;
			Task<TraceRouteHostResult> ProbeFn(IPAddress target, byte ttl, int timeout)
			{
				probeCount++;
				return Task.FromResult(new TraceRouteHostResult(null, false, 0, IPAddress.Any, ttl, target, ttl, timeout, DateTime.UtcNow));
			}

			var monitor = CreateMonitor(session, fake, initialMaxHops: 10,
				destinationProbeMissThreshold: 3, probeFn: ProbeFn);

			await monitor.RunOneCycleAsync();
			Assert.Equal(0, probeCount);
			await monitor.RunOneCycleAsync();
			Assert.Equal(0, probeCount);
			await monitor.RunOneCycleAsync();
			Assert.Equal(1, probeCount);
			await monitor.RunOneCycleAsync();
			Assert.Equal(2, probeCount);
		}

		[Fact]
		public async Task DestinationProbe_SuccessfulProbe_ResetsMissCounter()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};
			Task<TraceRouteHostResult> ProbeFn(IPAddress target, byte ttl, int timeout)
				=> Task.FromResult(new TraceRouteHostResult(null, true, 50, target, ttl, target, ttl, timeout, DateTime.UtcNow));

			var monitor = CreateMonitor(session, fake, initialMaxHops: 10,
				destinationProbeMissThreshold: 2, probeFn: ProbeFn);

			await monitor.RunOneCycleAsync(); // miss=1
			await monitor.RunOneCycleAsync(); // miss=2 → probe runs, succeeds → reset
			Assert.Equal(0, monitor.ConsecutiveDestinationMisses);
		}

		[Fact]
		public async Task ThrottlerSkip_NoCallbackForSkippedHop_NoPendingRecord()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};

			// Pre-populate throttler so hop 2 is unresponsive.
			var throttler = new TraceThrottler(unresponsiveThresholdMs: 50);
			throttler.SentPingToHop(2);
			Thread.Sleep(120);

			var monitor = CreateMonitor(session, fake, initialMaxHops: 4, allowedUnresponsiveDispatches: 0, throttler: throttler);
			await monitor.RunOneCycleAsync();

			// Hop 2 was skipped: no series created.
			Assert.Empty(session.HopData[1].Series);
			// Hops 1, 3, 4 got their records.
			Assert.Equal(1, session.HopData[0].ActiveSeries.RecordCount);
			Assert.Equal(1, session.HopData[2].ActiveSeries.RecordCount);
			Assert.Equal(1, session.HopData[3].ActiveSeries.RecordCount);

			Assert.Single(fake.SkipSetsObserved);
			Assert.Contains((byte)2, fake.SkipSetsObserved[0]);
		}

		[Fact]
		public async Task PingRecordCompleted_FiresOncePerHop()
		{
			var session = new MonitoringSession(Target, "test");
			// Destination responds at TTL 5 so MaxHops stays at 5 across cycles.
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => ttl == 5
					? (true, Target, 25L)
					: (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};

			int eventCount = 0;
			var monitor = CreateMonitor(session, fake, initialMaxHops: 5);
			monitor.PingRecordCompleted += (r, s, h) => Interlocked.Increment(ref eventCount);

			await monitor.RunOneCycleAsync();
			Assert.Equal(5, eventCount);

			await monitor.RunOneCycleAsync();
			Assert.Equal(10, eventCount);
		}

		[Fact]
		public async Task Stop_PreventsFurtherCycles()
		{
			var session = new MonitoringSession(Target, "test");
			var fake = new FakeTraceRouter
			{
				ReplyForTtl = ttl => (true, IPAddress.Parse($"10.0.0.{ttl}"), 5L)
			};
			var monitor = CreateMonitor(session, fake, initialMaxHops: 3);

			Assert.False(monitor.IsRunning);
			monitor.Start();
			Assert.True(monitor.IsRunning);
			monitor.Stop();
			Assert.False(monitor.IsRunning);
			monitor.Dispose();
		}
	}
}
