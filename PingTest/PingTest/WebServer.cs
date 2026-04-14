using BPUtil;
using BPUtil.MVC;
using BPUtil.SimpleHttp;
using Newtonsoft.Json;
using PingTracer.Controllers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer
{
	public class WebServer : HttpServerAsync
	{
		MVCMain mvcMain;
		ViteProxy viteProxy = null;
		public static string projectDirPath { get; private set; } = "";
		public WebServer() : base(CreateCertificateSelector())
		{
			MVCGlobals.RemoteClientsMaySeeExceptionDetails = true;
			MvcJson.DeserializeObject = JsonConvert.DeserializeObject;
			MvcJson.SerializeObject = JsonConvert.SerializeObject;
			mvcMain = new MVCMain(Assembly.GetExecutingAssembly(), typeof(PTControllerBase).Namespace, MvcErrorHandler);
#if DEBUG
			if (System.Diagnostics.Debugger.IsAttached)
			{
				string binFolderPath = FileUtil.FindAncestorDirectory(Globals.ApplicationDirectoryBase, "bin");
				if (binFolderPath == null)
					throw new ApplicationException("Unable to locate bin folder in path: " + Globals.ApplicationDirectoryBase);
				projectDirPath = new DirectoryInfo(binFolderPath).Parent.FullName;
				string path = Path.Combine(projectDirPath, "pingtracer-web");
				viteProxy = new ViteProxy(5173, path);
			}
#endif
		}
		private static void MvcErrorHandler(RequestContext Context, Exception ex)
		{
			if (!HttpProcessor.IsOrdinaryDisconnectException(ex))
				Logger.Debug(ex, "WebServer: " + Context.OriginalRequestPath);
		}

		private static ICertificateSelector CreateCertificateSelector()
		{
			return new SelfSignedCertificateSelector();
		}
		public override async Task handleRequest(HttpProcessor p, string method, CancellationToken cancellationToken = default)
		{
			if (!await mvcMain.ProcessRequestAsync(p, cancellationToken: cancellationToken).ConfigureAwait(false))
			{
				if (viteProxy != null)
				{
					// Handle hot module reload provided by Vite dev server.
					await viteProxy.ProxyAsync(p, cancellationToken).ConfigureAwait(false);
					return;
				}
				else
				{
					#region www
					string wwwDirectoryBase = Globals.ApplicationDirectoryBase + "www" + '/';

					FileInfo fi = new FileInfo(wwwDirectoryBase + p.Request.Page);
					string targetFilePath = fi.FullName.Replace('\\', '/');
					if (!targetFilePath.StartsWith(wwwDirectoryBase) || targetFilePath.Contains("../"))
					{
						p.Response.Simple("400 Bad Request");
						return;
					}
					if (p.Request.Page.IEquals(""))
						fi = new FileInfo(wwwDirectoryBase + "index.html");
					await p.Response.StaticFileAsync(fi, cancellationToken: cancellationToken).ConfigureAwait(false);
					#endregion
				}
			}
			p.Response.Simple("200 OK", "Web Server Online");
		}

		protected override void stopServer()
		{
		}
	}
}
