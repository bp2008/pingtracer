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

// Pre-computed Uint32 ABGR pixel values for direct ImageData manipulation.
// On little-endian (all modern browsers): byte order [R,G,B,A] → 0xAABBGGRR.
const U32_BG = 0xFF000000;
const U32_SUCCESS = 0xFF408040;
const U32_BAD = 0xFF008080;
const U32_WORSE = 0xFF00FFFF;
const U32_FAILURE = 0xFF0000FF;
const U32_BG_BAD = 0xFF002323;
const U32_BG_WORSE = 0xFF000028;

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
	created()
	{
		// Non-reactive rendering cache — stored directly on the instance
		// to bypass Vue reactivity proxy overhead entirely.
		this._rc = {
			offscreen: null,    // offscreen canvas for cached bar rendering
			offCtx: null,       // 2D context of offscreen canvas
			cacheKey: '',       // stringified viewport+data fingerprint
			stats: null,        // cached statistics from last bar render
			yAxis: null,        // cached Y-axis parameters
			// Reusable typed arrays (grow-only, never shrunk)
			bucketMaxBuf: null,
			bucketFailBuf: null,
			bucketBufLen: 0,
			// Reusable ImageData buffer for pixel-level bar rendering
			imgData: null,
			pixels: null,       // Uint32Array view of imgData.data.buffer
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
		if (this._rc)
		{
			this._rc.offscreen = null;
			this._rc.offCtx = null;
			this._rc = null;
		}
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
			// Invalidate bar cache on resize
			if (this._rc) this._rc.cacheKey = '';
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

		/**
		 * Main render entry point.  Uses an offscreen canvas cache so that
		 * mouse-only updates (the most frequent trigger) skip the expensive
		 * data-aggregation and bar-drawing pipeline entirely.
		 */
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

			if (logicalH <= 0 || logicalW <= 0) return;

			// Bypass Vue reactivity proxy for the hot path
			const pings = toRaw(this.pings);
			const ppp = this.pixelsPerPing;
			const totalPings = pings ? pings.length : 0;
			const scrollOffset = this.scrollOffset;

			// Cache key covers every input that affects bar rendering.
			// Text-only inputs (showPacketLoss, displayName, mouse pos) are
			// excluded so that mouse hover is essentially free.
			const cacheKey = totalPings + '|' + ppp + '|' + scrollOffset + '|'
				+ physW + '|' + physH + '|'
				+ this.scalingMethod + '|' + this.badThreshold + '|'
				+ this.worseThreshold + '|' + this.upperLimit + '|' + this.lowerLimit;

			const rc = this._rc;

			if (cacheKey !== rc.cacheKey)
			{
				// Ensure offscreen canvas exists with correct physical size
				if (!rc.offscreen || rc.offscreen.width !== physW || rc.offscreen.height !== physH)
				{
					rc.offscreen = document.createElement('canvas');
					rc.offscreen.width = physW;
					rc.offscreen.height = physH;
					rc.offCtx = rc.offscreen.getContext('2d');
				}

				const result = this._renderBars(
					rc.offCtx, dpr, logicalW, logicalH,
					pings, totalPings, ppp, scrollOffset, physW, physH
				);
				rc.stats = result.stats;
				rc.yAxis = result.yAxis;
				rc.cacheKey = cacheKey;
			}

			// Blit cached bars to the visible canvas (fast GPU copy)
			ctx.setTransform(1, 0, 0, 1, 0, 0);
			ctx.drawImage(rc.offscreen, 0, 0);
			ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

			// Draw text overlay (always — depends on mouse position & live state)
			this._renderOverlay(ctx, logicalW, logicalH, pings, ppp, rc.stats, rc.yAxis);
		},

		/**
		 * Renders backgrounds, threshold zones, and ping bars onto the
		 * offscreen canvas.  Returns cached stats and Y-axis parameters
		 * for the overlay to reuse.
		 */
		_renderBars(ctx, dpr, logicalW, logicalH, pings, totalPings, ppp, scrollOffset, physW, physH)
		{
			const graphHeight = logicalH;

			// Cache config values as locals for the hot path
			const badThreshold = this.badThreshold;
			const worseThreshold = this.worseThreshold;
			const upperLimit = this.upperLimit;
			const lowerLimit = this.lowerLimit;
			const scalingMethod = this.scalingMethod;

			const MAX_DATA_POINTS_PER_PIXEL = 1.0;

			const visiblePingCount = logicalW / ppp;

			// Viewport in ping-index space
			const rightIdx = totalPings - scrollOffset;
			const leftIdx = rightIdx - visiblePingCount;

			const maxColumns = Math.ceil(physW * MAX_DATA_POINTS_PER_PIXEL);
			const bucketSize = Math.max(1, Math.ceil(visiblePingCount / maxColumns));

			// Visible ping range (clamped to available data)
			const visStart = Math.max(0, Math.floor(leftIdx));
			const visEnd = Math.min(totalPings, Math.ceil(rightIdx));

			// --- Get or grow reusable ImageData buffer ---
			const rc = this._rc;
			if (!rc.imgData || rc.imgData.width !== physW || rc.imgData.height !== physH)
			{
				rc.imgData = ctx.createImageData(physW, physH);
				rc.pixels = new Uint32Array(rc.imgData.data.buffer);
			}
			const pixels = rc.pixels;

			if (visStart >= visEnd)
			{
				// No visible data — render empty background
				pixels.fill(U32_BG);
				ctx.putImageData(rc.imgData, 0, 0);
				return {
					stats: { avg: 0, statMin: 0, statMax: 0, statLast: 0, packetLoss: 0, statTotal: 0, statSucc: 0 },
					yAxis: { lowerLimitDraw: 0, upperLimitDraw: graphHeight, vScale: 1, leftIdx },
				};
			}

			// Absolute bucket index range covering visible pings
			const firstBucket = Math.floor(visStart / bucketSize);
			const lastBucket = Math.floor((visEnd - 1) / bucketSize);
			const numBuckets = lastBucket - firstBucket + 1;

			// --- Reuse typed arrays (grow-only to avoid repeated allocation) ---
			if (rc.bucketBufLen < numBuckets)
			{
				const newLen = Math.max(numBuckets, (rc.bucketBufLen * 2) || 256);
				rc.bucketMaxBuf = new Float32Array(newLen);
				rc.bucketFailBuf = new Uint8Array(newLen);
				rc.bucketBufLen = newLen;
			}
			const bucketMax = rc.bucketMaxBuf;
			const bucketFail = rc.bucketFailBuf;
			for (let i = 0; i < numBuckets; i++) { bucketMax[i] = -1; bucketFail[i] = 0; }

			// --- Single pass: compute stats AND bucket aggregation ---
			let statMin = Infinity, statMax = -Infinity, statSum = 0, statSucc = 0, statLast = 0;
			let statTotal = 0;

			const rangeStart = Math.max(0, firstBucket * bucketSize);
			const rangeEnd = Math.min(totalPings, (lastBucket + 1) * bucketSize);

			let bIdx = 0;
			let nextBucketBoundary = (firstBucket + 1) * bucketSize;

			for (let i = rangeStart; i < rangeEnd; i++)
			{
				if (i >= nextBucketBoundary)
				{
					bIdx++;
					nextBucketBoundary += bucketSize;
				}

				const p = pings[i];
				if (!p) continue;

				statTotal++;

				if (p.s === 0)
				{
					const ms = p.ms;
					statSucc++;
					statLast = ms;
					statSum += ms;
					if (ms > statMax) statMax = ms;
					if (ms < statMin) statMin = ms;
					if (ms > bucketMax[bIdx]) bucketMax[bIdx] = ms;
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

			// Y-axis scaling
			let lowerLimitDraw, upperLimitDraw;
			switch (scalingMethod)
			{
				case SCALING_CLASSIC:
					lowerLimitDraw = lowerLimit;
					upperLimitDraw = lowerLimit + graphHeight;
					if (statMax > upperLimitDraw)
						upperLimitDraw = Math.ceil(statMax * 1.1);
					if (upperLimitDraw > upperLimit)
						upperLimitDraw = upperLimit;
					break;
				case SCALING_ZOOM:
					lowerLimitDraw = Math.max(statMin - 1, lowerLimit);
					upperLimitDraw = Math.min(statMax + 1, upperLimit);
					break;
				case SCALING_ZOOM_UNLIMITED:
					lowerLimitDraw = statMin - 1;
					upperLimitDraw = statMax + 1;
					break;
				case SCALING_FIXED:
					lowerLimitDraw = Math.max(lowerLimit, 0);
					upperLimitDraw = Math.max(upperLimit, 1);
					break;
				default:
					lowerLimitDraw = 0;
					upperLimitDraw = graphHeight;
			}
			if (upperLimitDraw <= lowerLimitDraw)
				upperLimitDraw = lowerLimitDraw + 1;

			const drawRange = upperLimitDraw - lowerLimitDraw;
			const vScale = graphHeight / drawRange;

			// --- ImageData pixel rendering (bypasses GPU anti-aliasing) ---
			// Fill background
			pixels.fill(U32_BG);

			// Draw threshold background zones as horizontal bands.
			// TypedArray.fill() on contiguous row spans is essentially memset.
			const scaledBadLine = (badThreshold - lowerLimitDraw) * vScale;
			const scaledWorseLine = (worseThreshold - lowerLimitDraw) * vScale;

			const worseEndPhys = Math.max(0, Math.min(physH, Math.round((graphHeight - scaledWorseLine) * dpr)));
			const badEndPhys = Math.max(0, Math.min(physH, Math.round((graphHeight - scaledBadLine) * dpr)));

			if (worseEndPhys > 0)
				pixels.fill(U32_BG_WORSE, 0, worseEndPhys * physW);
			if (badEndPhys > worseEndPhys)
				pixels.fill(U32_BG_BAD, worseEndPhys * physW, badEndPhys * physW);

			// --- Draw bars via direct pixel writes ---
			// Each bar is snapped to physical pixel boundaries and written
			// row-by-row using TypedArray.fill(), avoiding all GPU compositing.
			const vScaleDpr = vScale * dpr;

			for (let b = 0; b < numBuckets; b++)
			{
				const val = bucketMax[b];
				if (val < 0) continue; // no data

				const absBucket = firstBucket + b;
				const rawX = (absBucket * bucketSize - leftIdx) * ppp;
				const rawXEnd = ((absBucket + 1) * bucketSize - leftIdx) * ppp;

				// Snap to physical pixel grid
				const pxLeft = Math.max(0, Math.round(rawX * dpr));
				const pxRight = Math.min(physW, Math.max(pxLeft + 1, Math.round(rawXEnd * dpr)));

				let colorU32, pxTop;
				if (bucketFail[b])
				{
					colorU32 = U32_FAILURE;
					pxTop = 0;
				}
				else if (val <= lowerLimitDraw)
				{
					continue; // below visible range
				}
				else
				{
					const barPhysH = Math.max(1, Math.round((val - lowerLimitDraw) * vScaleDpr));
					pxTop = Math.max(0, physH - barPhysH);
					if (val < badThreshold)
						colorU32 = U32_SUCCESS;
					else if (val < worseThreshold)
						colorU32 = U32_BAD;
					else
						colorU32 = U32_WORSE;
				}

				// Fill rectangle row-by-row with TypedArray.fill()
				for (let row = pxTop; row < physH; row++)
				{
					const offset = row * physW;
					pixels.fill(colorU32, offset + pxLeft, offset + pxRight);
				}
			}

			// Write pixel buffer to canvas (ignores transform, pure CPU→GPU copy)
			ctx.putImageData(rc.imgData, 0, 0);

			return {
				stats: { avg, statMin, statMax, statLast, packetLoss, statTotal, statSucc },
				yAxis: { lowerLimitDraw, upperLimitDraw, vScale, leftIdx },
			};
		},

		/**
		 * Draws the lightweight text overlay (limit labels, status line,
		 * mouse hint) on top of the cached bar image.
		 */
		_renderOverlay(ctx, logicalW, logicalH, pings, ppp, stats, yAxis)
		{
			if (!stats || !yAxis) return;

			const graphHeight = logicalH;
			const { lowerLimitDraw, upperLimitDraw, vScale, leftIdx } = yAxis;
			const { avg, statMin, statMax, statLast, packetLoss } = stats;

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
