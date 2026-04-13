using BPUtil.SimpleHttp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer
{
	public class WebServer : HttpServerAsync
	{
		public override Task handleRequest(HttpProcessor p, string method, CancellationToken cancellationToken = default)
		{
		}

		protected override void stopServer()
		{
		}
	}
}
