using PingTracer.Services;
using PingTracer.Services.Pinging;
using PingTracer.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PingTracer.Tests
{
	public class RouteMonitorTests
	{
		private static readonly IPAddress Target = IPAddress.Parse("198.51.100.10");
		private static IPAddress Router(int ttl) => IPAddress.Parse("203.0.113." + ttl);

		/// <summary>A pinger whose answers come from a function, completing either immediately or when released by the test.</summary>
		private sealed class FakePinger : IPinger
		{
			public Func<int, PingOutcome> Respond;
			public readonly List<int> Sent = new List<int>();
			public bool Manual;
			public readonly Dictionary<int, TaskCompletionSource<PingOutcome>> Waiting = new Dictionary<int, TaskCompletionSource<PingOutcome>>();

			public Task<PingOutcome> PingAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken ct)
			{
				lock (Sent) Sent.Add(ttl);
				if (!Manual)
					return Task.FromResult(Respond(ttl));
				TaskCompletionSource<PingOutcome> tcs = new TaskCompletionSource<PingOutcome>();
				Waiting[ttl] = tcs;
				return tcs.Task;
			}

			public int Completed;
			public MonitoringSession Session;

			/// <summary>Completes the ping for a TTL and waits until the monitor has processed it.</summary>
			public void Release(int ttl)
			{
				int before = Volatile.Read(ref Completed);
				Waiting[ttl].SetResult(Respond(ttl));
				// Pings voided while pending raise no event, so this wait is bounded rather than asserted.
				SpinWait.SpinUntil(() => Volatile.Read(ref Completed) > before, 300);
				// The counter is incremented while the monitor holds the session lock; taking it waits for processing to finish.
				lock (Session.DataLock) { }
			}

			public Task<string> ReverseLookupAsync(IPAddress address) => Task.FromResult<string>(null);
		}

		/// <summary>Simulates a path where routers 1..length-1 answer TTL-expired and the target answers at TTL >= length.</summary>
		private static Func<int, PingOutcome> Path(Func<int> length, Func<int, bool> silent = null, bool destinationDown = false)
		{
			return ttl =>
			{
				int len = length();
				if (ttl >= len)
					return destinationDown ? PingOutcome.Lost : new PingOutcome(PingOutcomeKind.EchoReply, Target, 20);
				if (silent != null && silent(ttl))
					return PingOutcome.Lost;
				return new PingOutcome(PingOutcomeKind.TtlExpired, Router(ttl), ttl);
			};
		}

		private static (RouteMonitor monitor, MonitoringSession session, FakePinger pinger) Create(Func<int, PingOutcome> respond, int maxHops = 10, bool trace = true)
		{
			MonitoringSession s = new MonitoringSession(Target, "test", trace ? SessionMode.TraceRoute : SessionMode.Direct, 1000, 1000);
			FakePinger p = new FakePinger { Respond = respond, Session = s };
			s.SampleCompleted += (_, _, _) => Interlocked.Increment(ref p.Completed);
			s.SampleVoided += (_, _, r) => { };
			RouteMonitor m = new RouteMonitor(s, p, new RouteMonitorOptions { TraceRoute = trace, MaxHops = maxHops, TimeoutMs = 1000, IntervalMs = 1000 });
			return (m, s, p);
		}

		private static (int n, int succ) Totals(MonitoringSession s, int ttl)
		{
			const int msPerCol = 1_000_000_000;
			var q = s.Query(s.StartUnixMs / msPerCol, msPerCol, 2).FirstOrDefault(r => r.ttl == ttl);
			if (q.stats == null) return (0, 0);
			int n = q.stats.N[0] + q.stats.N[1];
			return (n, n - q.stats.Lost[0] - q.stats.Lost[1]);
		}

		[Fact]
		public void DiscoversDestinationAndVoidsTtlsBeyondIt()
		{
			var (m, s, p) = Create(Path(() => 5));
			m.RunCycle();
			Assert.Equal(Enumerable.Range(1, 10), p.Sent.OrderBy(x => x));
			Assert.Equal(5, s.DestinationTtl);
			for (int ttl = 1; ttl <= 5; ttl++)
				Assert.Equal((1, 1), Totals(s, ttl));
			for (int ttl = 6; ttl <= 10; ttl++)
				Assert.Equal((0, 0), Totals(s, ttl));

			p.Sent.Clear();
			m.RunCycle();
			Assert.Equal(Enumerable.Range(1, 5), p.Sent.OrderBy(x => x));
			Assert.Equal((2, 2), Totals(s, 5));
			Assert.Equal(new[] { 1, 2, 3, 4, 5 }, s.GetRepliedTtls());
		}

		[Fact]
		public void EchoArrivingOutOfOrderIsVoidedRetroactively()
		{
			var (m, s, p) = Create(Path(() => 5), maxHops: 8);
			p.Manual = true;
			List<int> voided = new List<int>();
			s.SampleVoided += (sess, ttl, r) => voided.Add(ttl);
			RouteMonitor.Cycle cycle = m.RunCycle();
			p.Release(7); // the destination's reply to TTL 7 arrives first
			p.Release(6);
			Assert.Equal(6, s.DestinationTtl);
			p.Release(5); // now the real destination TTL
			Assert.Equal(5, s.DestinationTtl);
			Assert.Equal(new[] { 6, 7 }, voided.OrderBy(x => x));
			Assert.DoesNotContain(6, s.GetRepliedTtls());
			Assert.DoesNotContain(7, s.GetRepliedTtls());
			Assert.All(s.GetRouteEvents().Where(e => e.Kind == RouteEventKind.DestinationTtl), e => Assert.True(e.Initial));
			foreach (int ttl in new[] { 1, 2, 3, 4, 8 })
				p.Release(ttl);
			Assert.True(cycle.Completion.IsCompleted);
			Assert.Equal((0, 0), Totals(s, 7));
			Assert.Equal((0, 0), Totals(s, 8));
			Assert.Equal((1, 1), Totals(s, 5));
		}

		[Fact]
		public void RouteShorteningIsDetectedImmediately()
		{
			int length = 6;
			var (m, s, p) = Create(Path(() => length));
			m.RunCycle();
			Assert.Equal(6, s.DestinationTtl);
			length = 4;
			m.RunCycle();
			Assert.Equal(4, s.DestinationTtl);
			// Second cycle's echoes at TTL 5 and 6 are beyond the new destination.
			Assert.Equal((1, 1), Totals(s, 5)); // only the first cycle's TTL-expired reply
			Assert.Equal((1, 1), Totals(s, 6));
			Assert.Contains(s.GetRouteEvents(), e => e.Kind == RouteEventKind.DestinationTtl && e.Ttl == 4 && e.Value == 6);
		}

		[Fact]
		public void RouteLengtheningIsDetectedWhenRouterAnswersAtDestinationTtl()
		{
			int length = 5;
			var (m, s, p) = Create(Path(() => length));
			m.RunCycle();
			Assert.Equal(5, s.DestinationTtl);
			length = 7;
			m.RunCycle(); // TTL 5 now answers TTL-expired: the route is longer.
			Assert.Equal(5, s.DestinationTtl);
			p.Sent.Clear();
			m.RunCycle(); // extension scan finds the destination at 7
			Assert.Contains(7, p.Sent);
			Assert.Equal(7, s.DestinationTtl);
			Assert.True(Totals(s, 7).succ >= 1);
		}

		[Fact]
		public void LostDestinationKeepsDestinationTtlAndVoidsExtensionProbes()
		{
			bool down = false;
			var (m, s, p) = Create(ttl => Path(() => 5, destinationDown: down)(ttl), maxHops: 20);
			m.RunCycle();
			Assert.Equal(5, s.DestinationTtl);
			down = true;
			bool extended = false;
			for (int i = 0; i < 30; i++)
			{
				p.Sent.Clear();
				m.RunCycle();
				if (p.Sent.Any(t => t > 5)) extended = true;
			}
			Assert.True(extended, "Expected periodic probing beyond the destination while it is not answering");
			Assert.Equal(5, s.DestinationTtl);
			for (int ttl = 6; ttl <= 20; ttl++)
				Assert.Equal((0, 0), Totals(s, ttl));
			var dest = Totals(s, 5);
			Assert.Equal(31, dest.n);
			Assert.Equal(1, dest.succ);
		}

		[Fact]
		public void SilentHopsAreThrottledButDestinationIsNot()
		{
			var (m, s, p) = Create(Path(() => 6, silent: ttl => ttl == 3));
			for (int i = 0; i < 40; i++)
				m.RunCycle();
			int sentToSilent = p.Sent.Count(t => t == 3);
			int sentToDest = p.Sent.Count(t => t == 6);
			Assert.Equal(40, sentToDest);
			// Never-answering hops are not throttled until enough pings go unanswered, then only every few cycles.
			Assert.InRange(sentToSilent, 10, 20);
		}

		[Fact]
		public void DirectModeRecordsAtTtlZero()
		{
			var (m, s, p) = Create(ttl => ttl >= 64 ? new PingOutcome(PingOutcomeKind.EchoReply, Target, 12) : PingOutcome.Lost, trace: false);
			m.RunCycle();
			m.RunCycle();
			Assert.All(p.Sent, ttl => Assert.Equal(128, ttl));
			Assert.Equal((2, 2), Totals(s, 0));
			Assert.Equal(0, s.DestinationTtl);
		}

		[Fact]
		public void NewAddressAtHopProducesRouteEvent()
		{
			IPAddress hop2 = Router(2);
			var (m, s, p) = Create(ttl =>
			{
				if (ttl >= 3) return new PingOutcome(PingOutcomeKind.EchoReply, Target, 5);
				if (ttl == 2) return new PingOutcome(PingOutcomeKind.TtlExpired, hop2, 2);
				return new PingOutcome(PingOutcomeKind.TtlExpired, Router(1), 1);
			});
			m.RunCycle();
			hop2 = IPAddress.Parse("192.0.2.99");
			m.RunCycle();
			List<RouteEvent> events = s.GetRouteEvents().Where(e => e.Kind == RouteEventKind.HopAddress && e.Ttl == 2).ToList();
			Assert.Equal(2, events.Count);
			// Both happened within the learning period, so both count as discovery.
			Assert.All(events, e => Assert.True(e.Initial));
			Assert.Equal(hop2, s.Addresses.GetAddress(events[1].Value));
		}
	}
}
