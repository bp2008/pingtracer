namespace PingTracer
{
	partial class ConfigurationForm
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		private void InitializeComponent()
		{
			components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ConfigurationForm));
			splitContainerMain = new System.Windows.Forms.SplitContainer();
			treeConfigurations = new System.Windows.Forms.TreeView();
			panelEditor = new System.Windows.Forms.Panel();
			lblTitle = new System.Windows.Forms.Label();
			txtDisplayName = new System.Windows.Forms.TextBox();
			lblUniqueWarning = new System.Windows.Forms.Label();
			lblHosts = new System.Windows.Forms.Label();
			txtHosts = new System.Windows.Forms.TextBox();
			cbPreferIPv4 = new System.Windows.Forms.CheckBox();
			grpMonitoring = new System.Windows.Forms.GroupBox();
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings = new System.Windows.Forms.CheckBox();
			cbTraceroute = new System.Windows.Forms.CheckBox();
			lblTracerouteWarning = new System.Windows.Forms.Label();
			cbReverseDNS = new System.Windows.Forms.CheckBox();
			lblRate = new System.Windows.Forms.Label();
			lblRateValue = new System.Windows.Forms.Label();
			trackBarRate = new System.Windows.Forms.TrackBar();
			grpGraphOptions = new System.Windows.Forms.GroupBox();
			cbAlwaysShowServerNames = new System.Windows.Forms.CheckBox();
			cbPacketLoss = new System.Windows.Forms.CheckBox();
			cbDrawLimits = new System.Windows.Forms.CheckBox();
			cbLastPing = new System.Windows.Forms.CheckBox();
			cbAverage = new System.Windows.Forms.CheckBox();
			cbJitter = new System.Windows.Forms.CheckBox();
			cbMinMax = new System.Windows.Forms.CheckBox();
			lblBadThreshold = new System.Windows.Forms.Label();
			nudBadThreshold = new System.Windows.Forms.NumericUpDown();
			lblWorseThreshold = new System.Windows.Forms.Label();
			nudWorseThreshold = new System.Windows.Forms.NumericUpDown();
			lblUpperLimit = new System.Windows.Forms.Label();
			nudUpLimit = new System.Windows.Forms.NumericUpDown();
			lblLowerLimit = new System.Windows.Forms.Label();
			nudLowLimit = new System.Windows.Forms.NumericUpDown();
			lblScalingMethod = new System.Windows.Forms.Label();
			cbScalingMethod = new System.Windows.Forms.ComboBox();
			grpLogging = new System.Windows.Forms.GroupBox();
			cbLogFailures = new System.Windows.Forms.CheckBox();
			cbLogSuccesses = new System.Windows.Forms.CheckBox();
			btnSave = new System.Windows.Forms.Button();
			btnDiscard = new System.Windows.Forms.Button();
			btnDelete = new System.Windows.Forms.Button();
			btnLoad = new System.Windows.Forms.Button();
			btnClone = new System.Windows.Forms.Button();
			toolTip1 = new System.Windows.Forms.ToolTip(components);
			((System.ComponentModel.ISupportInitialize)splitContainerMain).BeginInit();
			splitContainerMain.Panel1.SuspendLayout();
			splitContainerMain.Panel2.SuspendLayout();
			splitContainerMain.SuspendLayout();
			panelEditor.SuspendLayout();
			grpMonitoring.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)trackBarRate).BeginInit();
			grpGraphOptions.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudBadThreshold).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudWorseThreshold).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudUpLimit).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudLowLimit).BeginInit();
			grpLogging.SuspendLayout();
			SuspendLayout();
			// 
			// splitContainerMain
			// 
			splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
			splitContainerMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			splitContainerMain.Location = new System.Drawing.Point(0, 0);
			splitContainerMain.Name = "splitContainerMain";
			// 
			// splitContainerMain.Panel1
			// 
			splitContainerMain.Panel1.Controls.Add(treeConfigurations);
			splitContainerMain.Panel1MinSize = 150;
			// 
			// splitContainerMain.Panel2
			// 
			splitContainerMain.Panel2.AutoScroll = true;
			splitContainerMain.Panel2.Controls.Add(panelEditor);
			splitContainerMain.Size = new System.Drawing.Size(840, 489);
			splitContainerMain.SplitterDistance = 200;
			splitContainerMain.TabIndex = 0;
			// 
			// treeConfigurations
			// 
			treeConfigurations.Dock = System.Windows.Forms.DockStyle.Fill;
			treeConfigurations.HideSelection = false;
			treeConfigurations.Location = new System.Drawing.Point(0, 0);
			treeConfigurations.Name = "treeConfigurations";
			treeConfigurations.Size = new System.Drawing.Size(200, 489);
			treeConfigurations.TabIndex = 0;
			treeConfigurations.AfterSelect += treeConfigurations_AfterSelect;
			// 
			// panelEditor
			// 
			panelEditor.AutoScroll = true;
			panelEditor.Controls.Add(lblTitle);
			panelEditor.Controls.Add(txtDisplayName);
			panelEditor.Controls.Add(lblUniqueWarning);
			panelEditor.Controls.Add(lblHosts);
			panelEditor.Controls.Add(txtHosts);
			panelEditor.Controls.Add(cbPreferIPv4);
			panelEditor.Controls.Add(grpMonitoring);
			panelEditor.Controls.Add(grpGraphOptions);
			panelEditor.Controls.Add(grpLogging);
			panelEditor.Controls.Add(btnSave);
			panelEditor.Controls.Add(btnDiscard);
			panelEditor.Controls.Add(btnDelete);
			panelEditor.Controls.Add(btnLoad);
			panelEditor.Controls.Add(btnClone);
			panelEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			panelEditor.Location = new System.Drawing.Point(0, 0);
			panelEditor.Name = "panelEditor";
			panelEditor.Size = new System.Drawing.Size(636, 489);
			panelEditor.TabIndex = 0;
			// 
			// lblTitle
			// 
			lblTitle.AutoSize = true;
			lblTitle.Location = new System.Drawing.Point(8, 11);
			lblTitle.Name = "lblTitle";
			lblTitle.Size = new System.Drawing.Size(83, 15);
			lblTitle.TabIndex = 0;
			lblTitle.Text = "Display Name:";
			// 
			// txtDisplayName
			// 
			txtDisplayName.Location = new System.Drawing.Point(92, 8);
			txtDisplayName.Name = "txtDisplayName";
			txtDisplayName.Size = new System.Drawing.Size(250, 23);
			txtDisplayName.TabIndex = 1;
			toolTip1.SetToolTip(txtDisplayName, "A unique name to identify this configuration.");
			txtDisplayName.TextChanged += txtDisplayName_TextChanged;
			// 
			// lblUniqueWarning
			// 
			lblUniqueWarning.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			lblUniqueWarning.ForeColor = System.Drawing.Color.Red;
			lblUniqueWarning.Location = new System.Drawing.Point(348, 11);
			lblUniqueWarning.Name = "lblUniqueWarning";
			lblUniqueWarning.Size = new System.Drawing.Size(272, 15);
			lblUniqueWarning.TabIndex = 2;
			// 
			// lblHosts
			// 
			lblHosts.AutoSize = true;
			lblHosts.Location = new System.Drawing.Point(8, 35);
			lblHosts.Name = "lblHosts";
			lblHosts.Size = new System.Drawing.Size(113, 15);
			lblHosts.TabIndex = 3;
			lblHosts.Text = "Hosts (one per line):";
			// 
			// txtHosts
			// 
			txtHosts.AcceptsReturn = true;
			txtHosts.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			txtHosts.Location = new System.Drawing.Point(11, 51);
			txtHosts.Multiline = true;
			txtHosts.Name = "txtHosts";
			txtHosts.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			txtHosts.Size = new System.Drawing.Size(610, 116);
			txtHosts.TabIndex = 4;
			toolTip1.SetToolTip(txtHosts, "Enter host names or IP addresses, one per line.\r\nComma or space separated entries are also accepted and will be normalized.");
			txtHosts.WordWrap = false;
			txtHosts.TextChanged += txtHosts_TextChanged;
			txtHosts.Leave += txtHosts_Leave;
			// 
			// cbPreferIPv4
			// 
			cbPreferIPv4.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			cbPreferIPv4.AutoSize = true;
			cbPreferIPv4.Checked = true;
			cbPreferIPv4.CheckState = System.Windows.Forms.CheckState.Checked;
			cbPreferIPv4.Location = new System.Drawing.Point(9, 171);
			cbPreferIPv4.Name = "cbPreferIPv4";
			cbPreferIPv4.Size = new System.Drawing.Size(82, 19);
			cbPreferIPv4.TabIndex = 5;
			cbPreferIPv4.Text = "Prefer IPv4";
			toolTip1.SetToolTip(cbPreferIPv4, "If checked and DNS returns both IPv4 and IPv6 addresses, the IPv4 address will be used.");
			cbPreferIPv4.UseVisualStyleBackColor = true;
			cbPreferIPv4.CheckedChanged += AnyControl_Changed;
			// 
			// grpMonitoring
			// 
			grpMonitoring.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			grpMonitoring.Controls.Add(cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings);
			grpMonitoring.Controls.Add(cbTraceroute);
			grpMonitoring.Controls.Add(lblTracerouteWarning);
			grpMonitoring.Controls.Add(cbReverseDNS);
			grpMonitoring.Controls.Add(lblRate);
			grpMonitoring.Controls.Add(lblRateValue);
			grpMonitoring.Controls.Add(trackBarRate);
			grpMonitoring.Location = new System.Drawing.Point(8, 196);
			grpMonitoring.Name = "grpMonitoring";
			grpMonitoring.Size = new System.Drawing.Size(612, 91);
			grpMonitoring.TabIndex = 6;
			grpMonitoring.TabStop = false;
			grpMonitoring.Text = "Monitoring Options";
			// 
			// cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings
			// 
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.AutoSize = true;
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Checked = true;
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.CheckState = System.Windows.Forms.CheckState.Checked;
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Location = new System.Drawing.Point(10, 65);
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Name = "cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings";
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Size = new System.Drawing.Size(406, 19);
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.TabIndex = 25;
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Text = "Stop monitoring intermediate hops that do not respond to regular pings";
			toolTip1.SetToolTip(cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings, resources.GetString("cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.ToolTip"));
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.UseVisualStyleBackColor = true;
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.CheckedChanged += cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings_CheckedChanged;
			// 
			// cbTraceroute
			// 
			cbTraceroute.AutoSize = true;
			cbTraceroute.Checked = true;
			cbTraceroute.CheckState = System.Windows.Forms.CheckState.Checked;
			cbTraceroute.Location = new System.Drawing.Point(10, 19);
			cbTraceroute.Name = "cbTraceroute";
			cbTraceroute.Size = new System.Drawing.Size(88, 19);
			cbTraceroute.TabIndex = 0;
			cbTraceroute.Text = "Trace Route";
			toolTip1.SetToolTip(cbTraceroute, "If checked, a traceroute operation will be performed\r\nand each hop along the route will be monitored.\r\nDisabled when multiple hosts are entered.");
			cbTraceroute.UseVisualStyleBackColor = true;
			cbTraceroute.CheckedChanged += cbTraceroute_CheckedChanged;
			// 
			// lblTracerouteWarning
			// 
			lblTracerouteWarning.AutoSize = true;
			lblTracerouteWarning.ForeColor = System.Drawing.Color.Gray;
			lblTracerouteWarning.Location = new System.Drawing.Point(100, 20);
			lblTracerouteWarning.Name = "lblTracerouteWarning";
			lblTracerouteWarning.Size = new System.Drawing.Size(0, 15);
			lblTracerouteWarning.TabIndex = 1;
			// 
			// cbReverseDNS
			// 
			cbReverseDNS.AutoSize = true;
			cbReverseDNS.Checked = true;
			cbReverseDNS.CheckState = System.Windows.Forms.CheckState.Checked;
			cbReverseDNS.Location = new System.Drawing.Point(10, 42);
			cbReverseDNS.Name = "cbReverseDNS";
			cbReverseDNS.Size = new System.Drawing.Size(135, 19);
			cbReverseDNS.TabIndex = 2;
			cbReverseDNS.Text = "Reverse DNS Lookup";
			toolTip1.SetToolTip(cbReverseDNS, "If checked, reverse DNS lookups are performed on each IP address to find the host name.");
			cbReverseDNS.UseVisualStyleBackColor = true;
			cbReverseDNS.CheckedChanged += AnyControl_Changed;
			// 
			// lblRate
			// 
			lblRate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			lblRate.AutoSize = true;
			lblRate.Location = new System.Drawing.Point(313, 20);
			lblRate.Name = "lblRate";
			lblRate.Size = new System.Drawing.Size(60, 15);
			lblRate.TabIndex = 3;
			lblRate.Text = "Ping Rate:";
			// 
			// lblRateValue
			// 
			lblRateValue.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			lblRateValue.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
			lblRateValue.Location = new System.Drawing.Point(294, 42);
			lblRateValue.Name = "lblRateValue";
			lblRateValue.Size = new System.Drawing.Size(76, 13);
			lblRateValue.TabIndex = 5;
			lblRateValue.Text = "1 ping/sec";
			lblRateValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
			// 
			// trackBarRate
			// 
			trackBarRate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			trackBarRate.Location = new System.Drawing.Point(376, 14);
			trackBarRate.Minimum = -10;
			trackBarRate.Name = "trackBarRate";
			trackBarRate.Size = new System.Drawing.Size(230, 45);
			trackBarRate.TabIndex = 4;
			toolTip1.SetToolTip(trackBarRate, "Positive = pings/sec, Negative = sec/ping, Zero = paused.\r\nA rate of 1 ping per second is recommended for long-term monitoring.");
			trackBarRate.Value = 1;
			trackBarRate.ValueChanged += trackBarRate_ValueChanged;
			// 
			// grpGraphOptions
			// 
			grpGraphOptions.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			grpGraphOptions.Controls.Add(cbAlwaysShowServerNames);
			grpGraphOptions.Controls.Add(cbPacketLoss);
			grpGraphOptions.Controls.Add(cbDrawLimits);
			grpGraphOptions.Controls.Add(cbLastPing);
			grpGraphOptions.Controls.Add(cbAverage);
			grpGraphOptions.Controls.Add(cbJitter);
			grpGraphOptions.Controls.Add(cbMinMax);
			grpGraphOptions.Controls.Add(lblBadThreshold);
			grpGraphOptions.Controls.Add(nudBadThreshold);
			grpGraphOptions.Controls.Add(lblWorseThreshold);
			grpGraphOptions.Controls.Add(nudWorseThreshold);
			grpGraphOptions.Controls.Add(lblUpperLimit);
			grpGraphOptions.Controls.Add(nudUpLimit);
			grpGraphOptions.Controls.Add(lblLowerLimit);
			grpGraphOptions.Controls.Add(nudLowLimit);
			grpGraphOptions.Controls.Add(lblScalingMethod);
			grpGraphOptions.Controls.Add(cbScalingMethod);
			grpGraphOptions.Location = new System.Drawing.Point(8, 293);
			grpGraphOptions.Name = "grpGraphOptions";
			grpGraphOptions.Size = new System.Drawing.Size(612, 100);
			grpGraphOptions.TabIndex = 7;
			grpGraphOptions.TabStop = false;
			grpGraphOptions.Text = "Graph Options";
			// 
			// cbAlwaysShowServerNames
			// 
			cbAlwaysShowServerNames.AutoSize = true;
			cbAlwaysShowServerNames.Checked = true;
			cbAlwaysShowServerNames.CheckState = System.Windows.Forms.CheckState.Checked;
			cbAlwaysShowServerNames.Location = new System.Drawing.Point(10, 19);
			cbAlwaysShowServerNames.Name = "cbAlwaysShowServerNames";
			cbAlwaysShowServerNames.Size = new System.Drawing.Size(98, 19);
			cbAlwaysShowServerNames.TabIndex = 0;
			cbAlwaysShowServerNames.Text = "Server Names";
			toolTip1.SetToolTip(cbAlwaysShowServerNames, "If checked, each server's name/address is overlaid on its graph.");
			cbAlwaysShowServerNames.UseVisualStyleBackColor = true;
			cbAlwaysShowServerNames.CheckedChanged += AnyControl_Changed;
			// 
			// cbPacketLoss
			// 
			cbPacketLoss.AutoSize = true;
			cbPacketLoss.Checked = true;
			cbPacketLoss.CheckState = System.Windows.Forms.CheckState.Checked;
			cbPacketLoss.Location = new System.Drawing.Point(114, 19);
			cbPacketLoss.Name = "cbPacketLoss";
			cbPacketLoss.Size = new System.Drawing.Size(100, 19);
			cbPacketLoss.TabIndex = 1;
			cbPacketLoss.Text = "Packet Loss %";
			toolTip1.SetToolTip(cbPacketLoss, "If checked, each graph's visible packet loss is overlaid as a percentage.");
			cbPacketLoss.UseVisualStyleBackColor = true;
			cbPacketLoss.CheckedChanged += AnyControl_Changed;
			// 
			// cbDrawLimits
			// 
			cbDrawLimits.AutoSize = true;
			cbDrawLimits.Location = new System.Drawing.Point(220, 19);
			cbDrawLimits.Name = "cbDrawLimits";
			cbDrawLimits.Size = new System.Drawing.Size(58, 19);
			cbDrawLimits.TabIndex = 2;
			cbDrawLimits.Text = "Limits";
			toolTip1.SetToolTip(cbDrawLimits, "If checked, each graph's vertical limits are drawn on the right side.");
			cbDrawLimits.UseVisualStyleBackColor = true;
			cbDrawLimits.CheckedChanged += AnyControl_Changed;
			// 
			// cbLastPing
			// 
			cbLastPing.AutoSize = true;
			cbLastPing.Checked = true;
			cbLastPing.CheckState = System.Windows.Forms.CheckState.Checked;
			cbLastPing.Location = new System.Drawing.Point(10, 42);
			cbLastPing.Name = "cbLastPing";
			cbLastPing.Size = new System.Drawing.Size(74, 19);
			cbLastPing.TabIndex = 3;
			cbLastPing.Text = "Last Ping";
			toolTip1.SetToolTip(cbLastPing, "If checked, the most recent ping response time is overlaid in text form.");
			cbLastPing.UseVisualStyleBackColor = true;
			cbLastPing.CheckedChanged += AnyControl_Changed;
			// 
			// cbAverage
			// 
			cbAverage.AutoSize = true;
			cbAverage.Checked = true;
			cbAverage.CheckState = System.Windows.Forms.CheckState.Checked;
			cbAverage.Location = new System.Drawing.Point(90, 42);
			cbAverage.Name = "cbAverage";
			cbAverage.Size = new System.Drawing.Size(69, 19);
			cbAverage.TabIndex = 4;
			cbAverage.Text = "Average";
			toolTip1.SetToolTip(cbAverage, "If checked, the average response time (of visible ping times) is overlaid in text form.");
			cbAverage.UseVisualStyleBackColor = true;
			cbAverage.CheckedChanged += AnyControl_Changed;
			// 
			// cbJitter
			// 
			cbJitter.AutoSize = true;
			cbJitter.Location = new System.Drawing.Point(165, 42);
			cbJitter.Name = "cbJitter";
			cbJitter.Size = new System.Drawing.Size(51, 19);
			cbJitter.TabIndex = 5;
			cbJitter.Text = "Jitter";
			toolTip1.SetToolTip(cbJitter, "If checked, the jitter (across visible ping times) is overlaid in text form.");
			cbJitter.UseVisualStyleBackColor = true;
			cbJitter.CheckedChanged += AnyControl_Changed;
			// 
			// cbMinMax
			// 
			cbMinMax.AutoSize = true;
			cbMinMax.Location = new System.Drawing.Point(222, 42);
			cbMinMax.Name = "cbMinMax";
			cbMinMax.Size = new System.Drawing.Size(80, 19);
			cbMinMax.TabIndex = 6;
			cbMinMax.Text = "Min / Max";
			toolTip1.SetToolTip(cbMinMax, "If checked, the shortest and longest visible ping times are overlaid in text form.");
			cbMinMax.UseVisualStyleBackColor = true;
			cbMinMax.CheckedChanged += AnyControl_Changed;
			// 
			// lblBadThreshold
			// 
			lblBadThreshold.AutoSize = true;
			lblBadThreshold.Location = new System.Drawing.Point(308, 20);
			lblBadThreshold.Name = "lblBadThreshold";
			lblBadThreshold.Size = new System.Drawing.Size(83, 15);
			lblBadThreshold.TabIndex = 7;
			lblBadThreshold.Text = "Bad threshold:";
			// 
			// nudBadThreshold
			// 
			nudBadThreshold.Location = new System.Drawing.Point(410, 18);
			nudBadThreshold.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
			nudBadThreshold.Name = "nudBadThreshold";
			nudBadThreshold.Size = new System.Drawing.Size(56, 23);
			nudBadThreshold.TabIndex = 8;
			nudBadThreshold.Value = new decimal(new int[] { 100, 0, 0, 0 });
			nudBadThreshold.ValueChanged += AnyControl_Changed;
			// 
			// lblWorseThreshold
			// 
			lblWorseThreshold.AutoSize = true;
			lblWorseThreshold.Location = new System.Drawing.Point(308, 43);
			lblWorseThreshold.Name = "lblWorseThreshold";
			lblWorseThreshold.Size = new System.Drawing.Size(96, 15);
			lblWorseThreshold.TabIndex = 9;
			lblWorseThreshold.Text = "Worse threshold:";
			// 
			// nudWorseThreshold
			// 
			nudWorseThreshold.Location = new System.Drawing.Point(410, 41);
			nudWorseThreshold.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
			nudWorseThreshold.Name = "nudWorseThreshold";
			nudWorseThreshold.Size = new System.Drawing.Size(56, 23);
			nudWorseThreshold.TabIndex = 10;
			nudWorseThreshold.Value = new decimal(new int[] { 200, 0, 0, 0 });
			nudWorseThreshold.ValueChanged += AnyControl_Changed;
			// 
			// lblUpperLimit
			// 
			lblUpperLimit.AutoSize = true;
			lblUpperLimit.Location = new System.Drawing.Point(472, 20);
			lblUpperLimit.Name = "lblUpperLimit";
			lblUpperLimit.Size = new System.Drawing.Size(72, 15);
			lblUpperLimit.TabIndex = 11;
			lblUpperLimit.Text = "Upper Limit:";
			// 
			// nudUpLimit
			// 
			nudUpLimit.Location = new System.Drawing.Point(550, 18);
			nudUpLimit.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			nudUpLimit.Name = "nudUpLimit";
			nudUpLimit.Size = new System.Drawing.Size(56, 23);
			nudUpLimit.TabIndex = 12;
			nudUpLimit.Value = new decimal(new int[] { 300, 0, 0, 0 });
			nudUpLimit.ValueChanged += AnyControl_Changed;
			// 
			// lblLowerLimit
			// 
			lblLowerLimit.AutoSize = true;
			lblLowerLimit.Location = new System.Drawing.Point(472, 43);
			lblLowerLimit.Name = "lblLowerLimit";
			lblLowerLimit.Size = new System.Drawing.Size(72, 15);
			lblLowerLimit.TabIndex = 13;
			lblLowerLimit.Text = "Lower Limit:";
			// 
			// nudLowLimit
			// 
			nudLowLimit.Location = new System.Drawing.Point(550, 41);
			nudLowLimit.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
			nudLowLimit.Minimum = new decimal(new int[] { 10, 0, 0, int.MinValue });
			nudLowLimit.Name = "nudLowLimit";
			nudLowLimit.Size = new System.Drawing.Size(56, 23);
			nudLowLimit.TabIndex = 14;
			nudLowLimit.ValueChanged += AnyControl_Changed;
			// 
			// lblScalingMethod
			// 
			lblScalingMethod.AutoSize = true;
			lblScalingMethod.Location = new System.Drawing.Point(10, 72);
			lblScalingMethod.Name = "lblScalingMethod";
			lblScalingMethod.Size = new System.Drawing.Size(93, 15);
			lblScalingMethod.TabIndex = 15;
			lblScalingMethod.Text = "Scaling Method:";
			// 
			// cbScalingMethod
			// 
			cbScalingMethod.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			cbScalingMethod.FormattingEnabled = true;
			cbScalingMethod.Items.AddRange(new object[] { "Classic", "Zoom", "Zoom Unlimited", "Fixed" });
			cbScalingMethod.Location = new System.Drawing.Point(110, 69);
			cbScalingMethod.Name = "cbScalingMethod";
			cbScalingMethod.Size = new System.Drawing.Size(121, 23);
			cbScalingMethod.TabIndex = 16;
			cbScalingMethod.SelectedIndexChanged += AnyControl_Changed;
			// 
			// grpLogging
			// 
			grpLogging.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			grpLogging.Controls.Add(cbLogFailures);
			grpLogging.Controls.Add(cbLogSuccesses);
			grpLogging.Location = new System.Drawing.Point(8, 398);
			grpLogging.Name = "grpLogging";
			grpLogging.Size = new System.Drawing.Size(612, 46);
			grpLogging.TabIndex = 8;
			grpLogging.TabStop = false;
			grpLogging.Text = "Logging";
			// 
			// cbLogFailures
			// 
			cbLogFailures.AutoSize = true;
			cbLogFailures.Location = new System.Drawing.Point(10, 19);
			cbLogFailures.Name = "cbLogFailures";
			cbLogFailures.Size = new System.Drawing.Size(89, 19);
			cbLogFailures.TabIndex = 0;
			cbLogFailures.Text = "Log Failures";
			cbLogFailures.UseVisualStyleBackColor = true;
			cbLogFailures.CheckedChanged += AnyControl_Changed;
			// 
			// cbLogSuccesses
			// 
			cbLogSuccesses.AutoSize = true;
			cbLogSuccesses.Location = new System.Drawing.Point(110, 19);
			cbLogSuccesses.Name = "cbLogSuccesses";
			cbLogSuccesses.Size = new System.Drawing.Size(101, 19);
			cbLogSuccesses.TabIndex = 1;
			cbLogSuccesses.Text = "Log Successes";
			cbLogSuccesses.UseVisualStyleBackColor = true;
			cbLogSuccesses.CheckedChanged += AnyControl_Changed;
			// 
			// btnSave
			// 
			btnSave.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			btnSave.Location = new System.Drawing.Point(8, 450);
			btnSave.Name = "btnSave";
			btnSave.Size = new System.Drawing.Size(80, 28);
			btnSave.TabIndex = 9;
			btnSave.Text = "Save";
			btnSave.UseVisualStyleBackColor = true;
			btnSave.Click += btnSave_Click;
			// 
			// btnDiscard
			// 
			btnDiscard.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			btnDiscard.Enabled = false;
			btnDiscard.Location = new System.Drawing.Point(94, 450);
			btnDiscard.Name = "btnDiscard";
			btnDiscard.Size = new System.Drawing.Size(80, 28);
			btnDiscard.TabIndex = 10;
			btnDiscard.Text = "Discard";
			toolTip1.SetToolTip(btnDiscard, "Discard unsaved changes and reload from disk.");
			btnDiscard.UseVisualStyleBackColor = true;
			btnDiscard.Click += btnDiscard_Click;
			// 
			// btnDelete
			// 
			btnDelete.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			btnDelete.Location = new System.Drawing.Point(180, 450);
			btnDelete.Name = "btnDelete";
			btnDelete.Size = new System.Drawing.Size(80, 28);
			btnDelete.TabIndex = 11;
			btnDelete.Text = "Delete";
			btnDelete.UseVisualStyleBackColor = true;
			btnDelete.Click += btnDelete_Click;
			// 
			// btnLoad
			// 
			btnLoad.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			btnLoad.Location = new System.Drawing.Point(266, 450);
			btnLoad.Name = "btnLoad";
			btnLoad.Size = new System.Drawing.Size(220, 28);
			btnLoad.TabIndex = 12;
			btnLoad.Text = "Load Configuration in Main Window";
			btnLoad.UseVisualStyleBackColor = true;
			btnLoad.Click += btnLoad_Click;
			// 
			// btnClone
			// 
			btnClone.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
			btnClone.Location = new System.Drawing.Point(545, 450);
			btnClone.Name = "btnClone";
			btnClone.Size = new System.Drawing.Size(75, 28);
			btnClone.TabIndex = 13;
			btnClone.Text = "Duplicate";
			toolTip1.SetToolTip(btnClone, "Create a duplicate of the selected configuration.");
			btnClone.UseVisualStyleBackColor = true;
			btnClone.Click += btnClone_Click;
			// 
			// toolTip1
			// 
			toolTip1.AutomaticDelay = 250;
			toolTip1.AutoPopDelay = 10000;
			toolTip1.InitialDelay = 250;
			toolTip1.ReshowDelay = 50;
			// 
			// ConfigurationForm
			// 
			AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			ClientSize = new System.Drawing.Size(840, 489);
			Controls.Add(splitContainerMain);
			Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
			MinimumSize = new System.Drawing.Size(856, 450);
			Name = "ConfigurationForm";
			StartPosition = System.Windows.Forms.FormStartPosition.Manual;
			Text = "Configuration Editor - PingTracer";
			FormClosing += ConfigurationForm_FormClosing;
			Load += ConfigurationForm_Load;
			splitContainerMain.Panel1.ResumeLayout(false);
			splitContainerMain.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)splitContainerMain).EndInit();
			splitContainerMain.ResumeLayout(false);
			panelEditor.ResumeLayout(false);
			panelEditor.PerformLayout();
			grpMonitoring.ResumeLayout(false);
			grpMonitoring.PerformLayout();
			((System.ComponentModel.ISupportInitialize)trackBarRate).EndInit();
			grpGraphOptions.ResumeLayout(false);
			grpGraphOptions.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudBadThreshold).EndInit();
			((System.ComponentModel.ISupportInitialize)nudWorseThreshold).EndInit();
			((System.ComponentModel.ISupportInitialize)nudUpLimit).EndInit();
			((System.ComponentModel.ISupportInitialize)nudLowLimit).EndInit();
			grpLogging.ResumeLayout(false);
			grpLogging.PerformLayout();
			ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.SplitContainer splitContainerMain;
		private System.Windows.Forms.TreeView treeConfigurations;
		private System.Windows.Forms.Panel panelEditor;
		private System.Windows.Forms.Label lblUniqueWarning;
		private System.Windows.Forms.Label lblTitle;
		private System.Windows.Forms.TextBox txtDisplayName;
		private System.Windows.Forms.Label lblHosts;
		private System.Windows.Forms.TextBox txtHosts;
		private System.Windows.Forms.CheckBox cbPreferIPv4;
		private System.Windows.Forms.GroupBox grpMonitoring;
		private System.Windows.Forms.CheckBox cbTraceroute;
		private System.Windows.Forms.Label lblTracerouteWarning;
		private System.Windows.Forms.CheckBox cbReverseDNS;
		private System.Windows.Forms.GroupBox grpGraphOptions;
		private System.Windows.Forms.CheckBox cbAlwaysShowServerNames;
		private System.Windows.Forms.CheckBox cbPacketLoss;
		private System.Windows.Forms.CheckBox cbDrawLimits;
		private System.Windows.Forms.CheckBox cbLastPing;
		private System.Windows.Forms.CheckBox cbAverage;
		private System.Windows.Forms.CheckBox cbJitter;
		private System.Windows.Forms.CheckBox cbMinMax;
		private System.Windows.Forms.Label lblBadThreshold;
		private System.Windows.Forms.NumericUpDown nudBadThreshold;
		private System.Windows.Forms.Label lblWorseThreshold;
		private System.Windows.Forms.NumericUpDown nudWorseThreshold;
		private System.Windows.Forms.Label lblUpperLimit;
		private System.Windows.Forms.NumericUpDown nudUpLimit;
		private System.Windows.Forms.Label lblLowerLimit;
		private System.Windows.Forms.NumericUpDown nudLowLimit;
		private System.Windows.Forms.Label lblScalingMethod;
		private System.Windows.Forms.ComboBox cbScalingMethod;
		private System.Windows.Forms.GroupBox grpLogging;
		private System.Windows.Forms.CheckBox cbLogFailures;
		private System.Windows.Forms.CheckBox cbLogSuccesses;
		private System.Windows.Forms.Button btnSave;
		private System.Windows.Forms.Button btnDiscard;
		private System.Windows.Forms.Button btnDelete;
		private System.Windows.Forms.Button btnLoad;
		private System.Windows.Forms.Button btnClone;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.Label lblRate;
		private System.Windows.Forms.TrackBar trackBarRate;
		private System.Windows.Forms.Label lblRateValue;
		private System.Windows.Forms.CheckBox cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings;
	}
}