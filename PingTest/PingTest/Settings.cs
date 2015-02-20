using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PingTest
{
	public class Settings : SerializableObjectBase
	{
		public List<HostSettings> hostHistory = new List<HostSettings>();

		public bool Save()
		{
			lock (hostHistory)
			{
				return Save(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd('/','\\') + "/PingTracer/settings.cfg");
			}
		}
		public bool Load()
		{
			return Load(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd('/','\\') + "/PingTracer/settings.cfg");
		}
	}
	public class HostSettings
	{
		public string host;
		public string displayName = "";
		public int rate = 1;
		public bool doTraceRoute = true;
		public bool drawServerNames = false;
		public bool drawMinMax = false;
		public int badThreshold = 100;
		public int worseThreshold = 200;

		public override bool Equals(object other)
		{
			if (other is HostSettings)
			{
				HostSettings o = (HostSettings)other;
				return host == o.host
					&& rate == o.rate
					&& doTraceRoute == o.doTraceRoute
					&& drawServerNames == o.drawServerNames
					&& drawMinMax == o.drawMinMax
					&& badThreshold == o.badThreshold
					&& worseThreshold == o.worseThreshold;
			}
			return false;
		}
	}
}
