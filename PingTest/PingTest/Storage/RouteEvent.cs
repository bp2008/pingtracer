namespace PingTracer.Storage
{
	public enum RouteEventKind : byte
	{
		/// <summary>An address replied from a hop where it had not been seen recently.</summary>
		HopAddress = 1,
		/// <summary>The destination's hop number changed (or was discovered).</summary>
		DestinationTtl = 2,
		/// <summary>
		/// The host name now resolves to a different address, which is pinged from then on.  Ttl is 0 and Value is the new
		/// address's id.
		/// </summary>
		Target = 3,
	}

	/// <summary>
	/// A notable change in the route, used to draw route-change markers.
	/// </summary>
	public readonly struct RouteEvent
	{
		/// <summary>Session-relative time in milliseconds.</summary>
		public readonly long TimeMs;
		public readonly RouteEventKind Kind;
		/// <summary>For <see cref="RouteEventKind.HopAddress"/>: the hop's TTL. For <see cref="RouteEventKind.DestinationTtl"/>: the new destination TTL.</summary>
		public readonly byte Ttl;
		/// <summary>For <see cref="RouteEventKind.HopAddress"/> and <see cref="RouteEventKind.Target"/>: the new address id. For <see cref="RouteEventKind.DestinationTtl"/>: the previous destination TTL (0 if unknown) stored as an address-sized field.</summary>
		public readonly ushort Value;
		/// <summary>True if this event is part of initial route discovery rather than a change.</summary>
		public readonly bool Initial;

		public RouteEvent(long timeMs, RouteEventKind kind, byte ttl, ushort value, bool initial)
		{
			TimeMs = timeMs;
			Kind = kind;
			Ttl = ttl;
			Value = value;
			Initial = initial;
		}
	}
}
