using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PingTracer.Services
{
	/// <summary>
	/// Direct DNS queries through the Windows DNS client API.  Unlike <see cref="Dns.GetHostAddresses(string)"/>, which hides
	/// the addresses of a family the computer has no global address for, this returns every A or AAAA record, so a host with
	/// only IPv6 addresses can still be monitored (and shown as unreachable) on a computer without IPv6 internet.
	/// </summary>
	internal static class WindowsDns
	{
		private const ushort DNS_TYPE_A = 1;
		private const ushort DNS_TYPE_AAAA = 28;
		private const uint DNS_QUERY_STANDARD = 0;
		private const int DnsFreeRecordList = 1;

		[DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, ExactSpelling = true)]
		private static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr results, IntPtr reserved);

		[DllImport("dnsapi.dll", ExactSpelling = true)]
		private static extern void DnsRecordListFree(IntPtr recordList, int freeType);

		/// <summary>Returns the addresses of the given family, or an empty array if there are none or the query failed.</summary>
		public static IPAddress[] Query(string host, AddressFamily family)
		{
			if (!OperatingSystem.IsWindows())
				return Array.Empty<IPAddress>();
			ushort type = family == AddressFamily.InterNetworkV6 ? DNS_TYPE_AAAA : DNS_TYPE_A;
			IntPtr list = IntPtr.Zero;
			List<IPAddress> found = new List<IPAddress>();
			try
			{
				if (DnsQuery(host, type, DNS_QUERY_STANDARD, IntPtr.Zero, out list, IntPtr.Zero) != 0)
					return Array.Empty<IPAddress>();
				// DNS_RECORDW: pNext, pName, WORD wType, WORD wDataLength, DWORD Flags, DWORD dwTtl, DWORD dwReserved, Data.
				int typeOffset = 2 * IntPtr.Size;
				int dataOffset = 2 * IntPtr.Size + 16;
				for (IntPtr rec = list; rec != IntPtr.Zero; rec = Marshal.ReadIntPtr(rec))
				{
					if ((ushort)Marshal.ReadInt16(rec, typeOffset) != type)
						continue; // CNAME records of the chain
					byte[] bytes = new byte[type == DNS_TYPE_AAAA ? 16 : 4];
					Marshal.Copy(rec + dataOffset, bytes, 0, bytes.Length);
					found.Add(new IPAddress(bytes));
				}
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
				return Array.Empty<IPAddress>();
			}
			finally
			{
				if (list != IntPtr.Zero)
					DnsRecordListFree(list, DnsFreeRecordList);
			}
			return found.ToArray();
		}
	}
}
