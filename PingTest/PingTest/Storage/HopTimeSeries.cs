using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace PingTracer.Storage
{
	/// <summary>
	/// Time-series ping data for a specific (hop number, IP address) pair
	/// during a contiguous monitoring period.
	/// </summary>
	public class HopTimeSeries
	{
		private readonly ChunkedSeriesBuffer<PingRecord> _records = new ChunkedSeriesBuffer<PingRecord>();
		private DateTime _lastRecordedTimestampUtc;
		private readonly object _writeLock = new object();

		public readonly byte HopNumber;
		public readonly IPAddress Address;
		public readonly DateTime StartTimeUtc;

		public string Hostname { get; private set; }
		public DateTime? EndTimeUtc { get; set; }
		public bool IsActive => EndTimeUtc == null;
		public long RecordCount => _records.ActiveCount;

		public event Action<HopTimeSeries> HostnameUpdated;

		public HopTimeSeries(byte hopNumber, IPAddress address, DateTime startTimeUtc, bool startDnsLookup = false)
		{
			HopNumber = hopNumber;
			Address = address ?? throw new ArgumentNullException(nameof(address));
			StartTimeUtc = startTimeUtc;
			_lastRecordedTimestampUtc = startTimeUtc;

			if (startDnsLookup)
				StartDnsLookup();
		}

		private void StartDnsLookup()
		{
			_ = Task.Run(async () =>
			{
				try
				{
					var entry = await Dns.GetHostEntryAsync(Address).ConfigureAwait(false);
					SetHostname(entry.HostName);
				}
				catch { }
			});
		}

		public void SetHostname(string hostname)
		{
			Hostname = hostname;
			HostnameUpdated?.Invoke(this);
		}

		/// <summary>
		/// Records a new ping as pending (in-flight). Returns a handle for later completion.
		/// Clamps backward clock jumps to preserve non-decreasing timestamp order.
		/// </summary>
		public RecordHandle BeginPendingRecord(DateTime sentTimestampUtc)
		{
			lock (_writeLock)
			{
				if (sentTimestampUtc < _lastRecordedTimestampUtc)
					sentTimestampUtc = _lastRecordedTimestampUtc;

				_lastRecordedTimestampUtc = sentTimestampUtc;

				uint relativeMs = (uint)(sentTimestampUtc - StartTimeUtc).TotalMilliseconds;
				var record = new PingRecord { RelativeTimeMs = relativeMs, RoundTripMs = PingRecordStatus.Pending };
				return _records.Append(record);
			}
		}

		/// <summary>
		/// Updates the pending record identified by handle with the actual RTT or status code.
		/// </summary>
		public void CompleteRecord(RecordHandle handle, ushort rttOrStatusCode)
		{
			lock (_writeLock)
			{
				ref PingRecord record = ref _records.GetRef(handle);
				record.RoundTripMs = rttOrStatusCode;
			}
		}

		/// <summary>
		/// Enumerates all records in the given UTC time range (inclusive on both ends).
		/// Pending records are included with their status code.
		/// </summary>
		public IEnumerable<(DateTime timestamp, ushort rtt)> QueryRange(DateTime start, DateTime end)
		{
			if (end < StartTimeUtc) yield break;
			if (start > (EndTimeUtc ?? DateTime.MaxValue)) yield break;

			uint startRelativeMs = start <= StartTimeUtc
				? 0
				: (uint)(start - StartTimeUtc).TotalMilliseconds;
			uint endRelativeMs = (uint)Math.Max(0, (end - StartTimeUtc).TotalMilliseconds);

			RecordHandle? startHandle = _records.LowerBound(r => r.RelativeTimeMs, startRelativeMs);
			if (startHandle == null) yield break;

			foreach (var (_, record) in _records.EnumerateFrom(startHandle.Value))
			{
				if (record.RelativeTimeMs > endRelativeMs) yield break;
				yield return (StartTimeUtc.AddMilliseconds(record.RelativeTimeMs), record.RoundTripMs);
			}
		}

		/// <summary>
		/// Returns aggregated statistics for the given time range.
		/// When record count exceeds maxPoints, results are bucketed into maxPoints buckets.
		/// Pending records are excluded from latency statistics.
		/// </summary>
		public AggregateResult AggregateRange(DateTime start, DateTime end, int maxPoints)
		{
			if (maxPoints < 1) throw new ArgumentOutOfRangeException(nameof(maxPoints));

			var raw = new List<(DateTime timestamp, ushort rtt)>(capacity: 256);
			foreach (var item in QueryRange(start, end))
				raw.Add(item);

			if (raw.Count == 0)
				return new AggregateResult { Series = this, Points = Array.Empty<AggregatedPoint>() };

			if (raw.Count <= maxPoints)
				return new AggregateResult { Series = this, Points = BuildSinglePointBuckets(raw) };

			return new AggregateResult { Series = this, Points = BuildDownsampledBuckets(raw, start, end, maxPoints) };
		}

		private static AggregatedPoint[] BuildSinglePointBuckets(List<(DateTime timestamp, ushort rtt)> raw)
		{
			var pts = new AggregatedPoint[raw.Count];
			for (int i = 0; i < raw.Count; i++)
			{
				var (ts, rtt) = raw[i];
				pts[i].TimestampUtc = ts;
				pts[i].SampleCount = 1;
				if (rtt == PingRecordStatus.Timeout)
				{
					pts[i].PacketLossPercent = 100.0;
				}
				else if (rtt != PingRecordStatus.Pending)
				{
					pts[i].AvgRtt = rtt;
					pts[i].MinRtt = rtt;
					pts[i].MaxRtt = rtt;
				}
			}
			return pts;
		}

		private static AggregatedPoint[] BuildDownsampledBuckets(
			List<(DateTime timestamp, ushort rtt)> raw,
			DateTime start, DateTime end, int maxPoints)
		{
			double totalMs = (end - start).TotalMilliseconds;
			double bucketMs = totalMs / maxPoints;

			var pts = new AggregatedPoint[maxPoints];
			var sumRtt = new double[maxPoints];
			var successCount = new int[maxPoints];
			var timeoutCount = new int[maxPoints];
			var hasData = new bool[maxPoints];

			for (int i = 0; i < maxPoints; i++)
				pts[i].TimestampUtc = start.AddMilliseconds((i + 0.5) * bucketMs);

			foreach (var (ts, rtt) in raw)
			{
				if (rtt == PingRecordStatus.Pending) continue;

				int bi = Math.Min(maxPoints - 1, (int)((ts - start).TotalMilliseconds / bucketMs));
				pts[bi].SampleCount++;
				hasData[bi] = true;

				if (rtt == PingRecordStatus.Timeout)
				{
					timeoutCount[bi]++;
				}
				else
				{
					double v = rtt;
					sumRtt[bi] += v;
					successCount[bi]++;
					if (successCount[bi] == 1 || v < pts[bi].MinRtt) pts[bi].MinRtt = v;
					if (v > pts[bi].MaxRtt) pts[bi].MaxRtt = v;
				}
			}

			for (int i = 0; i < maxPoints; i++)
			{
				if (!hasData[i]) continue;
				int total = pts[i].SampleCount;
				if (total > 0)
					pts[i].PacketLossPercent = 100.0 * timeoutCount[i] / total;
				if (successCount[i] > 0)
					pts[i].AvgRtt = sumRtt[i] / successCount[i];
			}

			return pts;
		}

		/// <summary>
		/// Walks records starting after the given cursor (exclusive). If <paramref name="cursor"/>
		/// is null, enumerates from the very first record. Materializes a snapshot under the
		/// write lock so iteration is safe against concurrent appends.
		/// </summary>
		internal List<(RecordHandle handle, PingRecord record)> SnapshotRecordsAfter(RecordHandle? cursor)
		{
			var output = new List<(RecordHandle, PingRecord)>();
			lock (_writeLock)
			{
				if (_records.ActiveCount == 0) return output;

				IEnumerable<(RecordHandle, PingRecord)> seq;
				bool skipFirst;
				if (cursor == null)
				{
					seq = _records.EnumerateFrom(new RecordHandle(_records.FirstLogicalChunkIndex, 0));
					skipFirst = false;
				}
				else
				{
					seq = _records.EnumerateFrom(cursor.Value);
					skipFirst = true;
				}

				foreach (var pair in seq)
				{
					if (skipFirst)
					{
						skipFirst = false;
						if (cursor != null && pair.Item1.ChunkIndex == cursor.Value.ChunkIndex && pair.Item1.SlotIndex == cursor.Value.SlotIndex)
							continue;
					}
					output.Add(pair);
				}
			}
			return output;
		}

		/// <summary>
		/// Appends a fully-formed record (with its own RelativeTimeMs and final RoundTripMs)
		/// without going through BeginPendingRecord/CompleteRecord. Used by the disk loader
		/// to reconstruct a series. Updates the monotonic timestamp watermark.
		/// </summary>
		internal RecordHandle AppendRawRecord(PingRecord record)
		{
			lock (_writeLock)
			{
				DateTime ts = StartTimeUtc.AddMilliseconds(record.RelativeTimeMs);
				if (ts > _lastRecordedTimestampUtc) _lastRecordedTimestampUtc = ts;
				return _records.Append(record);
			}
		}

		/// <summary>
		/// Drops whole leading chunks whose records all predate the cutoff.
		/// Keeps at least the last chunk.
		/// </summary>
		public void PruneOlderThan(DateTime cutoff)
		{
			if (cutoff <= StartTimeUtc) return;

			uint cutoffRelativeMs = (uint)(cutoff - StartTimeUtc).TotalMilliseconds;

			RecordHandle? keepFrom = _records.LowerBound(r => r.RelativeTimeMs, cutoffRelativeMs);
			int keepFromChunkIndex;
			if (keepFrom == null)
			{
				// All records predate cutoff; keep the last chunk.
				keepFromChunkIndex = _records.FirstLogicalChunkIndex + _records.ChunkCount - 1;
			}
			else
			{
				keepFromChunkIndex = keepFrom.Value.ChunkIndex;
			}

			_records.PruneChunksOlderThan(keepFromChunkIndex);
		}
	}

	public class AggregateResult
	{
		public HopTimeSeries Series;
		public AggregatedPoint[] Points;
	}

	public struct AggregatedPoint
	{
		public DateTime TimestampUtc;
		public double AvgRtt;
		public double MinRtt;
		public double MaxRtt;
		public double PacketLossPercent;
		public int SampleCount;
	}
}
