using BPUtil.MVC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PingTracer.Controllers
{
	public abstract class PTControllerBase : ControllerAsync
	{
		/// <summary>
		/// Implements Cross-Site Request Forgery (CSRF) protection.
		/// </summary>
		/// <returns></returns>
		protected override Task<ActionResult> OnAuthorization()
		{
			// "GET" requests are excluded from this requirement, as they are not typically able to use this the HTTP header method of CSRF protection.
			// Instead, we ensure that no "GET" requests can be used to mutate the server state in a meaningful way.
			if (Context.httpProcessor.Request.HttpMethod == "GET")
				return Task.FromResult<ActionResult>(null);

			// Check for the existence of the "X-WebProxy-CSRF-Protection" header.
			if (Context.httpProcessor.Request.Headers["X-WebProxy-CSRF-Protection"] == "1")
				return Task.FromResult<ActionResult>(null);

			// CSRF header missing, therefore this is a probable CSRF attempt.  Refuse the request.
			ActionResult forbidden = StatusCode("403 Forbidden");
			return Task.FromResult(forbidden);
		}
		/// <summary>
		/// Parses an API request argument (JSON) from the HTTP POST body.
		/// </summary>
		/// <typeparam name="T">Type of object to parse into.</typeparam>
		/// <param name="cancellationToken">Cancellation Token</param>
		/// <returns></returns>
		protected Task<T> ParseRequest<T>(CancellationToken cancellationToken = default)
		{
			return ApiRequest.ParseRequest<T>(this, cancellationToken);
		}
		/// <summary>
		/// Returns an ErrorResult containing the specified error message using HTTP status code 299 with the text "Graceful Error".  If you desire an actual error code to be used, call <see cref="Controller.Error(string, string)"/>.
		/// </summary>
		/// <param name="errorMessage">The error message to show to the user.</param>
		/// <returns></returns>
		protected ErrorResult ApiError(string errorMessage)
		{
			return Error(errorMessage, "299 Graceful Error"); // Prevents browser console spam by not using an error status code like 418 or 500.
		}
		/// <summary>
		/// Returns a JsonResult containing the specified object. This result specifies that a non-2xx HTTP response status code should be used in order to prevent autocomplete.  Use only for API methods that have been known to trigger autocomplete in an unwanted manner.
		/// </summary>
		/// <param name="obj">Result object that should be serialized as JSON.</param>
		/// <returns></returns>
		protected JsonResult Json418(object obj)
		{
			return new JsonResult(obj) { ResponseStatus = "418 Success But Prevent Autocomplete" };
		}
		/// <summary>
		/// Returns a JsonResult containing the specified object. This result specifies that a non-2xx HTTP response status code should be used in order to prevent autocomplete.  Use only for API methods that have been known to trigger autocomplete in an unwanted manner.
		/// </summary>
		/// <param name="obj">Result object that should be serialized as JSON.</param>
		/// <returns></returns>
		protected Task<ActionResult> Json418Task(object obj)
		{
			return Task.FromResult<ActionResult>(Json(obj));
		}
	}
}
