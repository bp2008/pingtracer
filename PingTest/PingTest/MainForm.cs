using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PingTest
{
	public partial class MainForm : Form
	{
		private volatile bool isRunning = false;
		private Thread controllerThread;
		private volatile int pingDelay = 1000;

		private long successfulPings = 0;
		private long failedPings = 0;

		private const string dateFormatString = "yyyy'-'MM'-'dd HH':'mm':'ss':'fff tt";

		/// <summary>
		/// Maps IP addresses to their PingGraphControl instances.
		/// </summary>
		public static SortedList<int, PingGraphControl> pingGraphs = new SortedList<int, PingGraphControl>();
		public static SortedList<int, IPAddress> pingTargets = new SortedList<int, IPAddress>();
		public static SortedList<int, bool> pingTargetHasAtLeastOneSuccess = new SortedList<int, bool>();
		private static int graphSortingCounter = 0;
		/// <summary>
		/// After a short period of time, any hosts that have not yet responded to ping are removed from most lists and this flag gets set to true which enables textual logging of ping failures.
		/// </summary>
		private bool clearedDeadHosts = false;

		/// <summary>
		/// A hidden panel that will hold the graphs once clicked.
		/// </summary>
		Form panelForm = new Form();

		Settings settings = new Settings();

		DateTime suppressHostSettingsSaveUntil = DateTime.MinValue;

		public MainForm()
		{
			InitializeComponent();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			this.Text += " " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
			settings.Load();
			lock (settings.hostHistory)
			{
				if (settings.hostHistory.Count > 0)
					LoadHostSettings(settings.hostHistory[0]);
			}
			nudPingsPerSecond_ValueChanged(null, null);
		}

		private void controllerLoop(object arg)
		{
			object[] args = (object[])arg;
			string host = (string)args[0];
			bool traceRoute = (bool)args[1];

			foreach (PingGraphControl graph in pingGraphs.Values)
				graph.ClearAll();
			pingGraphs.Clear();
			pingTargets.Clear();
			pingTargetHasAtLeastOneSuccess.Clear();
			clearedDeadHosts = false;
			panel_Graphs.Invoke((Action)(() =>
			{
				panel_Graphs.Controls.Clear();
			}));
			graphSortingCounter = 0;
			IPAddress target = null;
			try
			{
				// Find the IP address of the host entered by the user
				try
				{
					target = IPAddress.Parse(host);
				}
				catch (Exception)
				{
				}
				if (target == null)
				{
					IPHostEntry iphe = Dns.GetHostEntry(host);
					if (iphe.AddressList.Length > 0)
						target = iphe.AddressList[0];
				}
				if (target == null)
				{
					CreateLogEntry("Host \"" + host + "\" could not be resolved");
					return;
				}
				CreateLogEntry("(" + DateTime.Now.ToString(dateFormatString) + "): Initializing pings to " + target.ToString());
				if (traceRoute)
				{
					CreateLogEntry("Tracing route ...");
					foreach (var entry in Tracert.Trace(target, 64, 5000))
					{
						CreateLogEntry(entry.ToString());
						AddPingTarget(entry.Address, entry.Hostname);
					}
				}
				else
				{
					AddPingTarget(target, host);
				}
				CreateLogEntry("Now beginning pings");
				DateTime lastPingAt = DateTime.Now.AddSeconds(-60);
				byte[] buffer = new byte[0];

				DateTime startedPingingAt = DateTime.Now;
				while (true)
				{
					try
					{
						if (!clearedDeadHosts && startedPingingAt.AddSeconds(10) < DateTime.Now)
						{
							IList<int> pingTargetIds = pingTargets.Keys;
							foreach (int pingTargetId in pingTargetIds)
							{
								if (!pingTargetHasAtLeastOneSuccess[pingTargetId])
								{
									// This ping target has not yet had a successful response. Assume it never will, and delete it.
									panel_Graphs.Invoke((Action)(() =>
									{
										pingTargets.Remove(pingTargetId);
										panel_Graphs.Controls.Remove(pingGraphs[pingTargetId]);
										pingGraphs.Remove(pingTargetId);
									}));
								}
							}
							panel_Graphs.Invoke((Action)(() =>
							{
								panel_Graphs_Resize(null, null);
							}));
							clearedDeadHosts = true;
						}
						while (pingDelay < 0)
							Thread.Sleep(100);
						int msToWait = (int)(lastPingAt.AddMilliseconds(pingDelay) - DateTime.Now).TotalMilliseconds;
						if (msToWait > 0)
							Thread.Sleep(msToWait);
						lastPingAt = DateTime.Now;
						// We can't re-use the same Ping instance because it is only capable of one ping at a time.
						foreach (var targetMapping in pingTargets)
						{
							PingGraphControl graph = pingGraphs[targetMapping.Key];
							long offset = graph.ClearNextOffset();
							Ping pinger = PingInstancePool.Get();
							pinger.PingCompleted += pinger_PingCompleted;
							pinger.SendAsync(targetMapping.Value, 5000, buffer, new object[] { lastPingAt, offset, graph, targetMapping.Key, targetMapping.Value, pinger });
						}
					}
					catch (ThreadAbortException ex)
					{
						throw ex;
					}
					catch (Exception)
					{
					}
				}
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception ex)
			{
				if (!(ex.InnerException is ThreadAbortException))
					MessageBox.Show(ex.ToString());
			}
			finally
			{
				CreateLogEntry("(" + DateTime.Now.ToString(dateFormatString) + "): Shutting down pings to " + (target == null ? "null" : target.ToString()));
			}
		}
		void pinger_PingCompleted(object sender, PingCompletedEventArgs e)
		{
			try
			{
				object[] args = (object[])e.UserState;
				DateTime time = (DateTime)args[0];
				long pingNum = (long)args[1];

				PingGraphControl graph = (PingGraphControl)args[2];
				int pingTargetId = (int)args[3]; // Do not assume the pingTargets or pingGraphs containers will have this key!
				IPAddress remoteHost = (IPAddress)args[4];
				Ping pinger = (Ping)args[5];
				pinger.PingCompleted -= pinger_PingCompleted;
				PingInstancePool.Recycle(pinger);
				if (e.Cancelled)
				{
					graph.AddPingLogToSpecificOffset(pingNum, new PingLog(time, 0, IPStatus.Unknown));
					Interlocked.Increment(ref failedPings);
					return;
				}
				graph.AddPingLogToSpecificOffset(pingNum, new PingLog(time, (short)e.Reply.RoundtripTime, e.Reply.Status));
				if (e.Reply.Status != IPStatus.Success)
				{
					Interlocked.Increment(ref failedPings);
					if (clearedDeadHosts && pingTargets.ContainsKey(pingTargetId))
						CreateLogEntry("" + time.ToString(dateFormatString) + ", " + remoteHost.ToString() + ": " + e.Reply.Status.ToString());
				}
				else
				{
					if (!clearedDeadHosts)
					{
						pingTargetHasAtLeastOneSuccess[pingTargetId] = true;
					}
					Interlocked.Increment(ref successfulPings);
				}
			}
			finally
			{
				UpdatePingCounts(Interlocked.Read(ref successfulPings), Interlocked.Read(ref failedPings));
			}
		}
		private void AddPingTarget(IPAddress ipAddress, string name)
		{
			if (ipAddress == null)
				return;
			try
			{
				if (panel_Graphs.InvokeRequired)
					panel_Graphs.Invoke((Action<IPAddress, string>)AddPingTarget, ipAddress, name);
				else
				{
					int id = graphSortingCounter++;
					PingGraphControl graph = new PingGraphControl();

					pingTargets.Add(id, ipAddress);
					pingGraphs.Add(id, graph);
					pingTargetHasAtLeastOneSuccess.Add(id, false);

					string ipString = ipAddress.ToString();
					if (!string.IsNullOrEmpty(name))
					{
						if (ipString == name)
							graph.DisplayName = name;
						else
							graph.DisplayName = name + " [" + ipString + "]";
					}
					else
						graph.DisplayName = ipString;

					graph.AlwaysShowServerNames = cbAlwaysShowServerNames.Checked;
					graph.Threshold_Bad = (int)nudBadThreshold.Value;
					graph.Threshold_Worse = (int)nudWorseThreshold.Value;
					graph.ShowMinMax = cbMinMax.Checked;

					panel_Graphs.Controls.Add(graph);
					graph.Click += panel_Graphs_Click;
					panel_Graphs_Resize(null, null);
				}
			}
			catch (Exception ex)
			{
				if (!(ex.InnerException is ThreadAbortException))
					CreateLogEntry(ex.ToString());
			}
		}


		private void CreateLogEntry(string str)
		{
			try
			{
				if (txtOut.InvokeRequired)
					txtOut.Invoke(new Action<string>(CreateLogEntry), str);
				else
				{
					txtOut.AppendText(Environment.NewLine + str);
					File.AppendAllText("PingTest_Output.txt", str + Environment.NewLine);
				}
			}
			catch (Exception)
			{
			}
		}

		private void btnStart_Click(object sender, EventArgs e)
		{
			SaveHost();
			btnStart.Enabled = false;
			if (isRunning)
			{
				isRunning = false;
				btnStart.Text = "Idle";
				btnStart.BackColor = Color.FromArgb(255, 128, 128);
				controllerThread.Abort();
				txtHost.Enabled = true;
				cbTraceroute.Enabled = true;
			}
			else
			{
				isRunning = true;
				btnStart.Text = "Active";
				btnStart.BackColor = Color.FromArgb(128, 255, 128);
				controllerThread = new Thread(controllerLoop);
				controllerThread.Start(new object[] { txtHost.Text, cbTraceroute.Checked });
				txtHost.Enabled = false;
				cbTraceroute.Enabled = false;
			}
			btnStart.Enabled = true;
		}

		private void nudPingsPerSecond_ValueChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			if (nudPingsPerSecond.Value == 0)
				pingDelay = -1;
			else
				pingDelay = (int)(1000 / nudPingsPerSecond.Value);
		}

		private void UpdatePingCounts(long successful, long failed)
		{
			try
			{
				if (lblSuccessful.InvokeRequired)
					lblSuccessful.Invoke(new Action<long, long>(UpdatePingCounts), successful, failed);
				else
				{
					lblSuccessful.Text = successful.ToString();
					lblFailed.Text = failed.ToString();
				}
			}
			catch (Exception)
			{
			}
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (isRunning)
			{
				SaveHost();
				btnStart_Click(null, null);
			}
		}

		private void panel_Graphs_Resize(object sender, EventArgs e)
		{
			if (pingGraphs.Count == 0)
				return;
			int width = panel_Graphs.Width;
			int outerHeight = panel_Graphs.Height / pingGraphs.Count;
			int innerHeight = outerHeight - 1;
			IList<int> keys = pingGraphs.Keys;
			for (int i = 0; i < keys.Count; i++)
			{
				PingGraphControl graph = pingGraphs[keys[i]];
				if (i == keys.Count - 1)
				{
					int leftoverSpace = panel_Graphs.Height - (outerHeight * keys.Count);
					innerHeight += leftoverSpace + 1;
				}
				graph.SetBounds(0, i * outerHeight, width, innerHeight);
			}
		}

		private void cbAlwaysShowServerNames_CheckedChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.AlwaysShowServerNames = cbAlwaysShowServerNames.Checked;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}

		private void nudBadThreshold_ValueChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			if (nudWorseThreshold.Value < nudBadThreshold.Value)
				nudWorseThreshold.Value = nudBadThreshold.Value;
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.Threshold_Bad = (int)nudBadThreshold.Value;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}

		private void nudWorseThreshold_ValueChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			if (nudBadThreshold.Value > nudWorseThreshold.Value)
				nudBadThreshold.Value = nudWorseThreshold.Value;
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.Threshold_Worse = (int)nudWorseThreshold.Value;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}

		private void cbMinMax_CheckedChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.ShowMinMax = cbMinMax.Checked;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}

		private void cbTraceroute_CheckedChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
		}

		private void txtDisplayName_TextChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
		}

		private void panel_Graphs_Click(object sender, EventArgs e)
		{
			if (panel_Graphs.Parent == splitContainer1.Panel2)
			{
				panelForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
				panelForm.Controls.Add(panel_Graphs);
				panel_Graphs.Dock = DockStyle.Fill;
				panelForm.Show();
				panelForm.SetBounds(this.Left, this.Top, this.Width, this.Height);
				this.Hide();
			}
			else
			{
				splitContainer1.Panel2.Controls.Add(panel_Graphs);
				panel_Graphs.Dock = DockStyle.Fill;
				this.Show();
				panelForm.Hide();
			}
		}

		private void lblHost_Click(object sender, EventArgs e)
		{
			ShowHostHistory();
			contextMenuStripHostHistory.Show(Cursor.Position);
		}
		private void ShowHostHistory()
		{
			contextMenuStripHostHistory.Items.Clear();
			bool first = true;
			lock (settings.hostHistory)
			{
				foreach (HostSettings hs in settings.hostHistory)
				{
					ToolStripItem item = new ToolStripMenuItem();
					//Name that will appear on the menu
					if (string.IsNullOrWhiteSpace(hs.displayName))
						item.Text = hs.host;
					else
						item.Text = hs.displayName + " [" + hs.host + "]";
					//Put in the Name property whatever neccessery to retrive your data on click event
					item.Tag = hs;
					//On-Click event
					item.Click += new EventHandler(rsitem_Click);

					if (first)
						item.Font = new Font(item.Font, FontStyle.Bold);
					first = false;

					//Add the submenu to the parent menu
					contextMenuStripHostHistory.Items.Add(item);
				}
			}
		}
		private void rsitem_Click(object sender, EventArgs e)
		{
			if (isRunning)
			{
				MessageBox.Show("Cannot load a stored host while pings are running." + Environment.NewLine + "Please stop the pings first.");
				return;
			}

			ToolStripItem tsi = (ToolStripItem)sender;
			HostSettings hs = (HostSettings)tsi.Tag;
			LoadHostSettings(hs);
		}

		private void LoadHostSettings(HostSettings hs)
		{
			suppressHostSettingsSaveUntil = DateTime.Now.AddMilliseconds(100);

			txtHost.Text = hs.host;
			txtDisplayName.Text = hs.displayName;
			nudPingsPerSecond.Value = hs.rate;
			cbTraceroute.Checked = hs.doTraceRoute;
			cbAlwaysShowServerNames.Checked = hs.drawServerNames;
			cbMinMax.Checked = hs.drawMinMax;
			nudBadThreshold.Value = hs.badThreshold;
			nudWorseThreshold.Value = hs.worseThreshold;


			lock (settings.hostHistory)
			{
				for (int i = 1; i < settings.hostHistory.Count; i++)
					if (settings.hostHistory[i].host == hs.host)
					{
						HostSettings justLoaded = settings.hostHistory[i];
						settings.hostHistory.RemoveAt(i);
						settings.hostHistory.Insert(0, justLoaded);
						break;
					}
			}
		}
		private void SaveHostIfHostAlreadyExists()
		{
			lock (settings.hostHistory)
			{
				bool hostExists = false;
				foreach (HostSettings hs in settings.hostHistory)
					if (hs.host == txtHost.Text)
					{
						hostExists = true;
						continue;
					}
				if (hostExists)
					SaveHost();
			}
		}
		/// <summary>
		/// Adds the current host and settings to the recent hosts history and saves the history if necessary.
		/// </summary>
		private void SaveHost()
		{
			if (DateTime.Now < suppressHostSettingsSaveUntil)
				return;
			HostSettings hs = new HostSettings();
			hs.host = txtHost.Text;
			hs.displayName = txtDisplayName.Text;
			hs.rate = (int)nudPingsPerSecond.Value;
			hs.doTraceRoute = cbTraceroute.Checked;
			hs.drawServerNames = cbAlwaysShowServerNames.Checked;
			hs.drawMinMax = cbMinMax.Checked;
			hs.badThreshold = (int)nudBadThreshold.Value;
			hs.worseThreshold = (int)nudWorseThreshold.Value;

			if (!string.IsNullOrWhiteSpace(hs.host))
			{
				lock (settings.hostHistory)
				{
					if (settings.hostHistory.Count == 0)
						settings.hostHistory.Add(hs);
					else
					{
						for (int i = 0; i < settings.hostHistory.Count; i++)
							if (settings.hostHistory[i].host == hs.host)
							{
								settings.hostHistory.RemoveAt(i);
								break;
							}
						settings.hostHistory.Insert(0, hs);
					}
					settings.Save();
				}
			}
		}
	}
}
