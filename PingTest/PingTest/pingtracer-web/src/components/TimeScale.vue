<template>
	<canvas ref="canvas" class="time-scale"></canvas>
</template>

<script>
const COLOR_BG = '#000000';
const COLOR_TIMESTAMP_TEXT = '#c8c8c8';
const COLOR_TIMESTAMP_MARK = '#808080';
const COLOR_TIMESTAMP_BORDER = '#808080';

// Candidate label intervals, in seconds.
const LABEL_INTERVALS = [
	1,            // 1 second
	5,            // 5 seconds
	10,           // 10 seconds
	30,           // 30 seconds
	60,           // 1 minute
	5 * 60,
	10 * 60,
	15 * 60,
	30 * 60,
	60 * 60,      // 1 hour
	2 * 3600,
	3 * 3600,
	6 * 3600,
	12 * 3600,
	24 * 3600,    // 1 day
	7 * 86400,
	30 * 86400,
	365 * 86400,
];

const MIN_LABEL_SPACING_PX = 80;

export default {
	name: 'TimeScale',
	props: {
		viewportStartUtc: { type: Number, required: true },
		viewportEndUtc: { type: Number, required: true },
	},
	data()
	{
		return {
			resizeObserver: null,
			animFrameId: null,
		};
	},
	watch: {
		viewportStartUtc() { this.scheduleRender(); },
		viewportEndUtc() { this.scheduleRender(); },
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
			ctx.fillStyle = COLOR_BG;
			ctx.fillRect(0, 0, logicalW, logicalH);

			ctx.strokeStyle = COLOR_TIMESTAMP_BORDER;
			ctx.lineWidth = 1;
			ctx.beginPath();
			ctx.moveTo(0, 0.5);
			ctx.lineTo(logicalW, 0.5);
			ctx.stroke();

			const vStart = this.viewportStartUtc;
			const vEnd = this.viewportEndUtc;
			const vDur = vEnd - vStart;
			if (!(vDur > 0) || logicalW <= 0) return;

			const pxPerSec = logicalW / (vDur / 1000);

			let chosenInterval = LABEL_INTERVALS[LABEL_INTERVALS.length - 1];
			for (const iv of LABEL_INTERVALS)
			{
				if (iv * pxPerSec >= MIN_LABEL_SPACING_PX)
				{
					chosenInterval = iv;
					break;
				}
			}

			const intervalMs = chosenInterval * 1000;
			const startBoundary = Math.floor(vStart / intervalMs) * intervalMs;

			ctx.font = '11px sans-serif';
			let lastLabelRightEdge = -Infinity;

			for (let t = startBoundary; t <= vEnd + intervalMs; t += intervalMs)
			{
				const x = ((t - vStart) / vDur) * logicalW;
				if (x > logicalW + 100) break;
				if (x < -100) continue;

				const dt = new Date(t);

				ctx.strokeStyle = COLOR_TIMESTAMP_MARK;
				ctx.lineWidth = 1;
				ctx.beginPath();
				ctx.moveTo(x + 0.5, 1);
				ctx.lineTo(x + 0.5, logicalH);
				ctx.stroke();

				let stamp;
				if (chosenInterval >= 365 * 86400)
					stamp = dt.getFullYear().toString();
				else if (chosenInterval >= 30 * 86400)
					stamp = dt.toLocaleDateString([], { month: 'short', year: 'numeric' });
				else if (chosenInterval >= 86400)
					stamp = dt.toLocaleDateString([], { month: 'short', day: 'numeric' });
				else if (chosenInterval >= 3600)
					stamp = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
				else if (chosenInterval >= 60)
					stamp = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
				else
					stamp = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });

				const sw = ctx.measureText(stamp).width;
				const labelX = x + 2;

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
