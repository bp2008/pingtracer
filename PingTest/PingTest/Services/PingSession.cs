using PingTracer.Storage;
using PingTracer.Tracer;
using PingTracer.TraceRoute;
using PingTracer.Util;
using SmartPing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services
{
	/// <summary>
	/// Manages an active ping session for a <see cref="PingConfiguration"/>, decoupled from any UI.
	/// Hosts one <see cref="MonitoringSession"/> + <see cref="IPingMonitor"/> per resolved address;
	/// uses <see cref="ContinuousRouteMonitor"/> for traceroute mode and
	/// <see cref="DirectPingMonitor"/> for single-hop direct ping mode (including all
	/// multi-host configurations).
	/// </summary>
	public class PingSession
	{
		/// <summary>
		/// Factory hook for creating monitors. <paramref name="useTraceRoute"/> selects
		/// route vs direct mode. Tests can replace this to inject mock monitors.
		/// </summary>
		public delegate IPingMonitor MonitorFactory(IPAddress target, string displayName, bool useTraceRoute, int intervalMs, MonitoringSession session);

		public PingConfiguration Configuration { get; }
		public bool IsRunning { get; private set; }
		public long SuccessfulPings => Interlocked.Read(ref _successfulPings);
		public long FailedPings => Interlocked.Read(ref _failedPings);

		/// <summary>All MonitoringSessions for this PingSession, one per resolved host.</summary>
		public IReadOnlyList<MonitoringSession> Sessions
		{
			get
			{
				lock (_stateLock)
					return _sessions.ToArray();
			}
		}

		private readonly Settings _settings;
		private readonly MonitorFactory _factory;
		private readonly object _stateLock = new object();
		private readonly List<MonitoringSession> _sessions = new List<MonitoringSession>();
		private readonly List<IPingMonitor> _monitors = new List<IPingMonitor>();
		private readonly List<RouteSnapshot> _lastEmittedRoutes = new List<RouteSnapshot>();
		private readonly List<PingDataWriter> _writers = new List<PingDataWriter>();

		private long _successfulPings;
		private long _failedPings;
		private volatile int _intervalMs = 1000;
		private Timer _pruneTimer;

		/// <summary>Raised when a new HopTimeSeries first appears in any MonitoringSession.</summary>
		public event Action<MonitoringSession, HopTimeSeries> HopDiscovered;

		/// <summary>Raised when a hop stops responding (its active series gets EndTimeUtc set).</summary>
		public event Action<MonitoringSession, byte, IPAddress> HopDeactivated;

		/// <summary>Raised once per ping completion, bubbled from the underlying monitors.</summary>
		public event Action<MonitoringSession, TraceRouteHostResult, HopTimeSeries, RecordHandle?> PingRecordCompleted;

		/// <summary>Raised when a session's route topology changes.</summary>
		public event Action<MonitoringSession, RouteSnapshot, RouteSnapshot> RouteChanged;

		public event Action<string> StatusChanged;
		public event Action<string> LogCreated;
		public event Action SessionStopped;

		public PingSession(PingConfiguration config, Settings settings, MonitorFactory factory = null)
		{
			Configuration = config ?? throw new ArgumentNullException(nameof(config));
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
			_factory = factory ?? DefaultMonitorFactory;
			_intervalMs = ComputeIntervalMs(config.rate, config.pingsPerSecond);
		}

		public void Start()
		{
			lock (_stateLock)
			{
				if (IsRunning) return;
				IsRunning = true;
				// A previous Stop() retained sessions for post-stop queryData;
				// clear them now so this Start replaces them cleanly.
				_sessions.Clear();
				_monitors.Clear();
				_lastEmittedRoutes.Clear();
				_writers.Clear();
			}

			Interlocked.Exchange(ref _successfulPings, 0);
			Interlocked.Exchange(ref _failedPings, 0);

			string hostString = Configuration.GetHostString();
			bool reverseDnsLookup = Configuration.reverseDnsLookup;
			bool preferIpv4 = Configuration.GetPreferIPv4();

			StatusChanged?.Invoke("Starting...");
			LogCreated?.Invoke("Initializing pings to " + hostString);

			string[] addresses = (hostString ?? string.Empty)
				.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (addresses.Length == 0)
			{
				LogCreated?.Invoke("Unable to start pinging because the host input is empty.");
				StopInternal();
				return;
			}

			// Multi-host configurations always use direct mode (matches legacy behavior).
			bool useTraceRoute = Configuration.doTraceRoute && addresses.Length == 1;

			foreach (string raw in addresses)
			{
				IPAddress ip;
				string resolvedName;
				try
				{
					ip = StringToIp(raw.Trim(), preferIpv4, out resolvedName);
				}
				catch (Exception ex)
				{
					LogCreated?.Invoke(ex.Message);
					continue;
				}
				if (ip == null) continue;

				string displayName = ip.ToString();
				if (!string.IsNullOrWhiteSpace(resolvedName))
					displayName = resolvedName + " [" + ip + "]";

				MonitoringSession session = new MonitoringSession(ip, displayName);
				IPingMonitor monitor = _factory(ip, displayName, useTraceRoute, _intervalMs, session);
				if (monitor == null) continue;

				WireSessionEvents(session);
				WireMonitorEvents(session, monitor);

				PingDataWriter writer = TryCreateWriter(session);

				lock (_stateLock)
				{
					_sessions.Add(session);
					_monitors.Add(monitor);
					_lastEmittedRoutes.Add(null);
					if (writer != null) _writers.Add(writer);
				}

				monitor.Start();
			}

			List<IPingMonitor> snapshot;
			lock (_stateLock) snapshot = new List<IPingMonitor>(_monitors);
			if (snapshot.Count == 0)
			{
				LogCreated?.Invoke("No hosts could be resolved. Pinging will not start.");
				StopInternal();
				return;
			}

			_pruneTimer = new Timer(_ => RunPrune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

			LogCreated?.Invoke("Now beginning pings");
			StatusChanged?.Invoke("Pinging Active");
		}

		public void Stop()
		{
			lock (_stateLock)
			{
				if (!IsRunning) return;
			}
			StopInternal();
			StatusChanged?.Invoke("Idle");
			SessionStopped?.Invoke();
		}

		private void StopInternal()
		{
			Timer pt = Interlocked.Exchange(ref _pruneTimer, null);
			pt?.Dispose();

			List<IPingMonitor> monitorSnapshot;
			List<PingDataWriter> writerSnapshot;
			lock (_stateLock)
			{
				IsRunning = false;
				monitorSnapshot = new List<IPingMonitor>(_monitors);
				writerSnapshot = new List<PingDataWriter>(_writers);
				// Retain _sessions and _lastEmittedRoutes so post-stop queryData
				// requests can still resolve against the in-memory data.
				// They are cleared on the next Start().
				_monitors.Clear();
				_writers.Clear();
			}

			foreach (IPingMonitor m in monitorSnapshot)
			{
				try { m.Stop(); } catch { }
				try { m.Dispose(); } catch { }
			}

			// Disposing the writer waits for in-flight pings to complete and performs
			// a final flush, so it must happen after monitors are stopped.
			foreach (PingDataWriter w in writerSnapshot)
			{
				try { w.Dispose(); } catch { }
			}
		}

		private PingDataWriter TryCreateWriter(MonitoringSession session)
		{
			try
			{
				string dir = PingDataFile.ResolveDataDirectory(_settings.dataDirectory, Settings.SettingsFolderPath);
				string fileName = PingDataFile.BuildSessionFileName(session.TargetAddress, session.StartTimeUtc);
				string path = System.IO.Path.Combine(dir, fileName);
				return new PingDataWriter(session, path, _settings.diskFlushIntervalSeconds);
			}
			catch (Exception ex)
			{
				LogCreated?.Invoke("Disk persistence disabled (failed to open session log): " + ex.Message);
				return null;
			}
		}

		/// <summary>Updates the ping interval for this session and propagates to all monitors.</summary>
		public void SetPingDelay(int rate, bool pingsPerSecond)
		{
			_intervalMs = ComputeIntervalMs(rate, pingsPerSecond);
			List<IPingMonitor> snapshot;
			lock (_stateLock) snapshot = new List<IPingMonitor>(_monitors);
			foreach (IPingMonitor m in snapshot)
			{
				try { m.IntervalMs = _intervalMs; } catch { }
			}
		}

		/// <summary>
		/// Runs a prune cycle on all sessions immediately. Public so tests and external
		/// triggers can drive pruning without waiting for the periodic timer.
		/// </summary>
		public void RunPrune()
		{
			List<MonitoringSession> snapshot;
			lock (_stateLock) snapshot = new List<MonitoringSession>(_sessions);
			TimeSpan retention = _settings.DataRetentionPeriod;
			foreach (MonitoringSession s in snapshot)
			{
				try { s.PruneOlderThan(retention); } catch { }
			}
		}

		private void WireSessionEvents(MonitoringSession session)
		{
			session.RouteChanged += (oldRoute, newRoute) =>
			{
				int idx;
				RouteSnapshot prevEmitted;
				lock (_stateLock)
				{
					idx = _sessions.IndexOf(session);
					prevEmitted = idx >= 0 ? _lastEmittedRoutes[idx] : null;
					if (idx >= 0)
						_lastEmittedRoutes[idx] = newRoute;
				}

				IReadOnlyList<RouteChange> diff = newRoute.DiffFrom(prevEmitted);
				foreach (RouteChange ch in diff)
				{
					if (ch.OldAddress != null)
						HopDeactivated?.Invoke(session, ch.HopIndex, ch.OldAddress);
					if (ch.NewAddress != null && ch.HopIndex < newRoute.Hops.Length)
					{
						HopTimeSeries newSeries = newRoute.Hops[ch.HopIndex];
						if (newSeries != null)
							HopDiscovered?.Invoke(session, newSeries);
					}
				}

				RouteChanged?.Invoke(session, oldRoute, newRoute);
			};
		}

		private void WireMonitorEvents(MonitoringSession session, IPingMonitor monitor)
		{
			monitor.PingRecordCompleted += (result, series, handle) =>
			{
				if (result != null && result.success)
					Interlocked.Increment(ref _successfulPings);
				else
					Interlocked.Increment(ref _failedPings);

				PingRecordCompleted?.Invoke(session, result, series, handle);
			};
		}

		private IPingMonitor DefaultMonitorFactory(IPAddress target, string displayName, bool useTraceRoute, int intervalMs, MonitoringSession session)
		{
			if (useTraceRoute)
			{
				return new ContinuousRouteMonitor(
					target,
					session,
					intervalMs: intervalMs,
					initialMaxHops: (byte)Math.Min(255, Math.Max(1, _settings.maxHopsDefault)),
					pingTimeoutMs: 5000,
					destinationProbeMissThreshold: _settings.destinationProbeMissThreshold,
					destinationProbeTtl: _settings.destinationProbeTtl,
					traceRouteFn: RouteTracerMethodD.TraceRoute,
					probeFn: SendDestinationProbeAsync);
			}

			return new DirectPingMonitor(
				target,
				session,
				intervalMs: intervalMs,
				pingTimeoutMs: 5000,
				payloadSizeBytes: _settings.pingPayloadSizeBytes);
		}

		private static async Task<TraceRouteHostResult> SendDestinationProbeAsync(IPAddress target, byte ttl, int pingTimeoutMs)
		{
			Ping ping = PingInstancePool.Get();
			DateTime sentUtc = DateTime.UtcNow;
			try
			{
				PingOptions opt = new PingOptions(ttl, true);
				PingReply reply = await ping.SendPingAsync(
					target,
					pingTimeoutMs,
					PingBufferStatic.GetBuffer(RouteTracerMethodD.PingPayloadSizeBytes),
					opt).ConfigureAwait(false);

				bool success = reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired;
				IPAddress replyFrom = success ? reply.Address : IPAddress.Any;
				return new TraceRouteHostResult(null, success, reply.RoundtripTime, replyFrom, ttl, target, ttl, pingTimeoutMs, sentUtc);
			}
			catch
			{
				return new TraceRouteHostResult(null, false, 0, IPAddress.Any, ttl, target, ttl, pingTimeoutMs, sentUtc);
			}
			finally
			{
				PingInstancePool.Recycle(ping);
			}
		}

		private static int ComputeIntervalMs(int rate, bool pingsPerSecond)
		{
			if (rate == 0) return 1000;
			if (pingsPerSecond) return Math.Max(100, (int)(1000.0 / rate));
			return Math.Max(100, (int)(1000.0 * rate));
		}

		internal static IPAddress StringToIp(string address, bool preferIpv4, out string hostName)
		{
			hostName = null;
			try
			{
				if (IPAddress.TryParse(address, out IPAddress tmp))
					return tmp;
			}
			catch (FormatException) { }

			try
			{
				hostName = address;
				IPHostEntry iphe = Dns.GetHostEntry(address);
				if (preferIpv4)
				{
					IPAddress addr = iphe.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
					if (addr != null) return addr;
				}
				else
				{
					IPAddress addr = iphe.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
					if (addr != null) return addr;
				}
				if (iphe.AddressList.Length > 0)
					return iphe.AddressList[0];
			}
			catch (Exception e)
			{
				throw new Exception("Unable to resolve '" + address + "'", e);
			}
			throw new Exception("Unable to resolve '" + address + "'");
		}
	}
}
