using BPUtil;
using BPUtil.SimpleHttp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PingTracer
{
	static class Program
	{
		public static WebServer webServer;
		/// <summary>
		/// Port of the web interface on the loopback address: 8010, or the PINGTRACER_WEB_PORT environment variable (so that a
		/// second instance, such as a test build, can run beside the usual one).
		/// </summary>
		public static int WebPort { get; private set; } = 8010;
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			Logger.CatchAll();
			Logger.Info("EntryAssemblyLocation: " + Globals.EntryAssemblyLocation);
			Globals.InitializeApplicationData(null);
			Globals.SetWritableDirectory(Settings.SettingsFolderPath);

			Settings settings = new Settings();
			settings.Load();

			webServer = new WebServer(settings);
			if (int.TryParse(Environment.GetEnvironmentVariable("PINGTRACER_WEB_PORT"), out int port) && port > 0 && port <= 65535)
				WebPort = port;
			webServer.SetBindings(new HttpServerBase.Binding(AllowedConnectionTypes.httpAndHttps, new IPEndPoint(IPAddress.Loopback, WebPort)));
			//webServer.SetBindings(8010, 8010);

			Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm(args));

			webServer.PingWsHandler.Shutdown();
			webServer.Stop();
		}
	}
}
