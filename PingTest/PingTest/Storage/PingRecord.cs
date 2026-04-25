using System.Runtime.InteropServices;

namespace PingTracer.Storage
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct PingRecord
	{
		public uint RelativeTimeMs;
		public ushort RoundTripMs;
	}

	public static class PingRecordStatus
	{
		public const ushort Pending = 0xFFFE;
		public const ushort Timeout = 0xFFFF;

		public static bool IsSuccess(ushort rtt) => rtt <= 65533;
	}
}
