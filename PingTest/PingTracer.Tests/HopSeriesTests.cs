using PingTracer.Storage;
using System.Collections.Generic;
using Xunit;

namespace PingTracer.Tests
{
	public class HopSeriesTests
	{
		private static HopSeries Build(params (uint t, ushort rtt, ushort addr)[] records)
		{
			HopSeries h = new HopSeries(3);
			foreach (var r in records)
			{
				RecordHandle handle = h.Begin(r.t);
				if (r.rtt != PingRecordStatus.Pending)
					Assert.True(h.Complete(handle, r.rtt, r.addr, out _));
			}
			return h;
		}

		[Fact]
		public void BeginClampsBackwardsTime()
		{
			HopSeries h = new HopSeries(1);
			h.Begin(100);
			RecordHandle r = h.Begin(50);
			Assert.True(h.TryGet(r, out PingSample rec));
			Assert.Equal(100u, rec.TimeMs);
		}

		[Fact]
		public void SupportsSessionsLongerThan32BitMilliseconds()
		{
			HopSeries h = new HopSeries(1);
			long sixtyDays = 60L * 86400 * 1000;
			long[] times = { 0, 1000, uint.MaxValue - 5L, uint.MaxValue + 10L, sixtyDays, sixtyDays + 1000 };
			foreach (long t in times)
				h.Complete(h.Begin(t), 7, 0, out _);
			// Logical indices may skip when a chunk is started early to keep offsets in 32 bits, but must stay ordered.
			for (int i = 1; i < times.Length; i++)
				Assert.True(h.LowerBound(times[i]) > h.LowerBound(times[i - 1]));
			Assert.Equal(h.EndIndex, h.LowerBound(sixtyDays + 1001));
			// One column per day: the last two records fall on day 60.
			ColumnStats st = new ColumnStats(61);
			h.Aggregate(0, 0, 86400 * 1000, st);
			Assert.Equal(2, st.N[0]);
			Assert.Equal(2, st.N[49]);
			Assert.Equal(2, st.N[60]);
			h.PruneOlderThan(sixtyDays);
			ColumnStats after = new ColumnStats(61);
			h.Aggregate(0, 0, 86400 * 1000, after);
			Assert.Equal(0, after.N[0]);
			Assert.Equal(2, after.N[60]);
		}

		[Fact]
		public void CompleteOnlyOnce()
		{
			HopSeries h = new HopSeries(1);
			RecordHandle r = h.Begin(10);
			Assert.True(h.Complete(r, 5, 0, out _));
			Assert.False(h.Complete(r, 6, 0, out _));
			Assert.Equal(1, h.ReplyCount);
		}

		[Fact]
		public void AggregatesIntoAlignedColumns()
		{
			// Session starts at Unix ms 10_000; columns of 1000 ms starting at column 10 (= session start).
			HopSeries h = Build(
				(0, 10, 0), (500, 20, 0),          // column 0: two successes
				(1000, PingRecordStatus.Lost, AddressTable.None), (1500, 30, 1), // column 1: one loss, one success
				(2100, PingRecordStatus.Pending, 0), // pending: ignored
				(3000, 40, 0));                    // column 3
			ColumnStats st = new ColumnStats(5);
			int nonEmpty = h.Aggregate(10_000, 10, 1000, st);
			Assert.Equal(3, nonEmpty);
			Assert.Equal(2, st.N[0]); Assert.Equal(0, st.Lost[0]); Assert.Equal(10, st.Min[0]); Assert.Equal(20, st.Max[0]); Assert.Equal(30f, st.Sum[0]);
			Assert.Equal(2, st.N[1]); Assert.Equal(1, st.Lost[1]); Assert.Equal(30f, st.Sum[1]); Assert.Equal(1, st.Addr[1]);
			Assert.Equal(0, st.N[2]);
			Assert.Equal(1, st.N[3]); Assert.Equal(40, st.Max[3]);
		}

		[Fact]
		public void AggregationFlagsMultipleAddressesAndPicksMajority()
		{
			HopSeries h = Build((0, 10, 5), (100, 10, 6), (200, 10, 6), (300, 10, 6));
			ColumnStats st = new ColumnStats(1);
			h.Aggregate(0, 0, 1000, st);
			Assert.Equal(6, st.Addr[0]);
			Assert.Equal(ColumnStats.FlagMultipleAddresses, st.Flags[0]);

			HopSeries same = Build((0, 10, 2), (100, 12, 2));
			ColumnStats st2 = new ColumnStats(1);
			same.Aggregate(0, 0, 1000, st2);
			Assert.Equal(0, st2.Flags[0]);
		}

		[Fact]
		public void VoidRecordsAreExcluded()
		{
			HopSeries h = new HopSeries(1);
			RecordHandle a = h.Begin(0);
			h.Complete(a, 10, 0, out _);
			RecordHandle b = h.Begin(10);
			h.Complete(b, 20, 0, out _);
			Assert.True(h.MakeVoid(b, out PingSample prev));
			Assert.Equal(20, prev.Rtt);
			Assert.False(h.MakeVoid(b, out _));
			ColumnStats st = new ColumnStats(1);
			h.Aggregate(0, 0, 1000, st);
			Assert.Equal(1, st.N[0]);
			Assert.Equal(10f, st.Sum[0]);
		}

		[Fact]
		public void AggregationSpansChunksAndUsesBinarySearch()
		{
			HopSeries h = new HopSeries(1);
			int count = HopSeries.ChunkSize * 3 + 17;
			for (int i = 0; i < count; i++)
			{
				RecordHandle r = h.Begin((uint)(i * 10));
				h.Complete(r, (ushort)(i % 100), 0, out _);
			}
			// One column per 10 ms; ask for a window in the middle of chunk 2.
			long startCol = HopSeries.ChunkSize * 2 + 100;
			ColumnStats st = new ColumnStats(50);
			int nonEmpty = h.Aggregate(0, startCol, 10, st);
			Assert.Equal(50, nonEmpty);
			for (int c = 0; c < 50; c++)
				Assert.Equal((float)((startCol + c) % 100), st.Sum[c]);
			Assert.Equal(HopSeries.ChunkSize * 2 + 100, h.LowerBound((long)((HopSeries.ChunkSize * 2 + 100) * 10)));
		}

		[Fact]
		public void PruneDropsWholeChunksButKeepsLast()
		{
			HopSeries h = new HopSeries(1);
			for (int i = 0; i < HopSeries.ChunkSize * 3; i++)
				h.Complete(h.Begin((uint)i), 1, 0, out _);
			h.PruneOlderThan((uint)(HopSeries.ChunkSize * 2 + 5));
			Assert.Equal(HopSeries.ChunkSize * 2, h.FirstIndex);
			h.PruneOlderThan(uint.MaxValue);
			Assert.Equal(HopSeries.ChunkSize, h.Count);
		}

		[Fact]
		public void CopyStableRecordsStopsAtPendingAndSkipsVoid()
		{
			HopSeries h = new HopSeries(1);
			h.Complete(h.Begin(0), 1, 0, out _);
			RecordHandle v = h.Begin(10);
			h.Complete(v, 2, 0, out _);
			h.MakeVoid(v, out _);
			h.Begin(20); // pending
			h.Complete(h.Begin(30), 4, 0, out _);
			long cursor = 0;
			List<PingSample> output = new List<PingSample>();
			h.CopyStableRecords(ref cursor, 1000, output);
			Assert.Single(output);
			Assert.Equal(2, cursor);

			// Records newer than the stability cutoff are not copied either.
			HopSeries h2 = new HopSeries(1);
			h2.Complete(h2.Begin(0), 1, 0, out _);
			h2.Complete(h2.Begin(500), 1, 0, out _);
			long c2 = 0;
			List<PingSample> out2 = new List<PingSample>();
			h2.CopyStableRecords(ref c2, 400, out2);
			Assert.Single(out2);
			Assert.Equal(1, c2);
		}
	}
}
