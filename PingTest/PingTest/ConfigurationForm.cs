using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PingTracer
{
	public partial class ConfigurationForm : Form
	{
		private MainForm mainForm;
		private PingConfigurations allConfigs;
		/// <summary>
		/// The Guid of the configuration currently being edited, or null if creating a new configuration.
		/// </summary>
		private string editingGuid = null;
		/// <summary>
		/// Set to true while loading data into the UI to suppress change handlers.
		/// </summary>
		private bool suppressEvents = false;

		/// <summary>
		/// A snapshot of the configuration as it was last loaded/saved, for dirty detection.
		/// </summary>
		private string savedStateSnapshot = null;

		/// <summary>
		/// Raised when the user clicks "Load Configuration" to apply a configuration.
		/// </summary>
		public event Action<PingConfiguration> ConfigurationLoaded;

		/// <summary>
		/// Raised when a configuration is saved, for live-apply of settings to a running ping.
		/// </summary>
		public event Action<PingConfiguration> ConfigurationSaved;

		/// <summary>
		/// Raised when any control value changes while editing the configuration that is currently loaded in MainForm.
		/// </summary>
		public event Action<PingConfiguration> ConfigurationEdited;

		/// <summary>
		/// Raised when changes are discarded for the configuration that is currently loaded in MainForm,
		/// providing the saved-state configuration to revert to.
		/// </summary>
		public event Action<PingConfiguration> ConfigurationEditDiscarded;

		public ConfigurationForm(MainForm mainForm)
		{
			this.mainForm = mainForm;
			InitializeComponent();
		}

		private void ConfigurationForm_Load(object sender, EventArgs e)
		{
			cbScalingMethod.SelectedIndex = 0;
			RefreshConfigurationList();
		}

		/// <summary>
		/// Returns true if the configuration being edited is the one currently loaded in MainForm.
		/// </summary>
		private bool IsEditingLoadedConfig()
		{
			return editingGuid != null && mainForm.currentConfiguration != null && editingGuid == mainForm.currentConfiguration.guid;
		}

		/// <summary>
		/// Updates the enabled state of the Load button based on whether the
		/// currently edited configuration is already loaded in MainForm.
		/// </summary>
		public void UpdateLoadButtonState()
		{
			if (editingGuid == null)
				btnLoad.Enabled = false;
			else
				btnLoad.Enabled = !IsEditingLoadedConfig();
		}

		/// <summary>
		/// Reloads hosts.cfg from disk and refreshes the tree.
		/// </summary>
		private void RefreshConfigurationList()
		{
			allConfigs = new PingConfigurations();
			allConfigs.Load();

			treeConfigurations.Nodes.Clear();

			TreeNode newNode = new TreeNode("+ New Configuration");
			newNode.Tag = null;
			newNode.ForeColor = Color.Blue;
			treeConfigurations.Nodes.Add(newNode);

			foreach (PingConfiguration cfg in allConfigs.configurations)
			{
				TreeNode node = new TreeNode(cfg.displayName);
				node.Tag = cfg.guid;
				treeConfigurations.Nodes.Add(node);
			}

			treeConfigurations.SelectedNode = treeConfigurations.Nodes[0];
		}

		private void treeConfigurations_AfterSelect(object sender, TreeViewEventArgs e)
		{
			if (suppressEvents)
				return;
			if (e.Node == null)
				return;

			// Check for unsaved changes before switching
			if (HasUnsavedChanges())
			{
				DialogResult dr = MessageBox.Show(
					"You have unsaved changes. Do you want to save them before switching?",
					"Unsaved Changes",
					MessageBoxButtons.YesNoCancel,
					MessageBoxIcon.Warning);
				if (dr == DialogResult.Yes)
				{
					if (!PerformSave())
					{
						// Save failed — cancel navigation
						suppressEvents = true;
						try { ReSelectCurrentNode(); }
						finally { suppressEvents = false; }
						return;
					}
					// Save succeeded. PerformSave called RefreshConfigurationList which
					// may have changed the selected node. Re-select the node the user
					// was clicking toward.
					suppressEvents = true;
					try
					{
						string targetGuid = e.Node.Tag as string;
						if (targetGuid != null)
						{
							// Find the node with the matching guid in the (refreshed) tree
							foreach (TreeNode n in treeConfigurations.Nodes)
							{
								if (targetGuid.Equals(n.Tag as string))
								{
									treeConfigurations.SelectedNode = n;
									break;
								}
							}
						}
						else
						{
							treeConfigurations.SelectedNode = treeConfigurations.Nodes[0];
						}
					}
					finally { suppressEvents = false; }
				}
				else if (dr == DialogResult.Cancel)
				{
					// Cancel — stay on the current node
					suppressEvents = true;
					try { ReSelectCurrentNode(); }
					finally { suppressEvents = false; }
					return;
				}
				else if (dr == DialogResult.No)
				{
					// Discard changes — revert MainForm if editing the loaded config
					if (IsEditingLoadedConfig())
					{
						allConfigs = new PingConfigurations();
						allConfigs.Load();
						PingConfiguration savedCfg = allConfigs.GetByGuid(editingGuid);
						if (savedCfg != null)
							ConfigurationEditDiscarded?.Invoke(savedCfg);
					}
				}
			}

			LoadSelectedNode();
		}

		/// <summary>
		/// Loads the currently selected tree node into the editor panel.
		/// </summary>
		private void LoadSelectedNode()
		{
			TreeNode selected = treeConfigurations.SelectedNode;
			if (selected == null)
				return;

			string guid = selected.Tag as string;
			if (guid == null)
			{
				// "New Configuration" selected
				editingGuid = null;
				LoadConfigurationIntoUI(new PingConfiguration());
				btnDelete.Enabled = false;
				btnClone.Enabled = false;
				UpdateLoadButtonState();
			}
			else
			{
				// Existing configuration selected - reload from allConfigs (which was freshly loaded)
				PingConfiguration cfg = allConfigs.GetByGuid(guid);
				if (cfg == null)
				{
					MessageBox.Show("Configuration was not found. It may have been deleted by another instance.");
					RefreshConfigurationList();
					return;
				}
				editingGuid = cfg.guid;
				LoadConfigurationIntoUI(cfg);
				btnDelete.Enabled = true;
				btnClone.Enabled = true;
				UpdateLoadButtonState();
			}
		}

		/// <summary>
		/// Re-selects the tree node matching editingGuid, or the "New" node if editingGuid is null.
		/// </summary>
		private void ReSelectCurrentNode()
		{
			foreach (TreeNode node in treeConfigurations.Nodes)
			{
				string tag = node.Tag as string;
				if ((editingGuid == null && tag == null) || (editingGuid != null && editingGuid.Equals(tag)))
				{
					treeConfigurations.SelectedNode = node;
					return;
				}
			}
		}

		/// <summary>
		/// Populates all form controls from a PingConfiguration.
		/// </summary>
		private void LoadConfigurationIntoUI(PingConfiguration cfg)
		{
			suppressEvents = true;
			try
			{
				txtDisplayName.Text = cfg.displayName;
				ValidateDisplayNameUniqueness();

				// Hosts text box — one hostname per line
				if (cfg.hosts != null && cfg.hosts.Count > 0)
				{
					List<string> hostLines = new List<string>();
					foreach (Host h in cfg.hosts)
					{
						if (!string.IsNullOrWhiteSpace(h.hostname))
							hostLines.Add(h.hostname);
					}
					txtHosts.Text = string.Join(Environment.NewLine, hostLines);
				}
				else
				{
					txtHosts.Text = "";
				}

				// Prefer IPv4 (configuration-level)
				cbPreferIPv4.Checked = cfg.preferIPv4;

				// Monitoring
				cbTraceroute.Checked = cfg.doTraceRoute;
				cbReverseDNS.Checked = cfg.reverseDnsLookup;
				trackBarRate.Value = PingRateToTrackBar(cfg.rate, cfg.pingsPerSecond);
				cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Checked = !cfg.monitorUnresponsiveHops;
				UpdateRateLabel();

				// Graph options
				cbAlwaysShowServerNames.Checked = cfg.drawServerNames;
				cbLastPing.Checked = cfg.drawLastPing;
				cbAverage.Checked = cfg.drawAverage;
				cbJitter.Checked = cfg.drawJitter;
				cbMinMax.Checked = cfg.drawMinMax;
				cbPacketLoss.Checked = cfg.drawPacketLoss;
				cbDrawLimits.Checked = cfg.drawLimitText;
				nudBadThreshold.Value = Math.Max(nudBadThreshold.Minimum, Math.Min(nudBadThreshold.Maximum, cfg.badThreshold));
				nudWorseThreshold.Value = Math.Max(nudWorseThreshold.Minimum, Math.Min(nudWorseThreshold.Maximum, cfg.worseThreshold));
				nudUpLimit.Value = Math.Max(nudUpLimit.Minimum, Math.Min(nudUpLimit.Maximum, cfg.upperLimit));
				nudLowLimit.Value = Math.Max(nudLowLimit.Minimum, Math.Min(nudLowLimit.Maximum, cfg.lowerLimit));
				cbScalingMethod.SelectedIndex = Math.Max(0, Math.Min(cbScalingMethod.Items.Count - 1, cfg.ScalingMethodID));

				// Logging
				cbLogFailures.Checked = cfg.logFailures;
				cbLogSuccesses.Checked = cfg.logSuccesses;

				UpdateTracerouteState();
			}
			finally
			{
				suppressEvents = false;
			}

			// Take a snapshot for dirty detection
			savedStateSnapshot = GetCurrentStateSnapshot();
			UpdateDiscardButton();
		}

		/// <summary>
		/// Reads the form controls into a PingConfiguration object.
		/// </summary>
		private PingConfiguration ReadConfigurationFromUI()
		{
			PingConfiguration cfg = new PingConfiguration();
			cfg.guid = editingGuid ?? Guid.NewGuid().ToString();
			cfg.displayName = txtDisplayName.Text.Trim();

			cfg.hosts = new List<Host>();
			string[] lines = txtHosts.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string line in lines)
			{
				string hostname = line.Trim();
				if (!string.IsNullOrEmpty(hostname))
				{
					Host h = new Host();
					h.hostname = hostname;
					cfg.hosts.Add(h);
				}
			}

			cfg.preferIPv4 = cbPreferIPv4.Checked;
			cfg.doTraceRoute = cbTraceroute.Checked;
			cfg.reverseDnsLookup = cbReverseDNS.Checked;
			cfg.monitorUnresponsiveHops = !cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Checked;

			int tbVal = trackBarRate.Value;
			if (tbVal > 0)
			{
				cfg.rate = tbVal;
				cfg.pingsPerSecond = true;
			}
			else if (tbVal < 0)
			{
				cfg.rate = -tbVal;
				cfg.pingsPerSecond = false;
			}
			else
			{
				cfg.rate = 0;
				cfg.pingsPerSecond = true;
			}

			cfg.drawServerNames = cbAlwaysShowServerNames.Checked;
			cfg.drawLastPing = cbLastPing.Checked;
			cfg.drawAverage = cbAverage.Checked;
			cfg.drawJitter = cbJitter.Checked;
			cfg.drawMinMax = cbMinMax.Checked;
			cfg.drawPacketLoss = cbPacketLoss.Checked;
			cfg.drawLimitText = cbDrawLimits.Checked;
			cfg.badThreshold = (int)nudBadThreshold.Value;
			cfg.worseThreshold = (int)nudWorseThreshold.Value;
			cfg.upperLimit = (int)nudUpLimit.Value;
			cfg.lowerLimit = (int)nudLowLimit.Value;
			cfg.ScalingMethodID = cbScalingMethod.SelectedIndex;

			cfg.logFailures = cbLogFailures.Checked;
			cfg.logSuccesses = cbLogSuccesses.Checked;

			return cfg;
		}

		/// <summary>
		/// Returns a string snapshot of the current form state, used for dirty detection.
		/// </summary>
		private string GetCurrentStateSnapshot()
		{
			return string.Join("|",
				txtDisplayName.Text,
				txtHosts.Text,
				cbPreferIPv4.Checked,
				cbTraceroute.Checked,
				cbReverseDNS.Checked,
				trackBarRate.Value,
				cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Checked,
				cbAlwaysShowServerNames.Checked,
				cbLastPing.Checked,
				cbAverage.Checked,
				cbJitter.Checked,
				cbMinMax.Checked,
				cbPacketLoss.Checked,
				cbDrawLimits.Checked,
				nudBadThreshold.Value,
				nudWorseThreshold.Value,
				nudUpLimit.Value,
				nudLowLimit.Value,
				cbScalingMethod.SelectedIndex,
				cbLogFailures.Checked,
				cbLogSuccesses.Checked
			);
		}

		/// <summary>
		/// Returns true if the current form state differs from the last saved/loaded state.
		/// </summary>
		private bool HasUnsavedChanges()
		{
			if (savedStateSnapshot == null)
				return false;
			return GetCurrentStateSnapshot() != savedStateSnapshot;
		}

		private void UpdateDiscardButton()
		{
			btnDiscard.Enabled = HasUnsavedChanges();
		}

		/// <summary>
		/// Notifies MainForm of live edits when editing the currently loaded configuration.
		/// </summary>
		private void NotifyLiveEdit()
		{
			if (IsEditingLoadedConfig())
				ConfigurationEdited?.Invoke(ReadConfigurationFromUI());
		}

		private void btnSave_Click(object sender, EventArgs e)
		{
			PerformSave();
		}

		/// <summary>
		/// Performs a save operation. Returns true if successful, false if validation failed.
		/// </summary>
		private bool PerformSave()
		{
			string name = txtDisplayName.Text.Trim();
			if (string.IsNullOrWhiteSpace(name))
			{
				MessageBox.Show("A display name is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				txtDisplayName.Focus();
				return false;
			}

			// Reload to get the latest state from disk
			allConfigs = new PingConfigurations();
			allConfigs.Load();

			if (!allConfigs.IsDisplayNameUnique(name, editingGuid))
			{
				MessageBox.Show("A configuration with this display name already exists. Please choose a different name.",
					"Duplicate Name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				txtDisplayName.Focus();
				return false;
			}

			PingConfiguration cfg = ReadConfigurationFromUI();

			if (editingGuid == null)
			{
				// New configuration
				editingGuid = cfg.guid;
			}

			PingConfigurations.SaveSingleConfiguration(cfg);
			savedStateSnapshot = GetCurrentStateSnapshot();
			UpdateDiscardButton();

			// Refresh tree with events suppressed to avoid recursive AfterSelect
			suppressEvents = true;
			try
			{
				RefreshConfigurationList();
				SelectConfigurationByGuid(cfg.guid);
			}
			finally
			{
				suppressEvents = false;
			}

			// Update button states for the now-selected node
			btnDelete.Enabled = true;
			btnClone.Enabled = true;
			UpdateLoadButtonState();

			// Raise the ConfigurationSaved event so MainForm can live-apply changes
			ConfigurationSaved?.Invoke(cfg);

			return true;
		}

		private void btnDiscard_Click(object sender, EventArgs e)
		{
			if (editingGuid != null)
			{
				// Reload from disk
				allConfigs = new PingConfigurations();
				allConfigs.Load();
				PingConfiguration cfg = allConfigs.GetByGuid(editingGuid);
				if (cfg != null)
				{
					LoadConfigurationIntoUI(cfg);
					if (IsEditingLoadedConfig())
						ConfigurationEditDiscarded?.Invoke(cfg);
				}
				else
				{
					MessageBox.Show("Configuration was not found on disk. It may have been deleted.");
					RefreshConfigurationList();
				}
			}
			else
			{
				// Discard on a "new" config — just reset to defaults
				LoadConfigurationIntoUI(new PingConfiguration());
			}
		}

		private void btnDelete_Click(object sender, EventArgs e)
		{
			if (editingGuid == null)
				return;

			DialogResult result = MessageBox.Show("Are you sure you want to delete this configuration?",
				"Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (result != DialogResult.Yes)
				return;

			// Remember the index of the deleted node for re-selection
			int deletedIndex = treeConfigurations.SelectedNode != null ? treeConfigurations.SelectedNode.Index : -1;

			PingConfigurations.DeleteConfiguration(editingGuid);
			editingGuid = null;
			savedStateSnapshot = null;
			suppressEvents = true;
			try
			{
				RefreshConfigurationList();
				// Select the item at the same index, or the last item if past end
				if (deletedIndex >= 0 && treeConfigurations.Nodes.Count > 0)
				{
					int selectIndex = Math.Min(deletedIndex, treeConfigurations.Nodes.Count - 1);
					treeConfigurations.SelectedNode = treeConfigurations.Nodes[selectIndex];
				}
			}
			finally
			{
				suppressEvents = false;
			}
			LoadSelectedNode();
		}

		private void btnLoad_Click(object sender, EventArgs e)
		{
			if (editingGuid == null)
			{
				MessageBox.Show("Please save this configuration first, or select an existing configuration to load.");
				return;
			}

			PingConfiguration cfg = ReadConfigurationFromUI();
			ConfigurationLoaded?.Invoke(cfg);
		}

		private void btnClone_Click(object sender, EventArgs e)
		{
			if (editingGuid == null)
			{
				MessageBox.Show("Please save this configuration first, or select an existing configuration to duplicate.");
				return;
			}

			PingConfiguration source = ReadConfigurationFromUI();

			// Assign new identity
			source.guid = Guid.NewGuid().ToString();

			// Generate unique name
			string baseName = source.displayName + " (copy)";
			string name = baseName;
			int counter = 2;
			allConfigs = new PingConfigurations();
			allConfigs.Load();
			while (!allConfigs.IsDisplayNameUnique(name, null))
			{
				name = source.displayName + " (copy " + counter + ")";
				counter++;
			}
			source.displayName = name;

			PingConfigurations.SaveSingleConfiguration(source);

			suppressEvents = true;
			try
			{
				RefreshConfigurationList();
				SelectConfigurationByGuid(source.guid);
			}
			finally
			{
				suppressEvents = false;
			}
			// Load the cloned config into the editor
			editingGuid = source.guid;
			LoadConfigurationIntoUI(source);
			btnDelete.Enabled = true;
			btnClone.Enabled = true;
			UpdateLoadButtonState();
		}

		private void ConfigurationForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (HasUnsavedChanges())
			{
				DialogResult dr = MessageBox.Show(
					"You have unsaved changes. Do you want to save them before closing?",
					"Unsaved Changes",
					MessageBoxButtons.YesNoCancel,
					MessageBoxIcon.Warning);
				if (dr == DialogResult.Yes)
				{
					if (!PerformSave())
					{
						e.Cancel = true;
						return;
					}
				}
				else if (dr == DialogResult.Cancel)
				{
					e.Cancel = true;
					return;
				}
				else if (dr == DialogResult.No)
				{
					// Discard changes — revert MainForm if editing the loaded config
					if (IsEditingLoadedConfig())
					{
						allConfigs = new PingConfigurations();
						allConfigs.Load();
						PingConfiguration savedCfg = allConfigs.GetByGuid(editingGuid);
						if (savedCfg != null)
							ConfigurationEditDiscarded?.Invoke(savedCfg);
					}
				}
			}
		}

		private void txtDisplayName_TextChanged(object sender, EventArgs e)
		{
			if (suppressEvents)
				return;
			ValidateDisplayNameUniqueness();
			UpdateDiscardButton();
			NotifyLiveEdit();
		}

		private void ValidateDisplayNameUniqueness()
		{
			string name = txtDisplayName.Text.Trim();
			if (string.IsNullOrWhiteSpace(name))
			{
				lblUniqueWarning.Text = "Name is required";
				lblUniqueWarning.ForeColor = Color.Red;
				btnSave.Enabled = false;
				return;
			}

			// Check against allConfigs (loaded from disk)
			bool isUnique = allConfigs == null || allConfigs.IsDisplayNameUnique(name, editingGuid);
			if (!isUnique)
			{
				lblUniqueWarning.Text = "Name already in use";
				lblUniqueWarning.ForeColor = Color.Red;
				btnSave.Enabled = false;
			}
			else
			{
				lblUniqueWarning.Text = "";
				btnSave.Enabled = true;
			}
		}

		/// <summary>
		/// Disables (but retains) the traceroute checkbox when multiple hosts are entered.
		/// </summary>
		private void UpdateTracerouteState()
		{
			int hostCount = 0;
			string[] lines = txtHosts.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string line in lines)
			{
				if (!string.IsNullOrWhiteSpace(line))
					hostCount++;
			}

			if (hostCount > 1)
			{
				cbTraceroute.Enabled = false;
				lblTracerouteWarning.Text = "(disabled: multiple hosts)";
			}
			else
			{
				cbTraceroute.Enabled = true;
				lblTracerouteWarning.Text = "";
			}
		}

		private void cbTraceroute_CheckedChanged(object sender, EventArgs e)
		{
			if (!suppressEvents)
			{
				UpdateDiscardButton();
				NotifyLiveEdit();
			}
			cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings.Enabled = cbTraceroute.Checked;
		}

		private void cbStopMonitoringIntermediateHopsThatDoNotRepondToRegularPings_CheckedChanged(object sender, EventArgs e)
		{
			if (!suppressEvents)
			{
				UpdateDiscardButton();
				NotifyLiveEdit();
			}
		}

		private void txtHosts_TextChanged(object sender, EventArgs e)
		{
			if (suppressEvents)
				return;
			UpdateTracerouteState();
			UpdateDiscardButton();
			NotifyLiveEdit();
		}

		/// <summary>
		/// When the hosts text box loses focus, normalize the contents:
		/// split comma/space-separated entries onto individual lines,
		/// and remove empty/whitespace lines.
		/// </summary>
		private void txtHosts_Leave(object sender, EventArgs e)
		{
			string text = txtHosts.Text;
			// Split by newlines first, then by comma and space within each line
			List<string> hosts = new List<string>();
			string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string line in lines)
			{
				string[] parts = line.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string part in parts)
				{
					string trimmed = part.Trim();
					if (!string.IsNullOrEmpty(trimmed))
						hosts.Add(trimmed);
				}
			}
			string normalized = string.Join(Environment.NewLine, hosts);
			if (normalized != txtHosts.Text)
			{
				txtHosts.Text = normalized;
			}
		}

		private void SelectConfigurationByGuid(string guid)
		{
			foreach (TreeNode node in treeConfigurations.Nodes)
			{
				if (guid.Equals(node.Tag as string))
				{
					treeConfigurations.SelectedNode = node;
					return;
				}
			}
		}

		/// <summary>
		/// Programmatically selects a configuration in the tree by Guid and loads it into the editor.
		/// </summary>
		public void SelectConfiguration(string guid)
		{
			if (guid != null)
				SelectConfigurationByGuid(guid);
		}

		/// <summary>
		/// Positions this form adjacent to the given owner form, to its left or right with zero margin.
		/// If any part would be offscreen, nudges it the minimal amount to fit on-screen.
		/// </summary>
		public void PositionAdjacentTo(Form owner)
		{
			int spaceLeft = owner.Left - Screen.FromControl(owner).WorkingArea.Left;
			int spaceRight = Screen.FromControl(owner).WorkingArea.Right - owner.Right;

			int x, y;
			y = owner.Top;

			if (spaceLeft >= spaceRight)
			{
				// Place to the left of the owner
				x = owner.Left - this.Width;
			}
			else
			{
				// Place to the right of the owner
				x = owner.Right;
			}

			this.StartPosition = FormStartPosition.Manual;
			this.Location = this.NudgeOnscreen(x, y);
		}

		private void trackBarRate_ValueChanged(object sender, EventArgs e)
		{
			UpdateRateLabel();
			if (suppressEvents)
				return;
			UpdateDiscardButton();
			NotifyLiveEdit();
		}

		/// <summary>
		/// Updates the rate value label to reflect the current TrackBar position.
		/// </summary>
		private void UpdateRateLabel()
		{
			int val = trackBarRate.Value;
			if (val > 0)
				lblRateValue.Text = val + " ping" + (val == 1 ? "" : "s") + "/sec";
			else if (val < 0)
				lblRateValue.Text = (-val) + " sec/ping";
			else
				lblRateValue.Text = "0 pings/sec";
		}

		/// <summary>
		/// Converts a PingConfiguration rate+pingsPerSecond pair to a TrackBar value (-10 to 10).
		/// </summary>
		private static int PingRateToTrackBar(int rate, bool pingsPerSecond)
		{
			if (rate == 0)
				return 0;
			if (pingsPerSecond)
				return Math.Max(-10, Math.Min(10, rate));
			else
				return Math.Max(-10, Math.Min(10, -rate));
		}

		/// <summary>
		/// Generic handler for any control change that should trigger dirty detection and live-edit notification.
		/// </summary>
		private void AnyControl_Changed(object sender, EventArgs e)
		{
			if (suppressEvents)
				return;
			UpdateDiscardButton();
			NotifyLiveEdit();
		}

		/// <summary>
		/// Handles keyboard shortcuts for the Configuration Editor.
		/// Delete key triggers deletion; Escape key closes the form.
		/// </summary>
		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == Keys.Delete)
			{
				if (!IsTextInputFocused() && btnDelete.Enabled)
				{
					btnDelete_Click(btnDelete, EventArgs.Empty);
					return true;
				}
			}
			else if (keyData == Keys.Escape)
			{
				this.Close();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		/// <summary>
		/// Returns true if the currently focused control is a text-editing control
		/// where the Delete key has a text-editing function.
		/// </summary>
		private bool IsTextInputFocused()
		{
			Control active = this.ActiveControl;
			// Walk into nested containers to find the actual focused control
			while (active is ContainerControl cc && cc.ActiveControl != null && cc.ActiveControl != active)
				active = cc.ActiveControl;
			if (active is TextBoxBase || active is ComboBox)
				return true;
			// Check if the active control is within a NumericUpDown
			Control parent = active;
			while (parent != null)
			{
				if (parent is NumericUpDown)
					return true;
				parent = parent.Parent;
			}
			return false;
		}
	}
}
