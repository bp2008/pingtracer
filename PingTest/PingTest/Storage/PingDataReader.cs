using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace PingTracer.Storage
{
	/// <summary>
	/// User-side hook for handling a corrupt trailing block. Returning true allows the
	/// reader to truncate the file at <paramref name="lastGoodOffset"/> and continue;
	/// returning false aborts further reading and leaves the file untouched.
	/// </summary>
	public interface ICorruptionPrompt
	{
		bool ConfirmTruncate(string filePath, long lastGoodOffset);
	}

	/// <summary>
	/// Reads a .ptrc session log and reconstructs an in-memory <see cref="MonitoringSession"/>
	/// by replaying blocks in order. Tolerates a corrupt trailing block via
	/// <see cref="ICorruptionPrompt"/> (likely caused by an unclean shutdown).
	/// </summary>
	public static class PingDataReader
	{
		/// <summary>
		/// Loads <paramref name="filePath"/> into a fresh <see cref="MonitoringSession"/>.
		/// On success, <paramref name="resumeOffset"/> contains the byte offset where a
		/// follow-up writer could append new blocks (i.e. just after the last complete block).
		/// </summary>
		public static MonitoringSession LoadFile(string filePath, out long resumeOffset, ICorruptionPrompt prompt = null)
		{
			if (filePath == null) throw new ArgumentNullException(nameof(filePath));

			using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				return Read(fs, filePath, out resumeOffset, prompt);
			}
		}

		private static MonitoringSession Read(FileStream fs, string filePath, out long resumeOffset, ICorruptionPrompt prompt)
		{
			// --- Header ---
			byte[] magic = ReadExactly(fs, 4);
			if (magic == null || magic[0] != PingDataFile.Magic[0] || magic[1] != PingDataFile.Magic[1]
				|| magic[2] != PingDataFile.Magic[2] || magic[3] != PingDataFile.Magic[3])
				throw new InvalidDataException($"Not a .ptrc file (bad magic): {filePath}");

			ushort version = ReadUInt16(fs);
			if (version != PingDataFile.CurrentVersion)
				throw new InvalidDataException($"Unsupported .ptrc version {version} in {filePath}");

			IPAddress targetAddr = ReadIp(fs);
			ulong sessionStartMs = ReadUInt64(fs);
			DateTime sessionStartUtc = PingDataFile.FromUnixMs(sessionStartMs);

			MonitoringSession session = new MonitoringSession(
				targetAddr ?? IPAddress.None,
				targetAddr?.ToString() ?? string.Empty);

			// We can't override readonly StartTimeUtc, but for most purposes the file's
			// session start is preserved via the first HopTimeSeries' StartTimeUtc and
			// via the RouteHistory we rebuild below.
			_ = sessionStartUtc;

			// --- Reader-local state ---
			var currentSeriesPerHop = new Dictionary<byte, HopTimeSeries>();
			var dnsCache = new Dictionary<IPAddress, string>(new IPAddressComparer());
			var routeTimestamps = new SortedSet<long>();           // unix ms for routesnapshot anchors
			var routeFrames = new Dictionary<long, Dictionary<byte, HopTimeSeries>>();

			long lastGoodOffset = fs.Position;

			while (fs.Position < fs.Length)
			{
				long blockStart = fs.Position;
				long remaining = fs.Length - blockStart;
				if (remaining < 5)
				{
					if (HandleCorruption(fs, filePath, prompt, blockStart)) break;
					else break;
				}

				int btRaw = fs.ReadByte();
				if (btRaw < 0) break;
				byte blockType = (byte)btRaw;
				uint bodyLen = ReadUInt32(fs);

				if (fs.Length - fs.Position < bodyLen)
				{
					HandleCorruption(fs, filePath, prompt, blockStart);
					break;
				}

				switch (blockType)
				{
					case PingDataFile.BlockType_HopTimeSeriesStart:
					{
						byte hop = (byte)fs.ReadByte();
						ulong tsMs = ReadUInt64(fs);
						IPAddress addr = ReadIp(fs);
						DateTime tsUtc = PingDataFile.FromUnixMs(tsMs);

						// Close the previous series at this hop.
						if (currentSeriesPerHop.TryGetValue(hop, out HopTimeSeries prev) && prev.IsActive)
							prev.EndTimeUtc = tsUtc;

						if (addr == null) addr = IPAddress.None;
						HopTimeSeries series = new HopTimeSeries(hop, addr, tsUtc, startDnsLookup: false);
						if (dnsCache.TryGetValue(addr, out string cachedHost))
							series.SetHostname(cachedHost);

						HopHistory hist = session.HopData[hop];
						lock (hist) hist.Series.Add(series);
						currentSeriesPerHop[hop] = series;

						// Snapshot the topology at this timestamp for RouteHistory rebuild.
						long key = (long)tsMs;
						routeTimestamps.Add(key);
						routeFrames[key] = new Dictionary<byte, HopTimeSeries>(currentSeriesPerHop);
						break;
					}

					case PingDataFile.BlockType_ReverseDnsResult:
					{
						IPAddress addr = ReadIp(fs);
						ushort hostLen = ReadUInt16(fs);
						byte[] hostBytes = ReadExactly(fs, hostLen);
						string host = hostBytes == null ? string.Empty : Encoding.UTF8.GetString(hostBytes);
						if (addr != null && !string.IsNullOrEmpty(host))
						{
							dnsCache[addr] = host;
							// Apply to any existing series with matching addr and no hostname yet.
							foreach (HopHistory h in session.HopData)
							{
								lock (h)
								{
									foreach (HopTimeSeries s in h.Series)
									{
										if (s.Hostname == null && s.Address.Equals(addr))
											s.SetHostname(host);
									}
								}
							}
						}
						break;
					}

					case PingDataFile.BlockType_PingData:
					{
						byte hop = (byte)fs.ReadByte();
						uint timeOffset = ReadUInt32(fs);
						ushort recordCount = ReadUInt16(fs);
						long expectedRecBytes = (long)recordCount * 4;
						long bodyLeft = (long)bodyLen - (1 + 4 + 2);
						if (bodyLeft < expectedRecBytes)
						{
							// Malformed body length; skip the rest of this block.
							SkipBytes(fs, bodyLeft);
							break;
						}

						currentSeriesPerHop.TryGetValue(hop, out HopTimeSeries target);
						for (int i = 0; i < recordCount; i++)
						{
							ushort relInBlock = ReadUInt16(fs);
							ushort rtt = ReadUInt16(fs);
							if (target != null)
							{
								target.AppendRawRecord(new PingRecord
								{
									RelativeTimeMs = timeOffset + relInBlock,
									RoundTripMs = rtt
								});
							}
						}
						// Skip any trailing padding within the block (forward-compat).
						long consumedRecBytes = expectedRecBytes;
						long bodyTail = bodyLeft - consumedRecBytes;
						if (bodyTail > 0) SkipBytes(fs, bodyTail);
						break;
					}

					default:
						// Unknown block — skip body bytes for forward compatibility.
						SkipBytes(fs, bodyLen);
						break;
				}

				lastGoodOffset = fs.Position;
			}

			// --- Rebuild RouteHistory from collected anchors ---
			foreach (long key in routeTimestamps)
			{
				Dictionary<byte, HopTimeSeries> frame = routeFrames[key];
				byte maxHop = 0;
				foreach (byte k in frame.Keys) if (k > maxHop) maxHop = k;
				HopTimeSeries[] hops = new HopTimeSeries[maxHop + 1];
				foreach (var kv in frame) hops[kv.Key] = kv.Value;
				session.RouteHistory.Add(new RouteSnapshot(PingDataFile.FromUnixMs((ulong)key), hops));
			}
			if (session.RouteHistory.Count > 0)
				session.CurrentRoute = session.RouteHistory[session.RouteHistory.Count - 1];

			// For each currently-active series whose hop position has no later start block,
			// EndTimeUtc is left null only if the series was the last one recorded for that hop.
			// Per plan.md, set EndTimeUtc on the final series to its last record's timestamp.
			foreach (var kv in currentSeriesPerHop)
			{
				HopTimeSeries last = kv.Value;
				if (last.IsActive && last.RecordCount > 0)
				{
					DateTime lastSeen = last.StartTimeUtc;
					foreach (var pair in last.SnapshotRecordsAfter(null))
					{
						DateTime t = last.StartTimeUtc.AddMilliseconds(pair.record.RelativeTimeMs);
						if (t > lastSeen) lastSeen = t;
					}
					last.EndTimeUtc = lastSeen;
				}
			}

			resumeOffset = lastGoodOffset;
			return session;
		}

		private static bool HandleCorruption(FileStream fs, string filePath, ICorruptionPrompt prompt, long lastGoodOffset)
		{
			bool truncate = prompt != null && prompt.ConfirmTruncate(filePath, lastGoodOffset);
			if (truncate)
			{
				fs.Close();
				using (FileStream rw = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
				{
					rw.SetLength(lastGoodOffset);
					rw.Flush();
				}
			}
			return truncate;
		}

		// ----- Low-level readers -----

		private static byte[] ReadExactly(Stream s, int count)
		{
			if (count == 0) return Array.Empty<byte>();
			byte[] buf = new byte[count];
			int read = 0;
			while (read < count)
			{
				int n = s.Read(buf, read, count - read);
				if (n <= 0) return null;
				read += n;
			}
			return buf;
		}

		private static ushort ReadUInt16(Stream s)
		{
			int b0 = s.ReadByte();
			int b1 = s.ReadByte();
			if (b0 < 0 || b1 < 0) throw new EndOfStreamException();
			return (ushort)(b0 | (b1 << 8));
		}

		private static uint ReadUInt32(Stream s)
		{
			int b0 = s.ReadByte();
			int b1 = s.ReadByte();
			int b2 = s.ReadByte();
			int b3 = s.ReadByte();
			if ((b0 | b1 | b2 | b3) < 0) throw new EndOfStreamException();
			return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
		}

		private static ulong ReadUInt64(Stream s)
		{
			ulong v = 0;
			for (int i = 0; i < 8; i++)
			{
				int b = s.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				v |= ((ulong)(byte)b) << (i * 8);
			}
			return v;
		}

		private static IPAddress ReadIp(Stream s)
		{
			int len = s.ReadByte();
			if (len < 0) throw new EndOfStreamException();
			if (len == 0) return null;
			if (len != 4 && len != 16)
				throw new InvalidDataException($"Invalid IP byte length: {len}");
			byte[] bytes = ReadExactly(s, len);
			if (bytes == null) throw new EndOfStreamException();
			return new IPAddress(bytes);
		}

		private static void SkipBytes(Stream s, long count)
		{
			while (count > 0)
			{
				int b = s.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				count--;
			}
		}

		private sealed class IPAddressComparer : IEqualityComparer<IPAddress>
		{
			public bool Equals(IPAddress x, IPAddress y) => x?.Equals(y) ?? y == null;
			public int GetHashCode(IPAddress obj) => obj?.GetHashCode() ?? 0;
		}
	}
}
