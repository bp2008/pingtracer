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
			webServer.SetBindings(new HttpServerBase.Binding(AllowedConnectionTypes.httpAndHttps, new IPEndPoint(IPAddress.Loopback, 8010)));
			//webServer.SetBindings(8010, 8010);

			Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm(args));

			webServer.Stop();
		}
	}
}
