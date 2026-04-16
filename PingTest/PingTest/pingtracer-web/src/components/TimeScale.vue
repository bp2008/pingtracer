<template>
	<canvas ref="canvas" class="time-scale"></canvas>
</template>

<script>
const COLOR_BG = '#000000';
const COLOR_TIMESTAMP_TEXT = '#c8c8c8';
const COLOR_TIMESTAMP_MARK = '#808080';
const COLOR_TIMESTAMP_BORDER = '#808080';

// Candidate label intervals, in seconds.
// The algorithm picks the smallest interval whose labels don't physically overlap.
const LABEL_INTERVALS = [
	60,          // 1 minute
	5 * 60,      // 5 minutes
	10 * 60,     // 10 minutes
	15 * 60,     // 15 minutes
	30 * 60,     // 30 minutes
	60 * 60,     // 1 hour
	2 * 3600,    // 2 hours
	3 * 3600,    // 3 hours
	12 * 3600,   // 12 hours
	24 * 3600,   // 1 day
	7 * 86400,   // 1 week
	30 * 86400,  // ~1 month
	365 * 86400, // ~1 year
];

const MIN_LABEL_SPACING_PX = 80; // minimum CSS pixels between labels

export default {
	name: 'TimeScale',
	props: {
		pings: { type: Array, required: true },
		pixelsPerPing: { type: Number, required: true },
		scrollOffset: { type: Number, required: true },
	},
	data()
	{
		return {
			resizeObserver: null,
			animFrameId: null,
		};
	},
	computed: {
		pingCount() { return this.pings.length; },
	},
	watch: {
		pingCount() { this.scheduleRender(); },
		pixelsPerPing() { this.scheduleRender(); },
		scrollOffset() { this.scheduleRender(); },
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
			const logicalW = canvas.width / dpr;
			const logicalH = canvas.height / dpr;

			ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

			// Clear
			ctx.fillStyle = COLOR_BG;
			ctx.fillRect(0, 0, logicalW, logicalH);

			// Top border
			ctx.strokeStyle = COLOR_TIMESTAMP_BORDER;
			ctx.lineWidth = 1;
			ctx.beginPath();
			ctx.moveTo(0, 0.5);
			ctx.lineTo(logicalW, 0.5);
			ctx.stroke();

			const pings = this.pings;
			if (!pings || pings.length === 0) return;

			const ppp = this.pixelsPerPing;
			if (ppp <= 0) return;

			const totalPings = pings.length;
			const visiblePings = logicalW / ppp;
			const rightIdx = totalPings - this.scrollOffset;
			const leftIdx = rightIdx - visiblePings;

			// Estimate seconds-per-ping from data (assume ~1 ping/sec if unknown)
			let secPerPing = 1;
			if (totalPings >= 2)
			{
				const first = pings[0];
				const last = pings[totalPings - 1];
				if (first && last && first.t && last.t)
				{
					const dt = (last.t - first.t) / 1000;
					if (dt > 0)
						secPerPing = dt / (totalPings - 1);
				}
			}

			// Choose the best label interval.
			// Compute how many CSS pixels one second represents.
			const pxPerSec = ppp / secPerPing;

			let chosenInterval = LABEL_INTERVALS[LABEL_INTERVALS.length - 1];
			for (const iv of LABEL_INTERVALS)
			{
				if (iv * pxPerSec >= MIN_LABEL_SPACING_PX)
				{
					chosenInterval = iv;
					break;
				}
			}

			// Find the first and last timestamps in the visible range
			const iStart = Math.max(0, Math.floor(leftIdx));
			const iEnd = Math.min(totalPings, Math.ceil(rightIdx));

			if (iStart >= iEnd) return;

			// Find first ping with a valid timestamp to establish time mapping
			let firstTs = null, firstTsIdx = -1;
			for (let i = iStart; i < iEnd; i++)
			{
				if (pings[i] && pings[i].t)
				{
					firstTs = pings[i].t;
					firstTsIdx = i;
					break;
				}
			}
			if (firstTs === null) return;

			// Find the first aligned boundary before the visible range
			const intervalMs = chosenInterval * 1000;
			const startBoundary = Math.floor(firstTs / intervalMs) * intervalMs;

			// Draw labels at each boundary
			ctx.font = '11px sans-serif';
			let lastLabelRightEdge = -Infinity;

			for (let t = startBoundary; ; t += intervalMs)
			{
				// Convert this timestamp to a ping index
				const pingIdx = firstTsIdx + (t - firstTs) / 1000 / secPerPing;
				const x = (pingIdx - leftIdx) * ppp;

				if (x > logicalW + 100) break; // past visible area
				if (x < -100) continue; // before visible area

				const dt = new Date(t);

				// Draw tick mark
				ctx.strokeStyle = COLOR_TIMESTAMP_MARK;
				ctx.lineWidth = 1;
				ctx.beginPath();
				ctx.moveTo(x + 0.5, 1);
				ctx.lineTo(x + 0.5, logicalH);
				ctx.stroke();

				// Format label based on interval granularity
				let stamp;
				if (chosenInterval >= 365 * 86400)
					stamp = dt.getFullYear().toString();
				else if (chosenInterval >= 30 * 86400)
					stamp = dt.toLocaleDateString([], { month: 'short', year: 'numeric' });
				else if (chosenInterval >= 7 * 86400)
					stamp = dt.toLocaleDateString([], { month: 'short', day: 'numeric' });
				else if (chosenInterval >= 86400)
					stamp = dt.toLocaleDateString([], { month: 'short', day: 'numeric' });
				else if (chosenInterval >= 3600)
					stamp = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
				else
					stamp = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });

				const sw = ctx.measureText(stamp).width;
				const labelX = x + 2;

				// Only draw label if it won't overlap the previous one
				if (labelX > lastLabelRightEdge + 4)
				{
					ctx.fillStyle = COLOR_BG;
					ctx.fillRect(labelX, 1, sw, logicalH - 1);
					ctx.fillStyle = COLOR_TIMESTAMP_TEXT;
					ctx.fillText(stamp, labelX, logicalH - 3);
					lastLabelRightEdge = labelX + sw;
				}
			}
		},
	},
};
</script>

<style scoped>
.time-scale {
	width: 100%;
	height: 18px;
	display: block;
	background: #000;
	flex-shrink: 0;
}
</style>
