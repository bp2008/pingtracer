using System;
using System.IO;
using System.Net;

namespace PingTracer.Storage
{
	/// <summary>
	/// Loads a .ptrc session file (see <see cref="PingDataFile"/>) into a read-only <see cref="MonitoringSession"/>.
	/// </summary>
	public static class PingDataReader
	{
		public sealed class Result
		{
			public MonitoringSession Session;
			/// <summary>True if the file ended in the middle of a block (e.g. after a crash). Everything before it was loaded.</summary>
			public bool Truncated;
			/// <summary>Byte offset just past the last complete block.</summary>
			public long ValidLength;
		}

		public static Result Load(string path)
		{
			using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536);
			return Load(fs);
		}

		public static Result Load(Stream stream)
		{
			BinaryReader r = new BinaryReader(stream);
			PingDataFile.Header h = PingDataFile.ReadHeader(r);
			MonitoringSession session = new MonitoringSession(h.Target, h.DisplayName, h.Mode, h.IntervalMs, h.TimeoutMs, h.StartUnixMs, readOnly: true);
			Result result = new Result { Session = session, ValidLength = stream.Position };
			long length = stream.Length;

			while (stream.Position < length)
			{
				long blockStart = stream.Position;
				if (length - blockStart < 5)
				{
					result.Truncated = true;
					break;
				}
				byte type = r.ReadByte();
				uint bodyLength = r.ReadUInt32();
				long bodyStart = stream.Position;
				if (bodyStart + bodyLength > length)
				{
					result.Truncated = true;
					break;
				}
				try
				{
					switch (type)
					{
						case PingDataFile.BlockAddress:
							{
								ushort id = r.ReadUInt16();
								IPAddress ip = PingDataFile.ReadIp(r);
								if (ip != null && id == session.Addresses.Count)
									session.Addresses.AddWithId(id, ip);
								break;
							}
						case PingDataFile.BlockHostname:
							{
								ushort id = r.ReadUInt16();
								string host = PingDataFile.ReadString(r);
								session.LoadHostname(id, host);
								break;
							}
						case PingDataFile.BlockRecords:
							{
								int ttl = r.ReadByte();
								long baseTime = r.ReadInt64();
								int count = r.ReadUInt16();
								for (int i = 0; i < count; i++)
								{
									ushort dt = r.ReadUInt16();
									ushort rtt = r.ReadUInt16();
									ushort addr = r.ReadUInt16();
									session.LoadRecord(ttl, new PingSample(baseTime + dt, rtt, addr));
								}
								break;
							}
						case PingDataFile.BlockRouteEvent:
							{
								long t = r.ReadInt64();
								RouteEventKind kind = (RouteEventKind)r.ReadByte();
								byte ttl = r.ReadByte();
								ushort value = r.ReadUInt16();
								bool initial = r.ReadByte() != 0;
								session.LoadRouteEvent(new RouteEvent(t, kind, ttl, value, initial));
								break;
							}
					}
				}
				catch (EndOfStreamException)
				{
					result.Truncated = true;
					break;
				}
				stream.Position = bodyStart + bodyLength; // skip unknown blocks / unread bytes
				result.ValidLength = stream.Position;
			}
			return result;
		}
	}
}
