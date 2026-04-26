using System;
using System.IO;
using System.Linq;
using System.Net;
using PingTracer.Storage;
using PingTracer.TraceRoute;
using Xunit;

namespace PingTracer.Tests
{
	public class PingDataReaderCorruptionTests : IDisposable
	{
		private static readonly IPAddress Target = IPAddress.Parse("8.8.8.8");
		private readonly string _tmpDir;

		public PingDataReaderCorruptionTests()
		{
			_tmpDir = Path.Combine(Path.GetTempPath(), "ptrc-corrupt-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tmpDir);
		}

		public void Dispose()
		{
			try { Directory.Delete(_tmpDir, recursive: true); } catch { }
		}

		private static TraceRouteHostResult Hit(byte ttl, string ip)
			=> new TraceRouteHostResult(null, true, 10, IPAddress.Parse(ip), ttl, Target, ttl, 5000, DateTime.UtcNow);

		private string WriteValidFile()
		{
			string path = Path.Combine(_tmpDir, "valid.ptrc");
			MonitoringSession session = new MonitoringSession(Target, "test");
			session.RecordTraceResult(new[] { Hit(1, "10.0.0.1") }, session.StartTimeUtc);
			HopTimeSeries s = session.HopData[0].Series[0];
			RecordHandle h = s.BeginPendingRecord(session.StartTimeUtc.AddMilliseconds(50));
			s.CompleteRecord(h, 7);

			using (PingDataWriter w = new PingDataWriter(session, path, flushIntervalSeconds: 60))
				w.Flush();
			return path;
		}

		private sealed class AlwaysAccept : ICorruptionPrompt
		{
			public long ObservedOffset = -1;
			public bool ConfirmTruncate(string filePath, long lastGoodOffset)
			{
				ObservedOffset = lastGoodOffset;
				return true;
			}
		}

		private sealed class AlwaysReject : ICorruptionPrompt
		{
			public bool ConfirmTruncate(string filePath, long lastGoodOffset) => false;
		}

		[Fact]
		public void TrailingPartialBlock_PromptDenied_FileUntouched()
		{
			string path = WriteValidFile();
			long originalLen = new FileInfo(path).Length;

			// Append a fake block header that promises 100 body bytes but only writes 10.
			using (FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write))
			{
				fs.WriteByte(0x03);                  // BlockType: PingData
				fs.Write(new byte[] { 100, 0, 0, 0 }, 0, 4); // BodyLength: 100 LE
				fs.Write(new byte[10], 0, 10);       // truncated body
			}
			long corruptedLen = new FileInfo(path).Length;
			Assert.True(corruptedLen > originalLen);

			AlwaysReject reject = new AlwaysReject();
			MonitoringSession loaded = PingDataReader.LoadFile(path, out long resumeOffset, reject);
			Assert.Equal(originalLen, resumeOffset);
			// File on disk should be unchanged.
			Assert.Equal(corruptedLen, new FileInfo(path).Length);
			// Recovered data is still the pre-corruption prefix.
			Assert.Equal(1, loaded.HopData[0].Series.Single().RecordCount);
		}

		[Fact]
		public void TrailingPartialBlock_PromptApproved_FileTruncated()
		{
			string path = WriteValidFile();
			long originalLen = new FileInfo(path).Length;

			using (FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write))
			{
				fs.WriteByte(0x03);
				fs.Write(new byte[] { 100, 0, 0, 0 }, 0, 4);
				fs.Write(new byte[10], 0, 10);
			}

			AlwaysAccept accept = new AlwaysAccept();
			MonitoringSession loaded = PingDataReader.LoadFile(path, out long resumeOffset, accept);

			Assert.Equal(originalLen, accept.ObservedOffset);
			Assert.Equal(originalLen, new FileInfo(path).Length); // truncated back
			Assert.Equal(originalLen, resumeOffset);
			Assert.Equal(1, loaded.HopData[0].Series.Single().RecordCount);
		}

		[Fact]
		public void BadMagic_Throws()
		{
			string path = Path.Combine(_tmpDir, "bad.ptrc");
			File.WriteAllBytes(path, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 });
			Assert.Throws<InvalidDataException>(() => PingDataReader.LoadFile(path, out _));
		}
	}
}
