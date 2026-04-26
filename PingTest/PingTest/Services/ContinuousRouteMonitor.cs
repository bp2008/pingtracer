using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PingTracer.Storage;
using PingTracer.TraceRoute;

namespace PingTracer.Services
{
	/// <summary>
	/// Timer-driven continuous traceroute engine. Each tick dispatches one trace cycle
	/// in parallel; cycles may overlap to preserve sampling cadence even when prior
	/// cycles are still in-flight.
	/// </summary>
	public class ContinuousRouteMonitor : IDisposable
	{
		public delegate Task TraceRouteDelegate(
			object token,
			IPAddress target,
			byte maxHops,
			HashSet<byte> skipHops,
			Action<byte, DateTime> onHopDispatching,
			Action<TraceRouteHostResult> onHostResult,
			int pingTimeoutMs);

		/// <summary>
		/// Sends a single ping at the given TTL and returns the result.
		/// Used for the high-TTL destination liveness probe.
		/// </summary>
		public delegate Task<TraceRouteHostResult> SendProbeDelegate(
			IPAddress target,
			byte ttl,
			int pingTimeoutMs);

		private readonly TraceRouteDelegate _traceRouteFn;
		private readonly SendProbeDelegate _probeFn;
		private readonly int _pingTimeoutMs;
		private readonly int _destinationProbeMissThreshold;
		private readonly byte _destinationProbeTtl;
		private readonly byte _allowedUnresponsiveDispatches;

		private Timer _timer;
		private int _intervalMs;
		private byte _maxHops;
		private int _consecutiveDestinationMisses;
		private int _disposed;

		public IPAddress TargetAddress { get; }
		public MonitoringSession Session { get; }
		public TraceThrottler Throttler { get; }

		public int IntervalMs
		{
			get => _intervalMs;
			set
			{
				_intervalMs = value;
				_timer?.Change(value, value);
			}
		}

		public byte MaxHops => _maxHops;
		public bool IsRunning => _timer != null;
		public int ConsecutiveDestinationMisses => Volatile.Read(ref _consecutiveDestinationMisses);

		/// <summary>
		/// Fired once per ping completion (success or timeout). The third argument is
		/// non-null when the ping was tracked via a pending RecordHandle at dispatch
		/// time; null when the record was deferred to end-of-cycle reconciliation.
		/// </summary>
		public event Action<TraceRouteHostResult, HopTimeSeries, RecordHandle?> PingRecordCompleted;

		/// <summary>Fired when a cycle throws an unexpected exception.</summary>
		public event Action<Exception> CycleFaulted;

		public ContinuousRouteMonitor(
			IPAddress target,
			MonitoringSession session,
			int intervalMs = 1000,
			byte initialMaxHops = 30,
			int pingTimeoutMs = 5000,
			int destinationProbeMissThreshold = 10,
			byte destinationProbeTtl = 128,
			byte allowedUnresponsiveDispatches = 1,
			TraceRouteDelegate traceRouteFn = null,
			SendProbeDelegate probeFn = null,
			TraceThrottler throttler = null)
		{
			TargetAddress = target ?? throw new ArgumentNullException(nameof(target));
			Session = session ?? throw new ArgumentNullException(nameof(session));
			_intervalMs = intervalMs;
			_maxHops = initialMaxHops;
			_pingTimeoutMs = pingTimeoutMs;
			_destinationProbeMissThreshold = destinationProbeMissThreshold;
			_destinationProbeTtl = destinationProbeTtl;
			_allowedUnresponsiveDispatches = allowedUnresponsiveDispatches;
			Throttler = throttler ?? new TraceThrottler();
			_traceRouteFn = traceRouteFn ?? throw new ArgumentNullException(nameof(traceRouteFn));
			_probeFn = probeFn ?? throw new ArgumentNullException(nameof(probeFn));
		}

		public void Start()
		{
			if (_disposed != 0) throw new ObjectDisposedException(nameof(ContinuousRouteMonitor));
			if (_timer != null) return;
			_timer = new Timer(TimerTick, null, 0, _intervalMs);
		}

		public void Stop()
		{
			Timer t = Interlocked.Exchange(ref _timer, null);
			t?.Dispose();
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
			Stop();
		}

		private void TimerTick(object state)
		{
			if (_disposed != 0) return;
			// Fire-and-forget; overlapping cycles are intentional.
			_ = SafeRunOneCycleAsync();
		}

		private async Task SafeRunOneCycleAsync()
		{
			try
			{
				await RunOneCycleAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				CycleFaulted?.Invoke(ex);
			}
		}

		/// <summary>
		/// Runs exactly one cycle synchronously. Public to allow tests to drive
		/// the monitor deterministically without relying on the timer.
		/// </summary>
		public async Task RunOneCycleAsync()
		{
			byte cycleMaxHops = _maxHops;
			DateTime cycleStartUtc = DateTime.UtcNow;

			var skip = Throttler.GetHopNumbersThatShouldNotBePingedRightNow(cycleMaxHops, _allowedUnresponsiveDispatches);
			var cycleState = new CycleState(cycleMaxHops);

			await _traceRouteFn(
				token: cycleState,
				target: TargetAddress,
				maxHops: cycleMaxHops,
				skipHops: skip,
				onHopDispatching: (ttl, sentAt) => OnHopDispatching(cycleState, ttl, sentAt),
				onHostResult: result => OnHostResult(cycleState, result),
				pingTimeoutMs: _pingTimeoutMs).ConfigureAwait(false);

			// Topology pass: build/close HopTimeSeries based on observed results.
			var nonNullResults = cycleState.NonNullResults();
			if (nonNullResults.Length > 0)
				Session.RecordTraceResult(nonNullResults, cycleStartUtc);

			// Reconciliation pass: insert completed records on newly-created series
			// for first-cycle hops and address-change cases.
			ReconcileDeferredRecords(cycleState);

			AdjustMaxHops(cycleState);
			await MaybeRunDestinationProbeAsync(cycleState).ConfigureAwait(false);
		}

		private void OnHopDispatching(CycleState state, byte ttl, DateTime sentAt)
		{
			Throttler.SentPingToHop(ttl);
			int idx = ttl - 1;
			state.SendTimes[idx] = sentAt;
			state.WasDispatched[idx] = true;

			HopHistory history = Session.HopData[idx];
			HopTimeSeries active;
			lock (history)
				active = history.ActiveSeries;

			if (active != null)
			{
				RecordHandle h = active.BeginPendingRecord(sentAt);
				state.PendingSeries[idx] = active;
				state.PendingHandles[idx] = h;
			}
		}

		private void OnHostResult(CycleState state, TraceRouteHostResult result)
		{
			int idx = result.ttl - 1;
			state.Results[idx] = result;

			HopTimeSeries pendingSeries = state.PendingSeries[idx];
			RecordHandle? pendingHandle = state.PendingHandles[idx];
			HopTimeSeries reportedSeries = pendingSeries;

			if (pendingSeries != null && pendingHandle.HasValue)
			{
				if (result.success && result.replyFrom != null && result.replyFrom.Equals(pendingSeries.Address))
				{
					pendingSeries.CompleteRecord(pendingHandle.Value, ClampRtt(result.roundTripTime));
					state.PendingResolvedOk[idx] = true;
				}
				else
				{
					// Address mismatch or no response — old series saw no reply from its IP.
					pendingSeries.CompleteRecord(pendingHandle.Value, PingRecordStatus.Timeout);
				}
			}

			if (result.success) Throttler.GotResponseFromHop(result.ttl);

			PingRecordCompleted?.Invoke(result, reportedSeries, pendingHandle);
		}

		private void ReconcileDeferredRecords(CycleState state)
		{
			for (int idx = 0; idx < state.MaxHops; idx++)
			{
				if (state.PendingResolvedOk[idx]) continue;

				TraceRouteHostResult result = state.Results[idx];
				if (result == null || !result.success || result.replyFrom == null) continue;

				HopHistory history = Session.HopData[idx];
				HopTimeSeries active;
				lock (history)
					active = history.ActiveSeries;

				if (active == null || !active.Address.Equals(result.replyFrom)) continue;

				DateTime sentAt = state.SendTimes[idx];
				if (sentAt == default) sentAt = DateTime.UtcNow;
				RecordHandle h = active.BeginPendingRecord(sentAt);
				active.CompleteRecord(h, ClampRtt(result.roundTripTime));
			}
		}

		private void AdjustMaxHops(CycleState state)
		{
			byte? destinationTtl = null;
			bool destinationResponded = false;

			for (int idx = 0; idx < state.MaxHops; idx++)
			{
				TraceRouteHostResult r = state.Results[idx];
				if (r == null || !r.success || r.replyFrom == null) continue;
				if (r.replyFrom.Equals(TargetAddress))
				{
					destinationResponded = true;
					if (destinationTtl == null || r.ttl < destinationTtl)
						destinationTtl = r.ttl;
				}
			}

			if (destinationResponded)
			{
				Volatile.Write(ref _consecutiveDestinationMisses, 0);
				if (destinationTtl.HasValue && destinationTtl.Value < _maxHops)
					_maxHops = destinationTtl.Value;
			}
			else
			{
				Interlocked.Increment(ref _consecutiveDestinationMisses);
				int next = Math.Min(255, _maxHops + 10);
				_maxHops = (byte)next;
			}
		}

		private async Task MaybeRunDestinationProbeAsync(CycleState state)
		{
			if (Volatile.Read(ref _consecutiveDestinationMisses) < _destinationProbeMissThreshold)
				return;

			// Skip probe if the destination already appeared in this cycle's results
			// (possible if the destination responded but at a TTL we haven't yet shrunk to).
			for (int idx = 0; idx < state.MaxHops; idx++)
			{
				TraceRouteHostResult r = state.Results[idx];
				if (r != null && r.success && r.replyFrom != null && r.replyFrom.Equals(TargetAddress))
					return;
			}

			TraceRouteHostResult probe = await _probeFn(TargetAddress, _destinationProbeTtl, _pingTimeoutMs).ConfigureAwait(false);
			if (probe != null && probe.success && probe.replyFrom != null && probe.replyFrom.Equals(TargetAddress))
			{
				Volatile.Write(ref _consecutiveDestinationMisses, 0);
				int next = Math.Min(255, _maxHops + 10);
				_maxHops = (byte)next;
			}
		}

		private static ushort ClampRtt(long roundTripTime)
		{
			if (roundTripTime < 0) return 0;
			if (roundTripTime > 65533) return 65533;
			return (ushort)roundTripTime;
		}

		private sealed class CycleState
		{
			public readonly byte MaxHops;
			public readonly DateTime[] SendTimes;
			public readonly bool[] WasDispatched;
			public readonly HopTimeSeries[] PendingSeries;
			public readonly RecordHandle?[] PendingHandles;
			public readonly bool[] PendingResolvedOk;
			public readonly TraceRouteHostResult[] Results;

			public CycleState(byte maxHops)
			{
				MaxHops = maxHops;
				SendTimes = new DateTime[maxHops];
				WasDispatched = new bool[maxHops];
				PendingSeries = new HopTimeSeries[maxHops];
				PendingHandles = new RecordHandle?[maxHops];
				PendingResolvedOk = new bool[maxHops];
				Results = new TraceRouteHostResult[maxHops];
			}

			public TraceRouteHostResult[] NonNullResults()
			{
				int count = 0;
				for (int i = 0; i < Results.Length; i++)
					if (Results[i] != null) count++;
				var arr = new TraceRouteHostResult[count];
				int j = 0;
				for (int i = 0; i < Results.Length; i++)
					if (Results[i] != null) arr[j++] = Results[i];
				return arr;
			}
		}
	}
}
