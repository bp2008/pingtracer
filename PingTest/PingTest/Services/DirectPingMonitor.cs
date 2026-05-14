using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PingTracer.Storage;
using PingTracer.TraceRoute;
using PingTracer.Util;
using SmartPing;

namespace PingTracer.Services
{
	/// <summary>
	/// Lightweight single-hop counterpart to <see cref="ContinuousRouteMonitor"/>.
	/// Sends one ping per tick directly to the target (no TTL ladder, no traceroute)
	/// and records results into the same <see cref="MonitoringSession"/> shape:
	/// a single active <see cref="HopTimeSeries"/> in <c>Session.HopData[0]</c>.
	/// </summary>
	public class DirectPingMonitor : IPingMonitor
	{
		/// <summary>
		/// Sends a single ping to the target and returns a synthesized
		/// <see cref="TraceRouteHostResult"/>. The fixture is replaceable for tests.
		/// </summary>
		public delegate Task<TraceRouteHostResult> SendPingDelegate(
			IPAddress target,
			int pingTimeoutMs,
			DateTime sentAtUtc);

		private readonly SendPingDelegate _sendFn;
		private readonly int _pingTimeoutMs;
		private readonly int _payloadSizeBytes;

		private Timer _timer;
		private int _intervalMs;
		private int _disposed;

		public IPAddress TargetAddress { get; }
		public MonitoringSession Session { get; }

		public int IntervalMs
		{
			get => _intervalMs;
			set
			{
				_intervalMs = value;
				_timer?.Change(value, value);
			}
		}

		public bool IsRunning => _timer != null;

		public event Action<TraceRouteHostResult, HopTimeSeries, RecordHandle?> PingRecordCompleted;

		// Direct mode pings the target itself; there is no concept of an
		// unresponsive intermediate hop, so this event is never fired.
#pragma warning disable CS0067
		public event Action<byte, DateTime> UnresponsiveHopProbed;
#pragma warning restore CS0067

		public DirectPingMonitor(
			IPAddress target,
			MonitoringSession session,
			int intervalMs = 1000,
			int pingTimeoutMs = 5000,
			int payloadSizeBytes = 32,
			SendPingDelegate sendFn = null)
		{
			TargetAddress = target ?? throw new ArgumentNullException(nameof(target));
			Session = session ?? throw new ArgumentNullException(nameof(session));
			_intervalMs = intervalMs;
			_pingTimeoutMs = pingTimeoutMs;
			_payloadSizeBytes = payloadSizeBytes;
			_sendFn = sendFn ?? DefaultSendPing;
		}

		public void Start()
		{
			if (_disposed != 0) throw new ObjectDisposedException(nameof(DirectPingMonitor));
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
			_ = SafeRunOneCycleAsync();
		}

		private async Task SafeRunOneCycleAsync()
		{
			try { await RunOneCycleAsync().ConfigureAwait(false); }
			catch { /* swallow; surface via test-only hooks if needed */ }
		}

		/// <summary>
		/// Runs exactly one dispatch synchronously. Public so tests can drive
		/// the monitor without relying on the timer.
		/// </summary>
		public async Task RunOneCycleAsync()
		{
			DateTime sentAt = DateTime.UtcNow;

			// Ensure an active HopTimeSeries exists in HopData[0] for the target.
			// Reuse the same path RecordTraceResult walks so RouteSnapshot bookkeeping
			// and RouteChanged events fire identically to the traceroute path.
			HopHistory history = Session.HopData[0];
			HopTimeSeries active;
			lock (history)
				active = history.ActiveSeries;

			if (active == null || !active.Address.Equals(TargetAddress))
			{
				// Drive a one-hop topology pass through the session so a HopTimeSeries
				// for TargetAddress is established (or reused) and RouteChanged fires.
				var seedResult = new TraceRouteHostResult(
					token: this,
					success: true,
					roundTripTime: 0,
					replyFrom: TargetAddress,
					ttl: 1,
					target: TargetAddress,
					maxHops: 1,
					pingTimeoutMs: _pingTimeoutMs,
					sentTimestampUtc: sentAt);
				Session.RecordTraceResult(new[] { seedResult }, sentAt);

				lock (history)
					active = history.ActiveSeries;
			}

			if (active == null) return; // defensive; should not happen

			RecordHandle handle = active.BeginPendingRecord(sentAt);

			TraceRouteHostResult result;
			try
			{
				result = await _sendFn(TargetAddress, _pingTimeoutMs, sentAt).ConfigureAwait(false);
			}
			catch
			{
				result = new TraceRouteHostResult(this, false, 0, IPAddress.Any, 1, TargetAddress, 1, _pingTimeoutMs, sentAt);
			}

			if (result.success && result.replyFrom != null && result.replyFrom.Equals(TargetAddress))
			{
				active.CompleteRecord(handle, ClampRtt(result.roundTripTime));
			}
			else
			{
				active.CompleteRecord(handle, PingRecordStatus.Timeout);
			}

			PingRecordCompleted?.Invoke(result, active, handle);
		}

		private async Task<TraceRouteHostResult> DefaultSendPing(IPAddress target, int pingTimeoutMs, DateTime sentAtUtc)
		{
			Ping ping = PingInstancePool.Get();
			try
			{
				PingReply reply = await ping.SendPingAsync(
					target,
					pingTimeoutMs,
					PingBufferStatic.GetBuffer(_payloadSizeBytes)).ConfigureAwait(false);

				bool success = reply.Status == IPStatus.Success;
				IPAddress replyFrom = success ? reply.Address : IPAddress.Any;

				return new TraceRouteHostResult(
					token: this,
					success: success,
					roundTripTime: reply.RoundtripTime,
					replyFrom: replyFrom,
					ttl: 1,
					target: target,
					maxHops: 1,
					pingTimeoutMs: pingTimeoutMs,
					sentTimestampUtc: sentAtUtc);
			}
			finally
			{
				PingInstancePool.Recycle(ping);
			}
		}

		private static ushort ClampRtt(long roundTripTime)
		{
			if (roundTripTime < 0) return 0;
			if (roundTripTime > 65533) return 65533;
			return (ushort)roundTripTime;
		}
	}
}
