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
			this.cbLogToFile.TabIndex = 7;
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
			this.cbDelayMostRecentPing.TabIndex = 9;
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
			// OptionsForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(283, 81);
			this.Controls.Add(this.cbDelayMostRecentPing);
			this.Controls.Add(this.cbLogToFile);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.Name = "OptionsForm";
			this.Text = "Ping Tracer Options";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.CheckBox cbLogToFile;
		private System.Windows.Forms.CheckBox cbDelayMostRecentPing;
		private System.Windows.Forms.ToolTip toolTip1;
	}
}