using System;
using System.Collections.Generic;

namespace PingTracer.Tracer
{
	/// <summary>
	/// Describes a distinct set of hosts and monitoring options.  Replaces <see cref="HostSettings"/>.
	/// </summary>
	public class PingConfiguration
	{
		/// <summary>
		/// A unique identifier for this configuration, used for programmatic identification.
		/// </summary>
		public string guid = Guid.NewGuid().ToString();
		/// <summary>
		/// The user-facing unique name of the configuration.
		/// </summary>
		public string displayName = "";
		/// <summary>
		/// A list of hosts to monitor.
		/// </summary>
		public List<Host> hosts = new List<Host>();

		// --- Monitoring options (carried over from HostSettings) ---

		public int rate = 1;
		public bool pingsPerSecond = true;
		public bool doTraceRoute = true;
		public bool reverseDnsLookup = true;
		/// <summary>
		/// If true, prefer IPv4 addresses when DNS returns both IPv4 and IPv6. Applies to all hosts in this configuration.
		/// </summary>
		public bool preferIPv4 = true;

		// --- Graph display options ---

		public bool drawServerNames = true;
		public bool drawLastPing = true;
		public bool drawAverage = true;
		public bool drawJitter = false;
		public bool drawMinMax = false;
		public bool drawPacketLoss = true;
		public bool drawLimitText = false;
		public int badThreshold = 100;
		public int worseThreshold = 200;
		public int upperLimit = 300;
		public int lowerLimit = 0;
		public int ScalingMethodID = 0;

		// --- Logging options ---

		public bool logFailures = true;
		public bool logSuccesses = false;

		/// <summary>
		/// Creates a PingConfiguration from a legacy HostSettings object.
		/// </summary>
		public static PingConfiguration FromHostSettings(HostSettings hs)
		{
			PingConfiguration cfg = new PingConfiguration();
			cfg.displayName = string.IsNullOrWhiteSpace(hs.displayName) ? hs.host : hs.displayName;

			// Parse the legacy host field which may contain comma/space-separated hosts
			string[] addresses = (hs.host ?? "").Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			cfg.hosts = new List<Host>();
			foreach (string address in addresses)
			{
				Host h = new Host();
				h.hostname = address.Trim();
				cfg.hosts.Add(h);
			}

			cfg.preferIPv4 = hs.preferIpv4;
			cfg.rate = hs.rate;
			cfg.pingsPerSecond = hs.pingsPerSecond;
			cfg.doTraceRoute = hs.doTraceRoute;
			cfg.reverseDnsLookup = hs.reverseDnsLookup;
			cfg.drawServerNames = hs.drawServerNames;
			cfg.drawLastPing = hs.drawLastPing;
			cfg.drawAverage = hs.drawAverage;
			cfg.drawJitter = hs.drawJitter;
			cfg.drawMinMax = hs.drawMinMax;
			cfg.drawPacketLoss = hs.drawPacketLoss;
			cfg.drawLimitText = hs.drawLimitText;
			cfg.badThreshold = hs.badThreshold;
			cfg.worseThreshold = hs.worseThreshold;
			cfg.upperLimit = hs.upperLimit;
			cfg.lowerLimit = hs.lowerLimit;
			cfg.ScalingMethodID = hs.ScalingMethodID;
			cfg.logFailures = hs.logFailures;
			cfg.logSuccesses = hs.logSuccesses;

			return cfg;
		}

		/// <summary>
		/// Returns the combined hosts string for use when starting pings (comma-separated hostnames).
		/// </summary>
		public string GetHostString()
		{
			if (hosts == null || hosts.Count == 0)
				return "";
			List<string> parts = new List<string>();
			foreach (Host h in hosts)
			{
				if (!string.IsNullOrWhiteSpace(h.hostname))
					parts.Add(h.hostname);
			}
			return string.Join(",", parts);
		}

		/// <summary>
		/// Returns the preferIPv4 value from the configuration level.
		/// </summary>
		public bool GetPreferIPv4()
		{
			return preferIPv4;
		}
	}
}
