using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace PingTracer.Storage
{
	/// <summary>
	/// Appends a live <see cref="MonitoringSession"/> to a .ptrc file (see <see cref="PingDataFile"/>), flushing periodically.
	/// <para>
	/// Only "stable" records are written: completed records older than the ping timeout plus a margin.  This guarantees that a
	/// record is never written and then later changed (completed or voided), which keeps the file strictly append-only.
	/// </para>
	/// <para>Lock ordering: <see cref="MonitoringSession.DataLock"/> is always taken before this writer's queue lock.</para>
	/// </summary>
	public sealed class PingDataWriter : IDisposable
	{
		private readonly MonitoringSession session;
		private readonly FileStream fs;
		private readonly BinaryWriter bw;
		private readonly Timer flushTimer;
		private readonly object queueLock = new object();
		private readonly object fileLock = new object();
		private readonly long[] cursors = new long[256];
		private readonly List<(ushort id, IPAddress ip)> pendingAddresses = new List<(ushort, IPAddress)>();
		private readonly List<(ushort id, string host)> pendingHostnames = new List<(ushort, string)>();
		private readonly List<RouteEvent> pendingEvents = new List<RouteEvent>();
		private bool disposed;

		public string FilePath { get; }

		/// <summary>Raised if writing fails. The writer stops writing afterward.</summary>
		public event Action<Exception> WriteFailed;

		public PingDataWriter(MonitoringSession session, string filePath, int flushIntervalSeconds)
		{
			this.session = session ?? throw new ArgumentNullException(nameof(session));
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
			fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536);
			bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);
			PingDataFile.WriteHeader(fs, session);
			fs.Flush();

			lock (session.DataLock)
			{
				foreach ((ushort id, IPAddress ip, string host) in session.Addresses.All())
				{
					pendingAddresses.Add((id, ip));
					if (!string.IsNullOrEmpty(host))
						pendingHostnames.Add((id, host));
				}
				pendingEvents.AddRange(session.GetRouteEvents());
				session.AddressAdded += OnAddressAdded;
				session.HostnameResolved += OnHostnameResolved;
				session.RouteEventAdded += OnRouteEvent;
			}

			int ms = Math.Max(1, flushIntervalSeconds) * 1000;
			flushTimer = new Timer(_ => SafeFlush(final: false), null, ms, ms);
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

		private void SafeFlush(bool final)
		{
			try
			{
				Flush(final);
			}
			catch (Exception ex)
			{
				lock (fileLock) disposed = true;
				try { flushTimer.Dispose(); } catch { }
				WriteFailed?.Invoke(ex);
			}
		}

		/// <summary>
		/// Writes everything that is ready.  If <paramref name="final"/> is true, all completed records are written regardless of age
		/// (call only after the monitor has stopped and in-flight pings have completed).
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
				if (disposed && !final)
					return;
				MemoryStream body = new MemoryStream();
				BinaryWriter b = new BinaryWriter(body, Encoding.UTF8);
				foreach ((ushort id, IPAddress ip) in addrs)
				{
					Reset(body);
					b.Write(id);
					PingDataFile.WriteIp(b, ip);
					WriteBlock(PingDataFile.BlockAddress, body);
				}
				foreach ((ushort id, string host) in hosts)
				{
					Reset(body);
					b.Write(id);
					PingDataFile.WriteString(b, host);
					WriteBlock(PingDataFile.BlockHostname, body);
				}
				foreach (RouteEvent e in events)
				{
					Reset(body);
					b.Write(e.TimeMs);
					b.Write((byte)e.Kind);
					b.Write(e.Ttl);
					b.Write(e.Value);
					b.Write((byte)(e.Initial ? 1 : 0));
					WriteBlock(PingDataFile.BlockRouteEvent, body);
				}
				foreach ((int ttl, List<PingSample> records) in recordBatches)
				{
					int i = 0;
					while (i < records.Count)
					{
						long baseTime = records[i].TimeMs;
						int count = 0;
						while (i + count < records.Count && count < PingDataFile.MaxRecordsPerBlock && records[i + count].TimeMs - baseTime <= ushort.MaxValue)
							count++;
						Reset(body);
						b.Write((byte)ttl);
						b.Write(baseTime);
						b.Write((ushort)count);
						for (int k = 0; k < count; k++)
						{
							PingSample r = records[i + k];
							b.Write((ushort)(r.TimeMs - baseTime));
							b.Write(r.Rtt);
							b.Write(r.Addr);
						}
						WriteBlock(PingDataFile.BlockRecords, body);
						i += count;
					}
				}
				bw.Flush();
				fs.Flush();
			}
		}

		private static void Reset(MemoryStream ms)
		{
			ms.Position = 0;
			ms.SetLength(0);
		}

		private void WriteBlock(byte type, MemoryStream body)
		{
			bw.Write(type);
			bw.Write((uint)body.Length);
			bw.Write(body.GetBuffer(), 0, (int)body.Length);
		}

		/// <summary>Writes all remaining data and closes the file.  Call after the monitor has stopped.</summary>
		public void Dispose()
		{
			lock (session.DataLock)
			{
				session.AddressAdded -= OnAddressAdded;
				session.HostnameResolved -= OnHostnameResolved;
				session.RouteEventAdded -= OnRouteEvent;
			}
			try { flushTimer.Dispose(); } catch { }
			lock (fileLock)
			{
				if (disposed)
				{
					try { fs.Dispose(); } catch { }
					return;
				}
			}
			try { Flush(final: true); } catch { }
			lock (fileLock)
			{
				disposed = true;
				try { bw.Dispose(); } catch { }
				try { fs.Dispose(); } catch { }
			}
		}
	}
}
