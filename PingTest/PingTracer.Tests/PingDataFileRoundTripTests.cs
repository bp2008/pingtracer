using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using PingTracer.Storage;
using PingTracer.TraceRoute;
using Xunit;

namespace PingTracer.Tests
{
	/// <summary>
	/// End-to-end write→read tests for the .ptrc session log format.
	/// </summary>
	public class PingDataFileRoundTripTests : IDisposable
	{
		private static readonly IPAddress Target = IPAddress.Parse("8.8.8.8");
		private readonly string _tmpDir;

		public PingDataFileRoundTripTests()
		{
			_tmpDir = Path.Combine(Path.GetTempPath(), "ptrc-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tmpDir);
		}

		public void Dispose()
		{
			try { Directory.Delete(_tmpDir, recursive: true); } catch { }
		}

		private static TraceRouteHostResult Hit(byte ttl, string ip)
			=> new TraceRouteHostResult(null, true, 10, IPAddress.Parse(ip), ttl, Target, ttl, 5000, DateTime.UtcNow);

		[Fact]
		public void RoundTrip_BasicSeries_RoundTripsRecords()
		{
			string path = Path.Combine(_tmpDir, "basic.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "192.168.1.1") }, session.StartTimeUtc);

			HopTimeSeries s = session.HopData[0].Series[0];
			DateTime t = session.StartTimeUtc;
			RecordHandle h1 = s.BeginPendingRecord(t.AddMilliseconds(100));
			s.CompleteRecord(h1, 12);
			RecordHandle h2 = s.BeginPendingRecord(t.AddMilliseconds(200));
			s.CompleteRecord(h2, 15);

			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
				w.Flush();

			MonitoringSession loaded = PingDataReader.LoadFile(path, out _);
			HopTimeSeries ls = loaded.HopData[0].Series.Single();
			Assert.Equal(IPAddress.Parse("192.168.1.1"), ls.Address);
			Assert.Equal(2, ls.RecordCount);

			List<(DateTime ts, ushort rtt)> rows = ls.QueryRange(DateTime.MinValue, DateTime.MaxValue).ToList();
			Assert.Equal(2, rows.Count);
			Assert.Equal((ushort)12, rows[0].rtt);
			Assert.Equal((ushort)15, rows[1].rtt);
		}

		[Fact]
		public void RoundTrip_PendingRecord_NotPersisted()
		{
			string path = Path.Combine(_tmpDir, "pending.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, session.StartTimeUtc);

			HopTimeSeries s = session.HopData[0].Series[0];
			RecordHandle done = s.BeginPendingRecord(session.StartTimeUtc.AddMilliseconds(50));
			s.CompleteRecord(done, 8);
			s.BeginPendingRecord(session.StartTimeUtc.AddMilliseconds(150)); // intentionally never completed

			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
				w.Flush(); // dispose runs another flush; ensure both behave

			MonitoringSession loaded = PingDataReader.LoadFile(path, out _);
			HopTimeSeries ls = loaded.HopData[0].Series.Single();
			Assert.Equal(1, ls.RecordCount); // pending record was excluded
			Assert.Equal((ushort)8, ls.QueryRange(DateTime.MinValue, DateTime.MaxValue).Single().rtt);
		}

		[Fact]
		public void RoundTrip_AddressChange_TwoSeriesPreserved()
		{
			string path = Path.Combine(_tmpDir, "twoseries.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			DateTime t0 = session.StartTimeUtc;

			session.RecordTraceResult(new[] { Hit(1, "192.168.1.1") }, t0);
			HopTimeSeries first = session.HopData[0].Series[0];
			RecordHandle h = first.BeginPendingRecord(t0.AddMilliseconds(50));
			first.CompleteRecord(h, 5);

			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, t0.AddSeconds(1));
			HopTimeSeries second = session.HopData[0].Series[1];
			RecordHandle h2 = second.BeginPendingRecord(t0.AddSeconds(1).AddMilliseconds(75));
			second.CompleteRecord(h2, 22);

			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
				w.Flush();

			MonitoringSession loaded = PingDataReader.LoadFile(path, out _);
			List<HopTimeSeries> series = loaded.HopData[0].Series;
			Assert.Equal(2, series.Count);
			Assert.Equal(IPAddress.Parse("192.168.1.1"), series[0].Address);
			Assert.Equal(IPAddress.Parse("10.0.0.1"), series[1].Address);
			Assert.Equal(1, series[0].RecordCount);
			Assert.Equal(1, series[1].RecordCount);
			// First series should be closed (EndTimeUtc set to next series' start).
			Assert.NotNull(series[0].EndTimeUtc);
		}

		[Fact]
		public void RoundTrip_HostnameUpdated_RehydratedByReader()
		{
			string path = Path.Combine(_tmpDir, "dns.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "1.2.3.4") }, session.StartTimeUtc);
			HopTimeSeries s = session.HopData[0].Series[0];

			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
			{
				s.SetHostname("router.local");
				w.Flush();
			}

			MonitoringSession loaded = PingDataReader.LoadFile(path, out _);
			Assert.Equal("router.local", loaded.HopData[0].Series.Single().Hostname);
		}

		[Fact]
		public void RoundTrip_MultiBlockSplit_OverMaxRecordsPerBlock()
		{
			string path = Path.Combine(_tmpDir, "split.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, session.StartTimeUtc);
			HopTimeSeries s = session.HopData[0].Series[0];

			// 70k records spaced 1 ms apart — forces split on both record-count AND
			// time-span limits (uint16 = 65535).
			const int N = 70000;
			DateTime t = session.StartTimeUtc;
			for (int i = 0; i < N; i++)
			{
				RecordHandle h = s.BeginPendingRecord(t.AddMilliseconds(i));
				s.CompleteRecord(h, (ushort)(i % 100));
			}

			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
				w.Flush();

			MonitoringSession loaded = PingDataReader.LoadFile(path, out _);
			HopTimeSeries ls = loaded.HopData[0].Series.Single();
			Assert.Equal(N, ls.RecordCount);

			// Spot-check a few entries
			List<(DateTime ts, ushort rtt)> all = ls.QueryRange(DateTime.MinValue, DateTime.MaxValue).ToList();
			Assert.Equal(N, all.Count);
			Assert.Equal((ushort)0, all[0].rtt);
			Assert.Equal((ushort)(99), all[99].rtt);
			Assert.Equal((ushort)((N - 1) % 100), all[N - 1].rtt);
		}

		[Fact]
		public void RoundTrip_HeaderEncodesTargetAndVersion()
		{
			string path = Path.Combine(_tmpDir, "header.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
				w.Flush();

			byte[] bytes = File.ReadAllBytes(path);
			Assert.Equal((byte)'P', bytes[0]);
			Assert.Equal((byte)'T', bytes[1]);
			Assert.Equal((byte)'R', bytes[2]);
			Assert.Equal((byte)'C', bytes[3]);
			Assert.Equal((ushort)1, (ushort)(bytes[4] | (bytes[5] << 8)));
			Assert.Equal((byte)4, bytes[6]); // IPv4 length
		}
	}
}
