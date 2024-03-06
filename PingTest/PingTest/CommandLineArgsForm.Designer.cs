
namespace PingTracer
{
	partial class CommandLineArgsForm
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CommandLineArgsForm));
			this.txtDocumentation = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
			this.txtArgs = new System.Windows.Forms.TextBox();
			this.SuspendLayout();
			// 
			// txtDocumentation
			// 
			this.txtDocumentation.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.txtDocumentation.BackColor = System.Drawing.SystemColors.Window;
			this.txtDocumentation.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.txtDocumentation.Location = new System.Drawing.Point(12, 12);
			this.txtDocumentation.Multiline = true;
			this.txtDocumentation.Name = "txtDocumentation";
			this.txtDocumentation.ReadOnly = true;
			this.txtDocumentation.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.txtDocumentation.Size = new System.Drawing.Size(664, 331);
			this.txtDocumentation.TabIndex = 2;
			this.txtDocumentation.Text = resources.GetString("txtDocumentation.Text");
			// 
			// label1
			// 
			this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 346);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(168, 13);
			this.label1.TabIndex = 1;
			this.label1.Text = "Arguments to start in current state:";
			// 
			// txtArgs
			// 
			this.txtArgs.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.txtArgs.BackColor = System.Drawing.SystemColors.Window;
			this.txtArgs.Location = new System.Drawing.Point(12, 362);
			this.txtArgs.Name = "txtArgs";
			this.txtArgs.ReadOnly = true;
			this.txtArgs.Size = new System.Drawing.Size(664, 20);
			this.txtArgs.TabIndex = 0;
			// 
			// CommandLineArgsForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(688, 394);
			this.Controls.Add(this.txtArgs);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.txtDocumentation);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
			this.Name = "CommandLineArgsForm";
			this.Text = "CommandLineArgsForm";
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.CommandLineArgsForm_FormClosing);
			this.Load += new System.EventHandler(this.CommandLineArgsForm_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox txtDocumentation;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox txtArgs;
	}
}