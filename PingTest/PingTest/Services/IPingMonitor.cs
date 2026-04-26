using System;
using System.Net;
using PingTracer.Storage;
using PingTracer.TraceRoute;

namespace PingTracer.Services
{
	/// <summary>
	/// Common surface for monitors that drive a <see cref="MonitoringSession"/>.
	/// Implemented by <see cref="ContinuousRouteMonitor"/> (traceroute mode) and
	/// <see cref="DirectPingMonitor"/> (single-hop direct ping mode) so
	/// <see cref="PingSession"/> can treat them uniformly.
	/// </summary>
	public interface IPingMonitor : IDisposable
	{
		IPAddress TargetAddress { get; }
		MonitoringSession Session { get; }
		int IntervalMs { get; set; }
		bool IsRunning { get; }

		void Start();
		void Stop();

		/// <summary>
		/// Fired once per ping completion (success or timeout). The third argument
		/// is non-null when the ping was tracked via a pending RecordHandle at
		/// dispatch time; null when the record was deferred to end-of-cycle
		/// reconciliation.
		/// </summary>
		event Action<TraceRouteHostResult, HopTimeSeries, RecordHandle?> PingRecordCompleted;
	}
}
