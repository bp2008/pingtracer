using BPUtil.MVC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
		public Task<T> ParseRequest<T>(CancellationToken cancellationToken = default)
		{
			return ApiRequest.ParseRequest<T>(this, cancellationToken);
		}
		/// <summary>
		/// Returns a JsonResult containing an <see cref="ApiResponseBase"/> that indicates failure and includes the specified error message.
		/// </summary>
		/// <param name="errorMessage">The error message to show to the user.</param>
		/// <returns></returns>
		public JsonResult ApiError(string errorMessage)
		{
			return new JsonResult(new ApiResponseBase(false, errorMessage)) { ResponseStatus = "418 Error But Prevent Autocomplete" };
		}
		/// <summary>
		/// Returns a JsonResult containing an <see cref="ApiResponseBase"/> that indicates failure and includes the specified error message. This result specifies that a non-2xx HTTP response status code should be used in order to prevent autocomplete.
		/// </summary>
		/// <param name="obj">Result object that should be serialized as JSON.</param>
		/// <returns></returns>
		public JsonResult ApiSuccessNoAutocomplete(ApiResponseBase obj)
		{
			return new JsonResult(obj) { ResponseStatus = "418 Success But Prevent Autocomplete" };
		}
		/// <summary>
		/// Returns a JsonResult containing an <see cref="ApiResponseBase"/> that indicates failure and includes the specified error message.
		/// </summary>
		/// <param name="errorMessage">The error message to show to the user.</param>
		/// <returns></returns>
		public Task<ActionResult> ApiErrorTask(string errorMessage)
		{
			return Task.FromResult<ActionResult>(ApiError(errorMessage));
		}
		/// <summary>
		/// Returns a JsonResult containing an <see cref="ApiResponseBase"/> that indicates failure and includes the specified error message. This result specifies that a non-2xx HTTP response status code should be used in order to prevent autocomplete.
		/// </summary>
		/// <param name="obj">Result object that should be serialized as JSON.</param>
		/// <returns></returns>
		public Task<ActionResult> ApiSuccessNoAutocompleteTask(ApiResponseBase obj)
		{
			return Task.FromResult<ActionResult>(ApiSuccessNoAutocomplete(obj));
		}
	}
}
