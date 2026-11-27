using PingTracer.Storage;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace PingTracer.Tests
{
	/// <summary>
	/// Measures how large real data becomes when written the way monitoring writes it (a flush every N seconds).  Runs only
	/// when PINGTRACER_SAMPLE_FILE names a saved data file.
	/// </summary>
	public class CompressionBenchmark
	{
		private readonly ITestOutputHelper output;
		public CompressionBenchmark(ITestOutputHelper output) { this.output = output; }

		[Fact]
		public void LiveFlushSizes()
		{
			string src = Environment.GetEnvironmentVariable("PINGTRACER_SAMPLE_FILE");
			if (string.IsNullOrEmpty(src))
				return;
			string root = Path.Combine(Path.GetTempPath(), "pingtracer-bench-" + Guid.NewGuid().ToString("N"));
			try
			{
				MonitoringSession s = PingDataReader.Load(src).Session;
				long sourceBytes = new FileInfo(src).Length;
				var hops = s.GetHops().Select(h => { var l = new List<PingSample>(); long c = h.FirstIndex; lock (s.DataLock) h.CopyStableRecords(ref c, long.MaxValue, l); return (h.Ttl, l); }).ToList();
				long n = hops.Sum(h => (long)h.l.Count);
				foreach (int flushSec in new[] { 10, 60 })
				{
					string dir = Path.Combine(root, "g");
					MonitoringSession dst = new MonitoringSession(s.TargetAddress, s.DisplayName, s.Mode, s.IntervalMs, s.TimeoutMs, s.StartUnixMs, hostKey: "bench");
					foreach (var a in s.Addresses.All())
					{
						ushort id = dst.LoadAddress(a.address);
						if (!string.IsNullOrEmpty(a.hostname)) dst.LoadHostname(id, a.hostname);
					}
					int[] idx = new int[hops.Count];
					double flushMs = 0, maxFlushMs = 0;
					int flushes = 0;
					PingDataWriter w = new PingDataWriter(dst, dir, "g", 1_000_000, PingDataCompression.Brotli, s.StartUnixMs);
					for (long t = flushSec * 1000L; t <= s.LastRecordTimeMs + flushSec * 1000L; t += flushSec * 1000L)
					{
						for (int i = 0; i < hops.Count; i++)
						{
							var l = hops[i].l;
							List<PingSample> batch = new List<PingSample>();
							while (idx[i] < l.Count && l[idx[i]].TimeMs < t) batch.Add(l[idx[i]++]);
							dst.LoadRecords(hops[i].Ttl, batch);
						}
						long f0 = Stopwatch.GetTimestamp();
						w.Flush(final: true);
						double ms = Stopwatch.GetElapsedTime(f0).TotalMilliseconds;
						flushMs += ms; maxFlushMs = Math.Max(maxFlushMs, ms); flushes++;
					}
					w.Dispose();
					long total = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length);
					output.WriteLine($"flush every {flushSec}s: {total:N0} bytes = {total / (double)n:F3} bytes/ping, {sourceBytes / (double)total:F1}x smaller than the source; {flushes:N0} flushes, {flushMs / flushes:F3} ms average, {maxFlushMs:F1} ms max");
					ConfigDataSet set = ConfigDataSet.Load(root, "g", new[] { ("bench", s.Mode) }, long.MaxValue / 4);
					Assert.Equal(n, set.Sessions.Single().GetHops().Sum(h => h.Count));
					Directory.Move(dir, Path.Combine(root, "g-" + flushSec)); // keep the next round separate
				}
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}
	}
}
