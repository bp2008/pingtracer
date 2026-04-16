<template>
	<div class="app-container" @keydown="onKeyDown" tabindex="0">
		<ToolBar :configurations="store.configurations" :selectedConfigGuid="store.selectedConfigGuid"
			:isRunning="store.isRunning" :status="store.status" :successfulPings="store.successfulPings"
			:failedPings="store.failedPings" :connected="store.connected" @selectConfig="store.selectConfig"
			@start="store.startPinging" @stop="store.stopPinging" @editConfig="editCurrentConfig"
			@newConfig="newConfig" @showLog="showLog = true" />

		<div class="graph-area" ref="graphArea">
			<template v-if="store.targets.length > 0">
				<div v-for="(target, idx) in store.targets" :key="target.id" class="graph-container"
					:style="graphStyle(idx)">
					<PingGraph ref="graphs" :pings="store.pingData[target.id] || []"
						:displayName="target.displayName" :config="configForGraphs"
						:pixelsPerPing="effectivePixelsPerPing" :scrollOffset="scrollOffset" :isLive="isLive"
						@wheel="onGraphWheel" @dragStart="onDragStart" />
				</div>
			</template>
			<div v-else class="no-graphs">
				<div class="no-graphs-content">
					<p v-if="!store.selectedConfigGuid">Select or create a configuration to begin.</p>
					<p v-else-if="!store.isRunning">Press <strong>Start</strong> to begin pinging.</p>
					<p v-else>Waiting for targets...</p>
				</div>
			</div>
		</div>

		<TimeScale v-if="store.targets.length > 0" :pings="longestPingArray" :pixelsPerPing="effectivePixelsPerPing"
			:scrollOffset="scrollOffset" />

		<ConfigEditor v-if="showConfigEditor" :config="editingConfig" @save="onConfigSave" @delete="onConfigDelete"
			@close="onConfigClose" @preview="onConfigPreview" />

		<LogViewer v-if="showLog" :messages="store.logMessages" @close="showLog = false" />

		<div class="error-toast" v-if="store.errors.length > 0" @click="store.clearErrors">
			{{ store.errors[store.errors.length - 1] }}
		</div>

		<div v-if="zoomTooltipVisible" class="zoom-tooltip"
			:style="{ left: zoomTooltipX + 'px', top: zoomTooltipY + 'px' }">
			{{ zoomTooltipText }}
		</div>
	</div>
</template>

<script>
import ToolBar from '@/components/ToolBar.vue';
import PingGraph from '@/components/PingGraph.vue';
import TimeScale from '@/components/TimeScale.vue';
import ConfigEditor from '@/components/ConfigEditor.vue';
import LogViewer from '@/components/LogViewer.vue';
import { usePingStore } from '@/stores/ping';

// Zoom level = CSS pixels per ping.
// Default 1.0, max 3.0 (most zoomed in), min = canvasWidth / cacheSize (most zoomed out).
const ZOOM_DEFAULT = 1.0;
const ZOOM_MAX = 3.0;
const ZOOM_SNAP_THRESHOLD = 0.05; // snap to 1.0 if within this range

export default {
	name: 'App',
	components: { ToolBar, PingGraph, TimeScale, ConfigEditor, LogViewer },
	setup()
	{
		const store = usePingStore();
		return { store };
	},
	data()
	{
		return {
			showConfigEditor: false,
			showLog: false,
			editingConfig: null,
			previewConfig: null,
			savedConfigBackup: null,

			// Shared zoom/scroll state
			pixelsPerPing: ZOOM_DEFAULT,
			scrollOffset: 0,     // pings from the right edge (0 = live)
			liveUntil: 0,        // timestamp for showing LIVE label

			// Drag state
			isDragging: false,
			dragStartX: 0,
			dragStartScroll: 0,

			// Zoom tooltip
			zoomTooltipVisible: false,
			zoomTooltipX: 0,
			zoomTooltipY: 0,
			zoomTooltipText: '',
			zoomTooltipTimer: null,
		};
	},
	computed: {
		isLive()
		{
			return this.scrollOffset === 0 && Date.now() < this.liveUntil;
		},

		longestPingArray()
		{
			let longest = [];
			for (const target of this.store.targets)
			{
				const arr = this.store.pingData[target.id];
				if (arr && arr.length > longest.length)
					longest = arr;
			}
			return longest;
		},

		configForGraphs()
		{
			return this.previewConfig || this.store.configDetails;
		},

		graphAreaWidth()
		{
			// Rough estimate; recalculated on resize via ResizeObserver in graphs
			return this.$refs.graphArea?.clientWidth || 800;
		},

		zoomMin()
		{
			const w = this.$refs.graphArea?.clientWidth || 800;
			return w / this.store.cacheSize;
		},

		effectivePixelsPerPing()
		{
			const v = this.pixelsPerPing;
			// Snap near 1.0
			if (Math.abs(v - 1.0) < ZOOM_SNAP_THRESHOLD)
				return 1.0;
			return v;
		},
	},
	mounted()
	{
		this.store.connect();
		document.addEventListener('mouseup', this.onMouseUp);
		document.addEventListener('mousemove', this.onDocMouseMove);

		// Clean up WebSocket on page unload to prevent stale connections
		this._onBeforeUnload = () => this.store.disconnect();
		window.addEventListener('beforeunload', this._onBeforeUnload);
	},
	beforeUnmount()
	{
		window.removeEventListener('beforeunload', this._onBeforeUnload);
		this.store.disconnect();
		document.removeEventListener('mouseup', this.onMouseUp);
		document.removeEventListener('mousemove', this.onDocMouseMove);
		if (this.zoomTooltipTimer)
			clearTimeout(this.zoomTooltipTimer);
	},
	methods: {
		graphStyle(index)
		{
			const count = this.store.targets.length;
			if (count === 0) return {};
			const pct = 100 / count;
			return {
				height: pct + '%',
				borderBottom: index < count - 1 ? '1px solid #555' : 'none',
			};
		},

		// --- Zoom ---

		clampZoom(z)
		{
			const min = this.zoomMin;
			return Math.max(min, Math.min(ZOOM_MAX, z));
		},

		onGraphWheel(ev)
		{
			const oldPPP = this.effectivePixelsPerPing;
			const factor = ev.deltaY > 0 ? (1 / 1.15) : 1.15; // scroll down = zoom out (fewer px/ping)
			let newPPP = this.clampZoom(this.pixelsPerPing * factor);

			if (newPPP === this.pixelsPerPing) return;

			// Zoom centered on mouse position within the graph
			const mouseXFraction = (ev.clientX - ev.rectLeft) / ev.rectWidth;
			const oldVisible = ev.rectWidth / oldPPP;
			const newVisible = ev.rectWidth / newPPP;
			const pingDelta = (oldVisible - newVisible) * (1 - mouseXFraction);

			this.pixelsPerPing = newPPP;
			this.scrollOffset = Math.max(0, Math.round(this.scrollOffset + pingDelta));
			if (this.scrollOffset === 0)
				this.liveUntil = Date.now() + 1000;

			this.showZoomTooltip(ev.clientX, ev.clientY);
		},

		showZoomTooltip(clientX, clientY)
		{
			const display = this.effectivePixelsPerPing;
			let text;
			if (display >= 0.01)
				text = display.toFixed(Math.min(4, Math.max(0, 4 - Math.floor(Math.log10(display))))) + 'x';
			else
				text = display.toFixed(4) + 'x';

			// Remove trailing zeros but keep at least one decimal
			text = text.replace(/(\.\d*?)0+x$/, '$1x').replace(/\.x$/, '.0x');

			this.zoomTooltipText = text;
			this.zoomTooltipX = clientX + 14;
			this.zoomTooltipY = clientY - 10;
			this.zoomTooltipVisible = true;

			if (this.zoomTooltipTimer)
				clearTimeout(this.zoomTooltipTimer);
			this.zoomTooltipTimer = setTimeout(() =>
			{
				this.zoomTooltipVisible = false;
			}, 1500);

			// Track mouse for tooltip positioning
			this._lastTooltipMouseHandler = (me) =>
			{
				if (this.zoomTooltipVisible)
				{
					this.zoomTooltipX = me.clientX + 14;
					this.zoomTooltipY = me.clientY - 10;
				}
			};
			document.removeEventListener('mousemove', this._prevTooltipMouseHandler);
			document.addEventListener('mousemove', this._lastTooltipMouseHandler);
			this._prevTooltipMouseHandler = this._lastTooltipMouseHandler;
		},

		// --- Drag/Pan ---

		onDragStart(clientX)
		{
			this.isDragging = true;
			this.dragStartX = clientX;
			this.dragStartScroll = this.scrollOffset;
		},

		onMouseUp()
		{
			this.isDragging = false;
		},

		onDocMouseMove(e)
		{
			if (!this.isDragging) return;

			const dx = e.clientX - this.dragStartX;
			const ppp = this.effectivePixelsPerPing;
			// Dragging right = grab and slide data right = see older data (increase offset)
			const scrollDelta = Math.round(dx / ppp);
			const newScroll = this.dragStartScroll + scrollDelta;

			this.scrollOffset = Math.max(0, newScroll);
			if (this.scrollOffset === 0)
				this.liveUntil = Date.now() + 1000;
		},

		// --- Keyboard ---

		onKeyDown(e)
		{
			if (this.store.targets.length === 0) return;

			const graphEl = this.$refs.graphArea;
			const w = graphEl ? graphEl.clientWidth : 800;
			const visiblePings = Math.floor(w / this.effectivePixelsPerPing);

			switch (e.key)
			{
				case 'Home':
				case '9':
					this.scrollOffset = Math.max(0, this.store.cacheSize - visiblePings);
					e.preventDefault();
					break;
				case 'End':
				case '0':
					this.scrollOffset = 0;
					this.liveUntil = Date.now() + 1000;
					e.preventDefault();
					break;
				case 'PageUp':
				case '-':
					this.scrollOffset = Math.max(0, this.scrollOffset + visiblePings);
					e.preventDefault();
					break;
				case 'PageDown':
				case '=':
					this.scrollOffset = Math.max(0, this.scrollOffset - visiblePings);
					if (this.scrollOffset === 0)
						this.liveUntil = Date.now() + 1000;
					e.preventDefault();
					break;
			}
		},

		// --- Config ---

		editCurrentConfig()
		{
			this.editingConfig = this.store.configDetails ? { ...this.store.configDetails } : null;
			this.savedConfigBackup = this.store.configDetails ? { ...this.store.configDetails } : null;
			this.showConfigEditor = true;
		},

		newConfig()
		{
			this.editingConfig = null;
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
			// Revert ping rate if it was changed during preview
			if (this.savedConfigBackup && this.store.isRunning && this.previewConfig
				&& (this.previewConfig.rate !== this.savedConfigBackup.rate
					|| this.previewConfig.pingsPerSecond !== this.savedConfigBackup.pingsPerSecond))
			{
				this.store.setPingRate(this.savedConfigBackup.rate, this.savedConfigBackup.pingsPerSecond);
			}
			this.previewConfig = null;
			this.savedConfigBackup = null;
			this.showConfigEditor = false;
		},

		onConfigPreview(config)
		{
			this.previewConfig = config;
			// Apply ping rate changes to server immediately
			if (this.savedConfigBackup && this.store.isRunning
				&& (config.rate !== this.savedConfigBackup.rate
					|| config.pingsPerSecond !== this.savedConfigBackup.pingsPerSecond))
			{
				this.store.setPingRate(config.rate, config.pingsPerSecond);
			}
		},

		onConfigDelete(guid)
		{
			this.store.deleteConfig(guid);
			this.showConfigEditor = false;
		},
	}
};
</script>

<style>
*,
*::before,
*::after {
	box-sizing: border-box;
	margin: 0;
	padding: 0;
}

html,
body {
	height: 100%;
	overflow: hidden;
	background: #000;
	color: #e0e0e0;
	font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

#app {
	height: 100%;
}
</style>

<style scoped>
.app-container {
	display: flex;
	flex-direction: column;
	height: 100%;
	outline: none;
}

.graph-area {
	flex: 1;
	display: flex;
	flex-direction: column;
	overflow: hidden;
	background: #000;
}

.graph-container {
	flex-shrink: 0;
}

.no-graphs {
	flex: 1;
	display: flex;
	align-items: center;
	justify-content: center;
}

.no-graphs-content {
	text-align: center;
	color: #666;
	font-size: 16px;
}

.no-graphs-content strong {
	color: #888;
}

.error-toast {
	position: fixed;
	bottom: 20px;
	right: 20px;
	background: #702020;
	color: #fcc;
	padding: 10px 16px;
	border-radius: 4px;
	font-size: 13px;
	cursor: pointer;
	z-index: 2000;
	max-width: 400px;
}

.zoom-tooltip {
	position: fixed;
	background: rgba(30, 30, 50, 0.92);
	color: #e0e0e0;
	font-size: 12px;
	font-family: monospace;
	padding: 3px 7px;
	border-radius: 4px;
	border: 1px solid #555;
	pointer-events: none;
	z-index: 3000;
	white-space: nowrap;
}
</style>