using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using PingTracer.Storage;
using Xunit;

namespace PingTracer.Tests
{
	public class HopTimeSeriesTests
	{
		private static readonly IPAddress TestAddr = IPAddress.Parse("10.0.0.1");
		private static readonly DateTime T0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		[Fact]
		public void BeginPendingRecord_ReturnsPendingRecord()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			var handle = series.BeginPendingRecord(T0.AddMilliseconds(100));

			var results = new List<(DateTime, ushort)>(series.QueryRange(T0, T0.AddDays(1)));
			Assert.Single(results);
			Assert.Equal(PingRecordStatus.Pending, results[0].Item2);
		}

		[Fact]
		public void CompleteRecord_UpdatesRttInPlace()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			var handle = series.BeginPendingRecord(T0.AddMilliseconds(100));

			series.CompleteRecord(handle, 42);

			var results = new List<(DateTime, ushort)>(series.QueryRange(T0, T0.AddDays(1)));
			Assert.Single(results);
			Assert.Equal((ushort)42, results[0].Item2);
		}

		[Fact]
		public void BackwardClock_SecondRecordClamped()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);

			var h1 = series.BeginPendingRecord(T0.AddMilliseconds(500));
			var h2 = series.BeginPendingRecord(T0.AddMilliseconds(200)); // backward

			var results = new List<(DateTime, ushort)>(series.QueryRange(T0, T0.AddDays(1)));
			Assert.Equal(2, results.Count);

			// Second record should be clamped to T0+500ms, not T0+200ms
			Assert.True(results[1].Item1 >= results[0].Item1);
			Assert.Equal(T0.AddMilliseconds(500), results[1].Item1);
		}

		[Fact]
		public void OutOfOrderCompletion_AllRecordsComplete()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);

			var h1 = series.BeginPendingRecord(T0.AddMilliseconds(100));
			var h2 = series.BeginPendingRecord(T0.AddMilliseconds(200));
			var h3 = series.BeginPendingRecord(T0.AddMilliseconds(300));

			// Complete in reverse order
			series.CompleteRecord(h3, 30);
			series.CompleteRecord(h1, 10);
			series.CompleteRecord(h2, 20);

			var results = new List<(DateTime, ushort)>(series.QueryRange(T0, T0.AddDays(1)));
			Assert.Equal(3, results.Count);
			Assert.Equal((ushort)10, results[0].Item2);
			Assert.Equal((ushort)20, results[1].Item2);
			Assert.Equal((ushort)30, results[2].Item2);
		}

		[Fact]
		public void QueryRange_ReturnsOnlyRecordsInWindow()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);

			for (int i = 1; i <= 10; i++)
			{
				var h = series.BeginPendingRecord(T0.AddSeconds(i));
				series.CompleteRecord(h, (ushort)(i * 10));
			}

			// Query seconds 3-7
			var results = new List<(DateTime, ushort)>(
				series.QueryRange(T0.AddSeconds(3), T0.AddSeconds(7)));

			Assert.Equal(5, results.Count);
			Assert.Equal((ushort)30, results[0].Item2);
			Assert.Equal((ushort)70, results[4].Item2);
		}

		[Fact]
		public void QueryRange_EmptyWhenWindowOutside()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			var h = series.BeginPendingRecord(T0.AddSeconds(5));
			series.CompleteRecord(h, 10);

			var before = new List<(DateTime, ushort)>(series.QueryRange(T0.AddSeconds(6), T0.AddSeconds(10)));
			Assert.Empty(before);
		}

		[Fact]
		public void AggregateRange_ReturnsSinglePoints_WhenUnderMaxPoints()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			for (int i = 1; i <= 5; i++)
			{
				var h = series.BeginPendingRecord(T0.AddSeconds(i));
				series.CompleteRecord(h, (ushort)(i * 10));
			}

			var result = series.AggregateRange(T0, T0.AddSeconds(10), maxPoints: 100);
			Assert.Equal(5, result.Points.Length);
			Assert.Equal(10.0, result.Points[0].AvgRtt);
		}

		[Fact]
		public void AggregateRange_Downsamples_WhenOverMaxPoints()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			for (int i = 1; i <= 100; i++)
			{
				var h = series.BeginPendingRecord(T0.AddSeconds(i));
				series.CompleteRecord(h, (ushort)i);
			}

			var result = series.AggregateRange(T0, T0.AddSeconds(100), maxPoints: 10);
			Assert.Equal(10, result.Points.Length);

			// Each bucket should have ~10 samples
			foreach (var pt in result.Points)
				Assert.True(pt.SampleCount > 0);
		}

		[Fact]
		public void AggregateRange_PendingRecords_ExcludedFromLatency()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			series.BeginPendingRecord(T0.AddSeconds(1)); // left pending intentionally
			var h2 = series.BeginPendingRecord(T0.AddSeconds(2));
			series.CompleteRecord(h2, 50);

			var result = series.AggregateRange(T0, T0.AddSeconds(5), maxPoints: 10);
			// Only the completed record contributes to AvgRtt
			var ptWithData = Array.Find(result.Points, p => p.SampleCount > 0 && p.AvgRtt > 0);
			Assert.True(ptWithData.SampleCount > 0 && ptWithData.AvgRtt > 0, "Expected at least one bucket with completed ping data");
			Assert.Equal(50.0, ptWithData.AvgRtt);
		}

		[Fact]
		public void PruneOlderThan_DropsOldChunks_KeepsRecent()
		{
			var series = new HopTimeSeries(0, TestAddr, T0, startDnsLookup: false);

			// Insert 5000 records (> 1 chunk of 4096)
			for (int i = 0; i < 5000; i++)
			{
				var h = series.BeginPendingRecord(T0.AddSeconds(i));
				series.CompleteRecord(h, 10);
			}

			long before = series.RecordCount;
			Assert.Equal(5000, before);

			// Prune everything older than T0 + 4097 seconds
			series.PruneOlderThan(T0.AddSeconds(4097));

			Assert.True(series.RecordCount < before, "Some records should have been pruned");

			// Records after T0+4096s should still be accessible
			var results = new List<(DateTime, ushort)>(
				series.QueryRange(T0.AddSeconds(4096), T0.AddSeconds(5000)));
			Assert.NotEmpty(results);
		}

		[Fact]
		public void SetHostname_FiresHostnameUpdatedEvent()
		{
			var series = new HopTimeSeries(0, TestAddr, T0);
			HopTimeSeries fired = null;
			series.HostnameUpdated += s => fired = s;

			series.SetHostname("router.example.com");

			Assert.Same(series, fired);
			Assert.Equal("router.example.com", series.Hostname);
		}
	}
}
