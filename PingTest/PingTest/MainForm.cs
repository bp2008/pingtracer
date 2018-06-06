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

namespace PingTracer
{
	public partial class MainForm : Form
	{
		private volatile bool isRunning = false;
		private Thread controllerThread;
		private volatile int pingDelay = 1000;

		private long successfulPings = 0;
		private long failedPings = 0;

		private const string dateFormatString = "yyyy'-'MM'-'dd hh':'mm':'ss':'fff tt";
		private const string fileNameFriendlyDateFormatString = "yyyy'-'MM'-'dd HH'-'mm'-'ss";

		/// <summary>
		/// This may be null if no ping tracing has begun.
		/// </summary>
		private string currentIPAddress = null;

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

		public Settings settings = new Settings();

		DateTime suppressHostSettingsSaveUntil = DateTime.MinValue;

		public MainForm()
		{
			InitializeComponent();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			this.Text += " " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
			panelForm.Text = this.Text;
			panelForm.Icon = this.Icon;
			panelForm.FormClosing += panelForm_FormClosing;
			settings.Load();
			lock (settings.hostHistory)
			{
				if (settings.hostHistory.Count > 0)
					LoadHostSettings(settings.hostHistory[0]);
			}
			selectPingsPerSecond_SelectedIndexChanged(null, null);
		}

		private IPAddress StringToIp(string address)
		{
			// Validate IP
			try
			{
				return IPAddress.Parse(address);
			}
			catch (FormatException)
			{
			}

			// Try as address
			try
			{
				IPHostEntry iphe = Dns.GetHostEntry(address);
				if (iphe.AddressList.Length > 0)
					return iphe.AddressList[0];
			}
			catch (Exception e)
			{
				throw new Exception("Unable to resolve '" + address + "'", e);
			}

			// Fail
			throw new Exception("Unable to parse '" + address + "'");
		}

		private string GetIpHostname(IPAddress ip)
		{
			try
			{
				return Dns.GetHostEntry(ip).HostName;
			}
			catch (Exception)
			{
			}
			return string.Empty;
		}

		private void controllerLoop(object arg)
		{
			currentIPAddress = null;
			object[] args = (object[])arg;
			string host = (string)args[0];
			bool traceRoute = (bool)args[1];
			bool reverseDnsLookup = (bool)args[2];

			foreach (PingGraphControl graph in pingGraphs.Values)
			{
				graph.ClearAll();
				graph.MouseDown -= panel_Graphs_MouseDown;
				graph.MouseMove -= panel_Graphs_MouseMove;
				graph.MouseLeave -= panel_Graphs_MouseLeave;
				graph.MouseUp -= panel_Graphs_MouseUp;
			}
			Interlocked.Exchange(ref successfulPings, 0);
			Interlocked.Exchange(ref failedPings, 0);
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
				string[] addresses = host.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				target = StringToIp(addresses[0]);
				currentIPAddress = target.ToString();
				CreateLogEntry("(" + DateTime.Now.ToString(dateFormatString) + "): Initializing pings to " + host);

				// Multiple addresses
				if (addresses.Length > 1)
				{
					// Don't clear dead hosts from a predefined list
					clearedDeadHosts = true;
					foreach (string address in addresses)
					{
						IPAddress ip = StringToIp(address.Trim());
						string hostName = reverseDnsLookup ? GetIpHostname(ip) : "";
						AddPingTarget(ip, hostName);
					}
				}
				// Route
				else if (traceRoute)
				{
					CreateLogEntry("Tracing route ...");
					foreach (TracertEntry entry in Tracert.Trace(target, 64, 5000, reverseDnsLookup))
					{
						CreateLogEntry(entry.ToString());
						AddPingTarget(entry.Address, entry.Hostname);
					}
				}
				// Single address
				else
				{
					AddPingTarget(target, host);
				}

				CreateLogEntry("Now beginning pings");
				DateTime lastPingAt = DateTime.Now.AddSeconds(-60);
				byte[] buffer = new byte[0];

				long numberOfPingLoopIterations = 0;
				DateTime tenPingsAt = DateTime.MinValue;
				while (true)
				{
					try
					{
						if (!clearedDeadHosts && tenPingsAt != DateTime.MinValue && tenPingsAt.AddSeconds(10) < DateTime.Now)
						{
							if (pingTargets.Count > 1)
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
											pingGraphs[pingTargetId].MouseDown -= panel_Graphs_MouseDown;
											pingGraphs[pingTargetId].MouseMove -= panel_Graphs_MouseMove;
											pingGraphs[pingTargetId].MouseLeave -= panel_Graphs_MouseLeave;
											pingGraphs[pingTargetId].MouseUp -= panel_Graphs_MouseUp;
											pingGraphs.Remove(pingTargetId);
											if (pingGraphs.Count == 0)
											{
												Label lblNoGraphsRemain = new Label();
												lblNoGraphsRemain.Text = "All graphs were removed because" + Environment.NewLine + "none of the hosts responded to pings.";
												panel_Graphs.Controls.Add(lblNoGraphsRemain);
											}
											else
												pingGraphs[pingGraphs.Count - 1].ShowTimestamps = true; // In case the graph we just removed was the last graph, which is supposed to show the timestamps along its bottom, this will make sure the new (or old) bottom is showing timestamps.
										}));
									}
								}
								panel_Graphs.Invoke((Action)(() =>
								{
									panel_Graphs_Resize(null, null);
								}));
							}
							clearedDeadHosts = true;
						}
						while (pingDelay <= 0)
							Thread.Sleep(100);
						int msToWait = (int)(lastPingAt.AddMilliseconds(pingDelay) - DateTime.Now).TotalMilliseconds;
						while (msToWait > 0)
						{
							Thread.Sleep(Math.Min(msToWait, 1000));
							msToWait = (int)(lastPingAt.AddMilliseconds(pingDelay) - DateTime.Now).TotalMilliseconds;
						}
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
					numberOfPingLoopIterations++;
					if (numberOfPingLoopIterations == 10)
						tenPingsAt = DateTime.Now;
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
				CreateLogEntry("(" + DateTime.Now.ToString(dateFormatString) + "): Shutting down pings to " + host);
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
					if (pingGraphs.Count > 0) // We are adding a new graph that will be at the bottom, so disable timestamp display on the previous graph.
						pingGraphs.Values[pingGraphs.Count - 1].ShowTimestamps = false;

					int id = graphSortingCounter++;
					PingGraphControl graph = new PingGraphControl(this.settings);
					graph.ShowTimestamps = true;

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
					graph.ShowPacketLoss = cbPacketLoss.Checked;

					panel_Graphs.Controls.Add(graph);
					graph.MouseDown += panel_Graphs_MouseDown;
					graph.MouseMove += panel_Graphs_MouseMove;
					graph.MouseLeave += panel_Graphs_MouseLeave;
					graph.MouseUp += panel_Graphs_MouseUp;
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
					if (settings.logTextOutputToFile)
						File.AppendAllText("PingTracer_Output.txt", str + Environment.NewLine);
				}
			}
			catch (Exception)
			{
			}
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

		void panelForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			try
			{
				this.Close();
			}
			catch (Exception) { }
		}

		private void panel_Graphs_Resize(object sender, EventArgs e)
		{
			if (pingGraphs.Count == 0)
				return;
			IList<int> keys = pingGraphs.Keys;
			int width = panel_Graphs.Width;
			int timestampsHeight = pingGraphs[keys[0]].TimestampsHeight;
			int height = panel_Graphs.Height - timestampsHeight;
			int outerHeight = height / pingGraphs.Count;
			int innerHeight = outerHeight - 1;
			for (int i = 0; i < keys.Count; i++)
			{
				PingGraphControl graph = pingGraphs[keys[i]];
				if (i == keys.Count - 1)
				{
					int leftoverSpace = (height - (outerHeight * keys.Count)) + timestampsHeight;
					innerHeight += leftoverSpace + 1;
				}
				graph.SetBounds(0, i * outerHeight, width, innerHeight);
			}
		}

		#region Form input changed/clicked events

		private void lblHost_Click(object sender, EventArgs e)
		{
			ShowHostHistory();
			contextMenuStripHostHistory.Show(Cursor.Position);
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

		private void mi_snapshotGraphs_Click(object sender, EventArgs e)
		{
			string address = currentIPAddress;
			if (address == null)
			{
				MessageBox.Show("Unable to save a snapshot of the graphs at this time.");
				return;
			}
			using (Bitmap bmp = new Bitmap(panel_Graphs.Width, panel_Graphs.Height))
			{
				panel_Graphs.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
				bmp.Save("PingTracer " + address + " " + DateTime.Now.ToString(fileNameFriendlyDateFormatString) + ".png", System.Drawing.Imaging.ImageFormat.Png);
			}
		}

		private void btnStart_Click(object sender, EventArgs e)
		{
			SaveHost();
			btnStart.Enabled = false;
			if (isRunning)
			{
				isRunning = false;
				btnStart.Text = "Click to Start";
				btnStart.BackColor = Color.FromArgb(255, 128, 128);
				controllerThread.Abort();
				txtHost.Enabled = true;
				cbTraceroute.Enabled = true;
				cbReverseDNS.Enabled = true;
			}
			else
			{
				isRunning = true;
				btnStart.Text = "Click to Stop";
				btnStart.BackColor = Color.FromArgb(128, 255, 128);
				controllerThread = new Thread(controllerLoop);
				controllerThread.Start(new object[] { txtHost.Text, cbTraceroute.Checked, cbReverseDNS.Checked });
				txtHost.Enabled = false;
				cbTraceroute.Enabled = false;
				cbReverseDNS.Enabled = false;
			}
			btnStart.Enabled = true;
		}

		private void nudPingsPerSecond_ValueChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			if (nudPingsPerSecond.Value == 0)
				pingDelay = 0;
			else if (selectPingsPerSecond.SelectedIndex == 0)
				pingDelay = Math.Max(100, (int)(1000 / nudPingsPerSecond.Value));
			else
				pingDelay = Math.Max(100, (int)(1000 * nudPingsPerSecond.Value));
		}

		private void selectPingsPerSecond_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (selectPingsPerSecond.SelectedIndex == 0)
				nudPingsPerSecond.Maximum = 10;
			else
				nudPingsPerSecond.Maximum = 600;
			nudPingsPerSecond_ValueChanged(sender, e);
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

		private void cbPacketLoss_CheckedChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.ShowPacketLoss = cbPacketLoss.Checked;
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

		private void cbReverseDNS_CheckedChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
		}

		private void txtDisplayName_TextChanged(object sender, EventArgs e)
		{
			SaveHostIfHostAlreadyExists();
		}
		#endregion

		#region Mouse graph events

		Point pGraphMouseDownAt = new Point();
		Point pGraphMouseLastSeenAt = new Point();
		bool mouseIsDownOnGraph = false;
		bool mouseMayBeClickingGraph = false;
		DateTime lastAllGraphsRedrawTime = DateTime.MinValue;
		private void panel_Graphs_MouseDown(object sender, MouseEventArgs e)
		{
			mouseIsDownOnGraph = true;
			mouseMayBeClickingGraph = true;
			pGraphMouseLastSeenAt = pGraphMouseDownAt = e.Location;
		}
		private void panel_Graphs_MouseMove(object sender, MouseEventArgs e)
		{
			if (mouseIsDownOnGraph)
			{
				if (Math.Abs(pGraphMouseDownAt.X - e.Location.X) >= 5
					|| Math.Abs(pGraphMouseDownAt.Y - e.Location.Y) >= 5)
				{
					mouseMayBeClickingGraph = false;
				}

				if (!mouseMayBeClickingGraph)
				{
					int dx = e.Location.X - pGraphMouseLastSeenAt.X;
					if (dx != 0 && settings.graphScrollMultiplier != 0 && pingGraphs.Count > 0)
					{
						int newScrollXOffset = pingGraphs.Values[0].ScrollXOffset + (dx * settings.graphScrollMultiplier);

						foreach (PingGraphControl graph in pingGraphs.Values)
							graph.ScrollXOffset = newScrollXOffset;

						if (settings.fastRefreshScrollingGraphs || DateTime.Now > lastAllGraphsRedrawTime.AddSeconds(1))
						{
							bool aGraphIsInvalidated = false;
							foreach (PingGraphControl graph in pingGraphs.Values)
								if (graph.IsInvalidatedSync)
								{
									aGraphIsInvalidated = true;
									break;
								}
							if (!aGraphIsInvalidated)
							{
								Console.WriteLine("Invalidating All");
								foreach (PingGraphControl graph in pingGraphs.Values)
									graph.InvalidateSync();
								lastAllGraphsRedrawTime = DateTime.Now;
							}
						}
					}
				}
			}
			pGraphMouseLastSeenAt = e.Location;
		}
		private void panel_Graphs_MouseLeave(object sender, EventArgs e)
		{
			mouseMayBeClickingGraph = mouseIsDownOnGraph = false;
		}
		private void panel_Graphs_MouseUp(object sender, MouseEventArgs e)
		{
			if (mouseIsDownOnGraph
				&& mouseMayBeClickingGraph
				&& (Math.Abs(pGraphMouseDownAt.X - e.Location.X) < 5
					&& Math.Abs(pGraphMouseDownAt.Y - e.Location.Y) < 5))
			{
				panel_Graphs_Click(sender, e);
			}
			pGraphMouseLastSeenAt = e.Location;
			mouseMayBeClickingGraph = mouseIsDownOnGraph = false;
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
		#endregion

		#region Host History

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

		private void LoadHostSettings(HostSettings hs)
		{
			suppressHostSettingsSaveUntil = DateTime.Now.AddMilliseconds(100);

			txtHost.Text = hs.host;
			txtDisplayName.Text = hs.displayName;
			nudPingsPerSecond.Value = hs.rate;
			selectPingsPerSecond.SelectedIndex = hs.pingsPerSecond ? 0 : 1;
			cbTraceroute.Checked = hs.doTraceRoute;
			cbReverseDNS.Checked = hs.reverseDnsLookup;
			cbAlwaysShowServerNames.Checked = hs.drawServerNames;
			cbMinMax.Checked = hs.drawMinMax;
			cbPacketLoss.Checked = hs.drawPacketLoss;
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
			hs.pingsPerSecond = selectPingsPerSecond.SelectedIndex == 0;
			hs.doTraceRoute = cbTraceroute.Checked;
			hs.reverseDnsLookup = cbReverseDNS.Checked;
			hs.drawServerNames = cbAlwaysShowServerNames.Checked;
			hs.drawMinMax = cbMinMax.Checked;
			hs.drawPacketLoss = cbPacketLoss.Checked;
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

		#endregion

		private void mi_Exit_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		OptionsForm optionsForm = null;
		private void mi_Options_Click(object sender, EventArgs e)
		{
			if (optionsForm != null)
			{
				optionsForm.Close();
				optionsForm.Dispose();
			}
			optionsForm = new OptionsForm(this);
			optionsForm.Show();
		}
	}
}
