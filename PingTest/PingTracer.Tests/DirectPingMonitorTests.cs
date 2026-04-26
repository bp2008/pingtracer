using System;
using System.Net;
using System.Threading.Tasks;
using PingTracer.Services;
using PingTracer.Storage;
using PingTracer.TraceRoute;
using Xunit;

namespace PingTracer.Tests
{
	public class DirectPingMonitorTests
	{
		private static readonly IPAddress Target = IPAddress.Parse("8.8.8.8");

		private sealed class FakeSender
		{
			public int Calls;
			public Func<int, (bool success, long rtt)> Reply = _ => (true, 12L);

			public Task<TraceRouteHostResult> Send(IPAddress target, int pingTimeoutMs, DateTime sentAtUtc)
			{
				int n = ++Calls;
				var (success, rtt) = Reply(n);
				IPAddress replyFrom = success ? target : IPAddress.Any;
				return Task.FromResult(new TraceRouteHostResult(this, success, rtt, replyFrom, 1, target, 1, pingTimeoutMs, sentAtUtc));
			}
		}

		private static DirectPingMonitor CreateMonitor(MonitoringSession session, FakeSender sender)
			=> new DirectPingMonitor(Target, session, intervalMs: 1000, pingTimeoutMs: 5000, payloadSizeBytes: 32, sendFn: sender.Send);

		[Fact]
		public async Task FirstCycle_CreatesActiveSeriesAndCompletesRecord()
		{
			var session = new MonitoringSession(Target, "test");
			var sender = new FakeSender { Reply = _ => (true, 17L) };
			var monitor = CreateMonitor(session, sender);

			await monitor.RunOneCycleAsync();

			HopTimeSeries series;
			lock (session.HopData[0])
				series = session.HopData[0].ActiveSeries;

			Assert.NotNull(series);
			Assert.Equal(Target, series.Address);
			Assert.Equal(1, series.RecordCount);

			// Verify the record was completed (not still pending)
			var raw = System.Linq.Enumerable.ToArray(series.QueryRange(DateTime.MinValue, DateTime.MaxValue));
			Assert.Single(raw);
			Assert.Equal((ushort)17, raw[0].rtt);
		}

		[Fact]
		public async Task ManyCycles_AppendToSameActiveSeries()
		{
			var session = new MonitoringSession(Target, "test");
			var sender = new FakeSender { Reply = n => (true, 10L + n) };
			var monitor = CreateMonitor(session, sender);

			for (int i = 0; i < 5; i++)
				await monitor.RunOneCycleAsync();

			HopTimeSeries series;
			lock (session.HopData[0])
				series = session.HopData[0].ActiveSeries;

			Assert.NotNull(series);
			Assert.Equal(5, series.RecordCount);
			Assert.Single(session.HopData[0].Series);
		}

		[Fact]
		public async Task TimeoutResult_RecordedAsTimeoutSentinel()
		{
			var session = new MonitoringSession(Target, "test");
			var sender = new FakeSender { Reply = _ => (false, 0L) };
			var monitor = CreateMonitor(session, sender);

			await monitor.RunOneCycleAsync();

			HopTimeSeries series;
			lock (session.HopData[0])
				series = session.HopData[0].ActiveSeries;

			// On timeout the seed RecordTraceResult still creates a series (we always seed
			// once); the per-tick record gets the Timeout sentinel.
			Assert.NotNull(series);
			var raw = System.Linq.Enumerable.ToArray(series.QueryRange(DateTime.MinValue, DateTime.MaxValue));
			Assert.Single(raw);
			Assert.Equal(PingRecordStatus.Timeout, raw[0].rtt);
		}

		[Fact]
		public async Task PingRecordCompleted_FiresWithSeriesAndHandle()
		{
			var session = new MonitoringSession(Target, "test");
			var sender = new FakeSender { Reply = _ => (true, 5L) };
			var monitor = CreateMonitor(session, sender);

			TraceRouteHostResult observedResult = null;
			HopTimeSeries observedSeries = null;
			RecordHandle? observedHandle = null;
			monitor.PingRecordCompleted += (r, s, h) => { observedResult = r; observedSeries = s; observedHandle = h; };

			await monitor.RunOneCycleAsync();

			Assert.NotNull(observedResult);
			Assert.True(observedResult.success);
			Assert.NotNull(observedSeries);
			Assert.Equal(Target, observedSeries.Address);
			Assert.NotNull(observedHandle);
		}

		[Fact]
		public void IntervalMs_SetterUpdatesField()
		{
			var session = new MonitoringSession(Target, "test");
			var sender = new FakeSender();
			var monitor = CreateMonitor(session, sender);

			Assert.Equal(1000, monitor.IntervalMs);
			monitor.IntervalMs = 250;
			Assert.Equal(250, monitor.IntervalMs);
		}

		[Fact]
		public void Dispose_StopsTimer()
		{
			var session = new MonitoringSession(Target, "test");
			var sender = new FakeSender();
			var monitor = CreateMonitor(session, sender);
			monitor.Start();
			Assert.True(monitor.IsRunning);
			monitor.Dispose();
			Assert.False(monitor.IsRunning);
		}
	}
}
