using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace PingTracer.TraceRoute
{
	public static class RouteTracerMethodB
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
			List<Task> tasks = new List<Task>();
			byte[] buffer = new byte[0];
			for (byte ttl = 1; ttl <= MaxHops; ttl++)
			{
				tasks.Add(PingAsync(new
				{
					token,
					ttl,
					Target,
					MaxHops,
					OnHostResult,
					PingTimeoutMs,
					buffer
				}));
			}
			Task.WaitAll(tasks.ToArray());
		}

		private static async Task PingAsync(dynamic state)
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
			PingReply reply = await ping.SendPingAsync(Target, PingTimeoutMs, buffer, opt);
			sw.Stop();

			bool Success = reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired;
			long RoundTripTime = reply.RoundtripTime;
			IPAddress ReplyFrom = Success ? reply.Address : IPAddress.Any;

			PingInstancePool.Recycle(ping);

			TraceRouteHostResult result = new TraceRouteHostResult(token, Success, RoundTripTime, ReplyFrom, ttl, Target, MaxHops, PingTimeoutMs);
			OnHostResult(result);
		}
		private class PingTask
		{
			public object State;
			public Task Task;

			public PingTask(object state, Task task)
			{
				State = state;
				Task = task;
			}
		}
	}
}
