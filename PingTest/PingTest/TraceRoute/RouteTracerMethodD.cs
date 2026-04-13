using PingTracer.Properties;
using PingTracer.Util;
using SmartPing;
using System;
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
		/// <param name="token">An object which should be passed through to the OnHostResult method.</param>
		/// <param name="Target">The target host to ping.</param>
		/// <param name="MaxHops">The maximum number of hops to try.</param>
		/// <param name="OnHostResult">Callback method which will be called with the result of each individual ping.</param>
		/// <param name="PingTimeoutMs">Timeout in milliseconds after which the ping should be considered unsuccessful.</param>
		public static Task TraceRoute(object token, IPAddress Target, byte MaxHops, Action<TraceRouteHostResult> OnHostResult, int PingTimeoutMs = 5000)
		{
			Task[] tasks = new Task[MaxHops];
			for (byte ttl = 1; ttl <= MaxHops; ttl++)
			{
				tasks[ttl - 1] = PingAsync(token, ttl, Target, MaxHops, OnHostResult, PingTimeoutMs);
			}
			return Task.WhenAll(tasks);
		}

		private static async Task PingAsync(object token, byte ttl, IPAddress Target, byte MaxHops, Action<TraceRouteHostResult> OnHostResult, int PingTimeoutMs)
		{
			PingOptions opt = new PingOptions(ttl, true);
			Ping ping = PingInstancePool.Get();
			try
			{
				PingReply reply = await ping.SendPingAsync(Target, PingTimeoutMs, PingBufferStatic.GetBuffer(PingPayloadSizeBytes), opt).ConfigureAwait(false);

				bool Success = reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired;
				IPAddress ReplyFrom = Success ? reply.Address : IPAddress.Any;

				OnHostResult(new TraceRouteHostResult(token, Success, reply.RoundtripTime, ReplyFrom, ttl, Target, MaxHops, PingTimeoutMs));
			}
			finally
			{
				PingInstancePool.Recycle(ping);
			}
		}
	}
}