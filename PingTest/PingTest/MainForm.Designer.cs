namespace PingTest
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
			this.txtOut = new System.Windows.Forms.TextBox();
			this.txtHost = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
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
			((System.ComponentModel.ISupportInitialize)(this.nudPingsPerSecond)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.panel_Graphs.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.nudBadThreshold)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.nudWorseThreshold)).BeginInit();
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
			this.txtOut.Size = new System.Drawing.Size(608, 79);
			this.txtOut.TabIndex = 0;
			// 
			// txtHost
			// 
			this.txtHost.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.txtHost.Location = new System.Drawing.Point(51, 9);
			this.txtHost.Name = "txtHost";
			this.txtHost.Size = new System.Drawing.Size(473, 20);
			this.txtHost.TabIndex = 1;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(13, 12);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(32, 13);
			this.label1.TabIndex = 2;
			this.label1.Text = "Host:";
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(13, 38);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(33, 13);
			this.label2.TabIndex = 4;
			this.label2.Text = "Rate:";
			// 
			// nudPingsPerSecond
			// 
			this.nudPingsPerSecond.Location = new System.Drawing.Point(51, 36);
			this.nudPingsPerSecond.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
			this.nudPingsPerSecond.Name = "nudPingsPerSecond";
			this.nudPingsPerSecond.Size = new System.Drawing.Size(42, 20);
			this.nudPingsPerSecond.TabIndex = 5;
			this.nudPingsPerSecond.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
			this.nudPingsPerSecond.ValueChanged += new System.EventHandler(this.nudPingsPerSecond_ValueChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(99, 38);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(88, 13);
			this.label3.TabIndex = 6;
			this.label3.Text = "pings per second";
			// 
			// btnStart
			// 
			this.btnStart.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
			this.btnStart.Location = new System.Drawing.Point(530, 7);
			this.btnStart.Name = "btnStart";
			this.btnStart.Size = new System.Drawing.Size(66, 49);
			this.btnStart.TabIndex = 7;
			this.btnStart.Text = "Idle";
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
			this.splitContainer1.Location = new System.Drawing.Point(0, 85);
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
			this.splitContainer1.Size = new System.Drawing.Size(608, 453);
			this.splitContainer1.SplitterDistance = 79;
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
			this.panel_Graphs.Size = new System.Drawing.Size(608, 370);
			this.panel_Graphs.TabIndex = 1;
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
			this.label5.Location = new System.Drawing.Point(2, 11);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(603, 24);
			this.label5.TabIndex = 0;
			this.label5.Text = "Ping response graphs will appear here.";
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
			this.cbTraceroute.Location = new System.Drawing.Point(237, 37);
			this.cbTraceroute.Name = "cbTraceroute";
			this.cbTraceroute.Size = new System.Drawing.Size(232, 17);
			this.cbTraceroute.TabIndex = 14;
			this.cbTraceroute.Text = "Graph every node leading to the destination";
			this.cbTraceroute.UseVisualStyleBackColor = true;
			// 
			// cbAlwaysShowServerNames
			// 
			this.cbAlwaysShowServerNames.AutoSize = true;
			this.cbAlwaysShowServerNames.Location = new System.Drawing.Point(97, 62);
			this.cbAlwaysShowServerNames.Name = "cbAlwaysShowServerNames";
			this.cbAlwaysShowServerNames.Size = new System.Drawing.Size(91, 17);
			this.cbAlwaysShowServerNames.TabIndex = 15;
			this.cbAlwaysShowServerNames.Text = "Server names";
			this.cbAlwaysShowServerNames.UseVisualStyleBackColor = true;
			this.cbAlwaysShowServerNames.CheckedChanged += new System.EventHandler(this.cbAlwaysShowServerNames_CheckedChanged);
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(13, 63);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(78, 13);
			this.label6.TabIndex = 16;
			this.label6.Text = "Graph Options:";
			// 
			// label8
			// 
			this.label8.AutoSize = true;
			this.label8.Location = new System.Drawing.Point(295, 63);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(75, 13);
			this.label8.TabIndex = 17;
			this.label8.Text = "Bad threshold:";
			// 
			// label9
			// 
			this.label9.AutoSize = true;
			this.label9.Location = new System.Drawing.Point(447, 63);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(87, 13);
			this.label9.TabIndex = 18;
			this.label9.Text = "Worse threshold:";
			// 
			// nudBadThreshold
			// 
			this.nudBadThreshold.Location = new System.Drawing.Point(376, 61);
			this.nudBadThreshold.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudBadThreshold.Name = "nudBadThreshold";
			this.nudBadThreshold.Size = new System.Drawing.Size(56, 20);
			this.nudBadThreshold.TabIndex = 19;
			this.nudBadThreshold.Value = new decimal(new int[] {
            100,
            0,
            0,
            0});
			this.nudBadThreshold.ValueChanged += new System.EventHandler(this.nudBadThreshold_ValueChanged);
			// 
			// nudWorseThreshold
			// 
			this.nudWorseThreshold.Location = new System.Drawing.Point(540, 61);
			this.nudWorseThreshold.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
			this.nudWorseThreshold.Name = "nudWorseThreshold";
			this.nudWorseThreshold.Size = new System.Drawing.Size(56, 20);
			this.nudWorseThreshold.TabIndex = 20;
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
			this.cbMinMax.Location = new System.Drawing.Point(199, 62);
			this.cbMinMax.Name = "cbMinMax";
			this.cbMinMax.Size = new System.Drawing.Size(72, 17);
			this.cbMinMax.TabIndex = 21;
			this.cbMinMax.Text = "min / max";
			this.cbMinMax.UseVisualStyleBackColor = true;
			this.cbMinMax.CheckedChanged += new System.EventHandler(this.cbMinMax_CheckedChanged);
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(608, 561);
			this.Controls.Add(this.cbMinMax);
			this.Controls.Add(this.nudWorseThreshold);
			this.Controls.Add(this.nudBadThreshold);
			this.Controls.Add(this.label9);
			this.Controls.Add(this.label8);
			this.Controls.Add(this.label6);
			this.Controls.Add(this.cbAlwaysShowServerNames);
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
			this.Controls.Add(this.label1);
			this.Controls.Add(this.txtHost);
			this.MinimumSize = new System.Drawing.Size(300, 200);
			this.Name = "MainForm";
			this.Text = "Ping Test";
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
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox txtOut;
		private System.Windows.Forms.TextBox txtHost;
		private System.Windows.Forms.Label label1;
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
	}
}

