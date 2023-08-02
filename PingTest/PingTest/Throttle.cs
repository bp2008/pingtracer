using System;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace PingTracer
{
	/// <summary>
	/// Allows the creation of "throttled" actions.  A throttled action can be called many times, but the underlying action will be called at most once per time interval.  If the throttled action is called at a time when the underlying action is on cooldown, the underlying action will be called as soon as the cooldown expires.  See also <see cref="Debounce"/> for different behavior.
	/// </summary>
	public static class Throttle
	{
		/// <summary>
		/// <para>Creates a thread-safe throttled version of the specified action that, when invoked repeatedly, will only actually call the original action at most once per every [wait] milliseconds.</para>
		/// <para>When you invoke the action returned by this method, the underlying action will always complete asynchronously.</para>
		/// </summary>
		/// <param name="action">The action to throttle.</param>
		/// <param name="wait">The number of milliseconds to wait before calling the action again.</param>
		/// <param name="errorHandler">If the action throws an exception, the exception will be passed to this handler.  If the handler is null, the exception will be swallowed.</param>
		/// <returns>A new action that is a throttled version of the original action.</returns>
		public static Action Create(Action action, int wait, Action<Exception> errorHandler)
		{
			object syncLock = new object();
			Timer timer = null;
			bool pendingExecution = false;

			Action throttledAction = null;
			throttledAction = () =>
			{
				lock (syncLock)
				{
					if (timer != null)
					{
						// We're on cooldown.  Schedule the next invokation.
						pendingExecution = true;
					}
					else
					{
						pendingExecution = false;
						// Not on cooldown.  Invoke synchronously.
						try
						{
							Task.Run(action);
						}
						catch (Exception ex)
						{
							errorHandler?.Invoke(ex);
						}

						// Then start the cooldown.
						timer = new Timer(_ =>
						{
							lock (syncLock)
							{
								timer.Dispose();
								timer = null;
								// Cooldown expired.  Run the action again if required.
								if (pendingExecution)
									throttledAction();
							}
						}, null, wait, Timeout.Infinite);
					}

				}
			};
			return throttledAction;
		}
	}
}