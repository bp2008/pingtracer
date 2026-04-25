using System.Runtime.CompilerServices;
using PingTracer.Storage;
using Xunit;

namespace PingTracer.Tests
{
	public class PingRecordTests
	{
		[Fact]
		public void PingRecord_Is6Bytes()
		{
			Assert.Equal(6, Unsafe.SizeOf<PingRecord>());
		}

		[Fact]
		public void PendingStatusConstant_Is0xFFFE()
		{
			Assert.Equal(0xFFFE, PingRecordStatus.Pending);
		}

		[Fact]
		public void TimeoutStatusConstant_Is0xFFFF()
		{
			Assert.Equal(0xFFFF, PingRecordStatus.Timeout);
		}

		[Fact]
		public void IsSuccess_ReturnsTrueForValidRtt()
		{
			Assert.True(PingRecordStatus.IsSuccess(0));
			Assert.True(PingRecordStatus.IsSuccess(100));
			Assert.True(PingRecordStatus.IsSuccess(65533));
		}

		[Fact]
		public void IsSuccess_ReturnsFalseForSentinels()
		{
			Assert.False(PingRecordStatus.IsSuccess(PingRecordStatus.Pending));
			Assert.False(PingRecordStatus.IsSuccess(PingRecordStatus.Timeout));
		}
	}
}
