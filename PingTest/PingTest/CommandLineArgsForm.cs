using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PingTracer
{
	public partial class CommandLineArgsForm : Form
	{
		MainForm mainForm;
		StartupOptions options = new StartupOptions();
		public CommandLineArgsForm(MainForm mainForm)
		{
			this.mainForm = mainForm;

			InitializeComponent();

			this.SetLocationNearMouse();

			mainForm.Move += SetCommandLineArgs;
			mainForm.Resize += SetCommandLineArgs;
			mainForm.StartedPinging += SetCommandLineArgs;
			mainForm.StoppedPinging += SetCommandLineArgs;
			mainForm.SelectedHostChanged += SetCommandLineArgs;
			mainForm.MaximizeGraphsChanged += SetCommandLineArgs;

			SetCommandLineArgs();
		}

		private void SetCommandLineArgs(object sender, EventArgs e)
		{
			SetCommandLineArgs();
		}

		private void SetCommandLineArgs()
		{
			options.WindowLocation = new StartupOptions.WindowParams(mainForm.Location.X, mainForm.Location.Y, mainForm.Size.Width, mainForm.Size.Height);

			options.StartupHostName = mainForm.txtDisplayName.Text;
			if (string.IsNullOrWhiteSpace(options.StartupHostName))
				options.StartupHostName = mainForm.txtHost.Text;
			if (string.IsNullOrWhiteSpace(options.StartupHostName))
				options.StartupHostName = null;

			options.StartPinging = mainForm.isRunning;

			options.MaximizeGraphs = mainForm.graphsMaximized;

			txtArgs.Text = options.ToString();
		}

		private void CommandLineArgsForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			mainForm.Move -= SetCommandLineArgs;
			mainForm.Resize -= SetCommandLineArgs;
			mainForm.StartedPinging -= SetCommandLineArgs;
			mainForm.StoppedPinging -= SetCommandLineArgs;
			mainForm.SelectedHostChanged -= SetCommandLineArgs;
			mainForm.MaximizeGraphsChanged -= SetCommandLineArgs;
		}

		private void CommandLineArgsForm_Load(object sender, EventArgs e)
		{
			txtDocumentation.Select(0, 0);
			txtArgs.Select(0, 0);
			txtArgs.Focus();
			txtArgs.SelectAll();
		}
	}
}
