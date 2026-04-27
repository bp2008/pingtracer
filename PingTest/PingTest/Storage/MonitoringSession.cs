using System;
using System.Collections.Generic;
using System.Net;
using PingTracer.TraceRoute;

namespace PingTracer.Storage
{
	/// <summary>
	/// Top-level container for all data collected while monitoring a target.
	/// HopData is a fixed 255-element array initialized at construction; the array
	/// never changes, so callers can access a slot by index without locking.
	/// Modifications to HopHistory.Series must be guarded by locking on the HopHistory instance.
	/// </summary>
	public class MonitoringSession
	{
		private readonly object _sessionLock = new object();

		public readonly IPAddress TargetAddress;
		public readonly string DisplayName;
		public readonly DateTime StartTimeUtc;

		/// <summary>255 hop slots, 0-based. HopData[0] = TTL 1, etc.</summary>
		public readonly HopHistory[] HopData;

		public readonly List<RouteSnapshot> RouteHistory = new List<RouteSnapshot>();

		public RouteSnapshot CurrentRoute { get; internal set; }

		public event Action<RouteSnapshot, RouteSnapshot> RouteChanged;
		public event Action<DateTime> TraceResultRecorded;

		/// <summary>Raised whenever a new HopTimeSeries is appended to a HopHistory.Series list.</summary>
		public event Action<HopTimeSeries> HopSeriesAdded;

		public MonitoringSession(IPAddress targetAddress, string displayName)
		{
			TargetAddress = targetAddress ?? throw new ArgumentNullException(nameof(targetAddress));
			DisplayName = displayName ?? string.Empty;
			StartTimeUtc = DateTime.UtcNow;

			HopData = new HopHistory[255];
			for (int i = 0; i < 255; i++)
				HopData[i] = new HopHistory((byte)i);
		}

		/// <summary>
		/// Processes one completed traceroute cycle: updates HopTimeSeries entries,
		/// builds a new RouteSnapshot, fires RouteChanged if the topology changed.
		/// </summary>
		public void RecordTraceResult(TraceRouteHostResult[] results, DateTime timestamp)
		{
			if (results == null || results.Length == 0)
				return;

			// Index results by TTL for O(1) lookup
			var byTtl = new Dictionary<byte, TraceRouteHostResult>(results.Length);
			byte maxTtl = 0;
			foreach (var r in results)
			{
				byTtl[r.ttl] = r;
				if (r.ttl > maxTtl) maxTtl = r.ttl;
			}

			// Determine the lowest TTL at which the destination responded (if any).
			// Hops at deeper TTLs see the destination only because the probe packets
			// reach the destination with leftover TTL — they should not be treated as
			// real intermediate hops. Cap processing at destTtl when found.
			byte? destTtl = null;
			for (byte ttl = 1; ttl <= maxTtl; ttl++)
			{
				if (!byTtl.TryGetValue(ttl, out var r)) continue;
				if (r.success && r.replyFrom != null && r.replyFrom.Equals(TargetAddress))
				{
					destTtl = ttl;
					break;
				}
			}
			byte effectiveMaxTtl = destTtl ?? maxTtl;

			var snapshotHops = new HopTimeSeries[effectiveMaxTtl];
			List<HopTimeSeries> addedSeries = null;

			for (byte ttl = 1; ttl <= effectiveMaxTtl; ttl++)
			{
				int hopIndex = ttl - 1;
				byTtl.TryGetValue(ttl, out var result);

				bool responded = result != null
					&& result.success
					&& result.replyFrom != null
					&& !result.replyFrom.Equals(IPAddress.Any);

				HopHistory history = HopData[hopIndex];

				lock (history)
				{
					if (responded)
					{
						HopTimeSeries active = history.ActiveSeries;
						if (active != null && active.Address.Equals(result.replyFrom))
						{
							// Same address — reuse.
							snapshotHops[hopIndex] = active;
						}
						else
						{
							// Address changed (or first appearance). Close any active series.
							if (active != null)
								active.EndTimeUtc = timestamp;

							var newSeries = new HopTimeSeries((byte)hopIndex, result.replyFrom, timestamp, startDnsLookup: true);
							history.Series.Add(newSeries);
							snapshotHops[hopIndex] = newSeries;
							(addedSeries ??= new List<HopTimeSeries>()).Add(newSeries);
						}
					}
					else
					{
						// Non-response: keep the previously active series active. A single
						// missed reply (or a routine ICMP-rate-limited intermediate hop)
						// must not flap the series. The series will only be closed when
						// the responding address actually changes (handled above) or when
						// the destination responds at an earlier TTL (handled below).
						snapshotHops[hopIndex] = history.ActiveSeries;
					}
				}
			}

			// When the destination is known, deactivate any pre-existing series at
			// deeper TTLs (e.g. duplicates created during the very first cycle before
			// maxHops shrunk).
			if (destTtl.HasValue)
			{
				for (int hopIndex = destTtl.Value; hopIndex < HopData.Length; hopIndex++)
				{
					HopHistory history = HopData[hopIndex];
					lock (history)
					{
						HopTimeSeries active = history.ActiveSeries;
						if (active != null)
							active.EndTimeUtc = timestamp;
					}
				}
			}

			var newSnapshot = new RouteSnapshot(timestamp, snapshotHops);
			RouteSnapshot oldRoute;

			lock (_sessionLock)
			{
				oldRoute = CurrentRoute;
				RouteHistory.Add(newSnapshot);
				CurrentRoute = newSnapshot;
			}

			// Fire events outside all locks to prevent deadlocks.
			if (addedSeries != null)
			{
				var handler = HopSeriesAdded;
				if (handler != null)
				{
					foreach (var s in addedSeries)
						handler(s);
				}
			}

			var changes = newSnapshot.DiffFrom(oldRoute);
			if (changes.Count > 0)
				RouteChanged?.Invoke(oldRoute, newSnapshot);

			TraceResultRecorded?.Invoke(timestamp);
		}

		/// <summary>Returns all currently active HopTimeSeries across all hop positions.</summary>
		public IEnumerable<HopTimeSeries> GetActiveHops()
		{
			foreach (HopHistory history in HopData)
			{
				HopTimeSeries active;
				lock (history)
					active = history.ActiveSeries;

				if (active != null)
					yield return active;
			}
		}

		/// <summary>
		/// Prunes all HopTimeSeries older than maxAge and removes any fully-emptied
		/// inactive series from HopHistory.Series. Trims RouteHistory but always
		/// retains the single most-recent snapshot that predates the cutoff.
		/// </summary>
		public void PruneOlderThan(TimeSpan maxAge)
		{
			DateTime cutoff = DateTime.UtcNow - maxAge;

			foreach (HopHistory history in HopData)
			{
				lock (history)
				{
					for (int j = history.Series.Count - 1; j >= 0; j--)
					{
						HopTimeSeries series = history.Series[j];
						series.PruneOlderThan(cutoff);

						if (series.RecordCount == 0 && !series.IsActive)
							history.Series.RemoveAt(j);
					}
				}
			}

			lock (_sessionLock)
			{
				// Find the last entry strictly before the cutoff.
				int lastBeforeCutoff = -1;
				for (int i = 0; i < RouteHistory.Count; i++)
				{
					if (RouteHistory[i].TimestampUtc < cutoff)
						lastBeforeCutoff = i;
					else
						break;
				}

				// Remove all entries before lastBeforeCutoff (keep it as the anchor).
				if (lastBeforeCutoff > 0)
					RouteHistory.RemoveRange(0, lastBeforeCutoff);
			}
		}

		/// <summary>
		/// Returns all HopTimeSeries whose time ranges overlap [start, end].
		/// </summary>
		public IEnumerable<HopTimeSeries> GetAggregatedData(DateTime start, DateTime end, int maxPointsPerHop)
		{
			foreach (HopHistory history in HopData)
			{
				List<HopTimeSeries> snapshot;
				lock (history)
					snapshot = new List<HopTimeSeries>(history.Series);

				foreach (HopTimeSeries series in snapshot)
				{
					DateTime seriesEnd = series.EndTimeUtc ?? DateTime.MaxValue;
					bool overlaps = series.StartTimeUtc <= end && seriesEnd >= start;
					if (overlaps)
						yield return series;
				}
			}
		}
	}
}
