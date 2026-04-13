using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PingTracer
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm(args));
			//TestForm tf = new TestForm(System.Net.Dns.GetHostEntry("ipv4.google.com").AddressList[0]);
			//Application.Run(tf);
		}
	}
}
