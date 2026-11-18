using PingTracer.Storage;
using System;
using System.IO;
using System.Linq;
using System.Net;
using Xunit;

namespace PingTracer.Tests
{
	public class PersistenceTests : IDisposable
	{
		private readonly string dir = Path.Combine(Path.GetTempPath(), "pingtracer-tests-" + Guid.NewGuid().ToString("N"));

		public PersistenceTests() { Directory.CreateDirectory(dir); }

		public void Dispose()
		{
			try { Directory.Delete(dir, true); } catch { }
		}

		private static MonitoringSession BuildSession()
		{
			MonitoringSession s = new MonitoringSession(IPAddress.Parse("198.51.100.10"), "example [198.51.100.10]", SessionMode.TraceRoute, 1000, 2000, startUnixMs: ServerClock.NowUnixMs - 100_000);
			for (int i = 0; i < 70_000; i++) // more than one block's worth for TTL 2, spanning > 65 s
			{
				RecordHandle h = s.BeginPing(2);
				if (i % 10 == 0)
					s.CompletePing(2, h, false, 0, null);
				else
					s.CompletePing(2, h, true, i % 50, IPAddress.Parse(i % 2 == 0 ? "203.0.113.1" : "203.0.113.2"));
			}
			RecordHandle d = s.BeginPing(5);
			s.CompletePing(5, d, true, 30, IPAddress.Parse("198.51.100.10"));
			s.SetDestinationTtl(5);
			return s;
		}

		[Fact]
		public void WriterAndReaderRoundTrip()
		{
			MonitoringSession s = BuildSession();
			string path = Path.Combine(dir, "a.ptrc");
			using (PingDataWriter w = new PingDataWriter(s, path, 3600))
			{
				s.LoadHostname(0, "r1.example"); // hostname known before the first flush
			}
			PingDataReader.Result r = PingDataReader.Load(path);
			Assert.False(r.Truncated);
			MonitoringSession loaded = r.Session;
			Assert.True(loaded.IsReadOnly);
			Assert.Equal(s.StartUnixMs, loaded.StartUnixMs);
			Assert.Equal(s.DisplayName, loaded.DisplayName);
			Assert.Equal(5, loaded.DestinationTtl);
			Assert.Equal(s.Addresses.Count, loaded.Addresses.Count);
			Assert.Equal(new[] { 2, 5 }, loaded.GetRepliedTtls());

			// Aggregate both sessions over the same columns and compare.
			var a = s.Query(s.StartUnixMs / 1000, 1000, 200);
			var b = loaded.Query(s.StartUnixMs / 1000, 1000, 200);
			Assert.Equal(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.Equal(a[i].ttl, b[i].ttl);
				Assert.Equal(a[i].stats.N, b[i].stats.N);
				Assert.Equal(a[i].stats.Lost, b[i].stats.Lost);
				Assert.Equal(a[i].stats.Sum, b[i].stats.Sum);
				Assert.Equal(a[i].stats.Addr, b[i].stats.Addr);
			}
		}

		[Fact]
		public void TruncatedFileLoadsCompleteBlocks()
		{
			MonitoringSession s = BuildSession();
			string path = Path.Combine(dir, "b.ptrc");
			using (new PingDataWriter(s, path, 3600)) { }
			long full = new FileInfo(path).Length;
			using (FileStream fs = new FileStream(path, FileMode.Open))
				fs.SetLength(full - 7);
			PingDataReader.Result r = PingDataReader.Load(path);
			Assert.True(r.Truncated);
			Assert.True(r.ValidLength < full);
			Assert.NotNull(r.Session.GetHop(2));
		}

		[Fact]
		public void ListsSessionFiles()
		{
			MonitoringSession s = BuildSession();
			using (new PingDataWriter(s, Path.Combine(dir, PingDataFile.BuildSessionFileName(s.TargetAddress, s.StartUnixMs)), 3600)) { }
			File.WriteAllText(Path.Combine(dir, "junk.ptrc"), "not a session");
			var files = PingDataFile.ListSessionFiles(dir);
			Assert.Single(files);
			Assert.Equal("198.51.100.10", files[0].Target);
			Assert.Equal(SessionMode.TraceRoute, files[0].Mode);
		}

		[Fact]
		public void QueryFrameEncodesOnlyNonEmptyColumns()
		{
			MonitoringSession s = BuildSession();
			var results = s.Query(s.StartUnixMs / 1000, 1000, 100);
			byte[] frame = PingTracer.Services.PingWebSocketHandler.BuildQueryFrame(7, s.Id, 1000, s.StartUnixMs / 1000, 100, results);
			Assert.Equal(PingTracer.Services.BinaryFrameType.QueryResult, frame[0]);
			Assert.Equal(7u, BitConverter.ToUInt32(frame, 1));
			int expected = 1 + 4 + 2 + 4 + 8 + 2 + 2 + results.Sum(r => 3 + r.nonEmpty * 17);
			Assert.Equal(expected, frame.Length);
		}
	}
}
