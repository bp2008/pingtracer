<template>
	<div class="graph-view" ref="root" tabindex="0"
		@mousedown="onMouseDown" @mousemove="onMouseMove" @mouseleave="onMouseLeave"
		@wheel.prevent="onWheel" @dblclick="onDoubleClick" @keydown="onKeyDown">
		<div v-if="tip" class="tooltip" :style="tipStyle">
			<div class="tip-title">{{ tip.title }}</div>
			<div class="tip-time">{{ tip.time }}</div>
			<div v-if="tip.address" class="tip-addr">
				<span class="swatch" v-if="tip.swatch" :style="{ background: tip.swatch }"></span>{{ tip.address }}
				<span v-if="tip.host" class="tip-host">{{ tip.host }}</span>
			</div>
			<div v-if="tip.multi" class="tip-note">Several addresses answered in this interval (load balancing)</div>
			<div class="tip-value" :class="tip.valueClass">{{ tip.value }}</div>
			<div v-for="e in tip.events" :key="e" class="tip-event">⟂ {{ e }}</div>
		</div>
	</div>
</template>

<script>
import { GraphRenderer, ZOOM_LEVELS, ADDRESS_PALETTE_CSS } from '@/library/GraphRenderer';
import socket from '@/library/WebSocketService';
import { formatColumnRange } from '@/library/format';

const ZOOM_STORAGE_KEY = 'pingtracer.msPerCol';

export default {
	name: 'GraphView',
	props: {
		config: { type: Object, default: null },
		/** While viewing a saved session, zoom changes are not remembered as the user's preferred live zoom. */
		historyMode: { type: Boolean, default: false },
	},
	emits: ['viewChanged'],
	data()
	{
		return {
			tip: null,
			tipX: 0,
			tipY: 0,
		};
	},
	computed: {
		tipStyle()
		{
			const w = this.$refs.root ? this.$refs.root.clientWidth : 1000;
			const h = this.$refs.root ? this.$refs.root.clientHeight : 600;
			const left = this.tipX + 18 + 300 > w ? Math.max(4, this.tipX - 318) : this.tipX + 18;
			const top = Math.min(this.tipY + 16, h - 120);
			return { left: left + 'px', top: Math.max(4, top) + 'px' };
		},
	},
	created()
	{
		this.renderer = null;
		this.raf = 0;
		this.drag = null;
		this.mouse = null;
		this.lastViewEmit = 0;
		this.tipKey = '';
	},
	mounted()
	{
		this.renderer = new GraphRenderer(this.$refs.root, {
			getConfig: () => this.config,
			serverNow: () => socket.serverNow(),
			onViewChanged: () => this.emitView(true),
		});
		window.__pingtracer = { renderer: this.renderer };
		let saved = 0;
		try { saved = parseInt(localStorage.getItem(ZOOM_STORAGE_KEY), 10); } catch { /* storage unavailable */ }
		if (ZOOM_LEVELS.includes(saved)) this.renderer.msPerCol = saved;
		const loop = () =>
		{
			this.raf = requestAnimationFrame(loop);
			this.renderer.frame();
			if (this.mouse && !this.drag) this.updateTip();
			this.emitView(false);
		};
		this.raf = requestAnimationFrame(loop);
		document.addEventListener('mousemove', this.onDocMouseMove);
		document.addEventListener('mouseup', this.onDocMouseUp);
		window.addEventListener('scroll', this.onScroll, true);
	},
	beforeUnmount()
	{
		cancelAnimationFrame(this.raf);
		document.removeEventListener('mousemove', this.onDocMouseMove);
		document.removeEventListener('mouseup', this.onDocMouseUp);
		window.removeEventListener('scroll', this.onScroll, true);
		this.renderer.destroy();
	},
	methods: {
		// ----- API for the toolbar -----
		zoomIn() { this.renderer.zoomStep(-1); this.saveZoom(); },
		zoomOut() { this.renderer.zoomStep(1); this.saveZoom(); },
		goLive() { this.renderer.setLive(); },
		fitAll() { this.renderer.fitAll(); this.saveZoom(); },
		pageBack() { this.renderer.panBy(this.renderer.W * 0.9); },
		pageForward() { this.renderer.panBy(-this.renderer.W * 0.9); },
		setDefaultZoomForInterval(intervalMs)
		{
			let saved = null;
			try { saved = localStorage.getItem(ZOOM_STORAGE_KEY); } catch { /* ignore */ }
			if (saved) return;
			const dprCols = Math.max(1, Math.round(window.devicePixelRatio || 1));
			const want = intervalMs / (2 * dprCols);
			let best = ZOOM_LEVELS[0];
			for (const z of ZOOM_LEVELS) if (Math.abs(Math.log(z / want)) < Math.abs(Math.log(best / want))) best = z;
			this.renderer.setZoom(best);
		},
		saveZoom()
		{
			if (this.historyMode) return;
			try { localStorage.setItem(ZOOM_STORAGE_KEY, String(this.renderer.msPerCol)); } catch { /* ignore */ }
		},
		restoreSavedZoom()
		{
			let saved = 0;
			try { saved = parseInt(localStorage.getItem(ZOOM_STORAGE_KEY), 10); } catch { /* ignore */ }
			if (ZOOM_LEVELS.includes(saved)) this.renderer.setZoom(saved);
			this.renderer.setLive();
		},
		focus() { this.$refs.root.focus(); },

		emitView(force)
		{
			const t = performance.now();
			if (!force && t - this.lastViewEmit < 250) return;
			this.lastViewEmit = t;
			const r = this.renderer;
			const v = r.visibleRange();
			this.$emit('viewChanged', { ...v, widthPx: r.W, dpr: r.dpr });
		},

		// ----- Mouse -----
		localPos(e)
		{
			const rect = this.$refs.root.getBoundingClientRect();
			return { x: e.clientX - rect.left, y: e.clientY - rect.top };
		},
		onMouseDown(e)
		{
			if (e.button !== 0) return;
			this.$refs.root.focus();
			const p = this.localPos(e);
			this.drag = { startX: p.x, lastX: p.x, moved: false };
			e.preventDefault();
		},
		onDocMouseMove(e)
		{
			if (!this.drag) return;
			const p = this.localPos(e);
			const dx = p.x - this.drag.lastX;
			if (Math.abs(p.x - this.drag.startX) > 2) this.drag.moved = true;
			const dxDev = Math.round(dx * this.renderer.dpr);
			if (dxDev !== 0)
			{
				// Dragging right pulls older data into view.
				this.renderer.panBy(dxDev);
				this.drag.lastX += dxDev / this.renderer.dpr;
			}
			this.mouse = p;
			this.renderer.setHover(p.x, p.y);
			this.tip = null;
		},
		onDocMouseUp()
		{
			if (this.drag)
			{
				this.drag = null;
				this.updateTip();
			}
		},
		onMouseMove(e)
		{
			const p = this.localPos(e);
			this.mouse = p;
			this.renderer.setHover(p.x, p.y);
			if (!this.drag) this.updateTip();
		},
		onMouseLeave()
		{
			this.mouse = null;
			this.tip = null;
			this.renderer.setHover(null, null);
		},
		onWheel(e)
		{
			const p = this.localPos(e);
			if (e.shiftKey || Math.abs(e.deltaX) > Math.abs(e.deltaY))
			{
				const d = (Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY);
				this.renderer.panBy(-d * this.renderer.dpr);
				return;
			}
			if (e.deltaY === 0) return;
			this.renderer.zoomStep(e.deltaY > 0 ? 1 : -1, p.x * this.renderer.dpr);
			this.saveZoom();
		},
		onDoubleClick()
		{
			this.renderer.setLive();
		},
		onScroll()
		{
			this.renderer._snapToPixelGrid();
		},
		onKeyDown(e)
		{
			const r = this.renderer;
			switch (e.key)
			{
				case 'End': r.setLive(); break;
				case 'Home': { const s = r._earliestStart(); if (s != null) r.goTo(s + r.W * r.msPerCol * 0.75); break; }
				case 'PageUp': this.pageBack(); break;
				case 'PageDown': this.pageForward(); break;
				case 'ArrowLeft': r.panBy(r.W * 0.1); break;
				case 'ArrowRight': r.panBy(-r.W * 0.1); break;
				case '+': case '=': this.zoomIn(); break;
				case '-': case '_': this.zoomOut(); break;
				case 'f': case 'F': this.fitAll(); break;
				default: return;
			}
			e.preventDefault();
		},

		// ----- Tooltip -----
		updateTip()
		{
			if (!this.mouse) { this.tip = null; this.tipKey = ''; return; }
			const info = this.renderer.hitTest(this.mouse.x, this.mouse.y);
			this.tipX = this.mouse.x;
			this.tipY = this.mouse.y;
			if (!info) { this.tip = null; this.tipKey = ''; return; }
			const title = info.ttl === 0 ? info.sessionName : `Hop ${info.ttl}`;
			const time = formatColumnRange(info.startMs, info.endMs, info.msPerCol);
			let value, valueClass = '';
			const succ = info.n - info.lost;
			if (info.n === 0)
			{
				value = info.row.collapsed ? 'This hop does not answer pings' : 'No data';
				valueClass = 'muted';
			}
			else if (succ === 0)
			{
				value = info.n === 1 ? 'Lost (no reply)' : `All ${info.n} pings lost`;
				valueClass = 'bad';
			}
			else if (info.n === 1)
			{
				value = `${Math.round(info.avg)} ms`;
			}
			else
			{
				value = `avg ${Math.round(info.avg)} ms · min ${info.min} · max ${info.max} · ${info.n} pings`;
				if (info.lost > 0)
				{
					value += ` · ${info.lost} lost (${(info.lost / info.n * 100).toFixed(info.lost / info.n < 0.1 ? 1 : 0)}%)`;
					valueClass = 'warn';
				}
			}
			let swatch = null;
			if (info.address && info.row.strip)
			{
				const s = info.row.session;
				const ids = [...s.addresses.entries()].find(([, a]) => a.ip === info.address);
				if (ids) swatch = ADDRESS_PALETTE_CSS[s.addressIndexAtHop(info.ttl, ids[0]) % ADDRESS_PALETTE_CSS.length];
			}
			const tip = {
				title,
				time,
				address: info.address ? info.address + (info.isDestination && info.ttl !== 0 ? '  (destination)' : '') : '',
				host: info.host,
				multi: info.multi,
				value,
				valueClass,
				swatch,
				events: info.events,
			};
			const key = JSON.stringify(tip);
			if (key !== this.tipKey)
			{
				this.tipKey = key;
				this.tip = tip;
			}
		},
	},
};

</script>

<style scoped>
.graph-view {
	position: relative;
	flex: 1;
	min-height: 0;
	overflow: hidden;
	background: #000;
	outline: none;
	cursor: crosshair;
	user-select: none;
}

.tooltip {
	position: absolute;
	z-index: 10;
	pointer-events: none;
	background: rgba(18, 18, 26, 0.96);
	border: 1px solid #3c3c50;
	border-radius: 4px;
	padding: 6px 9px;
	font-size: 12px;
	line-height: 1.45;
	color: #ddd;
	max-width: 300px;
	box-shadow: 0 4px 14px rgba(0, 0, 0, 0.5);
}

.tip-title { font-weight: 600; color: #fff; }
.tip-time { color: #9a9aac; font-size: 11px; }
.tip-addr { font-family: Consolas, monospace; color: #cfe; margin-top: 2px; }
.tip-host { display: block; font-family: 'Segoe UI', system-ui, sans-serif; color: #9ab; font-size: 11px; word-break: break-all; }
.tip-note { color: #8fd8ff; font-size: 11px; }
.tip-value { margin-top: 3px; color: #9f9; }
.tip-value.bad { color: #ff6b6b; }
.tip-value.warn { color: #ffcf6b; }
.tip-value.muted { color: #888; }
.tip-event { color: #4fd0ff; font-size: 11px; margin-top: 2px; }
.swatch { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 5px; vertical-align: 0; }
</style>
