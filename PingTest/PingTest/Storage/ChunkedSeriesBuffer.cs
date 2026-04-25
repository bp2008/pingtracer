using System;
using System.Collections.Generic;

namespace PingTracer.Storage
{
	/// <summary>
	/// Append-only buffer backed by fixed-size chunks. Supports O(1) append,
	/// O(1) random access by RecordHandle, O(log n) binary search, and efficient
	/// front-pruning by dropping whole leading chunks.
	///
	/// Not thread-safe; callers are responsible for synchronization.
	/// </summary>
	public class ChunkedSeriesBuffer<T> where T : struct
	{
		private const int DefaultChunkSize = 4096;

		private readonly int _chunkSize;
		private readonly List<T[]> _chunks = new List<T[]>();

		// Logical chunk index of _chunks[0]. Increments each time we drop a leading chunk.
		private int _firstLogicalChunkIndex = 0;

		// Number of items used in _chunks[^1]. Valid only when _chunks.Count > 0.
		private int _lastChunkUsedCount = 0;

		/// <summary>Total records ever appended. Never decrements.</summary>
		public long TotalCount { get; private set; } = 0;

		/// <summary>Records currently stored (decrements on prune).</summary>
		public long ActiveCount { get; private set; } = 0;

		/// <summary>Number of live chunks.</summary>
		public int ChunkCount => _chunks.Count;

		/// <summary>Logical index of the first live chunk.</summary>
		public int FirstLogicalChunkIndex => _firstLogicalChunkIndex;

		public ChunkedSeriesBuffer(int chunkSize = DefaultChunkSize)
		{
			if (chunkSize < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));
			_chunkSize = chunkSize;
		}

		/// <summary>Appends an item and returns its stable handle.</summary>
		public RecordHandle Append(T item)
		{
			if (_chunks.Count == 0 || _lastChunkUsedCount == _chunkSize)
			{
				_chunks.Add(new T[_chunkSize]);
				_lastChunkUsedCount = 0;
			}

			int listIndex = _chunks.Count - 1;
			int slotIndex = _lastChunkUsedCount;
			_chunks[listIndex][slotIndex] = item;
			_lastChunkUsedCount++;
			TotalCount++;
			ActiveCount++;

			return new RecordHandle(_firstLogicalChunkIndex + listIndex, slotIndex);
		}

		/// <summary>Returns a managed ref to the record at h for in-place update.</summary>
		public ref T GetRef(RecordHandle h)
		{
			int listIndex = h.ChunkIndex - _firstLogicalChunkIndex;
			if ((uint)listIndex >= (uint)_chunks.Count)
				throw new InvalidOperationException($"Chunk {h.ChunkIndex} has been pruned or is out of range (first={_firstLogicalChunkIndex}, count={_chunks.Count}).");
			ValidateSlot(listIndex, h.SlotIndex);
			return ref _chunks[listIndex][h.SlotIndex];
		}

		/// <summary>Returns a value copy of the record at h.</summary>
		public T Get(RecordHandle h) => GetRef(h);

		private void ValidateSlot(int listIndex, int slotIndex)
		{
			int maxSlot = (listIndex == _chunks.Count - 1) ? _lastChunkUsedCount - 1 : _chunkSize - 1;
			if ((uint)slotIndex > (uint)maxSlot)
				throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot {slotIndex} is out of range [0,{maxSlot}].");
		}

		private int GetChunkUsedCount(int listIndex)
		{
			return listIndex == _chunks.Count - 1 ? _lastChunkUsedCount : _chunkSize;
		}

		/// <summary>
		/// Drops all chunks with logical index less than keepFromLogicalChunkIndex.
		/// The last chunk is never dropped regardless of the parameter.
		/// </summary>
		public void PruneChunksOlderThan(int keepFromLogicalChunkIndex)
		{
			// Always preserve at least 1 chunk
			int maxCanDrop = _chunks.Count - 1;
			if (maxCanDrop <= 0) return;

			int wantToDrop = keepFromLogicalChunkIndex - _firstLogicalChunkIndex;
			if (wantToDrop <= 0) return;

			int countToDrop = Math.Min(wantToDrop, maxCanDrop);

			// Non-last chunks are always full, so this is exact.
			ActiveCount -= (long)countToDrop * _chunkSize;

			_chunks.RemoveRange(0, countToDrop);
			_firstLogicalChunkIndex += countToDrop;
		}

		/// <summary>
		/// Enumerates records starting at (or after) the given handle.
		/// If the handle's chunk has been pruned, enumeration starts from the first available record.
		/// </summary>
		public IEnumerable<(RecordHandle handle, T record)> EnumerateFrom(RecordHandle start)
		{
			if (_chunks.Count == 0) yield break;

			int startListIndex = start.ChunkIndex - _firstLogicalChunkIndex;
			bool pruned = startListIndex < 0;
			if (pruned) startListIndex = 0;

			for (int ci = startListIndex; ci < _chunks.Count; ci++)
			{
				int logicalIndex = ci + _firstLogicalChunkIndex;
				T[] chunk = _chunks[ci];
				int usedCount = GetChunkUsedCount(ci);

				int startSlot = 0;
				if (ci == startListIndex && !pruned)
					startSlot = Math.Max(0, start.SlotIndex);

				for (int si = startSlot; si < usedCount; si++)
					yield return (new RecordHandle(logicalIndex, si), chunk[si]);
			}
		}

		/// <summary>
		/// Returns the handle of the first record where keySelector(record) >= target,
		/// or null if all records are less than target. Requires records to be in
		/// non-decreasing key order.
		/// </summary>
		public RecordHandle? LowerBound<TKey>(Func<T, TKey> keySelector, TKey target)
			where TKey : IComparable<TKey>
		{
			if (_chunks.Count == 0) return null;

			// Chunk-level search: find the first chunk whose last element >= target.
			int lo = 0, hi = _chunks.Count - 1;

			// Check if target is beyond all data
			{
				int lastListIndex = _chunks.Count - 1;
				int lastSlot = _lastChunkUsedCount - 1;
				if (keySelector(_chunks[lastListIndex][lastSlot]).CompareTo(target) < 0)
					return null;
			}

			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				int midUsed = GetChunkUsedCount(mid);
				int cmp = keySelector(_chunks[mid][midUsed - 1]).CompareTo(target);
				if (cmp < 0)
					lo = mid + 1;
				else
					hi = mid;
			}

			// lo is the first chunk whose last element >= target; binary search within it.
			T[] searchChunk = _chunks[lo];
			int chunkUsed = GetChunkUsedCount(lo);

			int cLo = 0, cHi = chunkUsed - 1;
			while (cLo <= cHi)
			{
				int mid = (cLo + cHi) / 2;
				if (keySelector(searchChunk[mid]).CompareTo(target) < 0)
					cLo = mid + 1;
				else
					cHi = mid - 1;
			}

			if (cLo < chunkUsed)
				return new RecordHandle(lo + _firstLogicalChunkIndex, cLo);

			// All elements in this chunk are < target — try next chunk.
			if (lo + 1 < _chunks.Count)
				return new RecordHandle(lo + 1 + _firstLogicalChunkIndex, 0);

			return null;
		}
	}
}
