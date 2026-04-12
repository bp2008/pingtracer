using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
		/// <summary>
		/// Legacy field retained during PingTracer 2.x lifecycle for backward-compatible deserialization and one-time migration from PingTracer 1.x format.
		/// After migration, this list will be empty.
		/// </summary>
		public List<HostSettings> hostHistory = new List<HostSettings>();
		public int cacheSize = 360000;
		public bool fastRefreshScrollingGraphs = true;
		public int graphScrollMultiplier = 50;
		public bool showDateOnGraphTimeline = true;
		public string customTimeStr;
		public WindowParams lastWindowParams = null;
		public int osWindowTopMargin = 0;
		public int osWindowLeftMargin = 7;
		public int osWindowRightMargin = 7;
		public int osWindowBottomMargin = 7;
		public int maxHeightOfPingTimeoutLine = 10000;
		public int pingPayloadSizeBytes = 32;
		/// <summary>
		/// The Guid of the most recently loaded PingConfiguration, used to auto-load on next launch.
		/// </summary>
		public string lastLoadedConfigurationGuid = null;

		public bool Save()
		{
			return Save(settingsFilePath);
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
				return SettingsFolderPath + "settings.cfg";
			}
		}
		/// <summary>
		/// Gets the absolute path to the settings folder.
		/// </summary>
		public static string SettingsFolderPath
		{
			get
			{
				string path = Environment.CurrentDirectory.TrimEnd('/', '\\') + '/';
				if (!File.Exists(path + "settings.cfg"))
				{
					path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd('/', '\\') + "/PingTracer/";
				}
				return path;
			}
		}
		/// <summary>
		/// In Explorer, opens the folder containing the settings file.
		/// </summary>
		public void OpenSettingsFolder()
		{
			Process.Start(new ProcessStartInfo(SettingsFolderPath) { UseShellExecute = true });
		}
	}
}
