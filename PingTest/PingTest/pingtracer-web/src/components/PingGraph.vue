<template>
	<canvas ref="canvas" class="ping-graph" @mousemove="onMouseMove" @mouseleave="onMouseLeave"
		@mousedown="onMouseDown" @wheel.prevent="onWheel"></canvas>
</template>

<script>
import { toRaw } from 'vue';

const IPStatus_Success = 0;

// Colors matching the WinForms PingGraphControl
const COLOR_BG = '#000000';
const COLOR_SUCCESS = '#408040';
const COLOR_BAD = '#808000';
const COLOR_WORSE = '#ffff00';
const COLOR_FAILURE = '#ff0000';
const COLOR_TEXT = '#ffffff';
const COLOR_BG_BAD = '#232300';
const COLOR_BG_WORSE = '#280000';

const SCALING_CLASSIC = 0;
const SCALING_ZOOM = 1;
const SCALING_ZOOM_UNLIMITED = 2;
const SCALING_FIXED = 3;

// Sentinel value for "timeout" — treated as highest possible ms for aggregation
const TIMEOUT_VALUE = 999999;

export default {
	name: 'PingGraph',
	props: {
		pings: { type: Array, required: true },
		displayName: { type: String, default: '' },
		config: { type: Object, default: () => ({}) },
		/** CSS pixels per ping (zoom level) */
		pixelsPerPing: { type: Number, required: true },
		/** Scroll offset in pings from the right (0 = live) */
		scrollOffset: { type: Number, required: true },
		isLive: { type: Boolean, default: false },
	},
	emits: ['wheel', 'dragStart'],
	data()
	{
		return {
			mouseX: -1,
			mouseY: -1,
			resizeObserver: null,
			animFrameId: null,
		};
	},
	computed: {
		pingCount() { return this.pings.length; },
		badThreshold() { return this.config?.badThreshold ?? 100; },
		worseThreshold() { return this.config?.worseThreshold ?? 200; },
		upperLimit() { return this.config?.upperLimit ?? 300; },
		lowerLimit() { return this.config?.lowerLimit ?? 0; },
		scalingMethod() { return this.config?.scalingMethodID ?? SCALING_CLASSIC; },
		showServerNames() { return this.config?.drawServerNames ?? true; },
		showLastPing() { return this.config?.drawLastPing ?? true; },
		showAverage() { return this.config?.drawAverage ?? true; },
		showJitter() { return this.config?.drawJitter ?? false; },
		showMinMax() { return this.config?.drawMinMax ?? false; },
		showPacketLoss() { return this.config?.drawPacketLoss ?? true; },
		drawLimitText() { return this.config?.drawLimitText ?? false; },
	},
	watch: {
		pingCount() { this.scheduleRender(); },
		pixelsPerPing() { this.scheduleRender(); },
		scrollOffset() { this.scheduleRender(); },
		config: {
			deep: true,
			handler() { this.scheduleRender(); }
		}
	},
	mounted()
	{
		this.resizeObserver = new ResizeObserver(() =>
		{
			this.updateCanvasSize();
			this.scheduleRender();
		});
		this.resizeObserver.observe(this.$refs.canvas);
		this.updateCanvasSize();
		this.scheduleRender();
	},
	beforeUnmount()
	{
		if (this.resizeObserver)
			this.resizeObserver.disconnect();
		if (this.animFrameId)
			cancelAnimationFrame(this.animFrameId);
	},
	methods: {
		updateCanvasSize()
		{
			const canvas = this.$refs.canvas;
			if (!canvas) return;
			const rect = canvas.getBoundingClientRect();
			const dpr = window.devicePixelRatio || 1;
			canvas.width = rect.width * dpr;
			canvas.height = rect.height * dpr;
		},

		scheduleRender()
		{
			if (this.animFrameId) return;
			this.animFrameId = requestAnimationFrame(() =>
			{
				this.animFrameId = null;
				this.render();
			});
		},

		render()
		{
			const canvas = this.$refs.canvas;
			if (!canvas) return;
			const ctx = canvas.getContext('2d');
			const dpr = window.devicePixelRatio || 1;
			const physW = canvas.width;   // physical pixels
			const physH = canvas.height;
			const logicalW = physW / dpr; // CSS pixels
			const logicalH = physH / dpr;

			ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

			// Clear background
			ctx.fillStyle = COLOR_BG;
			ctx.fillRect(0, 0, logicalW, logicalH);

			const graphHeight = logicalH;
			if (graphHeight <= 0) return;

			// Use toRaw() to bypass Vue reactivity proxy overhead in the
			// hot loop. Each proxy get-trap costs ~50-100ns; at 200K+
			// visible pings the overhead alone can exceed a frame budget.
			const pings = toRaw(this.pings);
			const ppp = this.pixelsPerPing; // CSS pixels per ping
			const totalPings = pings ? pings.length : 0;

			// Maximum bars per physical pixel. With pixel-aligned rendering
			// each bar occupies an integer number of physical pixels, so
			// values above 1.0 offer diminishing returns.
			const MAX_DATA_POINTS_PER_PIXEL = 1.0;

			// How many pings are visible in the viewport?
			const visiblePingCount = logicalW / ppp;

			// Viewport in ping-index space
			const rightIdx = totalPings - this.scrollOffset;
			const leftIdx = rightIdx - visiblePingCount;

			// Maximum number of drawable columns
			const maxColumns = Math.ceil(physW * MAX_DATA_POINTS_PER_PIXEL);

			// --- Stable bucketing ---
			// Bucket boundaries are aligned to absolute ping indices so the
			// same pings always belong to the same bucket regardless of
			// viewport position. This prevents visual instability when
			// outlier values shift between adjacent buckets on each frame.
			const bucketSize = Math.max(1, Math.ceil(visiblePingCount / maxColumns));

			// Visible ping range (clamped to available data)
			const visStart = Math.max(0, Math.floor(leftIdx));
			const visEnd = Math.min(totalPings, Math.ceil(rightIdx));

			if (visStart >= visEnd)
			{
				// No visible data — draw label only
				ctx.font = '12px sans-serif';
				ctx.fillStyle = COLOR_TEXT;
				if (this.showServerNames && this.displayName)
					ctx.fillText(this.displayName, 3, 14);
				return;
			}

			// Absolute bucket index range covering visible pings
			const firstBucket = Math.floor(visStart / bucketSize);
			const lastBucket = Math.floor((visEnd - 1) / bucketSize);
			const numBuckets = lastBucket - firstBucket + 1;

			// Bucket value arrays
			const bucketMax = new Float32Array(numBuckets);
			const bucketFail = new Uint8Array(numBuckets);
			bucketMax.fill(-1);

			// --- Single pass: compute stats AND bucket aggregation ---
			let statMin = Infinity, statMax = -Infinity, statSum = 0, statSucc = 0, statLast = 0;
			let statTotal = 0;

			const rangeStart = Math.max(0, firstBucket * bucketSize);
			const rangeEnd = Math.min(totalPings, (lastBucket + 1) * bucketSize);

			for (let i = rangeStart; i < rangeEnd; i++)
			{
				const p = pings[i];
				if (!p) continue;

				statTotal++;
				const bIdx = Math.floor(i / bucketSize) - firstBucket;

				if (p.s === IPStatus_Success)
				{
					statSucc++;
					statLast = p.ms;
					statSum += p.ms;
					if (p.ms > statMax) statMax = p.ms;
					if (p.ms < statMin) statMin = p.ms;
					if (p.ms > bucketMax[bIdx]) bucketMax[bIdx] = p.ms;
				}
				else
				{
					bucketFail[bIdx] = 1;
				}
			}

			const avg = statSucc > 0 ? Math.round(statSum / statSucc) : 0;
			if (statMin === Infinity) statMin = 0;
			if (statMax === -Infinity) statMax = 0;
			const packetLoss = statTotal > 0 ? ((statTotal - statSucc) / statTotal * 100) : 0;

			// Resolve: failure in a bucket overrides any success values
			for (let b = 0; b < numBuckets; b++)
			{
				if (bucketFail[b])
					bucketMax[b] = TIMEOUT_VALUE;
			}

			// Y-axis scaling
			let lowerLimitDraw, upperLimitDraw;
			switch (this.scalingMethod)
			{
				case SCALING_CLASSIC:
					lowerLimitDraw = this.lowerLimit;
					upperLimitDraw = this.lowerLimit + graphHeight;
					if (statMax > upperLimitDraw)
						upperLimitDraw = Math.ceil(statMax * 1.1);
					if (upperLimitDraw > this.upperLimit)
						upperLimitDraw = this.upperLimit;
					break;
				case SCALING_ZOOM:
					lowerLimitDraw = Math.max(statMin - 1, this.lowerLimit);
					upperLimitDraw = Math.min(statMax + 1, this.upperLimit);
					break;
				case SCALING_ZOOM_UNLIMITED:
					lowerLimitDraw = statMin - 1;
					upperLimitDraw = statMax + 1;
					break;
				case SCALING_FIXED:
					lowerLimitDraw = Math.max(this.lowerLimit, 0);
					upperLimitDraw = Math.max(this.upperLimit, 1);
					break;
				default:
					lowerLimitDraw = 0;
					upperLimitDraw = graphHeight;
			}
			if (upperLimitDraw <= lowerLimitDraw)
				upperLimitDraw = lowerLimitDraw + 1;

			const drawRange = upperLimitDraw - lowerLimitDraw;
			const vScale = graphHeight / drawRange;

			// Draw threshold background zones
			const scaledBadLine = (this.badThreshold - lowerLimitDraw) * vScale;
			const scaledWorseLine = (this.worseThreshold - lowerLimitDraw) * vScale;

			if (scaledWorseLine < graphHeight)
			{
				ctx.fillStyle = COLOR_BG_WORSE;
				ctx.fillRect(0, 0, logicalW, graphHeight - scaledWorseLine);
			}
			if (scaledBadLine < graphHeight)
			{
				ctx.fillStyle = COLOR_BG_BAD;
				ctx.fillRect(0, graphHeight - scaledWorseLine, logicalW, scaledWorseLine - scaledBadLine);
			}

			// --- Draw bars with physical-pixel alignment ---
			// Snapping bar coordinates to physical pixel boundaries
			// eliminates sub-pixel anti-aliasing that causes brightness
			// variation as bars slide across the canvas from frame to frame.
			const pixelSize = 1 / dpr; // one physical pixel in CSS units

			const colorBuckets = {
				[COLOR_SUCCESS]: [],
				[COLOR_BAD]: [],
				[COLOR_WORSE]: [],
				[COLOR_FAILURE]: [],
			};

			for (let b = 0; b < numBuckets; b++)
			{
				const val = bucketMax[b];
				if (val < 0) continue; // no data

				// Map absolute bucket index to screen CSS coordinates
				const absBucket = firstBucket + b;
				const rawX = (absBucket * bucketSize - leftIdx) * ppp;
				const rawXEnd = ((absBucket + 1) * bucketSize - leftIdx) * ppp;

				// Snap to physical pixel boundaries
				const x = Math.round(rawX * dpr) / dpr;
				const xEnd = Math.round(rawXEnd * dpr) / dpr;
				const w = Math.max(pixelSize, xEnd - x);

				let color, barH;
				if (bucketFail[b])
				{
					color = COLOR_FAILURE;
					barH = graphHeight;
				}
				else if (val <= lowerLimitDraw)
				{
					continue; // below visible range
				}
				else
				{
					barH = Math.max(pixelSize, (val - lowerLimitDraw) * vScale);
					if (val < this.badThreshold)
						color = COLOR_SUCCESS;
					else if (val < this.worseThreshold)
						color = COLOR_BAD;
					else
						color = COLOR_WORSE;
				}

				const y = graphHeight - barH;
				colorBuckets[color].push(x, y, w, barH);
			}

			// Draw each color's bars in one batch
			for (const color of [COLOR_SUCCESS, COLOR_BAD, COLOR_WORSE, COLOR_FAILURE])
			{
				const rects = colorBuckets[color];
				if (rects.length === 0) continue;
				ctx.fillStyle = color;
				ctx.beginPath();
				for (let i = 0; i < rects.length; i += 4)
				{
					ctx.rect(rects[i], rects[i + 1], rects[i + 2], rects[i + 3]);
				}
				ctx.fill();
			}

			// Draw limit text
			if (this.drawLimitText)
			{
				ctx.font = '11px sans-serif';
				ctx.fillStyle = COLOR_TEXT;
				const lowerLabel = lowerLimitDraw.toString();
				const upperLabel = upperLimitDraw.toString();
				const lowerW = ctx.measureText(lowerLabel).width;
				const upperW = ctx.measureText(upperLabel).width;
				ctx.fillText(lowerLabel, logicalW - lowerW - 2, graphHeight - 2);
				ctx.fillText(upperLabel, logicalW - upperW - 2, 12);
			}

			// Build status text
			let statusStr = '';

			if (this.scrollOffset > 0)
				statusStr += 'NOT LIVE -' + this.scrollOffset + ': ';
			else if (this.isLive)
				statusStr += 'LIVE ';

			if (this.showPacketLoss)
				statusStr += packetLoss.toFixed(2) + '% ';

			const intVals = [];
			if (this.showLastPing) intVals.push(statLast);
			if (this.showAverage) intVals.push(avg);
			if (this.showJitter) intVals.push(Math.abs(statMax - statMin));
			if (this.showMinMax) { intVals.push(statMin); intVals.push(statMax); }

			if (intVals.length > 0)
				statusStr += '[' + intVals.join(',') + '] ';

			// Mouseover hint
			const hintText = this.getMouseoverHint(pings, leftIdx, ppp, graphHeight, vScale, lowerLimitDraw);
			if (hintText)
			{
				if (this.displayName) statusStr += this.displayName + ' ';
				statusStr += hintText;
			}
			else if (this.showServerNames && this.displayName)
			{
				statusStr += this.displayName;
			}

			// Draw status text
			ctx.font = '12px sans-serif';
			ctx.fillStyle = COLOR_TEXT;
			ctx.fillText(statusStr, 3, 14);
		},

		getMouseoverHint(pings, leftIdx, ppp, graphHeight, vScale, lowerLimitDraw)
		{
			if (this.mouseX < 0 || this.mouseY < 0) return '';

			const mouseMs = Math.round(((graphHeight - this.mouseY) / graphHeight) * (graphHeight / vScale) + lowerLimitDraw);

			if (ppp <= 0 || !pings || pings.length === 0) return 'Mouse ms: ' + mouseMs;

			const dataIdx = Math.floor(leftIdx + this.mouseX / ppp);

			if (dataIdx < 0 || dataIdx >= pings.length)
				return 'Mouse ms: ' + mouseMs;

			const p = pings[dataIdx];
			if (!p) return 'Waiting for response, Mouse ms: ' + mouseMs;

			const time = new Date(p.t).toLocaleTimeString();
			if (p.s !== IPStatus_Success)
				return time + ': ' + this.getStatusName(p.s) + ', Mouse ms: ' + mouseMs;

			return time + ': ' + p.ms + ' ms, Mouse ms: ' + mouseMs;
		},

		getStatusName(status)
		{
			const names = {
				0: 'Success', 11001: 'BufferTooSmall', 11002: 'DestinationNetUnreachable',
				11003: 'DestinationHostUnreachable', 11004: 'DestinationProtocolUnreachable',
				11005: 'DestinationPortUnreachable', 11006: 'NoResources', 11007: 'BadOption',
				11008: 'HardwareError', 11009: 'PacketTooBig', 11010: 'TimedOut',
				11011: 'BadRoute', 11012: 'TtlExpired', 11013: 'TtlReassemblyTimeExceeded',
				11014: 'ParameterProblem', 11015: 'SourceQuench', 11016: 'BadDestination',
				11018: 'DestinationUnreachable', 11032: 'TimeExceeded', 11033: 'BadHeader',
				11034: 'UnrecognizedNextHeader', 11035: 'IcmpError', 11036: 'DestinationScopeMismatch',
				65536: 'Unknown'
			};
			return names[status] || 'Status ' + status;
		},

		// --- Input Handlers ---

		onMouseMove(e)
		{
			const rect = this.$refs.canvas.getBoundingClientRect();
			this.mouseX = e.clientX - rect.left;
			this.mouseY = e.clientY - rect.top;
			this.scheduleRender();
		},

		onMouseLeave()
		{
			this.mouseX = -1;
			this.mouseY = -1;
			this.scheduleRender();
		},

		onMouseDown(e)
		{
			if (e.button === 0)
			{
				this.$emit('dragStart', e.clientX);
			}
		},

		onWheel(e)
		{
			const rect = this.$refs.canvas.getBoundingClientRect();
			this.$emit('wheel', {
				deltaY: e.deltaY,
				clientX: e.clientX,
				clientY: e.clientY,
				rectLeft: rect.left,
				rectWidth: rect.width,
			});
		},
	}
};
</script>

<style scoped>
.ping-graph {
	width: 100%;
	height: 100%;
	display: block;
	cursor: crosshair;
	background: #000;
}
</style>
