using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PingTracer
{
	public partial class MainForm : Form
	{
		public bool isRunning { get; private set; } = false;
		public bool graphsMaximized { get; private set; } = false;
		private BackgroundWorker controllerWorker;
		private volatile int pingDelay = 1000;

		private long successfulPings = 0;
		private long failedPings = 0;

		//private const string dateFormatString = "yyyy'-'MM'-'dd hh':'mm':'ss':'fff tt";
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
		private string[] args;
		/// <summary>
		/// Event raised when pinging begins.  See <see cref="isRunning"/>.
		/// </summary>
		public event EventHandler StartedPinging = delegate { };
		/// <summary>
		/// Event raised when pinging stops.  See <see cref="isRunning"/>.
		/// </summary>
		public event EventHandler StoppedPinging = delegate { };
		/// <summary>
		/// Event raised when the selected Host field or Display Name field or Prefer IPv4 value changed.  See <see cref="txtHost"/> and <see cref="txtDisplayName"/> and <see cref="cbPreferIpv4"/>.
		/// </summary>
		public event EventHandler SelectedHostChanged = delegate { };
		/// <summary>
		/// Event raised when the graphs are maximized or restored to the regular window.  See <see cref="graphsMaximized"/>.
		/// </summary>
		public event EventHandler MaximizeGraphsChanged = delegate { };

		private bool _logFailures = false;
		/// <summary>
		/// Gets or sets a value indicating whether failures should be logged for the current UI state.
		/// </summary>
		public bool LogFailures
		{
			get
			{
				return _logFailures;
			}
			set
			{
				_logFailures = value;
				SetLogFailures(value);
			}
		}
		private void SetLogFailures(bool value)
		{
			if (this.InvokeRequired)
				this.Invoke((Action<bool>)SetLogFailures, value);
			else
				cbLogFailures.Checked = value;
		}
		/// <summary>
		/// Assigned during MainForm construction, this field remembers the default window size.
		/// </summary>
		private readonly Size defaultWindowSize;
		/// <summary>
		/// Calls <see cref="_rememberCurrentPosition"/> throttled.
		/// </summary>
		private Action RememberCurrentPositionThrottled;

		public MainForm(string[] args)
		{
			this.args = args;
			RememberCurrentPositionThrottled = Throttle.Create(_rememberCurrentPosition, 250, ex => MessageBox.Show(ex.ToString()));

			InitializeComponent();

			defaultWindowSize = this.Size;
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			this.Text += " " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
			panelForm.Text = this.Text;
			panelForm.Icon = this.Icon;
			panelForm.FormClosing += panelForm_FormClosing;
			selectPingsPerSecond.SelectedIndex = 0;
			settings.Load();
			StartupOptions options = new StartupOptions(args);
			lock (settings.hostHistory)
			{
				HostSettings item = null;
				if (options.StartupHostName != null)
				{
					// Attempt to find the given hostname from the startup options.
					// First, attempt to honor the argued IPv4/IPv6 preference.
					item = settings.hostHistory.FirstOrDefault(h => h.displayName == options.StartupHostName && h.preferIpv4 != options.PreferIPv6);
					if (item == null)
						item = settings.hostHistory.FirstOrDefault(h => h.host == options.StartupHostName && h.preferIpv4 != options.PreferIPv6);

					// Then attempt to match regardless of IPv4/IPv6 preference.
					if (item == null)
						item = settings.hostHistory.FirstOrDefault(h => h.displayName == options.StartupHostName);
					if (item == null)
						item = settings.hostHistory.FirstOrDefault(h => h.host == options.StartupHostName);
				}
				if (item == null)
					item = settings.hostHistory.FirstOrDefault();
				if (item != null)
					LoadProfileIntoUI(item);
			}
			selectPingsPerSecond_SelectedIndexChanged(null, null);
			AddKeyDownHandler(this);
			AddClickHandler(this);

			WindowParams wParams = options.WindowLocation;
			if (wParams == null)
				wParams = settings.lastWindowParams;
			if (wParams != null)
			{
				Size s = this.Size;
				if (wParams.W > 0)
					s.Width = wParams.W;
				if (wParams.H > 0)
					s.Height = wParams.H;

				this.Location = new Point(wParams.X, wParams.Y);
				this.Size = s;
			}

			this.MoveOnscreenIfOffscreen();

			if (options.StartPinging)
				btnStart_Click(this, new EventArgs());

			this.Move += MainForm_MoveOrResize;
			this.Resize += MainForm_MoveOrResize;
		}

		/// <summary>
		/// Adds the <see cref="HandleKeyDown"/> handler to the KeyDown event of this control and most child controls.
		/// </summary>
		/// <param name="parent"></param>
		private void AddKeyDownHandler(Control parent)
		{
			if (parent.GetType() == typeof(NumericUpDown)
				|| (parent.GetType() == typeof(TextBox) && !((TextBox)parent).ReadOnly)
				)
			{
				return;
			}
			parent.KeyDown += new KeyEventHandler(HandleKeyDown);
			foreach (Control c in parent.Controls)
				AddKeyDownHandler(c);
		}
		/// <summary>
		/// Adds a basic "click to focus" handler to this control and most child controls. Non-input control types (such as Form or GroupBox) normally lack this functionality, so calling this makes it possible to unfocus input controls by clicking outside of them.
		/// </summary>
		/// <param name="parent"></param>
		private void AddClickHandler(Control parent)
		{
			if (parent.GetType() == typeof(NumericUpDown)
				|| (parent.GetType() == typeof(TextBox) && !((TextBox)parent).ReadOnly)
				)
			{
				return;
			}
			parent.Click += (sender, e) =>
			{
				((Control)sender).Focus();
			};
			foreach (Control c in parent.Controls)
				AddClickHandler(c);
		}

		private IPAddress StringToIp(string address, bool preferIpv4)
		{
			// Validate IP
			try
			{
				return IPAddress.Parse(address);
			}
			catch (FormatException)
			{
			}

			// Try to resolve host name
			try
			{
				IPHostEntry iphe = Dns.GetHostEntry(address);
				if (preferIpv4)
				{
					IPAddress addr = iphe.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
					if (addr != null)
						return addr;
				}
				else
				{
					IPAddress addr = iphe.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
					if (addr != null)
						return addr;
				}
				if (iphe.AddressList.Length > 0)
					return iphe.AddressList[0];
			}
			catch (Exception e)
			{
				throw new Exception("Unable to resolve '" + address + "'", e);
			}

			// Fail
			throw new Exception("Unable to resolve '" + address + "'");
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

		private void ControllerWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			btnStart.Enabled = true;
		}
		private void Invoke(BackgroundWorker worker, Action action)
		{
			if (controllerWorker == worker)
				this.Invoke(action);
		}
		private void ControllerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			currentIPAddress = null;
			object[] args = (object[])e.Argument;
			BackgroundWorker self = (BackgroundWorker)args[0];
			string host = (string)args[1];
			bool traceRoute = (bool)args[2];
			bool reverseDnsLookup = (bool)args[3];
			bool preferIpv4 = (bool)args[4];

			Invoke(self, () =>
			{
				btnStart.Enabled = true;
			});
			foreach (PingGraphControl graph in pingGraphs.Values)
			{
				graph.ClearAll();
				RemoveEventHandlers(graph);
			}
			Interlocked.Exchange(ref successfulPings, 0);
			Interlocked.Exchange(ref failedPings, 0);
			pingGraphs.Clear();
			pingTargets.Clear();
			pingTargetHasAtLeastOneSuccess.Clear();
			clearedDeadHosts = false;
			Invoke(self, () =>
			{
				panel_Graphs.Controls.Clear();
			});
			graphSortingCounter = 0;
			IPAddress target = null;
			try
			{
				string[] addresses = host.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				target = StringToIp(addresses[0], preferIpv4);
				currentIPAddress = target.ToString();
				CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): Initializing pings to " + host);

				// Multiple addresses
				if (addresses.Length > 1)
				{
					// Don't clear dead hosts from a predefined list
					clearedDeadHosts = true;
					foreach (string address in addresses)
					{
						IPAddress ip = StringToIp(address.Trim(), preferIpv4);
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
				while (!self.CancellationPending)
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
										Invoke(self, () =>
										{
											pingTargets.Remove(pingTargetId);
											panel_Graphs.Controls.Remove(pingGraphs[pingTargetId]);
											RemoveEventHandlers(pingGraphs[pingTargetId]);
											pingGraphs.Remove(pingTargetId);
											if (pingGraphs.Count == 0)
											{
												Label lblNoGraphsRemain = new Label();
												lblNoGraphsRemain.Text = "All graphs were removed because" + Environment.NewLine + "none of the hosts responded to pings.";
												panel_Graphs.Controls.Add(lblNoGraphsRemain);
											}
											ResetGraphTimestamps();
										});
									}
								}
								Invoke(self, () =>
								{
									panel_Graphs_Resize(null, null);
								});
							}
							clearedDeadHosts = true;
						}
						while (!self.CancellationPending && pingDelay <= 0)
							Thread.Sleep(100);
						if (!self.CancellationPending)
						{
							int msToWait = (int)(lastPingAt.AddMilliseconds(pingDelay) - DateTime.Now).TotalMilliseconds;
							while (!self.CancellationPending && msToWait > 0)
							{
								Thread.Sleep(Math.Min(msToWait, 1000));
								msToWait = (int)(lastPingAt.AddMilliseconds(pingDelay) - DateTime.Now).TotalMilliseconds;
							}
							if (!self.CancellationPending)
							{
								lastPingAt = DateTime.Now;
								// We can't re-use the same Ping instance because it is only capable of one ping at a time.
								foreach (KeyValuePair<int, IPAddress> targetMapping in pingTargets)
								{
									PingGraphControl graph = pingGraphs[targetMapping.Key];
									long offset = graph.ClearNextOffset();
									Ping pinger = PingInstancePool.Get();
									pinger.PingCompleted += pinger_PingCompleted;
									pinger.SendAsync(targetMapping.Value, 5000, buffer, new object[] { lastPingAt, offset, graph, targetMapping.Key, targetMapping.Value, pinger });
								}
							}
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
			catch (Exception ex)
			{
				if (!(ex.InnerException is ThreadAbortException))
					MessageBox.Show(ex.ToString());
			}
			finally
			{
				CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): Shutting down pings to " + host);
				if (isRunning)
					btnStart_Click(btnStart, new EventArgs());
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
					if (clearedDeadHosts && pingTargets.ContainsKey(pingTargetId) && LogFailures)
						CreateLogEntry("" + GetTimestamp(time) + ", " + remoteHost.ToString() + ": " + e.Reply.Status.ToString());
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
					PingGraphControl graph = new PingGraphControl(this.settings);

					pingTargets.Add(id, ipAddress);
					pingGraphs.Add(id, graph);
					ResetGraphTimestamps();
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
					graph.ShowLastPing = cbLastPing.Checked;
					graph.ShowAverage = cbAverage.Checked;
					graph.ShowJitter = cbJitter.Checked;
					graph.ShowMinMax = cbMinMax.Checked;
					graph.ShowPacketLoss = cbPacketLoss.Checked;

					panel_Graphs.Controls.Add(graph);
					AddEventHandlers(graph);
					panel_Graphs_Resize(null, null);
				}
			}
			catch (Exception ex)
			{
				if (!(ex.InnerException is ThreadAbortException))
					CreateLogEntry(ex.ToString());
			}
		}
		private void AddEventHandlers(PingGraphControl graph)
		{
			graph.MouseDown += panel_Graphs_MouseDown;
			graph.MouseMove += panel_Graphs_MouseMove;
			graph.MouseLeave += panel_Graphs_MouseLeave;
			graph.MouseUp += panel_Graphs_MouseUp;
			graph.KeyDown += HandleKeyDown;
		}
		private void RemoveEventHandlers(PingGraphControl graph)
		{
			graph.MouseDown -= panel_Graphs_MouseDown;
			graph.MouseMove -= panel_Graphs_MouseMove;
			graph.MouseLeave -= panel_Graphs_MouseLeave;
			graph.MouseUp -= panel_Graphs_MouseUp;
			graph.KeyDown -= HandleKeyDown;
		}

		private void ResetGraphTimestamps()
		{
			IList<PingGraphControl> all = pingGraphs.Values;
			foreach (PingGraphControl g in all)
				g.ShowTimestamps = false;

			PingGraphControl last = all.LastOrDefault();
			if (last != null)
				last.ShowTimestamps = true;
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
			cla_form?.Close();
			if (isRunning)
			{
				SaveProfileFromUI();
				btnStart_Click(btnStart, new EventArgs());
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
			LoadHostHistory();
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
			HostSettings p = (HostSettings)tsi.Tag;
			LoadProfileIntoUI(p);
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
			if (btnStart.InvokeRequired)
			{
				btnStart.BeginInvoke((Action<object, EventArgs>)btnStart_Click, sender, e);
				return;
			}
			SaveProfileFromUI();
			btnStart.Enabled = false;
			if (isRunning)
			{
				isRunning = false;
				btnStart.Text = "Click to Start";
				btnStart.BackColor = Color.FromArgb(255, 128, 128);
				controllerWorker.CancelAsync();
				txtHost.Enabled = true;
				cbTraceroute.Enabled = true;
				cbReverseDNS.Enabled = true;
				StoppedPinging.Invoke(sender, e);
			}
			else
			{
				isRunning = true;
				btnStart.Text = "Click to Stop";
				btnStart.BackColor = Color.FromArgb(128, 255, 128);
				controllerWorker = new BackgroundWorker();
				controllerWorker.WorkerSupportsCancellation = true;
				controllerWorker.DoWork += ControllerWorker_DoWork;
				controllerWorker.RunWorkerCompleted += ControllerWorker_RunWorkerCompleted;
				controllerWorker.RunWorkerAsync(new object[] { controllerWorker, txtHost.Text, cbTraceroute.Checked, cbReverseDNS.Checked, cbPreferIpv4.Checked });
				txtHost.Enabled = false;
				cbTraceroute.Enabled = false;
				cbReverseDNS.Enabled = false;
				StartedPinging.Invoke(sender, e);
			}
		}

		private void nudPingsPerSecond_ValueChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
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
			SaveProfileIfProfileAlreadyExists();
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
			SaveProfileIfProfileAlreadyExists();
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
			SaveProfileIfProfileAlreadyExists();
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


		private void cbLastPing_CheckedChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.ShowLastPing = cbLastPing.Checked;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}

		private void cbAverage_CheckedChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.ShowAverage = cbAverage.Checked;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}
		private void cbJitter_CheckedChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
			try
			{
				IList<PingGraphControl> graphs = pingGraphs.Values;
				foreach (PingGraphControl graph in graphs)
				{
					graph.ShowJitter = cbJitter.Checked;
					graph.Invalidate();
				}
			}
			catch (Exception)
			{
			}
		}
		private void cbMinMax_CheckedChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
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
			SaveProfileIfProfileAlreadyExists();
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
			SaveProfileIfProfileAlreadyExists();
		}

		private void cbReverseDNS_CheckedChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
		}

		private void txtDisplayName_TextChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
			SelectedHostChanged.Invoke(sender, e);
		}

		private void cbPreferIpv4_CheckedChanged(object sender, EventArgs e)
		{
			SaveProfileIfProfileAlreadyExists();
			SelectedHostChanged.Invoke(sender, e);
		}

		private void cbLogFailures_CheckedChanged(object sender, EventArgs e)
		{
			_logFailures = cbLogFailures.Checked;
			SaveProfileIfProfileAlreadyExists();
		}

		private void txtHost_TextChanged(object sender, EventArgs e)
		{
			// This txtHost_TextChanged event handler was added on 2023-08-02, so it did not call SaveProfileIfProfileAlreadyExists(); like most other event handlers.
			SelectedHostChanged.Invoke(sender, e);
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
		private void refreshGraphs()
		{
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
		private void panel_Graphs_MouseMove(object sender, MouseEventArgs e)
		{
			bool mouseWasTeleported = false;
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

						refreshGraphs();

						#region while scrolling graph: teleport mouse when reaching end of graph to enable scrolling infinitely without having to click again
						this.Cursor = new Cursor(Cursor.Current.Handle);
						int offset = 9; //won't work with maximized window otherwise
						if (Cursor.Position.X >= Bounds.Right - offset) //mouse moving to the right
						{
							//teleport mouse to the left
							Cursor.Position = new Point(Bounds.Left + offset, Cursor.Position.Y);
							mouseWasTeleported = true;
						}
						else if (Cursor.Position.X <= Bounds.Left + offset //cursor moving to the left
							&& pingGraphs.Values[0].ScrollXOffset != 0) //usability: only teleport mouse if graphs have data to the right
						{
							//teleport mouse to the right
							Cursor.Position = new Point(Bounds.Right - offset, Cursor.Position.Y);
							mouseWasTeleported = true;
						}
						#endregion
					}
				}
			}
			pGraphMouseLastSeenAt = mouseWasTeleported ? PointToClient(Cursor.Position) : e.Location;
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
		private void HandleKeyDown(object sender, KeyEventArgs e)
		{
			switch (e.KeyData)
			{
				case Keys.Home: //Pos1
				case Keys.D9:
					foreach (PingGraphControl graph in pingGraphs.Values)
						graph.ScrollXOffset = graph.cachedPings - graph.Width - (settings.delayMostRecentPing ? 1 : 0);
					e.Handled = true;
					break;
				case Keys.End:
				case Keys.D0:
					foreach (PingGraphControl graph in pingGraphs.Values)
						graph.ScrollXOffset = 0;
					e.Handled = true;
					break;
				case Keys.PageUp:
				case Keys.OemMinus:
					foreach (PingGraphControl graph in pingGraphs.Values)
						graph.ScrollXOffset += graph.Width;
					e.Handled = true;
					break;
				case Keys.PageDown:
				case Keys.Oemplus:
					foreach (PingGraphControl graph in pingGraphs.Values)
						graph.ScrollXOffset -= graph.Width;
					e.Handled = true;
					break;
				default:
					return;
			}
			refreshGraphs();
		}
		private void panel_Graphs_Click(object sender, EventArgs e)
		{
			if (panel_Graphs.Parent == splitContainer1.Panel2)
			{
				graphsMaximized = true;
				panelForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
				panelForm.Controls.Add(panel_Graphs);
				panel_Graphs.Dock = DockStyle.Fill;
				panelForm.Show();
				panelForm.SetBounds(this.Left, this.Top, this.Width, this.Height);
				this.Hide();
				MaximizeGraphsChanged.Invoke(this, e);
			}
			else
			{
				graphsMaximized = false;
				splitContainer1.Panel2.Controls.Add(panel_Graphs);
				panel_Graphs.Dock = DockStyle.Fill;
				this.Show();
				panelForm.Hide();
				MaximizeGraphsChanged.Invoke(this, e);
			}
		}
		#endregion

		#region Host History

		private void LoadHostHistory()
		{
			contextMenuStripHostHistory.Items.Clear();
			bool first = true;
			lock (settings.hostHistory)
			{
				foreach (HostSettings p in settings.hostHistory)
				{
					ToolStripItem item = new ToolStripMenuItem();
					//Name that will appear on the menu
					if (string.IsNullOrWhiteSpace(p.displayName))
						item.Text = (p.preferIpv4 ? "" : "[ipv6] ") + p.host;
					else
						item.Text = (p.preferIpv4 ? "" : "[ipv6] ") + p.displayName + " [" + p.host + "]";
					item.Tag = p;
					item.Click += new EventHandler(rsitem_Click);

					if (first)
						item.Font = new Font(item.Font, FontStyle.Bold);
					first = false;

					//Add the submenu to the parent menu
					contextMenuStripHostHistory.Items.Add(item);
				}
			}
		}

		private void LoadProfileIntoUI(HostSettings hs)
		{
			suppressHostSettingsSaveUntil = DateTime.Now.AddMilliseconds(100);

			txtHost.Text = hs.host;
			txtDisplayName.Text = hs.displayName;
			selectPingsPerSecond.SelectedIndex = hs.pingsPerSecond ? 0 : 1;
			nudPingsPerSecond.Value = hs.rate;
			cbTraceroute.Checked = hs.doTraceRoute;
			cbReverseDNS.Checked = hs.reverseDnsLookup;
			cbAlwaysShowServerNames.Checked = hs.drawServerNames;
			cbLastPing.Checked = hs.drawLastPing;
			cbAverage.Checked = hs.drawAverage;
			cbJitter.Checked = hs.drawJitter;
			cbMinMax.Checked = hs.drawMinMax;
			cbPacketLoss.Checked = hs.drawPacketLoss;
			nudBadThreshold.Value = hs.badThreshold;
			nudWorseThreshold.Value = hs.worseThreshold;
			cbPreferIpv4.Checked = hs.preferIpv4;
			LogFailures = hs.logFailures;


			lock (settings.hostHistory)
			{
				for (int i = 1; i < settings.hostHistory.Count; i++)
					if (settings.hostHistory[i].host == hs.host && settings.hostHistory[i].preferIpv4 == hs.preferIpv4)
					{
						HostSettings justLoaded = settings.hostHistory[i];
						settings.hostHistory.RemoveAt(i);
						settings.hostHistory.Insert(0, justLoaded);
						break;
					}
			}
		}

		private void SaveProfileIfProfileAlreadyExists()
		{
			lock (settings.hostHistory)
			{
				bool hostExists = false;
				foreach (HostSettings p in settings.hostHistory)
					if (p.host == txtHost.Text && p.preferIpv4 == cbPreferIpv4.Checked)
					{
						hostExists = true;
						break;
					}
				if (hostExists)
					SaveProfileFromUI();
			}
		}
		private void DeleteCurrentProfile()
		{
			lock (settings.hostHistory)
			{
				bool hostExisted = false;
				for (int i = 0; i < settings.hostHistory.Count; i++)
					if (settings.hostHistory[i].host == txtHost.Text && settings.hostHistory[i].preferIpv4 == cbPreferIpv4.Checked)
					{
						hostExisted = true;
						settings.hostHistory.RemoveAt(i);
						settings.Save();
						break;
					}
				if (hostExisted)
				{
					if (settings.hostHistory.Count > 0)
						LoadProfileIntoUI(settings.hostHistory[0]);
				}
			}
		}
		/// <summary>
		/// Adds the current profile to the profile list and saves it to disk. Only if the host field is defined.
		/// </summary>
		private void SaveProfileFromUI()
		{
			if (DateTime.Now < suppressHostSettingsSaveUntil)
				return;
			HostSettings p = new HostSettings();
			p.host = txtHost.Text;
			p.displayName = txtDisplayName.Text;
			p.rate = (int)nudPingsPerSecond.Value;
			p.pingsPerSecond = selectPingsPerSecond.SelectedIndex == 0;
			p.doTraceRoute = cbTraceroute.Checked;
			p.reverseDnsLookup = cbReverseDNS.Checked;
			p.drawServerNames = cbAlwaysShowServerNames.Checked;
			p.drawLastPing = cbLastPing.Checked;
			p.drawAverage = cbAverage.Checked;
			p.drawJitter = cbJitter.Checked;
			p.drawMinMax = cbMinMax.Checked;
			p.drawPacketLoss = cbPacketLoss.Checked;
			p.badThreshold = (int)nudBadThreshold.Value;
			p.worseThreshold = (int)nudWorseThreshold.Value;
			p.preferIpv4 = cbPreferIpv4.Checked;
			p.logFailures = LogFailures;

			if (!string.IsNullOrWhiteSpace(p.host))
			{
				lock (settings.hostHistory)
				{
					if (settings.hostHistory.Count == 0)
						settings.hostHistory.Add(p);
					else
					{
						for (int i = 0; i < settings.hostHistory.Count; i++)
							if (settings.hostHistory[i].host == p.host && settings.hostHistory[i].preferIpv4 == p.preferIpv4)
							{
								settings.hostHistory.RemoveAt(i);
								break;
							}
						settings.hostHistory.Insert(0, p);
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

		private void mi_deleteHost_Click(object sender, EventArgs e)
		{
			DeleteCurrentProfile();
		}
		private string GetTimestamp(DateTime time)
		{
			if (!string.IsNullOrWhiteSpace(settings.customTimeStr))
			{
				try
				{
					return time.ToString(settings.customTimeStr);
				}
				catch { }
			}
			return time.ToString();
		}

		private void menuItem_OpenSettingsFolder_Click(object sender, EventArgs e)
		{
			settings.OpenSettingsFolder();
		}

		CommandLineArgsForm cla_form;
		private void menuItem_CommandLineArgs_Click(object sender, EventArgs e)
		{
			if (cla_form == null)
			{
				cla_form = new CommandLineArgsForm(this);
				cla_form.FormClosed += (sender2, e2) => { cla_form = null; };
				cla_form.Show();
			}
			else
				cla_form.BringToFront();
		}

		private void MainForm_Click(object sender, EventArgs e)
		{
			//this.Focus();
		}


		private void MainForm_MoveOrResize(object sender, EventArgs e)
		{
			RememberCurrentPositionThrottled();
		}

		/// <summary>
		/// Do not call this directly.  Instead, call <see cref="RememberCurrentPositionThrottled"/>.
		/// </summary>
		private void _rememberCurrentPosition()
		{
			if (this.InvokeRequired)
				this.Invoke((Action)_rememberCurrentPosition);
			else
			{
				lock (settings.hostHistory)
				{
					settings.lastWindowParams = new WindowParams(this.Location.X, this.Location.Y, this.Size.Width, this.Size.Height);
					settings.Save();
				}
				lblFailed.Text = (int.Parse(lblFailed.Text) + 1).ToString();
			}
		}
	}
}
