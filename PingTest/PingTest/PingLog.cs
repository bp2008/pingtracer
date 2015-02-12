﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

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
