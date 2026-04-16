using PingTracer.Tracer;
using PingTracer.Util;
using SmartPing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;

namespace PingTracer.Services
{
	/// <summary>
	/// Represents a single active ping target within a session, including its ping data buffer.
	/// </summary>
	public class PingTarget
	{
		public int Id { get; }
		public IPAddress Address { get; }
		public string DisplayName { get; set; }
		public bool HasAtLeastOneSuccess { get; set; } = false;

		private readonly PingLog[] pings;
		private long _nextIndexOffset = -1;

		public PingTarget(int id, IPAddress address, string displayName, int cacheSize)
		{
			Id = id;
			Address = address;
			DisplayName = displayName ?? address.ToString();
			pings = new PingLog[cacheSize];
		}

		/// <summary>
		/// Gets the total number of pings recorded (may exceed buffer size for wrap-around).
		/// </summary>
		public long TotalPingsRecorded => Interlocked.Read(ref _nextIndexOffset) + 1;

		/// <summary>
		/// Gets the cache size.
		/// </summary>
		public int CacheSize => pings.Length;

		/// <summary>
		/// Clears and reserves the next offset in the circular buffer. Returns the offset.
		/// </summary>
		public long ClearNextOffset()
		{
			long newOffset = Interlocked.Increment(ref _nextIndexOffset);
			pings[newOffset % pings.Length] = null;
			return newOffset;
		}

		/// <summary>
		/// Adds a ping log to the specified offset in the circular buffer.
		/// </summary>
		public void AddPingLogToSpecificOffset(long offset, PingLog pingLog)
		{
			pings[offset % pings.Length] = pingLog;
		}

		/// <summary>
		/// Gets ping data for a range of the most recent pings.
		/// Returns an array of PingLog (may contain nulls for pending results).
		/// </summary>
		/// <param name="count">Number of most-recent pings to return.</param>
		/// <param name="offset">Offset from the most recent ping (0 = most recent).</param>
		public PingLog[] GetRecentPings(int count, int offset = 0)
		{
			long currentOffset = Interlocked.Read(ref _nextIndexOffset);
			if (currentOffset < 0)
				return Array.Empty<PingLog>();

			long available = Math.Min(currentOffset + 1, pings.Length);
			if (offset >= available)
				return Array.Empty<PingLog>();

			int actualCount = (int)Math.Min(count, available - offset);
			PingLog[] result = new PingLog[actualCount];

			for (int i = 0; i < actualCount; i++)
			{
				long idx = currentOffset - offset - (actualCount - 1 - i);
				if (idx < 0)
					continue;
				result[i] = pings[idx % pings.Length];
			}
			return result;
		}

		/// <summary>
		/// Gets the most recent ping log entry that was added since the specified offset.
		/// Returns null if no new pings are available.
		/// </summary>
		public PingLog[] GetPingsSince(long sinceOffset)
		{
			long currentOffset = Interlocked.Read(ref _nextIndexOffset);
			if (currentOffset < 0 || sinceOffset >= currentOffset)
				return Array.Empty<PingLog>();

			long count = currentOffset - sinceOffset;
			if (count > pings.Length)
				count = pings.Length;

			PingLog[] result = new PingLog[(int)count];
			for (int i = 0; i < count; i++)
			{
				long idx = sinceOffset + 1 + i;
				result[i] = pings[idx % pings.Length];
			}
			return result;
		}
	}

	/// <summary>
	/// Manages an active ping session for a PingConfiguration, decoupled from any UI.
	/// </summary>
	public class PingSession
	{
		public PingConfiguration Configuration { get; }
		public bool IsRunning { get; private set; } = false;
		public long SuccessfulPings => Interlocked.Read(ref _successfulPings);
		public long FailedPings => Interlocked.Read(ref _failedPings);

		private long _successfulPings = 0;
		private long _failedPings = 0;

		private readonly Settings settings;
		private readonly ConcurrentDictionary<int, PingTarget> targets = new ConcurrentDictionary<int, PingTarget>();
		private BackgroundWorker controllerWorker;
		private volatile int pingDelay = 1000;
		private int graphSortingCounter = 0;
		private bool clearedDeadHosts = false;

		/// <summary>
		/// Raised when a new ping target is added.
		/// Args: target id.
		/// </summary>
		public event Action<PingTarget> TargetAdded;

		/// <summary>
		/// Raised when a target is removed.
		/// </summary>
		public event Action<int> TargetRemoved;

		/// <summary>
		/// Raised when a ping result is received.
		/// Args: target id, PingLog.
		/// </summary>
		public event Action<int, PingLog> PingResultReceived;

		/// <summary>
		/// Raised when the session status changes.
		/// </summary>
		public event Action<string> StatusChanged;

		/// <summary>
		/// Raised when a log entry is created.
		/// </summary>
		public event Action<string> LogCreated;

		/// <summary>
		/// Raised when the session stops (either by user or error).
		/// </summary>
		public event Action SessionStopped;

		public PingSession(PingConfiguration config, Settings settings)
		{
			Configuration = config;
			this.settings = settings;
		}

		/// <summary>
		/// Gets all current ping targets.
		/// </summary>
		public IReadOnlyCollection<PingTarget> GetTargets()
		{
			return targets.Values.ToList().AsReadOnly();
		}

		/// <summary>
		/// Gets a specific target by ID.
		/// </summary>
		public PingTarget GetTarget(int id)
		{
			targets.TryGetValue(id, out PingTarget target);
			return target;
		}

		public void Start()
		{
			if (IsRunning) return;
			IsRunning = true;

			// Compute ping delay
			if (Configuration.rate == 0)
				pingDelay = 0;
			else if (Configuration.pingsPerSecond)
				pingDelay = Math.Max(100, (int)(1000.0 / Configuration.rate));
			else
				pingDelay = Math.Max(100, (int)(1000.0 * Configuration.rate));

			controllerWorker = new BackgroundWorker();
			controllerWorker.WorkerSupportsCancellation = true;
			controllerWorker.DoWork += ControllerWorker_DoWork;
			controllerWorker.RunWorkerCompleted += (s, e) => { };

			string host = Configuration.GetHostString();
			bool traceRoute = Configuration.doTraceRoute;
			bool reverseDnsLookup = Configuration.reverseDnsLookup;
			bool preferIpv4 = Configuration.GetPreferIPv4();

			if (Configuration.hosts != null && Configuration.hosts.Count > 1)
				traceRoute = false;

			StatusChanged?.Invoke("Starting...");
			controllerWorker.RunWorkerAsync(new object[] { controllerWorker, host, traceRoute, reverseDnsLookup, preferIpv4, Configuration.monitorUnresponsiveHops });
		}

		public void Stop()
		{
			if (!IsRunning) return;
			IsRunning = false;
			controllerWorker?.CancelAsync();
			StatusChanged?.Invoke("Idle");
			SessionStopped?.Invoke();
		}

		private void ControllerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			object[] args = (object[])e.Argument;
			BackgroundWorker self = (BackgroundWorker)args[0];
			string host = (string)args[1];
			bool traceRoute = (bool)args[2];
			bool reverseDnsLookup = (bool)args[3];
			bool preferIpv4 = (bool)args[4];
			bool monitorUnresponsiveHops = (bool)args[5];

			Interlocked.Exchange(ref _successfulPings, 0);
			Interlocked.Exchange(ref _failedPings, 0);
			targets.Clear();
			clearedDeadHosts = false;
			graphSortingCounter = 0;

			try
			{
				string[] addresses = host.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (addresses.Length == 0)
				{
					LogCreated?.Invoke("Unable to start pinging because the host input is empty.");
					return;
				}

				LogCreated?.Invoke("Initializing pings to " + host);

				if (addresses.Length > 1)
				{
					clearedDeadHosts = true;
					foreach (string address in addresses)
					{
						if (self.CancellationPending) break;
						try
						{
							IPAddress ip = StringToIp(address.Trim(), preferIpv4, out string hostName);
							if (self.CancellationPending) break;
							AddPingTarget(ip, hostName, reverseDnsLookup);
						}
						catch (Exception ex)
						{
							LogCreated?.Invoke(ex.Message);
						}
					}
				}
				else if (traceRoute)
				{
					StatusChanged?.Invoke("Tracing Route");
					LogCreated?.Invoke("Tracing route ...");
					try
					{
						IPAddress target = StringToIp(addresses[0], preferIpv4, out string hostName);
						foreach (TracertEntry entry in Tracert.Trace(target, 64, 5000, settings.pingPayloadSizeBytes))
						{
							if (self.CancellationPending) break;
							LogCreated?.Invoke(entry.ToString());
							AddPingTarget(entry.Address, null, reverseDnsLookup);
						}
					}
					catch (Exception ex)
					{
						LogCreated?.Invoke(ex.Message);
					}
				}
				else
				{
					try
					{
						IPAddress ip = StringToIp(addresses[0], preferIpv4, out string hostName);
						AddPingTarget(ip, hostName, reverseDnsLookup);
					}
					catch (Exception ex)
					{
						LogCreated?.Invoke(ex.Message);
					}
				}

				if (targets.Count == 0)
				{
					LogCreated?.Invoke("No hosts could be resolved. Pinging will not start.");
					return;
				}

				LogCreated?.Invoke("Now beginning pings");
				StatusChanged?.Invoke("Pinging Active");

				Stopwatch sw = null;
				long numberOfPingLoopIterations = 0;
				DateTime tenPingsAt = DateTime.MinValue;

				while (!self.CancellationPending)
				{
					try
					{
						if (!clearedDeadHosts && tenPingsAt != DateTime.MinValue && tenPingsAt.AddSeconds(10) < DateTime.Now)
						{
							if (targets.Count > 1 && !monitorUnresponsiveHops)
							{
								List<int> toRemove = new List<int>();
								foreach (var kvp in targets)
								{
									if (!kvp.Value.HasAtLeastOneSuccess)
										toRemove.Add(kvp.Key);
								}
								foreach (int id in toRemove)
								{
									if (targets.TryRemove(id, out _))
										TargetRemoved?.Invoke(id);
								}
							}
							clearedDeadHosts = true;
						}

						while (!self.CancellationPending && pingDelay <= 0)
							Thread.Sleep(100);

						if (!self.CancellationPending)
						{
							int msToWait = sw == null ? 0 : (int)(pingDelay - sw.ElapsedMilliseconds);
							while (!self.CancellationPending && msToWait > 0)
							{
								Thread.Sleep(Math.Min(msToWait, 100));
								msToWait = sw == null ? 0 : (int)(pingDelay - sw.ElapsedMilliseconds);
							}
							if (!self.CancellationPending)
							{
								if (sw == null) sw = Stopwatch.StartNew();
								else sw.Restart();

								DateTime lastPingAt = DateTime.Now;
								foreach (var kvp in targets)
								{
									PingTarget pt = kvp.Value;
									long offset = pt.ClearNextOffset();
									Ping pinger = PingInstancePool.Get();
									pinger.PingCompleted += pinger_PingCompleted;
									pinger.SendAsync(pt.Address, 5000, PingBufferStatic.GetBuffer(settings.pingPayloadSizeBytes),
										new object[] { lastPingAt, offset, pt, pinger });
								}
							}
						}
					}
					catch (Exception) { }

					numberOfPingLoopIterations++;
					if (numberOfPingLoopIterations == 10)
						tenPingsAt = DateTime.Now;
				}
			}
			catch (Exception ex)
			{
				LogCreated?.Invoke("Error during ping operation: " + ex.Message);
			}
			finally
			{
				LogCreated?.Invoke("Shutting down pings to " + host);
				if (IsRunning)
					Stop();
			}
		}

		private void pinger_PingCompleted(object sender, PingCompletedEventArgs e)
		{
			try
			{
				object[] args = (object[])e.UserState;
				DateTime time = (DateTime)args[0];
				long pingNum = (long)args[1];
				PingTarget pt = (PingTarget)args[2];
				Ping pinger = (Ping)args[3];

				pinger.PingCompleted -= pinger_PingCompleted;
				PingInstancePool.Recycle(pinger);

				PingLog log;
				if (e.Cancelled)
				{
					log = new PingLog(time, 0, IPStatus.Unknown);
					Interlocked.Increment(ref _failedPings);
				}
				else
				{
					log = new PingLog(time, (short)e.Reply.RoundtripTime, e.Reply.Status);
					if (e.Reply.Status != IPStatus.Success)
						Interlocked.Increment(ref _failedPings);
					else
					{
						if (!clearedDeadHosts)
							pt.HasAtLeastOneSuccess = true;
						Interlocked.Increment(ref _successfulPings);
					}
				}

				pt.AddPingLogToSpecificOffset(pingNum, log);
				PingResultReceived?.Invoke(pt.Id, log);
			}
			catch (Exception) { }
		}

		private void AddPingTarget(IPAddress ipAddress, string name, bool reverseDnsLookup)
		{
			if (ipAddress == null) return;

			int id = Interlocked.Increment(ref graphSortingCounter) - 1;
			string displayName = ipAddress.ToString();

			if (!string.IsNullOrWhiteSpace(name))
				displayName = name + " [" + displayName + "]";

			PingTarget pt = new PingTarget(id, ipAddress, displayName, settings.cacheSize);
			targets[id] = pt;
			TargetAdded?.Invoke(pt);

			if (reverseDnsLookup && string.IsNullOrWhiteSpace(name))
			{
				ThreadPool.QueueUserWorkItem(_ =>
				{
					try
					{
						string hostName = Dns.GetHostEntry(ipAddress).HostName;
						if (!string.IsNullOrWhiteSpace(hostName))
							pt.DisplayName = hostName + " [" + ipAddress + "]";
					}
					catch { }
				});
			}
		}

		private static IPAddress StringToIp(string address, bool preferIpv4, out string hostName)
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
