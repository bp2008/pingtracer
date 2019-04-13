using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace PingTracer.TraceRoute
{
	/// <summary>
	/// A class which handles monitoring the network path to a host.
	/// </summary>
	public class PathTracer : IDisposable
	{
		public TimeSpan pingInterval { get; private set; } = TimeSpan.FromSeconds(1);
		public readonly string host;
		private Timer timer;
		/// <summary>
		/// An event which is raised when the path to a host has changed.
		/// </summary>
		public event EventHandler<PathChangedEventArgs> PathChanged = delegate { };
		/// <summary>
		/// An event which is raised when a ping response is received.
		/// </summary>
		public event EventHandler<PingResponseEventArgs> PingResponse = delegate { };

		public PathTracer(string host, TimeSpan pingInterval)
		{
			this.host = host;
			this.pingInterval = pingInterval;
			timer = new Timer(TimerElapsed, null, TimeSpan.Zero, pingInterval);
		}

		private void TimerElapsed(object state)
		{
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					// dispose managed state (managed objects).
				}

				//  free unmanaged resources (unmanaged objects) and override a finalizer below.
				// set large fields to null.

				disposedValue = true;
			}
		}

		// override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~PathTracer() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
