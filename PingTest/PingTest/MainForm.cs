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
using System.Threading.Tasks;
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

		/// <summary>
		/// In-memory log buffer. Access must be synchronized via logBufferLock.
		/// </summary>
		private readonly List<string> logBuffer = new List<string>();
		private readonly object logBufferLock = new object();
		private const int MaxLogLines = 10000;

		/// <summary>
		/// The output log form, created on demand and destroyed when closed.
		/// </summary>
		private OutputLogForm outputLogForm = null;

		/// <summary>
		/// Suppresses config dropdown SelectedIndexChanged events during programmatic updates.
		/// </summary>
		private bool suppressConfigDropdownEvents = false;

		/// <summary>
		/// The currently loaded PingConfiguration, or null if none is loaded.
		/// </summary>
		public PingConfiguration currentConfiguration { get; private set; } = null;
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
		/// Event raised when the currently loaded configuration changes.
		/// </summary>
		public event EventHandler SelectedHostChanged = delegate { };
		/// <summary>
		/// Event raised when the graphs are maximized or restored to the regular window.  See <see cref="graphsMaximized"/>.
		/// </summary>
		public event EventHandler MaximizeGraphsChanged = delegate { };

		/// <summary>
		/// Gets or sets a value indicating whether failures should be logged for the current configuration.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool LogFailures { get; set; } = false;

		/// <summary>
		/// Gets or sets a value indicating whether successes should be logged for the current configuration.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool LogSuccesses { get; set; } = false;
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
			settings.Load();

			// One-time migration from legacy HostSettings to PingConfigurations
			PingConfigurations.MigrateFromSettings(settings);

			// Load PingConfigurations and populate toolbar dropdown
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();
			PopulateConfigDropdown(allConfigs);

			StartupOptions options = new StartupOptions(args);
			PingConfiguration configToLoad = null;

			if (options.StartupHostName != null)
			{
				// Attempt to find a matching configuration by display name
				configToLoad = allConfigs.configurations.FirstOrDefault(c =>
					string.Equals(c.displayName, options.StartupHostName, StringComparison.OrdinalIgnoreCase)
					&& _configMatchesOptions(c, options));
				if (configToLoad == null)
					configToLoad = allConfigs.configurations.FirstOrDefault(c =>
						string.Equals(c.GetHostString(), options.StartupHostName, StringComparison.OrdinalIgnoreCase)
						&& _configMatchesOptions(c, options));
				if (configToLoad == null)
					configToLoad = allConfigs.configurations.FirstOrDefault(c =>
						string.Equals(c.displayName, options.StartupHostName, StringComparison.OrdinalIgnoreCase));
				if (configToLoad == null)
					configToLoad = allConfigs.configurations.FirstOrDefault(c =>
						string.Equals(c.GetHostString(), options.StartupHostName, StringComparison.OrdinalIgnoreCase));

				if (configToLoad == null)
				{
					// Create a new configuration from the startup options
					configToLoad = new PingConfiguration();
					configToLoad.displayName = options.StartupHostName;
					Host h = new Host();
					h.hostname = options.StartupHostName;
					configToLoad.hosts.Add(h);
					if (options.PreferIPv6 != BoolOverride.Inherit)
						configToLoad.preferIPv4 = options.PreferIPv6 == BoolOverride.False;
					if (options.TraceRoute != BoolOverride.Inherit)
						configToLoad.doTraceRoute = options.TraceRoute == BoolOverride.True;
					PingConfigurations.SaveSingleConfiguration(configToLoad);
				}
			}

			if (configToLoad == null && settings.lastLoadedConfigurationGuid != null)
				configToLoad = allConfigs.GetByGuid(settings.lastLoadedConfigurationGuid);

			if (configToLoad == null && allConfigs.configurations.Count > 0)
				configToLoad = allConfigs.configurations[0];

			if (configToLoad != null)
				ApplyConfiguration(configToLoad);
			else
				UpdateStatus("Idle");

			AddKeyDownHandler(this);
			AddClickHandler(this);

			if (options.WindowLocation != null)
			{
				WindowParams wp = options.WindowLocation;

				Size s = this.Size;
				if (wp.W > 0)
					s.Width = wp.W + (settings.osWindowLeftMargin + settings.osWindowRightMargin);
				if (wp.H > 0)
					s.Height = wp.H + (settings.osWindowTopMargin + settings.osWindowBottomMargin);

				this.Location = new Point(wp.X - settings.osWindowLeftMargin, wp.Y - settings.osWindowTopMargin);
				this.Size = s;
			}
			else if (settings.lastWindowParams != null)
			{
				WindowParams wp = settings.lastWindowParams;

				Size s = this.Size;
				if (wp.W > 0)
					s.Width = wp.W;
				if (wp.H > 0)
					s.Height = wp.H;

				this.Location = new Point(wp.X, wp.Y);
				this.Size = s;
			}

			this.MoveOnscreenIfOffscreen();

			if (options.StartPinging)
				btnStart_Click(this, new EventArgs());

			if (options.MaximizeGraphs)
			{
				this.Hide();
				this.BeginInvoke((Action)(() =>
				{
					this.Show();
					SetGraphsMaximizedState(true);
				}));
			}

			// If no configuration was loaded, show the configuration form so the user knows to create one
			if (configToLoad == null)
			{
				this.BeginInvoke((Action)(() => { OpenConfigurationForm(); }));
			}

			this.Move += MainForm_MoveOrResize;
			this.Resize += MainForm_MoveOrResize;
		}

		private bool _configMatchesOptions(PingConfiguration cfg, StartupOptions options)
		{
			return (options.PreferIPv6 == BoolOverride.Inherit || cfg.GetPreferIPv4() == (options.PreferIPv6 == BoolOverride.False))
				&& (options.TraceRoute == BoolOverride.Inherit || cfg.doTraceRoute == (options.TraceRoute == BoolOverride.True));
		}

		/// <summary>
		/// Applies a PingConfiguration to the MainForm, setting up ping parameters.
		/// </summary>
		public void ApplyConfiguration(PingConfiguration cfg)
		{
			currentConfiguration = cfg;
			LogFailures = cfg.logFailures;
			LogSuccesses = cfg.logSuccesses;

			// Remember this as the most recently loaded configuration
			settings.lastLoadedConfigurationGuid = cfg.guid;
			settings.Save();

			UpdateTitleBar(cfg);

			// Sync dropdown selection (reload from disk so newly saved configs are available)
			PopulateConfigDropdownFromDisk();
			SelectConfigInDropdown(cfg.guid);

			UpdateStatus(isRunning ? "Pinging Active" : "Idle");

			// Notify the configuration form (if open) so it can update button states
			configForm?.UpdateLoadButtonState();

			SelectedHostChanged.Invoke(this, EventArgs.Empty);
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
		/// <summary>
		/// 
		/// </summary>
		/// <param name="address"></param>
		/// <param name="preferIpv4"></param>
		/// <param name="hostName">This gets assigned a copy of [address] if DNS was queried to get the IP address.  Null if the IP was simply parsed from [address].</param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>

		private IPAddress StringToIp(string address, bool preferIpv4, out string hostName)
		{
			hostName = null;
			// Parse IP
			try
			{
				if (IPAddress.TryParse(address, out IPAddress tmp))
					return tmp;
			}
			catch (FormatException)
			{
			}

			// Try to resolve host name
			try
			{
				hostName = address;
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
			bool monitorUnresponsiveHops = (bool)args[5];

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
			string statusNote = "";
			try
			{
				string[] addresses = host.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (addresses.Length == 0)
				{
					CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): Unable to start pinging because the host input is empty.");
					return;
				}
				currentIPAddress = addresses[0];
				CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): Initializing pings to " + host);

				// Multiple addresses
				if (addresses.Length > 1)
				{
					// Don't clear dead hosts from a predefined list
					clearedDeadHosts = true;
					foreach (string address in addresses)
					{
						if (self.CancellationPending)
							break;
						try
						{
							IPAddress ip = StringToIp(address.Trim(), preferIpv4, out string hostName);
							if (self.CancellationPending)
								break;
							AddPingTarget(ip, hostName, reverseDnsLookup);
						}
						catch (Exception ex)
						{
							CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): " + ex.Message);
						}
					}
					if (addresses.Length > pingGraphs.Count)
						statusNote = " (" + pingGraphs.Count + "/" + addresses.Length + "; see log)";
				}
				// Route
				else if (traceRoute)
				{
					CreateLogEntry("Tracing route ...");
					UpdateStatus("Tracing Route");
					try
					{
						target = StringToIp(addresses[0], preferIpv4, out string hostName);
					}
					catch (Exception ex)
					{
						CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): " + ex.Message);
						target = null;
					}
					if (target != null)
					{
						foreach (TracertEntry entry in Tracert.Trace(target, 64, 5000, settings.pingPayloadSizeBytes))
						{
							if (self.CancellationPending)
								break;
							CreateLogEntry(entry.ToString());
							AddPingTarget(entry.Address, null, reverseDnsLookup);
						}
					}
				}
				// Single address
				else
				{
					try
					{
						target = StringToIp(addresses[0], preferIpv4, out string hostName);
						AddPingTarget(target, hostName, reverseDnsLookup);
					}
					catch (Exception ex)
					{
						CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): " + ex.Message);
					}
				}

				// If no graphs were added (all resolves failed), show the log and stop
				if (pingGraphs.Count == 0)
				{
					CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): No hosts could be resolved. Pinging will not start.");
					Invoke(self, () =>
					{
						ShowLogMessagesForm();
					});
					return;
				}

				CreateLogEntry("Now beginning pings");
				UpdateStatus("Pinging Active" + statusNote);
				Stopwatch sw = null;
				byte[] buffer = new byte[settings.pingPayloadSizeBytes];

				long numberOfPingLoopIterations = 0;
				DateTime tenPingsAt = DateTime.MinValue;
				while (!self.CancellationPending)
				{
					try
					{
						if (!clearedDeadHosts && tenPingsAt != DateTime.MinValue && tenPingsAt.AddSeconds(10) < DateTime.Now)
						{
							if (pingTargets.Count > 1 && !monitorUnresponsiveHops)
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
							int msToWait = sw == null ? 0 : (int)(pingDelay - sw.ElapsedMilliseconds);
							while (!self.CancellationPending && msToWait > 0)
							{
								Thread.Sleep(Math.Min(msToWait, 100));
								msToWait = sw == null ? 0 : (int)(pingDelay - sw.ElapsedMilliseconds);
							}
							if (!self.CancellationPending)
							{
								if (sw == null)
									sw = Stopwatch.StartNew();
								else
									sw.Restart();
								DateTime lastPingAt = DateTime.Now;
								// We can't re-use the same Ping instance because it is only capable of one ping at a time.
								foreach (KeyValuePair<int, IPAddress> targetMapping in pingTargets)
								{
									PingGraphControl graph = pingGraphs[targetMapping.Key];
									long offset = graph.ClearNextOffset();
									Ping pinger = PingInstancePool.Get();
									pinger.PingCompleted += pinger_PingCompleted;
									if (buffer.Length != settings.pingPayloadSizeBytes)
										buffer = new byte[settings.pingPayloadSizeBytes];
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
					CreateLogEntry("(" + GetTimestamp(DateTime.Now) + "): Error during ping operation: " + ex.Message);
			}
			finally
			{
				if (!string.IsNullOrEmpty(host))
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
					if (clearedDeadHosts && LogFailures && pingTargets.ContainsKey(pingTargetId))
						CreateLogEntry("" + GetTimestamp(time) + ", " + remoteHost.ToString() + ": " + e.Reply.Status.ToString());
				}
				else
				{
					if (!clearedDeadHosts)
					{
						pingTargetHasAtLeastOneSuccess[pingTargetId] = true;
					}
					Interlocked.Increment(ref successfulPings);
					if (LogSuccesses && pingTargets.ContainsKey(pingTargetId))
						CreateLogEntry("" + GetTimestamp(time) + ", " + remoteHost.ToString() + ": " + e.Reply.Status.ToString() + " in " + e.Reply.RoundtripTime + "ms");
				}
			}
			finally
			{
				UpdatePingCounts(Interlocked.Read(ref successfulPings), Interlocked.Read(ref failedPings));
			}
		}
		private void AddPingTarget(IPAddress ipAddress, string name, bool reverseDnsLookup)
		{
			if (ipAddress == null)
				return;
			try
			{
				if (panel_Graphs.InvokeRequired)
					panel_Graphs.Invoke((Action<IPAddress, string, bool>)AddPingTarget, ipAddress, name, reverseDnsLookup);
				else
				{
					int id = graphSortingCounter++;
					PingGraphControl graph = new PingGraphControl(this.settings, ipAddress, name, reverseDnsLookup);

					pingTargets.Add(id, ipAddress);
					pingGraphs.Add(id, graph);
					ResetGraphTimestamps();
					pingTargetHasAtLeastOneSuccess.Add(id, false);

					if (currentConfiguration != null)
					{
						graph.AlwaysShowServerNames = currentConfiguration.drawServerNames;
						graph.Threshold_Bad = currentConfiguration.badThreshold;
						graph.Threshold_Worse = currentConfiguration.worseThreshold;
						graph.upperLimit = currentConfiguration.upperLimit;
						graph.lowerLimit = currentConfiguration.lowerLimit;
						graph.ScalingMethod = (GraphScalingMethod)currentConfiguration.ScalingMethodID;
						graph.ShowLastPing = currentConfiguration.drawLastPing;
						graph.ShowAverage = currentConfiguration.drawAverage;
						graph.ShowJitter = currentConfiguration.drawJitter;
						graph.ShowMinMax = currentConfiguration.drawMinMax;
						graph.ShowPacketLoss = currentConfiguration.drawPacketLoss;
						graph.DrawLimitText = currentConfiguration.drawLimitText;
					}

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
				lock (logBufferLock)
				{
					logBuffer.Add(str);
					while (logBuffer.Count > MaxLogLines)
						logBuffer.RemoveAt(0);
				}
				if (outputLogForm != null)
					outputLogForm.AppendLog(str);
				if (settings.logTextOutputToFile)
				{
					File.AppendAllText(LogFilePath, str + Environment.NewLine);
				}
			}
			catch (Exception)
			{
				didCheckLogsDir = false;
			}
		}
		/// <summary>
		/// Gets the path to the log file appropriate for right now.  Named with year and month.
		/// </summary>
		private string LogFilePath
		{
			get
			{
				string folder = Path.Combine(Settings.SettingsFolderPath, "Logs");
				if (!didCheckLogsDir && !Directory.Exists(folder))
					Directory.CreateDirectory(folder);
				didCheckLogsDir = true;
				return Path.Combine(folder, "PingTracer-Log-" + DateTime.Now.ToString("yyyy-MM") + ".txt");
			}
		}
		bool didCheckLogsDir = false;

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
			if (e.Cancel)
				return;
			if (isRunning)
			{
				btnStart_Click(btnStart, new EventArgs());
			}
			if (outputLogForm != null)
			{
				outputLogForm.FormClosed -= OutputLogForm_FormClosed;
				outputLogForm.Dispose();
				outputLogForm = null;
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
			int bottomBorderSize = 1;
			IList<int> keys = pingGraphs.Keys;
			int widthAvailable = panel_Graphs.Width;
			int timestampsHeight = pingGraphs[keys[0]].TimestampsHeight;
			// Compute total height available for graphs.  Pretend there is 1 extra bottom border height available.  It is for the bottom graph's bottom border, which it will not use.
			int heightAvailable = (panel_Graphs.Height - timestampsHeight) + bottomBorderSize;
			int perGraphHeightIncludingBorder = heightAvailable / pingGraphs.Count;
			// Number of pixels left-over after giving every graph an equal number of integer pixels. It will be assigned equally to the graphs as extra height.
			// This is necessary because integer division rounds down, so there is often a remainder.
			int leftoverSpace = (heightAvailable - (perGraphHeightIncludingBorder * keys.Count));
			int[] heights = new int[keys.Count];
			// Compute heights for each graph, since they will not be exactly equal.
			for (int i = heights.Length - 1; i >= 0; i--)
			{
				heights[i] = perGraphHeightIncludingBorder;
				if (i == heights.Length - 1)
				{
					// Bottom graph gets all the timestamp section height, because it needs that extra height to draw the timestamps.
					heights[i] += timestampsHeight;
				}
				if (leftoverSpace > 0)
				{
					// There's some leftover space.  Assign one pixel of it to this graph (loop starts with bottom graph and iterates up).
					heights[i] += 1;
					leftoverSpace--;
				}
			}
			int heightConsumed = 0;
			for (int i = 0; i < keys.Count; i++)
			{
				int thisGraphHeight = heights[i]; // Height including 1px bottom border.
				PingGraphControl graph = pingGraphs[keys[i]];
				graph.SetBounds(0, heightConsumed, widthAvailable, thisGraphHeight - 1);
				heightConsumed += thisGraphHeight;
			}
		}

		#region Form input changed/clicked events

		private void btnStart_Click(object sender, EventArgs e)
		{
			if (this.InvokeRequired)
			{
				this.BeginInvoke((Action<object, EventArgs>)btnStart_Click, sender, e);
				return;
			}
			btnStart.Enabled = false;
			if (isRunning)
			{
				isRunning = false;
				btnStart.Text = "Click to Start";
				cbConfigurations.Enabled = true;
				controllerWorker.CancelAsync();
				UpdateStatus("Idle");
				StoppedPinging.Invoke(sender, e);
			}
			else
			{
				if (currentConfiguration == null)
				{
					MessageBox.Show("No configuration is loaded. Please select a configuration first.");
					btnStart.Enabled = true;
					return;
				}
				isRunning = true;
				btnStart.Text = "Click to Stop";
				cbConfigurations.Enabled = false;
				controllerWorker = new BackgroundWorker();
				controllerWorker.WorkerSupportsCancellation = true;
				controllerWorker.DoWork += ControllerWorker_DoWork;
				controllerWorker.RunWorkerCompleted += ControllerWorker_RunWorkerCompleted;

				string host = currentConfiguration.GetHostString();
				bool traceRoute = currentConfiguration.doTraceRoute;
				bool reverseDnsLookup = currentConfiguration.reverseDnsLookup;
				bool preferIpv4 = currentConfiguration.GetPreferIPv4();

				if (currentConfiguration.rate == 0)
					pingDelay = 0;
				else if (currentConfiguration.pingsPerSecond)
					pingDelay = Math.Max(100, (int)(1000 / currentConfiguration.rate));
				else
					pingDelay = Math.Max(100, (int)(1000 * currentConfiguration.rate));

				if (currentConfiguration.hosts != null && currentConfiguration.hosts.Count > 1)
					traceRoute = false;

				UpdateStatus("Starting...");
				controllerWorker.RunWorkerAsync(new object[] { controllerWorker, host, traceRoute, reverseDnsLookup, preferIpv4, currentConfiguration.monitorUnresponsiveHops });
				StartedPinging.Invoke(sender, e);
			}
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
			SetGraphsMaximizedState(!graphsMaximized);
		}
		private static int? graphsTopOffset = null;
		private static int? graphsBottomOffset = null;
		private void SetGraphsMaximizedState(bool maximize)
		{
			if (maximize)
			{
				if (graphsTopOffset == null)
					graphsTopOffset = panel_Graphs.Bounds.Top;
				if (graphsBottomOffset == null)
					graphsBottomOffset = this.ClientSize.Height - panel_Graphs.Bounds.Bottom;
				graphsMaximized = true;
				panelForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
				panelForm.Controls.Add(panel_Graphs);
				panel_Graphs.Dock = DockStyle.Fill;
				panelForm.Show();
				panelForm.SetBounds(this.Left + settings.osWindowLeftMargin, this.Top + settings.osWindowTopMargin, this.Width - (settings.osWindowLeftMargin + settings.osWindowRightMargin), this.Height - settings.osWindowBottomMargin);
				this.Hide();
				MaximizeGraphsChanged.Invoke(this, EventArgs.Empty);
			}
			else
			{
				if (graphsTopOffset == null)
					graphsTopOffset = 55;
				if (graphsBottomOffset == null)
					graphsBottomOffset = 19;
				graphsMaximized = false;
				panel_Graphs.Dock = DockStyle.None;
				this.Controls.Add(panel_Graphs);
				panel_Graphs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
				panel_Graphs.SetBounds(0, graphsTopOffset.Value, this.ClientSize.Width, this.ClientSize.Height - graphsTopOffset.Value - 20);
				this.Show();
				panelForm.Hide();
				MaximizeGraphsChanged.Invoke(this, EventArgs.Empty);
			}
		}
		#endregion

		#region Configuration

		ConfigurationForm configForm = null;
		/// <summary>
		/// Opens the Configuration Editor form positioned adjacent to the MainForm.
		/// </summary>
		public void OpenConfigurationForm()
		{
			OpenConfigurationForm(currentConfiguration?.guid);
		}
		/// <summary>
		/// Opens the Configuration Editor form positioned adjacent to the MainForm,
		/// selecting the configuration with the given guid.
		/// </summary>
		public void OpenConfigurationForm(string selectGuid)
		{
			if (configForm != null)
			{
				if (selectGuid != null)
					configForm.SelectConfiguration(selectGuid);
				configForm.BringToFront();
				return;
			}
			configForm = new ConfigurationForm(this);
			this.AddOwnedForm(configForm);
			configForm.ConfigurationLoaded += (cfg) =>
			{
				if (isRunning)
				{
					MessageBox.Show("Cannot load a configuration while pings are running." + Environment.NewLine + "Please stop the pings first.");
					return;
				}
				ApplyConfiguration(cfg);
				configForm.UpdateLoadButtonState();
			};
			configForm.ConfigurationSaved += (cfg) =>
			{
				// Live-apply settings that can safely change while running
				ApplyLiveConfigChanges(cfg);
			};
			configForm.ConfigurationEdited += (cfg) =>
			{
				// Live-apply edited settings immediately (before save)
				ApplyLiveConfigChanges(cfg);
			};
			configForm.ConfigurationEditDiscarded += (cfg) =>
			{
				// Revert MainForm to the saved state
				ApplyLiveConfigChanges(cfg);
			};
			configForm.FormClosed += (sender2, e2) =>
			{
				this.RemoveOwnedForm(configForm);
				configForm = null;
				PopulateConfigDropdownFromDisk();
			};
			configForm.PositionAdjacentTo(this);
			configForm.Show();
			// Select the configuration AFTER Show so the tree is populated
			if (selectGuid != null)
				configForm.SelectConfiguration(selectGuid);
			else if (currentConfiguration != null)
				configForm.SelectConfiguration(currentConfiguration.guid);
		}

		private void mi_Configuration_Click(object sender, EventArgs e)
		{
			OpenConfigurationForm();
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
			optionsForm.PositionCenteredOn(this);
			optionsForm.Show();
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
				this.AddOwnedForm(cla_form);
				cla_form.FormClosed += (sender2, e2) =>
				{
					this.RemoveOwnedForm(cla_form);
					cla_form = null;
				};
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
				settings.lastWindowParams = new WindowParams(this.Location.X, this.Location.Y, this.Size.Width, this.Size.Height);
				settings.Save();
			}
		}

		private void menuItem_resetWindowSize_Click(object sender, EventArgs e)
		{
			this.Size = defaultWindowSize;
		}

		#region ToolStrip Configuration Dropdown

		/// <summary>
		/// Populates the configuration dropdown from a loaded PingConfigurations object.
		/// </summary>
		private void PopulateConfigDropdown(PingConfigurations allConfigs)
		{
			suppressConfigDropdownEvents = true;
			try
			{
				cbConfigurations.Items.Clear();
				foreach (PingConfiguration cfg in allConfigs.configurations)
					cbConfigurations.Items.Add(new ConfigDropdownItem(cfg.guid, cfg.displayName));

				if (currentConfiguration != null)
					SelectConfigInDropdown(currentConfiguration.guid);
			}
			finally
			{
				suppressConfigDropdownEvents = false;
			}
		}

		/// <summary>
		/// Reloads configurations from disk and populates the dropdown.
		/// </summary>
		private void PopulateConfigDropdownFromDisk()
		{
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();
			PopulateConfigDropdown(allConfigs);
		}

		private void SelectConfigInDropdown(string guid)
		{
			suppressConfigDropdownEvents = true;
			try
			{
				for (int i = 0; i < cbConfigurations.Items.Count; i++)
				{
					ConfigDropdownItem item = cbConfigurations.Items[i] as ConfigDropdownItem;
					if (item != null && item.Guid == guid)
					{
						cbConfigurations.SelectedIndex = i;
						return;
					}
				}
			}
			finally
			{
				suppressConfigDropdownEvents = false;
			}
		}

		private void cbConfigurations_DropDown(object sender, EventArgs e)
		{
			// Refresh the list from disk every time the dropdown is expanded
			PopulateConfigDropdownFromDisk();
		}

		private void cbConfigurations_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (suppressConfigDropdownEvents)
				return;
			ConfigDropdownItem item = cbConfigurations.SelectedItem as ConfigDropdownItem;
			if (item == null)
				return;
			// Load the selected configuration from disk
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();
			PingConfiguration cfg = allConfigs.GetByGuid(item.Guid);
			if (cfg != null)
				ApplyConfiguration(cfg);
		}

		private void tsbEdit_Click(object sender, EventArgs e)
		{
			ConfigDropdownItem item = cbConfigurations.SelectedItem as ConfigDropdownItem;
			string guid = item?.Guid ?? currentConfiguration?.guid;
			OpenConfigurationForm(guid);
		}

		/// <summary>
		/// Helper class for items in the configuration dropdown.
		/// </summary>
		private class ConfigDropdownItem
		{
			public string Guid { get; }
			public string DisplayName { get; }
			public ConfigDropdownItem(string guid, string displayName)
			{
				Guid = guid;
				DisplayName = displayName;
			}
			public override string ToString() { return DisplayName; }
		}

		#endregion

		#region Status Label

		private void UpdateStatus(string status)
		{
			try
			{
				if (lblStatus.InvokeRequired)
					lblStatus.Invoke(new Action<string>(UpdateStatus), status);
				else
					lblStatus.Text = status;
			}
			catch (Exception)
			{
			}
		}

		#endregion

		#region Log Messages

		private void mi_OutputLog_Click(object sender, EventArgs e)
		{
			ShowLogMessagesForm();
		}

		/// <summary>
		/// Opens or brings to front the Log Messages form, centered on MainForm and nudged on-screen.
		/// </summary>
		private void ShowLogMessagesForm()
		{
			if (outputLogForm == null || outputLogForm.IsDisposed)
			{
				outputLogForm = new OutputLogForm();
				this.AddOwnedForm(outputLogForm);
				outputLogForm.FormClosed += OutputLogForm_FormClosed;
				// Populate with existing log buffer
				string[] snapshot;
				lock (logBufferLock)
				{
					snapshot = logBuffer.ToArray();
				}
				outputLogForm.LoadLines(snapshot);
				outputLogForm.PositionCenteredOn(this);
				outputLogForm.Show();
			}
			else
			{
				outputLogForm.Show();
				outputLogForm.BringToFront();
			}
		}

		private void OutputLogForm_FormClosed(object sender, FormClosedEventArgs e)
		{
			this.RemoveOwnedForm(outputLogForm);
			outputLogForm = null;
		}

		#endregion

		#region Live Config Apply

		/// <summary>
		/// Applies configuration changes that can safely take effect while pinging is active.
		/// This includes ping rate, graph options, and logging settings.
		/// Also updates non-live properties (hosts, traceRoute, reverseDNS, preferIPv4) on 
		/// currentConfiguration so they take effect if pinging is stopped and restarted.
		/// </summary>
		public void ApplyLiveConfigChanges(PingConfiguration cfg)
		{
			if (currentConfiguration == null || cfg.guid != currentConfiguration.guid)
				return;

			// Update ping rate
			if (cfg.rate == 0)
				pingDelay = 0;
			else if (cfg.pingsPerSecond)
				pingDelay = Math.Max(100, (int)(1000 / cfg.rate));
			else
				pingDelay = Math.Max(100, (int)(1000 * cfg.rate));

			// Update logging flags
			LogFailures = cfg.logFailures;
			LogSuccesses = cfg.logSuccesses;
			currentConfiguration.logFailures = cfg.logFailures;
			currentConfiguration.logSuccesses = cfg.logSuccesses;

			// Update ping rate in current config
			currentConfiguration.rate = cfg.rate;
			currentConfiguration.pingsPerSecond = cfg.pingsPerSecond;

			// Update non-live properties (take effect on next start)
			currentConfiguration.hosts = cfg.hosts;
			currentConfiguration.doTraceRoute = cfg.doTraceRoute;
			currentConfiguration.reverseDnsLookup = cfg.reverseDnsLookup;
			currentConfiguration.preferIPv4 = cfg.preferIPv4;
			currentConfiguration.monitorUnresponsiveHops = cfg.monitorUnresponsiveHops;
			currentConfiguration.displayName = cfg.displayName;

			// Update graph options on all live graphs
			currentConfiguration.drawServerNames = cfg.drawServerNames;
			currentConfiguration.drawLastPing = cfg.drawLastPing;
			currentConfiguration.drawAverage = cfg.drawAverage;
			currentConfiguration.drawJitter = cfg.drawJitter;
			currentConfiguration.drawMinMax = cfg.drawMinMax;
			currentConfiguration.drawPacketLoss = cfg.drawPacketLoss;
			currentConfiguration.drawLimitText = cfg.drawLimitText;
			currentConfiguration.badThreshold = cfg.badThreshold;
			currentConfiguration.worseThreshold = cfg.worseThreshold;
			currentConfiguration.upperLimit = cfg.upperLimit;
			currentConfiguration.lowerLimit = cfg.lowerLimit;
			currentConfiguration.ScalingMethodID = cfg.ScalingMethodID;

			foreach (PingGraphControl graph in pingGraphs.Values)
			{
				graph.AlwaysShowServerNames = cfg.drawServerNames;
				graph.Threshold_Bad = cfg.badThreshold;
				graph.Threshold_Worse = cfg.worseThreshold;
				graph.upperLimit = cfg.upperLimit;
				graph.lowerLimit = cfg.lowerLimit;
				graph.ScalingMethod = (GraphScalingMethod)cfg.ScalingMethodID;
				graph.ShowLastPing = cfg.drawLastPing;
				graph.ShowAverage = cfg.drawAverage;
				graph.ShowJitter = cfg.drawJitter;
				graph.ShowMinMax = cfg.drawMinMax;
				graph.ShowPacketLoss = cfg.drawPacketLoss;
				graph.DrawLimitText = cfg.drawLimitText;
				graph.InvalidateSync();
			}

			UpdateTitleBar(cfg);
		}

		private void UpdateTitleBar(PingConfiguration cfg)
		{
			string baseTitle = "Ping Tracer " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
			if (!string.IsNullOrWhiteSpace(cfg.displayName))
				this.Text = cfg.displayName + " - " + baseTitle;
			else
				this.Text = baseTitle;
			panelForm.Text = this.Text;
		}

		#endregion
	}
}
