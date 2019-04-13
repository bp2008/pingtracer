using PingTracer.TraceRoute;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace PingTracer
{
	public partial class TestForm : Form
	{
		const byte maxTtl = 64;
		SimpleThreadPool threadPool = new SimpleThreadPool("Ping Test", maxTtl, maxTtl);
		List<TraceRouteHostResult> results = new List<TraceRouteHostResult>();
		IPAddress addr;
		public TestForm(IPAddress addr)
		{
			this.addr = addr;
			InitializeComponent();
		}

		private void TestForm_Load(object sender, EventArgs e)
		{
			try
			{
				results = new List<TraceRouteHostResult>();
				//RouteTracerMethodA.TraceRoute(null, addr, maxTtl, HandlePingResponse);
				//RouteTracerMethodB.TraceRoute(null, addr, maxTtl, HandlePingResponse);
				RouteTracerMethodC.TraceRoute(threadPool, null, addr, maxTtl, HandlePingResponse);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}
		private void HandlePingResponse(TraceRouteHostResult r)
		{
			lock (this)
			{
				results.Add(r);
				//WriteLine("Got result " + results.Count);
				if (results.Count >= maxTtl)
				{
					results.Sort((a, b) =>
					{
						return a.ttl.CompareTo(b.ttl);
					});
					foreach (TraceRouteHostResult result in results)
					{
						WriteLine(result.ttl.ToString().PadLeft(2, ' ')
							+ " [" + result.roundTripTime.ToString().PadLeft(4, ' ') + "ms]: "
							+ result.replyFrom.ToString() + (result.success ? "" : " (failed)"));
					}
				}
			}
		}

		private void WriteLine(string str)
		{
			if (txtOut.InvokeRequired)
				txtOut.Invoke((Action<string>)WriteLine, str);
			else
			{
				txtOut.AppendText(str + Environment.NewLine);
			}
		}
	}
}
