using System;
using System.IO;
using System.Net;
using System.Text;

namespace PingTracer.Services
{
	/// <summary>
	/// Builds little-endian binary frames for the WebSocket data-plane protocol.
	/// Frames begin with a 1-byte FrameType and a 1-byte SessionIndex (0xFF = none).
	/// Strings: 2-byte uint16 utf-8 length + bytes.
	/// IPAddress: 1-byte length (0|4|16) + bytes.
	/// Timestamps: 8-byte uint64 Unix epoch milliseconds.
	/// </summary>
	public sealed class BinaryFrameWriter
	{
		private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private readonly MemoryStream _ms = new MemoryStream(64);

		public BinaryFrameWriter(byte frameType, byte sessionIndex)
		{
			_ms.WriteByte(frameType);
			_ms.WriteByte(sessionIndex);
		}

		public void WriteByte(byte v) => _ms.WriteByte(v);

		public void WriteUInt16(ushort v)
		{
			_ms.WriteByte((byte)(v & 0xFF));
			_ms.WriteByte((byte)((v >> 8) & 0xFF));
		}

		public void WriteUInt32(uint v)
		{
			_ms.WriteByte((byte)(v & 0xFF));
			_ms.WriteByte((byte)((v >> 8) & 0xFF));
			_ms.WriteByte((byte)((v >> 16) & 0xFF));
			_ms.WriteByte((byte)((v >> 24) & 0xFF));
		}

		public void WriteUInt64(ulong v)
		{
			for (int i = 0; i < 8; i++)
				_ms.WriteByte((byte)((v >> (i * 8)) & 0xFF));
		}

		public void WriteUnixMs(DateTime utc)
		{
			if (utc == default || utc < UnixEpoch)
			{
				WriteUInt64(0);
				return;
			}
			DateTime u = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
			ulong ms = (ulong)(u - UnixEpoch).TotalMilliseconds;
			WriteUInt64(ms);
		}

		public void WriteIp(IPAddress addr)
		{
			if (addr == null || addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any))
			{
				_ms.WriteByte(0);
				return;
			}
			byte[] bytes = addr.GetAddressBytes();
			if (bytes.Length != 4 && bytes.Length != 16)
			{
				_ms.WriteByte(0);
				return;
			}
			_ms.WriteByte((byte)bytes.Length);
			_ms.Write(bytes, 0, bytes.Length);
		}

		public void WriteUtf8String(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				WriteUInt16(0);
				return;
			}
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			if (bytes.Length > ushort.MaxValue)
				bytes = Encoding.UTF8.GetBytes(s.Substring(0, Math.Min(s.Length, ushort.MaxValue / 4)));
			WriteUInt16((ushort)bytes.Length);
			_ms.Write(bytes, 0, bytes.Length);
		}

		public byte[] ToArray() => _ms.ToArray();
	}

	/// <summary>Frame type constants for the binary WebSocket protocol.</summary>
	public static class BinaryFrameType
	{
		public const byte RouteUpdate = 0x01;
		public const byte HostnameUpdated = 0x02;
		public const byte PingUpdate = 0x03;
		public const byte AggregatedData = 0x04;
		public const byte HopDeactivated = 0x05;
		public const byte SessionTopology = 0x06;
		public const byte NoSession = 0xFF;
	}
}
