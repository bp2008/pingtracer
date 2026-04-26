using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace PingTracer.Storage
{
	/// <summary>
	/// Serializes a live <see cref="MonitoringSession"/> to a sequential append-only
	/// .ptrc file. Periodically flushes accumulated data; queues reverse-DNS results
	/// for batched emission. See plan.md (Phase 5) for the file format spec.
	///
	/// Block-ordering invariant: within a single flush we walk each <see cref="HopHistory"/>
	/// in order so that a series' <c>HopTimeSeriesStartBlock</c> is always followed
	/// (before any later series' start block on the same hop) by all of its
	/// <c>PingDataBlock</c>s. This is required so the reader can attribute data
	/// blocks to the right series via the "most recent start block per hop" rule.
	///
	/// Stop semantics: <see cref="Dispose"/> waits (up to a cap) for any in-flight
	/// pending records to complete, performs a final flush, and closes the file.
	/// </summary>
	public sealed class PingDataWriter : IDisposable
	{
		private const int FinalFlushPendingPollMs = 100;
		private const int FinalFlushPendingMaxWaitMs = 10000;

		private readonly MonitoringSession _session;
		private readonly string _filePath;
		private readonly int _flushIntervalMs;

		private FileStream _fs;
		private readonly object _writeLock = new object();
		private Timer _flushTimer;
		private bool _disposed;

		private readonly Dictionary<HopTimeSeries, RecordHandle?> _flushCursors =
			new Dictionary<HopTimeSeries, RecordHandle?>();
		private readonly HashSet<HopTimeSeries> _startBlockWritten = new HashSet<HopTimeSeries>();
		private readonly HashSet<HopTimeSeries> _hostnameSubscribed = new HashSet<HopTimeSeries>();
		private readonly Queue<(IPAddress addr, string hostname)> _pendingDns =
			new Queue<(IPAddress, string)>();

		private readonly Action<HopTimeSeries> _onHostnameUpdated;

		public string FilePath => _filePath;

		public PingDataWriter(MonitoringSession session, string filePath, int flushIntervalSeconds)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			if (flushIntervalSeconds < 1) flushIntervalSeconds = 1;
			_flushIntervalMs = flushIntervalSeconds * 1000;

			Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
			_fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read,
				bufferSize: 8192, useAsync: false);

			WriteFileHeader();
			_fs.Flush();

			_onHostnameUpdated = HandleHostnameUpdated;

			_flushTimer = new Timer(_ => SafeFlush(), null, _flushIntervalMs, _flushIntervalMs);
		}

		// ----- Header -----

		private void WriteFileHeader()
		{
			_fs.Write(PingDataFile.Magic, 0, PingDataFile.Magic.Length);
			WriteUInt16(_fs, PingDataFile.CurrentVersion);
			WriteIp(_fs, _session.TargetAddress);
			WriteUInt64(_fs, PingDataFile.ToUnixMs(_session.StartTimeUtc));
		}

		// ----- Event handlers -----

		private void HandleHostnameUpdated(HopTimeSeries series)
		{
			if (series == null || string.IsNullOrEmpty(series.Hostname)) return;
			lock (_writeLock)
			{
				if (_disposed) return;
				_pendingDns.Enqueue((series.Address, series.Hostname));
			}
		}

		private void SubscribeHostnameIfNeeded(HopTimeSeries series)
		{
			if (_hostnameSubscribed.Add(series))
			{
				series.HostnameUpdated += _onHostnameUpdated;
				if (!string.IsNullOrEmpty(series.Hostname))
					_pendingDns.Enqueue((series.Address, series.Hostname));
			}
		}

		// ----- Flush -----

		private void SafeFlush()
		{
			try { Flush(); }
			catch { /* don't tear down the writer on transient I/O errors */ }
		}

		/// <summary>
		/// Drains queued DNS results, then walks each hop's series list in order,
		/// emitting any not-yet-written start blocks followed by ping data blocks.
		/// Stops at the first pending record per series.
		/// </summary>
		public void Flush()
		{
			lock (_writeLock)
			{
				if (_disposed || _fs == null) return;

				while (_pendingDns.Count > 0)
				{
					var (addr, host) = _pendingDns.Dequeue();
					WriteReverseDnsBlock(addr, host);
				}

				foreach (HopHistory hist in _session.HopData)
				{
					List<HopTimeSeries> seriesSnapshot;
					lock (hist) seriesSnapshot = new List<HopTimeSeries>(hist.Series);

					foreach (HopTimeSeries series in seriesSnapshot)
					{
						SubscribeHostnameIfNeeded(series);

						if (_startBlockWritten.Add(series))
						{
							WriteHopTimeSeriesStartBlock(series);
							_flushCursors[series] = null;
						}

						RecordHandle? cursor = _flushCursors.TryGetValue(series, out var c) ? c : null;
						List<(RecordHandle handle, PingRecord record)> snapshot = series.SnapshotRecordsAfter(cursor);
						if (snapshot.Count == 0) continue;

						int writableCount = snapshot.Count;
						for (int i = 0; i < snapshot.Count; i++)
						{
							if (snapshot[i].record.RoundTripMs == PingRecordStatus.Pending)
							{
								writableCount = i;
								break;
							}
						}
						if (writableCount == 0) continue;

						int idx = 0;
						RecordHandle lastWritten = default;
						while (idx < writableCount)
						{
							uint blockBaseRel = snapshot[idx].record.RelativeTimeMs;
							int blockEnd = idx;
							while (blockEnd < writableCount
								&& (blockEnd - idx) < PingDataFile.MaxRecordsPerBlock
								&& (long)snapshot[blockEnd].record.RelativeTimeMs - blockBaseRel <= PingDataFile.MaxBlockTimeSpanMs)
							{
								blockEnd++;
							}
							if (blockEnd == idx) blockEnd = idx + 1;

							WritePingDataBlock(series.HopNumber, blockBaseRel, snapshot, idx, blockEnd);
							lastWritten = snapshot[blockEnd - 1].handle;
							idx = blockEnd;
						}

						_flushCursors[series] = lastWritten;
					}
				}

				_fs.Flush();
			}
		}

		// ----- Block writers -----

		private void WriteHopTimeSeriesStartBlock(HopTimeSeries series)
		{
			byte[] addrBytes = (series.Address == null || series.Address.Equals(IPAddress.Any) || series.Address.Equals(IPAddress.IPv6Any))
				? Array.Empty<byte>()
				: series.Address.GetAddressBytes();
			if (addrBytes.Length != 0 && addrBytes.Length != 4 && addrBytes.Length != 16)
				addrBytes = Array.Empty<byte>();

			uint bodyLen = (uint)(1 + 8 + 1 + addrBytes.Length);
			WriteBlockHeader(PingDataFile.BlockType_HopTimeSeriesStart, bodyLen);
			_fs.WriteByte(series.HopNumber);
			WriteUInt64(_fs, PingDataFile.ToUnixMs(series.StartTimeUtc));
			_fs.WriteByte((byte)addrBytes.Length);
			if (addrBytes.Length > 0) _fs.Write(addrBytes, 0, addrBytes.Length);
		}

		private void WriteReverseDnsBlock(IPAddress addr, string hostname)
		{
			byte[] addrBytes = (addr == null) ? Array.Empty<byte>() : addr.GetAddressBytes();
			if (addrBytes.Length != 4 && addrBytes.Length != 16) return;

			byte[] hostBytes = string.IsNullOrEmpty(hostname)
				? Array.Empty<byte>()
				: Encoding.UTF8.GetBytes(hostname);
			if (hostBytes.Length > ushort.MaxValue)
			{
				byte[] tmp = new byte[ushort.MaxValue];
				Buffer.BlockCopy(hostBytes, 0, tmp, 0, ushort.MaxValue);
				hostBytes = tmp;
			}

			uint bodyLen = (uint)(1 + addrBytes.Length + 2 + hostBytes.Length);
			WriteBlockHeader(PingDataFile.BlockType_ReverseDnsResult, bodyLen);
			_fs.WriteByte((byte)addrBytes.Length);
			_fs.Write(addrBytes, 0, addrBytes.Length);
			WriteUInt16(_fs, (ushort)hostBytes.Length);
			if (hostBytes.Length > 0) _fs.Write(hostBytes, 0, hostBytes.Length);
		}

		private void WritePingDataBlock(byte hopNumber, uint timeOffset,
			List<(RecordHandle handle, PingRecord record)> records, int startIdx, int endExclusive)
		{
			int count = endExclusive - startIdx;
			uint bodyLen = (uint)(1 + 4 + 2 + count * 4);
			WriteBlockHeader(PingDataFile.BlockType_PingData, bodyLen);
			_fs.WriteByte(hopNumber);
			WriteUInt32(_fs, timeOffset);
			WriteUInt16(_fs, (ushort)count);
			for (int i = startIdx; i < endExclusive; i++)
			{
				PingRecord r = records[i].record;
				ushort relInBlock = (ushort)(r.RelativeTimeMs - timeOffset);
				WriteUInt16(_fs, relInBlock);
				WriteUInt16(_fs, r.RoundTripMs);
			}
		}

		private void WriteBlockHeader(byte blockType, uint bodyLength)
		{
			_fs.WriteByte(blockType);
			WriteUInt32(_fs, bodyLength);
		}

		// ----- Low-level little-endian primitives -----

		private static void WriteUInt16(Stream s, ushort v)
		{
			s.WriteByte((byte)(v & 0xFF));
			s.WriteByte((byte)((v >> 8) & 0xFF));
		}
		private static void WriteUInt32(Stream s, uint v)
		{
			s.WriteByte((byte)(v & 0xFF));
			s.WriteByte((byte)((v >> 8) & 0xFF));
			s.WriteByte((byte)((v >> 16) & 0xFF));
			s.WriteByte((byte)((v >> 24) & 0xFF));
		}
		private static void WriteUInt64(Stream s, ulong v)
		{
			for (int i = 0; i < 8; i++)
				s.WriteByte((byte)((v >> (i * 8)) & 0xFF));
		}
		private static void WriteIp(Stream s, IPAddress addr)
		{
			if (addr == null || addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any))
			{
				s.WriteByte(0);
				return;
			}
			byte[] bytes = addr.GetAddressBytes();
			if (bytes.Length != 4 && bytes.Length != 16)
			{
				s.WriteByte(0);
				return;
			}
			s.WriteByte((byte)bytes.Length);
			s.Write(bytes, 0, bytes.Length);
		}

		// ----- Dispose -----

		public void Dispose()
		{
			Timer t = Interlocked.Exchange(ref _flushTimer, null);
			t?.Dispose();

			DateTime deadline = DateTime.UtcNow.AddMilliseconds(FinalFlushPendingMaxWaitMs);
			while (DateTime.UtcNow < deadline && AnyPending()) Thread.Sleep(FinalFlushPendingPollMs);

			lock (_writeLock)
			{
				if (_disposed) return;

				try { Flush(); } catch { }

				foreach (HopTimeSeries s in _hostnameSubscribed)
				{
					try { s.HostnameUpdated -= _onHostnameUpdated; } catch { }
				}
				_hostnameSubscribed.Clear();

				try { _fs?.Flush(); } catch { }
				try { _fs?.Dispose(); } catch { }
				_fs = null;
				_disposed = true;
			}
		}

		private bool AnyPending()
		{
			foreach (HopHistory hist in _session.HopData)
			{
				List<HopTimeSeries> snap;
				lock (hist) snap = new List<HopTimeSeries>(hist.Series);
				foreach (HopTimeSeries s in snap)
				{
					RecordHandle? cursor;
					lock (_writeLock) cursor = _flushCursors.TryGetValue(s, out var c) ? c : null;
					List<(RecordHandle h, PingRecord r)> tail = s.SnapshotRecordsAfter(cursor);
					foreach (var pair in tail)
					{
						if (pair.r.RoundTripMs == PingRecordStatus.Pending) return true;
					}
				}
			}
			return false;
		}
	}
}
