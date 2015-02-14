using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;

namespace PingTest
{
	public static class PingInstancePool
	{
		static Queue<Ping> pool = new Queue<Ping>();
		public static Ping Get()
		{
			if(pool.Count == 0)
				return new Ping();
			else
				return pool.Dequeue();
		}
		public static void Recycle(Ping pinger)
		{
			pool.Enqueue(pinger);
		}
	}
}
