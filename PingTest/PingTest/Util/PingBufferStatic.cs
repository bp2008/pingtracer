using System;
using System.Collections.Generic;
using System.Text;

namespace PingTracer.Util
{
	/// <summary>
	/// Constructs and stores a shared byte array of a specific size, and efficiently delivers it upon request.
	/// </summary>
	public static class PingBufferStatic
	{
		private static byte[] buffer = Array.Empty<byte>();
		/// <summary>
		/// Retrieves a byte array of the specified size.  IMPORTANT: This method stores the byte array in a static variable and delivers the same array to all callers, only if the size argument is the same.  Every time the size argument changes, the shared byte array is reallocated, which can cause inefficiency as well as a race condition in which the incorrect size of buffer is returned.
		/// </summary>
		/// <param name="size">Size of buffer that is required.</param>
		/// <returns>A byte array of the specified size.</returns>
		/// <exception cref="ArgumentOutOfRangeException">If size is negative.</exception>
		public static byte[] GetBuffer(int size)
		{
			if (buffer.Length != size)
			{
				if (size < 0)
					throw new ArgumentOutOfRangeException(nameof(size));
				return buffer = new byte[size];
			}
			return buffer;
		}
	}
}
