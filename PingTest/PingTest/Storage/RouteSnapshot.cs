using System;
using System.Collections.Generic;
using System.Net;

namespace PingTracer.Storage
{
	/// <summary>
	/// Represents the known route topology at a point in time.
	/// Hops is 0-indexed; a null entry means no response at that hop position.
	/// </summary>
	public class RouteSnapshot
	{
		public readonly DateTime TimestampUtc;

		/// <summary>
		/// 0-based hop entries. null means no response at that position.
		/// Length reflects the highest TTL included in the snapshot.
		/// </summary>
		public readonly HopTimeSeries[] Hops;

		public RouteSnapshot(DateTime timestampUtc, HopTimeSeries[] hops)
		{
			TimestampUtc = timestampUtc;
			Hops = hops ?? throw new ArgumentNullException(nameof(hops));
		}

		/// <summary>
		/// Computes the set of hop-address changes between this snapshot and a previous one.
		/// Returns an empty list when the routes are identical.
		/// </summary>
		public IReadOnlyList<RouteChange> DiffFrom(RouteSnapshot previous)
		{
			var changes = new List<RouteChange>();

			int maxLen = Math.Max(Hops.Length, previous?.Hops?.Length ?? 0);
			for (int i = 0; i < maxLen; i++)
			{
				IPAddress oldAddr = previous != null && i < previous.Hops.Length ? previous.Hops[i]?.Address : null;
				IPAddress newAddr = i < Hops.Length ? Hops[i]?.Address : null;

				bool changed = oldAddr == null
					? newAddr != null
					: newAddr == null || !oldAddr.Equals(newAddr);

				if (changed)
					changes.Add(new RouteChange((byte)i, oldAddr, newAddr));
			}

			return changes;
		}
	}

	public class RouteChange
	{
		public readonly byte HopIndex;
		public readonly IPAddress OldAddress;
		public readonly IPAddress NewAddress;

		public RouteChange(byte hopIndex, IPAddress oldAddress, IPAddress newAddress)
		{
			HopIndex = hopIndex;
			OldAddress = oldAddress;
			NewAddress = newAddress;
		}
	}
}
