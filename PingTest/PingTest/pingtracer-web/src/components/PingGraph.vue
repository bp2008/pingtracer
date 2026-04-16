<template>
	<canvas ref="canvas" class="ping-graph" @mousemove="onMouseMove" @mouseleave="onMouseLeave"
		@mousedown="onMouseDown" @wheel.prevent="onWheel"></canvas>
</template>

<script>
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
const COLOR_TIMESTAMP_TEXT = '#c8c8c8';
const COLOR_TIMESTAMP_MARK = '#808080';
const COLOR_TIMESTAMP_BORDER = '#808080';

const SCALING_CLASSIC = 0;
const SCALING_ZOOM = 1;
const SCALING_ZOOM_UNLIMITED = 2;
const SCALING_FIXED = 3;

export default {
	name: 'PingGraph',
	props: {
		/** Array of ping data: [{ t: unixMs, ms: pingTime, s: ipStatus }, ...] */
		pings: { type: Array, required: true },
		/** Display name shown in upper-left */
		displayName: { type: String, default: '' },
		/** Whether this is the bottom graph (show timestamps) */
		showTimestamps: { type: Boolean, default: false },
		/** Config display options */
		config: { type: Object, default: () => ({}) },
	},
	data()
	{
		return {
			scrollXOffset: 0,
			zoomLevel: 1, // 1 = 1 ping per pixel (default). >1 = zoomed out, <1 = zoomed in
			mouseX: -1,
			mouseY: -1,
			isDragging: false,
			dragStartX: 0,
			dragStartScroll: 0,
			resizeObserver: null,
			animFrameId: null,
			setLiveAtTime: 0,
		};
	},
	computed: {
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
		pings()
		{
			this.scheduleRender();
		},
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

		document.addEventListener('mouseup', this.onMouseUp);
		document.addEventListener('mousemove', this.onDocMouseMove);
	},
	beforeUnmount()
	{
		if (this.resizeObserver)
			this.resizeObserver.disconnect();
		if (this.animFrameId)
			cancelAnimationFrame(this.animFrameId);
		document.removeEventListener('mouseup', this.onMouseUp);
		document.removeEventListener('mousemove', this.onDocMouseMove);
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
			const W = canvas.width;
			const H = canvas.height;
			const logicalW = W / dpr;
			const logicalH = H / dpr;

			ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

			// Clear background
			ctx.fillStyle = COLOR_BG;
			ctx.fillRect(0, 0, logicalW, logicalH);

			const timestampsHeight = this.showTimestamps ? 16 : 0;
			const graphHeight = logicalH - timestampsHeight;
			if (graphHeight <= 0) return;

			const pings = this.pings;
			if (!pings || pings.length === 0) return;

			// Calculate how many pings are visible at current zoom level
			const visiblePings = Math.floor(logicalW * this.zoomLevel);

			// Calculate the viewport range in ping-index space
			const totalPings = pings.length;
			const scrollMax = Math.max(0, totalPings - visiblePings);
			const clampedScroll = Math.min(Math.max(0, this.scrollXOffset), scrollMax);

			// Determine the range of pings to display
			const startIdx = Math.max(0, totalPings - visiblePings - clampedScroll);
			const endIdx = Math.min(totalPings, startIdx + visiblePings);
			const count = endIdx - startIdx;

			if (count <= 0) return;

			// Calc stats over visible range
			let min = Infinity, max = -Infinity, sum = 0, successCount = 0, last = 0;
			for (let i = startIdx; i < endIdx; i++)
			{
				const p = pings[i];
				if (p && p.s === IPStatus_Success)
				{
					successCount++;
					last = p.ms;
					sum += last;
					if (last > max) max = last;
					if (last < min) min = last;
				}
			}
			const avg = successCount > 0 ? Math.round(sum / successCount) : 0;
			if (min === Infinity) min = 0;
			if (max === -Infinity) max = 0;
			const packetLoss = count > 0 ? ((count - successCount) / count * 100) : 0;

			// Y-axis scaling
			let lowerLimitDraw, upperLimitDraw;
			switch (this.scalingMethod)
			{
				case SCALING_CLASSIC:
					lowerLimitDraw = this.lowerLimit;
					upperLimitDraw = this.lowerLimit + graphHeight;
					if (max > upperLimitDraw)
						upperLimitDraw = Math.ceil(max * 1.1);
					if (upperLimitDraw > this.upperLimit)
						upperLimitDraw = this.upperLimit;
					break;
				case SCALING_ZOOM:
					lowerLimitDraw = Math.max(min - 1, this.lowerLimit);
					upperLimitDraw = Math.min(max + 1, this.upperLimit);
					break;
				case SCALING_ZOOM_UNLIMITED:
					lowerLimitDraw = min - 1;
					upperLimitDraw = max + 1;
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

			const drawHeight = upperLimitDraw - lowerLimitDraw;
			const vScale = graphHeight / drawHeight;

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

			// Draw timestamp border
			if (this.showTimestamps)
			{
				ctx.strokeStyle = COLOR_TIMESTAMP_BORDER;
				ctx.lineWidth = 1;
				ctx.beginPath();
				ctx.moveTo(0, graphHeight + 0.5);
				ctx.lineTo(logicalW, graphHeight + 0.5);
				ctx.stroke();
			}

			// Draw ping data
			const pixelsPerPing = logicalW / count;
			let lastStampedMinute = -1;

			for (let i = 0; i < count; i++)
			{
				const p = pings[startIdx + i];
				if (!p) continue;

				const x = i * pixelsPerPing;
				const barWidth = Math.max(1, Math.ceil(pixelsPerPing));

				if (p.s === IPStatus_Success)
				{
					if (p.ms > lowerLimitDraw)
					{
						const barH = Math.max(1, (p.ms - lowerLimitDraw) * vScale);
						const y = graphHeight - barH;

						if (p.ms < this.badThreshold)
							ctx.fillStyle = COLOR_SUCCESS;
						else if (p.ms < this.worseThreshold)
							ctx.fillStyle = COLOR_BAD;
						else
							ctx.fillStyle = COLOR_WORSE;

						ctx.fillRect(x, y, barWidth, barH);
					}
				}
				else
				{
					// Failure: draw red bar
					const failH = Math.min(graphHeight, 10000 * vScale);
					ctx.fillStyle = COLOR_FAILURE;
					ctx.fillRect(x, graphHeight - failH, barWidth, failH);
				}

				// Draw timestamps
				if (this.showTimestamps && p.t)
				{
					const dt = new Date(p.t);
					const minute = dt.getMinutes();
					if (minute !== lastStampedMinute)
					{
						if (dt.getSeconds() < 2)
						{
							ctx.strokeStyle = COLOR_TIMESTAMP_MARK;
							ctx.lineWidth = 1;
							ctx.beginPath();
							ctx.moveTo(x + 0.5, graphHeight + 1);
							ctx.lineTo(x + 0.5, logicalH);
							ctx.stroke();
						}

						const stamp = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
						ctx.font = '11px sans-serif';
						ctx.fillStyle = COLOR_BG;
						const sw = ctx.measureText(stamp).width;
						ctx.fillRect(x + 1, graphHeight + 1, sw, timestampsHeight - 1);
						ctx.fillStyle = COLOR_TIMESTAMP_TEXT;
						ctx.fillText(stamp, x + 1, graphHeight + timestampsHeight - 3);

						lastStampedMinute = minute;
					}
				}
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

			if (clampedScroll !== 0)
				statusStr += 'NOT LIVE -' + clampedScroll + ': ';
			else if (Math.abs(this.setLiveAtTime - Date.now()) < 1000)
				statusStr += 'LIVE ';

			if (this.showPacketLoss)
				statusStr += packetLoss.toFixed(2) + '% ';

			const intVals = [];
			if (this.showLastPing) intVals.push(last);
			if (this.showAverage) intVals.push(avg);
			if (this.showJitter) intVals.push(Math.abs(max - min));
			if (this.showMinMax) { intVals.push(min); intVals.push(max); }

			if (intVals.length > 0)
				statusStr += '[' + intVals.join(',') + '] ';

			// Mouseover hint
			const hintText = this.getMouseoverHint(pings, startIdx, count, pixelsPerPing, graphHeight, vScale, lowerLimitDraw, logicalW);
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

		getMouseoverHint(pings, startIdx, count, pixelsPerPing, graphHeight, vScale, lowerLimitDraw, logicalW)
		{
			if (this.mouseX < 0 || this.mouseY < 0) return '';

			const mouseMs = Math.round(((graphHeight - this.mouseY) / graphHeight) * (graphHeight / vScale) + lowerLimitDraw);

			if (pixelsPerPing <= 0 || count <= 0) return 'Mouse ms: ' + mouseMs;

			const pingIdx = Math.floor(this.mouseX / pixelsPerPing);
			const dataIdx = startIdx + pingIdx;

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
				this.isDragging = true;
				this.dragStartX = e.clientX;
				this.dragStartScroll = this.scrollXOffset;
			}
		},

		onMouseUp()
		{
			this.isDragging = false;
		},

		onDocMouseMove(e)
		{
			if (!this.isDragging) return;

			const dx = e.clientX - this.dragStartX;
			// Each pixel of drag = zoomLevel pings of scroll
			const scrollDelta = Math.round(dx * this.zoomLevel);
			const newScroll = this.dragStartScroll + scrollDelta;
			const totalPings = this.pings.length;
			const rect = this.$refs.canvas.getBoundingClientRect();
			const visiblePings = Math.floor(rect.width * this.zoomLevel);
			const scrollMax = Math.max(0, totalPings - visiblePings);

			this.scrollXOffset = Math.min(Math.max(0, newScroll), scrollMax);
			if (this.scrollXOffset === 0)
				this.setLiveAtTime = Date.now();
			this.scheduleRender();
		},

		/**
		 * Mouse wheel zoom: changes the X-axis scale.
		 * Scrolling up zooms in (fewer pings visible, more detail).
		 * Scrolling down zooms out (more pings visible, less detail).
		 */
		onWheel(e)
		{
			const oldZoom = this.zoomLevel;
			const factor = e.deltaY > 0 ? 1.15 : 1 / 1.15;
			let newZoom = oldZoom * factor;

			// Clamp zoom: min 0.1 (10x zoom in), max = enough to show all data
			const maxZoom = Math.max(1, this.pings.length / 100);
			newZoom = Math.max(0.1, Math.min(newZoom, maxZoom));

			if (newZoom === oldZoom) return;

			// Zoom centered on mouse position
			const rect = this.$refs.canvas.getBoundingClientRect();
			const mouseXFraction = (e.clientX - rect.left) / rect.width;

			const oldVisiblePings = rect.width * oldZoom;
			const newVisiblePings = rect.width * newZoom;
			const pingDelta = (oldVisiblePings - newVisiblePings) * mouseXFraction;

			this.zoomLevel = newZoom;
			this.scrollXOffset = Math.max(0, Math.round(this.scrollXOffset - pingDelta));
			if (this.scrollXOffset === 0)
				this.setLiveAtTime = Date.now();
			this.scheduleRender();
		},

		/**
		 * Scroll to live position (most recent data).
		 */
		scrollToLive()
		{
			this.scrollXOffset = 0;
			this.setLiveAtTime = Date.now();
			this.scheduleRender();
		},

		/**
		 * Scroll to oldest data.
		 */
		scrollToOldest()
		{
			const rect = this.$refs.canvas.getBoundingClientRect();
			const visiblePings = Math.floor(rect.width * this.zoomLevel);
			this.scrollXOffset = Math.max(0, this.pings.length - visiblePings);
			this.scheduleRender();
		},

		/**
		 * Scroll left/right by a page.
		 */
		scrollPage(direction)
		{
			const rect = this.$refs.canvas.getBoundingClientRect();
			const visiblePings = Math.floor(rect.width * this.zoomLevel);
			this.scrollXOffset += direction * visiblePings;
			const scrollMax = Math.max(0, this.pings.length - visiblePings);
			this.scrollXOffset = Math.min(Math.max(0, this.scrollXOffset), scrollMax);
			if (this.scrollXOffset === 0)
				this.setLiveAtTime = Date.now();
			this.scheduleRender();
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
