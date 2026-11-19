<template>
	<div class="toolbar">
		<div class="group">
			<select :value="selectedConfigGuid || ''" @change="$emit('selectConfig', $event.target.value)"
				:disabled="isRunning || !connected" class="config-select"
				:title="isRunning ? 'Stop monitoring to choose another configuration' : 'Configuration'">
				<option value="" disabled>Select a configuration…</option>
				<option v-for="cfg in configurations" :key="cfg.guid" :value="cfg.guid">{{ cfg.displayName }}</option>
			</select>
			<button class="btn icon" @click="$emit('editConfig')" :disabled="!selectedConfigGuid" title="Edit configuration">✎</button>
			<button class="btn icon" @click="$emit('newConfig')" title="New configuration">＋</button>
		</div>

		<div class="group">
			<button v-if="!isRunning" @click="$emit('start')" :disabled="!selectedConfigGuid || !connected || viewingHistory"
				class="btn start" :title="viewingHistory ? 'Close the saved session first' : 'Start monitoring'">▶ Start</button>
			<button v-else @click="$emit('stop')" class="btn stop" title="Stop monitoring">■ Stop</button>
		</div>

		<div class="group status">
			<span class="status-text" :class="{ active: isRunning }">{{ status }}</span>
			<span v-if="sentCount > 0" class="counts" :title="`${sentCount} pings sent, ${lostCount} lost`">
				{{ sentCount.toLocaleString() }} sent ·
				<span :class="{ lost: lostCount > 0 }">{{ lostCount.toLocaleString() }} lost</span>
			</span>
		</div>

		<div class="group view-controls">
			<button class="btn icon" @click="$emit('zoomOut')" :disabled="view.msPerCol >= maxZoomMs" title="Zoom out (−, mouse wheel)">−</button>
			<span class="zoom-label" :title="zoomTitle">{{ spanLabel }}</span>
			<button class="btn icon" @click="$emit('zoomIn')" :disabled="view.msPerCol <= minZoomMs" title="Zoom in (+, mouse wheel)">+</button>
			<button class="btn" @click="$emit('fitAll')" title="Show everything (F)">Fit</button>
			<button v-if="isRunning" class="btn live-btn" :class="{ on: view.live }" @click="$emit('goLive')"
				:title="view.live ? 'Showing the latest data' : 'Jump to the latest data (End, double-click)'">
				<span class="live-dot"></span>{{ view.live ? 'Live' : 'Go live' }}
			</button>
			<button v-else class="btn" @click="$emit('goLive')" title="Jump to the end of the data (End, double-click)">End ⇥</button>
		</div>

		<div class="group right">
			<button class="btn" @click="$emit('showHistory')" title="Open a saved session">History</button>
			<button class="btn" @click="$emit('showLog')" title="Show or hide the event log (L)">Log<span v-if="unreadLog" class="badge">{{ unreadLog }}</span></button>
			<button class="btn icon" @click="$emit('showSettings')" title="Settings">⚙</button>
			<button class="btn icon" @click="$emit('showHelp')" title="Graph controls and legend (?)">?</button>
			<span class="conn" :class="{ ok: connected }" :title="connected ? 'Connected to PingTracer' : 'Not connected — retrying…'"></span>
		</div>
	</div>
</template>

<script>
import { formatSpan } from '@/library/format';
import { ZOOM_LEVELS } from '@/library/GraphRenderer';

export default {
	name: 'ToolBar',
	props: {
		configurations: { type: Array, default: () => [] },
		selectedConfigGuid: { type: String, default: null },
		isRunning: Boolean,
		status: { type: String, default: 'Idle' },
		sentCount: { type: Number, default: 0 },
		lostCount: { type: Number, default: 0 },
		connected: Boolean,
		viewingHistory: Boolean,
		unreadLog: { type: Number, default: 0 },
		view: { type: Object, default: () => ({ live: true, msPerCol: 500, widthPx: 1000, dpr: 1, startMs: 0, endMs: 0 }) },
	},
	emits: ['selectConfig', 'editConfig', 'newConfig', 'start', 'stop', 'zoomIn', 'zoomOut', 'fitAll', 'goLive', 'showHistory', 'showLog', 'showSettings', 'showHelp'],
	computed: {
		minZoomMs() { return ZOOM_LEVELS[0]; },
		maxZoomMs() { return ZOOM_LEVELS[ZOOM_LEVELS.length - 1]; },
		spanLabel()
		{
			return formatSpan(this.view.endMs - this.view.startMs);
		},
		zoomTitle()
		{
			const perCssPx = this.view.msPerCol * (this.view.dpr || 1);
			return `Visible span. Each pixel is ${formatSpan(perCssPx)}.`;
		},
	},
};
</script>

<style scoped>
.toolbar {
	display: flex;
	align-items: center;
	gap: 14px;
	padding: 5px 10px;
	background: #17171f;
	border-bottom: 1px solid #2c2c3a;
	flex-shrink: 0;
	flex-wrap: wrap;
	font-size: 13px;
}
.group { display: flex; align-items: center; gap: 4px; }
.right { margin-left: auto; }
.config-select {
	background: #23232f;
	color: #e6e6e6;
	border: 1px solid #3c3c4c;
	border-radius: 3px;
	padding: 4px 6px;
	font-size: 13px;
	min-width: 200px;
	max-width: 320px;
}
.btn {
	background: #2a2a38;
	color: #e0e0e0;
	border: 1px solid #44445a;
	border-radius: 3px;
	padding: 4px 10px;
	cursor: pointer;
	font-size: 13px;
	line-height: 16px;
	white-space: nowrap;
	position: relative;
}
.btn:hover:not(:disabled) { background: #383850; }
.btn:disabled { opacity: 0.45; cursor: default; }
.btn.icon { padding: 4px 7px; min-width: 28px; }
.start { background: #24481c; border-color: #3a7030; }
.start:hover:not(:disabled) { background: #2f5e25; }
.stop { background: #5e1d1d; border-color: #8a2e2e; }
.stop:hover:not(:disabled) { background: #742626; }
.status-text { color: #8a8a96; }
.status-text.active { color: #8fe38f; }
.counts { color: #9a9aa6; font-size: 12px; }
.counts .lost { color: #ff8a8a; }
.zoom-label {
	min-width: 64px;
	text-align: center;
	color: #cfd2ff;
	font-variant-numeric: tabular-nums;
}
.live-btn { display: flex; align-items: center; gap: 6px; }
.live-dot { width: 8px; height: 8px; border-radius: 50%; background: #666; }
.live-btn.on { border-color: #3a7030; color: #b8f0b8; }
.live-btn.on .live-dot { background: #4ce04c; box-shadow: 0 0 6px #4ce04c; }
.badge {
	display: inline-block;
	margin-left: 5px;
	background: #b03030;
	color: #fff;
	border-radius: 8px;
	padding: 0 5px;
	font-size: 10px;
	line-height: 14px;
}
.conn { width: 9px; height: 9px; border-radius: 50%; background: #a33; margin-left: 4px; }
.conn.ok { background: #4c4; }
</style>
