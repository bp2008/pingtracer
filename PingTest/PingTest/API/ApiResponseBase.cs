using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingTracer
{
	/// <summary>
	/// Base class for API responses.
	/// </summary>
	public class ApiResponseBase
	{
		/// <summary>
		/// True if the API request was successful ([error] will be null or empty).  False if there was an error (see [error]).
		/// </summary>
		public bool success;
		/// <summary>
		/// Error message, only if [success] is false.
		/// </summary>
		public string error;

		/// <summary>
		/// Constructs an ApiResponseBase.
		/// </summary>
		/// <param name="success">If true, the request was successful and there is no error message.</param>
		/// <param name="error">The error which prevented success.</param>
		/// <exception cref="Exception"></exception>
		public ApiResponseBase(bool success, string error = null)
		{
			if (success && !string.IsNullOrEmpty(error))
				throw new Exception("API Response indicated success but contained an error message.");
			if (!success && string.IsNullOrEmpty(error))
				throw new Exception("API Response indicated failure but contained no error message.");

			this.success = success;
			this.error = error;
		}
	}
}
