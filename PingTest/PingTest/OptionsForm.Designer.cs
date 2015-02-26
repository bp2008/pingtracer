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
			this.cbLogToFile = new System.Windows.Forms.CheckBox();
			this.SuspendLayout();
			// 
			// cbLogToFile
			// 
			this.cbLogToFile.AutoSize = true;
			this.cbLogToFile.Location = new System.Drawing.Point(12, 12);
			this.cbLogToFile.Name = "cbLogToFile";
			this.cbLogToFile.Size = new System.Drawing.Size(125, 17);
			this.cbLogToFile.TabIndex = 7;
			this.cbLogToFile.Text = "Log text output to file";
			this.cbLogToFile.UseVisualStyleBackColor = true;
			this.cbLogToFile.CheckedChanged += new System.EventHandler(this.cbLogToFile_CheckedChanged);
			// 
			// OptionsForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(283, 43);
			this.Controls.Add(this.cbLogToFile);
			this.Name = "OptionsForm";
			this.Text = "Ping Tracer Options";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.CheckBox cbLogToFile;
	}
}