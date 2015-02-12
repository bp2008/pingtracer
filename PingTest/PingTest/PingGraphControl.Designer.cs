namespace PingTest
{
	partial class PingGraphControl
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

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.SuspendLayout();
			// 
			// PingGraphControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Cursor = System.Windows.Forms.Cursors.Default;
			this.DoubleBuffered = true;
			this.Name = "PingGraphControl";
			this.Size = new System.Drawing.Size(610, 110);
			this.Paint += new System.Windows.Forms.PaintEventHandler(this.PingGraphControl_Paint);
			this.MouseLeave += new System.EventHandler(this.PingGraphControl_MouseLeave);
			this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.PingGraphControl_MouseMove);
			this.Resize += new System.EventHandler(this.PingGraphControl_Resize);
			this.ResumeLayout(false);

		}

		#endregion
	}
}
