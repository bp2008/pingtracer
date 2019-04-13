namespace PingTracer
{
	partial class TestForm
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
			this.panel_Graphs = new System.Windows.Forms.Panel();
			this.txtOut = new System.Windows.Forms.TextBox();
			this.panel_Graphs.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel_Graphs
			// 
			this.panel_Graphs.Controls.Add(this.txtOut);
			this.panel_Graphs.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel_Graphs.Location = new System.Drawing.Point(0, 0);
			this.panel_Graphs.Name = "panel_Graphs";
			this.panel_Graphs.Size = new System.Drawing.Size(800, 450);
			this.panel_Graphs.TabIndex = 0;
			// 
			// txtOut
			// 
			this.txtOut.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.txtOut.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.txtOut.Location = new System.Drawing.Point(3, 3);
			this.txtOut.Multiline = true;
			this.txtOut.Name = "txtOut";
			this.txtOut.Size = new System.Drawing.Size(794, 444);
			this.txtOut.TabIndex = 0;
			// 
			// TestForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(800, 450);
			this.Controls.Add(this.panel_Graphs);
			this.Name = "TestForm";
			this.Text = "TestForm";
			this.Load += new System.EventHandler(this.TestForm_Load);
			this.panel_Graphs.ResumeLayout(false);
			this.panel_Graphs.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel_Graphs;
		private System.Windows.Forms.TextBox txtOut;
	}
}