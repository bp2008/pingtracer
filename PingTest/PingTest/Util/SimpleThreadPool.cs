using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

/// <summary>
/// From https://github.com/bp2008/BPUtil
/// </summary>
namespace PingTracer
{
	class PoolThread
	{
		public Thread thread;
		public EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
		public volatile bool thisThreadHasWork = false;
		public CancellationToken cancellationToken;
		private CancellationTokenSource cancellationTokenSource;

		public PoolThread(Thread thread)
		{
			this.thread = thread;
			this.cancellationTokenSource = new CancellationTokenSource();
			this.cancellationToken = cancellationTokenSource.Token;
		}
		/// <summary>
		/// Sets the cancellationToken to the canceled state.
		/// </summary>
		public void Cancel()
		{
			if (!this.cancellationTokenSource.IsCancellationRequested)
				this.cancellationTokenSource.Cancel();
		}
	}
	public class SimpleThreadPool
	{
		/// <summary>
		/// A stack of threads that are idle.
		/// </summary>
		List<PoolThread> idleThreads = new List<PoolThread>();
		/// <summary>
		/// A queue of actions to be performed by threads.
		/// </summary>
		ConcurrentQueue<Action> actionQueue = new ConcurrentQueue<Action>();
		int threadTimeoutMilliseconds;
		int _currentMinThreads;
		int _currentMaxThreads;
		int _currentLiveThreads = 0;
		int _currentBusyThreads = 0;
		int threadNamingCounter = -1;
		bool threadsAreBackgroundThreads;
		string poolName;
		object threadLock = new object();
		volatile bool abort = false;
		/// <summary>
		/// Gets the number of threads that are currently available, including those which are busy and those which are idle.
		/// </summary>
		public int CurrentLiveThreads
		{
			get
			{
				return Thread.VolatileRead(ref _currentLiveThreads);
			}
		}
		/// <summary>
		/// Gets the number of threads that are currently busy processing actions.
		/// </summary>
		public int CurrentBusyThreads
		{
			get
			{
				return Thread.VolatileRead(ref _currentBusyThreads);
			}
		}
		/// <summary>
		/// Gets or sets the soft maximum number of threads this pool should have active at any given time.  It is possible for there to be temporarily more threads than this if certain race conditions are met.  If reducing the value, it may take some time for the number of threads to fall into line, as no special effort is taken to reduce the live thread count quickly.
		/// </summary>
		public int MaxThreads
		{
			get
			{
				return Thread.VolatileRead(ref _currentMaxThreads);
			}
			set
			{
				if (value < 1 || MinThreads > value)
					throw new Exception("MaxThreads must be >= 1 and >= MinThreads");
				Interlocked.Exchange(ref _currentMaxThreads, value);
			}
		}
		/// <summary>
		/// Gets or sets the minimum number of threads this pool should have active at any given time.  If increasing the value, it may take some time for the number of threads to rise, as no special effort is taken to reach this number.
		/// </summary>
		public int MinThreads
		{
			get
			{
				return Thread.VolatileRead(ref _currentMinThreads);
			}
			set
			{
				if (value < 0 || value > MaxThreads)
					throw new Exception("MinThreads must be >= 0 and <= MaxThreads");
				Interlocked.Exchange(ref _currentMinThreads, value);
			}
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="poolName"></param>
		/// <param name="minThreads">The minimum number of threads that should be kept alive at all times.</param>
		/// <param name="maxThreads">The largest number of threads this pool should attempt to have alive at any given time.  It is possible for there to be temporarily more threads than this if certain race conditions are met.</param>
		/// <param name="threadTimeoutMilliseconds"></param>
		/// <param name="useBackgroundThreads">If true, the application will be able to exit without waiting for this thread pool.  Background threads do not prevent a process from terminating. Once all foreground threads belonging to a process have terminated, the common language runtime ends the process. Any remaining background threads are stopped and do not complete.</param>
		/// <param name="logErrorAction">A method to use for logging exceptions.  If null, SimpleHttpLogger.Log will be used.</param>
		public SimpleThreadPool(string poolName, int minThreads = 6, int maxThreads = 32, int threadTimeoutMilliseconds = 60000, bool useBackgroundThreads = true)
		{
			this.poolName = poolName;
			this.threadTimeoutMilliseconds = threadTimeoutMilliseconds;
			if (minThreads < 0 || minThreads > maxThreads)
				throw new ArgumentException("minThreads must be >= 0 and <= maxThreads", "minThreads");
			if (maxThreads < 1 || minThreads > maxThreads)
				throw new ArgumentException("maxThreads must be >= 1 and >= minThreads", "maxThreads");
			this._currentMinThreads = minThreads;
			this._currentMaxThreads = maxThreads;
			this.threadsAreBackgroundThreads = useBackgroundThreads;
			SpawnNewIdleThreads(minThreads);
		}
		/// <summary>
		/// Creates new threads and signal them to begin working.
		/// </summary>
		/// <returns></returns>
		private void SpawnNewActiveThreads(int count)
		{
			SpawnNewActiveOrIdleThreads(count, true);
		}
		/// <summary>
		/// Creates new thread but does not signal them. Instead, the threads are added to the pool of idle threads.
		/// </summary>
		/// <returns></returns>
		private void SpawnNewIdleThreads(int count)
		{
			SpawnNewActiveOrIdleThreads(count, false);
		}
		private void SpawnNewActiveOrIdleThreads(int count, bool active)
		{
			if (abort)
				return;
			lock (threadLock)
			{
				for (int i = 0; i < count; i++)
				{
					if (CurrentLiveThreads < MaxThreads)
					{
						Interlocked.Increment(ref _currentLiveThreads);
						PoolThread pt = new PoolThread(new Thread(threadLoop));
						if (active)
							pt.waitHandle.Set();
						pt.thread.IsBackground = threadsAreBackgroundThreads;
						pt.thread.Name = poolName + " " + Interlocked.Increment(ref threadNamingCounter);
						pt.thread.Start(pt);
						if (!active)
							idleThreads.Add(pt);
					}
				}
			}
		}
		/// <summary>
		/// Aborts all idle threads, prevents the creation of new threads, and prevents new actions from being enqueued.  This cannot be undone.
		/// </summary>
		public void Stop()
		{
			abort = true;
			lock (threadLock)
			{
				foreach (PoolThread pt in idleThreads)
					try
					{
						pt.Cancel();
					}
					catch (ThreadAbortException) { throw; }
					catch (Exception) { }
			}
		}
		public void Enqueue(Action action)
		{
			if (abort)
				return;
			actionQueue.Enqueue(action);
			if (!SignalTopThread())
				SpawnNewActiveThreads(1);
		}

		private bool SignalTopThread()
		{
			if (idleThreads.Count > 0)
			{
				lock (threadLock)
				{
					if (idleThreads.Count > 0)
					{
						PoolThread pt = idleThreads[idleThreads.Count - 1];
						idleThreads.RemoveAt(idleThreads.Count - 1);
						pt.thisThreadHasWork = true;
						pt.waitHandle.Set();
						return true;
					}
				}
			}
			return false;
		}
		private void threadLoop(object args)
		{
			try
			{
				PoolThread pt = (PoolThread)args;
				while (true)
				{
					// Wait for a signal
					if (WaitHandle.WaitAny(new WaitHandle[] { pt.waitHandle, pt.cancellationToken.WaitHandle }, threadTimeoutMilliseconds) != 0)
					{
						// Timeout has occurred. Make sure this thread has no work to perform before quitting.
						lock (threadLock)
						{
							bool isCancelled = pt.cancellationToken.IsCancellationRequested;
							if (!isCancelled && pt.thisThreadHasWork)
							{
								// This thread can't quit now because it has work to do.
								pt.thisThreadHasWork = false;
							}
							else if (!isCancelled && CurrentLiveThreads <= MinThreads)
							{
								// There is no work to do right now, but this thread is not allowed to quit.
								continue;
							}
							else
							{
								// This thread is allowed to quit
								Interlocked.Decrement(ref _currentLiveThreads);
								idleThreads.Remove(pt);
								return;
							}
						}
					}
					pt.thisThreadHasWork = false;
					// If we get here, this thread has been signaled or the timeout has expired but this thread was not allowed to quit.
					Interlocked.Increment(ref _currentBusyThreads);
					try
					{
						// Check for queued actions to perform.
						Action action;
						while (actionQueue.TryDequeue(out action))
						{
							try
							{
								action();
							}
							catch (ThreadAbortException) { throw; }
							catch (Exception) { }
						}
					}
					finally
					{
						Interlocked.Decrement(ref _currentBusyThreads);
					}
					// Return me to the pool
					lock (threadLock)
					{
						idleThreads.Add(pt);
					}
				}
			}
			catch (OperationCanceledException) { }
			catch (ThreadAbortException) { }
			catch (Exception) { }
			lock (threadLock)
			{
				Interlocked.Decrement(ref _currentLiveThreads);
				idleThreads.Remove((PoolThread)args);
			}
		}
	}
}
