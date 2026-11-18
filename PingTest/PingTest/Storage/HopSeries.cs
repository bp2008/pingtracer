using System;
using System.Collections.Generic;

namespace PingTracer.Storage
{
	/// <summary>
	/// All pings sent to one TTL position of a <see cref="MonitoringSession"/>, in send order.
	/// <para>
	/// Every record carries the id of the address that replied, so a hop whose responding router changes
	/// (route change) or alternates (ECMP load balancing) remains a single, gap-free series.  This is the
	/// central design decision of the storage layer: series are keyed by TTL, never by (TTL, address).
	/// </para>
	/// <para>
	/// Records live in fixed-size chunks so that appends are O(1), lookups by <see cref="RecordHandle"/> are
	/// O(1), time-range lookups are O(log n), and old data can be pruned by dropping whole chunks.  Each chunk has
	/// a 64-bit base time and its records store 32-bit offsets from it, so records stay 8 bytes while sessions can
	/// run indefinitely.
	/// </para>
	/// <para>Not thread safe. All access is synchronized by <see cref="MonitoringSession.DataLock"/>.</para>
	/// </summary>
	public sealed class HopSeries
	{
		public const int ChunkSize = 4096;

		/// <summary>TTL of this hop (1-255), or 0 for a direct (non-traceroute) ping series.</summary>
		public readonly int Ttl;

		private readonly List<PingRecord[]> chunks = new List<PingRecord[]>();
		private readonly List<long> chunkBase = new List<long>();
		/// <summary>Used slots per chunk. Chunks other than the last are normally full, but a chunk is started early if its 32-bit offsets would overflow.</summary>
		private readonly List<int> chunkUsed = new List<int>();
		private int firstLogicalChunk = 0;
		private long lastTimeMs = 0;

		/// <summary>Number of record slots currently held (including pending and void records).</summary>
		public long Count { get; private set; }
		/// <summary>Number of successful replies currently recorded (excluding voided ones).</summary>
		public long ReplyCount { get; private set; }
		/// <summary>Session-relative time of the most recent successful reply, or -1 if none.</summary>
		public long LastReplyTimeMs { get; private set; } = -1;
		/// <summary>Number of pings sent since the most recent successful reply.</summary>
		public int SentSinceLastReply { get; private set; }

		public HopSeries(int ttl)
		{
			Ttl = ttl;
		}

		/// <summary>Logical index of the first record still held. Increases as chunks are pruned.</summary>
		public long FirstIndex => (long)firstLogicalChunk * ChunkSize;
		/// <summary>Logical index one past the last record.</summary>
		public long EndIndex => chunks.Count == 0 ? FirstIndex : FirstIndex + (long)(chunks.Count - 1) * ChunkSize + chunkUsed[chunks.Count - 1];

		/// <summary>
		/// Appends a pending record.  Timestamps are clamped to be non-decreasing so that binary search remains valid.
		/// </summary>
		public RecordHandle Begin(long timeMs)
		{
			SentSinceLastReply++;
			return Append(timeMs, PingRecordStatus.Pending, AddressTable.None);
		}

		/// <summary>Appends an already-completed sample (used when loading from disk).</summary>
		public void AppendCompleted(PingSample s)
		{
			Append(s.TimeMs, s.Rtt, s.Addr);
			NoteCompletion(new PingSample(Math.Max(s.TimeMs, lastTimeMs), s.Rtt, s.Addr));
		}

		private RecordHandle Append(long timeMs, ushort rtt, ushort addr)
		{
			if (timeMs < lastTimeMs)
				timeMs = lastTimeMs;
			lastTimeMs = timeMs;
			if (chunks.Count == 0 || chunkUsed[chunks.Count - 1] == ChunkSize || timeMs - chunkBase[chunkBase.Count - 1] > uint.MaxValue)
			{
				chunks.Add(new PingRecord[ChunkSize]);
				chunkBase.Add(timeMs);
				chunkUsed.Add(0);
			}
			int listIndex = chunks.Count - 1;
			int slot = chunkUsed[listIndex]++;
			chunks[listIndex][slot] = new PingRecord((uint)(timeMs - chunkBase[listIndex]), rtt, addr);
			Count++;
			return new RecordHandle(firstLogicalChunk + listIndex, slot);
		}

		/// <summary>
		/// Sets the result of a pending record.  Returns false if the record no longer exists (pruned) or was already completed.
		/// </summary>
		public bool Complete(RecordHandle h, ushort rtt, ushort addr, out PingSample completed)
		{
			completed = default;
			if (!TryGetSlot(h, out int listIndex, out int slot))
				return false;
			ref PingRecord r = ref chunks[listIndex][slot];
			if (r.Rtt != PingRecordStatus.Pending)
				return false;
			r.Rtt = rtt;
			r.Addr = addr;
			completed = new PingSample(chunkBase[listIndex] + r.OffsetMs, rtt, addr);
			NoteCompletion(completed);
			return true;
		}

		private void NoteCompletion(PingSample s)
		{
			if (s.IsSuccess)
			{
				ReplyCount++;
				if (s.TimeMs > LastReplyTimeMs)
					LastReplyTimeMs = s.TimeMs;
				SentSinceLastReply = 0;
			}
		}

		/// <summary>
		/// Marks a record as void (excluded from all statistics).  Returns the sample as it was before being voided,
		/// or false if the record does not exist or is already void.
		/// </summary>
		public bool MakeVoid(RecordHandle h, out PingSample previous)
		{
			previous = default;
			if (!TryGetSlot(h, out int listIndex, out int slot))
				return false;
			ref PingRecord r = ref chunks[listIndex][slot];
			if (r.Rtt == PingRecordStatus.Void)
				return false;
			previous = new PingSample(chunkBase[listIndex] + r.OffsetMs, r.Rtt, r.Addr);
			if (previous.IsSuccess)
				ReplyCount--;
			r.Rtt = PingRecordStatus.Void;
			return true;
		}

		public bool TryGet(RecordHandle h, out PingSample sample)
		{
			if (TryGetSlot(h, out int listIndex, out int slot))
			{
				PingRecord r = chunks[listIndex][slot];
				sample = new PingSample(chunkBase[listIndex] + r.OffsetMs, r.Rtt, r.Addr);
				return true;
			}
			sample = default;
			return false;
		}

		private bool TryGetSlot(RecordHandle h, out int listIndex, out int slot)
		{
			listIndex = h.ChunkIndex - firstLogicalChunk;
			slot = h.SlotIndex;
			if ((uint)listIndex >= (uint)chunks.Count)
				return false;
			return (uint)slot < (uint)chunkUsed[listIndex];
		}

		private int UsedIn(int listIndex) => chunkUsed[listIndex];

		/// <summary>Converts a logical index to (chunk, slot), skipping the unused tail of a chunk that was started early.</summary>
		private bool Locate(ref long index, out int listIndex, out int slot)
		{
			while (true)
			{
				listIndex = (int)((index - FirstIndex) / ChunkSize);
				slot = (int)((index - FirstIndex) % ChunkSize);
				if (listIndex >= chunks.Count)
					return false;
				if (slot < chunkUsed[listIndex])
					return true;
				index = FirstIndex + (long)(listIndex + 1) * ChunkSize;
			}
		}

		private long LastTimeOf(int listIndex) => chunkBase[listIndex] + chunks[listIndex][UsedIn(listIndex) - 1].OffsetMs;

		/// <summary>
		/// Returns the logical index of the first record with time &gt;= <paramref name="timeMs"/>, or <see cref="EndIndex"/> if none.
		/// </summary>
		public long LowerBound(long timeMs)
		{
			if (chunks.Count == 0)
				return FirstIndex;
			int lo = 0, hi = chunks.Count - 1;
			if (LastTimeOf(hi) < timeMs)
				return EndIndex;
			while (lo < hi)
			{
				int mid = (lo + hi) >> 1;
				if (LastTimeOf(mid) < timeMs)
					lo = mid + 1;
				else
					hi = mid;
			}
			PingRecord[] c = chunks[lo];
			long baseMs = chunkBase[lo];
			int a = 0, b = UsedIn(lo) - 1;
			while (a <= b)
			{
				int mid = (a + b) >> 1;
				if (baseMs + c[mid].OffsetMs < timeMs)
					a = mid + 1;
				else
					b = mid - 1;
			}
			return FirstIndex + (long)lo * ChunkSize + a;
		}

		/// <summary>
		/// Aggregates completed records into time-aligned columns.  Column <c>k</c> covers absolute Unix time
		/// [(startCol + k) * msPerCol, (startCol + k + 1) * msPerCol).
		/// </summary>
		/// <returns>The number of non-empty columns written into <paramref name="target"/>.</returns>
		public int Aggregate(long sessionStartUnixMs, long startCol, int msPerCol, ColumnStats target)
		{
			int colCount = target.Length;
			long rangeStartUnix = startCol * msPerCol;
			long rangeEndUnix = (startCol + colCount) * msPerCol;
			long relStart = rangeStartUnix - sessionStartUnixMs;
			long relEnd = rangeEndUnix - sessionStartUnixMs;
			if (relEnd <= 0 || chunks.Count == 0)
				return 0;

			long index = LowerBound(relStart);
			long end = EndIndex;
			int nonEmpty = 0;
			long offsetToCol = sessionStartUnixMs - rangeStartUnix;

			ushort[] n = target.N, lost = target.Lost, min = target.Min, max = target.Max, addr = target.Addr;
			float[] sum = target.Sum;
			byte[] flags = target.Flags;
			int[] votes = target.Votes;

			while (index < end && Locate(ref index, out int listIndex, out int slot))
			{
				PingRecord[] c = chunks[listIndex];
				long baseMs = chunkBase[listIndex];
				int used = UsedIn(listIndex);
				for (; slot < used; slot++, index++)
				{
					PingRecord r = c[slot];
					long t = baseMs + r.OffsetMs;
					if (t >= relEnd)
						return nonEmpty;
					ushort rtt = r.Rtt;
					if (rtt == PingRecordStatus.Pending || rtt == PingRecordStatus.Void)
						continue;
					long colL = (t + offsetToCol) / msPerCol;
					if ((ulong)colL >= (ulong)colCount)
						continue;
					int col = (int)colL;
					if (n[col] == 0)
					{
						nonEmpty++;
						min[col] = ushort.MaxValue;
						max[col] = 0;
						addr[col] = AddressTable.None;
					}
					if (n[col] < ushort.MaxValue)
						n[col]++;
					if (rtt == PingRecordStatus.Lost)
					{
						if (lost[col] < ushort.MaxValue)
							lost[col]++;
						continue;
					}
					sum[col] += rtt;
					if (rtt < min[col]) min[col] = rtt;
					if (rtt > max[col]) max[col] = rtt;
					// Boyer-Moore majority vote for the dominant responding address.
					ushort a = r.Addr;
					if (votes[col] == 0)
					{
						if (addr[col] != AddressTable.None && addr[col] != a)
							flags[col] |= ColumnStats.FlagMultipleAddresses;
						addr[col] = a;
						votes[col] = 1;
					}
					else if (addr[col] == a)
						votes[col]++;
					else
					{
						flags[col] |= ColumnStats.FlagMultipleAddresses;
						votes[col]--;
					}
				}
			}
			return nonEmpty;
		}

		/// <summary>
		/// Drops leading chunks whose records are all older than <paramref name="cutoffMs"/>.  The final chunk is never dropped.
		/// </summary>
		public void PruneOlderThan(long cutoffMs)
		{
			int drop = 0;
			while (drop < chunks.Count - 1 && LastTimeOf(drop) < cutoffMs)
				drop++;
			if (drop == 0)
				return;
			for (int i = 0; i < drop; i++)
				Count -= chunkUsed[i];
			chunks.RemoveRange(0, drop);
			chunkBase.RemoveRange(0, drop);
			chunkUsed.RemoveRange(0, drop);
			firstLogicalChunk += drop;
		}

		/// <summary>
		/// Copies completed records starting at logical index <paramref name="cursor"/> into <paramref name="output"/>, stopping at the
		/// first record that is still pending or that is not older than <paramref name="stableBeforeMs"/>.  Void records are skipped.
		/// Advances <paramref name="cursor"/> past everything consumed.
		/// </summary>
		public void CopyStableRecords(ref long cursor, long stableBeforeMs, List<PingSample> output)
		{
			if (cursor < FirstIndex)
				cursor = FirstIndex;
			long end = EndIndex;
			while (cursor < end && Locate(ref cursor, out int listIndex, out int slot))
			{
				PingRecord r = chunks[listIndex][slot];
				long t = chunkBase[listIndex] + r.OffsetMs;
				if (t >= stableBeforeMs || r.Rtt == PingRecordStatus.Pending)
					return;
				if (r.Rtt != PingRecordStatus.Void)
					output.Add(new PingSample(t, r.Rtt, r.Addr));
				cursor++;
			}
		}
	}

	/// <summary>
	/// Per-column aggregation results for one hop.  Arrays are indexed by column offset.
	/// </summary>
	public sealed class ColumnStats
	{
		public const byte FlagMultipleAddresses = 1;

		public readonly int Length;
		/// <summary>Completed pings (successes + losses).</summary>
		public readonly ushort[] N;
		public readonly ushort[] Lost;
		public readonly ushort[] Min;
		public readonly ushort[] Max;
		/// <summary>Sum of successful RTTs.</summary>
		public readonly float[] Sum;
		/// <summary>Dominant responding address id.</summary>
		public readonly ushort[] Addr;
		public readonly byte[] Flags;
		internal readonly int[] Votes;

		public ColumnStats(int length)
		{
			Length = length;
			N = new ushort[length];
			Lost = new ushort[length];
			Min = new ushort[length];
			Max = new ushort[length];
			Sum = new float[length];
			Addr = new ushort[length];
			Flags = new byte[length];
			Votes = new int[length];
			Array.Fill(Addr, AddressTable.None);
		}
	}
}
