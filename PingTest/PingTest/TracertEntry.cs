using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace PingTracer
{
	/// <summary>
	/// BASED ON http://www.fluxbytes.com/csharp/tracert-implementation-in-c/
	/// </summary>
	public class TracertEntry
	{
		/// <summary>
		/// The hop id. Represents the number of the hop.
		/// </summary>
		public int HopID { get; set; }

		/// <summary>
		/// The IP address.
		/// </summary>
		public IPAddress Address { get; set; }

		/// <summary>
		/// The hostname
		/// </summary>
		public string Hostname { get; set; }

		/// <summary>
		/// The reply time it took for the host to receive and reply to the request in milliseconds.
		/// </summary>
		public long ReplyTime { get; set; }

		/// <summary>
		/// The reply status of the request.
		/// </summary>
		public IPStatus ReplyStatus { get; set; }

		public override string ToString()
		{
			string host;
			if (Address == null)
				host = "N/A";
			else if (string.IsNullOrEmpty(Hostname))
				host = Address.ToString();
			else
				host = Hostname + "[" + Address.ToString() + "]";
			string status;
			if (ReplyStatus == IPStatus.TimedOut)
				status = "Request Timed Out";
			else
				status = ReplyTime.ToString() + " ms";
			return HopID + " | " + host + " | " + status;
		}
	}
}
