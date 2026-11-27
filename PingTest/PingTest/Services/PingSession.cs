using PingTracer.Services.Pinging;
using PingTracer.Storage;
using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services
{
	/// <summary>
	/// One run of a <see cref="PingConfiguration"/>: a <see cref="MonitoringSession"/> and <see cref="RouteMonitor"/> per host,
	/// plus disk writers and periodic pruning.  Independent of any user interface.
	/// <para>
	/// Given a <see cref="ConfigDataSet"/>, a run continues the sessions it already holds (matched by host and mode), so the
	/// graphs pick up where the previous run left off, and it saves to and prunes the configuration's data directory.
	/// </para>
	/// </summary>
	public sealed class PingSession
	{
		/// <summary>Creates the pinger for a host string. Tests replace this.</summary>
		public Func<string, IPinger> PingerFactory { get; set; }

		public PingConfiguration Configuration { get; }
		public bool IsRunning { get; private set; }

		private readonly Settings settings;
		private readonly ConfigDataSet data;
		private ConfigDataSet.RunLock runLock;
		private readonly object stateLock = new object();
		private readonly List<MonitoringSession> sessions = new List<MonitoringSession>();
		private readonly List<RouteMonitor> monitors = new List<RouteMonitor>();
		private readonly List<PingDataWriter> writers = new List<PingDataWriter>();
		private readonly Dictionary<MonitoringSession, LossTracker> lossTrackers = new Dictionary<MonitoringSession, LossTracker>();
		private Timer pruneTimer;
		private Timer resolveTimer;
		private bool traceRoute;
		private readonly List<string> unresolvedHosts = new List<string>();
		private readonly object retryLock = new object();
		private volatile bool stopRequested;
		private readonly TaskCompletionSource<bool> startFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		private long sent;
		private long lost;

		public long SentCount => Interlocked.Read(ref sent);
		public long LostCount => Interlocked.Read(ref lost);

		public IReadOnlyList<MonitoringSession> Sessions
		{
			get { lock (stateLock) return sessions.ToArray(); }
		}

		/// <summary>Raised for each new session as it is created, before its monitor starts.  (Resumed sessions already exist.)</summary>
		public event Action<MonitoringSession> SessionCreated;
		public event Action<string> Log;
		public event Action<string> StatusChanged;
		/// <summary>Raised after the session has fully stopped (monitors stopped, files closed).</summary>
		public event Action Stopped;

		/// <param name="config">The configuration to run.</param>
		/// <param name="settings">Application settings.</param>
		/// <param name="data">The configuration's data, whose sessions are resumed and which is saved to disk if
		/// <see cref="Settings.saveSessionData"/> is set.  Null for a run that is neither resumed nor saved.</param>
		/// <param name="runLock">The configuration's run lock, released when the run stops.</param>
		public PingSession(PingConfiguration config, Settings settings, ConfigDataSet data = null, ConfigDataSet.RunLock runLock = null)
		{
			Configuration = config ?? throw new ArgumentNullException(nameof(config));
			this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
			this.data = data;
			this.runLock = runLock;
			PingerFactory = DefaultPingerFactory;
		}

		/// <summary>Monitoring time to keep, from the configuration.</summary>
		private long RetentionMs => Math.Max(60, Configuration.dataRetentionSeconds) * 1000L;

		private bool SaveToDisk => data != null && settings.saveSessionData;

		/// <summary>Host entries of a configuration, split and trimmed.</summary>
		public static List<string> GetHostList(PingConfiguration config)
		{
			return (config.hosts ?? new List<Host>())
				.SelectMany(h => (h?.hostname ?? "").Split(new[] { ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
				.Select(h => h.Trim())
				.Where(h => h.Length > 0)
				.ToList();
		}

		/// <summary>The (host key, mode) pairs a run of the configuration creates sessions for, in display order.</summary>
		public static List<(string hostKey, SessionMode mode)> GetSessionKeys(PingConfiguration config)
		{
			List<string> hosts = GetHostList(config);
			SessionMode mode = config.doTraceRoute && hosts.Count == 1 ? SessionMode.TraceRoute : SessionMode.Direct;
			return hosts.Select(h => (MonitoringSession.NormalizeHostKey(h), mode)).Distinct().ToList();
		}

		public static int ComputeIntervalMs(int rate, bool pingsPerSecond)
		{
			if (rate <= 0) return 1000;
			if (pingsPerSecond) return Math.Max(100, (int)(1000.0 / rate));
			return Math.Max(100, (int)(1000.0 * rate));
		}

		private IPinger DefaultPingerFactory(string host)
		{
			if (SimulatedNetwork.IsSimulatedHost(host))
				return new SimulatedNetwork();
			return new SmartPinger(settings.pingPayloadSizeBytes);
		}

		/// <summary>
		/// Resolves hosts and starts monitoring. Blocks during DNS resolution, so call from a background thread.
		/// Returns false if nothing could be started.
		/// </summary>
		public bool Start()
		{
			lock (stateLock)
			{
				if (IsRunning) return true;
				IsRunning = true;
			}
			try
			{
				return StartInternal();
			}
			finally
			{
				startFinished.TrySetResult(true);
			}
		}

		private bool StartInternal()
		{
			StatusChanged?.Invoke("Starting...");

			List<string> hosts = GetHostList(Configuration);
			if (hosts.Count == 0)
			{
				Log?.Invoke("Unable to start: the configuration has no hosts.");
				FailStart();
				return false;
			}

			traceRoute = Configuration.doTraceRoute && hosts.Count == 1;
			if (data != null)
			{
				long now = ServerClock.NowUnixMs;
				data.BeginRun(now);
				// Starting is when old data is discarded (never merely loading it): see ConfigDataSet.
				try
				{
					int deleted = data.ApplyRetention(RetentionMs, now, deleteFiles: SaveToDisk, Log);
					if (deleted > 0)
						Log?.Invoke("Deleted " + deleted + " saved data file" + (deleted == 1 ? "" : "s") + " older than the configuration keeps.");
				}
				catch (Exception ex)
				{
					Log?.Invoke("Unable to discard old data: " + ex.Message);
				}
			}
			List<string> unresolved = new List<string>();
			foreach (string host in hosts)
			{
				if (stopRequested)
					break;
				if (!TryStartHost(host, false))
					unresolved.Add(host);
			}

			lock (stateLock)
			{
				if (monitors.Count == 0)
				{
					Log?.Invoke("No hosts could be resolved. Pinging will not start.");
					FailStart();
					return false;
				}
				unresolvedHosts.AddRange(unresolved);
			}
			if (unresolved.Count > 0)
			{
				Log?.Invoke("Will keep trying to resolve " + string.Join(", ", unresolved) + " every " + ResolveRetrySeconds + " seconds.");
				resolveTimer = new Timer(_ => RetryUnresolved(), null, TimeSpan.FromSeconds(ResolveRetrySeconds), TimeSpan.FromSeconds(ResolveRetrySeconds));
			}
			pruneTimer = new Timer(_ => RunPrune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
			if (!stopRequested)
				StatusChanged?.Invoke("Running");
			return true;
		}

		/// <summary>How often hosts that could not be resolved at start are retried.</summary>
		public const int ResolveRetrySeconds = 30;

		/// <summary>Retries hosts whose names could not be resolved, so a DNS failure at start does not drop them for the whole run.</summary>
		private void RetryUnresolved()
		{
			if (!Monitor.TryEnter(retryLock)) return;
			try
			{
				List<string> pending;
				lock (stateLock) pending = new List<string>(unresolvedHosts);
				foreach (string host in pending)
				{
					if (stopRequested) return;
					if (!TryStartHost(host, true))
						continue;
					lock (stateLock) unresolvedHosts.Remove(host);
				}
				lock (stateLock)
				{
					if (unresolvedHosts.Count == 0)
						Interlocked.Exchange(ref resolveTimer, null)?.Dispose();
				}
			}
			finally
			{
				Monitor.Exit(retryLock);
			}
		}

		/// <summary>Resolves a host and starts monitoring it. Returns false if the name could not be resolved.</summary>
		private bool TryStartHost(string host, bool quietFailure)
		{
			int intervalMs = ComputeIntervalMs(Configuration.rate, Configuration.pingsPerSecond);
			int timeoutMs = Math.Clamp(settings.pingTimeoutMs, 200, 30000);
			IPAddress ip;
			string displayName;
			try
			{
				ip = ResolveHost(host, Configuration.preferIPv4, out displayName);
			}
			catch (Exception ex)
			{
				if (!quietFailure)
					Log?.Invoke(ex.Message);
				return false;
			}

			IPinger pinger = PingerFactory(host);
			SessionMode mode = traceRoute ? SessionMode.TraceRoute : SessionMode.Direct;
			string hostKey = MonitoringSession.NormalizeHostKey(host);
			MonitoringSession session = data?.Find(hostKey, mode);
			bool isNew = session == null;
			lock (stateLock)
			{
				// The same host listed twice: monitor it once.
				if (sessions.Any(s => s.HostKey == hostKey && s.Mode == mode))
					return true;
			}
			if (isNew)
				session = new MonitoringSession(ip, displayName, mode, intervalMs, timeoutMs, hostKey: hostKey);
			else
			{
				session.UpdateTarget(ip, displayName, timeoutMs);
				session.ResetRouteTracking();
			}
			if (Configuration.reverseDnsLookup)
				session.HostnameResolver = pinger.ReverseLookupAsync;
			RouteMonitor monitor = new RouteMonitor(session, pinger, new RouteMonitorOptions
			{
				TraceRoute = traceRoute,
				IntervalMs = intervalMs,
				TimeoutMs = timeoutMs,
				MaxHops = settings.maxHopsDefault,
			});
			monitor.Log += msg => Log?.Invoke(msg);

			// A retry that finishes after Stop must not start anything, because StopAsync only stops the monitors it finds
			// in the list.  (While Start itself runs, StopAsync waits for it, so Start's monitors are always found.)
			lock (stateLock)
			{
				if (stopRequested && quietFailure)
					return true;
			}
			WireSessionEvents(session);
			PingDataWriter writer = null;
			if (SaveToDisk)
				writer = TryCreateWriter(session);
			lock (stateLock)
			{
				if (stopRequested && quietFailure)
				{
					writer?.Dispose();
					UnwireSessionEvents(session);
					return true;
				}
				sessions.Add(session);
				monitors.Add(monitor);
				if (writer != null) writers.Add(writer);
			}
			if (isNew)
			{
				data?.Add(session);
				SessionCreated?.Invoke(session);
			}
			Log?.Invoke((traceRoute ? "Tracing route to " : "Pinging ") + displayName + " every " + intervalMs + " ms" + (isNew ? "" : " (continuing the saved graph)"));
			monitor.Start();
			return true;
		}

		private void FailStart()
		{
			data?.CancelRun();
			ReleaseRunLock();
			lock (stateLock) IsRunning = false;
			StatusChanged?.Invoke("Idle");
			Stopped?.Invoke();
		}

		/// <summary>Stops all monitors, waits for in-flight pings, and closes data files.</summary>
		public async Task StopAsync()
		{
			List<RouteMonitor> ms;
			List<PingDataWriter> ws;
			lock (stateLock)
			{
				if (!IsRunning) return;
				stopRequested = true;
			}
			// If Start is still resolving hosts, let it finish so that every monitor it creates gets stopped.
			await startFinished.Task.ConfigureAwait(false);
			lock (stateLock)
			{
				if (!IsRunning) return; // Start gave up on its own.
				ms = new List<RouteMonitor>(monitors);
				ws = new List<PingDataWriter>(writers);
				monitors.Clear();
				writers.Clear();
			}
			StatusChanged?.Invoke("Stopping...");
			Interlocked.Exchange(ref pruneTimer, null)?.Dispose();
			Interlocked.Exchange(ref resolveTimer, null)?.Dispose();
			await Task.WhenAll(ms.Select(m => m.StopAsync())).ConfigureAwait(false);
			FlushLossSummaries();
			foreach (PingDataWriter w in ws)
			{
				try { w.Dispose(); } catch { }
			}
			List<MonitoringSession> ended;
			lock (stateLock) ended = new List<MonitoringSession>(sessions);
			if (data != null)
			{
				long end = 0;
				foreach (MonitoringSession s in ended)
					end = Math.Max(end, s.ToUnixMs(s.LastRecordTimeMs));
				data.EndRun(end > 0 ? Math.Min(end, ServerClock.NowUnixMs) : ServerClock.NowUnixMs);
			}
			foreach (MonitoringSession s in ended)
				UnwireSessionEvents(s);
			ReleaseRunLock();
			lock (stateLock) IsRunning = false;
			StatusChanged?.Invoke("Idle");
			Log?.Invoke("Stopped.");
			Stopped?.Invoke();
		}

		public void SetPingDelay(int rate, bool pingsPerSecond)
		{
			int interval = ComputeIntervalMs(rate, pingsPerSecond);
			List<RouteMonitor> ms;
			lock (stateLock) ms = new List<RouteMonitor>(monitors);
			foreach (RouteMonitor m in ms)
				m.IntervalMs = interval;
		}

		private void ReleaseRunLock()
		{
			Interlocked.Exchange(ref runLock, null)?.Dispose();
		}

		public void RunPrune()
		{
			if (data != null)
			{
				try
				{
					int deleted = data.ApplyRetention(RetentionMs, ServerClock.NowUnixMs, deleteFiles: SaveToDisk, Log);
					if (deleted > 0)
						Log?.Invoke("Deleted " + deleted + " saved data file" + (deleted == 1 ? "" : "s") + " older than the configuration keeps.");
				}
				catch (Exception ex)
				{
					Log?.Invoke("Unable to discard old data: " + ex.Message);
				}
				return;
			}
			TimeSpan retention = TimeSpan.FromMilliseconds(RetentionMs);
			foreach (MonitoringSession s in Sessions)
			{
				try { s.PruneOlderThan(retention); } catch { }
			}
		}

		private PingDataWriter TryCreateWriter(MonitoringSession session)
		{
			try
			{
				PingDataWriter w = new PingDataWriter(session, data.Directory, data.ConfigGuid, settings.dataFlushIntervalSeconds);
				w.WriteFailed += ex => Log?.Invoke("Saving data for " + session.DisplayName + " failed and has been disabled: " + ex.Message);
				w.SegmentClosed += data.NoteSegmentClosed;
				return w;
			}
			catch (Exception ex)
			{
				Log?.Invoke("Data for " + session.DisplayName + " will not be saved (could not create a file): " + ex.Message);
				return null;
			}
		}

		/// <summary>Event handlers this run attached to each session, so they can be detached when it stops (sessions outlive runs).</summary>
		private readonly Dictionary<MonitoringSession, (Action<MonitoringSession, int, PingSample> completed, Action<MonitoringSession, int, PingSample> voided, Action<MonitoringSession, RouteEvent> route)> handlers
			= new Dictionary<MonitoringSession, (Action<MonitoringSession, int, PingSample>, Action<MonitoringSession, int, PingSample>, Action<MonitoringSession, RouteEvent>)>();

		private void UnwireSessionEvents(MonitoringSession session)
		{
			lock (stateLock)
			{
				if (!handlers.Remove(session, out var h))
					return;
				lock (session.DataLock)
				{
					session.SampleCompleted -= h.completed;
					session.SampleVoided -= h.voided;
					session.RouteEventAdded -= h.route;
				}
			}
		}

		private void WireSessionEvents(MonitoringSession session)
		{
			LossTracker tracker = new LossTracker(t => FormatTime(session, t));
			lock (stateLock) lossTrackers[session] = tracker;

			Action<MonitoringSession, int, PingSample> completed = (s, ttl, record) =>
			{
				Interlocked.Increment(ref sent);
				if (record.IsLost)
					Interlocked.Increment(ref lost);
				int destTtl = s.Mode == SessionMode.Direct ? 0 : s.DestinationTtl;
				if (ttl == destTtl && (s.Mode == SessionMode.Direct || destTtl > 0))
					TrackDestinationResult(s, tracker, record);
			};
			Action<MonitoringSession, int, PingSample> voided = (s, ttl, record) =>
			{
				Interlocked.Decrement(ref sent);
				if (record.IsLost)
					Interlocked.Decrement(ref lost);
			};
			Action<MonitoringSession, RouteEvent> route = (s, e) =>
			{
				if (e.Initial || e.Kind != RouteEventKind.HopAddress)
					return;
				IPAddress ip = s.Addresses.GetAddress(e.Value);
				string host = s.Addresses.GetHostname(e.Value);
				Log?.Invoke($"{s.DisplayName}: hop {e.Ttl} is now answering from {ip}{(string.IsNullOrEmpty(host) ? "" : " (" + host + ")")}");
			};
			lock (stateLock)
			{
				handlers[session] = (completed, voided, route);
				lock (session.DataLock)
				{
					session.SampleCompleted += completed;
					session.SampleVoided += voided;
					session.RouteEventAdded += route;
				}
			}
		}

		private void TrackDestinationResult(MonitoringSession s, LossTracker t, PingSample record)
		{
			if (Configuration.logFailures)
			{
				string summary = t.Add(record.IsLost, record.TimeMs, Environment.TickCount64);
				if (summary != null)
					Log?.Invoke(s.DisplayName + ": " + summary);
			}
			if (!record.IsLost && Configuration.logSuccesses)
				Log?.Invoke($"{s.DisplayName}: reply in {record.Rtt} ms at {FormatTime(s, record.TimeMs)}");
		}

		/// <summary>Logs the loss summaries that are still being collected (when monitoring stops).</summary>
		private void FlushLossSummaries()
		{
			List<KeyValuePair<MonitoringSession, LossTracker>> all;
			lock (stateLock) all = lossTrackers.ToList();
			foreach ((MonitoringSession s, LossTracker t) in all)
			{
				string summary = t.Flush();
				if (summary != null)
					Log?.Invoke(s.DisplayName + ": " + summary);
			}
		}

		private static string FormatTime(MonitoringSession s, long timeMs) => ServerClock.ToDateTime(s.ToUnixMs(timeMs)).ToLocalTime().ToString("HH:mm:ss");

		/// <summary>
		/// Turns the destination's lost pings into log lines: one summary per <see cref="WindowMs"/> of results that had
		/// any loss, e.g. "12 of 100 pings lost (12%) from 13:58:10 to 13:58:19".  Per-ping lines flood the log at high ping
		/// rates, and results do not arrive in order (a lost ping reports at its timeout, after newer replies), so
		/// "loss started / replies resumed" messages cannot be derived from the arrival order.
		/// </summary>
		internal sealed class LossTracker
		{
			public const int WindowMs = 10_000;
			private readonly Func<long, string> formatTime;
			private long windowStartTick = -1;
			private int total, lost;
			private long firstLostMs = long.MaxValue, lastLostMs = long.MinValue;

			/// <param name="formatTime">Formats a record time for the log.</param>
			public LossTracker(Func<long, string> formatTime) { this.formatTime = formatTime; }

			/// <summary>Counts one result.  Returns a summary line when this result closes a window that had loss.</summary>
			public string Add(bool isLost, long timeMs, long nowTick)
			{
				lock (this)
				{
					string summary = null;
					if (windowStartTick >= 0 && nowTick - windowStartTick >= WindowMs)
						summary = FlushLocked();
					if (windowStartTick < 0)
						windowStartTick = nowTick;
					total++;
					if (isLost)
					{
						lost++;
						firstLostMs = Math.Min(firstLostMs, timeMs);
						lastLostMs = Math.Max(lastLostMs, timeMs);
					}
					return summary;
				}
			}

			private static string FormatPercent(double p) => p < 1 ? "<1%" : Math.Round(p) + "%";

			/// <summary>Ends the current window.  Returns its summary, or null if it had no loss.</summary>
			public string Flush()
			{
				lock (this) return FlushLocked();
			}

			private string FlushLocked()
			{
				string summary = null;
				if (lost == 1)
					summary = "ping lost at " + formatTime(firstLostMs);
				else if (lost > 1)
				{
					string range = "from " + formatTime(firstLostMs) + " to " + formatTime(lastLostMs);
					summary = lost == total
						? $"all {lost} pings lost {range}"
						: $"{lost} of {total} pings lost ({FormatPercent(100.0 * lost / total)}) {range}";
				}
				windowStartTick = -1;
				total = lost = 0;
				firstLostMs = long.MaxValue;
				lastLostMs = long.MinValue;
				return summary;
			}
		}

		/// <summary>Resolves a host string to an address. Also handles the simulated-network <c>sim:</c> prefix.</summary>
		public static IPAddress ResolveHost(string host, bool preferIPv4, out string displayName)
		{
			host = host.Trim();
			if (SimulatedNetwork.IsSimulatedHost(host))
			{
				displayName = host;
				return SimulatedNetwork.DestinationAddress;
			}
			if (IPAddress.TryParse(host, out IPAddress literal))
			{
				displayName = literal.ToString();
				return literal;
			}
			IPAddress[] addresses;
			try
			{
				addresses = Dns.GetHostAddresses(host);
			}
			catch (SocketException ex)
			{
				// Windows hides the addresses of a family the computer has no global address for, so a name with only IPv6
				// addresses (ipv6.google.com) "does not exist" on a computer without IPv6 internet.  Query DNS directly so the
				// host is monitored anyway and its graph shows that it is unreachable.
				addresses = WindowsDns.Query(host, AddressFamily.InterNetworkV6);
				if (addresses.Length == 0)
					addresses = WindowsDns.Query(host, AddressFamily.InterNetwork);
				if (addresses.Length == 0)
					throw new Exception("Unable to resolve \"" + host + "\": " + ex.Message, ex);
			}
			catch (Exception ex)
			{
				throw new Exception("Unable to resolve \"" + host + "\": " + ex.Message, ex);
			}
			AddressFamily preferred = preferIPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
			IPAddress ip = addresses.FirstOrDefault(a => a.AddressFamily == preferred) ?? addresses.FirstOrDefault();
			if (ip == null)
				throw new Exception("Unable to resolve \"" + host + "\": no addresses found.");
			displayName = host + " [" + ip + "]";
			return ip;
		}
	}
}
