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
			components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OptionsForm));
			cbLogToFile = new System.Windows.Forms.CheckBox();
			cbDelayMostRecentPing = new System.Windows.Forms.CheckBox();
			toolTip1 = new System.Windows.Forms.ToolTip(components);
			cbWarnGraphNotLive = new System.Windows.Forms.CheckBox();
			label1 = new System.Windows.Forms.Label();
			nudPingResponsesToCache = new System.Windows.Forms.NumericUpDown();
			label2 = new System.Windows.Forms.Label();
			cbFastRefreshScrollingGraphs = new System.Windows.Forms.CheckBox();
			label3 = new System.Windows.Forms.Label();
			nudGraphScrollMultiplier = new System.Windows.Forms.NumericUpDown();
			cbShowDateInCorner = new System.Windows.Forms.CheckBox();
			nudPingTimeoutRedLineHeight = new System.Windows.Forms.NumericUpDown();
			label6 = new System.Windows.Forms.Label();
			label7 = new System.Windows.Forms.Label();
			label4 = new System.Windows.Forms.Label();
			txtCustomTimeString = new System.Windows.Forms.TextBox();
			customTimeStringHelp = new System.Windows.Forms.LinkLabel();
			panel1 = new System.Windows.Forms.Panel();
			groupBoxFormMargins = new System.Windows.Forms.GroupBox();
			nudBottomMargin = new System.Windows.Forms.NumericUpDown();
			nudRightMargin = new System.Windows.Forms.NumericUpDown();
			nudLeftMargin = new System.Windows.Forms.NumericUpDown();
			nudTopMargin = new System.Windows.Forms.NumericUpDown();
			label5 = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)nudPingResponsesToCache).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudGraphScrollMultiplier).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudPingTimeoutRedLineHeight).BeginInit();
			groupBoxFormMargins.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudBottomMargin).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudRightMargin).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudLeftMargin).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudTopMargin).BeginInit();
			SuspendLayout();
			// 
			// cbLogToFile
			// 
			cbLogToFile.AutoSize = true;
			cbLogToFile.Checked = true;
			cbLogToFile.CheckState = System.Windows.Forms.CheckState.Checked;
			cbLogToFile.Location = new System.Drawing.Point(12, 12);
			cbLogToFile.Name = "cbLogToFile";
			cbLogToFile.Size = new System.Drawing.Size(140, 19);
			cbLogToFile.TabIndex = 1;
			cbLogToFile.Text = "Log text output to file";
			toolTip1.SetToolTip(cbLogToFile, "Log output goes to the Logs subfolder in the AppData directory (see Tools menu).");
			cbLogToFile.UseVisualStyleBackColor = true;
			cbLogToFile.CheckedChanged += cbLogToFile_CheckedChanged;
			// 
			// cbDelayMostRecentPing
			// 
			cbDelayMostRecentPing.Checked = true;
			cbDelayMostRecentPing.CheckState = System.Windows.Forms.CheckState.Checked;
			cbDelayMostRecentPing.Location = new System.Drawing.Point(12, 35);
			cbDelayMostRecentPing.Name = "cbDelayMostRecentPing";
			cbDelayMostRecentPing.Size = new System.Drawing.Size(259, 34);
			cbDelayMostRecentPing.TabIndex = 2;
			cbDelayMostRecentPing.Text = "Delay ping graphing by one ping interval\r\n(reduces visual flickering)";
			toolTip1.SetToolTip(cbDelayMostRecentPing, "(Checked by default)\r\n\r\nIf unchecked, each wave of pings will appear early, \r\nlikely before the ping response has arrived, causing \r\na visual flickering effect when the response arrives.");
			cbDelayMostRecentPing.UseVisualStyleBackColor = true;
			cbDelayMostRecentPing.CheckedChanged += cbDelayMostRecentPing_CheckedChanged;
			// 
			// toolTip1
			// 
			toolTip1.AutomaticDelay = 250;
			toolTip1.AutoPopDelay = 10000;
			toolTip1.InitialDelay = 250;
			toolTip1.ReshowDelay = 50;
			// 
			// cbWarnGraphNotLive
			// 
			cbWarnGraphNotLive.AutoSize = true;
			cbWarnGraphNotLive.Checked = true;
			cbWarnGraphNotLive.CheckState = System.Windows.Forms.CheckState.Checked;
			cbWarnGraphNotLive.Location = new System.Drawing.Point(12, 75);
			cbWarnGraphNotLive.Name = "cbWarnGraphNotLive";
			cbWarnGraphNotLive.Size = new System.Drawing.Size(310, 19);
			cbWarnGraphNotLive.TabIndex = 3;
			cbWarnGraphNotLive.Text = "Warn when graph has been scrolled and is \"NOT LIVE\"";
			toolTip1.SetToolTip(cbWarnGraphNotLive, "(Checked by default)\r\n\r\nIf checked, \"NOT LIVE\" text will appear \r\nwhen you scroll the graph to the side.");
			cbWarnGraphNotLive.UseVisualStyleBackColor = true;
			cbWarnGraphNotLive.CheckedChanged += cbWarnGraphNotLive_CheckedChanged;
			// 
			// label1
			// 
			label1.AutoSize = true;
			label1.Location = new System.Drawing.Point(12, 181);
			label1.Name = "label1";
			label1.Size = new System.Drawing.Size(331, 15);
			label1.TabIndex = 11;
			label1.Text = "Number of ping responses to cache in memory for each host:";
			toolTip1.SetToolTip(label1, resources.GetString("label1.ToolTip"));
			// 
			// nudPingResponsesToCache
			// 
			nudPingResponsesToCache.Location = new System.Drawing.Point(12, 200);
			nudPingResponsesToCache.Maximum = new decimal(new int[] { 10000000, 0, 0, 0 });
			nudPingResponsesToCache.Minimum = new decimal(new int[] { 10000, 0, 0, 0 });
			nudPingResponsesToCache.Name = "nudPingResponsesToCache";
			nudPingResponsesToCache.Size = new System.Drawing.Size(102, 23);
			nudPingResponsesToCache.TabIndex = 7;
			toolTip1.SetToolTip(nudPingResponsesToCache, resources.GetString("nudPingResponsesToCache.ToolTip"));
			nudPingResponsesToCache.Value = new decimal(new int[] { 360000, 0, 0, 0 });
			nudPingResponsesToCache.ValueChanged += nudPingResponsesToCache_ValueChanged;
			// 
			// label2
			// 
			label2.AutoSize = true;
			label2.Location = new System.Drawing.Point(120, 202);
			label2.Name = "label2";
			label2.Size = new System.Drawing.Size(254, 15);
			label2.TabIndex = 13;
			label2.Text = "Takes effect when ping monitoring is restarted.";
			toolTip1.SetToolTip(label2, resources.GetString("label2.ToolTip"));
			// 
			// cbFastRefreshScrollingGraphs
			// 
			cbFastRefreshScrollingGraphs.AutoSize = true;
			cbFastRefreshScrollingGraphs.Checked = true;
			cbFastRefreshScrollingGraphs.CheckState = System.Windows.Forms.CheckState.Checked;
			cbFastRefreshScrollingGraphs.Location = new System.Drawing.Point(12, 98);
			cbFastRefreshScrollingGraphs.Name = "cbFastRefreshScrollingGraphs";
			cbFastRefreshScrollingGraphs.Size = new System.Drawing.Size(234, 19);
			cbFastRefreshScrollingGraphs.TabIndex = 4;
			cbFastRefreshScrollingGraphs.Text = "Accelerate graph redraw when scrolling";
			toolTip1.SetToolTip(cbFastRefreshScrollingGraphs, "(Checked by default)\r\n\r\nIf checked, graphs will update faster while being scrolled,\r\nat the cost of increased CPU usage.");
			cbFastRefreshScrollingGraphs.UseVisualStyleBackColor = true;
			cbFastRefreshScrollingGraphs.CheckedChanged += cbFastRefreshScrollingGraphs_CheckedChanged;
			// 
			// label3
			// 
			label3.AutoSize = true;
			label3.Location = new System.Drawing.Point(9, 150);
			label3.Name = "label3";
			label3.Size = new System.Drawing.Size(147, 15);
			label3.TabIndex = 15;
			label3.Text = "Graph scrolling multiplier: ";
			toolTip1.SetToolTip(label3, "(Default: 50)\r\n\r\nWhen you click and drag a ping graph horizontally,\r\nit scrolls.  If you increase this value, it will scroll faster.\r\n\r\nIf you set this value to 0, graph scrolling will be disabled.");
			// 
			// nudGraphScrollMultiplier
			// 
			nudGraphScrollMultiplier.Location = new System.Drawing.Point(162, 148);
			nudGraphScrollMultiplier.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			nudGraphScrollMultiplier.Name = "nudGraphScrollMultiplier";
			nudGraphScrollMultiplier.Size = new System.Drawing.Size(102, 23);
			nudGraphScrollMultiplier.TabIndex = 6;
			toolTip1.SetToolTip(nudGraphScrollMultiplier, "(Default: 50)\r\n\r\nWhen you click and drag a ping graph horizontally,\r\nit scrolls.  If you increase this value, it will scroll faster.\r\n\r\nIf you set this value to 0, graph scrolling will be disabled.");
			nudGraphScrollMultiplier.Value = new decimal(new int[] { 1, 0, 0, 0 });
			nudGraphScrollMultiplier.ValueChanged += nudGraphScrollMultiplier_ValueChanged;
			// 
			// cbShowDateInCorner
			// 
			cbShowDateInCorner.AutoSize = true;
			cbShowDateInCorner.Checked = true;
			cbShowDateInCorner.CheckState = System.Windows.Forms.CheckState.Checked;
			cbShowDateInCorner.Location = new System.Drawing.Point(12, 121);
			cbShowDateInCorner.Name = "cbShowDateInCorner";
			cbShowDateInCorner.Size = new System.Drawing.Size(348, 19);
			cbShowDateInCorner.TabIndex = 5;
			cbShowDateInCorner.Text = "Show the current date in the bottom left corner of the graphs";
			toolTip1.SetToolTip(cbShowDateInCorner, "(Checked by default)\r\n\r\nIf checked, the associated date will overlap the bottom\r\nleft corner of the timeline below the graphs.");
			cbShowDateInCorner.UseVisualStyleBackColor = true;
			cbShowDateInCorner.CheckedChanged += cbShowDateInCorner_CheckedChanged;
			// 
			// nudPingTimeoutRedLineHeight
			// 
			nudPingTimeoutRedLineHeight.Location = new System.Drawing.Point(176, 257);
			nudPingTimeoutRedLineHeight.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			nudPingTimeoutRedLineHeight.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudPingTimeoutRedLineHeight.Name = "nudPingTimeoutRedLineHeight";
			nudPingTimeoutRedLineHeight.Size = new System.Drawing.Size(64, 23);
			nudPingTimeoutRedLineHeight.TabIndex = 21;
			toolTip1.SetToolTip(nudPingTimeoutRedLineHeight, "When a ping times out (gets no response), a red line is drawn \r\nup to this many pixels tall in the graph. You can reduce this \r\nvalue to shrink the line that is drawn.");
			nudPingTimeoutRedLineHeight.Value = new decimal(new int[] { 10000, 0, 0, 0 });
			nudPingTimeoutRedLineHeight.ValueChanged += nudPingTimeoutRedLineHeight_ValueChanged;
			// 
			// label6
			// 
			label6.AutoSize = true;
			label6.Location = new System.Drawing.Point(12, 259);
			label6.Name = "label6";
			label6.Size = new System.Drawing.Size(158, 15);
			label6.TabIndex = 22;
			label6.Text = "Ping timeout red line height:";
			toolTip1.SetToolTip(label6, "When a ping times out (gets no response), a red line is drawn \r\nup to this many pixels tall in the graph. You can reduce this \r\nvalue to shrink the line that is drawn.");
			// 
			// label7
			// 
			label7.AutoSize = true;
			label7.Location = new System.Drawing.Point(246, 259);
			label7.Name = "label7";
			label7.Size = new System.Drawing.Size(44, 15);
			label7.TabIndex = 23;
			label7.Text = "(pixels)";
			toolTip1.SetToolTip(label7, "When a ping times out (gets no response), a red line is drawn \r\nup to this many pixels tall in the graph. You can reduce this \r\nvalue to shrink the line that is drawn.");
			// 
			// label4
			// 
			label4.AutoSize = true;
			label4.Location = new System.Drawing.Point(12, 234);
			label4.Name = "label4";
			label4.Size = new System.Drawing.Size(162, 15);
			label4.TabIndex = 16;
			label4.Text = "Custom Time String for Logs:";
			// 
			// txtCustomTimeString
			// 
			txtCustomTimeString.Location = new System.Drawing.Point(180, 231);
			txtCustomTimeString.Name = "txtCustomTimeString";
			txtCustomTimeString.Size = new System.Drawing.Size(147, 23);
			txtCustomTimeString.TabIndex = 17;
			txtCustomTimeString.TextChanged += txtCustomTimeStringGraphs_TextChanged;
			// 
			// customTimeStringHelp
			// 
			customTimeStringHelp.AutoSize = true;
			customTimeStringHelp.Location = new System.Drawing.Point(333, 234);
			customTimeStringHelp.Name = "customTimeStringHelp";
			customTimeStringHelp.Size = new System.Drawing.Size(38, 15);
			customTimeStringHelp.TabIndex = 18;
			customTimeStringHelp.TabStop = true;
			customTimeStringHelp.Text = "(help)";
			customTimeStringHelp.LinkClicked += customTimeStringHelp_LinkClicked;
			// 
			// panel1
			// 
			panel1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			panel1.Location = new System.Drawing.Point(244, 67);
			panel1.Name = "panel1";
			panel1.Size = new System.Drawing.Size(53, 49);
			panel1.TabIndex = 19;
			// 
			// groupBoxFormMargins
			// 
			groupBoxFormMargins.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			groupBoxFormMargins.Controls.Add(nudBottomMargin);
			groupBoxFormMargins.Controls.Add(nudRightMargin);
			groupBoxFormMargins.Controls.Add(nudLeftMargin);
			groupBoxFormMargins.Controls.Add(nudTopMargin);
			groupBoxFormMargins.Controls.Add(label5);
			groupBoxFormMargins.Controls.Add(panel1);
			groupBoxFormMargins.Location = new System.Drawing.Point(12, 294);
			groupBoxFormMargins.Name = "groupBoxFormMargins";
			groupBoxFormMargins.Size = new System.Drawing.Size(362, 151);
			groupBoxFormMargins.TabIndex = 20;
			groupBoxFormMargins.TabStop = false;
			groupBoxFormMargins.Text = "Window Margins";
			// 
			// nudBottomMargin
			// 
			nudBottomMargin.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			nudBottomMargin.Location = new System.Drawing.Point(244, 122);
			nudBottomMargin.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
			nudBottomMargin.Minimum = new decimal(new int[] { 30, 0, 0, int.MinValue });
			nudBottomMargin.Name = "nudBottomMargin";
			nudBottomMargin.Size = new System.Drawing.Size(53, 23);
			nudBottomMargin.TabIndex = 24;
			nudBottomMargin.ValueChanged += nudBottomMargin_ValueChanged;
			// 
			// nudRightMargin
			// 
			nudRightMargin.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			nudRightMargin.Location = new System.Drawing.Point(303, 81);
			nudRightMargin.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
			nudRightMargin.Minimum = new decimal(new int[] { 30, 0, 0, int.MinValue });
			nudRightMargin.Name = "nudRightMargin";
			nudRightMargin.Size = new System.Drawing.Size(53, 23);
			nudRightMargin.TabIndex = 23;
			nudRightMargin.ValueChanged += nudRightMargin_ValueChanged;
			// 
			// nudLeftMargin
			// 
			nudLeftMargin.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			nudLeftMargin.Location = new System.Drawing.Point(185, 81);
			nudLeftMargin.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
			nudLeftMargin.Minimum = new decimal(new int[] { 30, 0, 0, int.MinValue });
			nudLeftMargin.Name = "nudLeftMargin";
			nudLeftMargin.Size = new System.Drawing.Size(53, 23);
			nudLeftMargin.TabIndex = 22;
			nudLeftMargin.ValueChanged += nudLeftMargin_ValueChanged;
			// 
			// nudTopMargin
			// 
			nudTopMargin.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			nudTopMargin.Location = new System.Drawing.Point(244, 41);
			nudTopMargin.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
			nudTopMargin.Minimum = new decimal(new int[] { 30, 0, 0, int.MinValue });
			nudTopMargin.Name = "nudTopMargin";
			nudTopMargin.Size = new System.Drawing.Size(53, 23);
			nudTopMargin.TabIndex = 21;
			nudTopMargin.ValueChanged += nudTopMargin_ValueChanged;
			// 
			// label5
			// 
			label5.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			label5.Location = new System.Drawing.Point(6, 28);
			label5.Name = "label5";
			label5.Size = new System.Drawing.Size(173, 120);
			label5.TabIndex = 20;
			label5.Text = "When maximizing the ping graphs, they may appear larger or smaller than the regular program window.  To correct this, the following margins will be subtracted from the maximized graph view.";
			// 
			// OptionsForm
			// 
			AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			ClientSize = new System.Drawing.Size(386, 452);
			Controls.Add(label7);
			Controls.Add(nudPingTimeoutRedLineHeight);
			Controls.Add(label6);
			Controls.Add(groupBoxFormMargins);
			Controls.Add(customTimeStringHelp);
			Controls.Add(txtCustomTimeString);
			Controls.Add(label4);
			Controls.Add(cbShowDateInCorner);
			Controls.Add(nudGraphScrollMultiplier);
			Controls.Add(label3);
			Controls.Add(cbFastRefreshScrollingGraphs);
			Controls.Add(label2);
			Controls.Add(nudPingResponsesToCache);
			Controls.Add(label1);
			Controls.Add(cbWarnGraphNotLive);
			Controls.Add(cbDelayMostRecentPing);
			Controls.Add(cbLogToFile);
			FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
			Name = "OptionsForm";
			Text = "Options - PingTracer";
			((System.ComponentModel.ISupportInitialize)nudPingResponsesToCache).EndInit();
			((System.ComponentModel.ISupportInitialize)nudGraphScrollMultiplier).EndInit();
			((System.ComponentModel.ISupportInitialize)nudPingTimeoutRedLineHeight).EndInit();
			groupBoxFormMargins.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)nudBottomMargin).EndInit();
			((System.ComponentModel.ISupportInitialize)nudRightMargin).EndInit();
			((System.ComponentModel.ISupportInitialize)nudLeftMargin).EndInit();
			((System.ComponentModel.ISupportInitialize)nudTopMargin).EndInit();
			ResumeLayout(false);
			PerformLayout();

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
		private System.Windows.Forms.NumericUpDown nudPingTimeoutRedLineHeight;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label label7;
	}
}