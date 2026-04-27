<template>
	<div class="app-container" @keydown="onKeyDown" tabindex="0">
		<ToolBar :configurations="store.configurations" :selectedConfigGuid="store.selectedConfigGuid"
			:isRunning="store.isRunning" :status="store.status" :successfulPings="store.successfulPings"
			:failedPings="store.failedPings" :connected="store.connected" @selectConfig="store.selectConfig"
			@start="store.startPinging" @stop="store.stopPinging" @editConfig="editCurrentConfig"
			@newConfig="newConfig" @showLog="showLog = true" />

		<div class="graph-area" ref="graphArea">
			<template v-if="graphRows.length > 0">
				<div v-for="(row, idx) in graphRows" :key="row.key" class="graph-container"
					:style="graphStyle(idx)">
					<PingGraph ref="graphs" :segments="row.segments"
						:displayName="row.displayName" :config="configForGraphs"
						:viewportStartUtc="viewportStartUtc" :viewportEndUtc="viewportEndUtcEffective"
						:isLive="isLive" :scrollOffsetMs="scrollOffsetMs"
						@wheel="onGraphWheel" @dragStart="onDragStart" />
				</div>
			</template>
			<div v-else class="no-graphs">
				<div class="no-graphs-content">
					<p v-if="!store.selectedConfigGuid">Select or create a configuration to begin.</p>
					<p v-else-if="!store.isRunning">Press <strong>Start</strong> to begin pinging.</p>
					<p v-else>Waiting for route discovery...</p>
				</div>
			</div>
		</div>

		<TimeScale v-if="graphRows.length > 0"
			:viewportStartUtc="viewportStartUtc" :viewportEndUtc="viewportEndUtcEffective" />

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
import { usePingStore, keyFor } from '@/stores/ping';

// Time-based viewport: viewportEndUtc is the right edge of the graph in
// Unix ms. `null` means live (auto-tracks Date.now()).
// viewportDurationMs is the total visible time span.
const DEFAULT_VIEWPORT_DURATION_MS = 60 * 1000;       // 1 minute
const MIN_VIEWPORT_DURATION_MS = 1000;                // 1 second
const MAX_VIEWPORT_DURATION_MS = 7 * 86400 * 1000;    // 7 days
const LIVE_QUERY_THRESHOLD_MS = 5 * 60 * 1000;        // beyond 5 minutes, live mode also queries

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

			// Time-based viewport state
			viewportEndUtc: null,                         // null = live
			viewportDurationMs: DEFAULT_VIEWPORT_DURATION_MS,
			liveNow: Date.now(),                          // refreshed on a timer when live

			// Drag state
			isDragging: false,
			dragStartX: 0,
			dragStartEndUtc: 0,

			// Zoom tooltip
			zoomTooltipVisible: false,
			zoomTooltipX: 0,
			zoomTooltipY: 0,
			zoomTooltipText: '',
			zoomTooltipTimer: null,

			_liveRafId: null,
			_lastQueryViewport: null,
		};
	},
	computed: {
		isLive() { return this.viewportEndUtc === null; },

		viewportEndUtcEffective()
		{
			return this.isLive ? this.liveNow : this.viewportEndUtc;
		},

		viewportStartUtc()
		{
			return this.viewportEndUtcEffective - this.viewportDurationMs;
		},

		scrollOffsetMs()
		{
			// Distance from "now" to the right edge, in ms. 0 when live.
			if (this.isLive) return 0;
			return Math.max(0, this.liveNow - this.viewportEndUtc);
		},

		graphRows()
		{
			// One row per (sessionIndex, hopNumber). Each row carries
			// every series segment whose [startUtc, endUtc] overlaps the
			// viewport. PingGraph renders the segments in time order with
			// vertical seams at boundaries.
			const rows = [];
			const sessions = this.store.sessions || [];
			const routes = this.store.routes || {};
			const hopHistory = this.store.hopHistory || {};
			const liveTail = this.store.liveTail || {};
			const history = this.store.history || {};

			const vStart = this.viewportStartUtc;
			const vEnd = this.viewportEndUtcEffective;

			for (const session of sessions)
			{
				const sIdx = session.index;
				const route = routes[sIdx];
				const bySession = hopHistory[sIdx];

				// Collect every hop number we know about (active + closed).
				const hopNumbers = new Set();
				if (route && route.hops) for (const h of route.hops) if (h) hopNumbers.add(h.hopNumber);
				if (bySession) for (const k of Object.keys(bySession)) hopNumbers.add(parseInt(k, 10));

				const sortedHops = Array.from(hopNumbers).sort((a, b) => a - b);
				for (const hopNumber of sortedHops)
				{
					const segs = (bySession && bySession[hopNumber]) || [];
					const visibleSegs = [];
					for (const seg of segs)
					{
						const segStart = seg.seriesStartUtc;
						const segEnd = seg.seriesEndUtc != null ? seg.seriesEndUtc : Number.POSITIVE_INFINITY;
						// Always include synthetic unresponsive segment (start=0).
						// Real segments must overlap the viewport to be considered.
						if (segStart !== 0 && (segEnd < vStart || segStart > vEnd)) continue;

						const k = keyFor(sIdx, hopNumber, seg.seriesStartUtc);
						const histEntry = history[k];
						const tail = liveTail[k] || [];

						// Build bucket list. History buckets first (already aggregated),
						// then live-tail entries as 1-sample buckets after the last
						// history bucket's timestamp (to avoid double-drawing).
						// We do not filter buckets by viewport here — the renderer
						// uses the next bucket's t for bar widths and needs adjacent
						// out-of-viewport buckets to render edge bars correctly.
						const buckets = [];
						const lastHistT = histEntry && histEntry.points.length > 0
							? histEntry.points[histEntry.points.length - 1].t
							: -1;
						if (histEntry)
							for (const p of histEntry.points) buckets.push(p);
						for (const p of tail)
						{
							if (p.t <= lastHistT) continue;
							const isFail = p.ms === 0xFFFF || p.ms === 0xFFFE;
							buckets.push({
								t: p.t,
								min: isFail ? null : p.ms,
								max: isFail ? null : p.ms,
								avg: isFail ? null : p.ms,
								lossPct: isFail ? 100 : 0,
								samples: 1,
							});
						}

						visibleSegs.push({
							address: seg.address,
							hostname: seg.hostname,
							seriesStartUtc: seg.seriesStartUtc,
							seriesEndUtc: seg.seriesEndUtc,
							buckets,
						});
					}

					// Pick latest visible segment for the row label; fall back to
					// last known segment if none in window.
					let labelSeg = null;
					if (visibleSegs.length > 0) labelSeg = visibleSegs[visibleSegs.length - 1];
					else if (segs.length > 0) labelSeg = segs[segs.length - 1];
					if (!labelSeg) continue;

					const label = labelSeg.address === '*'
						? `${hopNumber + 1}. (no response)`
						: labelSeg.hostname && labelSeg.hostname.length > 0
							? `${hopNumber + 1}. ${labelSeg.hostname} [${labelSeg.address}]`
							: `${hopNumber + 1}. ${labelSeg.address}`;

					rows.push({
						key: `${sIdx}:${hopNumber}`,
						sessionIndex: sIdx,
						hopNumber,
						displayName: label,
						addressChanged: visibleSegs.length > 1,
						segments: visibleSegs,
					});
				}
			}
			return rows;
		},

		configForGraphs()
		{
			return this.previewConfig || this.store.configDetails;
		},

		graphAreaWidth()
		{
			return this.$refs.graphArea?.clientWidth || 800;
		},

		earliestSessionStartUtc()
		{
			let earliest = Number.POSITIVE_INFINITY;
			for (const s of this.store.sessions || [])
				if (s.sessionStartUtc && s.sessionStartUtc < earliest) earliest = s.sessionStartUtc;
			return Number.isFinite(earliest) ? earliest : null;
		},
	},
	watch: {
		'store.sessions'() { this.maybeQueryViewport(true); },
		viewportEndUtc() { this.maybeQueryViewport(); },
		viewportDurationMs() { this.maybeQueryViewport(); },
	},
	mounted()
	{
		this.store.connect();
		document.addEventListener('mouseup', this.onMouseUp);
		document.addEventListener('mousemove', this.onDocMouseMove);

		// rAF-driven live tick: ~60fps when tab is visible, paused when not.
		const tick = () =>
		{
			if (this.isLive) this.liveNow = Date.now();
			this._liveRafId = requestAnimationFrame(tick);
		};
		this._liveRafId = requestAnimationFrame(tick);

		this._onBeforeUnload = () => this.store.disconnect();
		window.addEventListener('beforeunload', this._onBeforeUnload);
	},
	beforeUnmount()
	{
		window.removeEventListener('beforeunload', this._onBeforeUnload);
		this.store.disconnect();
		document.removeEventListener('mouseup', this.onMouseUp);
		document.removeEventListener('mousemove', this.onDocMouseMove);
		if (this.zoomTooltipTimer) clearTimeout(this.zoomTooltipTimer);
		if (this._liveRafId) cancelAnimationFrame(this._liveRafId);
	},
	methods: {
		graphStyle(index)
		{
			const count = this.graphRows.length;
			if (count === 0) return {};
			const pct = 100 / count;
			return {
				height: pct + '%',
				borderBottom: index < count - 1 ? '1px solid #555' : 'none',
			};
		},

		// --- Viewport queries ---

		maybeQueryViewport(force = false)
		{
			const sessions = this.store.sessions || [];
			if (sessions.length === 0) return;

			const w = this.graphAreaWidth;
			const maxPoints = Math.max(64, Math.ceil(w));

			const startUtc = Math.floor(this.viewportStartUtc);
			const endUtc = Math.ceil(this.viewportEndUtcEffective);

			// Skip queries entirely when live AND viewport is small enough that
			// the live tail covers it.
			if (this.isLive && this.viewportDurationMs < LIVE_QUERY_THRESHOLD_MS && !force)
				return;

			for (const s of sessions)
				this.store.requestViewportData(s.index, startUtc, endUtc, maxPoints, force);
		},

		// --- Zoom (time-based) ---

		clampDuration(d)
		{
			return Math.max(MIN_VIEWPORT_DURATION_MS, Math.min(MAX_VIEWPORT_DURATION_MS, d));
		},

		onGraphWheel(ev)
		{
			const factor = ev.deltaY > 0 ? 1.15 : (1 / 1.15); // scroll down = zoom out (longer span)
			const newDur = this.clampDuration(this.viewportDurationMs * factor);
			if (newDur === this.viewportDurationMs) return;

			// Anchor zoom on the time under the mouse: keep that time at the same x.
			const mouseFrac = (ev.clientX - ev.rectLeft) / Math.max(1, ev.rectWidth);
			const anchorTime = this.viewportStartUtc + this.viewportDurationMs * mouseFrac;
			const newEnd = anchorTime + newDur * (1 - mouseFrac);

			this.viewportDurationMs = newDur;
			this._setEnd(newEnd);

			this.showZoomTooltip(ev.clientX, ev.clientY);
		},

		showZoomTooltip(clientX, clientY)
		{
			this.zoomTooltipText = this.formatDuration(this.viewportDurationMs);
			this.zoomTooltipX = clientX + 14;
			this.zoomTooltipY = clientY - 10;
			this.zoomTooltipVisible = true;

			if (this.zoomTooltipTimer) clearTimeout(this.zoomTooltipTimer);
			this.zoomTooltipTimer = setTimeout(() => { this.zoomTooltipVisible = false; }, 1500);

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

		formatDuration(ms)
		{
			if (ms < 1000) return ms + 'ms';
			const s = ms / 1000;
			if (s < 60) return s.toFixed(1) + 's';
			const m = s / 60;
			if (m < 60) return m.toFixed(1) + 'm';
			const h = m / 60;
			if (h < 24) return h.toFixed(1) + 'h';
			return (h / 24).toFixed(1) + 'd';
		},

		// --- Drag/Pan (time-based) ---

		onDragStart(clientX)
		{
			this.isDragging = true;
			this.dragStartX = clientX;
			this.dragStartEndUtc = this.viewportEndUtcEffective;
		},

		onMouseUp() { this.isDragging = false; },

		onDocMouseMove(e)
		{
			if (!this.isDragging) return;
			const w = this.graphAreaWidth;
			const dx = e.clientX - this.dragStartX;
			// Drag right = grab and pan to older data = decrease viewportEndUtc.
			const dtMs = (dx / w) * this.viewportDurationMs;
			this._setEnd(this.dragStartEndUtc - dtMs);
		},

		_setEnd(newEnd)
		{
			const now = Date.now();
			// Anything at or past now snaps to live (no future allowed).
			if (newEnd >= now - 250)
			{
				this.viewportEndUtc = null;
				this.liveNow = now;
				return;
			}
			// Allow panning far enough back that the oldest buffered data scrolls
			// off the right side of the view (i.e. the entire viewport sits before
			// the data window). Stop one full viewport before that to avoid endless
			// blank scrolling.
			const earliest = this.earliestSessionStartUtc;
			if (earliest != null)
			{
				const minEnd = earliest - this.viewportDurationMs;
				if (newEnd < minEnd) newEnd = minEnd;
			}
			this.viewportEndUtc = newEnd;
		},

		// --- Keyboard ---

		onKeyDown(e)
		{
			if (this.graphRows.length === 0) return;

			switch (e.key)
			{
				case 'Home':
				case '9': {
					const earliest = this.earliestSessionStartUtc;
					if (earliest != null)
						this._setEnd(earliest + this.viewportDurationMs);
					e.preventDefault();
					break;
				}
				case 'End':
				case '0':
					this.viewportEndUtc = null;
					this.liveNow = Date.now();
					e.preventDefault();
					break;
				case 'PageUp':
				case '-':
					this._setEnd(this.viewportEndUtcEffective - this.viewportDurationMs);
					e.preventDefault();
					break;
				case 'PageDown':
				case '=':
					this._setEnd(this.viewportEndUtcEffective + this.viewportDurationMs);
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
