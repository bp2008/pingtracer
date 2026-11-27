<template>
	<div class="app">
		<ToolBar :configurations="store.configurations" :selectedConfigGuid="store.selectedConfigGuid"
			:isRunning="store.isRunning" :status="store.status" :sentCount="store.sentCount" :lostCount="store.lostCount"
			:connected="store.connected" :viewingHistory="viewingHistory" :unreadLog="unreadLog" :view="view"
			@selectConfig="store.selectConfig" @start="store.start" @stop="store.stop"
			@editConfig="editCurrentConfig" @newConfig="newConfig"
			@zoomIn="graph('zoomIn')" @zoomOut="graph('zoomOut')" @fitAll="graph('fitAll')" @goLive="graph('goLive')"
			@showHistory="showHistory = true" @showLog="toggleLog" @showSettings="showSettings = true" @showHelp="showHelp = true" />

		<div v-if="viewingHistory" class="banner">
			Viewing saved session <strong>{{ historyTitle }}</strong>
			<button class="banner-btn" @click="store.closeHistory">Close</button>
		</div>

		<div class="main">
			<GraphView ref="graph" :config="configForGraphs" :historyMode="viewingHistory" @viewChanged="v => view = v" />
			<div v-if="placeholder" class="placeholder">
				<div class="placeholder-box">
					<div class="placeholder-title">{{ placeholder.title }}</div>
					<div v-if="placeholder.text" class="placeholder-text">{{ placeholder.text }}</div>
					<button v-if="placeholder.action" class="placeholder-btn" @click="placeholder.action.fn">{{ placeholder.action.label }}</button>
				</div>
			</div>
		</div>

		<LogViewer v-if="showLog" :messages="store.logMessages" @close="closeLog" />

		<div v-if="showHelp" class="dlg-overlay" @mousedown.self="showHelp = false">
			<div class="dlg help">
				<div class="dlg-header"><h3>Graph controls</h3><button class="dlg-close" @click="showHelp = false">&times;</button></div>
				<div class="dlg-body">
					<table class="keys">
						<tr><td>Mouse wheel, <kbd>+</kbd> <kbd>−</kbd></td><td>Zoom in / out in time</td></tr>
						<tr><td>Drag, <kbd>Shift</kbd>+wheel, <kbd>←</kbd> <kbd>→</kbd></td><td>Scroll through time</td></tr>
						<tr><td><kbd>PgUp</kbd> <kbd>PgDn</kbd></td><td>Scroll one screen</td></tr>
						<tr><td><kbd>End</kbd>, double-click</td><td>Jump to the latest data (live)</td></tr>
						<tr><td><kbd>Home</kbd></td><td>Jump to the start</td></tr>
						<tr><td><kbd>F</kbd></td><td>Fit everything on screen</td></tr>
						<tr><td><kbd>L</kbd></td><td>Show / hide the event log</td></tr>
					</table>
					<h4>Reading the graph</h4>
					<ul class="legend">
						<li><span class="sw" style="background:#409640"></span><span class="sw" style="background:#969600"></span><span class="sw" style="background:#f0e600"></span>Latency below the "bad" threshold, above it, and above the "worse" threshold. When several pings share a pixel, the solid bar is the average and the dimmer extension reaches the maximum.</li>
						<li><span class="sw" style="background:#ff0000"></span>Lost pings. When several pings share a pixel, red hangs from the top of the row in proportion to the fraction lost.</li>
						<li><span class="sw" style="background:#1a1a20"></span>Pings still waiting for a reply.</li>
						<li><span class="sw strip"></span>A colored strip at the top of a row appears when a hop has answered from more than one address; each address has its own color. Alternating colors mean load balancing.</li>
						<li><span class="sw" style="background:#00c8ff"></span><span class="sw" style="background:#e600e6"></span>Dashed lines mark a new address at a hop and a change in the number of hops to the destination.</li>
						<li>Thin gray rows are hops that do not answer pings; that is normal for many routers.</li>
					</ul>
				</div>
			</div>
		</div>

		<ConfigEditor v-if="showConfigEditor" :config="editingConfig" @save="onConfigSave" @delete="onConfigDelete"
			@close="onConfigClose" @preview="onConfigPreview" />
		<HistoryDialog v-if="showHistory" :history="store.history" :isRunning="store.isRunning"
			:openFileName="store.view.fileName" @close="showHistory = false" @refresh="store.listHistory"
			@open="onOpenHistory" />
		<SettingsDialog v-if="showSettings" :settings="store.settings" @close="showSettings = false" @save="onSaveSettings" />

		<div class="toasts">
			<div v-for="e in store.errors" :key="e.id" class="toast" role="alert" title="Click to dismiss" @click="store.dismissError(e.id)">
				{{ e.message }}<span class="toast-close" aria-hidden="true">&times;</span>
			</div>
		</div>
	</div>
</template>

<script>
import ToolBar from '@/components/ToolBar.vue';
import GraphView from '@/components/GraphView.vue';
import ConfigEditor from '@/components/ConfigEditor.vue';
import LogViewer from '@/components/LogViewer.vue';
import HistoryDialog from '@/components/HistoryDialog.vue';
import SettingsDialog from '@/components/SettingsDialog.vue';
import { usePingStore } from '@/stores/ping';
import { formatDateTime } from '@/library/format';

export default {
	name: 'App',
	components: { ToolBar, GraphView, ConfigEditor, LogViewer, HistoryDialog, SettingsDialog },
	setup()
	{
		return { store: usePingStore() };
	},
	data()
	{
		return {
			showConfigEditor: false,
			showLog: false,
			showHistory: false,
			showSettings: false,
			showHelp: false,
			editingConfig: null,
			previewConfig: null,
			savedConfigBackup: null,
			view: { live: true, msPerCol: 500, widthPx: 1000, dpr: 1, startMs: 0, endMs: 60000 },
			logSeen: 0,
			fitPending: false,
		};
	},
	computed: {
		viewingHistory() { return this.store.view.source === 'history'; },
		historyTitle()
		{
			const s = this.store.sessions[0];
			if (!s) return this.store.view.fileName || '';
			return `${s.displayName} — ${formatDateTime(s.startMs)}`;
		},
		configForGraphs() { return this.previewConfig || this.store.configDetails; },
		unreadLog() { return this.showLog ? 0 : Math.max(0, Math.min(99, this.store.logMessages.length - this.logSeen)); },
		placeholder()
		{
			const st = this.store;
			if (!st.connected)
				return { title: 'Connecting to PingTracer…', text: 'The PingTracer application must be running on this computer.' };
			if (st.sessions.length > 0)
				return null;
			if (st.configurations.length === 0)
				return { title: 'Welcome to PingTracer', text: 'Create a configuration to choose which host to monitor.', action: { label: 'New configuration', fn: this.newConfig } };
			if (!st.selectedConfigGuid)
				return { title: 'Choose a configuration', text: 'Select a configuration from the list above.' };
			if (st.isRunning)
				return { title: 'Starting…' };
			if (st.loading)
				return { title: 'Loading saved data…' };
			return { title: st.selectedConfig ? st.selectedConfig.displayName : 'Ready', text: 'Press Start to begin monitoring.', action: { label: '▶ Start', fn: st.start } };
		},
	},
	watch: {
		'store.view'(v, old)
		{
			if (v.source === 'history')
			{
				this.fitPending = true;
				this.showHistory = false;
			}
			else if (old && old.source === 'history')
				this.$nextTick(() => this.graph('restoreSavedZoom'));
		},
		'store.sessions'(list)
		{
			if (list.length === 0) return;
			if (this.fitPending)
			{
				this.fitPending = false;
				this.$nextTick(() => this.graph('fitAll'));
			}
			// Live view: the first sessions to appear (a run starting, or saved data loading) choose a zoom that suits their interval.
			else if (this.store.view.source === 'live' && list.length > 0)
			{
				const g = this.$refs.graph;
				if (g && !this._zoomDefaulted)
				{
					this._zoomDefaulted = true;
					g.setDefaultZoomForInterval(list[0].intervalMs);
				}
			}
		},
		'store.isRunning'(running)
		{
			if (running) this.graph('goLive');
		},
	},
	mounted()
	{
		this.store.connect();
		window.addEventListener('beforeunload', this.onUnload);
		window.addEventListener('keydown', this.onGlobalKey);
		this.$refs.graph.focus();
	},
	beforeUnmount()
	{
		window.removeEventListener('beforeunload', this.onUnload);
		window.removeEventListener('keydown', this.onGlobalKey);
		this.store.disconnect();
	},
	methods: {
		graph(method)
		{
			const g = this.$refs.graph;
			if (g && g[method]) g[method]();
		},
		onUnload() { this.store.disconnect(); },
		onGlobalKey(e)
		{
			// Route graph navigation keys to the graph even when it does not have focus (unless typing in a field or a dialog is open).
			if (this.showConfigEditor || this.showHistory || this.showSettings) return;
			if (e.key === 'Escape' && this.showHelp) { this.showHelp = false; return; }
			const t = e.target;
			if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT')) return;
			if (e.ctrlKey || e.altKey || e.metaKey) return;
			if (e.key === '?') { this.showHelp = !this.showHelp; return; }
			if (e.key === 'l' || e.key === 'L') { this.toggleLog(); return; }
			if (t && t.closest && t.closest('.graph-view')) return;
			const g = this.$refs.graph;
			if (g) g.onKeyDown(e);
		},

		toggleLog()
		{
			if (this.showLog) this.closeLog();
			else this.showLog = true;
		},
		closeLog()
		{
			this.showLog = false;
			this.logSeen = this.store.logMessages.length;
		},

		editCurrentConfig()
		{
			if (!this.store.configDetails) return;
			this.editingConfig = { ...this.store.configDetails };
			this.savedConfigBackup = { ...this.store.configDetails };
			this.showConfigEditor = true;
		},
		newConfig()
		{
			this.editingConfig = null;
			this.savedConfigBackup = null;
			this.showConfigEditor = true;
		},
		onConfigSave(config)
		{
			this.store.saveConfig(config);
			this.previewConfig = null;
			this.savedConfigBackup = null;
			this.showConfigEditor = false;
		},
		onConfigClose()
		{
			const b = this.savedConfigBackup, p = this.previewConfig;
			if (b && p && this.store.isRunning && (p.rate !== b.rate || p.pingsPerSecond !== b.pingsPerSecond))
				this.store.setPingRate(b.rate, b.pingsPerSecond);
			this.previewConfig = null;
			this.savedConfigBackup = null;
			this.showConfigEditor = false;
		},
		onConfigPreview(config)
		{
			// Only preview edits to the configuration whose graphs are on screen.
			if (!this.savedConfigBackup) return;
			this.previewConfig = config;
			const b = this.savedConfigBackup;
			if (this.store.isRunning && (config.rate !== b.rate || config.pingsPerSecond !== b.pingsPerSecond))
				this.store.setPingRate(config.rate, config.pingsPerSecond);
		},
		onConfigDelete(guid)
		{
			this.store.deleteConfig(guid);
			this.previewConfig = null;
			this.showConfigEditor = false;
		},
		onOpenHistory(fileName)
		{
			this.store.openHistory(fileName);
		},
		onSaveSettings(s)
		{
			this.store.saveSettings(s);
			this.showSettings = false;
		},
	},
};
</script>

<style scoped>
.app {
	display: flex;
	flex-direction: column;
	height: 100%;
}

.banner {
	background: #1d2a3e;
	color: #cfe0ff;
	padding: 4px 12px;
	font-size: 12.5px;
	border-bottom: 1px solid #2c3e5a;
	display: flex;
	align-items: center;
	gap: 10px;
	flex-shrink: 0;
}

.banner-btn {
	background: #2a3d5c;
	color: #e6eeff;
	border: 1px solid #3f5a85;
	border-radius: 3px;
	padding: 1px 10px;
	cursor: pointer;
	font-size: 12px;
}

.main {
	flex: 1;
	min-height: 0;
	position: relative;
	display: flex;
}

.placeholder {
	position: absolute;
	inset: 0;
	display: flex;
	align-items: center;
	justify-content: center;
	background: #000;
	z-index: 5;
}

.placeholder-box { text-align: center; max-width: 420px; }
.placeholder-title { font-size: 18px; color: #c8c8d4; margin-bottom: 8px; }
.placeholder-text { color: #7c7c8c; margin-bottom: 14px; line-height: 1.5; }
.placeholder-btn {
	background: #24481c;
	border: 1px solid #3a7030;
	color: #e8ffe8;
	padding: 6px 18px;
	border-radius: 3px;
	cursor: pointer;
	font-size: 13px;
}
.placeholder-btn:hover { background: #2f5e25; }

.help { width: 600px; }
.keys { border-collapse: collapse; margin-bottom: 14px; }
.keys td { padding: 3px 14px 3px 0; vertical-align: top; }
.keys td:first-child { white-space: nowrap; color: #c8c8d8; }
kbd {
	display: inline-block;
	background: #2a2a38;
	border: 1px solid #4a4a60;
	border-bottom-width: 2px;
	border-radius: 3px;
	padding: 0 5px;
	font-family: inherit;
	font-size: 11px;
}
.help h4 { margin: 4px 0 8px; font-size: 13px; color: #b0b0c0; }
.legend { list-style: none; line-height: 1.5; color: #b8b8c4; }
.legend li { margin-bottom: 7px; }
.sw { display: inline-block; width: 12px; height: 12px; margin-right: 4px; vertical-align: -1px; border-radius: 2px; }
.sw.strip { background: linear-gradient(90deg, #00afaf 50%, #ff9600 50%); height: 5px; vertical-align: 2px; }

.toasts {
	position: fixed;
	right: 16px;
	bottom: 34px;
	display: flex;
	flex-direction: column;
	gap: 6px;
	z-index: 2000;
}

.toast {
	background: #5e1c1c;
	border: 1px solid #8a2e2e;
	color: #ffdede;
	padding: 8px 14px;
	border-radius: 4px;
	cursor: pointer;
	max-width: 420px;
	box-shadow: 0 4px 14px rgba(0, 0, 0, 0.5);
}

.toast-close {
	margin-left: 12px;
	opacity: 0.7;
}

.toast:hover .toast-close {
	opacity: 1;
}
</style>
