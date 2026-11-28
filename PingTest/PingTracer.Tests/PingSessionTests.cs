using PingTracer.Services;
using PingTracer.Storage;
using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
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

		/// <summary>Answers every ping with an echo reply from the address pinged, recording where each ping went.</summary>
		private sealed class EchoPinger : PingTracer.Services.Pinging.IPinger
		{
			public readonly List<IPAddress> SentTo = new List<IPAddress>();

			public Task<PingTracer.Services.Pinging.PingOutcome> PingAsync(IPAddress target, int ttl, int timeoutMs, System.Threading.CancellationToken ct)
			{
				lock (SentTo) SentTo.Add(target);
				return Task.FromResult(new PingTracer.Services.Pinging.PingOutcome(PingTracer.Services.Pinging.PingOutcomeKind.EchoReply, target, 5));
			}

			public Task<string> ReverseLookupAsync(IPAddress address) => Task.FromResult<string>(null);

			public bool HasPinged(IPAddress a) { lock (SentTo) return SentTo.Contains(a); }
		}

		[Fact]
		public async Task RefreshTargetsFollowsTheHostNameToANewAddress()
		{
			IPAddress a = IPAddress.Parse("198.51.100.1"), b = IPAddress.Parse("198.51.100.2");
			IPAddress answer = a;
			List<IPAddress> currentSeen = new List<IPAddress>();
			PingSession run = Create("name.test");
			EchoPinger pinger = new EchoPinger();
			run.PingerFactory = _ => pinger;
			run.HostResolver = (host, current) =>
			{
				lock (currentSeen) currentSeen.Add(current);
				if (answer == null) throw new Exception("DNS is down");
				return (answer, host + " [" + answer + "]");
			};
			List<string> log = new List<string>();
			run.Log += m => { lock (log) log.Add(m); };
			Assert.True(run.Start());
			try
			{
				MonitoringSession s = Assert.Single(run.Sessions);
				Assert.True(SpinWait.SpinUntil(() => pinger.HasPinged(a), 2000));

				// The same answer, or no answer at all, changes nothing.
				run.RefreshTargets();
				answer = null;
				run.RefreshTargets();
				Assert.Equal(a, s.TargetAddress);
				Assert.DoesNotContain(s.GetRouteEvents(), e => e.Kind == RouteEventKind.Target);

				answer = b;
				run.RefreshTargets();
				Assert.Equal(b, s.TargetAddress);
				Assert.Equal("name.test [198.51.100.2]", s.DisplayName);
				Assert.Equal(new[] { a, b }, s.GetTargets());
				RouteEvent change = Assert.Single(s.GetRouteEvents(), e => e.Kind == RouteEventKind.Target);
				Assert.Equal(b, s.Addresses.GetAddress(change.Value));
				lock (log) Assert.Contains(log, m => m.Contains("name.test now resolves to 198.51.100.2 (was 198.51.100.1)"));
				Assert.True(SpinWait.SpinUntil(() => pinger.HasPinged(b), 2000), "the monitor pings the new address");
				// Each lookup was told which address is pinged now, so it can keep it while the name still resolves to it.
				lock (currentSeen) Assert.Equal(new IPAddress[] { null, a, a, a }, currentSeen);
			}
			finally
			{
				await run.StopAsync();
			}
		}

		[Fact]
		public async Task RefreshTargetsSkipsHostsGivenAsAddresses()
		{
			PingSession run = Create("192.0.2.7");
			EchoPinger pinger = new EchoPinger();
			run.PingerFactory = _ => pinger;
			int lookups = 0;
			run.HostResolver = (host, current) => { lookups++; return (IPAddress.Parse(host), host); };
			Assert.True(run.Start());
			try
			{
				run.RefreshTargets();
				Assert.Equal(1, lookups); // only the one at Start
			}
			finally
			{
				await run.StopAsync();
			}
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

		[Fact]
		public void LossLogIsSummarizedPerWindow()
		{
			PingSession.LossTracker t = new PingSession.LossTracker(ms => "t" + ms);
			const int W = PingSession.LossTracker.WindowMs;
			// Out-of-order arrival: losses report at their timeout, after newer replies.  No line until the window closes.
			Assert.Null(t.Add(false, 100, 0));
			Assert.Null(t.Add(true, 200, 10));
			Assert.Null(t.Add(false, 300, 20));
			Assert.Null(t.Add(true, 150, 30));
			for (int i = 0; i < 96; i++)
				Assert.Null(t.Add(false, 400 + i, 40 + i));
			// The first result after the window closes reports it.
			Assert.Equal("2 of 100 pings lost (2%) from t150 to t200", t.Add(false, 1000, W));
			// A window with a single loss, then one where everything was lost.
			Assert.Null(t.Add(true, 2000, W + 1));
			Assert.Equal("ping lost at t2000", t.Add(true, 3000, 2 * W + 5));
			Assert.Null(t.Add(true, 3500, 2 * W + 6));
			Assert.Equal("all 2 pings lost from t3000 to t3500", t.Flush());
			// Windows without loss log nothing; a tiny fraction is shown as <1%.
			Assert.Null(t.Flush());
			t.Add(true, 5000, 0);
			for (int i = 0; i < 200; i++) t.Add(false, 5001 + i, 1);
			Assert.Equal("ping lost at t5000", t.Flush());
			t.Add(true, 6000, 0); t.Add(true, 6001, 0);
			for (int i = 0; i < 300; i++) t.Add(false, 6002 + i, 1);
			Assert.Equal("2 of 302 pings lost (<1%) from t6000 to t6001", t.Flush());
		}

		private static bool WindowsDnsReachable()
		{
			try { return System.Net.Dns.GetHostAddresses("ipv4.google.com").Length > 0; }
			catch { return false; }
		}
	}
}
