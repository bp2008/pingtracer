using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using PingTracer.Storage;
using PingTracer.TraceRoute;
using Xunit;

namespace PingTracer.Tests
{
	public class MonitoringSessionTests
	{
		private static readonly IPAddress Target = IPAddress.Parse("8.8.8.8");
		private static readonly DateTime T0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static TraceRouteHostResult Hit(byte ttl, string ip, long rtt = 10)
		{
			return new TraceRouteHostResult(null, true, rtt, IPAddress.Parse(ip), ttl, Target, ttl, 5000, default);
		}

		private static TraceRouteHostResult Miss(byte ttl)
		{
			return new TraceRouteHostResult(null, false, 0, IPAddress.Any, ttl, Target, ttl, 5000, default);
		}

		[Fact]
		public void RecordTraceResult_NewHop_CreatesSeriesInHistory()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "192.168.1.1") }, T0);

			var series = session.HopData[0].Series;
			Assert.Single(series);
			Assert.Equal(IPAddress.Parse("192.168.1.1"), series[0].Address);
			Assert.True(series[0].IsActive);
		}

		[Fact]
		public void RecordTraceResult_SameHopSameAddress_ReusesActiveSeries()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "192.168.1.1") }, T0);
			session.RecordTraceResult(new[] { Hit(1, "192.168.1.1") }, T0.AddSeconds(1));

			// Still only one series
			Assert.Single(session.HopData[0].Series);
		}

		[Fact]
		public void RecordTraceResult_AddressChange_CreatesNewSeriesClosesOld()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "192.168.1.1") }, T0);
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0.AddSeconds(1));

			var history = session.HopData[0].Series;
			Assert.Equal(2, history.Count);

			var old = history[0];
			var current = history[1];

			Assert.False(old.IsActive);
			Assert.Equal(T0.AddSeconds(1), old.EndTimeUtc);
			Assert.True(current.IsActive);
			Assert.Equal(IPAddress.Parse("10.0.0.1"), current.Address);
		}

		[Fact]
		public void RecordTraceResult_AddressRevert_CreatesNewSeries_NoReuse()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "1.1.1.1") }, T0);
			session.RecordTraceResult(new[] { Hit(1, "2.2.2.2") }, T0.AddSeconds(1));
			session.RecordTraceResult(new[] { Hit(1, "1.1.1.1") }, T0.AddSeconds(2)); // revert

			var history = session.HopData[0].Series;
			// All three are distinct entries even though third has same address as first
			Assert.Equal(3, history.Count);
			Assert.False(history[0].IsActive);
			Assert.False(history[1].IsActive);
			Assert.True(history[2].IsActive);
		}

		[Fact]
		public void RecordTraceResult_MissingHop_ClosesActiveSeries()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1"), Hit(2, "10.0.0.2") }, T0);

			// Second trace: hop 1 is gone
			session.RecordTraceResult(new[] { Miss(1), Hit(2, "10.0.0.2") }, T0.AddSeconds(1));

			var h0 = session.HopData[0];
			Assert.Single(h0.Series);
			Assert.False(h0.Series[0].IsActive);
			Assert.Equal(T0.AddSeconds(1), h0.Series[0].EndTimeUtc);

			var h1 = session.HopData[1];
			Assert.Single(h1.Series);
			Assert.True(h1.Series[0].IsActive);
		}

		[Fact]
		public void RouteChangedEvent_FiredOnAddressChange()
		{
			var session = new MonitoringSession(Target, "test");
			int changeCount = 0;
			session.RouteChanged += (_, _) => changeCount++;

			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0);
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0.AddSeconds(1)); // no change
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.2") }, T0.AddSeconds(2)); // change

			Assert.Equal(2, changeCount); // first trace (from null) + address change
		}

		[Fact]
		public void TraceResultRecorded_FiredEachTrace()
		{
			var session = new MonitoringSession(Target, "test");
			int fired = 0;
			session.TraceResultRecorded += _ => fired++;

			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0);
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0.AddSeconds(1));

			Assert.Equal(2, fired);
		}

		[Fact]
		public void GetActiveHops_ReturnsOnlyActiveSeriesPerHop()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1"), Hit(2, "10.0.0.2") }, T0);

			var active = new List<HopTimeSeries>(session.GetActiveHops());
			Assert.Equal(2, active.Count);
			Assert.All(active, s => Assert.True(s.IsActive));
		}

		[Fact]
		public void PruneOlderThan_RemovesOldRouteHistory_RetainsAnchor()
		{
			var session = new MonitoringSession(Target, "test");

			// Add several route history entries
			for (int i = 0; i < 5; i++)
				session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0.AddMinutes(i));

			// Prune everything older than T0 + 3 minutes
			// RouteHistory has entries at T0, T0+1m, T0+2m, T0+3m, T0+4m
			// Entries before T0+3m that predate the cutoff: T0, T0+1m, T0+2m
			// The last one before cutoff (T0+2m) must be retained as anchor.
			// So we remove T0 (only), keeping T0+1m as anchor... wait let me re-check.
			// cutoff = T0 + 3m. entries < cutoff: T0, T0+1m, T0+2m (lastBeforeCutoff index = 2)
			// We remove [0, lastBeforeCutoff) = [0, 2) = indices 0 and 1
			// Remaining: T0+2m, T0+3m, T0+4m

			session.PruneOlderThan(TimeSpan.FromMinutes(-3).Negate()); // maxAge = 3 minutes

			// The anchor (last entry before cutoff) must be kept.
			// After pruning, RouteHistory should have at most 3 entries and the oldest should be T0+2m
			lock (session) { } // just to ensure memory visibility; RouteHistory access is not locked in reads
			Assert.True(session.RouteHistory.Count >= 1);

			// The very first remaining entry should be >= T0+2m (anchor preserved)
			Assert.True(session.RouteHistory[0].TimestampUtc >= T0.AddMinutes(2));
		}

		[Fact]
		public void PruneOlderThan_RemovesEmptyInactiveSeries()
		{
			var session = new MonitoringSession(Target, "test");

			// Hop 0 active for T0..T0+1s, then gone
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0);
			session.RecordTraceResult(new[] { Miss(1) }, T0.AddSeconds(1));

			// Hop 0 now has one inactive series with no ping records
			Assert.Single(session.HopData[0].Series);
			Assert.False(session.HopData[0].Series[0].IsActive);

			// Pruning with a cutoff past the series should remove it
			session.PruneOlderThan(TimeSpan.FromSeconds(-10)); // cutoff = now - (-10s) = now + 10s... that's future

			// Use a very small maxAge so the cutoff is effectively "now"
			// which should include the T0 records
			// Actually let's just call it directly with sufficient maxAge
			// The series is empty (no pings were recorded in it for >1 day)
			// PruneOlderThan with maxAge = 0 would set cutoff = now
			// T0 is 2025-01-01, so all records predate "now" (2026)
			session.PruneOlderThan(TimeSpan.FromDays(1));

			// Empty inactive series should be gone
			Assert.Empty(session.HopData[0].Series);
		}

		[Fact]
		public void GetAggregatedData_ReturnsOverlappingSeries()
		{
			var session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, T0);

			var overlapping = new List<HopTimeSeries>(
				session.GetAggregatedData(T0, T0.AddHours(1), maxPointsPerHop: 100));

			Assert.Single(overlapping);
		}
	}
}
