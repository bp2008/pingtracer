using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
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
			if (args.Length > 0 && string.Equals(args[0], "benchmark", StringComparison.OrdinalIgnoreCase))
			{
				IPAddress target = args.Length > 1 ? IPAddress.Parse(args[1]) : IPAddress.Parse("10.30.0.1");
				// Run on a thread pool thread to avoid SynchronizationContext interference,
				// which would unfairly penalize Method A's EAP pattern in a WinForms context.
				Task.Run(() => TracerBenchmark.RunAsync(target)).GetAwaiter().GetResult();
				return;
			}

			Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			//Application.Run(new MainForm(args));
			TestForm tf = new TestForm(System.Net.Dns.GetHostEntry("ipv4.google.com").AddressList[0]);
			Application.Run(tf);
		}
	}
}
