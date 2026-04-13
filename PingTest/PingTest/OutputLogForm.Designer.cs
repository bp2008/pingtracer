namespace PingTracer
{
	partial class OutputLogForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OutputLogForm));
			txtLog = new System.Windows.Forms.TextBox();
			SuspendLayout();
			// 
			// txtLog
			// 
			txtLog.BackColor = System.Drawing.SystemColors.Window;
			txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
			txtLog.Location = new System.Drawing.Point(0, 0);
			txtLog.Multiline = true;
			txtLog.Name = "txtLog";
			txtLog.ReadOnly = true;
			txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			txtLog.Size = new System.Drawing.Size(584, 261);
			txtLog.TabIndex = 0;
			// 
			// OutputLogForm
			// 
			AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			ClientSize = new System.Drawing.Size(584, 261);
			Controls.Add(txtLog);
			Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
			Name = "OutputLogForm";
			Text = "Log Messages - PingTracer";
			ResumeLayout(false);
			PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox txtLog;
	}
}
