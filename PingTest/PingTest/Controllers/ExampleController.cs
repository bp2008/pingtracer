using BPUtil;
using BPUtil.MVC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Controllers
{
	/// <summary>
	/// This class is exposed via the web server as the paths "/ExampleController/ExampleRpcMethodWithNoArgs" and so on.  This class demonstrates how to write API methods that can be called from JavaScript using the "RPC" proxy object or the "ExecAPI" function.
	/// </summary>
	public class ExampleController : PTControllerBase
	{
		/// <summary>
		/// This action method is to be used with the JavaScript "RPC" proxy object, which offers simplified API call syntax using arguments in the query string.
		/// </summary>
		/// <returns></returns>
		public async Task<ActionResult> ExampleRpcMethodWithNoArgs()
		{
			return Json("Hello this fine " + DateTime.Now.DayOfWeek.ToString() + ".");
		}
		/// <summary>
		/// This action method is to be used with the JavaScript "RPC" proxy object, which offers simplified API call syntax using arguments in the query string.
		/// </summary>
		/// <param name="arg1">1st argument. The argument name does not matter, but the type and order of arguments is of critical importance.</param>
		/// <param name="arg2">2nd argument. The argument name does not matter, but the type and order of arguments is of critical importance.</param>
		/// <returns></returns>
		public async Task<ActionResult> ExampleRpcMethodWithTwoArgs(string arg1, int arg2)
		{
			return Json("I have received your " + arg1 + " and your " + arg2 + " and I accept them gladly.");
		}
		/// <summary>
		/// This action method is to be used with the "ExecAPI" function.  ExecAPI can accept a complex JSON-encoded argument via the HTTP POST body.
		/// </summary>
		/// <returns></returns>
		public async Task<ActionResult> ExampleApiMethodWithNoArgument()
		{
			return Json(DateTime.UtcNow.ToString("o"));
		}
		/// <summary>
		/// This action method is to be used with the "ExecAPI" function.  ExecAPI can accept a complex JSON-encoded argument via the HTTP POST body.
		/// </summary>
		/// <returns></returns>
		public async Task<ActionResult> ExampleApiMethodWithNumbersArgument()
		{
			ExampleApiRequest request = await ParseRequest<ExampleApiRequest>().ConfigureAwait(false);
			return Json(new ExampleApiResponse("The sum of the argued numbers is " + request.numbers.Sum() + "."));
		}
		class ExampleApiRequest
		{
			public int[] numbers;
		}
		class ExampleApiResponse
		{
			public string message;
			public ExampleApiResponse(string message)
			{
				this.message = message;
			}
		}
		/// <summary>
		/// This action method can be used with either the "RPC" object or the "ExecAPI" function, and demonstrates how graceful error messages are returned.
		/// </summary>
		/// <returns></returns>
		public async Task<ActionResult> ExampleMethodReturnsError()
		{
			ExampleApiRequest request = await ParseRequest<ExampleApiRequest>().ConfigureAwait(false);
			return ApiError("This example error was created at " + DateTime.Now.ToString() + ".");
		}
	}
}
