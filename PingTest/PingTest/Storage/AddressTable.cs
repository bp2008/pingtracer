using System;
using System.Collections.Generic;
using System.Net;

namespace PingTracer.Storage
{
	/// <summary>
	/// Maps IP addresses seen during a session to compact 16-bit ids, and holds their reverse-DNS hostnames.
	/// <para>Not thread safe. All access is synchronized by <see cref="MonitoringSession.DataLock"/>.</para>
	/// </summary>
	public sealed class AddressTable
	{
		/// <summary>Address id meaning "no address" (e.g. for lost pings).</summary>
		public const ushort None = 0xFFFF;

		private readonly List<Entry> entries = new List<Entry>();
		private readonly Dictionary<IPAddress, ushort> idByAddress = new Dictionary<IPAddress, ushort>();

		public int Count => entries.Count;

		/// <summary>
		/// Returns the id for <paramref name="address"/>, adding it if it is new.
		/// Returns <see cref="None"/> for null or if the table is full.
		/// </summary>
		public ushort GetOrAdd(IPAddress address, out bool added)
		{
			added = false;
			if (address == null)
				return None;
			if (idByAddress.TryGetValue(address, out ushort id))
				return id;
			if (entries.Count >= None)
				return None;
			id = (ushort)entries.Count;
			entries.Add(new Entry(address));
			idByAddress[address] = id;
			added = true;
			return id;
		}

		/// <summary>Adds an address with a specific id (used when loading from disk). Ids must be added in order.</summary>
		public void AddWithId(ushort id, IPAddress address)
		{
			if (id != entries.Count)
				throw new InvalidOperationException("Address ids must be added sequentially. Expected " + entries.Count + ", got " + id);
			entries.Add(new Entry(address));
			idByAddress[address] = id;
		}

		public bool TryGetId(IPAddress address, out ushort id)
		{
			if (address == null)
			{
				id = None;
				return false;
			}
			return idByAddress.TryGetValue(address, out id);
		}

		public IPAddress GetAddress(ushort id) => id < entries.Count ? entries[id].Address : null;

		public string GetHostname(ushort id) => id < entries.Count ? entries[id].Hostname : null;

		public void SetHostname(ushort id, string hostname)
		{
			if (id < entries.Count)
				entries[id].Hostname = hostname;
		}

		public IEnumerable<(ushort id, IPAddress address, string hostname)> All()
		{
			for (int i = 0; i < entries.Count; i++)
				yield return ((ushort)i, entries[i].Address, entries[i].Hostname);
		}

		private sealed class Entry
		{
			public readonly IPAddress Address;
			public string Hostname;
			public Entry(IPAddress address) { Address = address; }
		}
	}
}
