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
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
			this.panel_Graphs = new System.Windows.Forms.Panel();
			this.label5 = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this.lblSuccessful = new System.Windows.Forms.Label();
			this.label7 = new System.Windows.Forms.Label();
			this.lblFailed = new System.Windows.Forms.Label();
			this.lblHoveredPingStatus = new System.Windows.Forms.Label();
			this.lblStatus = new System.Windows.Forms.Label();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.mainMenu1 = new System.Windows.Forms.MainMenu(this.components);
			this.menuItem6 = new System.Windows.Forms.MenuItem();
			this.mi_Configuration = new System.Windows.Forms.MenuItem();
			this.mi_Exit = new System.Windows.Forms.MenuItem();
			this.menuItem1 = new System.Windows.Forms.MenuItem();
			this.mi_snapshotGraphs = new System.Windows.Forms.MenuItem();
			this.menuItem4 = new System.Windows.Forms.MenuItem();
			this.mi_Options = new System.Windows.Forms.MenuItem();
			this.mi_OutputLog = new System.Windows.Forms.MenuItem();
			this.menuItem_OpenSettingsFolder = new System.Windows.Forms.MenuItem();
			this.menuItem_CommandLineArgs = new System.Windows.Forms.MenuItem();
			this.menuItem_resetWindowSize = new System.Windows.Forms.MenuItem();
			this.cbConfigurations = new System.Windows.Forms.ComboBox();
			this.btnEdit = new System.Windows.Forms.Button();
			this.btnStart = new System.Windows.Forms.Button();
			this.panel_Graphs.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel_Graphs
			// 
			this.panel_Graphs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.panel_Graphs.BackColor = System.Drawing.SystemColors.Window;
			this.panel_Graphs.Controls.Add(this.label5);
			this.panel_Graphs.Location = new System.Drawing.Point(0, 31);
			this.panel_Graphs.Name = "panel_Graphs";
			this.panel_Graphs.Size = new System.Drawing.Size(584, 292);
			this.panel_Graphs.TabIndex = 16;
			this.panel_Graphs.Click += new System.EventHandler(this.panel_Graphs_Click);
			this.panel_Graphs.Resize += new System.EventHandler(this.panel_Graphs_Resize);
			// 
			// label5
			// 
			this.label5.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.label5.BackColor = System.Drawing.SystemColors.Window;
			this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 14F);
			this.label5.ForeColor = System.Drawing.SystemColors.ControlText;
			this.label5.Location = new System.Drawing.Point(2, 30);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(579, 249);
			this.label5.TabIndex = 0;
			this.label5.Text = "Ping response graphs will appear here. \r\n\r\nClick the graph(s) to maximize them an" +
    "d remove window borders.\r\n\r\nUse File > Configuration to set up ping targets.";
			this.label5.TextAlign = System.Drawing.ContentAlignment.TopCenter;
			this.label5.Click += new System.EventHandler(this.panel_Graphs_Click);
			// 
			// label4
			// 
			this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(12, 326);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(62, 13);
			this.label4.TabIndex = 8;
			this.label4.Text = "Successful:";
			// 
			// lblSuccessful
			// 
			this.lblSuccessful.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblSuccessful.AutoSize = true;
			this.lblSuccessful.Location = new System.Drawing.Point(80, 326);
			this.lblSuccessful.Name = "lblSuccessful";
			this.lblSuccessful.Size = new System.Drawing.Size(13, 13);
			this.lblSuccessful.TabIndex = 9;
			this.lblSuccessful.Text = "0";
			// 
			// label7
			// 
			this.label7.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label7.AutoSize = true;
			this.label7.Location = new System.Drawing.Point(162, 326);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(38, 13);
			this.label7.TabIndex = 10;
			this.label7.Text = "Failed:";
			// 
			// lblFailed
			// 
			this.lblFailed.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblFailed.AutoSize = true;
			this.lblFailed.Location = new System.Drawing.Point(206, 326);
			this.lblFailed.Name = "lblFailed";
			this.lblFailed.Size = new System.Drawing.Size(13, 13);
			this.lblFailed.TabIndex = 11;
			this.lblFailed.Text = "0";
			// 
			// lblHoveredPingStatus
			// 
			this.lblHoveredPingStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblHoveredPingStatus.AutoSize = true;
			this.lblHoveredPingStatus.Location = new System.Drawing.Point(276, 326);
			this.lblHoveredPingStatus.Name = "lblHoveredPingStatus";
			this.lblHoveredPingStatus.Size = new System.Drawing.Size(7, 13);
			this.lblHoveredPingStatus.TabIndex = 13;
			this.lblHoveredPingStatus.Text = "\t";
			// 
			// lblStatus
			// 
			this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.lblStatus.Location = new System.Drawing.Point(279, 326);
			this.lblStatus.Name = "lblStatus";
			this.lblStatus.Size = new System.Drawing.Size(297, 13);
			this.lblStatus.TabIndex = 14;
			this.lblStatus.Text = "Idle";
			this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// toolTip1
			// 
			this.toolTip1.AutomaticDelay = 250;
			this.toolTip1.AutoPopDelay = 10000;
			this.toolTip1.InitialDelay = 250;
			this.toolTip1.ReshowDelay = 50;
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
            this.mi_Configuration,
            this.mi_Exit});
			this.menuItem6.Text = "&File";
			// 
			// mi_Configuration
			// 
			this.mi_Configuration.Index = 0;
			this.mi_Configuration.Text = "&Configuration...";
			this.mi_Configuration.Click += new System.EventHandler(this.mi_Configuration_Click);
			// 
			// mi_Exit
			// 
			this.mi_Exit.Index = 1;
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
            this.mi_Options,
            this.mi_OutputLog,
            this.menuItem_OpenSettingsFolder,
            this.menuItem_CommandLineArgs,
            this.menuItem_resetWindowSize});
			this.menuItem4.Text = "&Tools";
			// 
			// mi_Options
			// 
			this.mi_Options.Index = 0;
			this.mi_Options.Text = "&Options";
			this.mi_Options.Click += new System.EventHandler(this.mi_Options_Click);
			// 
			// mi_OutputLog
			// 
			this.mi_OutputLog.Index = 1;
			this.mi_OutputLog.Text = "&Log Messages";
			this.mi_OutputLog.Click += new System.EventHandler(this.mi_OutputLog_Click);
			// 
			// menuItem_OpenSettingsFolder
			// 
			this.menuItem_OpenSettingsFolder.Index = 2;
			this.menuItem_OpenSettingsFolder.Text = "Open &Settings Folder";
			this.menuItem_OpenSettingsFolder.Click += new System.EventHandler(this.menuItem_OpenSettingsFolder_Click);
			// 
			// menuItem_CommandLineArgs
			// 
			this.menuItem_CommandLineArgs.Index = 3;
			this.menuItem_CommandLineArgs.Text = "&Command Line Args";
			this.menuItem_CommandLineArgs.Click += new System.EventHandler(this.menuItem_CommandLineArgs_Click);
			// 
			// menuItem_resetWindowSize
			// 
			this.menuItem_resetWindowSize.Index = 4;
			this.menuItem_resetWindowSize.Text = "&Reset Window Size";
			this.menuItem_resetWindowSize.Click += new System.EventHandler(this.menuItem_resetWindowSize_Click);
			// 
			// cbConfigurations
			// 
			this.cbConfigurations.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbConfigurations.FormattingEnabled = true;
			this.cbConfigurations.Location = new System.Drawing.Point(6, 4);
			this.cbConfigurations.Name = "cbConfigurations";
			this.cbConfigurations.Size = new System.Drawing.Size(213, 21);
			this.cbConfigurations.TabIndex = 17;
			this.cbConfigurations.DropDown += new System.EventHandler(this.cbConfigurations_DropDown);
			this.cbConfigurations.SelectedIndexChanged += new System.EventHandler(this.cbConfigurations_SelectedIndexChanged);
			// 
			// btnEdit
			// 
			this.btnEdit.Location = new System.Drawing.Point(225, 2);
			this.btnEdit.Name = "btnEdit";
			this.btnEdit.Size = new System.Drawing.Size(58, 23);
			this.btnEdit.TabIndex = 18;
			this.btnEdit.Text = "Edit";
			this.btnEdit.UseVisualStyleBackColor = true;
			this.btnEdit.Click += new System.EventHandler(this.tsbEdit_Click);
			// 
			// btnStart
			// 
			this.btnStart.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.btnStart.Location = new System.Drawing.Point(452, 2);
			this.btnStart.Name = "btnStart";
			this.btnStart.Size = new System.Drawing.Size(120, 23);
			this.btnStart.TabIndex = 19;
			this.btnStart.Tag = "Start";
			this.btnStart.Text = "Click to Start";
			this.btnStart.UseVisualStyleBackColor = true;
			this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(584, 340);
			this.Controls.Add(this.btnStart);
			this.Controls.Add(this.btnEdit);
			this.Controls.Add(this.cbConfigurations);
			this.Controls.Add(this.lblStatus);
			this.Controls.Add(this.lblHoveredPingStatus);
			this.Controls.Add(this.panel_Graphs);
			this.Controls.Add(this.lblFailed);
			this.Controls.Add(this.label7);
			this.Controls.Add(this.lblSuccessful);
			this.Controls.Add(this.label4);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.Menu = this.mainMenu1;
			this.MinimumSize = new System.Drawing.Size(300, 198);
			this.Name = "MainForm";
			this.Text = "Ping Tracer";
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
			this.Load += new System.EventHandler(this.MainForm_Load);
			this.Click += new System.EventHandler(this.MainForm_Click);
			this.panel_Graphs.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

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
		private System.Windows.Forms.MainMenu mainMenu1;
		private System.Windows.Forms.MenuItem menuItem1;
		private System.Windows.Forms.MenuItem mi_snapshotGraphs;
		private System.Windows.Forms.MenuItem menuItem4;
		private System.Windows.Forms.MenuItem mi_Options;
		private System.Windows.Forms.MenuItem mi_OutputLog;
		private System.Windows.Forms.MenuItem menuItem6;
		private System.Windows.Forms.MenuItem mi_Configuration;
		private System.Windows.Forms.MenuItem mi_Exit;
		private System.Windows.Forms.MenuItem menuItem_OpenSettingsFolder;
		private System.Windows.Forms.MenuItem menuItem_CommandLineArgs;
		private System.Windows.Forms.MenuItem menuItem_resetWindowSize;
		private System.Windows.Forms.ComboBox cbConfigurations;
		private System.Windows.Forms.Button btnEdit;
		private System.Windows.Forms.Button btnStart;
	}
}
