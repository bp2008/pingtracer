using System;
using System.Collections.Generic;
using System.Net;
using PingTracer.Services;
using PingTracer.Storage;
using Xunit;

namespace PingTracer.Tests
{
	/// <summary>
	/// Decodes the aggregatedData frame produced by PingWebSocketHandler and asserts
	/// the wire layout matches the protocol spec. The decode mirrors what the JS
	/// client does in WebSocketService.js — keep the two in lock-step.
	/// </summary>
	public class AggregatedDataFrameTests
	{
		[Fact]
		public void EmptyResults_ProducesValidFrame()
		{
			byte[] frame = PingWebSocketHandler.BuildAggregatedDataFrame(0, 7, Array.Empty<AggregateResult>());
			var r = new FrameReader(frame);
			Assert.Equal(BinaryFrameType.AggregatedData, r.ReadByte());
			Assert.Equal(0, r.ReadByte());
			Assert.Equal(7u, r.ReadUInt32());
			Assert.Equal(0, r.ReadByte()); // SeriesCount
			Assert.True(r.AtEnd);
		}

		[Fact]
		public void SingleSeries_RoundTripsSeriesAndPoints()
		{
			DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var series = new HopTimeSeries(2, IPAddress.Parse("10.0.0.1"), start, startDnsLookup: false);
			series.SetHostname("router.lan");

			AggregatedPoint[] pts = new AggregatedPoint[2];
			pts[0] = new AggregatedPoint
			{
				TimestampUtc = start.AddMilliseconds(500),
				MinRtt = 10, MaxRtt = 20, AvgRtt = 15, PacketLossPercent = 0,
				SampleCount = 4
			};
			pts[1] = new AggregatedPoint
			{
				TimestampUtc = start.AddMilliseconds(1500),
				MinRtt = double.NaN, MaxRtt = double.NaN, AvgRtt = double.NaN,
				PacketLossPercent = 50,
				SampleCount = 2
			};
			var result = new AggregateResult { Series = series, Points = pts };

			byte[] frame = PingWebSocketHandler.BuildAggregatedDataFrame(3, 42, new List<AggregateResult> { result });
			var r = new FrameReader(frame);

			Assert.Equal(BinaryFrameType.AggregatedData, r.ReadByte());
			Assert.Equal(3, r.ReadByte());
			Assert.Equal(42u, r.ReadUInt32());
			Assert.Equal(1, r.ReadByte());

			// Series header
			Assert.Equal(2, r.ReadByte()); // hop number
			Assert.Equal(IPAddress.Parse("10.0.0.1"), r.ReadIp());
			Assert.Equal("router.lan", r.ReadUtf8String());
			Assert.Equal(ToUnixMs(start), r.ReadUInt64());
			Assert.Equal(0UL, r.ReadUInt64()); // EndTimeUtcOrZero
			Assert.Equal(2u, r.ReadUInt32());

			// First point — has successful samples
			Assert.Equal(ToUnixMs(start.AddMilliseconds(500)), r.ReadUInt64());
			Assert.Equal((ushort)10, r.ReadUInt16()); // min
			Assert.Equal((ushort)20, r.ReadUInt16()); // max
			Assert.Equal((ushort)15, r.ReadUInt16()); // avg
			Assert.Equal((ushort)0, r.ReadUInt16());  // loss * 100
			Assert.Equal(4u, r.ReadUInt32());          // sampleCount

			// Second point — only timeouts; min/max/avg encode as 0xFFFF
			Assert.Equal(ToUnixMs(start.AddMilliseconds(1500)), r.ReadUInt64());
			Assert.Equal((ushort)0xFFFF, r.ReadUInt16());
			Assert.Equal((ushort)0xFFFF, r.ReadUInt16());
			Assert.Equal((ushort)0xFFFF, r.ReadUInt16());
			Assert.Equal((ushort)5000, r.ReadUInt16()); // 50% × 100
			Assert.Equal(2u, r.ReadUInt32());

			Assert.True(r.AtEnd);
		}

		private static ulong ToUnixMs(DateTime utc)
		{
			DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			return (ulong)(utc - epoch).TotalMilliseconds;
		}

		private sealed class FrameReader
		{
			private readonly byte[] _b;
			private int _i;
			public FrameReader(byte[] b) { _b = b; _i = 0; }
			public bool AtEnd => _i == _b.Length;

			public byte ReadByte() => _b[_i++];

			public ushort ReadUInt16()
			{
				ushort v = (ushort)(_b[_i] | (_b[_i + 1] << 8));
				_i += 2;
				return v;
			}

			public uint ReadUInt32()
			{
				uint v = (uint)(_b[_i] | (_b[_i + 1] << 8) | (_b[_i + 2] << 16) | (_b[_i + 3] << 24));
				_i += 4;
				return v;
			}

			public ulong ReadUInt64()
			{
				ulong v = 0;
				for (int i = 0; i < 8; i++) v |= ((ulong)_b[_i + i]) << (i * 8);
				_i += 8;
				return v;
			}

			public IPAddress ReadIp()
			{
				byte len = ReadByte();
				if (len == 0) return null;
				byte[] bytes = new byte[len];
				Buffer.BlockCopy(_b, _i, bytes, 0, len);
				_i += len;
				return new IPAddress(bytes);
			}

			public string ReadUtf8String()
			{
				ushort len = ReadUInt16();
				if (len == 0) return string.Empty;
				string s = System.Text.Encoding.UTF8.GetString(_b, _i, len);
				_i += len;
				return s;
			}
		}
	}
}
