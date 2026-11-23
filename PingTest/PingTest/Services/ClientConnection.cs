using BPUtil;
using BPUtil.SimpleHttp.WebSockets;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services
{
	/// <summary>
	/// One connected web client.  All outbound messages go through a queue drained by a dedicated sender thread, so producers
	/// (ping callbacks, which run while holding a session's data lock) never block on the network.  Consecutive live ping
	/// results are coalesced into a single binary frame.
	/// </summary>
	internal sealed class ClientConnection
	{
		/// <summary>A client that falls this far behind is disconnected rather than allowed to consume unbounded memory.</summary>
		private const int MaxQueuedItems = 200000;
		private const int MaxLiveItemsPerFrame = 4096;

		public readonly string Id;
		private readonly WebSocket ws;
		private readonly ConcurrentQueue<OutItem> queue = new ConcurrentQueue<OutItem>();
		private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
		/// <summary>Continuations run asynchronously so that whoever calls <see cref="Close"/> (often the WebSocket's reader thread) does not run the connection's cleanup inline.</summary>
		private readonly TaskCompletionSource closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		private int queued;
		private volatile bool isClosed;

		public ClientConnection(string id, WebSocket ws)
		{
			Id = id;
			this.ws = ws;
			Thread t = new Thread(SendLoop) { IsBackground = true, Name = "WebSocket sender " + id };
			t.Start();
		}

		public bool IsClosed => isClosed;

		public void EnqueueJson(string json) => Enqueue(new OutItem { Json = json });

		public void EnqueueBinary(byte[] data) => Enqueue(new OutItem { Binary = data });

		public void EnqueueLive(in LiveItem item) => Enqueue(new OutItem { Live = item, IsLive = true });

		private void Enqueue(OutItem item)
		{
			if (isClosed) return;
			if (Interlocked.Increment(ref queued) > MaxQueuedItems)
			{
				Logger.Info("WebSocket client " + Id + " is not keeping up; disconnecting it.");
				Close();
				return;
			}
			queue.Enqueue(item);
			signal.Release();
		}

		private void SendLoop()
		{
			BinaryFrameWriter live = null;
			int liveCount = 0;
			try
			{
				while (!isClosed)
				{
					signal.Wait(1000);
					while (!isClosed && queue.TryPeek(out OutItem item))
					{
						if (item.IsLive)
						{
							queue.TryDequeue(out _);
							Interlocked.Decrement(ref queued);
							if (live == null)
							{
								live = new BinaryFrameWriter(16 + 16 * 64);
								live.U8(BinaryFrameType.LiveBatch);
								live.U16(0);
								liveCount = 0;
							}
							LiveItem li = item.Live;
							live.U16((ushort)li.SessionId);
							live.U8(li.Ttl);
							live.U8(li.Kind);
							live.F64(li.TimeUnixMs);
							live.U16(li.Rtt);
							live.U16(li.Addr);
							liveCount++;
							if (liveCount >= MaxLiveItemsPerFrame)
								FlushLive(ref live, ref liveCount);
							continue;
						}
						// Preserve ordering: send any accumulated live items before a different message.
						FlushLive(ref live, ref liveCount);
						queue.TryDequeue(out _);
						Interlocked.Decrement(ref queued);
						if (item.Json != null)
							Send(() => ws.Send(item.Json));
						else if (item.Binary != null)
							Send(() => ws.Send(item.Binary));
					}
					FlushLive(ref live, ref liveCount);
				}
			}
			catch (Exception ex)
			{
				if (!BPUtil.SimpleHttp.HttpProcessor.IsOrdinaryDisconnectException(ex))
					Logger.Debug(ex, "WebSocket sender");
				Close();
			}
		}

		private void FlushLive(ref BinaryFrameWriter live, ref int liveCount)
		{
			if (live == null || liveCount == 0)
				return;
			live.PatchU16(1, (ushort)liveCount);
			byte[] data = live.ToArray();
			live = null;
			liveCount = 0;
			Send(() => ws.Send(data));
		}

		private void Send(Action send)
		{
			if (isClosed || ws.State != WebSocketState.Open)
				return;
			send();
		}

		public void Close()
		{
			if (isClosed) return;
			isClosed = true;
			try { signal.Release(); } catch { }
			try { ws.Close(); } catch { }
			closed.TrySetResult();
		}

		/// <summary>Completes when the connection has been closed.</summary>
		public Task Closed => closed.Task;

		private struct OutItem
		{
			public string Json;
			public byte[] Binary;
			public LiveItem Live;
			public bool IsLive;
		}
	}

	internal struct LiveItem
	{
		public int SessionId;
		public byte Ttl;
		/// <summary>0 = completed sample, 1 = void (retract a previously sent sample).</summary>
		public byte Kind;
		public double TimeUnixMs;
		public ushort Rtt;
		public ushort Addr;
	}
}
