using System;
using System.Buffers.Binary;

namespace PingTracer.Services
{
	/// <summary>
	/// Minimal little-endian binary writer for WebSocket data-plane frames.
	/// </summary>
	public sealed class BinaryFrameWriter
	{
		private byte[] buf;
		private int pos;

		public BinaryFrameWriter(int initialCapacity = 256)
		{
			buf = new byte[Math.Max(16, initialCapacity)];
		}

		public int Length => pos;

		private void Ensure(int n)
		{
			if (pos + n <= buf.Length) return;
			int size = buf.Length * 2;
			while (size < pos + n) size *= 2;
			Array.Resize(ref buf, size);
		}

		public void U8(byte v) { Ensure(1); buf[pos++] = v; }
		public void U16(ushort v) { Ensure(2); BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos), v); pos += 2; }
		public void U32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), v); pos += 4; }
		public void F32(float v) { Ensure(4); BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos), v); pos += 4; }
		public void F64(double v) { Ensure(8); BinaryPrimitives.WriteDoubleLittleEndian(buf.AsSpan(pos), v); pos += 8; }

		/// <summary>Overwrites a previously written u16 at <paramref name="offset"/>.</summary>
		public void PatchU16(int offset, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), v);

		public byte[] ToArray()
		{
			byte[] result = new byte[pos];
			Buffer.BlockCopy(buf, 0, result, 0, pos);
			return result;
		}
	}

	/// <summary>Binary frame type codes (first byte of every binary WebSocket message).</summary>
	public static class BinaryFrameType
	{
		/// <summary>
		/// Batch of live ping results.
		/// <code>[0x11][count u16] then count x { sessionId u16, ttl u8, kind u8 (0 = sample, 1 = void), timeUnixMs f64, rtt u16, addr u16 }</code>
		/// </summary>
		public const byte LiveBatch = 0x11;

		/// <summary>
		/// Response to a column query.
		/// <code>[0x12][queryId u32][sessionId u16][msPerCol u32][startCol f64][colCount u16][hopCount u16]
		/// then hopCount x { ttl u8, nonEmpty u16, nonEmpty x { col u16, n u16, lost u16, min u16, max u16, sum f32, addr u16, flags u8 } }</code>
		/// </summary>
		public const byte QueryResult = 0x12;
	}
}
