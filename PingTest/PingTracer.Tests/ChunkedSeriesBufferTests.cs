using System;
using PingTracer.Storage;
using Xunit;

namespace PingTracer.Tests
{
	public class ChunkedSeriesBufferTests
	{
		[Fact]
		public void Append_And_Get_Roundtrip()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 4);
			var r = new PingRecord { RelativeTimeMs = 100, RoundTripMs = 42 };

			var handle = buf.Append(r);
			var got = buf.Get(handle);

			Assert.Equal(100u, got.RelativeTimeMs);
			Assert.Equal((ushort)42, got.RoundTripMs);
			Assert.Equal(1, buf.ActiveCount);
			Assert.Equal(1, buf.TotalCount);
		}

		[Fact]
		public void GetRef_InPlaceUpdate_Works()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 4);
			var handle = buf.Append(new PingRecord { RelativeTimeMs = 50, RoundTripMs = PingRecordStatus.Pending });

			ref PingRecord rec = ref buf.GetRef(handle);
			rec.RoundTripMs = 77;

			Assert.Equal((ushort)77, buf.Get(handle).RoundTripMs);
		}

		[Fact]
		public void Append_AcrossMultipleChunks()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);

			for (int i = 0; i < 7; i++)
				buf.Append(new PingRecord { RelativeTimeMs = (uint)i * 10, RoundTripMs = (ushort)i });

			Assert.Equal(7, buf.ActiveCount);
			Assert.Equal(3, buf.ChunkCount); // chunks: [0,1,2] [3,4,5] [6]

			// Spot-check last item
			var last = buf.Append(new PingRecord { RelativeTimeMs = 70, RoundTripMs = 99 });
			Assert.Equal((ushort)99, buf.Get(last).RoundTripMs);
		}

		[Fact]
		public void PruneChunksOlderThan_DropsLeadingChunks()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);
			// Fill 2 full chunks + 1 partial: logical indices 0, 1, 2
			for (int i = 0; i < 7; i++)
				buf.Append(new PingRecord { RelativeTimeMs = (uint)i });

			// Keep chunks with logical index >= 1 (drop chunk 0)
			buf.PruneChunksOlderThan(1);

			Assert.Equal(2, buf.ChunkCount);
			Assert.Equal(1, buf.FirstLogicalChunkIndex);
			Assert.Equal(4, buf.ActiveCount); // 3 from chunk1 + 1 from chunk2
		}

		[Fact]
		public void PruneChunksOlderThan_NeverDropsLastChunk()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);
			for (int i = 0; i < 3; i++)
				buf.Append(new PingRecord { RelativeTimeMs = (uint)i });

			// Request dropping all chunks
			buf.PruneChunksOlderThan(999);

			Assert.Equal(1, buf.ChunkCount); // last chunk always preserved
			Assert.Equal(3, buf.ActiveCount);
		}

		[Fact]
		public void BinarySearch_LowerBound_FindsCorrectHandle()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 4);
			uint[] times = { 10, 20, 30, 40, 50, 60 };
			foreach (uint t in times)
				buf.Append(new PingRecord { RelativeTimeMs = t });

			var h = buf.LowerBound(r => r.RelativeTimeMs, 30u);
			Assert.NotNull(h);
			Assert.Equal(30u, buf.Get(h.Value).RelativeTimeMs);
		}

		[Fact]
		public void BinarySearch_LowerBound_FindsFirstGreaterThan()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 4);
			uint[] times = { 10, 20, 40, 50 };
			foreach (uint t in times)
				buf.Append(new PingRecord { RelativeTimeMs = t });

			// Target 30 doesn't exist; should land on 40
			var h = buf.LowerBound(r => r.RelativeTimeMs, 30u);
			Assert.NotNull(h);
			Assert.Equal(40u, buf.Get(h.Value).RelativeTimeMs);
		}

		[Fact]
		public void BinarySearch_LowerBound_ReturnsNull_WhenAllLess()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 4);
			buf.Append(new PingRecord { RelativeTimeMs = 5 });
			buf.Append(new PingRecord { RelativeTimeMs = 10 });

			var h = buf.LowerBound(r => r.RelativeTimeMs, 100u);
			Assert.Null(h);
		}

		[Fact]
		public void BinarySearch_LowerBound_ReturnsNull_WhenEmpty()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 4);
			Assert.Null(buf.LowerBound(r => r.RelativeTimeMs, 0u));
		}

		[Fact]
		public void BinarySearch_LowerBound_AcrossChunkBoundary()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);
			// chunk 0: 10, 20, 30 | chunk 1: 40, 50, 60 | chunk 2: 70
			for (uint t = 10; t <= 70; t += 10)
				buf.Append(new PingRecord { RelativeTimeMs = t });

			var h = buf.LowerBound(r => r.RelativeTimeMs, 45u);
			Assert.NotNull(h);
			Assert.Equal(50u, buf.Get(h.Value).RelativeTimeMs);
		}

		[Fact]
		public void EnumerateFrom_PrunedHandle_StartFromBeginning()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);
			for (uint i = 0; i < 6; i++)
				buf.Append(new PingRecord { RelativeTimeMs = i * 10 });

			var staleHandle = new RecordHandle(0, 0); // logical chunk 0

			// Prune chunk 0
			buf.PruneChunksOlderThan(1);

			// Enumerate from stale handle — should start from first available
			var list = new System.Collections.Generic.List<uint>();
			foreach (var (_, r) in buf.EnumerateFrom(staleHandle))
				list.Add(r.RelativeTimeMs);

			Assert.Equal(new uint[] { 30, 40, 50 }, list);
		}

		[Fact]
		public void HandleLogicalIndexStable_AfterPrune()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);
			// Fill chunk 0
			for (uint i = 0; i < 3; i++)
				buf.Append(new PingRecord { RelativeTimeMs = i });

			// Append to chunk 1, keep the handle
			var handle = buf.Append(new PingRecord { RelativeTimeMs = 100, RoundTripMs = 55 });

			// Drop chunk 0
			buf.PruneChunksOlderThan(1);

			// Handle should still work
			Assert.Equal((ushort)55, buf.Get(handle).RoundTripMs);
		}

		[Fact]
		public void GetRef_PrunedChunk_Throws()
		{
			var buf = new ChunkedSeriesBuffer<PingRecord>(chunkSize: 3);
			for (int i = 0; i < 4; i++)
				buf.Append(new PingRecord { RelativeTimeMs = (uint)i });

			var stale = new RecordHandle(0, 0);
			buf.PruneChunksOlderThan(1);

			Assert.Throws<InvalidOperationException>(() => buf.GetRef(stale));
		}
	}
}
