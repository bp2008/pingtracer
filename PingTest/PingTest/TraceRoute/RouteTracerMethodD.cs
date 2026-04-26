using PingTracer.Properties;
using PingTracer.Util;
using SmartPing;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace PingTracer.TraceRoute
{
	/// <summary>
	/// Optimized TAP-based traceroute. Eliminates dynamic dispatch, anonymous type allocations,
	/// unnecessary Stopwatch allocations, and uses pre-allocated arrays.
	/// </summary>
	public static class RouteTracerMethodD
	{
		/// <summary>
		/// Payload size in bytes for pings sent by this method.  0 works on most systems, but some systems fail to get responses with an empty payload.  32 is the default size for the traceroute program on Windows.
		/// </summary>
		public static int PingPayloadSizeBytes = 32;

		/// <summary>
		/// Performs an asynchronous traceroute operation using the TAP pattern with minimal overhead.
		/// </summary>
		/// <param name="token">An object passed through unchanged to <paramref name="onHopDispatching"/> and <paramref name="onHostResult"/>.</param>
		/// <param name="target">The target host to ping.</param>
		/// <param name="maxHops">The maximum number of hops to try.</param>
		/// <param name="skipHops">Optional set of TTL values to skip dispatching for; null = ping all hops 1..maxHops. Skipped hops produce no callbacks.</param>
		/// <param name="onHopDispatching">Optional synchronous hook called immediately before each hop's <c>SendPingAsync</c>. Receives the TTL and the captured send timestamp (UTC).</param>
		/// <param name="onHostResult">Callback invoked once per dispatched hop with the result.</param>
		/// <param name="pingTimeoutMs">Timeout in milliseconds after which the ping is considered unsuccessful.</param>
		public static Task TraceRoute(
			object token,
			IPAddress target,
			byte maxHops,
			HashSet<byte> skipHops,
			Action<byte, DateTime> onHopDispatching,
			Action<TraceRouteHostResult> onHostResult,
			int pingTimeoutMs = 5000)
		{
			Task[] tasks = new Task[maxHops];
			int taskCount = 0;
			for (byte ttl = 1; ttl <= maxHops; ttl++)
			{
				if (skipHops != null && skipHops.Contains(ttl))
					continue;
				tasks[taskCount++] = PingAsync(token, ttl, target, maxHops, onHopDispatching, onHostResult, pingTimeoutMs);
			}
			if (taskCount == tasks.Length)
				return Task.WhenAll(tasks);
			Task[] trimmed = new Task[taskCount];
			Array.Copy(tasks, trimmed, taskCount);
			return Task.WhenAll(trimmed);
		}

		private static async Task PingAsync(
			object token,
			byte ttl,
			IPAddress target,
			byte maxHops,
			Action<byte, DateTime> onHopDispatching,
			Action<TraceRouteHostResult> onHostResult,
			int pingTimeoutMs)
		{
			PingOptions opt = new PingOptions(ttl, true);
			Ping ping = PingInstancePool.Get();
			DateTime sentUtc = DateTime.UtcNow;
			try
			{
				onHopDispatching?.Invoke(ttl, sentUtc);
				PingReply reply = await ping.SendPingAsync(target, pingTimeoutMs, PingBufferStatic.GetBuffer(PingPayloadSizeBytes), opt).ConfigureAwait(false);

				bool success = reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired;
				IPAddress replyFrom = success ? reply.Address : IPAddress.Any;

				onHostResult(new TraceRouteHostResult(token, success, reply.RoundtripTime, replyFrom, ttl, target, maxHops, pingTimeoutMs, sentUtc));
			}
			finally
			{
				PingInstancePool.Recycle(ping);
			}
		}
	}
}
