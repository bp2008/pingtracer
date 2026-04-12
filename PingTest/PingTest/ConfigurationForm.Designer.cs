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
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ConfigurationForm));
			this.splitContainerMain = new System.Windows.Forms.SplitContainer();
			this.treeConfigurations = new System.Windows.Forms.TreeView();
			this.panelEditor = new System.Windows.Forms.Panel();
			this.lblTitle = new System.Windows.Forms.Label();
			this.txtDisplayName = new System.Windows.Forms.TextBox();
			this.lblUniqueWarning = new System.Windows.Forms.Label();
			this.lblHosts = new System.Windows.Forms.Label();
			this.txtHosts = new System.Windows.Forms.TextBox();
			this.cbPreferIPv4 = new System.Windows.Forms.CheckBox();
			this.grpMonitoring = new System.Windows.Forms.GroupBox();
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings = new System.Windows.Forms.CheckBox();
			this.cbTraceroute = new System.Windows.Forms.CheckBox();
			this.lblTracerouteWarning = new System.Windows.Forms.Label();
			this.cbReverseDNS = new System.Windows.Forms.CheckBox();
			this.lblRate = new System.Windows.Forms.Label();
			this.lblRateValue = new System.Windows.Forms.Label();
			this.trackBarRate = new System.Windows.Forms.TrackBar();
			this.grpGraphOptions = new System.Windows.Forms.GroupBox();
			this.cbAlwaysShowServerNames = new System.Windows.Forms.CheckBox();
			this.cbPacketLoss = new System.Windows.Forms.CheckBox();
			this.cbDrawLimits = new System.Windows.Forms.CheckBox();
			this.cbLastPing = new System.Windows.Forms.CheckBox();
			this.cbAverage = new System.Windows.Forms.CheckBox();
			this.cbJitter = new System.Windows.Forms.CheckBox();
			this.cbMinMax = new System.Windows.Forms.CheckBox();
			this.lblBadThreshold = new System.Windows.Forms.Label();
			this.nudBadThreshold = new System.Windows.Forms.NumericUpDown();
			this.lblWorseThreshold = new System.Windows.Forms.Label();
			this.nudWorseThreshold = new System.Windows.Forms.NumericUpDown();
			this.lblUpperLimit = new System.Windows.Forms.Label();
			this.nudUpLimit = new System.Windows.Forms.NumericUpDown();
			this.lblLowerLimit = new System.Windows.Forms.Label();
			this.nudLowLimit = new System.Windows.Forms.NumericUpDown();
			this.lblScalingMethod = new System.Windows.Forms.Label();
			this.cbScalingMethod = new System.Windows.Forms.ComboBox();
			this.grpLogging = new System.Windows.Forms.GroupBox();
			this.cbLogFailures = new System.Windows.Forms.CheckBox();
			this.cbLogSuccesses = new System.Windows.Forms.CheckBox();
			this.btnSave = new System.Windows.Forms.Button();
			this.btnDiscard = new System.Windows.Forms.Button();
			this.btnDelete = new System.Windows.Forms.Button();
			this.btnLoad = new System.Windows.Forms.Button();
			this.btnClone = new System.Windows.Forms.Button();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).BeginInit();
			this.splitContainerMain.Panel1.SuspendLayout();
			this.splitContainerMain.Panel2.SuspendLayout();
			this.splitContainerMain.SuspendLayout();
			this.panelEditor.SuspendLayout();
			this.grpMonitoring.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.trackBarRate)).BeginInit();
			this.grpGraphOptions.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.nudBadThreshold)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudWorseThreshold)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudUpLimit)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudLowLimit)).BeginInit();
			this.grpLogging.SuspendLayout();
			this.SuspendLayout();
			// 
			// splitContainerMain
			// 
			this.splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainerMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.splitContainerMain.Location = new System.Drawing.Point(0, 0);
			this.splitContainerMain.Name = "splitContainerMain";
			// 
			// splitContainerMain.Panel1
			// 
			this.splitContainerMain.Panel1.Controls.Add(this.treeConfigurations);
			this.splitContainerMain.Panel1MinSize = 150;
			// 
			// splitContainerMain.Panel2
			// 
			this.splitContainerMain.Panel2.AutoScroll = true;
			this.splitContainerMain.Panel2.Controls.Add(this.panelEditor);
			this.splitContainerMain.Size = new System.Drawing.Size(804, 489);
			this.splitContainerMain.SplitterDistance = 200;
			this.splitContainerMain.TabIndex = 0;
			// 
			// treeConfigurations
			// 
			this.treeConfigurations.Dock = System.Windows.Forms.DockStyle.Fill;
			this.treeConfigurations.HideSelection = false;
			this.treeConfigurations.Location = new System.Drawing.Point(0, 0);
			this.treeConfigurations.Name = "treeConfigurations";
			this.treeConfigurations.Size = new System.Drawing.Size(200, 489);
			this.treeConfigurations.TabIndex = 0;
			this.treeConfigurations.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeConfigurations_AfterSelect);
			// 
			// panelEditor
			// 
			this.panelEditor.AutoScroll = true;
			this.panelEditor.Controls.Add(this.lblTitle);
			this.panelEditor.Controls.Add(this.txtDisplayName);
			this.panelEditor.Controls.Add(this.lblUniqueWarning);
			this.panelEditor.Controls.Add(this.lblHosts);
			this.panelEditor.Controls.Add(this.txtHosts);
			this.panelEditor.Controls.Add(this.cbPreferIPv4);
			this.panelEditor.Controls.Add(this.grpMonitoring);
			this.panelEditor.Controls.Add(this.grpGraphOptions);
			this.panelEditor.Controls.Add(this.grpLogging);
			this.panelEditor.Controls.Add(this.btnSave);
			this.panelEditor.Controls.Add(this.btnDiscard);
			this.panelEditor.Controls.Add(this.btnDelete);
			this.panelEditor.Controls.Add(this.btnLoad);
			this.panelEditor.Controls.Add(this.btnClone);
			this.panelEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panelEditor.Location = new System.Drawing.Point(0, 0);
			this.panelEditor.Name = "panelEditor";
			this.panelEditor.Size = new System.Drawing.Size(600, 489);
			this.panelEditor.TabIndex = 0;
			// 
			// lblTitle
			// 
			this.lblTitle.AutoSize = true;
			this.lblTitle.Location = new System.Drawing.Point(8, 11);
			this.lblTitle.Name = "lblTitle";
			this.lblTitle.Size = new System.Drawing.Size(75, 13);
			this.lblTitle.TabIndex = 0;
			this.lblTitle.Text = "Display Name:";
			// 
			// txtDisplayName
			// 
			this.txtDisplayName.Location = new System.Drawing.Point(92, 8);
			this.txtDisplayName.Name = "txtDisplayName";
			this.txtDisplayName.Size = new System.Drawing.Size(250, 20);
			this.txtDisplayName.TabIndex = 1;
			this.toolTip1.SetToolTip(this.txtDisplayName, "A unique name to identify this configuration.");
			this.txtDisplayName.TextChanged += new System.EventHandler(this.txtDisplayName_TextChanged);
			// 
			// lblUniqueWarning
			// 
			this.lblUniqueWarning.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.lblUniqueWarning.ForeColor = System.Drawing.Color.Red;
			this.lblUniqueWarning.Location = new System.Drawing.Point(348, 11);
			this.lblUniqueWarning.Name = "lblUniqueWarning";
			this.lblUniqueWarning.Size = new System.Drawing.Size(236, 15);
			this.lblUniqueWarning.TabIndex = 2;
			// 
			// lblHosts
			// 
			this.lblHosts.AutoSize = true;
			this.lblHosts.Location = new System.Drawing.Point(8, 35);
			this.lblHosts.Name = "lblHosts";
			this.lblHosts.Size = new System.Drawing.Size(101, 13);
			this.lblHosts.TabIndex = 3;
			this.lblHosts.Text = "Hosts (one per line):";
			// 
			// txtHosts
			// 
			this.txtHosts.AcceptsReturn = true;
			this.txtHosts.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.txtHosts.Location = new System.Drawing.Point(11, 51);
			this.txtHosts.Multiline = true;
			this.txtHosts.Name = "txtHosts";
			this.txtHosts.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.txtHosts.Size = new System.Drawing.Size(574, 116);
			this.txtHosts.TabIndex = 4;
			this.toolTip1.SetToolTip(this.txtHosts, "Enter host names or IP addresses, one per line.\r\nComma or space separated entries" +
        " are also accepted and will be normalized.");
			this.txtHosts.WordWrap = false;
			this.txtHosts.TextChanged += new System.EventHandler(this.txtHosts_TextChanged);
			this.txtHosts.Leave += new System.EventHandler(this.txtHosts_Leave);
			// 
			// cbPreferIPv4
			// 
			this.cbPreferIPv4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.cbPreferIPv4.AutoSize = true;
			this.cbPreferIPv4.Checked = true;
			this.cbPreferIPv4.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbPreferIPv4.Location = new System.Drawing.Point(9, 173);
			this.cbPreferIPv4.Name = "cbPreferIPv4";
			this.cbPreferIPv4.Size = new System.Drawing.Size(79, 17);
			this.cbPreferIPv4.TabIndex = 5;
			this.cbPreferIPv4.Text = "Prefer IPv4";
			this.toolTip1.SetToolTip(this.cbPreferIPv4, "If checked and DNS returns both IPv4 and IPv6 addresses, the IPv4 address will be" +
        " used.");
			this.cbPreferIPv4.UseVisualStyleBackColor = true;
			this.cbPreferIPv4.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// grpMonitoring
			// 
			this.grpMonitoring.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.grpMonitoring.Controls.Add(this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings);
			this.grpMonitoring.Controls.Add(this.cbTraceroute);
			this.grpMonitoring.Controls.Add(this.lblTracerouteWarning);
			this.grpMonitoring.Controls.Add(this.cbReverseDNS);
			this.grpMonitoring.Controls.Add(this.lblRate);
			this.grpMonitoring.Controls.Add(this.lblRateValue);
			this.grpMonitoring.Controls.Add(this.trackBarRate);
			this.grpMonitoring.Location = new System.Drawing.Point(8, 196);
			this.grpMonitoring.Name = "grpMonitoring";
			this.grpMonitoring.Size = new System.Drawing.Size(576, 91);
			this.grpMonitoring.TabIndex = 6;
			this.grpMonitoring.TabStop = false;
			this.grpMonitoring.Text = "Monitoring Options";
			// 
			// cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings
			// 
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.AutoSize = true;
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Checked = true;
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Location = new System.Drawing.Point(10, 65);
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Name = "cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings";
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Size = new System.Drawing.Size(355, 17);
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.TabIndex = 25;
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Text = "Stop monitoring intermediate hops that do not respond to regular pings";
			this.toolTip1.SetToolTip(this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings, resources.GetString("cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.ToolTip"));
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.UseVisualStyleBackColor = true;
			this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.CheckedChanged += new System.EventHandler(this.cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings_CheckedChanged);
			// 
			// cbTraceroute
			// 
			this.cbTraceroute.AutoSize = true;
			this.cbTraceroute.Checked = true;
			this.cbTraceroute.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbTraceroute.Location = new System.Drawing.Point(10, 19);
			this.cbTraceroute.Name = "cbTraceroute";
			this.cbTraceroute.Size = new System.Drawing.Size(86, 17);
			this.cbTraceroute.TabIndex = 0;
			this.cbTraceroute.Text = "Trace Route";
			this.toolTip1.SetToolTip(this.cbTraceroute, "If checked, a traceroute operation will be performed\r\nand each hop along the rout" +
        "e will be monitored.\r\nDisabled when multiple hosts are entered.");
			this.cbTraceroute.UseVisualStyleBackColor = true;
			this.cbTraceroute.CheckedChanged += new System.EventHandler(this.cbTraceroute_CheckedChanged);
			// 
			// lblTracerouteWarning
			// 
			this.lblTracerouteWarning.AutoSize = true;
			this.lblTracerouteWarning.ForeColor = System.Drawing.Color.Gray;
			this.lblTracerouteWarning.Location = new System.Drawing.Point(100, 20);
			this.lblTracerouteWarning.Name = "lblTracerouteWarning";
			this.lblTracerouteWarning.Size = new System.Drawing.Size(0, 13);
			this.lblTracerouteWarning.TabIndex = 1;
			// 
			// cbReverseDNS
			// 
			this.cbReverseDNS.AutoSize = true;
			this.cbReverseDNS.Checked = true;
			this.cbReverseDNS.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbReverseDNS.Location = new System.Drawing.Point(10, 42);
			this.cbReverseDNS.Name = "cbReverseDNS";
			this.cbReverseDNS.Size = new System.Drawing.Size(131, 17);
			this.cbReverseDNS.TabIndex = 2;
			this.cbReverseDNS.Text = "Reverse DNS Lookup";
			this.toolTip1.SetToolTip(this.cbReverseDNS, "If checked, reverse DNS lookups are performed on each IP address to find the host" +
        " name.");
			this.cbReverseDNS.UseVisualStyleBackColor = true;
			this.cbReverseDNS.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// lblRate
			// 
			this.lblRate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.lblRate.AutoSize = true;
			this.lblRate.Location = new System.Drawing.Point(277, 20);
			this.lblRate.Name = "lblRate";
			this.lblRate.Size = new System.Drawing.Size(57, 13);
			this.lblRate.TabIndex = 3;
			this.lblRate.Text = "Ping Rate:";
			// 
			// lblRateValue
			// 
			this.lblRateValue.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.lblRateValue.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblRateValue.Location = new System.Drawing.Point(258, 42);
			this.lblRateValue.Name = "lblRateValue";
			this.lblRateValue.Size = new System.Drawing.Size(76, 13);
			this.lblRateValue.TabIndex = 5;
			this.lblRateValue.Text = "1 ping/sec";
			this.lblRateValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
			// 
			// trackBarRate
			// 
			this.trackBarRate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.trackBarRate.Location = new System.Drawing.Point(340, 14);
			this.trackBarRate.Minimum = -10;
			this.trackBarRate.Name = "trackBarRate";
			this.trackBarRate.Size = new System.Drawing.Size(230, 45);
			this.trackBarRate.TabIndex = 4;
			this.toolTip1.SetToolTip(this.trackBarRate, "Positive = pings/sec, Negative = sec/ping, Zero = paused.\r\nA rate of 1 ping per s" +
        "econd is recommended for long-term monitoring.");
			this.trackBarRate.Value = 1;
			this.trackBarRate.ValueChanged += new System.EventHandler(this.trackBarRate_ValueChanged);
			// 
			// grpGraphOptions
			// 
			this.grpGraphOptions.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.grpGraphOptions.Controls.Add(this.cbAlwaysShowServerNames);
			this.grpGraphOptions.Controls.Add(this.cbPacketLoss);
			this.grpGraphOptions.Controls.Add(this.cbDrawLimits);
			this.grpGraphOptions.Controls.Add(this.cbLastPing);
			this.grpGraphOptions.Controls.Add(this.cbAverage);
			this.grpGraphOptions.Controls.Add(this.cbJitter);
			this.grpGraphOptions.Controls.Add(this.cbMinMax);
			this.grpGraphOptions.Controls.Add(this.lblBadThreshold);
			this.grpGraphOptions.Controls.Add(this.nudBadThreshold);
			this.grpGraphOptions.Controls.Add(this.lblWorseThreshold);
			this.grpGraphOptions.Controls.Add(this.nudWorseThreshold);
			this.grpGraphOptions.Controls.Add(this.lblUpperLimit);
			this.grpGraphOptions.Controls.Add(this.nudUpLimit);
			this.grpGraphOptions.Controls.Add(this.lblLowerLimit);
			this.grpGraphOptions.Controls.Add(this.nudLowLimit);
			this.grpGraphOptions.Controls.Add(this.lblScalingMethod);
			this.grpGraphOptions.Controls.Add(this.cbScalingMethod);
			this.grpGraphOptions.Location = new System.Drawing.Point(8, 293);
			this.grpGraphOptions.Name = "grpGraphOptions";
			this.grpGraphOptions.Size = new System.Drawing.Size(576, 100);
			this.grpGraphOptions.TabIndex = 7;
			this.grpGraphOptions.TabStop = false;
			this.grpGraphOptions.Text = "Graph Options";
			// 
			// cbAlwaysShowServerNames
			// 
			this.cbAlwaysShowServerNames.AutoSize = true;
			this.cbAlwaysShowServerNames.Checked = true;
			this.cbAlwaysShowServerNames.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbAlwaysShowServerNames.Location = new System.Drawing.Point(10, 19);
			this.cbAlwaysShowServerNames.Name = "cbAlwaysShowServerNames";
			this.cbAlwaysShowServerNames.Size = new System.Drawing.Size(93, 17);
			this.cbAlwaysShowServerNames.TabIndex = 0;
			this.cbAlwaysShowServerNames.Text = "Server Names";
			this.toolTip1.SetToolTip(this.cbAlwaysShowServerNames, "If checked, each server\'s name/address is overlaid on its graph.");
			this.cbAlwaysShowServerNames.UseVisualStyleBackColor = true;
			this.cbAlwaysShowServerNames.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbPacketLoss
			// 
			this.cbPacketLoss.AutoSize = true;
			this.cbPacketLoss.Checked = true;
			this.cbPacketLoss.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbPacketLoss.Location = new System.Drawing.Point(110, 19);
			this.cbPacketLoss.Name = "cbPacketLoss";
			this.cbPacketLoss.Size = new System.Drawing.Size(96, 17);
			this.cbPacketLoss.TabIndex = 1;
			this.cbPacketLoss.Text = "Packet Loss %";
			this.toolTip1.SetToolTip(this.cbPacketLoss, "If checked, each graph\'s visible packet loss is overlaid as a percentage.");
			this.cbPacketLoss.UseVisualStyleBackColor = true;
			this.cbPacketLoss.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbDrawLimits
			// 
			this.cbDrawLimits.AutoSize = true;
			this.cbDrawLimits.Location = new System.Drawing.Point(213, 19);
			this.cbDrawLimits.Name = "cbDrawLimits";
			this.cbDrawLimits.Size = new System.Drawing.Size(52, 17);
			this.cbDrawLimits.TabIndex = 2;
			this.cbDrawLimits.Text = "Limits";
			this.toolTip1.SetToolTip(this.cbDrawLimits, "If checked, each graph\'s vertical limits are drawn on the right side.");
			this.cbDrawLimits.UseVisualStyleBackColor = true;
			this.cbDrawLimits.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbLastPing
			// 
			this.cbLastPing.AutoSize = true;
			this.cbLastPing.Checked = true;
			this.cbLastPing.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbLastPing.Location = new System.Drawing.Point(10, 42);
			this.cbLastPing.Name = "cbLastPing";
			this.cbLastPing.Size = new System.Drawing.Size(70, 17);
			this.cbLastPing.TabIndex = 3;
			this.cbLastPing.Text = "Last Ping";
			this.toolTip1.SetToolTip(this.cbLastPing, "If checked, the most recent ping response time is overlaid in text form.");
			this.cbLastPing.UseVisualStyleBackColor = true;
			this.cbLastPing.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbAverage
			// 
			this.cbAverage.AutoSize = true;
			this.cbAverage.Checked = true;
			this.cbAverage.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbAverage.Location = new System.Drawing.Point(87, 42);
			this.cbAverage.Name = "cbAverage";
			this.cbAverage.Size = new System.Drawing.Size(66, 17);
			this.cbAverage.TabIndex = 4;
			this.cbAverage.Text = "Average";
			this.toolTip1.SetToolTip(this.cbAverage, "If checked, the average response time (of visible ping times) is overlaid in text" +
        " form.");
			this.cbAverage.UseVisualStyleBackColor = true;
			this.cbAverage.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbJitter
			// 
			this.cbJitter.AutoSize = true;
			this.cbJitter.Location = new System.Drawing.Point(160, 42);
			this.cbJitter.Name = "cbJitter";
			this.cbJitter.Size = new System.Drawing.Size(48, 17);
			this.cbJitter.TabIndex = 5;
			this.cbJitter.Text = "Jitter";
			this.toolTip1.SetToolTip(this.cbJitter, "If checked, the jitter (across visible ping times) is overlaid in text form.");
			this.cbJitter.UseVisualStyleBackColor = true;
			this.cbJitter.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbMinMax
			// 
			this.cbMinMax.AutoSize = true;
			this.cbMinMax.Location = new System.Drawing.Point(215, 42);
			this.cbMinMax.Name = "cbMinMax";
			this.cbMinMax.Size = new System.Drawing.Size(74, 17);
			this.cbMinMax.TabIndex = 6;
			this.cbMinMax.Text = "Min / Max";
			this.toolTip1.SetToolTip(this.cbMinMax, "If checked, the shortest and longest visible ping times are overlaid in text form" +
        ".");
			this.cbMinMax.UseVisualStyleBackColor = true;
			this.cbMinMax.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// lblBadThreshold
			// 
			this.lblBadThreshold.AutoSize = true;
			this.lblBadThreshold.Location = new System.Drawing.Point(299, 20);
			this.lblBadThreshold.Name = "lblBadThreshold";
			this.lblBadThreshold.Size = new System.Drawing.Size(75, 13);
			this.lblBadThreshold.TabIndex = 7;
			this.lblBadThreshold.Text = "Bad threshold:";
			// 
			// nudBadThreshold
			// 
			this.nudBadThreshold.Location = new System.Drawing.Point(380, 18);
			this.nudBadThreshold.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudBadThreshold.Name = "nudBadThreshold";
			this.nudBadThreshold.Size = new System.Drawing.Size(56, 20);
			this.nudBadThreshold.TabIndex = 8;
			this.nudBadThreshold.Value = new decimal(new int[] {
            100,
            0,
            0,
            0});
			this.nudBadThreshold.ValueChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// lblWorseThreshold
			// 
			this.lblWorseThreshold.AutoSize = true;
			this.lblWorseThreshold.Location = new System.Drawing.Point(287, 44);
			this.lblWorseThreshold.Name = "lblWorseThreshold";
			this.lblWorseThreshold.Size = new System.Drawing.Size(87, 13);
			this.lblWorseThreshold.TabIndex = 9;
			this.lblWorseThreshold.Text = "Worse threshold:";
			// 
			// nudWorseThreshold
			// 
			this.nudWorseThreshold.Location = new System.Drawing.Point(380, 42);
			this.nudWorseThreshold.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudWorseThreshold.Name = "nudWorseThreshold";
			this.nudWorseThreshold.Size = new System.Drawing.Size(56, 20);
			this.nudWorseThreshold.TabIndex = 10;
			this.nudWorseThreshold.Value = new decimal(new int[] {
            200,
            0,
            0,
            0});
			this.nudWorseThreshold.ValueChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// lblUpperLimit
			// 
			this.lblUpperLimit.AutoSize = true;
			this.lblUpperLimit.Location = new System.Drawing.Point(442, 20);
			this.lblUpperLimit.Name = "lblUpperLimit";
			this.lblUpperLimit.Size = new System.Drawing.Size(63, 13);
			this.lblUpperLimit.TabIndex = 11;
			this.lblUpperLimit.Text = "Upper Limit:";
			// 
			// nudUpLimit
			// 
			this.nudUpLimit.Location = new System.Drawing.Point(511, 18);
			this.nudUpLimit.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
			this.nudUpLimit.Name = "nudUpLimit";
			this.nudUpLimit.Size = new System.Drawing.Size(56, 20);
			this.nudUpLimit.TabIndex = 12;
			this.nudUpLimit.Value = new decimal(new int[] {
            300,
            0,
            0,
            0});
			this.nudUpLimit.ValueChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// lblLowerLimit
			// 
			this.lblLowerLimit.AutoSize = true;
			this.lblLowerLimit.Location = new System.Drawing.Point(442, 44);
			this.lblLowerLimit.Name = "lblLowerLimit";
			this.lblLowerLimit.Size = new System.Drawing.Size(63, 13);
			this.lblLowerLimit.TabIndex = 13;
			this.lblLowerLimit.Text = "Lower Limit:";
			// 
			// nudLowLimit
			// 
			this.nudLowLimit.Location = new System.Drawing.Point(511, 42);
			this.nudLowLimit.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudLowLimit.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            -2147483648});
			this.nudLowLimit.Name = "nudLowLimit";
			this.nudLowLimit.Size = new System.Drawing.Size(56, 20);
			this.nudLowLimit.TabIndex = 14;
			this.nudLowLimit.ValueChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// lblScalingMethod
			// 
			this.lblScalingMethod.AutoSize = true;
			this.lblScalingMethod.Location = new System.Drawing.Point(10, 72);
			this.lblScalingMethod.Name = "lblScalingMethod";
			this.lblScalingMethod.Size = new System.Drawing.Size(84, 13);
			this.lblScalingMethod.TabIndex = 15;
			this.lblScalingMethod.Text = "Scaling Method:";
			// 
			// cbScalingMethod
			// 
			this.cbScalingMethod.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbScalingMethod.FormattingEnabled = true;
			this.cbScalingMethod.Items.AddRange(new object[] {
            "Classic",
            "Zoom",
            "Zoom Unlimited",
            "Fixed"});
			this.cbScalingMethod.Location = new System.Drawing.Point(100, 69);
			this.cbScalingMethod.Name = "cbScalingMethod";
			this.cbScalingMethod.Size = new System.Drawing.Size(121, 21);
			this.cbScalingMethod.TabIndex = 16;
			this.cbScalingMethod.SelectedIndexChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// grpLogging
			// 
			this.grpLogging.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.grpLogging.Controls.Add(this.cbLogFailures);
			this.grpLogging.Controls.Add(this.cbLogSuccesses);
			this.grpLogging.Location = new System.Drawing.Point(8, 398);
			this.grpLogging.Name = "grpLogging";
			this.grpLogging.Size = new System.Drawing.Size(576, 42);
			this.grpLogging.TabIndex = 8;
			this.grpLogging.TabStop = false;
			this.grpLogging.Text = "Logging";
			// 
			// cbLogFailures
			// 
			this.cbLogFailures.AutoSize = true;
			this.cbLogFailures.Location = new System.Drawing.Point(10, 19);
			this.cbLogFailures.Name = "cbLogFailures";
			this.cbLogFailures.Size = new System.Drawing.Size(83, 17);
			this.cbLogFailures.TabIndex = 0;
			this.cbLogFailures.Text = "Log Failures";
			this.cbLogFailures.UseVisualStyleBackColor = true;
			this.cbLogFailures.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// cbLogSuccesses
			// 
			this.cbLogSuccesses.AutoSize = true;
			this.cbLogSuccesses.Location = new System.Drawing.Point(110, 19);
			this.cbLogSuccesses.Name = "cbLogSuccesses";
			this.cbLogSuccesses.Size = new System.Drawing.Size(99, 17);
			this.cbLogSuccesses.TabIndex = 1;
			this.cbLogSuccesses.Text = "Log Successes";
			this.cbLogSuccesses.UseVisualStyleBackColor = true;
			this.cbLogSuccesses.CheckedChanged += new System.EventHandler(this.AnyControl_Changed);
			// 
			// btnSave
			// 
			this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.btnSave.Location = new System.Drawing.Point(8, 450);
			this.btnSave.Name = "btnSave";
			this.btnSave.Size = new System.Drawing.Size(80, 28);
			this.btnSave.TabIndex = 9;
			this.btnSave.Text = "Save";
			this.btnSave.UseVisualStyleBackColor = true;
			this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
			// 
			// btnDiscard
			// 
			this.btnDiscard.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.btnDiscard.Enabled = false;
			this.btnDiscard.Location = new System.Drawing.Point(94, 450);
			this.btnDiscard.Name = "btnDiscard";
			this.btnDiscard.Size = new System.Drawing.Size(80, 28);
			this.btnDiscard.TabIndex = 10;
			this.btnDiscard.Text = "Discard";
			this.toolTip1.SetToolTip(this.btnDiscard, "Discard unsaved changes and reload from disk.");
			this.btnDiscard.UseVisualStyleBackColor = true;
			this.btnDiscard.Click += new System.EventHandler(this.btnDiscard_Click);
			// 
			// btnDelete
			// 
			this.btnDelete.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.btnDelete.Location = new System.Drawing.Point(180, 450);
			this.btnDelete.Name = "btnDelete";
			this.btnDelete.Size = new System.Drawing.Size(80, 28);
			this.btnDelete.TabIndex = 11;
			this.btnDelete.Text = "Delete";
			this.btnDelete.UseVisualStyleBackColor = true;
			this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
			// 
			// btnLoad
			// 
			this.btnLoad.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.btnLoad.Location = new System.Drawing.Point(266, 450);
			this.btnLoad.Name = "btnLoad";
			this.btnLoad.Size = new System.Drawing.Size(220, 28);
			this.btnLoad.TabIndex = 12;
			this.btnLoad.Text = "Load Configuration in Main Window";
			this.btnLoad.UseVisualStyleBackColor = true;
			this.btnLoad.Click += new System.EventHandler(this.btnLoad_Click);
			// 
			// btnClone
			// 
			this.btnClone.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.btnClone.Location = new System.Drawing.Point(509, 450);
			this.btnClone.Name = "btnClone";
			this.btnClone.Size = new System.Drawing.Size(75, 28);
			this.btnClone.TabIndex = 13;
			this.btnClone.Text = "Duplicate";
			this.toolTip1.SetToolTip(this.btnClone, "Create a duplicate of the selected configuration.");
			this.btnClone.UseVisualStyleBackColor = true;
			this.btnClone.Click += new System.EventHandler(this.btnClone_Click);
			// 
			// toolTip1
			// 
			this.toolTip1.AutomaticDelay = 250;
			this.toolTip1.AutoPopDelay = 10000;
			this.toolTip1.InitialDelay = 250;
			this.toolTip1.ReshowDelay = 50;
			// 
			// ConfigurationForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(804, 489);
			this.Controls.Add(this.splitContainerMain);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MinimumSize = new System.Drawing.Size(820, 441);
			this.Name = "ConfigurationForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
			this.Text = "Configuration Editor - PingTracer";
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ConfigurationForm_FormClosing);
			this.Load += new System.EventHandler(this.ConfigurationForm_Load);
			this.splitContainerMain.Panel1.ResumeLayout(false);
			this.splitContainerMain.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).EndInit();
			this.splitContainerMain.ResumeLayout(false);
			this.panelEditor.ResumeLayout(false);
			this.panelEditor.PerformLayout();
			this.grpMonitoring.ResumeLayout(false);
			this.grpMonitoring.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.trackBarRate)).EndInit();
			this.grpGraphOptions.ResumeLayout(false);
			this.grpGraphOptions.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.nudBadThreshold)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudWorseThreshold)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudUpLimit)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudLowLimit)).EndInit();
			this.grpLogging.ResumeLayout(false);
			this.grpLogging.PerformLayout();
			this.ResumeLayout(false);

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