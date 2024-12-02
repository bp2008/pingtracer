namespace PingTracer
{
	partial class OptionsForm
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OptionsForm));
			this.cbLogToFile = new System.Windows.Forms.CheckBox();
			this.cbDelayMostRecentPing = new System.Windows.Forms.CheckBox();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.cbWarnGraphNotLive = new System.Windows.Forms.CheckBox();
			this.label1 = new System.Windows.Forms.Label();
			this.nudPingResponsesToCache = new System.Windows.Forms.NumericUpDown();
			this.label2 = new System.Windows.Forms.Label();
			this.cbFastRefreshScrollingGraphs = new System.Windows.Forms.CheckBox();
			this.label3 = new System.Windows.Forms.Label();
			this.nudGraphScrollMultiplier = new System.Windows.Forms.NumericUpDown();
			this.cbShowDateInCorner = new System.Windows.Forms.CheckBox();
			this.label4 = new System.Windows.Forms.Label();
			this.txtCustomTimeString = new System.Windows.Forms.TextBox();
			this.customTimeStringHelp = new System.Windows.Forms.LinkLabel();
			this.panel1 = new System.Windows.Forms.Panel();
			this.groupBoxFormMargins = new System.Windows.Forms.GroupBox();
			this.nudBottomMargin = new System.Windows.Forms.NumericUpDown();
			this.nudRightMargin = new System.Windows.Forms.NumericUpDown();
			this.nudLeftMargin = new System.Windows.Forms.NumericUpDown();
			this.nudTopMargin = new System.Windows.Forms.NumericUpDown();
			this.label5 = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.nudPingResponsesToCache)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudGraphScrollMultiplier)).BeginInit();
			this.groupBoxFormMargins.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.nudBottomMargin)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudRightMargin)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudLeftMargin)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudTopMargin)).BeginInit();
			this.SuspendLayout();
			// 
			// cbLogToFile
			// 
			this.cbLogToFile.AutoSize = true;
			this.cbLogToFile.Checked = true;
			this.cbLogToFile.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbLogToFile.Location = new System.Drawing.Point(12, 12);
			this.cbLogToFile.Name = "cbLogToFile";
			this.cbLogToFile.Size = new System.Drawing.Size(125, 17);
			this.cbLogToFile.TabIndex = 1;
			this.cbLogToFile.Text = "Log text output to file";
			this.toolTip1.SetToolTip(this.cbLogToFile, "Output goes to PingTracer_Output.txt in the current working directory.");
			this.cbLogToFile.UseVisualStyleBackColor = true;
			this.cbLogToFile.CheckedChanged += new System.EventHandler(this.cbLogToFile_CheckedChanged);
			// 
			// cbDelayMostRecentPing
			// 
			this.cbDelayMostRecentPing.Checked = true;
			this.cbDelayMostRecentPing.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbDelayMostRecentPing.Location = new System.Drawing.Point(12, 35);
			this.cbDelayMostRecentPing.Name = "cbDelayMostRecentPing";
			this.cbDelayMostRecentPing.Size = new System.Drawing.Size(259, 34);
			this.cbDelayMostRecentPing.TabIndex = 2;
			this.cbDelayMostRecentPing.Text = "Delay ping graphing by one ping interval\r\n(reduces visual flickering)";
			this.toolTip1.SetToolTip(this.cbDelayMostRecentPing, "(Checked by default)\r\n\r\nIf unchecked, each wave of pings will appear early, \r\nlik" +
        "ely before the ping response has arrived, causing \r\na visual flickering effect w" +
        "hen the response arrives.");
			this.cbDelayMostRecentPing.UseVisualStyleBackColor = true;
			this.cbDelayMostRecentPing.CheckedChanged += new System.EventHandler(this.cbDelayMostRecentPing_CheckedChanged);
			// 
			// toolTip1
			// 
			this.toolTip1.AutomaticDelay = 250;
			this.toolTip1.AutoPopDelay = 10000;
			this.toolTip1.InitialDelay = 250;
			this.toolTip1.ReshowDelay = 50;
			// 
			// cbWarnGraphNotLive
			// 
			this.cbWarnGraphNotLive.AutoSize = true;
			this.cbWarnGraphNotLive.Checked = true;
			this.cbWarnGraphNotLive.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbWarnGraphNotLive.Location = new System.Drawing.Point(12, 75);
			this.cbWarnGraphNotLive.Name = "cbWarnGraphNotLive";
			this.cbWarnGraphNotLive.Size = new System.Drawing.Size(290, 17);
			this.cbWarnGraphNotLive.TabIndex = 3;
			this.cbWarnGraphNotLive.Text = "Warn when graph has been scrolled and is \"NOT LIVE\"";
			this.toolTip1.SetToolTip(this.cbWarnGraphNotLive, "(Checked by default)\r\n\r\nIf checked, \"NOT LIVE\" text will appear \r\nwhen you scroll" +
        " the graph to the side.");
			this.cbWarnGraphNotLive.UseVisualStyleBackColor = true;
			this.cbWarnGraphNotLive.CheckedChanged += new System.EventHandler(this.cbWarnGraphNotLive_CheckedChanged);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 182);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(293, 13);
			this.label1.TabIndex = 11;
			this.label1.Text = "Number of ping responses to cache in memory for each host:";
			this.toolTip1.SetToolTip(this.label1, resources.GetString("label1.ToolTip"));
			// 
			// nudPingResponsesToCache
			// 
			this.nudPingResponsesToCache.Location = new System.Drawing.Point(12, 201);
			this.nudPingResponsesToCache.Maximum = new decimal(new int[] {
            10000000,
            0,
            0,
            0});
			this.nudPingResponsesToCache.Minimum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
			this.nudPingResponsesToCache.Name = "nudPingResponsesToCache";
			this.nudPingResponsesToCache.Size = new System.Drawing.Size(102, 20);
			this.nudPingResponsesToCache.TabIndex = 7;
			this.toolTip1.SetToolTip(this.nudPingResponsesToCache, resources.GetString("nudPingResponsesToCache.ToolTip"));
			this.nudPingResponsesToCache.Value = new decimal(new int[] {
            360000,
            0,
            0,
            0});
			this.nudPingResponsesToCache.ValueChanged += new System.EventHandler(this.nudPingResponsesToCache_ValueChanged);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(120, 203);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(227, 13);
			this.label2.TabIndex = 13;
			this.label2.Text = "Takes effect when ping monitoring is restarted.";
			this.toolTip1.SetToolTip(this.label2, resources.GetString("label2.ToolTip"));
			// 
			// cbFastRefreshScrollingGraphs
			// 
			this.cbFastRefreshScrollingGraphs.AutoSize = true;
			this.cbFastRefreshScrollingGraphs.Checked = true;
			this.cbFastRefreshScrollingGraphs.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbFastRefreshScrollingGraphs.Location = new System.Drawing.Point(12, 98);
			this.cbFastRefreshScrollingGraphs.Name = "cbFastRefreshScrollingGraphs";
			this.cbFastRefreshScrollingGraphs.Size = new System.Drawing.Size(212, 17);
			this.cbFastRefreshScrollingGraphs.TabIndex = 4;
			this.cbFastRefreshScrollingGraphs.Text = "Accelerate graph redraw when scrolling";
			this.toolTip1.SetToolTip(this.cbFastRefreshScrollingGraphs, "(Checked by default)\r\n\r\nIf checked, graphs will update faster while being scrolle" +
        "d,\r\nat the cost of increased CPU usage.");
			this.cbFastRefreshScrollingGraphs.UseVisualStyleBackColor = true;
			this.cbFastRefreshScrollingGraphs.CheckedChanged += new System.EventHandler(this.cbFastRefreshScrollingGraphs_CheckedChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(9, 151);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(126, 13);
			this.label3.TabIndex = 15;
			this.label3.Text = "Graph scrolling multiplier: ";
			this.toolTip1.SetToolTip(this.label3, "(Default: 50)\r\n\r\nWhen you click and drag a ping graph horizontally,\r\nit scrolls. " +
        " If you increase this value, it will scroll faster.\r\n\r\nIf you set this value to " +
        "0, graph scrolling will be disabled.");
			// 
			// nudGraphScrollMultiplier
			// 
			this.nudGraphScrollMultiplier.Location = new System.Drawing.Point(141, 149);
			this.nudGraphScrollMultiplier.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
			this.nudGraphScrollMultiplier.Name = "nudGraphScrollMultiplier";
			this.nudGraphScrollMultiplier.Size = new System.Drawing.Size(102, 20);
			this.nudGraphScrollMultiplier.TabIndex = 6;
			this.toolTip1.SetToolTip(this.nudGraphScrollMultiplier, "(Default: 50)\r\n\r\nWhen you click and drag a ping graph horizontally,\r\nit scrolls. " +
        " If you increase this value, it will scroll faster.\r\n\r\nIf you set this value to " +
        "0, graph scrolling will be disabled.");
			this.nudGraphScrollMultiplier.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
			this.nudGraphScrollMultiplier.ValueChanged += new System.EventHandler(this.nudGraphScrollMultiplier_ValueChanged);
			// 
			// cbShowDateInCorner
			// 
			this.cbShowDateInCorner.AutoSize = true;
			this.cbShowDateInCorner.Checked = true;
			this.cbShowDateInCorner.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbShowDateInCorner.Location = new System.Drawing.Point(12, 121);
			this.cbShowDateInCorner.Name = "cbShowDateInCorner";
			this.cbShowDateInCorner.Size = new System.Drawing.Size(310, 17);
			this.cbShowDateInCorner.TabIndex = 5;
			this.cbShowDateInCorner.Text = "Show the current date in the bottom left corner of the graphs";
			this.toolTip1.SetToolTip(this.cbShowDateInCorner, "(Checked by default)\r\n\r\nIf checked, the associated date will overlap the bottom\r\n" +
        "left corner of the timeline below the graphs.");
			this.cbShowDateInCorner.UseVisualStyleBackColor = true;
			this.cbShowDateInCorner.CheckedChanged += new System.EventHandler(this.cbShowDateInCorner_CheckedChanged);
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(12, 235);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(142, 13);
			this.label4.TabIndex = 16;
			this.label4.Text = "Custom Time String for Logs:";
			// 
			// txtCustomTimeString
			// 
			this.txtCustomTimeString.Location = new System.Drawing.Point(160, 232);
			this.txtCustomTimeString.Name = "txtCustomTimeString";
			this.txtCustomTimeString.Size = new System.Drawing.Size(147, 20);
			this.txtCustomTimeString.TabIndex = 17;
			this.txtCustomTimeString.TextChanged += new System.EventHandler(this.txtCustomTimeStringGraphs_TextChanged);
			// 
			// customTimeStringHelp
			// 
			this.customTimeStringHelp.AutoSize = true;
			this.customTimeStringHelp.Location = new System.Drawing.Point(313, 235);
			this.customTimeStringHelp.Name = "customTimeStringHelp";
			this.customTimeStringHelp.Size = new System.Drawing.Size(33, 13);
			this.customTimeStringHelp.TabIndex = 18;
			this.customTimeStringHelp.TabStop = true;
			this.customTimeStringHelp.Text = "(help)";
			this.customTimeStringHelp.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.customTimeStringHelp_LinkClicked);
			// 
			// panel1
			// 
			this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.panel1.Location = new System.Drawing.Point(228, 42);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(53, 49);
			this.panel1.TabIndex = 19;
			// 
			// groupBoxFormMargins
			// 
			this.groupBoxFormMargins.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.groupBoxFormMargins.Controls.Add(this.nudBottomMargin);
			this.groupBoxFormMargins.Controls.Add(this.nudRightMargin);
			this.groupBoxFormMargins.Controls.Add(this.nudLeftMargin);
			this.groupBoxFormMargins.Controls.Add(this.nudTopMargin);
			this.groupBoxFormMargins.Controls.Add(this.label5);
			this.groupBoxFormMargins.Controls.Add(this.panel1);
			this.groupBoxFormMargins.Location = new System.Drawing.Point(12, 258);
			this.groupBoxFormMargins.Name = "groupBoxFormMargins";
			this.groupBoxFormMargins.Size = new System.Drawing.Size(346, 133);
			this.groupBoxFormMargins.TabIndex = 20;
			this.groupBoxFormMargins.TabStop = false;
			this.groupBoxFormMargins.Text = "Window Margins";
			// 
			// nudBottomMargin
			// 
			this.nudBottomMargin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nudBottomMargin.Location = new System.Drawing.Point(228, 97);
			this.nudBottomMargin.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
			this.nudBottomMargin.Minimum = new decimal(new int[] {
            30,
            0,
            0,
            -2147483648});
			this.nudBottomMargin.Name = "nudBottomMargin";
			this.nudBottomMargin.Size = new System.Drawing.Size(53, 20);
			this.nudBottomMargin.TabIndex = 24;
			this.nudBottomMargin.ValueChanged += new System.EventHandler(this.nudBottomMargin_ValueChanged);
			// 
			// nudRightMargin
			// 
			this.nudRightMargin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nudRightMargin.Location = new System.Drawing.Point(287, 56);
			this.nudRightMargin.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
			this.nudRightMargin.Minimum = new decimal(new int[] {
            30,
            0,
            0,
            -2147483648});
			this.nudRightMargin.Name = "nudRightMargin";
			this.nudRightMargin.Size = new System.Drawing.Size(53, 20);
			this.nudRightMargin.TabIndex = 23;
			this.nudRightMargin.ValueChanged += new System.EventHandler(this.nudRightMargin_ValueChanged);
			// 
			// nudLeftMargin
			// 
			this.nudLeftMargin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nudLeftMargin.Location = new System.Drawing.Point(169, 56);
			this.nudLeftMargin.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
			this.nudLeftMargin.Minimum = new decimal(new int[] {
            30,
            0,
            0,
            -2147483648});
			this.nudLeftMargin.Name = "nudLeftMargin";
			this.nudLeftMargin.Size = new System.Drawing.Size(53, 20);
			this.nudLeftMargin.TabIndex = 22;
			this.nudLeftMargin.ValueChanged += new System.EventHandler(this.nudLeftMargin_ValueChanged);
			// 
			// nudTopMargin
			// 
			this.nudTopMargin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nudTopMargin.Location = new System.Drawing.Point(228, 16);
			this.nudTopMargin.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
			this.nudTopMargin.Minimum = new decimal(new int[] {
            30,
            0,
            0,
            -2147483648});
			this.nudTopMargin.Name = "nudTopMargin";
			this.nudTopMargin.Size = new System.Drawing.Size(53, 20);
			this.nudTopMargin.TabIndex = 21;
			this.nudTopMargin.ValueChanged += new System.EventHandler(this.nudTopMargin_ValueChanged);
			// 
			// label5
			// 
			this.label5.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.label5.Location = new System.Drawing.Point(6, 18);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(157, 112);
			this.label5.TabIndex = 20;
			this.label5.Text = "When maximizing the ping graphs, they may appear larger or smaller than the regul" +
    "ar program window.  To correct this, the following margins will be subtracted fr" +
    "om the maximized graph view.";
			// 
			// OptionsForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(370, 398);
			this.Controls.Add(this.groupBoxFormMargins);
			this.Controls.Add(this.customTimeStringHelp);
			this.Controls.Add(this.txtCustomTimeString);
			this.Controls.Add(this.label4);
			this.Controls.Add(this.cbShowDateInCorner);
			this.Controls.Add(this.nudGraphScrollMultiplier);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.cbFastRefreshScrollingGraphs);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.nudPingResponsesToCache);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.cbWarnGraphNotLive);
			this.Controls.Add(this.cbDelayMostRecentPing);
			this.Controls.Add(this.cbLogToFile);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.Name = "OptionsForm";
			this.Text = "Ping Tracer Options";
			((System.ComponentModel.ISupportInitialize)(this.nudPingResponsesToCache)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudGraphScrollMultiplier)).EndInit();
			this.groupBoxFormMargins.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.nudBottomMargin)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudRightMargin)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudLeftMargin)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.nudTopMargin)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.CheckBox cbLogToFile;
		private System.Windows.Forms.CheckBox cbDelayMostRecentPing;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.CheckBox cbWarnGraphNotLive;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.NumericUpDown nudPingResponsesToCache;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.CheckBox cbFastRefreshScrollingGraphs;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.NumericUpDown nudGraphScrollMultiplier;
		private System.Windows.Forms.CheckBox cbShowDateInCorner;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.TextBox txtCustomTimeString;
		private System.Windows.Forms.LinkLabel customTimeStringHelp;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.GroupBox groupBoxFormMargins;
		private System.Windows.Forms.NumericUpDown nudTopMargin;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.NumericUpDown nudBottomMargin;
		private System.Windows.Forms.NumericUpDown nudRightMargin;
		private System.Windows.Forms.NumericUpDown nudLeftMargin;
	}
}