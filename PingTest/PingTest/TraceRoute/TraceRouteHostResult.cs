using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace PingTracer.TraceRoute
{
	public class TraceRouteHostResult
	{
		/// <summary>
		/// Token originally provided to RouteTracer.TraceRoute()
		/// </summary>
		public object token;
		/// <summary>
		/// If true, the ping recieved a response.
		/// </summary>
		public bool success;
		/// <summary>
		/// The round-trip-time of the ping, in milliseconds (only if <see cref="success"/>).
		/// </summary>
		public long roundTripTime;
		/// <summary>
		/// The address a reply was received from (only if <see cref="success"/>).
		/// </summary>
		public IPAddress replyFrom;
		/// <summary>
		/// The ttl value which was sent with this ping.
		/// </summary>
		public byte ttl;
		/// <summary>
		/// The destination host of the ping (may differ from the <see cref="replyFrom"/> address).
		/// </summary>
		public IPAddress target;
		/// <summary>
		/// Maximum ttl value which was used for the traceroute operation.
		/// </summary>
		public byte maxHops;
		/// <summary>
		/// Number of milliseconds after which the ping was instructed to time out.
		/// </summary>
		public int pingTimeoutMs;

		public TraceRouteHostResult(object token, bool success, long roundTripTime, IPAddress replyFrom, byte ttl, IPAddress target, byte maxHops, int pingTimeoutMs)
		{
			this.token = token;
			this.success = success;
			this.roundTripTime = roundTripTime;
			this.replyFrom = replyFrom;
			this.ttl = ttl;
			this.target = target;
			this.maxHops = maxHops;
			this.pingTimeoutMs = pingTimeoutMs;
		}
	}
}
