using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PingTracer.Storage
{
	/// <summary>
	/// The graph data of one ping configuration: a <see cref="MonitoringSession"/> per host, rebuilt from the segment files in
	/// the configuration's data directory (<c>data/{configuration guid}/</c>) and kept across runs, so that Stop followed by
	/// Start, or closing and reopening PingTracer, continues the same graphs.
	/// <para>
	/// <b>Retention</b> is measured in monitoring time, not calendar time: the newest <c>retention</c> worth of time during
	/// which the configuration was running is kept, however long ago that was.  A configuration that keeps one day of data
	/// but runs for one hour a day therefore shows about 24 days of history, and data is never discarded merely because
	/// PingTracer was not running.  Whole segment files are deleted once all of their data is older than the cutoff, and
	/// files are only deleted while the configuration runs (from Start onward), never when it is merely loaded.
	/// </para>
	/// <para>Thread safety: members lock <see cref="SyncRoot"/>; it may be taken before a session's DataLock, never after.</para>
	/// </summary>
	public sealed class ConfigDataSet
	{
		/// <summary>Name of the lock file held while a configuration runs.</summary>
		public const string LockFileName = "running.lock";
		/// <summary>Breaks between runs shorter than this are not shown as gaps.</summary>
		public const long MinGapMs = 5000;

		public readonly object SyncRoot = new object();
		public string ConfigGuid { get; }
		/// <summary>The configuration's data directory.</summary>
		public string Directory { get; }

		private readonly List<MonitoringSession> sessions = new List<MonitoringSession>();
		/// <summary>Monitoring periods (Unix ms), merged and ascending, excluding the current run.</summary>
		private List<(long start, long end)> runs = new List<(long, long)>();
		private long currentRunStart = -1;
		/// <summary>Segment files this instance knows about (from loading, or that it wrote), with their sizes.</summary>
		private readonly Dictionary<string, long> knownFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		private long cutoffUnixMs = long.MinValue;

		public ConfigDataSet(string dataRoot, string configGuid)
		{
			if (string.IsNullOrWhiteSpace(configGuid) || configGuid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || configGuid.Contains(".."))
				throw new ArgumentException("Invalid configuration id.", nameof(configGuid));
			ConfigGuid = configGuid;
			Directory = Path.Combine(dataRoot, configGuid);
		}

		public IReadOnlyList<MonitoringSession> Sessions
		{
			get { lock (SyncRoot) return sessions.ToArray(); }
		}

		public MonitoringSession Find(string hostKey, SessionMode mode)
		{
			lock (SyncRoot)
				return sessions.FirstOrDefault(s => s.HostKey == hostKey && s.Mode == mode);
		}

		public void Add(MonitoringSession session)
		{
			lock (SyncRoot)
			{
				sessions.Add(session);
				PushHistory(session);
			}
		}

		/// <summary>True while a run is in progress.</summary>
		public bool IsRunning
		{
			get { lock (SyncRoot) return currentRunStart >= 0; }
		}

		#region Loading

		/// <summary>Information from a segment's header.</summary>
		public sealed class Segment
		{
			public string Path;
			public long Size;
			public long StartUnixMs;
			public long EndUnixMs;
			public string HostKey;
			public SessionMode Mode;
		}

		/// <summary>Lists this configuration's readable segment files.</summary>
		public List<Segment> ListSegments()
		{
			List<Segment> list = new List<Segment>();
			if (!System.IO.Directory.Exists(Directory))
				return list;
			foreach (string path in System.IO.Directory.GetFiles(Directory, "*" + PingDataFile.FileExtension))
			{
				PingDataFile.FileInfoSummary f = PingDataFile.Describe(path);
				if (f == null || f.Version != PingDataFile.CurrentVersion || !string.Equals(f.ConfigGuid, ConfigGuid, StringComparison.OrdinalIgnoreCase))
					continue;
				list.Add(new Segment { Path = f.FullPath, Size = f.SizeBytes, StartUnixMs = f.StartUnixMs, EndUnixMs = f.EndUnixMs, HostKey = f.HostKey, Mode = f.Mode });
			}
			list.Sort((a, b) => a.StartUnixMs.CompareTo(b.StartUnixMs));
			return list;
		}

		/// <summary>
		/// Reads the saved data for the given hosts.  Only the newest <paramref name="retentionMs"/> of monitoring time is loaded;
		/// nothing is deleted.  Data saved for hosts that are not listed (removed from the configuration, or saved with a
		/// different traceroute setting) is not loaded, but it still counts toward the configuration's monitoring time.
		/// </summary>
		/// <param name="dataRoot">The directory holding all configurations' data directories.</param>
		/// <param name="configGuid">The configuration.</param>
		/// <param name="hosts">Host keys and modes to load, in display order.</param>
		/// <param name="retentionMs">Monitoring time to keep.</param>
		/// <param name="log">Receives messages about files that could not be read completely.</param>
		public static ConfigDataSet Load(string dataRoot, string configGuid, IEnumerable<(string hostKey, SessionMode mode)> hosts, long retentionMs, Action<string> log = null)
		{
			ConfigDataSet set = new ConfigDataSet(dataRoot, configGuid);
			List<Segment> segments = set.ListSegments();
			foreach (Segment s in segments)
				set.knownFiles[s.Path] = s.Size;
			List<(long, long)> intervals = MergeIntervals(segments.Select(s => (s.StartUnixMs, s.EndUnixMs)), 0);
			long cutoff = ComputeCutoff(intervals, retentionMs);
			set.cutoffUnixMs = cutoff;
			set.runs = ClipIntervals(intervals, cutoff);

			foreach ((string hostKey, SessionMode mode) in hosts.Distinct())
			{
				List<Segment> mine = segments.Where(s => s.HostKey == hostKey && s.Mode == mode && s.EndUnixMs >= cutoff).ToList();
				if (mine.Count == 0)
					continue;
				PingDataFile.Header newest;
				try
				{
					newest = PingDataFile.ReadHeader(mine[mine.Count - 1].Path);
				}
				catch (Exception ex)
				{
					log?.Invoke("Unable to read " + Path.GetFileName(mine[mine.Count - 1].Path) + ": " + ex.Message);
					continue;
				}
				long origin = mine.Min(s => s.StartUnixMs);
				MonitoringSession session = new MonitoringSession(newest.Target, newest.DisplayName, mode, newest.IntervalMs, newest.TimeoutMs, origin, hostKey: hostKey);
				foreach (Segment seg in mine)
				{
					try
					{
						PingDataReader.Result r = PingDataReader.LoadInto(session, seg.Path);
						if (r.Truncated)
							log?.Invoke("Saved data file " + Path.GetFileName(seg.Path) + " ends with an incomplete block (PingTracer may have been closed unexpectedly). Everything before it was loaded.");
					}
					catch (Exception ex)
					{
						log?.Invoke("Unable to read saved data file " + Path.GetFileName(seg.Path) + ": " + ex.Message);
					}
				}
				session.PruneBeforeUnix(cutoff);
				set.sessions.Add(session);
			}
			lock (set.SyncRoot)
				foreach (MonitoringSession s in set.sessions)
					set.PushHistory(s);
			return set;
		}

		/// <summary>
		/// True if the configuration's directory holds segments this instance has not loaded or written, or that changed size:
		/// another PingTracer instance ran the configuration since it was loaded.
		/// </summary>
		public bool IsStale()
		{
			if (!System.IO.Directory.Exists(Directory))
				return false;
			lock (SyncRoot)
			{
				foreach (string path in System.IO.Directory.GetFiles(Directory, "*" + PingDataFile.FileExtension))
				{
					string full = Path.GetFullPath(path);
					if (!knownFiles.TryGetValue(full, out long size))
					{
						// Ignore files that are not this configuration's segments.
						PingDataFile.FileInfoSummary f = PingDataFile.Describe(full);
						if (f != null && f.Version == PingDataFile.CurrentVersion && string.Equals(f.ConfigGuid, ConfigGuid, StringComparison.OrdinalIgnoreCase))
							return true;
						continue;
					}
					try
					{
						if (new FileInfo(full).Length != size)
							return true;
					}
					catch { }
				}
			}
			return false;
		}

		#endregion

		#region Runs

		/// <summary>Records that a run is starting.</summary>
		public void BeginRun(long nowUnixMs)
		{
			lock (SyncRoot)
				currentRunStart = nowUnixMs;
		}

		/// <summary>Records that the run ended (at the latest data time, or now).</summary>
		public void EndRun(long endUnixMs)
		{
			lock (SyncRoot)
			{
				if (currentRunStart < 0)
					return;
				List<(long, long)> all = new List<(long, long)>(runs) { (currentRunStart, Math.Max(currentRunStart, endUnixMs)) };
				runs = ClipIntervals(MergeIntervals(all, 0), cutoffUnixMs);
				currentRunStart = -1;
				foreach (MonitoringSession s in sessions)
					PushHistory(s);
			}
		}

		/// <summary>Forgets a run that failed to start.</summary>
		public void CancelRun()
		{
			lock (SyncRoot)
				currentRunStart = -1;
		}

		/// <summary>Records the final size of a segment this instance wrote, so it is not mistaken for another instance's work.</summary>
		public void NoteSegmentClosed(string path)
		{
			if (path == null)
				return;
			try
			{
				string full = Path.GetFullPath(path);
				long size = new FileInfo(full).Length;
				lock (SyncRoot)
					knownFiles[full] = size;
			}
			catch { }
		}

		/// <summary>
		/// Discards data beyond the newest <paramref name="retentionMs"/> of monitoring time from memory and, if
		/// <paramref name="deleteFiles"/>, deletes segment files whose data is all older than that.
		/// </summary>
		/// <returns>The number of files deleted.</returns>
		public int ApplyRetention(long retentionMs, long nowUnixMs, bool deleteFiles, Action<string> log = null)
		{
			List<Segment> segments = deleteFiles ? ListSegments() : new List<Segment>();
			long cutoff;
			MonitoringSession[] list;
			lock (SyncRoot)
			{
				List<(long, long)> all = new List<(long, long)>(runs);
				foreach (Segment s in segments)
					all.Add((s.StartUnixMs, s.EndUnixMs));
				if (currentRunStart >= 0)
					all.Add((currentRunStart, Math.Max(currentRunStart, nowUnixMs)));
				List<(long, long)> merged = MergeIntervals(all, 0);
				// Recomputed every time (not only advanced), so that raising the retention while running stops deleting files.
				cutoff = ComputeCutoff(merged, retentionMs);
				cutoffUnixMs = cutoff;
				runs = ClipIntervals(MergeIntervals(runs, 0), cutoff);
				list = sessions.ToArray();
			}
			foreach (MonitoringSession s in list)
				s.PruneBeforeUnix(cutoff);
			int deleted = 0;
			foreach (Segment seg in segments)
			{
				if (seg.EndUnixMs >= cutoff)
					continue;
				try
				{
					File.Delete(seg.Path);
					deleted++;
					lock (SyncRoot) knownFiles.Remove(seg.Path);
				}
				catch (Exception ex)
				{
					log?.Invoke("Unable to delete old data file " + Path.GetFileName(seg.Path) + ": " + ex.Message);
				}
			}
			lock (SyncRoot)
				foreach (MonitoringSession s in list)
					PushHistory(s);
			return deleted;
		}

		/// <summary>Gives a session its data start and the gaps between runs.  Call with <see cref="SyncRoot"/> held.</summary>
		private void PushHistory(MonitoringSession s)
		{
			long tolerance = Math.Max(MinGapMs, 2L * s.IntervalMs);
			List<(long start, long end)> merged = MergeIntervals(runs, tolerance);
			List<(long, long)> gaps = new List<(long, long)>();
			for (int i = 1; i < merged.Count; i++)
				gaps.Add((merged[i - 1].end, merged[i].start));
			if (currentRunStart >= 0 && merged.Count > 0 && currentRunStart - merged[merged.Count - 1].end > tolerance)
				gaps.Add((merged[merged.Count - 1].end, currentRunStart));
			long dataStart = merged.Count > 0 ? merged[0].start : currentRunStart >= 0 ? currentRunStart : s.StartUnixMs;
			if (cutoffUnixMs > dataStart) dataStart = cutoffUnixMs;
			s.SetHistory(Math.Max(dataStart, s.StartUnixMs), gaps);
		}

		#endregion

		#region Interval math

		/// <summary>Sorts intervals and merges those that overlap or are separated by at most <paramref name="tolerance"/>.</summary>
		public static List<(long start, long end)> MergeIntervals(IEnumerable<(long start, long end)> intervals, long tolerance)
		{
			List<(long start, long end)> sorted = intervals.Select(i => (i.start, Math.Max(i.start, i.end))).OrderBy(i => i.start).ToList();
			List<(long start, long end)> merged = new List<(long, long)>();
			foreach ((long s, long e) in sorted)
			{
				if (merged.Count > 0 && s <= merged[merged.Count - 1].end + tolerance)
				{
					(long ps, long pe) = merged[merged.Count - 1];
					merged[merged.Count - 1] = (ps, Math.Max(pe, e));
				}
				else
					merged.Add((s, e));
			}
			return merged;
		}

		/// <summary>
		/// Returns the time after which the newest <paramref name="retentionMs"/> of monitoring time lies, given merged, ascending
		/// monitoring periods.  Returns <see cref="long.MinValue"/> if all of them fit.
		/// </summary>
		public static long ComputeCutoff(List<(long start, long end)> merged, long retentionMs)
		{
			if (retentionMs <= 0)
				return long.MinValue;
			long remaining = retentionMs;
			for (int i = merged.Count - 1; i >= 0; i--)
			{
				long len = merged[i].end - merged[i].start;
				if (len >= remaining)
					return merged[i].end - remaining;
				remaining -= len;
			}
			return long.MinValue;
		}

		private static List<(long start, long end)> ClipIntervals(List<(long start, long end)> merged, long cutoff)
		{
			List<(long, long)> list = new List<(long, long)>();
			foreach ((long s, long e) in merged)
			{
				if (e < cutoff) continue;
				list.Add((Math.Max(s, cutoff), e));
			}
			return list;
		}

		#endregion

		#region Import

		/// <summary>
		/// Copies the data of a saved file (for example a version 3 file from an earlier PingTracer 3 build) into a configuration's
		/// data directory as segment files, so that it becomes part of the configuration's history.  The source file is not changed.
		/// </summary>
		/// <param name="sourcePath">The file to import.</param>
		/// <param name="dataRoot">The directory holding all configurations' data directories.</param>
		/// <param name="configGuid">The configuration to add the data to.</param>
		/// <param name="hostKey">The configuration host entry the data belongs to (see <see cref="MonitoringSession.NormalizeHostKey"/>).</param>
		/// <returns>The paths of the segment files written.</returns>
		public static List<string> ImportFile(string sourcePath, string dataRoot, string configGuid, string hostKey)
		{
			return ImportSession(PingDataReader.Load(sourcePath).Session, dataRoot, configGuid, hostKey);
		}

		/// <summary>Writes all of a session's data into a configuration's data directory as segment files.</summary>
		/// <returns>The paths of the segment files written.</returns>
		public static List<string> ImportSession(MonitoringSession s, string dataRoot, string configGuid, string hostKey)
		{
			string dir = new ConfigDataSet(dataRoot, configGuid).Directory;
			List<string> written = new List<string>();
			using RunLock l = TryAcquireRunLock(dir) ?? throw new IOException("The configuration is running in another PingTracer instance.");

			MonitoringSession dst = new MonitoringSession(s.TargetAddress, s.DisplayName, s.Mode, s.IntervalMs, s.TimeoutMs, s.StartUnixMs, hostKey: MonitoringSession.NormalizeHostKey(hostKey));
			lock (s.DataLock)
			{
				foreach ((ushort id, System.Net.IPAddress ip, string host) in s.Addresses.All())
				{
					ushort nid = dst.LoadAddress(ip);
					if (!string.IsNullOrEmpty(host))
						dst.LoadHostname(nid, host);
				}
			}
			PingDataWriter w = new PingDataWriter(dst, dir, configGuid, 3600, PingDataCompression.Brotli, s.StartUnixMs);
			w.SegmentClosed += p => { if (p != null) written.Add(p); };
			try
			{
				// Records keep their session-relative times: both sessions have the same origin.
				foreach (RouteEvent e in s.GetRouteEvents())
					dst.ImportRouteEvent(e);
				foreach (HopSeries hop in s.GetHops())
				{
					List<PingSample> records = new List<PingSample>();
					long cursor = hop.FirstIndex;
					lock (s.DataLock)
						hop.CopyStableRecords(ref cursor, long.MaxValue, records);
					dst.LoadRecords(hop.Ttl, records);
				}
			}
			finally
			{
				w.Dispose();
			}
			return written;
		}

		#endregion

		#region Locking and deletion

		/// <summary>
		/// Exclusive ownership of a configuration's data while it runs, held as a lock file opened without sharing.  This works
		/// across processes on every platform .NET supports (on Unix, .NET takes an advisory flock for FileShare.None), and the
		/// operating system releases it if PingTracer exits or crashes.
		/// </summary>
		public sealed class RunLock : IDisposable
		{
			private FileStream stream;
			internal RunLock(FileStream s) { stream = s; }
			public void Dispose()
			{
				try { stream?.Dispose(); } catch { }
				stream = null;
			}
		}

		/// <summary>Takes the run lock, or returns null if another PingTracer instance holds it.</summary>
		public RunLock TryAcquireRunLock() => TryAcquireRunLock(Directory);

		public static RunLock TryAcquireRunLock(string directory)
		{
			System.IO.Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, LockFileName);
			try
			{
				return new RunLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}
		}

		/// <summary>
		/// Deletes a configuration's data directory.  Returns false (and deletes nothing) if the configuration is running in
		/// another PingTracer instance.
		/// </summary>
		public static bool DeleteData(string dataRoot, string configGuid)
		{
			string dir = new ConfigDataSet(dataRoot, configGuid).Directory;
			if (!System.IO.Directory.Exists(dir))
				return true;
			using (RunLock l = TryAcquireRunLock(dir))
			{
				if (l == null)
					return false;
				foreach (string f in System.IO.Directory.GetFiles(dir, "*" + PingDataFile.FileExtension))
					File.Delete(f);
			}
			try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
			return true;
		}

		#endregion
	}
}
