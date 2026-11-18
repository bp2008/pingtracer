using PingTracer.Services;
using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PingTracer.Tests
{
	public class PingSessionTests
	{
		private static PingSession Create(params string[] hosts)
		{
			PingConfiguration cfg = new PingConfiguration
			{
				hosts = hosts.Select(h => new Host { hostname = h }).ToList(),
				rate = 10,
				pingsPerSecond = true,
				doTraceRoute = false,
				reverseDnsLookup = false,
			};
			Settings settings = new Settings { saveSessionData = false };
			return new PingSession(cfg, settings);
		}

		[Fact]
		public async Task UnresolvableHostDoesNotStopTheOthersAndIsRetried()
		{
			PingSession run = Create("sim:demo", "no-such-host.invalid");
			List<string> log = new List<string>();
			run.Log += m => { lock (log) log.Add(m); };
			Assert.True(run.Start());
			try
			{
				Assert.True(run.IsRunning);
				Assert.Single(run.Sessions);
				Assert.Equal("sim:demo", run.Sessions[0].DisplayName);
				lock (log)
				{
					Assert.Contains(log, m => m.StartsWith("Unable to resolve \"no-such-host.invalid\""));
					Assert.Contains(log, m => m.Contains("keep trying to resolve no-such-host.invalid"));
				}
			}
			finally
			{
				await run.StopAsync();
			}
			Assert.False(run.IsRunning);
		}

		[Fact]
		public void NoResolvableHostFailsToStart()
		{
			PingSession run = Create("no-such-host.invalid");
			bool stopped = false;
			run.Stopped += () => stopped = true;
			Assert.False(run.Start());
			Assert.False(run.IsRunning);
			Assert.True(stopped);
		}

		[Fact]
		public void HostWithOnlyIPv6AddressesResolvesWithoutIPv6Connectivity()
		{
			// Needs DNS access; Windows hides AAAA records from the normal resolver on computers without IPv6 internet.
			System.Net.IPAddress ip;
			try { ip = PingSession.ResolveHost("ipv6.google.com", preferIPv4: true, out _); }
			catch (Exception ex) when (ex.Message.Contains("Unable to resolve"))
			{
				// No DNS at all (offline test machine): nothing to check.
				if (WindowsDnsReachable()) throw;
				return;
			}
			Assert.Equal(System.Net.Sockets.AddressFamily.InterNetworkV6, ip.AddressFamily);
		}

		private static bool WindowsDnsReachable()
		{
			try { return System.Net.Dns.GetHostAddresses("ipv4.google.com").Length > 0; }
			catch { return false; }
		}
	}
}
