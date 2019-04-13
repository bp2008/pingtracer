using SmartPing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PingTracer
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
		public static IEnumerable<TracertEntry> Trace(IPAddress address, int maxHops, int timeout, bool reverseDnsLookup)
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
				if (reverseDnsLookup && reply.Address != null)
				{
					try
					{
						hostname = Dns.GetHostEntry(reply.Address).HostName;    // Retrieve the hostname for the replied address.
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
			while (reply.Status != IPStatus.Success && pingOptions.Ttl <= maxHops && consecutiveTimeouts < 5);
		}

		///// <summary>
		///// Quickly traces the route which data have to travel through in order to reach an IP address.  All hosts are pinged concurrently.
		///// </summary>
		///// <param name="address">The IP address of the destination.</param>
		///// <param name="maxHops">Max hops to be returned.</param>
		//public static IEnumerable<TracertEntry> FastTrace(IPAddress address, int maxHops, int timeout)
		//{
		//	// Max hops should be at least one or else there won't be any data to return.
		//	if (maxHops < 1)
		//		throw new ArgumentException("Max hops can't be lower than 1.");

		//	// Ensure that the timeout is not set to 0 or a negative number.
		//	if (timeout < 1)
		//		throw new ArgumentException("Timeout value must be higher than 0.");

		//	byte[] buffer = new byte[0];
		//	ConcurrentQueue<TracertEntry> traceRouteReplies = new ConcurrentQueue<TracertEntry>();
		//	for (int i = 1; i <= maxHops; i++)
		//	{
		//		Ping ping = PingInstancePool.Get();
		//		ping.PingCompleted += ping_PingCompleted;
		//		PingOptions pingOptions = new PingOptions(i, true);
		//		Stopwatch pingReplyTime = new Stopwatch();
		//		pingReplyTime.Start();
		//		ping.SendAsync(address, timeout, buffer, pingOptions, new object[] { pingOptions, pingReplyTime, traceRouteReplies, ping });
		//	}
		//	while (traceRouteReplies.Count < maxHops)
		//		Thread.Sleep(100);
		//	SortedList<int, TracertEntry> sortedReplies = new SortedList<int, TracertEntry>();
		//	TracertEntry reply;
		//	while (traceRouteReplies.TryDequeue(out reply))
		//	{
		//		sortedReplies.Add(reply.HopID, reply);
		//	}
		//	foreach (int hopId in sortedReplies.Keys)
		//	{
		//		yield return sortedReplies[hopId];
		//		if (sortedReplies[hopId].ReplyStatus == IPStatus.Success)
		//			break;
		//	}
		//}

		//static void ping_PingCompleted(object sender, PingCompletedEventArgs e)
		//{
		//	Stopwatch pingReplyTime = (Stopwatch)((object[])e.UserState)[1];
		//	pingReplyTime.Stop();
		//	PingOptions pingOptions = (PingOptions)((object[])e.UserState)[0];
		//	ConcurrentQueue<TracertEntry> traceRouteReplies = (ConcurrentQueue<TracertEntry>)((object[])e.UserState)[2];
		//	Ping ping = (Ping)((object[])e.UserState)[3];
		//	ping.PingCompleted -= ping_PingCompleted;
		//	PingInstancePool.Recycle(ping);

		//	string hostname = string.Empty;
		//	if (e.Reply.Address != null)
		//	{
		//		try
		//		{
		//			hostname = Dns.GetHostByAddress(e.Reply.Address).HostName;    // Retrieve the hostname for the replied address.
		//		}
		//		catch (SocketException) { /* No host available for that address. */ }
		//	}

		//	Console.WriteLine(pingOptions.Ttl + " " + e.Reply.Address);
		//	traceRouteReplies.Enqueue(new TracertEntry()
		//		{
		//			HopID = pingOptions.Ttl,
		//			Address = e.Reply.Address,
		//			Hostname = hostname,
		//			ReplyTime = pingReplyTime.ElapsedMilliseconds,
		//			ReplyStatus = e.Reply.Status
		//		});
		//}
	}
}
