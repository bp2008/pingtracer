using PingTracer.Util;
using SmartPing;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services.Pinging
{
	/// <summary>
	/// Real ICMP pinger built on SmartPing (which, unlike System.Net.NetworkInformation.Ping on .NET 10,
	/// reports accurate round trip times for "TTL expired" replies).
	/// </summary>
	public sealed class SmartPinger : IPinger
	{
		private readonly byte[] payload;

		public SmartPinger(int payloadSizeBytes)
		{
			payload = new byte[Math.Clamp(payloadSizeBytes, 0, 65500)];
		}

		public async Task<PingOutcome> PingAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken cancellationToken)
		{
			Ping ping = PingInstancePool.Get();
			try
			{
				PingOptions options = new PingOptions(ttl, true);
				PingReply reply = await ping.SendPingAsync(target, timeoutMs, payload, options).ConfigureAwait(false);
				switch (reply.Status)
				{
					case IPStatus.Success:
						return new PingOutcome(PingOutcomeKind.EchoReply, reply.Address ?? target, reply.RoundtripTime);
					case IPStatus.TtlExpired:
					case IPStatus.TimeExceeded:
						if (reply.Address != null && reply.Address.Equals(target))
							return new PingOutcome(PingOutcomeKind.EchoReply, reply.Address, reply.RoundtripTime);
						return new PingOutcome(PingOutcomeKind.TtlExpired, reply.Address, reply.RoundtripTime);
					default:
						return PingOutcome.Lost;
				}
			}
			catch
			{
				return PingOutcome.Lost;
			}
			finally
			{
				PingInstancePool.Recycle(ping);
			}
		}

		public async Task<string> ReverseLookupAsync(IPAddress address)
		{
			try
			{
				IPHostEntry entry = await Dns.GetHostEntryAsync(address).ConfigureAwait(false);
				return entry?.HostName;
			}
			catch
			{
				return null;
			}
		}
	}
}
