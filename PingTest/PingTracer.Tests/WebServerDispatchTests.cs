using BPUtil.SimpleHttp;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace PingTracer.Tests
{
	/// <summary>
	/// Regression tests for the web interface going silent: a WebSocket whose upgrade request had already arrived when the
	/// listener accepted it was handled synchronously on the listener's accept loop, and <c>HandleConnection</c> blocked that
	/// thread until the WebSocket closed.  An open browser tab therefore stopped the server from accepting anything else.
	/// </summary>
	public class WebServerDispatchTests
	{
		private const string UpgradeRequest =
			"GET /ws HTTP/1.1\r\n" +
			"Host: 127.0.0.1\r\n" +
			"Upgrade: websocket\r\n" +
			"Connection: Upgrade\r\n" +
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
			"Sec-WebSocket-Version: 13\r\n" +
			"\r\n";

		/// <summary>Exposes the protected connection dispatch so a test can hand it a connection directly.</summary>
		private sealed class ExposedWebServer : WebServer
		{
			public ExposedWebServer() : base(new Settings { saveSessionData = false }) { }
			public void Dispatch(TcpClient c) => AcceptClient(MakeClientProcessor(c, this, certificateSelector, AllowedConnectionTypes.httpAndHttps), CancellationToken.None);
		}

		/// <summary>
		/// The exact failing case, made deterministic: the whole upgrade request is buffered before the connection is
		/// dispatched, which is what made every await in the request pipeline complete synchronously.
		/// </summary>
		[Fact]
		public void DispatchingABufferedWebSocketUpgradeReturnsPromptly()
		{
			ExposedWebServer server = new ExposedWebServer();
			using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			using TcpClient browser = new TcpClient();
			browser.Connect((IPEndPoint)listener.LocalEndpoint);
			TcpClient serverSide = listener.AcceptTcpClient();
			try
			{
				byte[] request = Encoding.ASCII.GetBytes(UpgradeRequest);
				browser.GetStream().Write(request);
				Stopwatch wait = Stopwatch.StartNew();
				while (serverSide.Available < request.Length && wait.ElapsedMilliseconds < 5000)
					Thread.Sleep(5);
				Assert.Equal(request.Length, serverSide.Available);

				Task dispatch = Task.Factory.StartNew(() => server.Dispatch(serverSide), TaskCreationOptions.LongRunning);
				Assert.True(dispatch.Wait(2000), "Dispatching the connection blocked while the WebSocket was open, which would stall the accept loop.");

				// The WebSocket itself still works: the client receives the upgrade response.
				browser.ReceiveTimeout = 5000;
				Assert.StartsWith("HTTP/1.1 101", ReadHttpHead(browser.GetStream()));
			}
			finally
			{
				browser.Close();
				server.Stop();
				server.PingWsHandler.Shutdown();
			}
		}

		/// <summary>
		/// End to end: many tabs open WebSockets at once (the burst leaves upgrade requests queued behind the listener),
		/// and they stay open.  An ordinary HTTP request must still be answered.
		/// </summary>
		[Fact]
		public void PlainHttpIsAnsweredWhileManyWebSocketsStayOpen()
		{
			int port = FreePort();
			WebServer server = new WebServer(new Settings { saveSessionData = false });
			List<TcpClient> sockets = new List<TcpClient>();
			try
			{
				server.SetBindings(new HttpServerBase.Binding(AllowedConnectionTypes.httpAndHttps, new IPEndPoint(IPAddress.Loopback, port)));
				WaitForListening(port);

				byte[] request = Encoding.ASCII.GetBytes(UpgradeRequest);
				for (int i = 0; i < 32; i++)
				{
					TcpClient c = new TcpClient { NoDelay = true };
					c.Connect(IPAddress.Loopback, port);
					c.GetStream().Write(request);
					sockets.Add(c);
				}

				foreach (TcpClient c in sockets)
				{
					c.ReceiveTimeout = 5000;
					Assert.StartsWith("HTTP/1.1 101", ReadHttpHead(c.GetStream()));
				}

				using TcpClient plain = new TcpClient();
				plain.Connect(IPAddress.Loopback, port);
				plain.ReceiveTimeout = 5000;
				plain.GetStream().Write(Encoding.ASCII.GetBytes("GET /no-such-file.txt HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));
				Assert.StartsWith("HTTP/1.1 ", ReadHttpHead(plain.GetStream()));
			}
			finally
			{
				foreach (TcpClient c in sockets)
					c.Close();
				server.Stop();
				server.PingWsHandler.Shutdown();
			}
		}

		/// <summary>
		/// An open WebSocket must not occupy a thread-pool thread: one per tab starved the pool as tabs accumulated, which
		/// delays every request and timer callback in the process.
		/// </summary>
		[Fact]
		public void OpenWebSocketsDoNotHoldThreadPoolThreads()
		{
			const int count = 32;
			int port = FreePort();
			WebServer server = new WebServer(new Settings { saveSessionData = false });
			List<TcpClient> sockets = new List<TcpClient>();
			try
			{
				server.SetBindings(new HttpServerBase.Binding(AllowedConnectionTypes.httpAndHttps, new IPEndPoint(IPAddress.Loopback, port)));
				WaitForListening(port);
				int busyBefore = BusyWorkerThreads();

				byte[] request = Encoding.ASCII.GetBytes(UpgradeRequest);
				for (int i = 0; i < count; i++)
				{
					TcpClient c = new TcpClient { NoDelay = true, ReceiveTimeout = 10000 };
					c.Connect(IPAddress.Loopback, port);
					c.GetStream().Write(request);
					Assert.StartsWith("HTTP/1.1 101", ReadHttpHead(c.GetStream()));
					sockets.Add(c);
				}
				Thread.Sleep(500); // Let each connection reach its steady state.

				int busyAfter = BusyWorkerThreads();
				Assert.True(busyAfter - busyBefore < count / 2, $"{busyAfter - busyBefore} more thread-pool threads are busy with {count} WebSockets open (before: {busyBefore}, after: {busyAfter}).");
			}
			finally
			{
				foreach (TcpClient c in sockets)
					c.Close();
				server.Stop();
				server.PingWsHandler.Shutdown();
			}
		}

		private static int BusyWorkerThreads()
		{
			ThreadPool.GetMaxThreads(out int max, out _);
			ThreadPool.GetAvailableThreads(out int available, out _);
			return max - available;
		}

		private static int FreePort()
		{
			TcpListener l = new TcpListener(IPAddress.Loopback, 0);
			l.Start();
			int port = ((IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();
			return port;
		}

		private static void WaitForListening(int port)
		{
			Stopwatch sw = Stopwatch.StartNew();
			while (true)
			{
				try
				{
					using TcpClient probe = new TcpClient();
					probe.Connect(IPAddress.Loopback, port);
					return;
				}
				catch (SocketException) when (sw.ElapsedMilliseconds < 5000)
				{
					Thread.Sleep(20);
				}
			}
		}

		/// <summary>Reads up to the end of the HTTP response headers (throws IOException if the socket's ReceiveTimeout elapses).</summary>
		private static string ReadHttpHead(NetworkStream s)
		{
			StringBuilder sb = new StringBuilder();
			while (!sb.ToString().EndsWith("\r\n\r\n"))
			{
				int b = s.ReadByte();
				if (b < 0) break;
				sb.Append((char)b);
			}
			return sb.ToString();
		}
	}
}
