using PingTracer.Services.Pinging;
using PingTracer.Storage;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services
{
	public sealed class RouteMonitorOptions
	{
		/// <summary>If true, every TTL up to the destination is pinged each cycle. If false, only the destination is pinged.</summary>
		public bool TraceRoute = true;
		public int IntervalMs = 1000;
		public int TimeoutMs = 4000;
		/// <summary>Largest TTL that will ever be probed.</summary>
		public int MaxHops = 30;
		/// <summary>TTL used for direct pings.</summary>
		public int DirectTtl = 128;
	}

	/// <summary>
	/// Continuously monitors one target.  Once per interval it dispatches one "cycle": in traceroute mode, a ping to every TTL
	/// from 1 to the destination's hop number, all sent at the same moment; in direct mode, a single ping to the destination.
	/// Cycles may overlap (a cycle lasts until its slowest ping times out), which keeps the sampling cadence exact.
	/// <para>
	/// Every ping is recorded in the session at send time as a pending record and completed in place when its result arrives,
	/// so results can arrive in any order.  Route length is tracked continuously:
	/// <list type="bullet">
	/// <item>An echo reply from the destination at a lower TTL than expected shortens the route immediately.</item>
	/// <item>A "TTL expired" reply at the expected destination TTL means the route got longer; the next cycles scan further.</item>
	/// <item>If the destination stops replying, extra TTLs are periodically probed in case it moved further away.</item>
	/// <item>Records at TTLs beyond the destination (which can only echo the destination's own reply) are voided.</item>
	/// </list>
	/// </para>
	/// </summary>
	public sealed class RouteMonitor : IDisposable
	{
		private const int ExtensionTtls = 6;
		private const int ExtensionCycles = 5;
		private const int ConfirmLengthenCycles = 3;
		private const int SilentAfterUnansweredPings = 10;
		private const long SilentAfterNoReplyMs = 5 * 60 * 1000;
		private const int SilentProbeEveryNCycles = 5;

		public readonly MonitoringSession Session;
		private readonly IPinger pinger;
		private readonly RouteMonitorOptions options;
		private readonly IPAddress target;

		private CancellationTokenSource cts;
		private Task loopTask;
		private PeriodicTimer timer;
		private int inFlight;
		/// <summary>Cycles with pings still in flight.  Guarded by Session.DataLock.</summary>
		private readonly List<Cycle> activeCycles = new List<Cycle>();
		/// <summary>Set (under Session.DataLock) when monitoring stops; results arriving later are ignored.</summary>
		private bool stopped;
		/// <summary>Guards Start against StopAsync; set once StopAsync has been called (a later Start does nothing).</summary>
		private readonly object lifecycleLock = new object();
		private bool stopCalled;
		private volatile int intervalMs;

		// State below is guarded by Session.DataLock.
		private long cycleNumber;
		private int consecutiveMisses;
		private int consecutiveBeyond;
		private int extensionCyclesLeft;
		private int highestReplyTtl;
		private bool discoveryLogged;

		/// <summary>Human-readable log messages about route changes.</summary>
		public event Action<string> Log;

		public RouteMonitor(MonitoringSession session, IPinger pinger, RouteMonitorOptions options)
		{
			Session = session ?? throw new ArgumentNullException(nameof(session));
			this.pinger = pinger ?? throw new ArgumentNullException(nameof(pinger));
			this.options = options ?? new RouteMonitorOptions();
			this.options.MaxHops = Math.Clamp(this.options.MaxHops, 1, 255);
			this.options.TimeoutMs = Math.Clamp(this.options.TimeoutMs, 100, 60000);
			target = session.TargetAddress;
			intervalMs = Math.Max(50, this.options.IntervalMs);
		}

		public bool IsRunning => loopTask != null && !loopTask.IsCompleted;

		public int InFlight => Volatile.Read(ref inFlight);

		public int IntervalMs
		{
			get => intervalMs;
			set
			{
				int v = Math.Max(50, value);
				intervalMs = v;
				Session.SetIntervalMs(v);
				PeriodicTimer t = timer;
				if (t != null)
				{
					try { t.Period = TimeSpan.FromMilliseconds(v); } catch (ObjectDisposedException) { }
				}
			}
		}

		/// <summary>Starts monitoring.  Does nothing if already started, or if <see cref="StopAsync"/> was called first.</summary>
		public void Start()
		{
			lock (lifecycleLock)
			{
				if (loopTask != null || stopCalled)
					return;
				if (options.TraceRoute == false)
					Session.EnsureAddress(target);
				cts = new CancellationTokenSource();
				timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
				Session.SetIntervalMs(intervalMs);
				Session.SetRunning(true);
				CancellationToken token = cts.Token;
				loopTask = Task.Run(() => LoopAsync(token));
			}
		}

		private async Task LoopAsync(CancellationToken ct)
		{
			try
			{
				RunCycle(ct);
				while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
					RunCycle(ct);
			}
			catch (OperationCanceledException) { }
			catch (ObjectDisposedException) { }
		}

		/// <summary>
		/// Stops dispatching new cycles.  Pings still in flight are not waited for (that would take up to the ping timeout):
		/// once monitoring stops their outcome is unknown, so their records are voided and late results are ignored.
		/// </summary>
		public async Task StopAsync()
		{
			CancellationTokenSource c;
			lock (lifecycleLock)
			{
				stopCalled = true;
				c = cts;
			}
			if (c == null)
				return;
			c.Cancel();
			try { if (loopTask != null) await loopTask.ConfigureAwait(false); } catch { }
			timer?.Dispose();
			AbandonInFlight();
			Session.SetRunning(false);
		}

		private void AbandonInFlight()
		{
			lock (Session.DataLock)
			{
				stopped = true;
				foreach (Cycle cycle in activeCycles)
				{
					foreach ((int ttl, RecordHandle h) in cycle.Dispatched)
					{
						HopSeries hop = Session.GetHop(ttl);
						if (hop != null && hop.TryGet(h, out PingSample sample) && sample.Rtt == PingRecordStatus.Pending)
							Session.VoidPing(ttl, h);
					}
					cycle.MarkComplete();
				}
				activeCycles.Clear();
			}
		}

		public void Dispose()
		{
			try { cts?.Cancel(); } catch { }
			timer?.Dispose();
			Session.SetRunning(false);
		}

		/// <summary>
		/// Dispatches one cycle.  Public so tests can drive the monitor deterministically.
		/// </summary>
		public Cycle RunCycle(CancellationToken ct = default)
		{
			Cycle cycle;
			List<int> ttls;
			lock (Session.DataLock)
			{
				cycle = new Cycle(++cycleNumber) { Discovery = Session.DestinationTtl == 0 };
				ttls = options.TraceRoute ? ChooseTraceTtls() : new List<int> { 0 };
				cycle.Remaining = ttls.Count;
				foreach (int ttl in ttls)
				{
					RecordHandle h = Session.BeginPing(ttl);
					cycle.Dispatched.Add((ttl, h));
				}
				if (cycle.Remaining == 0)
					cycle.MarkComplete();
				else
					activeCycles.Add(cycle);
			}
			for (int i = 0; i < cycle.Dispatched.Count; i++)
			{
				(int ttl, RecordHandle h) = cycle.Dispatched[i];
				_ = PingOneAsync(cycle, ttl, h, ct);
			}
			return cycle;
		}

		private List<int> ChooseTraceTtls()
		{
			int dest = Session.DestinationTtl;
			int scanMax;
			if (dest > 0)
			{
				scanMax = dest;
				if (extensionCyclesLeft == 0 && consecutiveMisses >= 5 && consecutiveMisses % 20 == 5)
					extensionCyclesLeft = ExtensionCycles;
				if (extensionCyclesLeft > 0)
				{
					extensionCyclesLeft--;
					scanMax = dest + ExtensionTtls;
				}
				if (consecutiveMisses >= 60 && consecutiveMisses % 60 == 0)
					scanMax = options.MaxHops;
			}
			else
			{
				scanMax = Math.Max(highestReplyTtl + ExtensionTtls, 10);
				if (cycleNumber % 30 == 1)
					scanMax = options.MaxHops;
			}
			scanMax = Math.Min(scanMax, options.MaxHops);

			long now = Session.NowMs;
			List<int> ttls = new List<int>(scanMax);
			for (int ttl = 1; ttl <= scanMax; ttl++)
			{
				if (ttl != dest && IsSilent(ttl, now) && (cycleNumber + ttl) % SilentProbeEveryNCycles != 0)
					continue;
				ttls.Add(ttl);
			}
			return ttls;
		}

		/// <summary>A hop is silent if it has not answered many consecutive pings and has not answered at all for several minutes.</summary>
		private bool IsSilent(int ttl, long now)
		{
			HopSeries h = Session.GetHop(ttl);
			if (h == null || h.SentSinceLastReply < SilentAfterUnansweredPings)
				return false;
			return h.LastReplyTimeMs < 0 || now - h.LastReplyTimeMs > SilentAfterNoReplyMs;
		}

		private async Task PingOneAsync(Cycle cycle, int ttl, RecordHandle handle, CancellationToken ct)
		{
			Interlocked.Increment(ref inFlight);
			try
			{
				PingOutcome outcome;
				try
				{
					outcome = await pinger.PingAsync(target, ttl == 0 ? options.DirectTtl : ttl, options.TimeoutMs, CancellationToken.None).ConfigureAwait(false);
				}
				catch
				{
					outcome = PingOutcome.Lost;
				}
				lock (Session.DataLock)
				{
					if (stopped)
						return; // monitoring stopped while this ping was in flight; its record was voided
					if (options.TraceRoute)
						ProcessTraceResult(cycle, ttl, handle, outcome);
					else
						ProcessDirectResult(handle, outcome);
					if (--cycle.Remaining == 0)
					{
						if (options.TraceRoute)
							EndTraceCycle(cycle);
						activeCycles.Remove(cycle);
						cycle.MarkComplete();
					}
				}
			}
			finally
			{
				Interlocked.Decrement(ref inFlight);
			}
		}

		private void ProcessDirectResult(RecordHandle handle, PingOutcome outcome)
		{
			bool ok = outcome.Kind == PingOutcomeKind.EchoReply;
			Session.CompletePing(0, handle, ok, outcome.RttMs, ok ? (outcome.From ?? target) : null);
		}

		private void ProcessTraceResult(Cycle cycle, int ttl, RecordHandle handle, PingOutcome outcome)
		{
			switch (outcome.Kind)
			{
				case PingOutcomeKind.EchoReply:
					{
						if (ttl > cycle.MinEchoTtl)
						{
							// The destination already replied at a lower TTL in this cycle, so this TTL is beyond the destination.
							Session.VoidPing(ttl, handle);
							return;
						}
						Session.CompletePing(ttl, handle, true, outcome.RttMs, target);
						if (ttl < cycle.MinEchoTtl)
						{
							for (int i = cycle.Echoes.Count - 1; i >= 0; i--)
							{
								if (cycle.Echoes[i].ttl > ttl)
								{
									Session.VoidPing(cycle.Echoes[i].ttl, cycle.Echoes[i].handle);
									cycle.Echoes.RemoveAt(i);
								}
							}
							cycle.MinEchoTtl = ttl;
						}
						cycle.Echoes.Add((ttl, handle));
						int dest = Session.DestinationTtl;
						if (dest == 0 || ttl < dest)
						{
							// During discovery, replies from TTLs beyond the destination can arrive first; that is not a route change.
							Session.SetDestinationTtl(ttl, initial: cycle.Discovery);
							if (!cycle.Discovery)
								Log?.Invoke($"{Session.DisplayName}: route shortened, destination is now hop {ttl} (was {dest})");
						}
						break;
					}
				case PingOutcomeKind.TtlExpired:
					{
						Session.CompletePing(ttl, handle, true, outcome.RttMs, outcome.From);
						if (ttl > highestReplyTtl)
							highestReplyTtl = ttl;
						int dest = Session.DestinationTtl;
						if (dest > 0 && ttl >= dest)
							cycle.ExpiredAtOrBeyondDestination = true;
						break;
					}
				default:
					Session.CompletePing(ttl, handle, false, 0, null);
					break;
			}
		}

		private void EndTraceCycle(Cycle cycle)
		{
			int dest = Session.DestinationTtl;
			if (cycle.Discovery && dest > 0 && !discoveryLogged)
			{
				discoveryLogged = true;
				Log?.Invoke($"{Session.DisplayName}: destination reached at hop {dest}");
			}
			if (cycle.MinEchoTtl != int.MaxValue && dest > 0 && cycle.MinEchoTtl <= dest)
			{
				consecutiveMisses = 0;
				consecutiveBeyond = 0;
				extensionCyclesLeft = 0;
			}
			else if (cycle.MinEchoTtl != int.MaxValue && dest > 0)
			{
				// The destination replied, but only beyond the TTL where we thought it was.
				consecutiveBeyond++;
				if (cycle.ExpiredAtOrBeyondDestination || consecutiveBeyond >= ConfirmLengthenCycles)
				{
					Session.SetDestinationTtl(cycle.MinEchoTtl);
					Log?.Invoke($"{Session.DisplayName}: route lengthened, destination is now hop {cycle.MinEchoTtl} (was {dest})");
					consecutiveMisses = 0;
					consecutiveBeyond = 0;
					extensionCyclesLeft = 0;
				}
				else
					extensionCyclesLeft = Math.Max(extensionCyclesLeft, 1);
			}
			else
			{
				consecutiveMisses++;
				consecutiveBeyond = 0;
				if (cycle.ExpiredAtOrBeyondDestination && dest > 0)
				{
					// A router answered at the destination's TTL, so the destination must be further away now.
					extensionCyclesLeft = Math.Max(extensionCyclesLeft, ExtensionCycles);
				}
			}

			// Anything recorded beyond the (possibly updated) destination is not a real hop.
			int finalDest = Session.DestinationTtl;
			if (finalDest > 0)
			{
				foreach ((int ttl, RecordHandle h) in cycle.Dispatched)
				{
					if (ttl > finalDest)
						Session.VoidPing(ttl, h);
				}
			}
		}

		/// <summary>One dispatch of pings. All fields are guarded by <see cref="MonitoringSession.DataLock"/>.</summary>
		public sealed class Cycle
		{
			public readonly long Number;
			public readonly List<(int ttl, RecordHandle handle)> Dispatched = new List<(int, RecordHandle)>();
			internal readonly List<(int ttl, RecordHandle handle)> Echoes = new List<(int, RecordHandle)>();
			internal int MinEchoTtl = int.MaxValue;
			internal bool ExpiredAtOrBeyondDestination;
			/// <summary>True if the destination's hop number was unknown when this cycle was dispatched.</summary>
			internal bool Discovery;
			internal int Remaining;
			private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			public Cycle(long number) { Number = number; }

			/// <summary>Completes when every ping in the cycle has a result.</summary>
			public Task Completion => completion.Task;

			internal void MarkComplete() => completion.TrySetResult(true);
		}
	}
}
