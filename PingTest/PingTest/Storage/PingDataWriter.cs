using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;

namespace PingTracer.Storage
{
	/// <summary>
	/// Saves the data a live <see cref="MonitoringSession"/> receives during one run to segment files (see
	/// <see cref="PingDataFile"/>) in a configuration's data directory, flushing periodically.  A new segment is started at
	/// each local midnight.  Data the session already held when the writer was created (from earlier runs) is not written
	/// again.
	/// <para>
	/// Only "stable" records are written: completed records older than the ping timeout plus a margin.  This guarantees that a
	/// record is never written and then later changed (completed or voided), which keeps the files append-only.
	/// </para>
	/// <para>Lock ordering: <see cref="MonitoringSession.DataLock"/> before the queue lock; the file lock before DataLock.</para>
	/// </summary>
	public sealed class PingDataWriter : IDisposable
	{
		private readonly MonitoringSession session;
		private readonly string directory;
		private readonly string configGuid;
		private readonly PingDataCompression compression;
		private readonly Timer flushTimer;
		private readonly object queueLock = new object();
		private readonly object fileLock = new object();
		private readonly long[] cursors = new long[256];
		private readonly List<(ushort id, IPAddress ip)> pendingAddresses = new List<(ushort, IPAddress)>();
		private readonly List<(ushort id, string host)> pendingHostnames = new List<(ushort, string)>();
		private readonly List<RouteEvent> pendingEvents = new List<RouteEvent>();
		private readonly MemoryStream body = new MemoryStream();
		private readonly MemoryStream block = new MemoryStream();
		private bool disposed;

		// Current segment (guarded by fileLock).
		private FileStream fs;
		private Stream output;
		private long segmentStartUnixMs;
		private long segmentEndUnixMs;
		private long nextMidnightUnixMs;
		private long itemsInSegment;
		private byte[] endTimeBytes = new byte[8];

		/// <summary>Path of the segment being written.</summary>
		public string FilePath { get; private set; }

		/// <summary>Raised if writing fails. The writer stops writing afterward.</summary>
		public event Action<Exception> WriteFailed;
		/// <summary>Raised (with the file lock held) after a segment file is closed. Carries its path, or null if it was empty and deleted.</summary>
		public event Action<string> SegmentClosed;

		/// <param name="session">The session to save.</param>
		/// <param name="directory">The configuration's data directory.</param>
		/// <param name="configGuid">The configuration's id, recorded in each file.</param>
		/// <param name="flushIntervalSeconds">How often data is written (longer intervals compress better).</param>
		/// <param name="compression">Compression for the files.</param>
		/// <param name="firstSegmentStartUnixMs">Start time of the first segment (default: now).  Used when importing older data.</param>
		public PingDataWriter(MonitoringSession session, string directory, string configGuid, int flushIntervalSeconds, PingDataCompression compression = PingDataCompression.Brotli, long firstSegmentStartUnixMs = 0)
		{
			this.session = session ?? throw new ArgumentNullException(nameof(session));
			this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
			this.configGuid = configGuid ?? "";
			this.compression = compression;
			Directory.CreateDirectory(directory);

			lock (session.DataLock)
			{
				// Resume after whatever the session already holds.
				foreach (HopSeries hop in session.GetHops())
					cursors[hop.Ttl] = hop.EndIndex;
				session.AddressAdded += OnAddressAdded;
				session.HostnameResolved += OnHostnameResolved;
				session.RouteEventAdded += OnRouteEvent;
			}
			lock (fileLock)
			{
				try
				{
					OpenSegment(firstSegmentStartUnixMs > 0 ? firstSegmentStartUnixMs : ServerClock.NowUnixMs);
				}
				catch
				{
					Unsubscribe();
					throw;
				}
			}

			int ms = Math.Max(1, flushIntervalSeconds) * 1000;
			flushTimer = new Timer(_ => SafeFlush(), null, ms, ms);
		}

		private void OnAddressAdded(MonitoringSession s, ushort id, IPAddress ip)
		{
			lock (queueLock) pendingAddresses.Add((id, ip));
		}

		private void OnHostnameResolved(MonitoringSession s, ushort id, string host)
		{
			lock (queueLock) pendingHostnames.Add((id, host));
		}

		private void OnRouteEvent(MonitoringSession s, RouteEvent e)
		{
			lock (queueLock) pendingEvents.Add(e);
		}

		private void Unsubscribe()
		{
			lock (session.DataLock)
			{
				session.AddressAdded -= OnAddressAdded;
				session.HostnameResolved -= OnHostnameResolved;
				session.RouteEventAdded -= OnRouteEvent;
			}
		}

		private void SafeFlush()
		{
			try
			{
				Flush(final: false);
			}
			catch (Exception ex)
			{
				Fail(ex);
			}
		}

		private void Fail(Exception ex)
		{
			lock (fileLock)
			{
				if (disposed) return;
				disposed = true;
				try { output?.Dispose(); } catch { }
				try { fs?.Dispose(); } catch { }
			}
			try { flushTimer?.Dispose(); } catch { }
			WriteFailed?.Invoke(ex);
		}

		#region Segments

		/// <summary>Starts a new segment file.  Call with the file lock held.</summary>
		private void OpenSegment(long startUnixMs)
		{
			string name = PingDataFile.BuildSegmentFileName(session.HostKey, session.Mode, startUnixMs);
			string path = Path.Combine(directory, name);
			for (int i = 2; File.Exists(path); i++)
				path = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + "-" + i + PingDataFile.FileExtension);

			List<(ushort id, IPAddress ip, string host)> addresses;
			PingDataFile.Header h;
			int destTtl;
			lock (session.DataLock)
			{
				addresses = new List<(ushort, IPAddress, string)>(session.Addresses.All());
				destTtl = session.DestinationTtl;
				h = new PingDataFile.Header
				{
					Compression = compression,
					StartUnixMs = startUnixMs,
					EndUnixMs = 0,
					Mode = session.Mode,
					IntervalMs = session.IntervalMs,
					TimeoutMs = session.TimeoutMs,
					Target = session.TargetAddress,
					DisplayName = session.DisplayName,
					ConfigGuid = configGuid,
					HostKey = session.HostKey,
				};
			}

			FileStream f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 65536);
			try
			{
				PingDataFile.WriteHeader(f, h);
				output = compression == PingDataCompression.Brotli
					? new BrotliStream(f, new BrotliCompressionOptions { Quality = PingDataFile.BrotliQuality }, leaveOpen: true)
					: f;
			}
			catch
			{
				f.Dispose();
				try { File.Delete(path); } catch { }
				throw;
			}
			fs = f;
			FilePath = path;
			segmentStartUnixMs = startUnixMs;
			segmentEndUnixMs = 0;
			itemsInSegment = 0;
			nextMidnightUnixMs = PingDataFile.NextLocalMidnightUnixMs(startUnixMs);

			// Each segment stands on its own: it starts with the whole address table and the current destination TTL.
			BinaryWriter b = new BinaryWriter(body, Encoding.UTF8);
			foreach ((ushort id, IPAddress ip, string host) in addresses)
			{
				Reset(body);
				b.Write(id);
				PingDataFile.WriteIp(b, ip);
				b.Flush();
				WriteBlock(PingDataFile.BlockAddress, body);
				if (!string.IsNullOrEmpty(host))
				{
					Reset(body);
					b.Write(id);
					PingDataFile.WriteString(b, host);
					b.Flush();
					WriteBlock(PingDataFile.BlockHostname, body);
				}
			}
			if (destTtl > 0)
			{
				Reset(body);
				body.WriteByte((byte)destTtl);
				WriteBlock(PingDataFile.BlockState, body);
			}
			CommitToDisk();
		}

		/// <summary>Finishes the current segment.  Deletes it if it holds no records or events.  Call with the file lock held.</summary>
		private void CloseSegment()
		{
			if (fs == null)
				return;
			string path = FilePath;
			bool empty = itemsInSegment == 0;
			try
			{
				if (!ReferenceEquals(output, fs))
					output.Dispose(); // finishes the compressed stream
				WriteEndTime();
				fs.Flush(flushToDisk: true);
			}
			finally
			{
				fs.Dispose();
				fs = null;
				output = null;
			}
			if (empty)
			{
				try { File.Delete(path); } catch { }
				path = null;
			}
			SegmentClosed?.Invoke(path);
		}

		private void WriteEndTime()
		{
			if (segmentEndUnixMs <= 0)
				return;
			long pos = fs.Position;
			BitConverter.TryWriteBytes(endTimeBytes, segmentEndUnixMs);
			fs.Position = PingDataFile.EndTimeOffset;
			fs.Write(endTimeBytes, 0, 8);
			fs.Position = pos;
		}

		/// <summary>Pushes everything written so far to the file, so readers can decode it.</summary>
		private void CommitToDisk()
		{
			output.Flush();
			WriteEndTime();
			fs.Flush();
		}

		#endregion

		#region Writing

		/// <summary>
		/// Writes everything that is ready.  If <paramref name="final"/> is true, all completed records are written regardless of age
		/// (call only after the monitor has stopped and in-flight pings have completed or been voided).
		/// </summary>
		public void Flush(bool final = false)
		{
			List<(ushort, IPAddress)> addrs;
			List<(ushort, string)> hosts;
			List<RouteEvent> events;
			List<(int ttl, List<PingSample> records)> recordBatches = new List<(int, List<PingSample>)>();

			lock (session.DataLock)
			{
				long stableBefore = final ? long.MaxValue : session.NowMs - (session.TimeoutMs + 2000);
				foreach (HopSeries hop in session.GetHops())
				{
					List<PingSample> list = new List<PingSample>();
					hop.CopyStableRecords(ref cursors[hop.Ttl], stableBefore, list);
					if (list.Count > 0)
						recordBatches.Add((hop.Ttl, list));
				}
				lock (queueLock)
				{
					addrs = new List<(ushort, IPAddress)>(pendingAddresses);
					hosts = new List<(ushort, string)>(pendingHostnames);
					events = new List<RouteEvent>(pendingEvents);
					pendingAddresses.Clear();
					pendingHostnames.Clear();
					pendingEvents.Clear();
				}
			}

			lock (fileLock)
			{
				if (disposed || fs == null)
					return;
				long origin = session.StartUnixMs;
				// Addresses and hostnames first: records and events refer to them.
				WriteAddresses(addrs, hosts);

				// Split the data at local midnight(s).  Times are stable from here on, so each part can be written in order.
				while (true)
				{
					long boundary = nextMidnightUnixMs - origin; // session-relative
					bool anyAfter = false;
					long earliestAfter = long.MaxValue;
					foreach (RouteEvent e in events)
						if (e.TimeMs >= boundary) { anyAfter = true; earliestAfter = Math.Min(earliestAfter, e.TimeMs); }
					foreach ((int ttl, List<PingSample> records) in recordBatches)
					{
						int idx = LowerBound(records, boundary);
						if (idx < records.Count) { anyAfter = true; earliestAfter = Math.Min(earliestAfter, records[idx].TimeMs); }
					}
					WriteEvents(events, long.MinValue, boundary);
					foreach ((int ttl, List<PingSample> records) in recordBatches)
						WriteRecords(ttl, records, 0, LowerBound(records, boundary));
					if (!anyAfter)
						break;

					// The day ended: the rest goes into a segment that starts at the beginning of its own day.
					long nextStart = Math.Max(nextMidnightUnixMs, LocalDayStartUnixMs(origin + earliestAfter));
					CloseSegment();
					OpenSegment(nextStart);
					events.RemoveAll(e => e.TimeMs < boundary);
					for (int i = 0; i < recordBatches.Count; i++)
					{
						List<PingSample> r = recordBatches[i].records;
						r.RemoveRange(0, LowerBound(r, boundary));
					}
				}
				CommitToDisk();
			}
		}

		private static long LocalDayStartUnixMs(long unixMs)
		{
			DateTime local = ServerClock.ToDateTime(unixMs).ToLocalTime().Date;
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds();
		}

		/// <summary>Index of the first record at or after <paramref name="timeMs"/> (records are in time order).</summary>
		private static int LowerBound(List<PingSample> records, long timeMs)
		{
			int lo = 0, hi = records.Count;
			while (lo < hi)
			{
				int mid = (lo + hi) >> 1;
				if (records[mid].TimeMs < timeMs) lo = mid + 1; else hi = mid;
			}
			return lo;
		}

		private void NoteTime(long sessionMs)
		{
			long unix = session.StartUnixMs + sessionMs;
			if (unix > segmentEndUnixMs) segmentEndUnixMs = unix;
			itemsInSegment++;
		}

		private void WriteAddresses(List<(ushort id, IPAddress ip)> addrs, List<(ushort id, string host)> hosts)
		{
			BinaryWriter b = new BinaryWriter(body, Encoding.UTF8);
			foreach ((ushort id, IPAddress ip) in addrs)
			{
				Reset(body);
				b.Write(id);
				PingDataFile.WriteIp(b, ip);
				b.Flush();
				WriteBlock(PingDataFile.BlockAddress, body);
			}
			foreach ((ushort id, string host) in hosts)
			{
				Reset(body);
				b.Write(id);
				PingDataFile.WriteString(b, host);
				b.Flush();
				WriteBlock(PingDataFile.BlockHostname, body);
			}
		}

		private void WriteEvents(List<RouteEvent> events, long fromMs, long toMs)
		{
			BinaryWriter b = new BinaryWriter(body, Encoding.UTF8);
			foreach (RouteEvent e in events)
			{
				if (e.TimeMs < fromMs || e.TimeMs >= toMs)
					continue;
				Reset(body);
				b.Write(session.StartUnixMs + e.TimeMs);
				b.Write((byte)e.Kind);
				b.Write(e.Ttl);
				b.Write(e.Value);
				b.Write((byte)(e.Initial ? 1 : 0));
				b.Flush();
				WriteBlock(PingDataFile.BlockRouteEvent, body);
				NoteTime(e.TimeMs);
			}
		}

		private void WriteRecords(int ttl, List<PingSample> records, int from, int to)
		{
			int i = from;
			Span<byte> b8 = stackalloc byte[8];
			while (i < to)
			{
				int count = Math.Min(to - i, PingDataFile.MaxRecordsPerBlock);
				long baseTime = records[i].TimeMs;
				Reset(body);
				body.WriteByte((byte)ttl);
				BitConverter.TryWriteBytes(b8, session.StartUnixMs + baseTime);
				body.Write(b8);
				PingDataFile.WriteVarUInt(body, (ulong)count);
				long prevT = baseTime, prevD = 0;
				for (int k = 0; k < count; k++)
				{
					long t = records[i + k].TimeMs;
					long d = t - prevT;
					PingDataFile.WriteVarUInt(body, PingDataFile.ZigZag(d - prevD));
					prevD = d;
					prevT = t;
				}
				long prevC = 0;
				for (int k = 0; k < count; k++)
				{
					PingSample r = records[i + k];
					long c = r.IsLost ? 0 : r.Rtt + 1;
					PingDataFile.WriteVarUInt(body, PingDataFile.ZigZag(c - prevC));
					prevC = c;
				}
				for (int k = 0; k < count; k++)
					PingDataFile.WriteVarUInt(body, (ushort)(records[i + k].Addr + 1));
				WriteBlock(PingDataFile.BlockRecords, body);
				NoteTime(records[i + count - 1].TimeMs);
				i += count;
			}
		}

		private static void Reset(MemoryStream ms)
		{
			ms.Position = 0;
			ms.SetLength(0);
		}

		private void WriteBlock(byte type, MemoryStream content)
		{
			Reset(block);
			block.WriteByte(type);
			PingDataFile.WriteVarUInt(block, (ulong)content.Length);
			block.Write(content.GetBuffer(), 0, (int)content.Length);
			output.Write(block.GetBuffer(), 0, (int)block.Length);
		}

		#endregion

		/// <summary>Writes all remaining data and closes the file.  Call after the monitor has stopped.</summary>
		public void Dispose()
		{
			Unsubscribe();
			try { flushTimer.Dispose(); } catch { }
			lock (fileLock)
			{
				if (disposed)
					return;
			}
			try { Flush(final: true); } catch { }
			lock (fileLock)
			{
				if (disposed)
					return;
				disposed = true;
				try { CloseSegment(); } catch { }
			}
		}
	}
}
