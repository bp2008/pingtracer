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
		/// <summary>
		/// Legacy field retained only for backward-compatible XML deserialization.
		/// PingTracer 3+ uses time-based retention via <see cref="dataRetentionPeriod"/>.
		/// </summary>
		[Obsolete("PingTracer 3+ uses time-based retention. See dataRetentionPeriod.")]
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

		// --- PingTracer 3 monitoring/retention/persistence settings ---

		/// <summary>
		/// Legacy global retention.  Retention is now set per configuration (<see cref="PingConfiguration.dataRetentionSeconds"/>);
		/// this is only read so that old settings files load.
		/// </summary>
		public long dataRetentionSeconds = (long)TimeSpan.FromDays(1).TotalSeconds;

		/// <summary>Largest TTL the route monitor will ever probe.</summary>
		public int maxHopsDefault = 30;

		/// <summary>Milliseconds after which an unanswered ping is considered lost.</summary>
		public int pingTimeoutMs = 4000;

		/// <summary>
		/// If true, each configuration's graph data is saved to disk (and loaded again when the configuration is selected), so
		/// it survives closing PingTracer.  Old data is deleted according to each configuration's retention.
		/// </summary>
		public bool saveSessionData = true;

		/// <summary>Legacy flush interval (10 seconds), ignored.  See <see cref="dataFlushIntervalSeconds"/>.</summary>
		public int diskFlushIntervalSeconds = 10;

		/// <summary>
		/// How often graph data is written to disk.  Longer intervals compress better (Brotli restarts its entropy coding at
		/// every flush); a crash loses at most this much data.
		/// </summary>
		public int dataFlushIntervalSeconds = 60;

		/// <summary>
		/// Directory where data files are written. Null means <c>{Globals.WritableDirectoryBase}/data/</c>, which holds a
		/// subdirectory per configuration.
		/// </summary>
		public string dataDirectory = null;

		/// <summary>The directory holding every configuration's data directory.</summary>
		[XmlIgnore]
		public string DataRoot => Storage.PingDataFile.ResolveDataDirectory(dataDirectory, BPUtil.Globals.WritableDirectoryBase);

		/// <summary>Strongly-typed accessor for <see cref="dataRetentionSeconds"/>.</summary>
		[XmlIgnore]
		public TimeSpan DataRetentionPeriod
		{
			get => TimeSpan.FromSeconds(dataRetentionSeconds);
			set => dataRetentionSeconds = (long)value.TotalSeconds;
		}

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
