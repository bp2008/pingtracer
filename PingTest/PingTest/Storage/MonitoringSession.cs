using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Storage
{
	public enum SessionMode : byte
	{
		/// <summary>Every TTL from 1 to the destination is pinged each cycle (continuous traceroute).</summary>
		TraceRoute = 1,
		/// <summary>Only the destination is pinged. Data is stored in TTL slot 0.</summary>
		Direct = 2,
	}

	/// <summary>
	/// All data collected while monitoring one target: one <see cref="HopSeries"/> per TTL, the table of responding
	/// addresses, and route-change events.
	/// <para>
	/// Thread safety: every member is protected by <see cref="DataLock"/>.  Public methods take the lock themselves
	/// (the lock is re-entrant).  Events are raised while the lock is held so that subscribers observe changes in
	/// exactly the order they happened; subscribers must therefore do nothing but queue work.
	/// </para>
	/// </summary>
	public sealed class MonitoringSession
	{
		private static int nextId = 0;

		/// <summary>An address reappearing at a hop after this long without being seen there produces a new route event.</summary>
		public const long AddressReappearMs = 10 * 60 * 1000;
		/// <summary>New addresses at a hop within this long after the hop first replied are considered part of route discovery.</summary>
		public const long HopLearningMs = 30 * 1000;
		private const int MaxRouteEvents = 100000;

		public readonly object DataLock = new object();

		public readonly int Id;
		public readonly IPAddress TargetAddress;
		public readonly string DisplayName;
		public readonly long StartUnixMs;
		public readonly SessionMode Mode;

		/// <summary>True if this session was loaded from disk and will never receive new data.</summary>
		public bool IsReadOnly { get; }
		/// <summary>True while a monitor is actively pinging for this session.</summary>
		public bool IsRunning { get; private set; }
		public int IntervalMs { get; private set; }
		public int TimeoutMs { get; private set; }
		/// <summary>TTL at which the destination replies, or 0 if unknown. Always 0 in Direct mode.</summary>
		public int DestinationTtl { get; private set; }
		/// <summary>Session-relative time of the last record (approximate session end for loaded sessions).</summary>
		public long LastRecordTimeMs { get; private set; }

		public readonly AddressTable Addresses = new AddressTable();
		private readonly HopSeries[] hops = new HopSeries[256];
		private readonly bool[] replied = new bool[256];
		private readonly HopRouteState[] routeState = new HopRouteState[256];
		private readonly List<RouteEvent> routeEvents = new List<RouteEvent>();

		/// <summary>Optional reverse DNS resolver. Called once per new address. Null disables hostname lookups.</summary>
		public Func<IPAddress, Task<string>> HostnameResolver { get; set; }

		/// <summary>Raised (under <see cref="DataLock"/>) when a pending record is completed.</summary>
		public event Action<MonitoringSession, int, PingSample> SampleCompleted;
		/// <summary>Raised (under <see cref="DataLock"/>) when a completed record is voided. Carries the record as it was before.</summary>
		public event Action<MonitoringSession, int, PingSample> SampleVoided;
		public event Action<MonitoringSession, ushort, IPAddress> AddressAdded;
		public event Action<MonitoringSession, ushort, string> HostnameResolved;
		public event Action<MonitoringSession, RouteEvent> RouteEventAdded;
		/// <summary>Raised when destination TTL, replied TTLs, interval, or running state change.</summary>
		public event Action<MonitoringSession> StateChanged;

		public MonitoringSession(IPAddress target, string displayName, SessionMode mode, int intervalMs, int timeoutMs, long startUnixMs = 0, bool readOnly = false)
		{
			Id = Interlocked.Increment(ref nextId);
			TargetAddress = target ?? throw new ArgumentNullException(nameof(target));
			DisplayName = string.IsNullOrWhiteSpace(displayName) ? target.ToString() : displayName;
			Mode = mode;
			IntervalMs = intervalMs;
			TimeoutMs = timeoutMs;
			StartUnixMs = startUnixMs > 0 ? startUnixMs : ServerClock.NowUnixMs;
			IsReadOnly = readOnly;
		}

		/// <summary>Current session-relative time in milliseconds.</summary>
		public long NowMs => Math.Max(0, ServerClock.NowUnixMs - StartUnixMs);

		public long ToUnixMs(long sessionMs) => StartUnixMs + sessionMs;

		#region Hop access

		/// <summary>Returns the series for a TTL, or null if nothing has been recorded there.</summary>
		public HopSeries GetHop(int ttl)
		{
			lock (DataLock)
				return (uint)ttl < 256 ? hops[ttl] : null;
		}

		private HopSeries GetOrCreateHop(int ttl)
		{
			HopSeries h = hops[ttl];
			if (h == null)
				hops[ttl] = h = new HopSeries(ttl);
			return h;
		}

		/// <summary>True if the TTL has ever produced a successful reply in this session.</summary>
		public bool HasReplied(int ttl)
		{
			lock (DataLock)
				return (uint)ttl < 256 && replied[ttl];
		}

		/// <summary>TTLs that have ever replied, ascending.</summary>
		public List<int> GetRepliedTtls()
		{
			lock (DataLock)
			{
				List<int> list = new List<int>();
				for (int i = 0; i < 256; i++)
					if (replied[i]) list.Add(i);
				return list;
			}
		}

		/// <summary>Series that currently exist, in TTL order.</summary>
		public List<HopSeries> GetHops()
		{
			lock (DataLock)
			{
				List<HopSeries> list = new List<HopSeries>();
				foreach (HopSeries h in hops)
					if (h != null) list.Add(h);
				return list;
			}
		}

		#endregion

		#region Recording

		/// <summary>Records that a ping is being sent now to the given TTL, returning a handle used to complete it.</summary>
		public RecordHandle BeginPing(int ttl)
		{
			lock (DataLock)
			{
				long now = NowMs;
				if (now > LastRecordTimeMs) LastRecordTimeMs = now;
				return GetOrCreateHop(ttl).Begin(now);
			}
		}

		/// <summary>
		/// Completes a pending ping.  <paramref name="from"/> is the replying address (null for loss).
		/// </summary>
		public bool CompletePing(int ttl, RecordHandle handle, bool success, long rttMs, IPAddress from)
		{
			lock (DataLock)
			{
				HopSeries hop = hops[ttl];
				if (hop == null)
					return false;
				ushort addr = AddressTable.None;
				ushort rtt = PingRecordStatus.Lost;
				if (success)
				{
					addr = GetAddressId(from);
					rtt = PingRecordStatus.ClampRtt(rttMs);
				}
				if (!hop.Complete(handle, rtt, addr, out PingSample completed))
					return false;
				if (success)
					OnReply(ttl, addr, completed.TimeMs);
				SampleCompleted?.Invoke(this, ttl, completed);
				return true;
			}
		}

		/// <summary>Voids a record (completed or pending) so that it is excluded from all statistics.</summary>
		public bool VoidPing(int ttl, RecordHandle handle)
		{
			lock (DataLock)
			{
				HopSeries hop = hops[ttl];
				if (hop == null || !hop.MakeVoid(handle, out PingSample previous))
					return false;
				if (previous.IsCompleted)
					SampleVoided?.Invoke(this, ttl, previous);
				if (previous.IsSuccess && hop.ReplyCount == 0 && replied[ttl])
				{
					// The only replies at this TTL turned out to be bogus (e.g. echoes from beyond the destination).
					replied[ttl] = false;
					StateChanged?.Invoke(this);
				}
				return true;
			}
		}

		/// <summary>Appends a completed record (disk loading).</summary>
		internal void LoadRecord(int ttl, PingSample record)
		{
			lock (DataLock)
			{
				GetOrCreateHop(ttl).AppendCompleted(record);
				if (record.TimeMs > LastRecordTimeMs) LastRecordTimeMs = record.TimeMs;
				if (record.IsSuccess)
					replied[ttl] = true;
			}
		}

		internal void LoadRouteEvent(RouteEvent e)
		{
			lock (DataLock)
			{
				routeEvents.Add(e);
				if (e.Kind == RouteEventKind.DestinationTtl)
					DestinationTtl = e.Ttl;
			}
		}

		private ushort GetAddressId(IPAddress from)
		{
			ushort id = Addresses.GetOrAdd(from, out bool added);
			if (added)
			{
				AddressAdded?.Invoke(this, id, from);
				StartHostnameLookup(id, from);
			}
			return id;
		}

		/// <summary>Ensures an address is in the table (e.g. the target) and returns its id.</summary>
		public ushort EnsureAddress(IPAddress address)
		{
			lock (DataLock)
				return GetAddressId(address);
		}

		private void OnReply(int ttl, ushort addr, long timeMs)
		{
			if (!replied[ttl])
			{
				replied[ttl] = true;
				StateChanged?.Invoke(this);
			}
			HopRouteState st = routeState[ttl] ??= new HopRouteState();
			if (st.FirstReplyMs < 0)
				st.FirstReplyMs = timeMs;
			if (!st.LastSeen.TryGetValue(addr, out long last) || timeMs - last > AddressReappearMs)
			{
				bool initial = timeMs - st.FirstReplyMs < HopLearningMs;
				AddRouteEvent(new RouteEvent(timeMs, RouteEventKind.HopAddress, (byte)ttl, addr, initial));
			}
			st.LastSeen[addr] = timeMs;
		}

		private void AddRouteEvent(RouteEvent e)
		{
			if (routeEvents.Count >= MaxRouteEvents)
				routeEvents.RemoveRange(0, MaxRouteEvents / 10);
			routeEvents.Add(e);
			RouteEventAdded?.Invoke(this, e);
		}

		/// <summary>Sets the destination TTL, emitting a route event when it changes.</summary>
		/// <param name="ttl">New destination TTL.</param>
		/// <param name="initial">True if this is part of initial route discovery rather than a route change.</param>
		public void SetDestinationTtl(int ttl, bool initial = false)
		{
			lock (DataLock)
			{
				if (ttl == DestinationTtl)
					return;
				int old = DestinationTtl;
				DestinationTtl = ttl;
				AddRouteEvent(new RouteEvent(NowMs, RouteEventKind.DestinationTtl, (byte)ttl, (ushort)old, initial || old == 0));
				StateChanged?.Invoke(this);
			}
		}

		public void SetRunning(bool running)
		{
			lock (DataLock)
			{
				if (IsRunning == running) return;
				IsRunning = running;
				StateChanged?.Invoke(this);
			}
		}

		public void SetIntervalMs(int intervalMs)
		{
			lock (DataLock)
			{
				if (IntervalMs == intervalMs) return;
				IntervalMs = intervalMs;
				StateChanged?.Invoke(this);
			}
		}

		public List<RouteEvent> GetRouteEvents()
		{
			lock (DataLock)
				return new List<RouteEvent>(routeEvents);
		}

		#endregion

		#region Query / Prune

		/// <summary>
		/// Aggregates all hops into time-aligned columns.  Must be called with <see cref="DataLock"/> held if the caller needs
		/// to atomically publish the result relative to live updates (this method takes the lock itself as well).
		/// </summary>
		public List<(int ttl, ColumnStats stats, int nonEmpty)> Query(long startCol, int msPerCol, int colCount)
		{
			List<(int, ColumnStats, int)> results = new List<(int, ColumnStats, int)>();
			if (msPerCol <= 0 || colCount <= 0)
				return results;
			lock (DataLock)
			{
				for (int ttl = 0; ttl < 256; ttl++)
				{
					HopSeries h = hops[ttl];
					if (h == null || h.Count == 0)
						continue;
					ColumnStats stats = new ColumnStats(colCount);
					int nonEmpty = h.Aggregate(StartUnixMs, startCol, msPerCol, stats);
					if (nonEmpty > 0)
						results.Add((ttl, stats, nonEmpty));
				}
			}
			return results;
		}

		/// <summary>Discards data older than <paramref name="maxAge"/>.</summary>
		public void PruneOlderThan(TimeSpan maxAge)
		{
			lock (DataLock)
			{
				long cutoff = ServerClock.NowUnixMs - (long)maxAge.TotalMilliseconds - StartUnixMs;
				if (cutoff <= 0)
					return;
				foreach (HopSeries h in hops)
					h?.PruneOlderThan(cutoff);
				int remove = 0;
				while (remove < routeEvents.Count && routeEvents[remove].TimeMs < cutoff)
					remove++;
				if (remove > 0)
					routeEvents.RemoveRange(0, remove);
			}
		}

		#endregion

		#region Reverse DNS

		private static readonly ConcurrentDictionary<IPAddress, string> dnsCache = new ConcurrentDictionary<IPAddress, string>();
		private static readonly SemaphoreSlim dnsConcurrency = new SemaphoreSlim(4);

		private void StartHostnameLookup(ushort id, IPAddress address)
		{
			Func<IPAddress, Task<string>> resolver = HostnameResolver;
			if (resolver == null)
				return;
			if (dnsCache.TryGetValue(address, out string cached))
			{
				if (!string.IsNullOrEmpty(cached))
				{
					Addresses.SetHostname(id, cached);
					HostnameResolved?.Invoke(this, id, cached);
				}
				return;
			}
			_ = Task.Run(async () =>
			{
				string host = null;
				await dnsConcurrency.WaitAsync().ConfigureAwait(false);
				try
				{
					host = await resolver(address).ConfigureAwait(false);
				}
				catch { }
				finally
				{
					dnsConcurrency.Release();
				}
				if (string.IsNullOrWhiteSpace(host) || host == address.ToString())
					host = "";
				dnsCache[address] = host;
				if (host.Length > 0)
				{
					lock (DataLock)
					{
						Addresses.SetHostname(id, host);
						HostnameResolved?.Invoke(this, id, host);
					}
				}
			});
		}

		/// <summary>Sets a hostname directly (disk loading).</summary>
		internal void LoadHostname(ushort id, string hostname)
		{
			lock (DataLock)
				Addresses.SetHostname(id, hostname);
		}

		#endregion

		private sealed class HopRouteState
		{
			public long FirstReplyMs = -1;
			public readonly Dictionary<ushort, long> LastSeen = new Dictionary<ushort, long>();
		}
	}
}
