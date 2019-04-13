using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingTracer.Tracer
{
	public class Profile
	{
		public string host;
		public string displayName = "";
		public int rate = 1;
		public bool pingsPerSecond = true;
		public bool doTraceRoute = true;
		public bool reverseDnsLookup = true;
		public bool drawServerNames = true;
		public bool drawLastPing = true;
		public bool drawAverage = true;
		public bool drawJitter = false;
		public bool drawMinMax = false;
		public bool drawPacketLoss = true;
		public int badThreshold = 100;
		public int worseThreshold = 200;

		public override bool Equals(object other)
		{
			if (other is Profile)
			{
				Profile o = (Profile)other;
				return host == o.host
					&& rate == o.rate
					&& pingsPerSecond == o.pingsPerSecond
					&& doTraceRoute == o.doTraceRoute
					&& reverseDnsLookup == o.reverseDnsLookup
					&& drawServerNames == o.drawServerNames
					&& drawMinMax == o.drawMinMax
					&& drawPacketLoss == o.drawPacketLoss
					&& badThreshold == o.badThreshold
					&& worseThreshold == o.worseThreshold;
			}
			return false;
		}
		public override int GetHashCode()
		{
			return host.GetHashCode() ^ rate.GetHashCode() ^ badThreshold.GetHashCode() ^ worseThreshold.GetHashCode();
		}
	}
}
