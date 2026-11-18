using System.Runtime.InteropServices;

namespace PingTracer.Storage
{
	/// <summary>
	/// Storage form of one ping sent to one TTL position. 8 bytes.  The time is relative to the base time of the
	/// <see cref="HopSeries"/> chunk that holds it, which keeps records small while allowing sessions of unlimited length.
	/// Outside of <see cref="HopSeries"/>, pings are exchanged as <see cref="PingSample"/>.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	public struct PingRecord
	{
		/// <summary>Milliseconds since the owning chunk's base time.</summary>
		public uint OffsetMs;
		/// <summary>Round trip time in milliseconds (0 to <see cref="PingRecordStatus.MaxRtt"/>), or one of the <see cref="PingRecordStatus"/> codes.</summary>
		public ushort Rtt;
		/// <summary>Id (within the session's <see cref="AddressTable"/>) of the address that replied, or <see cref="AddressTable.None"/>.</summary>
		public ushort Addr;

		public PingRecord(uint offsetMs, ushort rtt, ushort addr)
		{
			OffsetMs = offsetMs;
			Rtt = rtt;
			Addr = addr;
		}
	}

	/// <summary>
	/// One ping with its absolute session-relative time.
	/// </summary>
	public readonly struct PingSample
	{
		/// <summary>Time the ping was sent, in milliseconds since the session start.</summary>
		public readonly long TimeMs;
		public readonly ushort Rtt;
		public readonly ushort Addr;

		public PingSample(long timeMs, ushort rtt, ushort addr)
		{
			TimeMs = timeMs;
			Rtt = rtt;
			Addr = addr;
		}

		public bool IsSuccess => Rtt <= PingRecordStatus.MaxRtt;
		public bool IsLost => Rtt == PingRecordStatus.Lost;
		/// <summary>True if this sample was completed (success or loss) and is not void.</summary>
		public bool IsCompleted => Rtt <= PingRecordStatus.MaxRtt || Rtt == PingRecordStatus.Lost;

		public override string ToString() => $"{TimeMs}ms rtt={Rtt} addr={Addr}";
	}

	public static class PingRecordStatus
	{
		/// <summary>Largest representable round trip time.</summary>
		public const ushort MaxRtt = 65000;
		/// <summary>
		/// The ping was sent, but later determined to be meaningless for this TTL (e.g. an echo reply from the
		/// destination at a TTL beyond the destination's actual hop number).  Excluded from all statistics.
		/// </summary>
		public const ushort Void = 0xFFFD;
		/// <summary>The ping was sent and no result is known yet.</summary>
		public const ushort Pending = 0xFFFE;
		/// <summary>The ping timed out or failed.</summary>
		public const ushort Lost = 0xFFFF;

		public static ushort ClampRtt(long rtt)
		{
			if (rtt < 0) return 0;
			if (rtt > MaxRtt) return MaxRtt;
			return (ushort)rtt;
		}
	}
}
