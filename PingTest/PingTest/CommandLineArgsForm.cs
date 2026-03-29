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

		private void CommandLineArgsForm_Load(object sender, EventArgs e)
		{
			txtDocumentation.Select(0, 0);
			txtArgs.Select(0, 0);
			txtArgs.Focus();
			txtArgs.SelectAll();
		}

		private void SetCommandLineArgs(object sender, EventArgs e)
		{
			SetCommandLineArgs();
		}
		int mT => mainForm.settings.osWindowTopMargin;
		int mL => mainForm.settings.osWindowLeftMargin;
		int mR => mainForm.settings.osWindowRightMargin;
		int mB => mainForm.settings.osWindowBottomMargin;
		private void SetCommandLineArgs()
		{
			options.WindowLocation = new WindowParams(mainForm.Location.X + mL,
				mainForm.Location.Y + mT,
				mainForm.Size.Width - (mL + mR),
				mainForm.Size.Height - (mT + mB));

			var cfg = mainForm.currentConfiguration;
			if (cfg != null)
			{
				options.StartupHostName = cfg.displayName;
				if (string.IsNullOrWhiteSpace(options.StartupHostName))
					options.StartupHostName = cfg.GetHostString();
				if (string.IsNullOrWhiteSpace(options.StartupHostName))
					options.StartupHostName = null;

				options.PreferIPv6 = cfg.GetPreferIPv4() ? BoolOverride.False : BoolOverride.True;
				options.TraceRoute = cfg.doTraceRoute ? BoolOverride.True : BoolOverride.False;
			}
			else
			{
				options.StartupHostName = null;
				options.PreferIPv6 = BoolOverride.Inherit;
				options.TraceRoute = BoolOverride.Inherit;
			}

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
	}
}
