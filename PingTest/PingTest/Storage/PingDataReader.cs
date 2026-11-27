using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace PingTracer.Storage
{
	/// <summary>
	/// Reads .ptrc files (see <see cref="PingDataFile"/>), either into a new read-only <see cref="MonitoringSession"/> or
	/// appended to an existing session (to rebuild a configuration's history from its segments).
	/// </summary>
	public static class PingDataReader
	{
		public sealed class Result
		{
			public MonitoringSession Session;
			public PingDataFile.Header Header;
			/// <summary>True if the file ended in the middle of a block (e.g. after a crash). Everything before it was loaded.</summary>
			public bool Truncated;
			/// <summary>Latest record or route event time found (Unix ms), or 0 if the file holds none.</summary>
			public long LastTimeUnixMs;
		}

		/// <summary>Loads a file into a new read-only session (for viewing one saved file).</summary>
		public static Result Load(string path)
		{
			using FileStream fs = PingDataFile.OpenRead(path);
			return Load(fs);
		}

		public static Result Load(Stream stream)
		{
			BinaryReader r = new BinaryReader(stream);
			PingDataFile.Header h = PingDataFile.ReadHeader(r);
			MonitoringSession session = new MonitoringSession(h.Target, h.DisplayName, h.Mode, h.IntervalMs, h.TimeoutMs, h.StartUnixMs, readOnly: true, hostKey: h.HostKey);
			return ReadBody(session, stream, h);
		}

		/// <summary>
		/// Appends a file's data to <paramref name="session"/>.  Addresses are matched by IP, so the file's address ids need not
		/// agree with the session's.  Records must be appended in time order, so call this for a session's files oldest first.
		/// </summary>
		public static Result LoadInto(MonitoringSession session, string path)
		{
			using FileStream fs = PingDataFile.OpenRead(path);
			BinaryReader r = new BinaryReader(fs);
			PingDataFile.Header h = PingDataFile.ReadHeader(r);
			return ReadBody(session, fs, h);
		}

		private static Result ReadBody(MonitoringSession session, Stream afterHeader, PingDataFile.Header h)
		{
			Result result = new Result { Session = session, Header = h };
			// Version 3 times are relative to the file's start; version 4 times are absolute.
			long fileToUnix = h.Version == PingDataFile.LegacyVersion ? h.StartUnixMs : 0;
			long unixToSession = -session.StartUnixMs;
			BlockSink sink = new BlockSink(session, fileToUnix, unixToSession);
			using Stream body = PingDataFile.OpenBody(afterHeader, h);
			BufferedStream bs = new BufferedStream(body, 1 << 16);
			byte[] buf = new byte[4096];
			byte[] lenBytes = new byte[4];
			try
			{
				while (true)
				{
					int type = bs.ReadByte();
					if (type < 0)
						break;
					long length;
					if (h.Version == PingDataFile.LegacyVersion)
					{
						if (bs.ReadAtLeast(lenBytes, 4, throwOnEndOfStream: false) < 4)
						{
							result.Truncated = true;
							break;
						}
						length = BinaryPrimitives.ReadUInt32LittleEndian(lenBytes);
					}
					else
					{
						if (!PingDataFile.TryReadVarUInt(bs, out ulong v))
						{
							result.Truncated = true;
							break;
						}
						length = (long)Math.Min(v, long.MaxValue);
					}
					if (length > PingDataFile.MaxBlockLength)
						throw new InvalidDataException("Block too long.");
					int len = (int)length;
					if (buf.Length < len)
						buf = new byte[Math.Max(len, buf.Length * 2)];
					if (bs.ReadAtLeast(buf.AsSpan(0, len), len, throwOnEndOfStream: false) < len)
					{
						result.Truncated = true;
						break;
					}
					if (h.Version == PingDataFile.LegacyVersion)
						sink.LegacyBlock((byte)type, buf, len);
					else
						sink.Block((byte)type, buf, len);
				}
			}
			catch (Exception ex) when (ex is EndOfStreamException || ex is InvalidDataException || ex is IOException || ex is ArgumentException)
			{
				// A partial or damaged block at the end (e.g. after a crash, or while another process is writing the file).
				result.Truncated = true;
			}
			result.LastTimeUnixMs = sink.LastTimeUnixMs;
			return result;
		}

		/// <summary>Applies decoded blocks to a session.</summary>
		private sealed class BlockSink
		{
			private readonly MonitoringSession session;
			private readonly long fileToUnix;
			private readonly long unixToSession;
			private ushort[] addressMap = new ushort[16];
			private int addressMapCount = 0;
			private readonly List<PingSample> samples = new List<PingSample>();
			public long LastTimeUnixMs;

			public BlockSink(MonitoringSession session, long fileToUnix, long unixToSession)
			{
				this.session = session;
				this.fileToUnix = fileToUnix;
				this.unixToSession = unixToSession;
			}

			private ushort MapAddress(int fileId)
			{
				if (fileId == AddressTable.None || fileId >= addressMapCount)
					return AddressTable.None;
				return addressMap[fileId];
			}

			private void AddAddress(int fileId, IPAddress ip)
			{
				ushort sessionId = session.LoadAddress(ip);
				if (fileId >= addressMap.Length)
				{
					int n = addressMap.Length;
					while (n <= fileId) n *= 2;
					Array.Resize(ref addressMap, n);
				}
				for (int i = addressMapCount; i < fileId; i++)
					addressMap[i] = AddressTable.None;
				addressMap[fileId] = sessionId;
				addressMapCount = Math.Max(addressMapCount, fileId + 1);
			}

			private void NoteTime(long unixMs)
			{
				if (unixMs > LastTimeUnixMs) LastTimeUnixMs = unixMs;
			}

			public void Block(byte type, byte[] b, int len)
			{
				switch (type)
				{
					case PingDataFile.BlockAddress:
					case PingDataFile.BlockHostname:
					case PingDataFile.BlockRouteEvent:
						CommonBlock(type, b, len);
						break;
					case PingDataFile.BlockState:
						if (len >= 1)
							session.LoadDestinationTtl(b[0]);
						break;
					case PingDataFile.BlockRecords:
						{
							if (len < 10) throw new InvalidDataException("Records block too short.");
							int ttl = b[0];
							long baseTime = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(1));
							int pos = 9;
							ulong countL = PingDataFile.ReadVarUInt(b, ref pos, len);
							if (countL > (ulong)len) throw new InvalidDataException("Invalid record count.");
							int count = (int)countL;
							samples.Clear();
							long[] times = new long[count];
							long t = baseTime, d = 0;
							for (int k = 0; k < count; k++)
							{
								d += PingDataFile.UnZigZag(PingDataFile.ReadVarUInt(b, ref pos, len));
								t += d;
								times[k] = t;
							}
							ushort[] rtts = new ushort[count];
							long c = 0;
							for (int k = 0; k < count; k++)
							{
								c += PingDataFile.UnZigZag(PingDataFile.ReadVarUInt(b, ref pos, len));
								rtts[k] = c <= 0 ? PingRecordStatus.Lost : c - 1 > PingRecordStatus.MaxRtt ? PingRecordStatus.MaxRtt : (ushort)(c - 1);
							}
							for (int k = 0; k < count; k++)
							{
								ulong a = PingDataFile.ReadVarUInt(b, ref pos, len);
								ushort addr = a == 0 ? AddressTable.None : MapAddress((int)((a - 1) & 0xFFFF));
								samples.Add(new PingSample(times[k] + unixToSession, rtts[k], addr));
								NoteTime(times[k]);
							}
							session.LoadRecords(ttl, samples);
							break;
						}
				}
			}

			public void LegacyBlock(byte type, byte[] b, int len)
			{
				switch (type)
				{
					case PingDataFile.BlockAddress:
					case PingDataFile.BlockHostname:
					case PingDataFile.BlockRouteEvent:
						CommonBlock(type, b, len);
						break;
					case PingDataFile.BlockRecords:
						{
							if (len < 11) throw new InvalidDataException("Records block too short.");
							int ttl = b[0];
							long baseTime = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(1)) + fileToUnix;
							int count = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(9));
							if (11 + count * 6 > len) throw new InvalidDataException("Records block too short.");
							samples.Clear();
							for (int k = 0, o = 11; k < count; k++, o += 6)
							{
								long t = baseTime + BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
								ushort rtt = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 2));
								ushort addr = MapAddress(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 4)));
								if (rtt > PingRecordStatus.MaxRtt && rtt != PingRecordStatus.Lost)
									continue;
								samples.Add(new PingSample(t + unixToSession, rtt, addr));
								NoteTime(t);
							}
							session.LoadRecords(ttl, samples);
							break;
						}
				}
			}

			private void CommonBlock(byte type, byte[] b, int len)
			{
				BinaryReader r = new BinaryReader(new MemoryStream(b, 0, len, writable: false), Encoding.UTF8);
				switch (type)
				{
					case PingDataFile.BlockAddress:
						{
							ushort id = r.ReadUInt16();
							IPAddress ip = PingDataFile.ReadIp(r);
							if (ip != null && id != AddressTable.None)
								AddAddress(id, ip);
							break;
						}
					case PingDataFile.BlockHostname:
						{
							ushort id = r.ReadUInt16();
							string host = PingDataFile.ReadString(r);
							ushort sid = MapAddress(id);
							if (sid != AddressTable.None)
								session.LoadHostname(sid, host);
							break;
						}
					case PingDataFile.BlockRouteEvent:
						{
							long t = r.ReadInt64() + fileToUnix;
							RouteEventKind kind = (RouteEventKind)r.ReadByte();
							byte ttl = r.ReadByte();
							ushort value = r.ReadUInt16();
							bool initial = r.ReadByte() != 0;
							if (kind == RouteEventKind.HopAddress)
							{
								value = MapAddress(value);
								if (value == AddressTable.None)
									break;
							}
							else if (kind != RouteEventKind.DestinationTtl)
								break;
							session.LoadRouteEvent(new RouteEvent(t + unixToSession, kind, ttl, value, initial));
							NoteTime(t);
							break;
						}
				}
			}
		}
	}
}
