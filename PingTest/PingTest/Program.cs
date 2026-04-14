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
			webServer = new WebServer();
			webServer.SetBindings(8010, 8010);

			Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm(args));

			webServer.Stop();
		}
	}
}
