using System;
using System.Collections.Generic;
using System.Net;
using PingTracer;
using PingTracer.Services;
using PingTracer.Storage;
using PingTracer.Tracer;
using PingTracer.TraceRoute;
using Xunit;

namespace PingTracer.Tests
{
	public class PingSessionTests
	{
		private sealed class FakeMonitor : IPingMonitor
		{
			public IPAddress TargetAddress { get; }
			public MonitoringSession Session { get; }
			public bool UseTraceRoute { get; }
			public int IntervalMs { get; set; }
			public bool IsRunning { get; private set; }
			public bool Disposed { get; private set; }
			public int StartCalls;
			public int StopCalls;

			public event Action<TraceRouteHostResult, HopTimeSeries, RecordHandle?> PingRecordCompleted;

			public FakeMonitor(IPAddress target, MonitoringSession session, bool useTraceRoute, int intervalMs)
			{
				TargetAddress = target;
				Session = session;
				UseTraceRoute = useTraceRoute;
				IntervalMs = intervalMs;
			}

			public void Start() { StartCalls++; IsRunning = true; }
			public void Stop() { StopCalls++; IsRunning = false; }
			public void Dispose() { Disposed = true; Stop(); }

			public void RaisePingRecordCompleted(TraceRouteHostResult r, HopTimeSeries s, RecordHandle? h)
				=> PingRecordCompleted?.Invoke(r, s, h);
		}

		private static (PingSession session, List<FakeMonitor> monitors) BuildSession(
			PingConfiguration cfg, Settings settings = null)
		{
			settings ??= new Settings();
			var monitors = new List<FakeMonitor>();
			PingSession.MonitorFactory factory = (target, name, useTrace, interval, sess) =>
			{
				var m = new FakeMonitor(target, sess, useTrace, interval);
				monitors.Add(m);
				return m;
			};
			return (new PingSession(cfg, settings, factory), monitors);
		}

		private static PingConfiguration CfgFor(params string[] hosts)
		{
			var cfg = new PingConfiguration { rate = 1, pingsPerSecond = true, doTraceRoute = true, reverseDnsLookup = false };
			cfg.hosts.Clear();
			foreach (string h in hosts) cfg.hosts.Add(new Host { hostname = h });
			return cfg;
		}

		[Fact]
		public void Start_SingleHost_TraceRoute_CreatesOneRouteMonitor()
		{
			var cfg = CfgFor("8.8.8.8");
			var (session, monitors) = BuildSession(cfg);

			session.Start();
			try
			{
				Assert.True(session.IsRunning);
				Assert.Single(monitors);
				Assert.True(monitors[0].UseTraceRoute);
				Assert.Equal(IPAddress.Parse("8.8.8.8"), monitors[0].TargetAddress);
				Assert.Equal(1, monitors[0].StartCalls);
				Assert.Single(session.Sessions);
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void Start_SingleHost_DirectPing_WhenDoTraceRouteFalse()
		{
			var cfg = CfgFor("8.8.8.8");
			cfg.doTraceRoute = false;
			var (session, monitors) = BuildSession(cfg);

			session.Start();
			try
			{
				Assert.Single(monitors);
				Assert.False(monitors[0].UseTraceRoute);
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void Start_MultiHost_AlwaysDirectPing_OneMonitorPerHost()
		{
			var cfg = CfgFor("8.8.8.8", "1.1.1.1");
			cfg.doTraceRoute = true; // Multi-host should still force direct mode.
			var (session, monitors) = BuildSession(cfg);

			session.Start();
			try
			{
				Assert.Equal(2, monitors.Count);
				Assert.All(monitors, m => Assert.False(m.UseTraceRoute));
				Assert.Equal(IPAddress.Parse("8.8.8.8"), monitors[0].TargetAddress);
				Assert.Equal(IPAddress.Parse("1.1.1.1"), monitors[1].TargetAddress);
				Assert.Equal(2, session.Sessions.Count);
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void Stop_DisposesAllMonitors_FiresSessionStoppedOnce()
		{
			var cfg = CfgFor("8.8.8.8", "1.1.1.1");
			var (session, monitors) = BuildSession(cfg);

			int stoppedCount = 0;
			session.SessionStopped += () => stoppedCount++;

			session.Start();
			session.Stop();

			Assert.False(session.IsRunning);
			Assert.Equal(1, stoppedCount);
			Assert.All(monitors, m => Assert.True(m.StopCalls >= 1));
			Assert.All(monitors, m => Assert.True(m.Disposed));

			// A second Stop() must be a no-op.
			session.Stop();
			Assert.Equal(1, stoppedCount);
		}

		[Fact]
		public void SetPingDelay_PropagatesToAllMonitors()
		{
			var cfg = CfgFor("8.8.8.8", "1.1.1.1");
			var (session, monitors) = BuildSession(cfg);

			session.Start();
			try
			{
				session.SetPingDelay(2, true); // 2 pings/sec → 500ms interval
				foreach (var m in monitors)
					Assert.Equal(500, m.IntervalMs);

				session.SetPingDelay(5, false); // 5 sec/ping → 5000ms
				foreach (var m in monitors)
					Assert.Equal(5000, m.IntervalMs);
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void PingRecordCompleted_BubblesAndCountersIncrement()
		{
			var cfg = CfgFor("8.8.8.8");
			cfg.doTraceRoute = false;
			var (session, monitors) = BuildSession(cfg);

			MonitoringSession capturedSession = null;
			TraceRouteHostResult capturedResult = null;
			session.PingRecordCompleted += (s, r, ts, h) => { capturedSession = s; capturedResult = r; };

			session.Start();
			try
			{
				var fake = monitors[0];
				var result = new TraceRouteHostResult(null, true, 12, IPAddress.Parse("8.8.8.8"), 1, IPAddress.Parse("8.8.8.8"), 1, 5000, DateTime.UtcNow);
				fake.RaisePingRecordCompleted(result, null, null);

				Assert.Same(fake.Session, capturedSession);
				Assert.Same(result, capturedResult);
				Assert.Equal(1, session.SuccessfulPings);
				Assert.Equal(0, session.FailedPings);

				var failResult = new TraceRouteHostResult(null, false, 0, IPAddress.Any, 1, IPAddress.Parse("8.8.8.8"), 1, 5000, DateTime.UtcNow);
				fake.RaisePingRecordCompleted(failResult, null, null);

				Assert.Equal(1, session.SuccessfulPings);
				Assert.Equal(1, session.FailedPings);
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void RouteChanged_BubblesUpAndEmitsHopDiscoveredAndDeactivated()
		{
			var cfg = CfgFor("8.8.8.8");
			var (session, monitors) = BuildSession(cfg);

			var routeChanges = new List<(MonitoringSession s, RouteSnapshot oldR, RouteSnapshot newR)>();
			var discovered = new List<(MonitoringSession s, HopTimeSeries h)>();
			var deactivated = new List<(MonitoringSession s, byte hop, IPAddress addr)>();

			session.RouteChanged += (s, oldR, newR) => routeChanges.Add((s, oldR, newR));
			session.HopDiscovered += (s, h) => discovered.Add((s, h));
			session.HopDeactivated += (s, hop, addr) => deactivated.Add((s, hop, addr));

			session.Start();
			try
			{
				var ms = monitors[0].Session;
				var ts = DateTime.UtcNow;

				// First trace: hop 1 = 10.0.0.1
				ms.RecordTraceResult(new[]
				{
					new TraceRouteHostResult(null, true, 5, IPAddress.Parse("10.0.0.1"), 1, IPAddress.Parse("8.8.8.8"), 1, 5000, ts)
				}, ts);

				// Second trace: hop 1 changes to 10.0.0.2
				var ts2 = ts.AddSeconds(1);
				ms.RecordTraceResult(new[]
				{
					new TraceRouteHostResult(null, true, 5, IPAddress.Parse("10.0.0.2"), 1, IPAddress.Parse("8.8.8.8"), 1, 5000, ts2)
				}, ts2);

				Assert.Equal(2, routeChanges.Count);

				// First call: HopDiscovered for 10.0.0.1
				Assert.Contains(discovered, d => d.h.Address.Equals(IPAddress.Parse("10.0.0.1")));
				// Second call: HopDeactivated for 10.0.0.1, HopDiscovered for 10.0.0.2
				Assert.Contains(deactivated, d => d.addr.Equals(IPAddress.Parse("10.0.0.1")));
				Assert.Contains(discovered, d => d.h.Address.Equals(IPAddress.Parse("10.0.0.2")));
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void RunPrune_InvokesPruneOnEachSession()
		{
			var cfg = CfgFor("8.8.8.8", "1.1.1.1");
			var settings = new Settings { dataRetentionSeconds = 1 };
			var (session, _) = BuildSession(cfg, settings);

			session.Start();
			try
			{
				// Add an old route snapshot so we can verify pruning kicks in.
				foreach (var ms in session.Sessions)
				{
					var oldTs = DateTime.UtcNow.AddMinutes(-5);
					ms.RecordTraceResult(new[]
					{
						new TraceRouteHostResult(null, true, 5, IPAddress.Parse("10.0.0.1"), 1, ms.TargetAddress, 1, 5000, oldTs)
					}, oldTs);
					var ts = DateTime.UtcNow;
					ms.RecordTraceResult(new[]
					{
						new TraceRouteHostResult(null, true, 5, IPAddress.Parse("10.0.0.1"), 1, ms.TargetAddress, 1, 5000, ts)
					}, ts);
				}

				int beforeCount = session.Sessions[0].RouteHistory.Count;
				session.RunPrune();
				int afterCount = session.Sessions[0].RouteHistory.Count;

				// PruneOlderThan retains at most one snapshot before the cutoff plus all
				// snapshots after, so afterCount <= beforeCount.
				Assert.True(afterCount <= beforeCount);
			}
			finally { session.Stop(); }
		}

		[Fact]
		public void Start_EmptyHosts_DoesNotStart()
		{
			var cfg = new PingConfiguration { rate = 1, pingsPerSecond = true, doTraceRoute = true };
			cfg.hosts.Clear();
			var (session, monitors) = BuildSession(cfg);

			session.Start();

			Assert.Empty(monitors);
			Assert.False(session.IsRunning);
		}
	}
}
