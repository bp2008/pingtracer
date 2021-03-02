using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace PingTracer
{
	public class Settings : SerializableObjectBase
	{
		public bool logTextOutputToFile = true;
		public bool delayMostRecentPing = true;
		public bool warnGraphNotLive = true;
		public List<HostSettings> hostHistory = new List<HostSettings>();
		public int cacheSize = 10000;
		public bool fastRefreshScrollingGraphs = true;
		public int graphScrollMultiplier = 1;
		public bool showDateOnGraphTimeline = true;
		public string customTimeStr;

		public bool Save()
		{
			lock (hostHistory)
			{
				return Save(settingsFilePath);
			}
		}
		public bool Load()
		{
			return Load(settingsFilePath);
		}
		/// <summary>
		/// Gets the absolute path to the settings file.
		/// </summary>
		private static string settingsFilePath
		{
			get
			{
				return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd('/', '\\') + "/PingTracer/settings.cfg";
			}
		}
	}
}
