using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;

namespace PingTracer
{
	public static class PingInstancePool
	{
		static ConcurrentQueue<Ping> pool = new ConcurrentQueue<Ping>();
		public static Ping Get()
		{
			Ping pinger;
			if (!pool.TryDequeue(out pinger))
				pinger = new Ping();
			return pinger;
		}
		public static void Recycle(Ping pinger)
		{
			pool.Enqueue(pinger);
		}
	}
}
