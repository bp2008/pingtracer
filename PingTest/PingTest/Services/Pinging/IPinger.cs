using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services.Pinging
{
	public enum PingOutcomeKind : byte
	{
		/// <summary>No usable reply (timeout, unreachable, or error).</summary>
		Lost = 0,
		/// <summary>An echo reply from the destination itself.</summary>
		EchoReply = 1,
		/// <summary>A "time exceeded" reply from an intermediate router.</summary>
		TtlExpired = 2,
	}

	public readonly struct PingOutcome
	{
		public readonly PingOutcomeKind Kind;
		public readonly IPAddress From;
		public readonly long RttMs;

		public PingOutcome(PingOutcomeKind kind, IPAddress from, long rttMs)
		{
			Kind = kind;
			From = from;
			RttMs = rttMs;
		}

		public bool IsReply => Kind != PingOutcomeKind.Lost;

		public static readonly PingOutcome Lost = new PingOutcome(PingOutcomeKind.Lost, null, 0);

		public override string ToString() => Kind + " " + From + " " + RttMs + "ms";
	}

	/// <summary>
	/// Sends a single ICMP echo request.  Implementations must be safe for concurrent use.
	/// </summary>
	public interface IPinger
	{
		Task<PingOutcome> PingAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken cancellationToken);

		/// <summary>Resolves a reverse-DNS hostname for an address, or returns null.</summary>
		Task<string> ReverseLookupAsync(IPAddress address);
	}
}
