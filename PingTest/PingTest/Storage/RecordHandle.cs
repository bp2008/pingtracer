namespace PingTracer.Storage
{
	/// <summary>
	/// Identifies the exact position of a PingRecord in a ChunkedSeriesBuffer.
	/// Logical chunk indices are stable across front-pruning operations.
	/// </summary>
	public readonly struct RecordHandle
	{
		public readonly int ChunkIndex;
		public readonly int SlotIndex;

		public RecordHandle(int chunkIndex, int slotIndex)
		{
			ChunkIndex = chunkIndex;
			SlotIndex = slotIndex;
		}

		public override string ToString() => $"[chunk={ChunkIndex}, slot={SlotIndex}]";
	}
}
