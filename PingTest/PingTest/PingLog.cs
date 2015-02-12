using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;

namespace PingTest
{
	public class PingLog
	{
		public DateTime startTime;
		public short pingTime;
		public IPStatus result;
		public PingLog(DateTime startTime, short pingTime, IPStatus result)
		{
			this.startTime = startTime;
			this.pingTime = pingTime;
			this.result = result;
		}
	}
}
