using PingTracer.Storage;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services.Pinging
{
	/// <summary>
	/// A fake network used for demonstrations and for testing the user interface without depending on real network conditions.
	/// Activated by entering a host name that begins with <c>sim:</c> (e.g. <c>sim:demo</c>).
	/// <para>The simulated path exhibits the behaviors that make traceroute monitoring hard to visualize:</para>
	/// <list type="bullet">
	/// <item>A hop that never answers (ICMP filtered).</item>
	/// <item>A hop that load-balances between two routers (ECMP), so its replying address alternates.</item>
	/// <item>A hop that rate-limits its ICMP replies (appears lossy while forwarding fine).</item>
	/// <item>A periodic route change to a different, one-hop-longer path.</item>
	/// <item>Periodic loss bursts, latency spikes, and brief outages.</item>
	/// </list>
	/// All addresses are from documentation ranges (RFC 5737), so they can never collide with a real network.
	/// </summary>
	public sealed class SimulatedNetwork : IPinger
	{
		public const string Prefix = "sim:";

		public static readonly IPAddress DestinationAddress = IPAddress.Parse("198.51.100.10");

		public static bool IsSimulatedHost(string host) => host != null && host.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

		private sealed class SimHop
		{
			public readonly IPAddress[] Addresses;
			public readonly double RttMs;
			public readonly double JitterMs;
			/// <summary>Probability that this hop does not send a TTL-expired reply (ICMP rate limiting).</summary>
			public readonly double ReplyLoss;
			public readonly bool Silent;

			public SimHop(string[] addresses, double rttMs, double jitterMs, double replyLoss = 0, bool silent = false)
			{
				Addresses = Array.ConvertAll(addresses, IPAddress.Parse);
				RttMs = rttMs;
				JitterMs = jitterMs;
				ReplyLoss = replyLoss;
				Silent = silent;
			}
		}

		private static readonly SimHop[] PathA = new[]
		{
			new SimHop(new[] { "192.0.2.1" }, 0.6, 0.3),
			new SimHop(new[] { "192.0.2.254" }, 0, 0, silent: true),
			new SimHop(new[] { "203.0.113.17" }, 8, 1.5),
			new SimHop(new[] { "203.0.113.33" }, 9, 2),
			new SimHop(new[] { "203.0.113.65", "203.0.113.66" }, 11, 2.5),
			new SimHop(new[] { "203.0.113.97" }, 14, 2),
			new SimHop(new[] { "203.0.113.129" }, 18, 3, replyLoss: 0.25),
			new SimHop(new[] { "198.51.100.193" }, 24, 2),
			new SimHop(new[] { "198.51.100.161" }, 25, 2),
			new SimHop(new[] { "198.51.100.129" }, 27, 3),
			new SimHop(new[] { "198.51.100.65" }, 28, 2),
			new SimHop(new[] { DestinationAddress.ToString() }, 29, 2),
		};

		private static readonly SimHop[] PathB = new[]
		{
			PathA[0], PathA[1], PathA[2], PathA[3], PathA[4], PathA[5], PathA[6],
			new SimHop(new[] { "192.0.2.201" }, 36, 3),
			new SimHop(new[] { "192.0.2.209" }, 39, 4),
			new SimHop(new[] { "192.0.2.217" }, 41, 3),
			new SimHop(new[] { "192.0.2.225" }, 43, 3),
			new SimHop(new[] { "198.51.100.65" }, 44, 3),
			new SimHop(new[] { DestinationAddress.ToString() }, 45, 3),
		};

		private static readonly Dictionary<IPAddress, string> Hostnames = new Dictionary<IPAddress, string>
		{
			{ IPAddress.Parse("192.0.2.1"), "gateway.home.example" },
			{ IPAddress.Parse("203.0.113.17"), "lo0.bras1.isp.example" },
			{ IPAddress.Parse("203.0.113.33"), "agg1.city.isp.example" },
			{ IPAddress.Parse("203.0.113.65"), "core1a.city.isp.example" },
			{ IPAddress.Parse("203.0.113.66"), "core1b.city.isp.example" },
			{ IPAddress.Parse("203.0.113.97"), "core2.region.isp.example" },
			{ IPAddress.Parse("203.0.113.129"), "edge1.ix.isp.example" },
			{ IPAddress.Parse("198.51.100.193"), "ae1.transit-a.example" },
			{ IPAddress.Parse("198.51.100.161"), "ae7.transit-a.example" },
			{ IPAddress.Parse("198.51.100.129"), "ae3.transit-a.example" },
			{ IPAddress.Parse("192.0.2.201"), "be10.transit-b.example" },
			{ IPAddress.Parse("192.0.2.209"), "be22.transit-b.example" },
			{ IPAddress.Parse("192.0.2.217"), "be31.transit-b.example" },
			{ IPAddress.Parse("198.51.100.65"), "dc-edge.hosting.example" },
			{ DestinationAddress, "www.service.example" },
		};

		/// <summary>Length of one full simulation period in seconds.</summary>
		private const int RoutePeriodSec = 300;
		/// <summary>Seconds into each period during which path B is used.</summary>
		private const int PathBStartSec = 180, PathBEndSec = 260;

		public async Task<PingOutcome> PingAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken cancellationToken)
		{
			long now = ServerClock.NowUnixMs;
			double sec = now / 1000.0;
			int phase = (int)(sec % RoutePeriodSec);
			SimHop[] path = phase >= PathBStartSec && phase < PathBEndSec ? PathB : PathA;
			Random rnd = Random.Shared;

			int reach = Math.Min(ttl, path.Length);
			bool isDestination = ttl >= path.Length;

			// Path-wide impairments.
			double forwardLoss = 0.002;
			double extraLatency = 0;
			if (sec % 97 < 6) forwardLoss = 0.3;            // loss burst
			if (sec % 293 < 3 && reach > 4) forwardLoss = 1; // brief outage beyond hop 4
			if (sec % 61 < 8 && reach >= 7) extraLatency = 60 + 40 * Math.Sin(sec); // congestion spike

			SimHop hop = path[reach - 1];
			bool lost = reach >= 5 && rnd.NextDouble() < forwardLoss;
			if (!isDestination)
			{
				if (hop.Silent || rnd.NextDouble() < hop.ReplyLoss)
					lost = true;
			}
			if (lost)
			{
				await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
				return PingOutcome.Lost;
			}

			double rtt = hop.RttMs + extraLatency + Math.Abs(Gaussian(rnd)) * hop.JitterMs;
			if (!isDestination && rnd.NextDouble() < 0.03)
				rtt += rnd.Next(5, 40); // routers answer TTL-expired on their slow path
			if (rtt > timeoutMs)
			{
				await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
				return PingOutcome.Lost;
			}
			if (rtt >= 20)
				await Task.Delay((int)rtt, cancellationToken).ConfigureAwait(false);
			else
				await Task.Yield();

			IPAddress from = hop.Addresses.Length == 1 ? hop.Addresses[0] : hop.Addresses[rnd.Next(hop.Addresses.Length)];
			return new PingOutcome(isDestination ? PingOutcomeKind.EchoReply : PingOutcomeKind.TtlExpired, from, (long)Math.Round(rtt));
		}

		public Task<string> ReverseLookupAsync(IPAddress address)
		{
			Hostnames.TryGetValue(address, out string name);
			return Task.FromResult(name);
		}

		private static double Gaussian(Random rnd)
		{
			double u1 = 1.0 - rnd.NextDouble();
			double u2 = rnd.NextDouble();
			return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
		}
	}
}
