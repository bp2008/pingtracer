<template>
	<canvas ref="canvas" class="ping-graph" @mousemove="onMouseMove" @mouseleave="onMouseLeave"
		@mousedown="onMouseDown" @wheel.prevent="onWheel"></canvas>
</template>

<script>
import { toRaw } from 'vue';

const COLOR_TEXT = '#ffffff';
const COLOR_SEAM = '#00b0b0';

// Pre-computed Uint32 ABGR pixel values for direct ImageData manipulation.
// On little-endian (all modern browsers): byte order [R,G,B,A] → 0xAABBGGRR.
const U32_BG = 0xFF000000;
const U32_SUCCESS = 0xFF408040;
const U32_BAD = 0xFF008080;
const U32_WORSE = 0xFF00FFFF;
const U32_FAILURE = 0xFF0000FF;
const U32_BG_BAD = 0xFF002323;
const U32_BG_WORSE = 0xFF000028;
const U32_SEAM = 0xFFB0B000; // teal/cyan-ish in ABGR (little-endian)

const SCALING_CLASSIC = 0;
const SCALING_ZOOM = 1;
const SCALING_ZOOM_UNLIMITED = 2;
const SCALING_FIXED = 3;

export default {
	name: 'PingGraph',
	props: {
		/**
		 * Array of series segments to render in this row, each:
		 * { address, hostname, seriesStartUtc, seriesEndUtc|null,
		 *   buckets: [{t, min, max, avg, lossPct, samples}] }
		 */
		segments: { type: Array, required: true },
		displayName: { type: String, default: '' },
		config: { type: Object, default: () => ({}) },
		viewportStartUtc: { type: Number, required: true },
		viewportEndUtc: { type: Number, required: true },
		isLive: { type: Boolean, default: false },
		scrollOffsetMs: { type: Number, default: 0 },
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
		this._rc = {
			offscreen: null,
			offCtx: null,
			cacheKey: '',
			stats: null,
			yAxis: null,
			imgData: null,
			pixels: null,
		};
	},
	computed: {
		bucketCount()
		{
			let n = 0;
			for (const seg of this.segments) n += seg.buckets.length;
			return n;
		},
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
		bucketCount() { this.scheduleRender(); },
		viewportStartUtc() { this.scheduleRender(); },
		viewportEndUtc() { this.scheduleRender(); },
		segments: { deep: true, handler() { this.scheduleRender(); } },
		config: { deep: true, handler() { this.scheduleRender(); } },
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
		if (this.resizeObserver) this.resizeObserver.disconnect();
		if (this.animFrameId) cancelAnimationFrame(this.animFrameId);
		this._rc = null;
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

		render()
		{
			const canvas = this.$refs.canvas;
			if (!canvas) return;
			const ctx = canvas.getContext('2d');
			const dpr = window.devicePixelRatio || 1;
			const physW = canvas.width;
			const physH = canvas.height;
			const logicalW = physW / dpr;
			const logicalH = physH / dpr;

			if (logicalH <= 0 || logicalW <= 0) return;

			const segments = toRaw(this.segments);
			const vStart = this.viewportStartUtc;
			const vEnd = this.viewportEndUtc;
			const vDur = Math.max(1, vEnd - vStart);

			// Cache key: viewport + bucket count + thresholds.
			const cacheKey = vStart + '|' + vEnd + '|' + this.bucketCount + '|'
				+ physW + '|' + physH + '|'
				+ this.scalingMethod + '|' + this.badThreshold + '|'
				+ this.worseThreshold + '|' + this.upperLimit + '|' + this.lowerLimit;

			const rc = this._rc;
			if (cacheKey !== rc.cacheKey)
			{
				if (!rc.offscreen || rc.offscreen.width !== physW || rc.offscreen.height !== physH)
				{
					rc.offscreen = document.createElement('canvas');
					rc.offscreen.width = physW;
					rc.offscreen.height = physH;
					rc.offCtx = rc.offscreen.getContext('2d');
				}
				const result = this._renderBars(rc.offCtx, dpr, logicalW, logicalH, segments, vStart, vEnd, vDur, physW, physH);
				rc.stats = result.stats;
				rc.yAxis = result.yAxis;
				rc.cacheKey = cacheKey;
			}

			ctx.setTransform(1, 0, 0, 1, 0, 0);
			ctx.drawImage(rc.offscreen, 0, 0);
			ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

			this._renderOverlay(ctx, logicalW, logicalH, segments, vStart, vDur, rc.stats, rc.yAxis);
		},

		_renderBars(ctx, dpr, logicalW, logicalH, segments, vStart, vEnd, vDur, physW, physH)
		{
			const graphHeight = logicalH;

			const badThreshold = this.badThreshold;
			const worseThreshold = this.worseThreshold;
			const upperLimit = this.upperLimit;
			const lowerLimit = this.lowerLimit;
			const scalingMethod = this.scalingMethod;

			const rc = this._rc;
			if (!rc.imgData || rc.imgData.width !== physW || rc.imgData.height !== physH)
			{
				rc.imgData = ctx.createImageData(physW, physH);
				rc.pixels = new Uint32Array(rc.imgData.data.buffer);
			}
			const pixels = rc.pixels;

			// Build a flat list of (bucket, segment) tuples sorted by t. This
			// lets each bar span from its t to the next bar's t regardless of
			// segment boundaries, which butts segments together and eliminates
			// "holes" between consecutive series within the same hop.
			const flat = [];
			for (const seg of segments)
				for (const b of seg.buckets)
					flat.push({ b, seg });
			flat.sort((a, b) => a.b.t - b.b.t);

			// --- Pass 1: compute stats from all visible buckets ---
			// "Visible" includes any bucket whose [t, nextT] band overlaps the
			// viewport, so we do not under-count edge-clipped bars.
			let statMin = Infinity, statMax = -Infinity, statSum = 0, statSucc = 0;
			let statTotal = 0, statFail = 0, statLastT = -1, statLast = 0;

			for (let i = 0; i < flat.length; i++)
			{
				const b = flat[i].b;
				const nextT = (i + 1 < flat.length) ? flat[i + 1].b.t : Number.POSITIVE_INFINITY;
				if (nextT < vStart || b.t > vEnd) continue;

				statTotal += b.samples || 1;
				if (b.avg != null)
				{
					const succThis = Math.max(1, Math.round((b.samples || 1) * (1 - (b.lossPct || 0) / 100)));
					statSucc += succThis;
					statSum += b.avg * succThis;
					if (b.max != null && b.max > statMax) statMax = b.max;
					if (b.min != null && b.min < statMin) statMin = b.min;
					if (b.t > statLastT) { statLastT = b.t; statLast = b.avg; }
				}
				else
				{
					statFail += b.samples || 1;
				}
			}

			const avg = statSucc > 0 ? Math.round(statSum / statSucc) : 0;
			if (statMin === Infinity) statMin = 0;
			if (statMax === -Infinity) statMax = 0;
			const packetLoss = statTotal > 0 ? (statFail / statTotal * 100) : 0;

			// Y-axis scaling
			let lowerLimitDraw, upperLimitDraw;
			switch (scalingMethod)
			{
				case SCALING_CLASSIC:
					lowerLimitDraw = lowerLimit;
					upperLimitDraw = lowerLimit + graphHeight;
					if (statMax > upperLimitDraw) upperLimitDraw = Math.ceil(statMax * 1.1);
					if (upperLimitDraw > upperLimit) upperLimitDraw = upperLimit;
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
			if (upperLimitDraw <= lowerLimitDraw) upperLimitDraw = lowerLimitDraw + 1;

			const drawRange = upperLimitDraw - lowerLimitDraw;
			const vScale = graphHeight / drawRange;

			// --- Background + threshold zones ---
			pixels.fill(U32_BG);

			const scaledBadLine = (badThreshold - lowerLimitDraw) * vScale;
			const scaledWorseLine = (worseThreshold - lowerLimitDraw) * vScale;
			const worseEndPhys = Math.max(0, Math.min(physH, Math.round((graphHeight - scaledWorseLine) * dpr)));
			const badEndPhys = Math.max(0, Math.min(physH, Math.round((graphHeight - scaledBadLine) * dpr)));
			if (worseEndPhys > 0) pixels.fill(U32_BG_WORSE, 0, worseEndPhys * physW);
			if (badEndPhys > worseEndPhys) pixels.fill(U32_BG_BAD, worseEndPhys * physW, badEndPhys * physW);

			const vScaleDpr = vScale * dpr;
			const pxPerMs = physW / vDur;

			// --- Pass 2: draw bars ---
			// Each bar spans from its t to the next bar's t (regardless of
			// segment) so consecutive series butt up with no visual hole.
			// Bars whose extent crosses the viewport edges are clipped to the
			// edge but still rendered, so partial bars at the left/right
			// remain visible when zoomed in.
			for (let i = 0; i < flat.length; i++)
			{
				const b = flat[i].b;
				const nextT = (i + 1 < flat.length) ? flat[i + 1].b.t : vEnd;
				const tStart = b.t;
				const tEnd = nextT;
				if (tEnd < vStart || tStart > vEnd) continue;

				const xLeft = (tStart - vStart) * pxPerMs;
				const xRight = (tEnd - vStart) * pxPerMs;

				const pxLeft = Math.max(0, Math.round(xLeft));
				const pxRight = Math.min(physW, Math.max(pxLeft + 1, Math.round(xRight)));

				let colorU32, pxTop;
				if (b.avg == null)
				{
					colorU32 = U32_FAILURE;
					pxTop = 0;
				}
				else
				{
					const heightFromVal = b.max != null ? b.max : b.avg;
					if (heightFromVal <= lowerLimitDraw) continue;
					const barPhysH = Math.max(1, Math.round((heightFromVal - lowerLimitDraw) * vScaleDpr));
					pxTop = Math.max(0, physH - barPhysH);
					if (heightFromVal < badThreshold) colorU32 = U32_SUCCESS;
					else if (heightFromVal < worseThreshold) colorU32 = U32_BAD;
					else colorU32 = U32_WORSE;
				}

				for (let row = pxTop; row < physH; row++)
				{
					const offset = row * physW;
					pixels.fill(colorU32, offset + pxLeft, offset + pxRight);
				}

				// Loss overlay: thin red bar at bottom proportional to lossPct.
				if (b.avg != null && b.lossPct > 0)
				{
					const lossRows = Math.max(1, Math.round(physH * 0.05 * (b.lossPct / 100)));
					const lossTop = physH - lossRows;
					for (let row = lossTop; row < physH; row++)
					{
						const offset = row * physW;
						pixels.fill(U32_FAILURE, offset + pxLeft, offset + pxRight);
					}
				}
			}

			// --- Pass 3: seam markers between segments ---
			// Place a vertical line wherever one segment ends and the next begins.
			for (let s = 1; s < segments.length; s++)
			{
				const seamT = segments[s].seriesStartUtc;
				if (seamT < vStart || seamT > vEnd) continue;
				const x = Math.round((seamT - vStart) * pxPerMs);
				if (x < 0 || x >= physW) continue;
				for (let row = 0; row < physH; row++)
				{
					pixels[row * physW + x] = U32_SEAM;
				}
			}

			ctx.putImageData(rc.imgData, 0, 0);

			return {
				stats: { avg, statMin, statMax, statLast, packetLoss, statTotal, statSucc },
				yAxis: { lowerLimitDraw, upperLimitDraw, vScale },
			};
		},

		_renderOverlay(ctx, logicalW, logicalH, segments, vStart, vDur, stats, yAxis)
		{
			if (!stats || !yAxis) return;

			const graphHeight = logicalH;
			const { lowerLimitDraw, upperLimitDraw, vScale } = yAxis;
			const { avg, statMin, statMax, statLast, packetLoss } = stats;

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

			let statusStr = '';
			if (this.scrollOffsetMs > 0)
				statusStr += 'NOT LIVE -' + this.formatScrollOffset(this.scrollOffsetMs) + ': ';
			else if (this.isLive)
				statusStr += 'LIVE ';

			if (this.showPacketLoss) statusStr += packetLoss.toFixed(2) + '% ';

			const intVals = [];
			if (this.showLastPing) intVals.push(statLast);
			if (this.showAverage) intVals.push(avg);
			if (this.showJitter) intVals.push(Math.abs(statMax - statMin));
			if (this.showMinMax) { intVals.push(statMin); intVals.push(statMax); }

			if (intVals.length > 0) statusStr += '[' + intVals.join(',') + '] ';

			const hintText = this.getMouseoverHint(segments, vStart, vDur, logicalW, graphHeight, vScale, lowerLimitDraw);
			if (hintText)
			{
				if (this.displayName) statusStr += this.displayName + ' ';
				statusStr += hintText;
			}
			else if (this.showServerNames && this.displayName)
			{
				statusStr += this.displayName;
			}

			ctx.font = '12px sans-serif';
			ctx.fillStyle = COLOR_TEXT;
			ctx.fillText(statusStr, 3, 14);

			// Seam labels: draw a small marker at the top of each seam.
			if (segments.length > 1)
			{
				ctx.fillStyle = COLOR_SEAM;
				ctx.font = '10px sans-serif';
				const pxPerMs = logicalW / vDur;
				for (let s = 1; s < segments.length; s++)
				{
					const seamT = segments[s].seriesStartUtc;
					if (seamT < vStart || seamT > vStart + vDur) continue;
					const x = (seamT - vStart) * pxPerMs;
					ctx.fillText('▼', x - 4, 10);
				}
			}
		},

		formatScrollOffset(ms)
		{
			if (ms < 1000) return ms + 'ms';
			const s = ms / 1000;
			if (s < 60) return s.toFixed(0) + 's';
			const m = s / 60;
			if (m < 60) return m.toFixed(1) + 'm';
			const h = m / 60;
			if (h < 24) return h.toFixed(1) + 'h';
			return (h / 24).toFixed(1) + 'd';
		},

		getMouseoverHint(segments, vStart, vDur, logicalW, graphHeight, vScale, lowerLimitDraw)
		{
			if (this.mouseX < 0 || this.mouseY < 0) return '';

			const mouseMs = Math.round(((graphHeight - this.mouseY) / graphHeight) * (graphHeight / vScale) + lowerLimitDraw);
			const mouseT = vStart + (this.mouseX / logicalW) * vDur;

			// Pick the segment whose [start, end) window contains the mouse
			// time. This makes the address shown reflect the actual segment
			// the cursor is over, not the segment of the nearest bucket
			// (which would mislabel positions near a series boundary).
			let activeSeg = null;
			for (const seg of segments)
			{
				const segStart = seg.seriesStartUtc;
				const segEnd = seg.seriesEndUtc != null ? seg.seriesEndUtc : Number.POSITIVE_INFINITY;
				if (mouseT >= segStart && mouseT <= segEnd) { activeSeg = seg; break; }
			}
			// Fallback: nearest segment by midpoint, in case windows don't cover mouseT.
			if (!activeSeg && segments.length > 0)
			{
				let bestDist = Infinity;
				for (const seg of segments)
				{
					const mid = (seg.seriesStartUtc + (seg.seriesEndUtc != null ? seg.seriesEndUtc : Date.now())) / 2;
					const d = Math.abs(mid - mouseT);
					if (d < bestDist) { bestDist = d; activeSeg = seg; }
				}
			}

			// Within the active segment, find the bucket nearest mouseT.
			let bucket = null;
			if (activeSeg)
			{
				let bestDist = Infinity;
				for (const b of activeSeg.buckets)
				{
					const d = Math.abs(b.t - mouseT);
					if (d < bestDist) { bestDist = d; bucket = b; }
				}
			}

			const time = new Date(mouseT).toLocaleTimeString();
			const addrStr = activeSeg ? ' [' + activeSeg.address
				+ (activeSeg.hostname ? ' ' + activeSeg.hostname : '') + ']' : '';

			if (!bucket) return time + addrStr + ', Mouse ms: ' + mouseMs;

			const bt = new Date(bucket.t).toLocaleTimeString();
			let info;
			if (bucket.avg == null)
				info = bt + ': lost' + (bucket.samples > 1 ? ' (' + bucket.samples + ' sent)' : '');
			else if (bucket.samples === 1)
				info = bt + ': ' + bucket.avg + ' ms';
			else
				info = bt + ': avg ' + bucket.avg + ' (' + bucket.min + '-' + bucket.max
					+ ', n=' + bucket.samples + ', loss ' + bucket.lossPct.toFixed(1) + '%)';
			return info + addrStr + ', Mouse ms: ' + mouseMs;
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
			if (e.button === 0) this.$emit('dragStart', e.clientX);
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
