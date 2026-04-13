
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
			txtDocumentation = new System.Windows.Forms.TextBox();
			label1 = new System.Windows.Forms.Label();
			txtArgs = new System.Windows.Forms.TextBox();
			SuspendLayout();
			// 
			// txtDocumentation
			// 
			txtDocumentation.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			txtDocumentation.BackColor = System.Drawing.SystemColors.Window;
			txtDocumentation.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
			txtDocumentation.Location = new System.Drawing.Point(12, 12);
			txtDocumentation.Multiline = true;
			txtDocumentation.Name = "txtDocumentation";
			txtDocumentation.ReadOnly = true;
			txtDocumentation.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			txtDocumentation.Size = new System.Drawing.Size(664, 331);
			txtDocumentation.TabIndex = 2;
			txtDocumentation.Text = resources.GetString("txtDocumentation.Text");
			// 
			// label1
			// 
			label1.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
			label1.AutoSize = true;
			label1.Location = new System.Drawing.Point(12, 346);
			label1.Name = "label1";
			label1.Size = new System.Drawing.Size(191, 15);
			label1.TabIndex = 1;
			label1.Text = "Arguments to start in current state:";
			// 
			// txtArgs
			// 
			txtArgs.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			txtArgs.BackColor = System.Drawing.SystemColors.Window;
			txtArgs.Location = new System.Drawing.Point(12, 362);
			txtArgs.Name = "txtArgs";
			txtArgs.ReadOnly = true;
			txtArgs.Size = new System.Drawing.Size(664, 23);
			txtArgs.TabIndex = 0;
			// 
			// CommandLineArgsForm
			// 
			AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			ClientSize = new System.Drawing.Size(688, 394);
			Controls.Add(txtArgs);
			Controls.Add(label1);
			Controls.Add(txtDocumentation);
			FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
			Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
			Name = "CommandLineArgsForm";
			Text = "Command Line Args - PingTracer";
			FormClosing += CommandLineArgsForm_FormClosing;
			Load += CommandLineArgsForm_Load;
			ResumeLayout(false);
			PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox txtDocumentation;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox txtArgs;
	}
}