using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PingTracer
{
	public partial class OptionsForm : Form
	{
		private MainForm mainForm;

		public OptionsForm()
		{
			InitializeComponent();
		}

		public OptionsForm(MainForm mainForm)
		{
			this.mainForm = mainForm;
			InitializeComponent();
			cbLogToFile.Checked = mainForm.settings.logTextOutputToFile;
			cbDelayMostRecentPing.Checked = mainForm.settings.delayMostRecentPing;
			cbWarnGraphNotLive.Checked = mainForm.settings.warnGraphNotLive;
			nudPingResponsesToCache.Value = mainForm.settings.cacheSize;
			cbFastRefreshScrollingGraphs.Checked = mainForm.settings.fastRefreshScrollingGraphs;
			nudGraphScrollMultiplier.Value = mainForm.settings.graphScrollMultiplier;
			cbShowDateInCorner.Checked = mainForm.settings.showDateOnGraphTimeline;
		}

		private void cbLogToFile_CheckedChanged(object sender, EventArgs e)
		{
			mainForm.settings.logTextOutputToFile = cbLogToFile.Checked;
			mainForm.settings.Save();
		}

		private void cbDelayMostRecentPing_CheckedChanged(object sender, EventArgs e)
		{
			mainForm.settings.delayMostRecentPing = cbDelayMostRecentPing.Checked;
			mainForm.settings.Save();
		}

		private void cbWarnGraphNotLive_CheckedChanged(object sender, EventArgs e)
		{
			mainForm.settings.warnGraphNotLive = cbWarnGraphNotLive.Checked;
			mainForm.settings.Save();
		}

		private void nudPingResponsesToCache_ValueChanged(object sender, EventArgs e)
		{
			mainForm.settings.cacheSize = (int)nudPingResponsesToCache.Value;
			mainForm.settings.Save();
		}

		private void cbFastRefreshScrollingGraphs_CheckedChanged(object sender, EventArgs e)
		{
			mainForm.settings.fastRefreshScrollingGraphs = cbFastRefreshScrollingGraphs.Checked;
			mainForm.settings.Save();
		}

		private void nudGraphScrollMultiplier_ValueChanged(object sender, EventArgs e)
		{
			mainForm.settings.graphScrollMultiplier = (int)nudGraphScrollMultiplier.Value;
			mainForm.settings.Save();
		}

		private void cbShowDateInCorner_CheckedChanged(object sender, EventArgs e)
		{
			mainForm.settings.showDateOnGraphTimeline = cbShowDateInCorner.Checked;
			mainForm.settings.Save();
		}

        private void cbOverlapTimestamps_CheckedChanged(object sender, EventArgs e)
        {
            mainForm.settings.overlapTimeText = cbOverlapTimestamps.Checked;
            mainForm.settings.Save();
        }
    }
}
