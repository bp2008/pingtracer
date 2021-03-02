using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingTracer.Tracer
{
	/// <summary>
	/// Handles pinging of the host(s) defined by a Profile.
	/// </summary>
	public class PingController
	{
		/// <summary>
		/// Gets the Profile bound to this PingController.
		/// </summary>
		public readonly HostSettings p;
		public PingController(HostSettings p)
		{
			this.p = p;
		}
	}
}
