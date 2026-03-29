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
	}
}
