using System;
using System.Xml.Serialization;

namespace PingTracer.Tracer
{
	/// <summary>
	/// Describes a target host for monitoring.
	/// </summary>
	public class Host
	{
		/// <summary>
		/// Hostname or IP address.
		/// </summary>
		public string hostname = "";
		/// <summary>
		/// If true and the DNS response for <see cref="hostname"/> includes IPv4 and IPv6 addresses, use the IPv4 address.
		/// </summary>
		public bool preferIPv4 = true;
	}
}
