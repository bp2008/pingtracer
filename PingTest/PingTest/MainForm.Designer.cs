namespace PingTracer
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
			components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
			panel_Graphs = new System.Windows.Forms.Panel();
			label5 = new System.Windows.Forms.Label();
			label4 = new System.Windows.Forms.Label();
			lblSuccessful = new System.Windows.Forms.Label();
			label7 = new System.Windows.Forms.Label();
			lblFailed = new System.Windows.Forms.Label();
			lblHoveredPingStatus = new System.Windows.Forms.Label();
			lblStatus = new System.Windows.Forms.Label();
			toolTip1 = new System.Windows.Forms.ToolTip(components);
			mainMenu1 = new System.Windows.Forms.MenuStrip();
			menuItem6 = new System.Windows.Forms.ToolStripMenuItem();
			mi_Configuration = new System.Windows.Forms.ToolStripMenuItem();
			mi_Exit = new System.Windows.Forms.ToolStripMenuItem();
			menuItem1 = new System.Windows.Forms.ToolStripMenuItem();
			mi_snapshotGraphs = new System.Windows.Forms.ToolStripMenuItem();
			menuItem4 = new System.Windows.Forms.ToolStripMenuItem();
			mi_Options = new System.Windows.Forms.ToolStripMenuItem();
			mi_OutputLog = new System.Windows.Forms.ToolStripMenuItem();
			menuItem_OpenSettingsFolder = new System.Windows.Forms.ToolStripMenuItem();
			menuItem_CommandLineArgs = new System.Windows.Forms.ToolStripMenuItem();
			menuItem_resetWindowSize = new System.Windows.Forms.ToolStripMenuItem();
			cbConfigurations = new System.Windows.Forms.ComboBox();
			btnEdit = new System.Windows.Forms.Button();
			btnStart = new System.Windows.Forms.Button();
			panel_Graphs.SuspendLayout();
			mainMenu1.SuspendLayout();
			SuspendLayout();
			// 
			// panel_Graphs
			// 
			panel_Graphs.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			panel_Graphs.BackColor = System.Drawing.SystemColors.Window;
			panel_Graphs.Controls.Add(label5);
			panel_Graphs.Location = new System.Drawing.Point(0, 55);
			panel_Graphs.Margin = new System.Windows.Forms.Padding(4);
			panel_Graphs.Name = "panel_Graphs";
			panel_Graphs.Size = new System.Drawing.Size(682, 318);
			panel_Graphs.TabIndex = 16;
			panel_Graphs.Click += panel_Graphs_Click;
			panel_Graphs.Resize += panel_Graphs_Resize;
			// 
			// label5
			// 
			label5.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			label5.BackColor = System.Drawing.SystemColors.Window;
			label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 14F);
			label5.ForeColor = System.Drawing.SystemColors.ControlText;
			label5.Location = new System.Drawing.Point(3, 34);
			label5.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			label5.Name = "label5";
			label5.Size = new System.Drawing.Size(676, 268);
			label5.TabIndex = 0;
			label5.Text = "Ping response graphs will appear here. \r\n\r\nClick the graph(s) to maximize them and remove window borders.\r\n\r\nUse File > Configuration to set up ping targets.";
			label5.TextAlign = System.Drawing.ContentAlignment.TopCenter;
			label5.Click += panel_Graphs_Click;
			// 
			// label4
			// 
			label4.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			label4.AutoSize = true;
			label4.Location = new System.Drawing.Point(14, 376);
			label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			label4.Name = "label4";
			label4.Size = new System.Drawing.Size(65, 15);
			label4.TabIndex = 8;
			label4.Text = "Successful:";
			// 
			// lblSuccessful
			// 
			lblSuccessful.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			lblSuccessful.AutoSize = true;
			lblSuccessful.Location = new System.Drawing.Point(94, 376);
			lblSuccessful.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			lblSuccessful.Name = "lblSuccessful";
			lblSuccessful.Size = new System.Drawing.Size(13, 15);
			lblSuccessful.TabIndex = 9;
			lblSuccessful.Text = "0";
			// 
			// label7
			// 
			label7.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			label7.AutoSize = true;
			label7.Location = new System.Drawing.Point(189, 376);
			label7.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			label7.Name = "label7";
			label7.Size = new System.Drawing.Size(41, 15);
			label7.TabIndex = 10;
			label7.Text = "Failed:";
			// 
			// lblFailed
			// 
			lblFailed.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			lblFailed.AutoSize = true;
			lblFailed.Location = new System.Drawing.Point(241, 376);
			lblFailed.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			lblFailed.Name = "lblFailed";
			lblFailed.Size = new System.Drawing.Size(13, 15);
			lblFailed.TabIndex = 11;
			lblFailed.Text = "0";
			// 
			// lblHoveredPingStatus
			// 
			lblHoveredPingStatus.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			lblHoveredPingStatus.AutoSize = true;
			lblHoveredPingStatus.Location = new System.Drawing.Point(322, 376);
			lblHoveredPingStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			lblHoveredPingStatus.Name = "lblHoveredPingStatus";
			lblHoveredPingStatus.Size = new System.Drawing.Size(7, 15);
			lblHoveredPingStatus.TabIndex = 13;
			lblHoveredPingStatus.Text = "\t";
			// 
			// lblStatus
			// 
			lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			lblStatus.Location = new System.Drawing.Point(326, 376);
			lblStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			lblStatus.Name = "lblStatus";
			lblStatus.Size = new System.Drawing.Size(346, 15);
			lblStatus.TabIndex = 14;
			lblStatus.Text = "Idle";
			lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// toolTip1
			// 
			toolTip1.AutomaticDelay = 250;
			toolTip1.AutoPopDelay = 10000;
			toolTip1.InitialDelay = 250;
			toolTip1.ReshowDelay = 50;
			// 
			// mainMenu1
			// 
			mainMenu1.ImageScalingSize = new System.Drawing.Size(20, 20);
			mainMenu1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { menuItem6, menuItem1, menuItem4 });
			mainMenu1.Location = new System.Drawing.Point(0, 0);
			mainMenu1.Name = "mainMenu1";
			mainMenu1.Padding = new System.Windows.Forms.Padding(7, 2, 0, 2);
			mainMenu1.Size = new System.Drawing.Size(682, 24);
			mainMenu1.TabIndex = 20;
			// 
			// menuItem6
			// 
			menuItem6.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { mi_Configuration, mi_Exit });
			menuItem6.Name = "menuItem6";
			menuItem6.Size = new System.Drawing.Size(37, 20);
			menuItem6.Text = "&File";
			// 
			// mi_Configuration
			// 
			mi_Configuration.Name = "mi_Configuration";
			mi_Configuration.Size = new System.Drawing.Size(157, 22);
			mi_Configuration.Text = "&Configuration...";
			mi_Configuration.Click += mi_Configuration_Click;
			// 
			// mi_Exit
			// 
			mi_Exit.Name = "mi_Exit";
			mi_Exit.Size = new System.Drawing.Size(157, 22);
			mi_Exit.Text = "E&xit";
			mi_Exit.Click += mi_Exit_Click;
			// 
			// menuItem1
			// 
			menuItem1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { mi_snapshotGraphs });
			menuItem1.Name = "menuItem1";
			menuItem1.Size = new System.Drawing.Size(52, 20);
			menuItem1.Text = "E&xport";
			// 
			// mi_snapshotGraphs
			// 
			mi_snapshotGraphs.Name = "mi_snapshotGraphs";
			mi_snapshotGraphs.Size = new System.Drawing.Size(176, 22);
			mi_snapshotGraphs.Text = "&Snapshot of graphs";
			mi_snapshotGraphs.Click += mi_snapshotGraphs_Click;
			// 
			// menuItem4
			// 
			menuItem4.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { mi_Options, mi_OutputLog, menuItem_OpenSettingsFolder, menuItem_CommandLineArgs, menuItem_resetWindowSize });
			menuItem4.Name = "menuItem4";
			menuItem4.Size = new System.Drawing.Size(47, 20);
			menuItem4.Text = "&Tools";
			// 
			// mi_Options
			// 
			mi_Options.Name = "mi_Options";
			mi_Options.Size = new System.Drawing.Size(188, 22);
			mi_Options.Text = "&Options";
			mi_Options.Click += mi_Options_Click;
			// 
			// mi_OutputLog
			// 
			mi_OutputLog.Name = "mi_OutputLog";
			mi_OutputLog.Size = new System.Drawing.Size(188, 22);
			mi_OutputLog.Text = "&Log Messages";
			mi_OutputLog.Click += mi_OutputLog_Click;
			// 
			// menuItem_OpenSettingsFolder
			// 
			menuItem_OpenSettingsFolder.Name = "menuItem_OpenSettingsFolder";
			menuItem_OpenSettingsFolder.Size = new System.Drawing.Size(188, 22);
			menuItem_OpenSettingsFolder.Text = "Open &AppData Folder";
			menuItem_OpenSettingsFolder.Click += menuItem_OpenSettingsFolder_Click;
			// 
			// menuItem_CommandLineArgs
			// 
			menuItem_CommandLineArgs.Name = "menuItem_CommandLineArgs";
			menuItem_CommandLineArgs.Size = new System.Drawing.Size(188, 22);
			menuItem_CommandLineArgs.Text = "&Command Line Args";
			menuItem_CommandLineArgs.Click += menuItem_CommandLineArgs_Click;
			// 
			// menuItem_resetWindowSize
			// 
			menuItem_resetWindowSize.Name = "menuItem_resetWindowSize";
			menuItem_resetWindowSize.Size = new System.Drawing.Size(188, 22);
			menuItem_resetWindowSize.Text = "&Reset Window Size";
			menuItem_resetWindowSize.Click += menuItem_resetWindowSize_Click;
			// 
			// cbConfigurations
			// 
			cbConfigurations.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			cbConfigurations.FormattingEnabled = true;
			cbConfigurations.Location = new System.Drawing.Point(7, 28);
			cbConfigurations.Margin = new System.Windows.Forms.Padding(4);
			cbConfigurations.Name = "cbConfigurations";
			cbConfigurations.Size = new System.Drawing.Size(248, 23);
			cbConfigurations.TabIndex = 17;
			cbConfigurations.DropDown += cbConfigurations_DropDown;
			cbConfigurations.SelectedIndexChanged += cbConfigurations_SelectedIndexChanged;
			// 
			// btnEdit
			// 
			btnEdit.Location = new System.Drawing.Point(262, 26);
			btnEdit.Margin = new System.Windows.Forms.Padding(4);
			btnEdit.Name = "btnEdit";
			btnEdit.Size = new System.Drawing.Size(67, 26);
			btnEdit.TabIndex = 18;
			btnEdit.Text = "Edit";
			btnEdit.UseVisualStyleBackColor = true;
			btnEdit.Click += tsbEdit_Click;
			// 
			// btnStart
			// 
			btnStart.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			btnStart.Location = new System.Drawing.Point(537, 26);
			btnStart.Margin = new System.Windows.Forms.Padding(4);
			btnStart.Name = "btnStart";
			btnStart.Size = new System.Drawing.Size(140, 26);
			btnStart.TabIndex = 19;
			btnStart.Tag = "Start";
			btnStart.Text = "Click to Start";
			btnStart.UseVisualStyleBackColor = true;
			btnStart.Click += btnStart_Click;
			// 
			// MainForm
			// 
			AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			ClientSize = new System.Drawing.Size(682, 392);
			Controls.Add(btnStart);
			Controls.Add(btnEdit);
			Controls.Add(cbConfigurations);
			Controls.Add(lblStatus);
			Controls.Add(lblHoveredPingStatus);
			Controls.Add(panel_Graphs);
			Controls.Add(lblFailed);
			Controls.Add(label7);
			Controls.Add(lblSuccessful);
			Controls.Add(label4);
			Controls.Add(mainMenu1);
			Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
			MainMenuStrip = mainMenu1;
			Margin = new System.Windows.Forms.Padding(4);
			MinimumSize = new System.Drawing.Size(347, 219);
			Name = "MainForm";
			Text = "Ping Tracer";
			FormClosing += MainForm_FormClosing;
			Load += MainForm_Load;
			Click += MainForm_Click;
			panel_Graphs.ResumeLayout(false);
			mainMenu1.ResumeLayout(false);
			mainMenu1.PerformLayout();
			ResumeLayout(false);
			PerformLayout();

		}

		#endregion
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label lblSuccessful;
		private System.Windows.Forms.Label lblFailed;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label lblHoveredPingStatus;
		private System.Windows.Forms.Label lblStatus;
		private System.Windows.Forms.Panel panel_Graphs;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.MenuStrip mainMenu1;
		private System.Windows.Forms.ToolStripMenuItem menuItem1;
		private System.Windows.Forms.ToolStripMenuItem mi_snapshotGraphs;
		private System.Windows.Forms.ToolStripMenuItem menuItem4;
		private System.Windows.Forms.ToolStripMenuItem mi_Options;
		private System.Windows.Forms.ToolStripMenuItem mi_OutputLog;
		private System.Windows.Forms.ToolStripMenuItem menuItem6;
		private System.Windows.Forms.ToolStripMenuItem mi_Configuration;
		private System.Windows.Forms.ToolStripMenuItem mi_Exit;
		private System.Windows.Forms.ToolStripMenuItem menuItem_OpenSettingsFolder;
		private System.Windows.Forms.ToolStripMenuItem menuItem_CommandLineArgs;
		private System.Windows.Forms.ToolStripMenuItem menuItem_resetWindowSize;
		private System.Windows.Forms.ComboBox cbConfigurations;
		private System.Windows.Forms.Button btnEdit;
		private System.Windows.Forms.Button btnStart;
	}
}
