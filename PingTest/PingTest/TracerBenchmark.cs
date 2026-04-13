using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PingTracer.TraceRoute;

namespace PingTracer
{
	/// <summary>
	/// Benchmarks the four RouteTracerMethod implementations (A, B, C, D) by running
	/// repeated traceroutes to a local target and measuring CPU time, wall time,
	/// memory allocations, and GC collections.
	/// 
	/// Run with command line argument: benchmark [targetIP]
	/// Example: benchmark 10.30.0.1
	/// </summary>
	public static class TracerBenchmark
	{
		private const byte MaxHops = 40;
		private const int Iterations = 400;
		private const int PingTimeoutMs = 2000;

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AllocConsole();

		public static async Task RunAsync(IPAddress target)
		{
			AllocConsole();

			Console.WriteLine("============================================================");
			Console.WriteLine("  Traceroute Method Benchmark");
			Console.WriteLine("============================================================");
			Console.WriteLine($"  Target:       {target}");
			Console.WriteLine($"  MaxHops:      {MaxHops}");
			Console.WriteLine($"  Iterations:   {Iterations}");
			Console.WriteLine($"  PingTimeout:  {PingTimeoutMs}ms");
			Console.WriteLine($"  Total pings:  {MaxHops * Iterations} per method");
			Console.WriteLine();

			Console.Write("  Warming up (JIT + ping pool priming)...");
			await WarmupAll(target);
			Console.WriteLine(" done.");
			Console.WriteLine();

			BenchmarkResult[] results = new BenchmarkResult[4];

			results[0] = await RunBenchmark("A (EAP/SendAsync)", () =>
			{
				TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				int remaining = MaxHops;
				RouteTracerMethodA.TraceRoute(null, target, MaxHops, _ =>
				{
					if (Interlocked.Decrement(ref remaining) <= 0)
						tcs.TrySetResult(true);
				}, PingTimeoutMs);
				return tcs.Task;
			});

			results[1] = await RunBenchmark("B (TAP/SendPingAsync)", () =>
				RouteTracerMethodB.TraceRoute(null, target, MaxHops, _ => { }, PingTimeoutMs));

			SimpleThreadPool pool = new SimpleThreadPool("Benchmark", MaxHops, MaxHops);
			results[2] = await RunBenchmark("C (Sync/ThreadPool)", () =>
			{
				TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				int remaining = MaxHops;
				RouteTracerMethodC.TraceRoute(pool, null, target, MaxHops, _ =>
				{
					if (Interlocked.Decrement(ref remaining) <= 0)
						tcs.TrySetResult(true);
				}, PingTimeoutMs);
				return tcs.Task;
			});
			pool.Stop();

			results[3] = await RunBenchmark("D (Optimized TAP)", () =>
				RouteTracerMethodD.TraceRoute(null, target, MaxHops, _ => { }, PingTimeoutMs));

			Console.WriteLine();
			Console.WriteLine("============================================================");
			Console.WriteLine("  SUMMARY");
			Console.WriteLine("============================================================");
			Console.WriteLine();
			Console.WriteLine("  {0,-25} {1,10} {2,10} {3,17} {4,6} {5,6} {6,6}",
				"Method", "Wall(ms)", "CPU(ms)", "Allocated(bytes)", "Gen0", "Gen1", "Gen2");
			Console.WriteLine("  " + new string('-', 86));

			foreach (BenchmarkResult r in results.Where(r => r != null))
			{
				Console.WriteLine("  {0,-25} {1,10:F1} {2,10:F1} {3,17:N0} {4,6} {5,6} {6,6}",
					r.Name,
					r.WallTime.TotalMilliseconds,
					r.CpuTime.TotalMilliseconds,
					r.AllocatedBytes,
					r.Gen0Collections,
					r.Gen1Collections,
					r.Gen2Collections);
			}

			Console.WriteLine();
			Console.WriteLine("  * Lower is better for all metrics.");
			Console.WriteLine("  * Wall time includes network I/O (same for all methods).");
			Console.WriteLine("  * CPU time measures only processor usage (overhead differences).");
			Console.WriteLine("  * Allocated bytes via GC.GetTotalAllocatedBytes (cumulative, all threads).");
			Console.WriteLine();
			Console.WriteLine("  Press any key to exit...");
			Console.ReadKey(true);
		}

		private static async Task WarmupAll(IPAddress target)
		{
			// Method A
			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			int remaining = MaxHops;
			RouteTracerMethodA.TraceRoute(null, target, MaxHops, _ =>
			{
				if (Interlocked.Decrement(ref remaining) <= 0) tcs.TrySetResult(true);
			}, PingTimeoutMs);
			await tcs.Task;

			// Method B
			await RouteTracerMethodB.TraceRoute(null, target, MaxHops, _ => { }, PingTimeoutMs);

			// Method C
			SimpleThreadPool pool = new SimpleThreadPool("Warmup", MaxHops, MaxHops);
			tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			remaining = MaxHops;
			RouteTracerMethodC.TraceRoute(pool, null, target, MaxHops, _ =>
			{
				if (Interlocked.Decrement(ref remaining) <= 0) tcs.TrySetResult(true);
			}, PingTimeoutMs);
			await tcs.Task;
			pool.Stop();

			// Method D
			await RouteTracerMethodD.TraceRoute(null, target, MaxHops, _ => { }, PingTimeoutMs);

			ForceGC();
		}

		private static async Task<BenchmarkResult> RunBenchmark(string name, Func<Task> iteration)
		{
			ForceGC();
			Thread.Sleep(200); // Let system settle

			long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
			int gen0Before = GC.CollectionCount(0);
			int gen1Before = GC.CollectionCount(1);
			int gen2Before = GC.CollectionCount(2);
			TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
			Stopwatch sw = Stopwatch.StartNew();

			for (int i = 0; i < Iterations; i++)
			{
				await iteration().ConfigureAwait(false);
			}

			sw.Stop();
			TimeSpan cpuUsed = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
			long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

			BenchmarkResult result = new BenchmarkResult
			{
				Name = name,
				WallTime = sw.Elapsed,
				CpuTime = cpuUsed,
				AllocatedBytes = allocated,
				Gen0Collections = GC.CollectionCount(0) - gen0Before,
				Gen1Collections = GC.CollectionCount(1) - gen1Before,
				Gen2Collections = GC.CollectionCount(2) - gen2Before,
			};

			Console.WriteLine("  {0,-25} {1,8:F1}ms wall | {2,6:F1}ms CPU | {3,14:N0} bytes",
				name, sw.Elapsed.TotalMilliseconds, cpuUsed.TotalMilliseconds, allocated);

			return result;
		}

		private static void ForceGC()
		{
			GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
			GC.WaitForPendingFinalizers();
			GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
		}

		private class BenchmarkResult
		{
			public string Name;
			public TimeSpan WallTime;
			public TimeSpan CpuTime;
			public long AllocatedBytes;
			public int Gen0Collections;
			public int Gen1Collections;
			public int Gen2Collections;
		}
	}
}