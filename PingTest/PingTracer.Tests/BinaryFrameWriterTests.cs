using System;
using System.Net;
using System.Text;
using PingTracer.Services;
using Xunit;

namespace PingTracer.Tests
{
	/// <summary>
	/// Round-trip tests for the BinaryFrameWriter primitives. The reader mirror lives
	/// in the JS client (WebSocketService.js); these tests guard the encoder so it stays
	/// in lock-step with the documented wire format.
	/// </summary>
	public class BinaryFrameWriterTests
	{
		private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static byte[] Slice(byte[] src, int offset, int length)
		{
			byte[] dst = new byte[length];
			Buffer.BlockCopy(src, offset, dst, 0, length);
			return dst;
		}

		[Fact]
		public void Header_WritesFrameTypeAndSessionIndex()
		{
			var w = new BinaryFrameWriter(0x03, 0x05);
			byte[] buf = w.ToArray();
			Assert.Equal(0x03, buf[0]);
			Assert.Equal(0x05, buf[1]);
		}

		[Fact]
		public void Integers_AreLittleEndian()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteUInt16(0x0102);
			w.WriteUInt32(0x04030201u);
			w.WriteUInt64(0x0807060504030201UL);
			byte[] buf = w.ToArray();
			Assert.Equal(new byte[] { 0x00, 0x00, 0x02, 0x01, 0x01, 0x02, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, buf);
		}

		[Fact]
		public void WriteIp_IPv4()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteIp(IPAddress.Parse("8.8.4.4"));
			byte[] buf = w.ToArray();
			Assert.Equal(4, buf[2]);
			Assert.Equal(new byte[] { 8, 8, 4, 4 }, Slice(buf, 3, 4));
		}

		[Fact]
		public void WriteIp_IPv6()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			IPAddress addr = IPAddress.Parse("2001:4860:4860::8888");
			w.WriteIp(addr);
			byte[] buf = w.ToArray();
			Assert.Equal(16, buf[2]);
			byte[] expected = addr.GetAddressBytes();
			Assert.Equal(expected, Slice(buf, 3, 16));
		}

		[Fact]
		public void WriteIp_NullEncodesAsZeroLength()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteIp(null);
			Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, w.ToArray());
		}

		[Fact]
		public void WriteIp_AnyEncodesAsZeroLength()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteIp(IPAddress.Any);
			Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, w.ToArray());
		}

		[Fact]
		public void WriteUtf8String_AsciiAndUnicode()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteUtf8String("hi");
			w.WriteUtf8String("héllo");
			byte[] buf = w.ToArray();

			// header(2) + len(2) + "hi"(2) + len(2) + utf8 bytes(6)
			Assert.Equal(0x02, buf[2]); Assert.Equal(0x00, buf[3]);
			Assert.Equal((byte)'h', buf[4]); Assert.Equal((byte)'i', buf[5]);

			byte[] hello = Encoding.UTF8.GetBytes("héllo");
			Assert.Equal((byte)hello.Length, buf[6]); Assert.Equal(0x00, buf[7]);
			Assert.Equal(hello, Slice(buf, 8, hello.Length));
		}

		[Fact]
		public void WriteUtf8String_NullOrEmptyEncodesAsZeroLength()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteUtf8String(null);
			w.WriteUtf8String(string.Empty);
			byte[] buf = w.ToArray();
			Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, buf);
		}

		[Fact]
		public void WriteUnixMs_ProducesEpochMilliseconds()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			DateTime t = Epoch.AddMilliseconds(1735689600000);
			w.WriteUnixMs(t);
			byte[] buf = w.ToArray();
			ulong expected = 1735689600000UL;
			ulong actual = 0;
			for (int i = 0; i < 8; i++) actual |= ((ulong)buf[2 + i]) << (i * 8);
			Assert.Equal(expected, actual);
		}

		[Fact]
		public void WriteUnixMs_DefaultDateTimeEmitsZero()
		{
			var w = new BinaryFrameWriter(0x00, 0x00);
			w.WriteUnixMs(default(DateTime));
			byte[] buf = w.ToArray();
			for (int i = 0; i < 8; i++) Assert.Equal(0, buf[2 + i]);
		}
	}
}
