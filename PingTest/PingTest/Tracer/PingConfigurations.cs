using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PingTracer
{
	/// <summary>
	/// Manages the list of <see cref="PingConfiguration"/> objects.  Serialized to hosts.cfg.
	/// </summary>
	public class PingConfigurations : SerializableObjectBase
	{
		public List<PingConfiguration> configurations = new List<PingConfiguration>();

		/// <summary>
		/// Saves this instance to hosts.cfg.
		/// </summary>
		public bool Save()
		{
			configurations.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
			return Save(HostsFilePath);
		}

		/// <summary>
		/// Loads this instance from hosts.cfg.
		/// </summary>
		public bool Load()
		{
			bool result = Load(HostsFilePath);
			configurations.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
			return result;
		}

		/// <summary>
		/// Gets the absolute path to the hosts.cfg file, using the same settings folder logic as Settings.
		/// </summary>
		public static string HostsFilePath
		{
			get
			{
				return SettingsFolderPath + "hosts.cfg";
			}
		}

		/// <summary>
		/// Gets the absolute path to the settings folder. If settings.cfg exists in the current working directory,
		/// uses that directory. Otherwise falls back to the ApplicationData subfolder.
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
		/// Performs one-time migration from legacy HostSettings in settings.cfg to PingConfiguration objects in hosts.cfg.
		/// Call this after loading Settings.  If hostHistory has entries and hosts.cfg does not yet exist, 
		/// the entries are migrated and hostHistory is cleared.
		/// </summary>
		/// <param name="settings">The Settings instance that may contain legacy hostHistory entries.</param>
		public static void MigrateFromSettings(Settings settings)
		{
			if (settings.hostHistory == null || settings.hostHistory.Count == 0)
				return;

			// Backup the current settings file before migration
			try
			{
				string settingsFile = Settings.SettingsFolderPath + "settings.cfg";
				if (File.Exists(settingsFile))
				{
					string backupFile = settingsFile + ".pre-migration.bak";
					File.Copy(settingsFile, backupFile, true);
				}
			}
			catch (Exception) { }

			PingConfigurations pingConfigs = new PingConfigurations();
			// Load existing hosts.cfg if it exists (to avoid overwriting another instance's data)
			pingConfigs.Load();

			HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PingConfiguration existing in pingConfigs.configurations)
				existingNames.Add(existing.displayName);

			foreach (HostSettings hs in settings.hostHistory)
			{
				PingConfiguration cfg = PingConfiguration.FromHostSettings(hs);
				// Ensure unique display name
				string baseName = cfg.displayName;
				if (string.IsNullOrWhiteSpace(baseName))
					baseName = "Untitled";
				string name = baseName;
				int counter = 2;
				while (existingNames.Contains(name))
				{
					name = baseName + " (" + counter + ")";
					counter++;
				}
				cfg.displayName = name;
				existingNames.Add(name);
				pingConfigs.configurations.Add(cfg);
			}

			pingConfigs.Save();

			// Clear the legacy host history now that migration is complete
			settings.hostHistory.Clear();
			settings.Save();
		}

		/// <summary>
		/// Finds a PingConfiguration by its Guid.
		/// </summary>
		public PingConfiguration GetByGuid(string guid)
		{
			if (guid == null)
				return null;
			return configurations.FirstOrDefault(c => c.guid == guid);
		}

		/// <summary>
		/// Finds a PingConfiguration by its display name (case-insensitive).
		/// </summary>
		public PingConfiguration GetByDisplayName(string displayName)
		{
			if (displayName == null)
				return null;
			return configurations.FirstOrDefault(c => string.Equals(c.displayName, displayName, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Checks if a display name is unique among all configurations, optionally excluding a specific Guid.
		/// </summary>
		public bool IsDisplayNameUnique(string displayName, string excludeGuid = null)
		{
			if (string.IsNullOrWhiteSpace(displayName))
				return false;
			return !configurations.Any(c =>
				string.Equals(c.displayName, displayName, StringComparison.OrdinalIgnoreCase)
				&& c.guid != excludeGuid);
		}

		/// <summary>
		/// Atomically saves a single PingConfiguration. Loads hosts.cfg from disk, replaces or adds the 
		/// configuration matching the given Guid, then saves back to disk.
		/// This is safe for concurrent instances of PingTracer that may share the same hosts.cfg.
		/// </summary>
		public static void SaveSingleConfiguration(PingConfiguration cfg)
		{
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();

			bool found = false;
			for (int i = 0; i < allConfigs.configurations.Count; i++)
			{
				if (allConfigs.configurations[i].guid == cfg.guid)
				{
					allConfigs.configurations[i] = cfg;
					found = true;
					break;
				}
			}
			if (!found)
				allConfigs.configurations.Add(cfg);

			allConfigs.Save();
		}

		/// <summary>
		/// Atomically deletes a single PingConfiguration by Guid.  Loads hosts.cfg from disk, removes the
		/// configuration, then saves back to disk.
		/// </summary>
		public static void DeleteConfiguration(string guid)
		{
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();

			for (int i = 0; i < allConfigs.configurations.Count; i++)
			{
				if (allConfigs.configurations[i].guid == guid)
				{
					allConfigs.configurations.RemoveAt(i);
					break;
				}
			}

			allConfigs.Save();
		}
	}
}
