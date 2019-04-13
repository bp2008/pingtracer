using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using SmartPing;
using System.Text;

namespace PingTracer.TraceRoute
{
	public static class RouteTracerMethodC
	{
		/// <summary>
		/// Performs an asynchronous, multi-threaded traceroute operation.
		/// </summary>
		/// <param name="threadPool">A thread pool upon which to execute the pings. It should contain as many threads as the <see cref="MaxHops"/> value.</param>
		/// <param name="token">An object which should be passed through to the OnHostResult method.</param>
		/// <param name="Target">The target host to ping.</param>
		/// <param name="MaxHops">The maximum number of hops to try.</param>
		/// <param name="OnHostResult">Callback method which will be called with the result of each individual ping.</param>
		/// <param name="PingTimeoutMs">Timeout in milliseconds after which the ping should be considered unsuccessful.</param>
		public static void TraceRoute(SimpleThreadPool threadPool, object token, IPAddress Target, byte MaxHops, Action<TraceRouteHostResult> OnHostResult, int PingTimeoutMs = 5000)
		{
			byte[] buffer = new byte[0];
			for (byte ttl = 1; ttl <= MaxHops; ttl++)
			{
				object state = new
				{
					token,
					ttl,
					Target,
					MaxHops,
					OnHostResult,
					PingTimeoutMs,
					buffer
				};
				threadPool.Enqueue(() =>
				{
					PingSync(state);
				});
			}
		}

		private static void PingSync(dynamic state)
		{
			object token = state.token;
			byte ttl = state.ttl;
			IPAddress Target = state.Target;
			byte MaxHops = state.MaxHops;
			Action<TraceRouteHostResult> OnHostResult = state.OnHostResult;
			int PingTimeoutMs = state.PingTimeoutMs;
			byte[] buffer = state.buffer;

			PingOptions opt = new PingOptions(ttl, true);
			Ping ping = PingInstancePool.Get();
			Stopwatch sw = new Stopwatch();
			sw.Start();
			PingReply reply = ping.Send(Target, PingTimeoutMs, buffer, opt);
			sw.Stop();

			bool Success = reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired;
			long RoundTripTime = reply.RoundtripTime * 10000 + sw.ElapsedMilliseconds;
			IPAddress ReplyFrom = Success ? reply.Address : IPAddress.Any;

			PingInstancePool.Recycle(ping);

			TraceRouteHostResult result = new TraceRouteHostResult(token, Success, RoundTripTime, ReplyFrom, ttl, Target, MaxHops, PingTimeoutMs);
			OnHostResult(result);
		}
	}
}
