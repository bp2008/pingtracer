using BPUtil;
using BPUtil.MVC;
using BPUtil.SimpleHttp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer
{
	public static class ApiRequest
	{
		/// <summary>
		/// Maximum size of a Request Body, in bytes.
		/// </summary>
		public const int RequestBodySizeLimit = 20 * 1024 * 1024;
		/// <summary>
		/// Parses an API request argument (JSON) from the HTTP POST body.
		/// </summary>
		/// <typeparam name="T">Type of object to parse into.</typeparam>
		/// <param name="controller">The <see cref="Controller"/> you are calling from. ("this")</param>
		/// <param name="cancellationToken">Cancellation Token</param>
		/// <returns></returns>
		public static Task<T> ParseRequest<T>(Controller controller, CancellationToken cancellationToken = default)
		{
			return ParseRequest<T>(controller.Context.httpProcessor, cancellationToken);
		}

		/// <summary>
		/// Parses an API request argument (JSON) from the HTTP POST body.
		/// </summary>
		/// <typeparam name="T">Type of object to parse into.</typeparam>
		/// <param name="httpProcessor">The <see cref="HttpProcessor"/> which is handling the API request.</param>
		/// <param name="cancellationToken">Cancellation Token</param>
		/// <returns></returns>
		public static async Task<T> ParseRequest<T>(HttpProcessor httpProcessor, CancellationToken cancellationToken = default)
		{
			if (httpProcessor.Request.HttpMethod != "POST")
				throw new Exception("This API method must be called using HTTP POST");
			ByteUtil.ReadToEndResult result = await ByteUtil.ReadToEndWithMaxLengthAsync(httpProcessor.Request.RequestBodyStream, RequestBodySizeLimit, 5000, cancellationToken).ConfigureAwait(false);
			if (result.EndOfStream)
			{
				string str = ByteUtil.Utf8NoBOM.GetString(result.Data);
				return JsonConvert.DeserializeObject<T>(str);
			}
			else
			{
				throw new HttpProcessor.HttpProcessorException("413 Content Too Large", "This server allows a maximum request body size of " + RequestBodySizeLimit + " bytes.");
			}
		}
	}
}
