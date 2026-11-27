using PingTracer.Services;
using PingTracer.Storage;
using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PingTracer.Tests
{
	public class PersistenceTests : IDisposable
	{
		private const string Guid1 = "11111111-2222-3333-4444-555555555555";
		private readonly string root = Path.Combine(Path.GetTempPath(), "pingtracer-tests-" + Guid.NewGuid().ToString("N"));
		private string ConfigDir => Path.Combine(root, Guid1);

		public PersistenceTests() { Directory.CreateDirectory(root); }

		public void Dispose()
		{
			try { Directory.Delete(root, true); } catch { }
		}

		private static readonly (string, SessionMode)[] ExampleKey = { ("example", SessionMode.TraceRoute) };

		/// <summary>A traceroute session with 70,000 pings at TTL 2 (loss, two alternating addresses) and a destination at TTL 5.</summary>
		private static MonitoringSession BuildSession(long startUnixMs = 0, string hostKey = "example")
		{
			// Midnight two days ago: the 19.4 hours of data stay within one local day, so they are one segment.
			long origin = startUnixMs > 0 ? startUnixMs : new DateTimeOffset(DateTime.Now.Date.AddDays(-2)).ToUnixTimeMilliseconds();
			MonitoringSession s = new MonitoringSession(IPAddress.Parse("198.51.100.10"), "example [198.51.100.10]", SessionMode.TraceRoute, 1000, 2000, startUnixMs: origin, hostKey: hostKey);
			List<PingSample> hop2 = new List<PingSample>();
			ushort a1 = s.LoadAddress(IPAddress.Parse("203.0.113.1"));
			ushort a2 = s.LoadAddress(IPAddress.Parse("203.0.113.2"));
			ushort dest = s.LoadAddress(IPAddress.Parse("198.51.100.10"));
			for (int i = 0; i < 70_000; i++)
			{
				long t = i * 1000L + (i % 7); // a little jitter
				hop2.Add(i % 10 == 0 ? new PingSample(t, PingRecordStatus.Lost, AddressTable.None) : new PingSample(t, (ushort)(i % 50), i % 2 == 0 ? a1 : a2));
			}
			s.LoadRecords(2, hop2);
			s.LoadRecords(5, new List<PingSample> { new PingSample(500, 30, dest) });
			s.LoadHostname(a1, "r1.example");
			s.LoadRouteEvent(new RouteEvent(500, RouteEventKind.DestinationTtl, 5, 0, true));
			return s;
		}

		private static void AssertSameData(MonitoringSession a, MonitoringSession b, long fromUnixMs, int msPerCol, int cols)
		{
			var qa = a.Query(fromUnixMs / msPerCol, msPerCol, cols);
			var qb = b.Query(fromUnixMs / msPerCol, msPerCol, cols);
			Assert.Equal(qa.Count, qb.Count);
			for (int i = 0; i < qa.Count; i++)
			{
				Assert.Equal(qa[i].ttl, qb[i].ttl);
				Assert.Equal(qa[i].stats.N, qb[i].stats.N);
				Assert.Equal(qa[i].stats.Lost, qb[i].stats.Lost);
				Assert.Equal(qa[i].stats.Sum, qb[i].stats.Sum);
				Assert.Equal(qa[i].stats.Min, qb[i].stats.Min);
				Assert.Equal(qa[i].stats.Max, qb[i].stats.Max);
				// Address ids may differ between sessions; compare the addresses.
				for (int c = 0; c < cols; c++)
					Assert.Equal(a.Addresses.GetAddress(qa[i].stats.Addr[c]), b.Addresses.GetAddress(qb[i].stats.Addr[c]));
			}
		}

		[Fact]
		public void ImportedSessionLoadsBackIdentically()
		{
			MonitoringSession s = BuildSession();
			List<string> files = ConfigDataSet.ImportSession(s, root, Guid1, "example");
			Assert.NotEmpty(files);

			PingDataFile.Header h = PingDataFile.ReadHeader(files[0]);
			Assert.Equal(PingDataFile.CurrentVersion, h.Version);
			Assert.Equal(PingDataCompression.Brotli, h.Compression);
			Assert.Equal(Guid1, h.ConfigGuid);
			Assert.Equal("example", h.HostKey);

			ConfigDataSet set = ConfigDataSet.Load(root, Guid1, ExampleKey, long.MaxValue / 4);
			MonitoringSession loaded = Assert.Single(set.Sessions);
			Assert.False(loaded.IsReadOnly);
			Assert.Equal(5, loaded.DestinationTtl);
			Assert.Equal(new[] { 2, 5 }, loaded.GetRepliedTtls());
			Assert.True(loaded.Addresses.TryGetId(IPAddress.Parse("203.0.113.1"), out ushort id));
			Assert.Equal("r1.example", loaded.Addresses.GetHostname(id));
			Assert.Equal(70_001, loaded.GetHops().Sum(x => x.Count));
			AssertSameData(s, loaded, s.StartUnixMs, 1000, 4096);
			AssertSameData(s, loaded, s.StartUnixMs, 60_000, 1200);

			// About one byte per ping (the uncompressed version 3 format used six).
			long size = files.Sum(f => new FileInfo(f).Length);
			Assert.True(size < 70_000 * 2, "size " + size);
		}

		[Fact]
		public void LiveWriterSavesOnlyNewDataAndTheFileIsReadableWhileOpen()
		{
			MonitoringSession s = BuildSession(); // "earlier runs": must not be written again
			long before = s.GetHops().Sum(x => x.Count);
			PingDataWriter w = new PingDataWriter(s, ConfigDir, Guid1, 3600);
			try
			{
				for (int i = 0; i < 50; i++)
				{
					RecordHandle r = s.BeginPing(3);
					s.CompletePing(3, r, i % 5 != 0, 12 + i % 3, IPAddress.Parse("203.0.113.3"));
				}
				w.Flush(final: true);
				// Brotli is flushed, not finished, so everything so far must decode while the writer still has the file open.
				PingDataReader.Result live = PingDataReader.Load(w.FilePath);
				Assert.Equal(50, live.Session.GetHop(3).Count);
				Assert.Null(live.Session.GetHop(2));
				Assert.True(PingDataFile.ReadHeader(w.FilePath).EndUnixMs > 0, "end time is written at each flush");
			}
			finally
			{
				w.Dispose();
			}
			Assert.Equal(before + 50, s.GetHops().Sum(x => x.Count));
			PingDataReader.Result r2 = PingDataReader.Load(w.FilePath);
			Assert.False(r2.Truncated);
			Assert.Equal(50, r2.Session.GetHop(3).Count);
			Assert.Equal(40, r2.Session.GetHop(3).ReplyCount);
		}

		[Fact]
		public void EmptySegmentIsDeleted()
		{
			MonitoringSession s = BuildSession();
			PingDataWriter w = new PingDataWriter(s, ConfigDir, Guid1, 3600);
			string path = w.FilePath;
			Assert.True(File.Exists(path));
			w.Dispose();
			Assert.False(File.Exists(path));
		}

		[Fact]
		public void TruncatedFileLoadsCompleteBlocks()
		{
			MonitoringSession s = BuildSession();
			string path = ConfigDataSet.ImportSession(s, root, Guid1, "example").Single();
			long full = new FileInfo(path).Length;
			using (FileStream fs = new FileStream(path, FileMode.Open))
				fs.SetLength(full - full / 3);
			MonitoringSession target = new MonitoringSession(s.TargetAddress, "x", SessionMode.TraceRoute, 1000, 2000, s.StartUnixMs, hostKey: "example");
			PingDataReader.Result r = PingDataReader.LoadInto(target, path);
			Assert.True(r.Truncated);
			long n = target.GetHop(2).Count;
			Assert.InRange(n, 1, 69_999);
		}

		[Fact]
		public void SegmentsSplitAtLocalMidnight()
		{
			// Starts 23:00 local time two days ago and runs for 26 hours: three local days.
			DateTime startLocal = DateTime.Now.Date.AddDays(-2).AddHours(23);
			long start = new DateTimeOffset(startLocal).ToUnixTimeMilliseconds();
			MonitoringSession s = new MonitoringSession(IPAddress.Parse("198.51.100.10"), "x", SessionMode.Direct, 10_000, 2000, start, hostKey: "x");
			ushort a = s.LoadAddress(IPAddress.Parse("198.51.100.10"));
			List<PingSample> samples = new List<PingSample>();
			for (long t = 0; t < 26 * 3600_000L; t += 10_000)
				samples.Add(new PingSample(t, 20, a));
			s.LoadRecords(0, samples);

			List<string> files = ConfigDataSet.ImportSession(s, root, Guid1, "x");
			Assert.Equal(3, files.Count);
			List<PingDataFile.Header> headers = files.Select(PingDataFile.ReadHeader).OrderBy(h => h.StartUnixMs).ToList();
			Assert.Equal(start, headers[0].StartUnixMs);
			for (int i = 1; i < 3; i++)
			{
				DateTime local = DateTimeOffset.FromUnixTimeMilliseconds(headers[i].StartUnixMs).LocalDateTime;
				Assert.Equal(local.Date, local);
				Assert.True(headers[i - 1].EndUnixMs < headers[i].StartUnixMs);
			}
			ConfigDataSet set = ConfigDataSet.Load(root, Guid1, new[] { ("x", SessionMode.Direct) }, long.MaxValue / 4);
			Assert.Equal(samples.Count, set.Sessions.Single().GetHop(0).Count);
			AssertSameData(s, set.Sessions.Single(), start, 60_000, 26 * 60);
		}

		[Fact]
		public void RetentionCountsMonitoringTimeNotCalendarTime()
		{
			// One hour a day for 30 days, with one day of retention: the newest 24 runs are kept.
			long day = 86_400_000, hour = 3_600_000;
			long t0 = 1_700_000_000_000;
			List<(long, long)> runs = Enumerable.Range(0, 30).Select(i => (t0 + i * day, t0 + i * day + hour)).ToList();
			long cutoff = ConfigDataSet.ComputeCutoff(ConfigDataSet.MergeIntervals(runs, 0), day);
			Assert.Equal(t0 + 6 * day, cutoff);
			// A partial run: 2.5 hours kept out of 1-hour runs ends half way through the third-newest run.
			cutoff = ConfigDataSet.ComputeCutoff(ConfigDataSet.MergeIntervals(runs, 0), 2 * hour + hour / 2);
			Assert.Equal(t0 + 27 * day + hour / 2, cutoff);
			// Everything fits.
			Assert.Equal(long.MinValue, ConfigDataSet.ComputeCutoff(ConfigDataSet.MergeIntervals(runs, 0), 31 * hour));
			// Overlapping and touching intervals are merged.
			var merged = ConfigDataSet.MergeIntervals(new[] { (10L, 20L), (15L, 30L), (31L, 40L), (100L, 110L) }, 1);
			Assert.Equal(new[] { (10L, 40L), (100L, 110L) }, merged);
		}

		/// <summary>Writes a segment of <paramref name="hours"/> of pings starting at <paramref name="start"/>.</summary>
		private string WriteRun(long start, int hours)
		{
			MonitoringSession s = new MonitoringSession(IPAddress.Parse("198.51.100.10"), "x", SessionMode.Direct, 60_000, 2000, start, hostKey: "x");
			ushort a = s.LoadAddress(IPAddress.Parse("198.51.100.10"));
			List<PingSample> samples = new List<PingSample>();
			for (long t = 0; t <= hours * 3600_000L; t += 60_000)
				samples.Add(new PingSample(t, 20, a));
			s.LoadRecords(0, samples);
			return ConfigDataSet.ImportSession(s, root, Guid1, "x").Single();
		}

		[Fact]
		public void LoadingDeletesNothingAndStartKeepsTheNewestMonitoringTime()
		{
			// Three one-hour runs on three different days, long ago; one hour and a half of retention.
			DateTime d = DateTime.Now.Date.AddDays(-60).AddHours(10);
			long day = 86_400_000;
			long start = new DateTimeOffset(d).ToUnixTimeMilliseconds();
			string f1 = WriteRun(start, 1), f2 = WriteRun(start + day, 1), f3 = WriteRun(start + 2 * day, 1);
			long retention = 90 * 60_000;

			ConfigDataSet set = ConfigDataSet.Load(root, Guid1, new[] { ("x", SessionMode.Direct) }, retention);
			Assert.True(File.Exists(f1) && File.Exists(f2) && File.Exists(f3), "loading must not delete anything");
			MonitoringSession s = set.Sessions.Single();
			// The oldest run is not loaded; half of the middle one is.
			Assert.InRange(s.DataStartUnixMs, start + day + 25 * 60_000, start + day + 35 * 60_000);
			Assert.Single(s.Gaps);

			// Starting (much later): the oldest file is deleted; the partly kept one stays.
			long now = ServerClock.NowUnixMs;
			set.BeginRun(now);
			int deleted = set.ApplyRetention(retention, now, deleteFiles: true);
			Assert.Equal(1, deleted);
			Assert.False(File.Exists(f1));
			Assert.True(File.Exists(f2) && File.Exists(f3));
			// As the run accumulates monitoring time, older data goes.
			deleted = set.ApplyRetention(retention, now + 31 * 60_000, deleteFiles: true);
			Assert.Equal(1, deleted);
			Assert.False(File.Exists(f2));
			Assert.True(File.Exists(f3));
		}

		[Fact]
		public void RunLockIsExclusive()
		{
			ConfigDataSet a = new ConfigDataSet(root, Guid1);
			using (ConfigDataSet.RunLock l1 = a.TryAcquireRunLock())
			{
				Assert.NotNull(l1);
				Assert.Null(new ConfigDataSet(root, Guid1).TryAcquireRunLock());
				Assert.False(ConfigDataSet.DeleteData(root, Guid1), "data of a running configuration must not be deleted");
			}
			using (ConfigDataSet.RunLock l2 = a.TryAcquireRunLock())
				Assert.NotNull(l2);
		}

		[Fact]
		public void DetectsDataWrittenByAnotherInstance()
		{
			WriteRun(ServerClock.NowUnixMs - 10 * 3600_000L, 1);
			ConfigDataSet set = ConfigDataSet.Load(root, Guid1, new[] { ("x", SessionMode.Direct) }, long.MaxValue / 4);
			Assert.False(set.IsStale());
			WriteRun(ServerClock.NowUnixMs - 5 * 3600_000L, 1);
			Assert.True(set.IsStale());
		}

		[Fact]
		public void HeaderRejectsNewerVersionsAndUnknownCompression()
		{
			string path = ConfigDataSet.ImportSession(BuildSession(), root, Guid1, "example").Single();
			byte[] bytes = File.ReadAllBytes(path);
			byte[] compression = (byte[])bytes.Clone();
			compression[6] = 9;
			NotSupportedException ex = Assert.Throws<NotSupportedException>(() => PingDataFile.ReadHeader(new BinaryReader(new MemoryStream(compression))));
			Assert.Contains("compression", ex.Message);
			byte[] version = (byte[])bytes.Clone();
			version[4] = 5;
			ex = Assert.Throws<NotSupportedException>(() => PingDataFile.ReadHeader(new BinaryReader(new MemoryStream(version))));
			Assert.Contains("newer version", ex.Message);
			// Listing skips such files instead of failing.
			File.WriteAllBytes(path, compression);
			Assert.Empty(PingDataFile.ListSessionFiles(root, recursive: true));
		}

		[Fact]
		public void ReadsVersion3Files()
		{
			string path = Path.Combine(root, "old.ptrc");
			long start = 1_700_000_000_000;
			using (BinaryWriter w = new BinaryWriter(File.Create(path)))
			{
				w.Write(Encoding.ASCII.GetBytes("PTRC"));
				w.Write((ushort)3);
				w.Write(start);
				w.Write((byte)SessionMode.Direct);
				w.Write(1000);
				w.Write(2000);
				w.Write((byte)4); w.Write(new byte[] { 198, 51, 100, 10 });
				w.Write((ushort)3); w.Write(Encoding.UTF8.GetBytes("old"));
				// Address 0
				w.Write((byte)0x10); w.Write(7u); w.Write((ushort)0); w.Write((byte)4); w.Write(new byte[] { 198, 51, 100, 10 });
				// Records at TTL 0: two pings, one lost
				w.Write((byte)0x12); w.Write((uint)(11 + 12)); w.Write((byte)0); w.Write(5000L); w.Write((ushort)2);
				w.Write((ushort)0); w.Write((ushort)25); w.Write((ushort)0);
				w.Write((ushort)1000); w.Write(PingRecordStatus.Lost); w.Write(AddressTable.None);
			}
			PingDataReader.Result r = PingDataReader.Load(path);
			Assert.False(r.Truncated);
			Assert.Equal(start, r.Session.StartUnixMs);
			Assert.Equal(2, r.Session.GetHop(0).Count);
			Assert.Equal(start + 6000, r.LastTimeUnixMs);
			PingDataFile.FileInfoSummary f = Assert.Single(PingDataFile.ListSessionFiles(root));
			Assert.Equal(start + 6000, f.EndUnixMs);

			// And it can be imported into a configuration.
			ConfigDataSet.ImportFile(path, root, Guid1, "old");
			ConfigDataSet set = ConfigDataSet.Load(root, Guid1, new[] { ("old", SessionMode.Direct) }, long.MaxValue / 4);
			AssertSameData(r.Session, set.Sessions.Single(), start, 1000, 10);
		}

		[Fact]
		public async Task StopAndStartResumesTheSameGraphAndSavesIt()
		{
			PingConfiguration cfg = new PingConfiguration
			{
				guid = Guid1,
				hosts = new List<Host> { new Host { hostname = "sim:demo" } },
				rate = 10,
				pingsPerSecond = true,
				doTraceRoute = true,
				reverseDnsLookup = false,
			};
			Settings settings = new Settings { saveSessionData = true, pingTimeoutMs = 500 };
			ConfigDataSet set = new ConfigDataSet(root, Guid1);

			PingSession run1 = new PingSession(cfg, settings, set, set.TryAcquireRunLock());
			Assert.True(run1.Start());
			await Task.Delay(1500);
			await run1.StopAsync();
			MonitoringSession s = Assert.Single(set.Sessions);
			long afterFirst = s.GetHops().Sum(h => h.Count);
			Assert.True(afterFirst > 0);
			using (ConfigDataSet.RunLock l = set.TryAcquireRunLock())
				Assert.NotNull(l); // released when the run stopped

			PingSession run2 = new PingSession(cfg, settings, set, null);
			Assert.True(run2.Start());
			Assert.Same(s, run2.Sessions.Single());
			await Task.Delay(1500);
			await run2.StopAsync();
			long afterSecond = s.GetHops().Sum(h => h.Count);
			Assert.True(afterSecond > afterFirst);

			// Everything is on disk: a new instance sees the same data (voided and pending records are not saved).
			ConfigDataSet reloaded = ConfigDataSet.Load(root, Guid1, PingSession.GetSessionKeys(cfg), long.MaxValue / 4);
			MonitoringSession r = reloaded.Sessions.Single();
			Assert.Equal(2, Directory.GetFiles(ConfigDir, "*.ptrc").Length);
			AssertSameData(s, r, s.StartUnixMs, 100, 60);
			Assert.Equal(s.DestinationTtl, r.DestinationTtl);
		}
	}
}
