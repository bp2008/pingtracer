﻿namespace PingTracer
{
	partial class MainForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
			this.txtOut = new System.Windows.Forms.TextBox();
			this.txtHost = new System.Windows.Forms.TextBox();
			this.lblHost = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.nudPingsPerSecond = new System.Windows.Forms.NumericUpDown();
			this.label3 = new System.Windows.Forms.Label();
			this.btnStart = new System.Windows.Forms.Button();
			this.label4 = new System.Windows.Forms.Label();
			this.lblSuccessful = new System.Windows.Forms.Label();
			this.lblFailed = new System.Windows.Forms.Label();
			this.label7 = new System.Windows.Forms.Label();
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.panel_Graphs = new System.Windows.Forms.Panel();
			this.label5 = new System.Windows.Forms.Label();
			this.lblHoveredPingStatus = new System.Windows.Forms.Label();
			this.cbTraceroute = new System.Windows.Forms.CheckBox();
			this.cbAlwaysShowServerNames = new System.Windows.Forms.CheckBox();
			this.label6 = new System.Windows.Forms.Label();
			this.label8 = new System.Windows.Forms.Label();
			this.label9 = new System.Windows.Forms.Label();
			this.nudBadThreshold = new System.Windows.Forms.NumericUpDown();
			this.nudWorseThreshold = new System.Windows.Forms.NumericUpDown();
			this.cbMinMax = new System.Windows.Forms.CheckBox();
			this.contextMenuStripHostHistory = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.label1 = new System.Windows.Forms.Label();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.txtDisplayName = new System.Windows.Forms.TextBox();
			this.cbPacketLoss = new System.Windows.Forms.CheckBox();
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.mainMenu1 = new System.Windows.Forms.MainMenu(this.components);
			this.menuItem6 = new System.Windows.Forms.MenuItem();
			this.mi_Exit = new System.Windows.Forms.MenuItem();
			this.menuItem1 = new System.Windows.Forms.MenuItem();
			this.mi_snapshotGraphs = new System.Windows.Forms.MenuItem();
			this.menuItem4 = new System.Windows.Forms.MenuItem();
			this.mi_Options = new System.Windows.Forms.MenuItem();
			((System.ComponentModel.ISupportInitialize)(this.nudPingsPerSecond)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.panel_Graphs.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.nudBadThreshold)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudWorseThreshold)).BeginInit();
			this.groupBox1.SuspendLayout();
			this.SuspendLayout();
			// 
			// txtOut
			// 
			this.txtOut.BackColor = System.Drawing.SystemColors.Window;
			this.txtOut.Dock = System.Windows.Forms.DockStyle.Fill;
			this.txtOut.Location = new System.Drawing.Point(0, 0);
			this.txtOut.Multiline = true;
			this.txtOut.Name = "txtOut";
			this.txtOut.ReadOnly = true;
			this.txtOut.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.txtOut.Size = new System.Drawing.Size(608, 73);
			this.txtOut.TabIndex = 15;
			// 
			// txtHost
			// 
			this.txtHost.Location = new System.Drawing.Point(50, 6);
			this.txtHost.Name = "txtHost";
			this.txtHost.Size = new System.Drawing.Size(184, 20);
			this.txtHost.TabIndex = 1;
			this.toolTip1.SetToolTip(this.txtHost, "Enter the IP address or host name of the destination you wish to monitor.\r\n\r\nYou " +
        "may click the blue Host label to choose a previously entered value.");
			// 
			// lblHost
			// 
			this.lblHost.AutoSize = true;
			this.lblHost.ForeColor = System.Drawing.Color.Blue;
			this.lblHost.Location = new System.Drawing.Point(12, 9);
			this.lblHost.Name = "lblHost";
			this.lblHost.Size = new System.Drawing.Size(32, 13);
			this.lblHost.TabIndex = 2;
			this.lblHost.Text = "Host:";
			this.toolTip1.SetToolTip(this.lblHost, "Enter the IP address or host name of the destination you wish to monitor.\r\n\r\nYou " +
        "may click the blue Host label to choose a previously entered value.");
			this.lblHost.Click += new System.EventHandler(this.lblHost_Click);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(11, 34);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(33, 13);
			this.label2.TabIndex = 4;
			this.label2.Text = "Rate:";
			this.toolTip1.SetToolTip(this.label2, "A rate of 1 ping per second is recommended \r\nfor all long-term monitoring.");
			// 
			// nudPingsPerSecond
			// 
			this.nudPingsPerSecond.Location = new System.Drawing.Point(50, 32);
			this.nudPingsPerSecond.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
			this.nudPingsPerSecond.Name = "nudPingsPerSecond";
			this.nudPingsPerSecond.Size = new System.Drawing.Size(42, 20);
			this.nudPingsPerSecond.TabIndex = 4;
			this.toolTip1.SetToolTip(this.nudPingsPerSecond, "A rate of 1 ping per second is recommended \r\nfor all long-term monitoring.");
			this.nudPingsPerSecond.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
			this.nudPingsPerSecond.ValueChanged += new System.EventHandler(this.nudPingsPerSecond_ValueChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(98, 34);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(88, 13);
			this.label3.TabIndex = 6;
			this.label3.Text = "pings per second";
			this.toolTip1.SetToolTip(this.label3, "A rate of 1 ping per second is recommended \r\nfor all long-term monitoring.");
			// 
			// btnStart
			// 
			this.btnStart.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
			this.btnStart.Location = new System.Drawing.Point(529, 6);
			this.btnStart.Name = "btnStart";
			this.btnStart.Size = new System.Drawing.Size(66, 46);
			this.btnStart.TabIndex = 3;
			this.btnStart.Text = "Click to Start";
			this.toolTip1.SetToolTip(this.btnStart, "This button shows the current status of ping monitoring.\r\n\r\nClick the button to s" +
        "tart or stop.");
			this.btnStart.UseVisualStyleBackColor = false;
			this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
			// 
			// label4
			// 
			this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(12, 541);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(62, 13);
			this.label4.TabIndex = 8;
			this.label4.Text = "Successful:";
			// 
			// lblSuccessful
			// 
			this.lblSuccessful.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblSuccessful.AutoSize = true;
			this.lblSuccessful.Location = new System.Drawing.Point(80, 541);
			this.lblSuccessful.Name = "lblSuccessful";
			this.lblSuccessful.Size = new System.Drawing.Size(13, 13);
			this.lblSuccessful.TabIndex = 9;
			this.lblSuccessful.Text = "0";
			// 
			// lblFailed
			// 
			this.lblFailed.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblFailed.AutoSize = true;
			this.lblFailed.Location = new System.Drawing.Point(206, 541);
			this.lblFailed.Name = "lblFailed";
			this.lblFailed.Size = new System.Drawing.Size(13, 13);
			this.lblFailed.TabIndex = 11;
			this.lblFailed.Text = "0";
			// 
			// label7
			// 
			this.label7.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label7.AutoSize = true;
			this.label7.Location = new System.Drawing.Point(162, 541);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(38, 13);
			this.label7.TabIndex = 10;
			this.label7.Text = "Failed:";
			// 
			// splitContainer1
			// 
			this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.splitContainer1.Location = new System.Drawing.Point(0, 112);
			this.splitContainer1.Name = "splitContainer1";
			this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.txtOut);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.panel_Graphs);
			this.splitContainer1.Size = new System.Drawing.Size(608, 426);
			this.splitContainer1.SplitterDistance = 73;
			this.splitContainer1.TabIndex = 12;
			// 
			// panel_Graphs
			// 
			this.panel_Graphs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.panel_Graphs.BackColor = System.Drawing.SystemColors.Window;
			this.panel_Graphs.Controls.Add(this.label5);
			this.panel_Graphs.Location = new System.Drawing.Point(0, 0);
			this.panel_Graphs.Name = "panel_Graphs";
			this.panel_Graphs.Size = new System.Drawing.Size(608, 348);
			this.panel_Graphs.TabIndex = 16;
			this.panel_Graphs.Click += new System.EventHandler(this.panel_Graphs_Click);
			this.panel_Graphs.Resize += new System.EventHandler(this.panel_Graphs_Resize);
			// 
			// label5
			// 
			this.label5.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.label5.BackColor = System.Drawing.SystemColors.Window;
			this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 14F);
			this.label5.ForeColor = System.Drawing.SystemColors.ControlText;
			this.label5.Location = new System.Drawing.Point(2, 12);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(603, 91);
			this.label5.TabIndex = 0;
			this.label5.Text = "Ping response graphs will appear here. \r\n\r\nTry clicking the graph(s) after you ac" +
    "tivate ping tracing.";
			this.label5.TextAlign = System.Drawing.ContentAlignment.TopCenter;
			// 
			// lblHoveredPingStatus
			// 
			this.lblHoveredPingStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblHoveredPingStatus.AutoSize = true;
			this.lblHoveredPingStatus.Location = new System.Drawing.Point(276, 541);
			this.lblHoveredPingStatus.Name = "lblHoveredPingStatus";
			this.lblHoveredPingStatus.Size = new System.Drawing.Size(19, 13);
			this.lblHoveredPingStatus.TabIndex = 13;
			this.lblHoveredPingStatus.Text = "    ";
			// 
			// cbTraceroute
			// 
			this.cbTraceroute.AutoSize = true;
			this.cbTraceroute.Checked = true;
			this.cbTraceroute.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbTraceroute.Location = new System.Drawing.Point(243, 33);
			this.cbTraceroute.Name = "cbTraceroute";
			this.cbTraceroute.Size = new System.Drawing.Size(232, 17);
			this.cbTraceroute.TabIndex = 5;
			this.cbTraceroute.Text = "Graph every node leading to the destination";
			this.toolTip1.SetToolTip(this.cbTraceroute, "If checked, a traceroute operation will be performed \r\nand multiple destinations " +
        "may be monitored.");
			this.cbTraceroute.UseVisualStyleBackColor = true;
			this.cbTraceroute.CheckedChanged += new System.EventHandler(this.cbTraceroute_CheckedChanged);
			// 
			// cbAlwaysShowServerNames
			// 
			this.cbAlwaysShowServerNames.AutoSize = true;
			this.cbAlwaysShowServerNames.Checked = true;
			this.cbAlwaysShowServerNames.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbAlwaysShowServerNames.Location = new System.Drawing.Point(6, 19);
			this.cbAlwaysShowServerNames.Name = "cbAlwaysShowServerNames";
			this.cbAlwaysShowServerNames.Size = new System.Drawing.Size(93, 17);
			this.cbAlwaysShowServerNames.TabIndex = 10;
			this.cbAlwaysShowServerNames.Text = "Server Names";
			this.toolTip1.SetToolTip(this.cbAlwaysShowServerNames, "If checked, each server\'s name/address is overlaid on its graph.");
			this.cbAlwaysShowServerNames.UseVisualStyleBackColor = true;
			this.cbAlwaysShowServerNames.CheckedChanged += new System.EventHandler(this.cbAlwaysShowServerNames_CheckedChanged);
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(264, 70);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(0, 13);
			this.label6.TabIndex = 16;
			// 
			// label8
			// 
			this.label8.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.label8.AutoSize = true;
			this.label8.Location = new System.Drawing.Point(286, 20);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(75, 13);
			this.label8.TabIndex = 17;
			this.label8.Text = "Bad threshold:";
			this.toolTip1.SetToolTip(this.label8, "Pings exceeding this threshold are drawn in faded yellow color, \r\nand the backgro" +
        "und of the ping graph will be yellow tinted \r\nabove this point.");
			// 
			// label9
			// 
			this.label9.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.label9.AutoSize = true;
			this.label9.Location = new System.Drawing.Point(429, 20);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(87, 13);
			this.label9.TabIndex = 18;
			this.label9.Text = "Worse threshold:";
			this.toolTip1.SetToolTip(this.label9, "Pings exceeding this threshold are drawn in bright yellow color, \r\nand the backgr" +
        "ound of the ping graph will be red tinted \r\nabove this point.");
			// 
			// nudBadThreshold
			// 
			this.nudBadThreshold.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nudBadThreshold.Location = new System.Drawing.Point(367, 17);
			this.nudBadThreshold.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudBadThreshold.Name = "nudBadThreshold";
			this.nudBadThreshold.Size = new System.Drawing.Size(56, 20);
			this.nudBadThreshold.TabIndex = 13;
			this.toolTip1.SetToolTip(this.nudBadThreshold, "Pings exceeding this threshold are drawn in faded yellow color, \r\nand the backgro" +
        "und of the ping graph will be yellow tinted \r\nabove this point.");
			this.nudBadThreshold.Value = new decimal(new int[] {
            100,
            0,
            0,
            0});
			this.nudBadThreshold.ValueChanged += new System.EventHandler(this.nudBadThreshold_ValueChanged);
			// 
			// nudWorseThreshold
			// 
			this.nudWorseThreshold.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nudWorseThreshold.Location = new System.Drawing.Point(522, 18);
			this.nudWorseThreshold.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudWorseThreshold.Name = "nudWorseThreshold";
			this.nudWorseThreshold.Size = new System.Drawing.Size(56, 20);
			this.nudWorseThreshold.TabIndex = 14;
			this.toolTip1.SetToolTip(this.nudWorseThreshold, "Pings exceeding this threshold are drawn in bright yellow color, \r\nand the backgr" +
        "ound of the ping graph will be red tinted \r\nabove this point.");
			this.nudWorseThreshold.Value = new decimal(new int[] {
            200,
            0,
            0,
            0});
			this.nudWorseThreshold.ValueChanged += new System.EventHandler(this.nudWorseThreshold_ValueChanged);
			// 
			// cbMinMax
			// 
			this.cbMinMax.AutoSize = true;
			this.cbMinMax.Location = new System.Drawing.Point(105, 19);
			this.cbMinMax.Name = "cbMinMax";
			this.cbMinMax.Size = new System.Drawing.Size(74, 17);
			this.cbMinMax.TabIndex = 11;
			this.cbMinMax.Text = "Min / Max";
			this.toolTip1.SetToolTip(this.cbMinMax, "If checked, the shortest and longest visible ping times are overlaid in text form" +
        ".");
			this.cbMinMax.UseVisualStyleBackColor = true;
			this.cbMinMax.CheckedChanged += new System.EventHandler(this.cbMinMax_CheckedChanged);
			// 
			// contextMenuStripHostHistory
			// 
			this.contextMenuStripHostHistory.Name = "contextMenuStripHostHistory";
			this.contextMenuStripHostHistory.ShowImageMargin = false;
			this.contextMenuStripHostHistory.Size = new System.Drawing.Size(36, 4);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(240, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(75, 13);
			this.label1.TabIndex = 22;
			this.label1.Text = "Display Name:";
			this.toolTip1.SetToolTip(this.label1, "(Optional) \r\nA memorable name to show in the history \r\nwhen you click on the blue" +
        " Host label.");
			// 
			// toolTip1
			// 
			this.toolTip1.AutomaticDelay = 250;
			this.toolTip1.AutoPopDelay = 10000;
			this.toolTip1.InitialDelay = 250;
			this.toolTip1.ReshowDelay = 50;
			// 
			// txtDisplayName
			// 
			this.txtDisplayName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.txtDisplayName.Location = new System.Drawing.Point(321, 6);
			this.txtDisplayName.Name = "txtDisplayName";
			this.txtDisplayName.Size = new System.Drawing.Size(202, 20);
			this.txtDisplayName.TabIndex = 2;
			this.toolTip1.SetToolTip(this.txtDisplayName, "(Optional) \r\nA memorable name to show in the history \r\nwhen you click on the blue" +
        " Host label.");
			this.txtDisplayName.TextChanged += new System.EventHandler(this.txtDisplayName_TextChanged);
			// 
			// cbPacketLoss
			// 
			this.cbPacketLoss.AutoSize = true;
			this.cbPacketLoss.Checked = true;
			this.cbPacketLoss.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbPacketLoss.Location = new System.Drawing.Point(185, 19);
			this.cbPacketLoss.Name = "cbPacketLoss";
			this.cbPacketLoss.Size = new System.Drawing.Size(85, 17);
			this.cbPacketLoss.TabIndex = 12;
			this.cbPacketLoss.Text = "Packet Loss";
			this.toolTip1.SetToolTip(this.cbPacketLoss, "If checked, each graph\'s visible packet loss is overlaid as a percentage.");
			this.cbPacketLoss.UseVisualStyleBackColor = true;
			this.cbPacketLoss.CheckedChanged += new System.EventHandler(this.cbPacketLoss_CheckedChanged);
			// 
			// groupBox1
			// 
			this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox1.Controls.Add(this.cbAlwaysShowServerNames);
			this.groupBox1.Controls.Add(this.cbMinMax);
			this.groupBox1.Controls.Add(this.cbPacketLoss);
			this.groupBox1.Controls.Add(this.nudBadThreshold);
			this.groupBox1.Controls.Add(this.nudWorseThreshold);
			this.groupBox1.Controls.Add(this.label8);
			this.groupBox1.Controls.Add(this.label9);
			this.groupBox1.Location = new System.Drawing.Point(12, 58);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(584, 48);
			this.groupBox1.TabIndex = 7;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Graph Options:";
			// 
			// mainMenu1
			// 
			this.mainMenu1.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.menuItem6,
            this.menuItem1,
            this.menuItem4});
			// 
			// menuItem6
			// 
			this.menuItem6.Index = 0;
			this.menuItem6.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.mi_Exit});
			this.menuItem6.Text = "&File";
			// 
			// mi_Exit
			// 
			this.mi_Exit.Index = 0;
			this.mi_Exit.Text = "E&xit";
			this.mi_Exit.Click += new System.EventHandler(this.mi_Exit_Click);
			// 
			// menuItem1
			// 
			this.menuItem1.Index = 1;
			this.menuItem1.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.mi_snapshotGraphs});
			this.menuItem1.Text = "E&xport";
			// 
			// mi_snapshotGraphs
			// 
			this.mi_snapshotGraphs.Index = 0;
			this.mi_snapshotGraphs.Text = "&Snapshot of graphs";
			this.mi_snapshotGraphs.Click += new System.EventHandler(this.mi_snapshotGraphs_Click);
			// 
			// menuItem4
			// 
			this.menuItem4.Index = 2;
			this.menuItem4.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.mi_Options});
			this.menuItem4.Text = "&Tools";
			// 
			// mi_Options
			// 
			this.mi_Options.Index = 0;
			this.mi_Options.Text = "&Options";
			this.mi_Options.Click += new System.EventHandler(this.mi_Options_Click);
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(608, 561);
			this.Controls.Add(this.groupBox1);
			this.Controls.Add(this.txtDisplayName);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.label6);
			this.Controls.Add(this.cbTraceroute);
			this.Controls.Add(this.lblHoveredPingStatus);
			this.Controls.Add(this.splitContainer1);
			this.Controls.Add(this.lblFailed);
			this.Controls.Add(this.label7);
			this.Controls.Add(this.lblSuccessful);
			this.Controls.Add(this.label4);
			this.Controls.Add(this.btnStart);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.nudPingsPerSecond);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.lblHost);
			this.Controls.Add(this.txtHost);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.Menu = this.mainMenu1;
			this.MinimumSize = new System.Drawing.Size(300, 200);
			this.Name = "MainForm";
			this.Text = "Ping Tracer";
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
			this.Load += new System.EventHandler(this.MainForm_Load);
			((System.ComponentModel.ISupportInitialize)(this.nudPingsPerSecond)).EndInit();
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel1.PerformLayout();
			this.splitContainer1.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
			this.splitContainer1.ResumeLayout(false);
			this.panel_Graphs.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.nudBadThreshold)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudWorseThreshold)).EndInit();
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox txtOut;
		private System.Windows.Forms.TextBox txtHost;
		private System.Windows.Forms.Label lblHost;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.NumericUpDown nudPingsPerSecond;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Button btnStart;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label lblSuccessful;
		private System.Windows.Forms.Label lblFailed;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.SplitContainer splitContainer1;
		private System.Windows.Forms.Label lblHoveredPingStatus;
		private System.Windows.Forms.CheckBox cbTraceroute;
		private System.Windows.Forms.Panel panel_Graphs;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.CheckBox cbAlwaysShowServerNames;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label label8;
		private System.Windows.Forms.Label label9;
		private System.Windows.Forms.NumericUpDown nudBadThreshold;
		private System.Windows.Forms.NumericUpDown nudWorseThreshold;
		private System.Windows.Forms.CheckBox cbMinMax;
		private System.Windows.Forms.ContextMenuStrip contextMenuStripHostHistory;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.TextBox txtDisplayName;
		private System.Windows.Forms.CheckBox cbPacketLoss;
		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.MainMenu mainMenu1;
		private System.Windows.Forms.MenuItem menuItem1;
		private System.Windows.Forms.MenuItem mi_snapshotGraphs;
		private System.Windows.Forms.MenuItem menuItem4;
		private System.Windows.Forms.MenuItem mi_Options;
		private System.Windows.Forms.MenuItem menuItem6;
		private System.Windows.Forms.MenuItem mi_Exit;
	}
}

