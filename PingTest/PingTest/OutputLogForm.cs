using System;
using System.Windows.Forms;

namespace PingTracer
{
	public partial class OutputLogForm : Form
	{
		public OutputLogForm()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Loads an array of log lines into the text box, replacing any existing content.
		/// Must be called from the UI thread before the form is shown.
		/// </summary>
		public void LoadLines(string[] lines)
		{
			if (lines.Length > 0)
				txtLog.Text = string.Join(Environment.NewLine, lines);
			// Place caret at end and scroll to bottom instead of selecting all text
			txtLog.SelectionStart = txtLog.TextLength;
			txtLog.SelectionLength = 0;
			txtLog.ScrollToCaret();
		}

		/// <summary>
		/// Appends a log entry to the output text box. Thread-safe.
		/// </summary>
		public void AppendLog(string text)
		{
			if (IsDisposed || !IsHandleCreated)
				return;
			try
			{
				if (txtLog.InvokeRequired)
					txtLog.BeginInvoke(new Action<string>(AppendLogInternal), text);
				else
					AppendLogInternal(text);
			}
			catch (ObjectDisposedException) { }
			catch (InvalidOperationException) { }
		}

		private void AppendLogInternal(string text)
		{
			if (IsDisposed || txtLog.IsDisposed)
				return;
			if (txtLog.TextLength > 500000)
				txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 400000);
			txtLog.AppendText(Environment.NewLine + text);
			// Ensure we scroll to the bottom after appending
			txtLog.SelectionStart = txtLog.TextLength;
			txtLog.ScrollToCaret();
		}
	}
}
