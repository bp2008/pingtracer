using PingTracer.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace PingTracer.Tests
{
	/// <summary>
	/// Performance checks against a large synthetic session.  The file-generating test only runs when the environment
	/// variable PINGTRACER_GENERATE_DIR names a directory (used for manual testing of the history viewer).
	/// </summary>
	public class LargeSessionTests
	{
		private readonly ITestOutputHelper output;

		public LargeSessionTests(ITestOutputHelper output) { this.output = output; }

		private static MonitoringSession Generate(TimeSpan duration, int hops, long startUnixMs)
		{
			MonitoringSession s = new MonitoringSession(IPAddress.Parse("198.51.100.10"), "synthetic 3-day trace", SessionMode.TraceRoute, 1000, 4000, startUnixMs, readOnly: true);
			Random rnd = new Random(42);
			ushort[] addrA = new ushort[hops + 1], addrB = new ushort[hops + 1];
			for (int t = 1; t <= hops; t++)
			{
				addrA[t] = s.Addresses.GetOrAdd(t == hops ? s.TargetAddress : IPAddress.Parse("203.0.113." + t), out _);
				addrB[t] = s.Addresses.GetOrAdd(t == hops ? s.TargetAddress : IPAddress.Parse("192.0.2." + t), out _);
			}
			s.LoadRouteEvent(new RouteEvent(0, RouteEventKind.DestinationTtl, (byte)hops, 0, true));
			int seconds = (int)duration.TotalSeconds;
			for (int sec = 0; sec < seconds; sec++)
			{
				bool pathB = (sec / 3600) % 7 == 3; // one hour on the alternate path every 7 hours
				bool prevPathB = sec > 0 && ((sec - 1) / 3600) % 7 == 3;
				if (sec == 0 || pathB != prevPathB)
					for (int t = sec == 0 ? 1 : 6; t < hops; t++)
						if (t != 2)
							s.LoadRouteEvent(new RouteEvent(sec * 1000L, RouteEventKind.HopAddress, (byte)t, pathB && t >= 6 ? addrB[t] : addrA[t], sec == 0));
				double diurnal = 1 + 0.5 * Math.Sin(sec / 86400.0 * 2 * Math.PI);
				bool outage = sec % 20000 < 30;
				for (int t = 1; t <= hops; t++)
				{
					if (t == 2) { s.LoadRecord(t, new PingSample(sec * 1000L, PingRecordStatus.Lost, AddressTable.None)); continue; }
					bool lost = (outage && t > 4) || rnd.NextDouble() < 0.004 * diurnal;
					ushort addr = pathB && t >= 6 ? addrB[t] : addrA[t];
					ushort rtt = (ushort)Math.Min(PingRecordStatus.MaxRtt, (t * 3 + (pathB && t >= 6 ? 15 : 0)) * diurnal + rnd.Next(0, 6));
					s.LoadRecord(t, lost ? new PingSample(sec * 1000L, PingRecordStatus.Lost, AddressTable.None) : new PingSample(sec * 1000L, rtt, addr));
				}
			}
			return s;
		}

		[Fact]
		public void QueryOverThreeDaysIsFast()
		{
			long start = ServerClock.NowUnixMs - (long)TimeSpan.FromDays(3).TotalMilliseconds;
			Stopwatch sw = Stopwatch.StartNew();
			MonitoringSession s = Generate(TimeSpan.FromDays(3), 15, start);
			output.WriteLine($"Generated in {sw.ElapsedMilliseconds} ms");

			// Zoomed all the way out: 256 columns of 30 minutes each covers the whole session.
			int msPerCol = 1800000;
			sw.Restart();
			var r = s.Query(start / msPerCol, msPerCol, 256);
			long wide = sw.ElapsedMilliseconds;
			output.WriteLine($"3-day query ({r.Count} hops): {wide} ms");
			Assert.Equal(15, r.Count);
			Assert.True(wide < 2000, "3-day query took " + wide + " ms");

			// Typical chunk at a fine zoom: 256 columns of 1 second.
			sw.Restart();
			for (int i = 0; i < 100; i++)
				s.Query(start / 1000 + i * 1000, 1000, 256);
			output.WriteLine($"Fine chunk query: {sw.Elapsed.TotalMilliseconds / 100:0.00} ms");
		}

		[Fact]
		public void GenerateFileForManualTesting()
		{
			string dir = Environment.GetEnvironmentVariable("PINGTRACER_GENERATE_DIR");
			if (string.IsNullOrEmpty(dir))
				return;
			long start = ServerClock.NowUnixMs - (long)TimeSpan.FromDays(3).TotalMilliseconds;
			MonitoringSession s = Generate(TimeSpan.FromDays(3), 15, start);
			foreach (string path in ConfigDataSet.ImportSession(s, dir, "generated", s.HostKey))
				output.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");
		}
	}
}
