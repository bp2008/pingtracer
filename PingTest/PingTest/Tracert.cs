using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PingTest
{
	/// <summary>
	/// BASED ON http://www.fluxbytes.com/csharp/tracert-implementation-in-c/
	/// </summary>
	public static class Tracert
	{
		/// <summary>
		/// Traces the route which data have to travel through in order to reach an IP address.
		/// </summary>
		/// <param name="address">The IP address of the destination.</param>
		/// <param name="maxHops">Max hops to be returned.</param>
		public static IEnumerable<TracertEntry> Trace(IPAddress address, int maxHops, int timeout)
		{
			// Max hops should be at least one or else there won't be any data to return.
			if (maxHops < 1)
				throw new ArgumentException("Max hops can't be lower than 1.");

			// Ensure that the timeout is not set to 0 or a negative number.
			if (timeout < 1)
				throw new ArgumentException("Timeout value must be higher than 0.");


			Ping ping = new Ping();
			PingOptions pingOptions = new PingOptions(1, true);
			Stopwatch pingReplyTime = new Stopwatch();
			PingReply reply;
			byte[] buffer = new byte[0];
			int consecutiveTimeouts = 0;
			do
			{
				pingReplyTime.Start();
				reply = ping.Send(address, timeout, buffer, pingOptions);
				pingReplyTime.Stop();

				string hostname = string.Empty;
				if (reply.Address != null)
				{
					try
					{
						hostname = Dns.GetHostByAddress(reply.Address).HostName;    // Retrieve the hostname for the replied address.
					}
					catch (SocketException) { /* No host available for that address. */ }
				}
				if (reply.Status == IPStatus.TimedOut)
					consecutiveTimeouts++;
				else
					consecutiveTimeouts = 0;
				// Return out TracertEntry object with all the information about the hop.
				yield return new TracertEntry()
				{
					HopID = pingOptions.Ttl,
					Address = reply.Address,
					Hostname = hostname,
					ReplyTime = pingReplyTime.ElapsedMilliseconds,
					ReplyStatus = reply.Status
				};

				pingOptions.Ttl++;
				pingReplyTime.Reset();
			}
			while (reply.Status != IPStatus.Success && pingOptions.Ttl <= maxHops && consecutiveTimeouts < 3);
		}
	}
}
