using SmartPing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;

namespace PingTracer.TraceRoute
{
	public static class RouteTracerMethodA
	{
		/// <summary>
		/// Performs an asynchronous, multi-threaded traceroute operation.
		/// </summary>
		/// <param name="token">An object which should be passed through to the OnHostResult method.</param>
		/// <param name="Target">The target host to ping.</param>
		/// <param name="MaxHops">The maximum number of hops to try.</param>
		/// <param name="OnHostResult">Callback method which will be called with the result of each individual ping.</param>
		/// <param name="PingTimeoutMs">Timeout in milliseconds after which the ping should be considered unsuccessful.</param>
		public static void TraceRoute(object token, IPAddress Target, byte MaxHops, Action<TraceRouteHostResult> OnHostResult, int PingTimeoutMs = 5000)
		{
			byte[] buffer = new byte[0];
			for (byte ttl = 1; ttl <= MaxHops; ttl++)
			{
				PingOptions opt = new PingOptions(ttl, true);
				Ping ping = PingInstancePool.Get();
				ping.PingCompleted += Ping_PingCompleted;
				Stopwatch sw = new Stopwatch();
				sw.Start();
				ping.SendAsync(Target, PingTimeoutMs, buffer, opt, new
				{
					sw,
					token,
					ttl,
					Target,
					MaxHops,
					OnHostResult,
					PingTimeoutMs
				});
			}
		}

		private static void Ping_PingCompleted(object sender, PingCompletedEventArgs e)
		{
			dynamic state = e.UserState;
			Stopwatch sw = state.sw;
			sw.Stop();
			object token = state.token;
			byte ttl = state.ttl;
			IPAddress Target = state.Target;
			byte MaxHops = state.MaxHops;
			Action<TraceRouteHostResult> OnHostResult = state.OnHostResult;
			int PingTimeoutMs = state.PingTimeoutMs;

			bool Success = !e.Cancelled && (e.Reply.Status == IPStatus.Success || e.Reply.Status == IPStatus.TtlExpired);
			long RoundTripTime = e.Reply.RoundtripTime;
			IPAddress ReplyFrom = Success ? e.Reply.Address : IPAddress.Any;

			((Ping)sender).PingCompleted -= Ping_PingCompleted;
			PingInstancePool.Recycle((Ping)sender);

			TraceRouteHostResult result = new TraceRouteHostResult(token, Success, RoundTripTime, ReplyFrom, ttl, Target, MaxHops, PingTimeoutMs);
			OnHostResult(result);
		}
	}
}
