/**
 * Renders the ping graphs onto canvases.
 *
 * Performance model
 * -----------------
 * - Every canvas's backing store is sized in exact device pixels and the canvases are snapped to the device pixel grid,
 *   so the browser composites them 1:1 without any resampling.  (Resampling a canvas whose size or position is not
 *   pixel-aligned was the main cost of the previous renderer.)
 * - One device pixel column == one time column.  Columns are aligned to absolute time (column c covers
 *   [c * msPerCol, (c + 1) * msPerCol)), so scrolling is always a whole number of pixels.
 * - The visible data is reduced to one small record per (row, column) in a column buffer, a ring indexed by absolute
 *   column modulo the view width.  Only "dirty" columns are recomputed: newly exposed columns and columns whose data
 *   changed.  Scrolling and changes of vertical scale need no recomputation at all.
 * - The bars are painted from the column buffer by a backend (see BarsBackend.js): normally a WebGL2 fragment shader,
 *   which repaints the whole graph for a negligible CPU cost, with a Canvas 2D rasterizer as fallback.
 * - Labels and statistics live on an overlay canvas that is redrawn only when their text changes, and the hover
 *   crosshair on a canvas of its own, so neither panning nor pointer movement redraws text needlessly.
 * - Nothing here touches Vue reactivity.
 */
import dataStore, { CHUNK_COLS, ADDR_NONE, FLAG_MULTI_ADDR } from './DataStore';
import {
	GlBars, CpuBars, C, cssOf, ADDRESS_PALETTE,
	KIND_DATA, KIND_OUTSIDE, KIND_EMPTY, KIND_LOST, STATE_STRIP_SHIFT, STATE_DIM, STATE_MARKER_SHIFT, STATE_LEVEL_SHIFT,
} from './BarsBackend';

export { ADDRESS_PALETTE };
export const ADDRESS_PALETTE_CSS = ADDRESS_PALETTE.map(cssOf);

/** Milliseconds per device-pixel column for each zoom step. */
export const ZOOM_LEVELS = [
	10, 20, 50, 100, 200, 250, 500, 1000, 2000, 5000, 10000, 15000, 30000, 60000,
	120000, 300000, 600000, 900000, 1800000, 3600000, 7200000, 14400000,
];

const SCALING_CLASSIC = 0, SCALING_ZOOM = 1, SCALING_ZOOM_UNLIMITED = 2, SCALING_FIXED = 3;

const AXIS_FMT = {
	day: new Intl.DateTimeFormat([], { month: 'short', day: 'numeric' }),
	weekday: new Intl.DateTimeFormat([], { weekday: 'short', month: 'short', day: 'numeric' }),
	minute: new Intl.DateTimeFormat([], { hour: 'numeric', minute: '2-digit' }),
	second: new Intl.DateTimeFormat([], { hour: 'numeric', minute: '2-digit', second: '2-digit' }),
};

/** While the view merely follows the clock, row statistics (labels, vertical scale) are refreshed at most this often. */
const STATS_INTERVAL_MS = 250;
/** Duration of the animation that shows a zoom step (a mouse-wheel notch or a zoom button). */
const ZOOM_ANIM_MS = 200;
/** After a zoom animation, the old image stays up this long at most while the new zoom level's data loads. */
const ZOOM_SNAPSHOT_HOLD_MS = 1500;
const FONT_MAIN = '12px "Segoe UI", system-ui, sans-serif';
const FONT_SMALL = '10.5px "Segoe UI", system-ui, sans-serif';

const textWidths = new Map();

/**
 * Sets a context's font, remembering it in `ctx.fontName`: reading `ctx.font` back is slow (the browser serializes it).
 * Resizing a canvas resets its context state, so the renderer clears `fontName` then.
 */
function setFont(ctx, font)
{
	if (ctx.fontName !== font)
	{
		ctx.font = font;
		ctx.fontName = font;
	}
}

/** Width of `text` in the font last set with setFont.  Cached: the same labels and figures are measured again and again. */
function textWidth(ctx, text)
{
	const key = ctx.fontName + '\u0000' + text;
	let w = textWidths.get(key);
	if (w === undefined)
	{
		if (textWidths.size >= 4096) textWidths.clear();
		w = ctx.measureText(text).width;
		textWidths.set(key, w);
	}
	return w;
}

/** Increments a diagnostic counter in renderer.perf. */
function perfCount(r, name) { r.perf[name] = (r.perf[name] || 0) + 1; }

/** True if msPerCol is usable as a zoom level (any whole number of milliseconds within the zoom range). */
export function isValidZoom(ms)
{
	return Number.isInteger(ms) && ms >= ZOOM_LEVELS[0] && ms <= ZOOM_LEVELS[ZOOM_LEVELS.length - 1];
}

/** The smallest tidy zoom level (two significant digits) of at least `want` ms per column. */
function fitZoom(want)
{
	if (!(want > ZOOM_LEVELS[0])) return ZOOM_LEVELS[0];
	const p = Math.pow(10, Math.floor(Math.log10(want)) - 1);
	return Math.min(ZOOM_LEVELS[ZOOM_LEVELS.length - 1], Math.max(ZOOM_LEVELS[0], Math.ceil(want / p - 1e-9) * p));
}

const NICE_STEPS = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];
function niceCeil(v)
{
	if (!(v > 0)) return 1;
	const p = Math.pow(10, Math.floor(Math.log10(v)));
	for (const s of NICE_STEPS) if (s * p >= v - 1e-9) return s * p;
	return 10 * p;
}
function niceFloor(v)
{
	if (!(v > 0)) return 0;
	const p = Math.pow(10, Math.floor(Math.log10(v)));
	for (let i = NICE_STEPS.length - 1; i >= 0; i--) if (NICE_STEPS[i] * p <= v + 1e-9) return NICE_STEPS[i] * p;
	return 0;
}

const AXIS_INTERVALS = [
	1000, 2000, 5000, 10000, 15000, 30000, 60000, 120000, 300000, 600000, 900000, 1800000,
	3600000, 7200000, 10800000, 21600000, 43200000, 86400000, 2 * 86400000, 7 * 86400000, 14 * 86400000, 28 * 86400000,
];

export class GraphRenderer
{
	/**
	 * @param {HTMLElement} root Container element. Its size determines the graph size.
	 * @param {object} opts { getConfig(): object, serverNow(): number, onViewChanged(): void }
	 */
	constructor(root, opts)
	{
		this.root = root;
		this.opts = opts;
		this.layer = document.createElement('div');
		this.layer.style.cssText = 'position:absolute;left:0;top:0;will-change:transform;';
		root.appendChild(this.layer);
		this.bars = this._canvas();
		this.overlay = this._canvas();
		this.hoverCanvas = this._canvas();
		this.axis = this._canvas();
		this.backend = this._createBackend(opts.backend);
		this.ctxOverlay = this.overlay.getContext('2d');
		this.ctxHover = this.hoverCanvas.getContext('2d');
		this.ctxAxis = this.axis.getContext('2d', { alpha: false });

		this.dpr = 1;
		this.W = 0;
		this.H = 0;
		this.axisH = 0;
		this.msPerCol = 500;
		this.live = true;
		/** The zoom step being animated (see setZoom), or null. */
		this.zoomAnim = null;
		/** Duration of zoom animations; a property so that it can be slowed down from the console to inspect them. */
		this.zoomAnimMs = ZOOM_ANIM_MS;
		this.snapCanvas = null;
		this.axisReserve = 0;
		/** Whether every column in view was loaded at the last frame. */
		this.viewLoaded = true;
		/** Whether a displayed session was monitoring at the last metadata change (see _onData). */
		this.wasRunning = dataStore.getSessions().some(s => s.meta.running);
		this.rightCol = 0;

		this.rows = [];
		this.rowSignature = '';
		this.needFull = true;
		this.dirtyX0 = Infinity;
		this.dirtyX1 = -Infinity;
		this.dirtyRows = new Set();
		this.drawnRightCol = null;
		this.drawnMsPerCol = null;
		this.statsDirty = true;
		this.statsStale = false;
		this.lastStatsTime = 0;
		this.overlayDirty = true;
		this.overlayStatsStale = false;
		this.lastOverlayDraw = 0;
		this.axisDirty = true;
		this.lastAxisRightCol = null;
		this.axisLabels = new Map();
		this.hover = null;
		this.colBuf = null;
		this.colBufRows = 0;
		this.drawnPendKey = '';
		this.metaVersionSeen = -1;
		this.configKey = '';
		this.emptyMessage = '';

		this.resetPerf();
		this.unsubscribe = dataStore.subscribe((ch) => this._onData(ch));
		this.resizeObserver = new ResizeObserver((entries) => this._onResize(entries[0]));
		try { this.resizeObserver.observe(root, { box: 'device-pixel-content-box' }); }
		catch { this.resizeObserver.observe(root); }
		this._onResize(null);
	}

	destroy()
	{
		this.unsubscribe();
		this.resizeObserver.disconnect();
		if (this.dprQuery) this.dprQuery.removeEventListener('change', this.onDprChange);
		this.layer.remove();
	}

	_canvas()
	{
		const c = document.createElement('canvas');
		c.style.cssText = 'position:absolute;left:0;display:block;image-rendering:pixelated;transform-origin:0 0;';
		// A 2D context restored after a GPU reset has lost its state and contents.
		c.addEventListener('contextrestored', () =>
		{
			for (const ctx of [this.ctxOverlay, this.ctxHover, this.ctxAxis]) if (ctx) ctx.fontName = null;
			this.labelKey = null;
			this.hoverDirty = true;
			this.invalidateAll();
		});
		this.layer.appendChild(c);
		return c;
	}

	/** Picks the bar backend: WebGL2 unless 'cpu' is requested (or ?renderer=cpu is in the URL) or WebGL2 fails. */
	_createBackend(preference)
	{
		let pref = preference;
		try { pref = pref || new URLSearchParams(location.search).get('renderer'); } catch { /* ignore */ }
		if (pref !== 'cpu')
		{
			const gl = GlBars.create(this.bars, () => this.invalidateAll());
			if (gl) return gl;
		}
		return new CpuBars(this.bars);
	}

	/** Name of the active bar backend ('webgl2' or 'canvas2d'). */
	get backendName() { return this.backend.isGl ? 'webgl2' : 'canvas2d'; }

	// ------------------------------------------------------------------ layout

	_onResize(entry)
	{
		const dpr = window.devicePixelRatio || 1;
		const rect = this.root.getBoundingClientRect();
		let W = Math.round(rect.width * dpr), totalH = Math.round(rect.height * dpr);
		const dev = entry && entry.devicePixelContentBoxSize && entry.devicePixelContentBoxSize[0];
		// The exact device size from the observer, unless it was measured at a different pixel ratio than the current one
		// (zooming the page or moving the window to another monitor); then the estimate stands until the next observation.
		if (dev && Math.abs(dev.inlineSize - W) <= 1 && Math.abs(dev.blockSize - totalH) <= 1)
		{
			W = dev.inlineSize;
			totalH = dev.blockSize;
		}
		this.setDeviceSize(W, totalH, dpr);
		this._watchPixelRatio(dpr);
	}

	/** Re-measures when the device pixel ratio changes; a resize observer does not reliably report that on its own. */
	_watchPixelRatio(dpr)
	{
		if (this.watchedDpr === dpr || typeof window.matchMedia !== 'function') return;
		if (this.dprQuery) this.dprQuery.removeEventListener('change', this.onDprChange);
		this.watchedDpr = dpr;
		this.onDprChange = this.onDprChange || (() => this._onResize(null));
		this.dprQuery = window.matchMedia(`(resolution: ${dpr}dppx)`);
		this.dprQuery.addEventListener('change', this.onDprChange);
	}

	/** Sizes the canvases to exactly W x totalH device pixels (graph plus time axis). */
	setDeviceSize(W, totalH, dpr)
	{
		const axisH = Math.round(22 * dpr);
		const H = Math.max(1, totalH - axisH);
		W = Math.max(1, W);
		const dprChanged = dpr !== this.dpr;
		this.dpr = dpr;
		if (W !== this.W || H !== this.H || axisH !== this.axisH || dprChanged)
		{
			this.W = W;
			this.H = H;
			this.axisH = axisH;
			this.labelKey = null; // resizing clears the canvases and resets their contexts
			for (const ctx of [this.ctxOverlay, this.ctxHover, this.ctxAxis]) ctx.fontName = null;
			for (const c of [this.bars, this.overlay, this.hoverCanvas])
			{
				c.width = W;
				c.height = H;
				c.style.width = (W / dpr) + 'px';
				c.style.height = (H / dpr) + 'px';
				c.style.top = '0px';
			}
			this.axis.width = W;
			this.axis.height = axisH;
			this.axis.style.width = (W / dpr) + 'px';
			this.axis.style.height = (axisH / dpr) + 'px';
			this.axis.style.top = (H / dpr) + 'px';
			if (!this.backend.supportsWidth(W))
			{
				// Wider than the GPU's maximum texture size: switch to the Canvas 2D backend on a fresh canvas.
				const old = this.bars;
				this.bars = document.createElement('canvas');
				this.bars.style.cssText = old.style.cssText;
				this.bars.width = W;
				this.bars.height = H;
				old.replaceWith(this.bars);
				this.backend = new CpuBars(this.bars);
			}
			this._endZoomAnim();
			this.backend.resize(W, H);
			this.colBuf = null;
			this.invalidateAll();
		}
		this._snapToPixelGrid();
	}

	/** Offsets the canvases by a sub-pixel amount so that their top-left corner lands exactly on a device pixel. */
	_snapToPixelGrid()
	{
		const dpr = this.dpr;
		const rect = this.root.getBoundingClientRect();
		const fx = rect.left * dpr - Math.round(rect.left * dpr);
		const fy = rect.top * dpr - Math.round(rect.top * dpr);
		this.layer.style.transform = (fx || fy) ? `translate(${-fx / dpr}px, ${-fy / dpr}px)` : '';
	}

	invalidateAll()
	{
		this.needFull = true;
		this.statsDirty = true;
		this.overlayDirty = true;
		this.axisDirty = true;
	}

	// ------------------------------------------------------------------ view control

	get nowCol() { return Math.floor(this.opts.serverNow() / this.msPerCol); }

	/** The rightmost visible column. */
	currentRightCol() { return this.live ? this.nowCol : this.rightCol; }

	visibleRange()
	{
		const rc = this.currentRightCol();
		const lc = rc - this.W + 1;
		return { startMs: lc * this.msPerCol, endMs: (rc + 1) * this.msPerCol, msPerCol: this.msPerCol, live: this.live };
	}

	/** 'live' while following a running session, 'behind' while monitoring with the view scrolled back, otherwise null. */
	liveState()
	{
		if (!dataStore.getSessions().some(s => s.meta.running)) return null;
		return this.live ? 'live' : 'behind';
	}

	/** Keeps axis labels out of the rightmost `cssPx` of the time axis (where the live indicator sits). */
	setAxisReserve(cssPx)
	{
		if (cssPx === this.axisReserve) return;
		this.axisReserve = cssPx;
		this.axisDirty = true;
	}

	/** Jumps to the newest data: the live edge while monitoring, otherwise the end of the displayed sessions. */
	setLive()
	{
		if (this._allStopped())
		{
			this._setRightCol(this._endCol());
			return;
		}
		if (this.live) return;
		this.live = true;
		this._viewChanged();
	}

	/** True when sessions are displayed and none of them is still monitoring (a stopped run or a saved session). */
	_allStopped()
	{
		const sessions = dataStore.getSessions();
		return sessions.length > 0 && !sessions.some(s => s.meta.running);
	}

	/** The right edge that shows the end of the data with a small margin. */
	_endCol() { return Math.floor(this._latestEnd() / this.msPerCol) + Math.floor(this.W * 0.02); }

	/** Pans by a number of device pixels (positive = towards older data). */
	panBy(dxDevice)
	{
		const cur = this.currentRightCol();
		this._setRightCol(cur - Math.round(dxDevice));
		// Panning towards the past never moves the view forward (the clamp in _setRightCol follows the clock).
		if (dxDevice > 0 && !this.live && this.rightCol > cur) this.rightCol = cur;
	}

	_setRightCol(rc)
	{
		const now = this.nowCol;
		// Without a running session there is nothing to follow: stop at the end of the data instead of going live and
		// scrolling on into empty time.
		const stopped = this._allStopped();
		if (stopped) rc = Math.min(rc, this._endCol());
		else if (rc >= now)
		{
			if (!this.live) { this.live = true; this._viewChanged(); }
			return;
		}
		// Do not scroll endlessly into the past beyond the oldest session: keep at least a quarter of the view on data,
		// or, when all the data is narrower than that, keep its end in view.
		const minMs = this._earliestStart();
		if (minMs != null)
		{
			let minRc = Math.floor(minMs / this.msPerCol) + Math.floor(this.W * 0.75);
			const endMs = this._latestEnd();
			if (endMs != null) minRc = Math.min(minRc, Math.floor(endMs / this.msPerCol) + Math.floor(this.W * 0.02));
			// A request for a time before now (dragging, Home) pauses the view even when the clamp leaves nothing to scroll.
			if (rc < minRc) rc = Math.min(minRc, now - 1);
		}
		if (rc >= now && !stopped)
		{
			this.live = true;
		}
		else
		{
			this.live = false;
			this.rightCol = rc;
		}
		this._viewChanged();
	}

	_earliestStart()
	{
		let min = null;
		for (const s of dataStore.getSessions())
			if (min == null || s.meta.startMs < min) min = s.meta.startMs;
		return min;
	}

	_latestEnd()
	{
		let max = null;
		for (const s of dataStore.getSessions())
		{
			const end = s.meta.endMs != null ? s.meta.endMs : this.opts.serverNow();
			if (max == null || end > max) max = end;
		}
		return max;
	}

	/** Moves the right edge of the view to a time. */
	goTo(endMs)
	{
		this._setRightCol(Math.floor(endMs / this.msPerCol));
	}

	/**
	 * Changes zoom level.  `anchorX` (device px) keeps the time under that x position fixed, unless the view is live, in
	 * which case it stays live.  With `animate`, the change is shown as a quick continuous zoom (see _applyZoomAnim).
	 */
	setZoom(msPerCol, anchorX = null, animate = false)
	{
		if (msPerCol === this.msPerCol) return;
		if (animate) this._beginZoomAnim();
		else this._endZoomAnim();
		const old = this.msPerCol;
		const oldRc = this.currentRightCol();
		const wasLive = this.live;
		this.msPerCol = msPerCol;
		if (wasLive)
		{
			this._viewChanged();
			return;
		}
		const ax = anchorX == null ? this.W - 1 : Math.max(0, Math.min(this.W - 1, Math.round(anchorX)));
		const anchorCol = oldRc - (this.W - 1 - ax);
		const anchorMs = (anchorCol + 0.5) * old;
		const rc = Math.floor(anchorMs / msPerCol) + (this.W - 1 - ax);
		this.live = false;
		this._setRightCol(rc);
	}

	zoomStep(direction, anchorX = null, animate = false)
	{
		const ms = this.msPerCol;
		let next;
		if (direction > 0) next = ZOOM_LEVELS.find(z => z > ms) ?? ZOOM_LEVELS[ZOOM_LEVELS.length - 1];
		else next = [...ZOOM_LEVELS].reverse().find(z => z < ms) ?? ZOOM_LEVELS[0];
		this.setZoom(next, anchorX, animate);
	}

	/** Zooms to show the whole time span of all sessions. */
	fitAll()
	{
		const start = this._earliestStart();
		const end = this._latestEnd();
		if (start == null || end == null) return;
		const span = Math.max(1000, end - start);
		// Not limited to the zoom steps: columns can be any whole number of milliseconds, so the data fills the view.
		const level = fitZoom(span / (this.W * 0.96));
		this._endZoomAnim();
		this.msPerCol = level;
		const running = dataStore.getSessions().some(s => s.meta.running);
		if (running)
		{
			this.live = true;
			this._viewChanged();
		}
		else
		{
			this.live = false;
			this._setRightCol(Math.floor(end / level) + Math.floor(this.W * 0.02));
		}
	}

	_viewChanged()
	{
		this.statsDirty = true;
		this.overlayDirty = true;
		this.axisDirty = true;
		if (this.drawnMsPerCol !== this.msPerCol) this.needFull = true;
		if (this.opts.onViewChanged) this.opts.onViewChanged();
	}

	setHover(cssX, cssY)
	{
		this.hover = cssX == null ? null : { x: cssX, y: cssY };
		this.hoverDirty = true;
	}

	// ------------------------------------------------------------------ data notifications

	_onData(ch)
	{
		if (ch.type === 'meta')
		{
			// Session bounds, route events or address lists may have changed: recompute everything (cheap).
			this.invalidateAll();
			// When monitoring starts, follow it.  (The toolbar's Start also asks for live view, but its request can arrive
			// while the previous, stopped run is still displayed.)  When monitoring stops, a live view stays on the end of
			// the data rather than scrolling on into empty time.
			const running = dataStore.getSessions().some(s => s.meta.running);
			if (running && !this.wasRunning)
			{
				this.live = true;
				this._viewChanged();
			}
			else if (this.live && this._allStopped()) this._setRightCol(this._endCol());
			this.wasRunning = running;
			return;
		}
		if (ch.msPerCol !== this.msPerCol || this.drawnRightCol == null) return;
		const lc = this.drawnRightCol - this.W + 1;
		const x0 = Math.max(0, ch.c0 - lc), x1 = Math.min(this.W, ch.c1 - lc);
		if (x1 <= x0) return;
		// Filling forward from the changed columns can change columns up to the fill lookback after them.
		this._markX(x0, Math.min(this.W, x1 + this._fillLookback() + 1));
		// Freshly loaded chunks update the statistics at once; live pings are folded in at a limited rate.
		if (ch.bulk) this.statsDirty = true;
		else this.statsStale = true;
	}

	_markX(x0, x1)
	{
		if (x0 < this.dirtyX0) this.dirtyX0 = x0;
		if (x1 > this.dirtyX1) this.dirtyX1 = x1;
	}

	_fillCols()
	{
		let interval = 1000;
		for (const s of dataStore.getSessions()) interval = s.meta.intervalMs || interval;
		// Each ping's bar extends until the next ping is due.  Pings are not sent exactly on schedule (timer resolution), so
		// allow 10% slack; otherwise a ping a millisecond late leaves a one-column gap between bars.  Columns wider than
		// the interval always contain a ping and need no filling.
		const per = interval / this.msPerCol;
		return per < 1 ? 0 : Math.round(per) - 1 + Math.ceil(per * 0.1);
	}

	// ------------------------------------------------------------------ frame

	/** Draws whatever is needed.  Call once per animation frame. */
	frame()
	{
		const t0 = performance.now();
		this._frame();
		const dt = performance.now() - t0;
		const p = this.perf;
		p.frames++;
		p.totalMs += dt;
		if (dt > p.maxMs) p.maxMs = dt;
	}

	/** Frame timing statistics, for diagnostics (window.__pingtracer.renderer.perf). */
	resetPerf() { this.perf = { frames: 0, totalMs: 0, maxMs: 0, fullRedraws: 0, paints: 0, columns: 0 }; }

	_frame()
	{
		if (this.W <= 1 || this.H <= 1) return;
		const now = this.opts.serverNow();
		const cfg = sanitizeConfig(this.opts.getConfig());
		const configKey = [cfg.badThreshold, cfg.worseThreshold, cfg.upperLimit, cfg.lowerLimit, cfg.scalingMethodID,
			cfg.drawServerNames, cfg.monitorUnresponsiveHops, cfg.drawLastPing, cfg.drawAverage, cfg.drawJitter, cfg.drawMinMax, cfg.drawPacketLoss, cfg.drawLimitText].join(',');
		if (configKey !== this.configKey)
		{
			this.configKey = configKey;
			this.invalidateAll();
		}
		if (dataStore.metaVersion !== this.metaVersionSeen)
		{
			this.metaVersionSeen = dataStore.metaVersion;
			this.statsDirty = true;
			this.overlayDirty = true;
			this.axisDirty = true;
		}

		const rc = this.live ? Math.floor(now / this.msPerCol) : this.rightCol;
		const lc = rc - this.W + 1;
		const sessions = dataStore.getSessions();
		let loaded = true;
		for (const s of sessions)
			if (!dataStore.ensure(s.id, this.msPerCol, lc, rc, now)) loaded = false;
		this.viewLoaded = loaded;

		const t = performance.now();
		if (rc !== this.drawnRightCol)
		{
			// Following the clock only needs statistics refreshed now and then; any other move needs them at once.
			if (this.live) this.statsStale = true;
			else this.statsDirty = true;
		}
		let layoutChanged = false;
		if (this.statsDirty || (this.statsStale && t - this.lastStatsTime >= STATS_INTERVAL_MS))
		{
			layoutChanged = this._computeRows(sessions, lc, rc, cfg);
			this.statsDirty = false;
			this.statsStale = false;
			this.lastStatsTime = t;
			this.overlayStatsStale = true;
		}
		if (layoutChanged) this.needFull = true;

		const W = this.W, backend = this.backend, rows = this.rows;
		if (!this.colBuf || this.colBufRows < rows.length)
		{
			this.colBufRows = Math.max(16, Math.ceil((rows.length + 1) / 16) * 16);
			this.colBuf = new Uint16Array(this.colBufRows * W * 4);
			this.needFull = true;
		}
		if (backend.ensureTexture(W, this.colBufRows)) this.needFull = true;

		const scrolled = this.drawnRightCol != null && rc !== this.drawnRightCol;
		if (!this.needFull && scrolled)
		{
			const d = rc - this.drawnRightCol;
			if (Math.abs(d) >= W) this.needFull = true;
			else
			{
				// The column ring keeps every column still in view; only the exposed ones need computing.  The Canvas 2D
				// backend moves its pixels along; WebGL simply repaints with the new ring offset.
				if (!backend.isGl) backend.scroll(d);
				if (this.dirtyX0 !== Infinity) { this.dirtyX0 -= d; this.dirtyX1 -= d; }
				if (d > 0) this._markX(W - d, W);
				else this._markX(0, -d);
			}
		}

		const renderCtx = {
			now, cfg, lc, rc, fillCols: this._fillCols(), maxBack: this._fillLookback(),
			bad: cfg.badThreshold ?? 100, worse: cfg.worseThreshold ?? 200, minCurtain: Math.max(1, Math.round(this.dpr)),
		};
		const pendKey = this._updateRowFrameParams(now, lc);
		const pendChanged = pendKey !== this.drawnPendKey;
		if (!backend.isGl && pendChanged && this.live)
		{
			// Pending pings near the live edge change appearance as time passes; the shader handles this by itself.
			let timeout = 4000, interval = 1000;
			for (const s of sessions) { timeout = s.meta.timeoutMs || timeout; interval = s.meta.intervalMs || interval; }
			const liveCols = Math.ceil((timeout + interval * 2) / this.msPerCol) + 2;
			this._markX(Math.max(0, W - liveCols), W);
		}

		const fp = this._frameParams(lc);
		const full = this.needFull;
		const x0 = full ? 0 : Math.max(0, this.dirtyX0), x1 = full ? W : Math.min(W, this.dirtyX1);
		const perf = this.perf;
		// Rows whose vertical scale changed need all their columns recomputed (positions are precomputed integers).
		const rescaled = full || this.dirtyRows.size === 0 ? [] : rows.filter(r => this.dirtyRows.has(r.key));
		if (x1 > x0)
		{
			this._computeColumns(renderCtx, x0, x1, null);
			perf.columns += x1 - x0;
		}
		for (const row of rescaled)
		{
			this._computeColumns(renderCtx, 0, W, row);
			perf.columns += W;
		}
		if (backend.isGl)
		{
			if (x1 > x0)
			{
				const tx0 = mod(lc + x0, W), n = x1 - x0;
				if (tx0 + n <= W) backend.upload(this.colBuf, W, 0, rows.length, tx0, tx0 + n);
				else
				{
					backend.upload(this.colBuf, W, 0, rows.length, tx0, W);
					backend.upload(this.colBuf, W, 0, rows.length, 0, tx0 + n - W);
				}
			}
			for (const row of rescaled) backend.upload(this.colBuf, W, row.texRow, 1, 0, W);
			if (full || x1 > x0 || scrolled || pendChanged || rescaled.length > 0)
			{
				backend.draw(fp, rows);
				perf.paints++;
				if (full) perf.fullRedraws++;
			}
		}
		else if (full)
		{
			backend.render(fp, this.colBuf, 0, W, rows, 0, this.H);
			perf.paints++;
			perf.fullRedraws++;
		}
		else
		{
			for (const row of rescaled)
			{
				backend.render(fp, this.colBuf, 0, W, [row], row.y0, row.y0 + row.h);
				perf.paints++;
			}
			if (x1 > x0)
			{
				backend.render(fp, this.colBuf, x0, x1, rows, 0, this.H);
				perf.paints++;
			}
		}
		this.needFull = false;
		this.dirtyRows.clear();
		this.dirtyX0 = Infinity;
		this.dirtyX1 = -Infinity;
		this.drawnPendKey = pendKey;
		this.drawnRightCol = rc;
		this.drawnMsPerCol = this.msPerCol;

		if (this.overlayDirty || (this.overlayStatsStale && t - this.lastOverlayDraw > 250))
		{
			this._drawOverlay(renderCtx);
			// The value under the pointer depends on the row scales, which may have changed too.
			this.hoverDirty = this.hoverDirty || this.overlayDirty;
			this.overlayDirty = false;
			this.overlayStatsStale = false;
			this.lastOverlayDraw = t;
		}
		if (this.hoverDirty)
		{
			this._drawHover();
			this.hoverDirty = false;
		}
		if (this.zoomAnim) this._applyZoomAnim(lc);
		if (!this.zoomAnim && (this.axisDirty || rc !== this.lastAxisRightCol))
		{
			this._drawAxis(lc * this.msPerCol, this.msPerCol);
			this.axisDirty = false;
			this.lastAxisRightCol = rc;
		}
	}

	// ------------------------------------------------------------------ rows & statistics

	/** Rebuilds the row list and per-row statistics for the visible columns.  Returns true if the layout changed. */
	_computeRows(sessions, lc, rc, cfg)
	{
		const ms = this.msPerCol;
		const rows = [];
		for (const s of sessions)
		{
			if (s.meta.mode === 'direct')
			{
				rows.push(this._rowFor(s, 0, lc, rc, ms));
				continue;
			}
			// Candidate TTLs: everything that has a chunk in view, plus the known route when live.
			let maxTtl = 0;
			const zoom = s.zooms.get(ms);
			if (zoom)
			{
				for (let ci = Math.floor(lc / CHUNK_COLS); ci <= Math.floor(rc / CHUNK_COLS); ci++)
				{
					const chunk = zoom.chunks.get(ci);
					if (!chunk) continue;
					for (const ttl of chunk.hops.keys()) if (ttl > maxTtl) maxTtl = ttl;
				}
			}
			const sessionRows = [];
			let lastWithData = 0;
			for (let ttl = 1; ttl <= maxTtl; ttl++)
			{
				const row = this._rowFor(s, ttl, lc, rc, ms);
				sessionRows.push(row);
				if (row.stats.n > 0) lastWithData = ttl;
			}
			let rowCount = lastWithData;
			if (rowCount === 0)
			{
				// No data in view: keep a sensible set of (empty) rows rather than none.
				rowCount = s.meta.destTtl || 0;
				for (const t of s.repliedTtls) if (t > rowCount) rowCount = t;
				for (let ttl = sessionRows.length + 1; ttl <= rowCount; ttl++)
					sessionRows.push(this._rowFor(s, ttl, lc, rc, ms));
			}
			sessionRows.length = Math.min(sessionRows.length, rowCount);

			// Decide which rows are "silent" (shown collapsed): no replies in view, and either the hop has never replied
			// or replies did reach the destination during this time (so the silence is the router ignoring us, not an outage).
			let destReplied = false;
			for (const row of sessionRows)
				if (row.stats.succ > 0 && row.dominantAddr === s.targetAddrId && s.targetAddrId !== ADDR_NONE) destReplied = true;
			for (const row of sessionRows)
			{
				const neverReplied = !s.repliedTtls.has(row.ttl);
				row.silent = row.stats.succ === 0 && (neverReplied || destReplied);
				row.collapsed = row.silent && !cfg.monitorUnresponsiveHops;
			}
			// Drop trailing silent rows (TTLs beyond the last hop that answers).  But while the destination has never been
			// reached, keep the first of them at full size: it stands for the destination not answering, which is the
			// most important thing to show (otherwise an unreachable target would show nothing but the working hops).
			let trailing = sessionRows.length;
			while (trailing > 0 && sessionRows[trailing - 1].silent) trailing--;
			if (trailing < sessionRows.length && !destReplied && !s.meta.destTtl && sessionRows[trailing].stats.n > 0)
			{
				const row = sessionRows[trailing];
				row.unreached = true;
				row.silent = false;
				row.collapsed = false;
				trailing++;
			}
			sessionRows.length = trailing;
			rows.push(...sessionRows);
		}

		// Assign heights (device pixels).
		const dpr = this.dpr;
		const H = this.H;
		const sep = 1;
		const collapsedH = Math.round(15 * dpr);
		const nCollapsed = rows.filter(r => r.collapsed).length;
		const nNormal = rows.length - nCollapsed;
		const seps = Math.max(0, rows.length - 1) * sep;
		let normalH = nNormal > 0 ? Math.floor((H - seps - nCollapsed * collapsedH) / nNormal) : 0;
		const minNormal = Math.round(18 * dpr);
		if (normalH < minNormal) normalH = minNormal;
		let extra = nNormal > 0 ? Math.max(0, H - seps - nCollapsed * collapsedH - normalH * nNormal) : 0;
		let y = 0;
		const stripH = Math.max(2, Math.round(3 * dpr));
		for (let i = 0; i < rows.length; i++)
		{
			const row = rows[i];
			let h = row.collapsed ? collapsedH : normalH;
			if (!row.collapsed && extra > 0) { h++; extra--; }
			row.y0 = y;
			row.plotH = h;
			row.h = h + (i < rows.length - 1 ? sep : 0);
			row.strip = !row.collapsed && row.session.addressCountAtHop(row.ttl) > 1 ? stripH : 0;
			y += row.h;
			this._computeScale(row, cfg);
		}

		// Detect layout and scale changes.
		const signature = rows.map(r => `${r.key}/${r.y0}/${r.h}/${r.collapsed ? 1 : 0}/${r.strip}`).join('|');
		const layoutChanged = signature !== this.rowSignature;
		if (!layoutChanged)
		{
			const prev = new Map(this.rows.map(r => [r.key, r]));
			for (const row of rows)
			{
				const p = prev.get(row.key);
				if (!p || p.lo !== row.lo || p.hi !== row.hi) this.dirtyRows.add(row.key);
			}
		}
		this.rowSignature = signature;
		this.rows = rows;
		this.emptyMessage = rows.length === 0 ? (sessions.length === 0 ? '' : 'No data in this time range') : '';
		return layoutChanged;
	}

	_rowFor(s, ttl, lc, rc, ms)
	{
		const st = newStats();
		const votes = new Map();
		// Addresses near the right edge of the view decide the label, so it describes the route "now" (in view terms).
		const recentFrom = rc - Math.max(30, Math.floor(this.W * 0.1));
		const recentVotes = new Map();
		const zoom = s.zooms.get(ms);
		if (zoom)
		{
			for (let ci = Math.floor(lc / CHUNK_COLS); ci <= Math.floor(rc / CHUNK_COLS); ci++)
			{
				const chunk = zoom.chunks.get(ci);
				if (!chunk) continue;
				const h = chunk.hops.get(ttl);
				if (!h) continue;
				const base = ci * CHUNK_COLS;
				const i0 = Math.max(0, lc - base), i1 = Math.min(CHUNK_COLS - 1, rc - base);
				if (i0 === 0 && i1 === CHUNK_COLS - 1 && base + CHUNK_COLS <= recentFrom)
					mergeStats(st, votes, chunkSummary(h), base, null); // whole chunk in view: use its cached summary
				else
					accumulateRange(h, base, i0, i1, st, votes, recentFrom, recentVotes);
			}
		}
		let dominant = ADDR_NONE, best = -1;
		for (const [a, v] of votes) if (v > best) { best = v; dominant = a; }
		const key = s.id + ':' + ttl;
		let labelAddr = dominant;
		if (recentVotes.size > 0)
		{
			// Prefer the previous label on ties so load-balanced hops do not flicker between addresses.
			const prev = this.rows.find(r => r.key === key);
			const prevLabel = prev ? prev.labelAddr : ADDR_NONE;
			let bestRecent = -1;
			for (const [a, v] of recentVotes)
				if (v > bestRecent || (v === bestRecent && a === prevLabel)) { bestRecent = v; labelAddr = a; }
		}
		return { key, session: s, ttl, stats: st, votes, dominantAddr: dominant, labelAddr, collapsed: false };
	}

	_computeScale(row, cfg)
	{
		const st = row.stats;
		const lowerLimit = cfg.lowerLimit ?? 0;
		const upperLimit = Math.max(lowerLimit + 1, cfg.upperLimit ?? 300);
		const plotCss = Math.max(1, (row.plotH - row.strip) / this.dpr);
		const hasData = st.succ > 0;
		let lo, hi;
		switch (cfg.scalingMethodID ?? SCALING_CLASSIC)
		{
			case SCALING_ZOOM:
				lo = hasData ? Math.max(lowerLimit, niceFloor(st.min * 0.9)) : lowerLimit;
				hi = hasData ? Math.min(upperLimit, niceCeil(st.max * 1.05)) : lowerLimit + plotCss;
				break;
			case SCALING_ZOOM_UNLIMITED:
				lo = hasData ? niceFloor(st.min * 0.9) : 0;
				hi = hasData ? niceCeil(st.max * 1.05) : plotCss;
				break;
			case SCALING_FIXED:
				lo = lowerLimit;
				hi = upperLimit;
				break;
			default: // classic: one millisecond per CSS pixel, expanding (up to the upper limit) to fit the data
				lo = lowerLimit;
				hi = lo + plotCss;
				if (hasData && st.max > hi) hi = Math.min(upperLimit, niceCeil(st.max * 1.1));
				hi = Math.max(hi, lo + 1);
				hi = Math.round(hi);
				break;
		}
		if (!(hi > lo)) hi = lo + 1;
		row.lo = lo;
		row.hi = hi;
		// Rows (relative to the row top) where the threshold background zones end.
		row.yWorse = this._rowY(row, cfg.worseThreshold ?? 200);
		row.yBad = this._rowY(row, cfg.badThreshold ?? 100);
	}

	/** Device row (relative to the top of the graph row) of a value on the row's vertical scale, clamped to the plot. */
	_rowY(row, v)
	{
		const plotTop = row.strip, plotBottom = row.plotH;
		const y = Math.round(plotBottom - (v - row.lo) * (plotBottom - plotTop) / (row.hi - row.lo));
		return y < plotTop ? plotTop : y > plotBottom ? plotBottom : y;
	}

	// ------------------------------------------------------------------ bars

	/** Parameters shared by all rows for painting this frame. */
	_frameParams(lc)
	{
		return {
			W: this.W,
			H: this.H,
			lcMod: mod(lc, this.W),
			dash: Math.max(2, Math.round(3 * this.dpr)),
		};
	}

	/**
	 * Assigns each row its column-buffer row and the range of view columns shaded as "awaiting reply".  Returns a key
	 * that changes whenever any shaded range changes.
	 */
	_updateRowFrameParams(now, lc)
	{
		const ms = this.msPerCol;
		let key = '';
		for (let i = 0; i < this.rows.length; i++)
		{
			const row = this.rows[i];
			row.texRow = i;
			row.pendX0 = 0x7FFFFFFF;
			row.pendX1 = -1;
			const s = row.session;
			// Shade pings awaiting a reply, but only on rows that are still being probed (not beyond the destination).
			const probed = s.meta.running && !(s.meta.mode === 'trace' && s.meta.destTtl > 0 && row.ttl > s.meta.destTtl);
			if (this.live && probed && !row.collapsed)
			{
				const pendingFrom = now - (s.meta.timeoutMs || 4000) - (s.meta.intervalMs || 1000);
				row.pendX0 = Math.max(-1, Math.ceil(pendingFrom / ms) - lc);
				row.pendX1 = Math.min(this.W, Math.floor(now / ms) - lc);
				key += row.pendX0 + ',' + row.pendX1 + ';';
			}
		}
		return key;
	}

	/**
	 * Reduces view columns [x0, x1) of every row (or only of `onlyRow`) to column-buffer records (see BarsBackend.js).
	 * The loops work chunk by chunk with small integer indexes; absolute column numbers exceed 2^31 at fine zoom levels
	 * and would force slow floating-point arithmetic in the inner loop.
	 */
	_computeColumns(rc_, x0, x1, onlyRow)
	{
		const { lc, fillCols, maxBack, bad, worse, minCurtain } = rc_;
		const W = this.W, buf = this.colBuf, ms = this.msPerCol;
		const txStart = mod(lc + x0, W);
		if (!this.markerScratch || this.markerScratch.length !== W) this.markerScratch = new Uint8Array(W);
		const mk = this.markerScratch;
		for (let r = 0; r < this.rows.length; r++)
		{
			const row = this.rows[r];
			if (row.collapsed || (onlyRow && row !== onlyRow)) continue;
			const plotTop = row.strip, plotBottom = row.plotH, plotH = plotBottom - plotTop;
			const lo = row.lo, scale = plotH / (row.hi - row.lo);
			const s = row.session, ttl = row.ttl;
			// View columns inside the session: column c is outside if it ends before the start or starts after the end.
			const xIn0 = Math.floor(s.meta.startMs / ms) - lc;
			const xIn1 = s.meta.endMs != null ? Math.floor(s.meta.endMs / ms) - lc : Infinity;
			const isStrip = row.strip > 0;
			const markers = this._markersFor(s, ttl, lc + x0, lc + x1 - 1, ms);
			if (markers !== null) for (const [c, kind] of markers) mk[c - lc] = kind;

			// Fill-forward state: the last three non-empty columns (as view x) and the values of the last one.  See _fillAllows.
			let lastX = -Infinity, p2 = -Infinity, p3 = -Infinity;
			let lN = 0, lLost = 0, lSum = 0, lMax = 0, lAddr = ADDR_NONE, lFlags = 0;
			{
				const back = this._recentPings(s, ms, ttl, lc + x0, maxBack);
				if (back.length > 0)
				{
					const d = this._col(s, ms, ttl, back[0]);
					lastX = back[0] - lc; lN = d.n[d.i]; lLost = d.lost[d.i]; lSum = d.sum[d.i]; lMax = d.max[d.i]; lAddr = d.addr[d.i]; lFlags = d.flags[d.i];
					if (back.length > 1) p2 = back[1] - lc;
					if (back.length > 2) p3 = back[2] - lc;
				}
			}

			const zoom = s.zooms.get(ms);
			const rowBase = r * W;
			let tx = txStart;
			let palAddr = -1, palBits = 0; // palette lookup cache: consecutive columns usually share an address
			let x = x0;
			while (x < x1)
			{
				const ci = Math.floor((lc + x) / CHUNK_COLS);
				let i = lc + x - ci * CHUNK_COLS;
				const iEnd = Math.min(CHUNK_COLS, i + (x1 - x));
				const chunk = zoom ? zoom.chunks.get(ci) : null;
				const hc = chunk ? chunk.hops.get(ttl) || null : null;
				const N = hc ? hc.n : null;
				const L = hc ? hc.lost : null, S = hc ? hc.sum : null, MX = hc ? hc.max : null, A = hc ? hc.addr : null, F = hc ? hc.flags : null;
				for (; i < iEnd; i++, x++)
				{
					let n = 0, lost = 0, sum = 0, max = 0, addr = ADDR_NONE, flags = 0;
					if (N !== null && N[i] > 0)
					{
						n = N[i]; lost = L[i]; sum = S[i]; max = MX[i]; addr = A[i]; flags = F[i];
						p3 = p2; p2 = lastX; lastX = x;
						lN = n; lLost = lost; lSum = sum; lMax = max; lAddr = addr; lFlags = flags;
					}
					else if (x - lastX <= maxBack && _fillAllows(x, lastX, p2, p3, fillCols, maxBack))
					{
						n = lN; lost = lLost; sum = lSum; max = lMax; addr = lAddr; flags = lFlags;
					}

					const o = (rowBase + tx) * 4;
					if (++tx === W) tx = 0;
					let st, bTop = plotBottom, rTop = plotBottom, cBottom = plotTop;
					if (n === 0)
						st = x < xIn0 || x > xIn1 ? KIND_OUTSIDE : KIND_EMPTY;
					else
					{
						const succ = n - lost;
						if (succ === 0) st = KIND_LOST;
						else
						{
							const avg = sum / succ;
							let barTop = Math.round(plotBottom - (avg - lo) * scale);
							barTop = barTop < plotTop ? plotTop : barTop > plotBottom ? plotBottom : barTop;
							if (barTop === plotBottom && avg > lo) barTop = plotBottom - 1; // always show at least one pixel
							let rangeTop = Math.round(plotBottom - (max - lo) * scale);
							rangeTop = rangeTop < plotTop ? plotTop : rangeTop > barTop ? barTop : rangeTop;
							bTop = barTop;
							rTop = rangeTop;
							// Partial loss hangs from the top of the column like a curtain whose height is the loss fraction,
							// so it can be read quantitatively and light loss spread over many pings stays unobtrusive.
							if (lost > 0) cBottom = plotTop + Math.min(plotH, Math.max(minCurtain, Math.round(lost / n * plotH)));
							st = KIND_DATA | ((avg >= worse ? 2 : avg >= bad ? 1 : 0) << STATE_LEVEL_SHIFT);
						}
						if (isStrip && addr !== ADDR_NONE)
						{
							if (addr !== palAddr) { palAddr = addr; palBits = ((s.addressIndexAtHop(ttl, addr) % ADDRESS_PALETTE.length) + 1) << STATE_STRIP_SHIFT; }
							st |= palBits;
							// Columns where several addresses answered are dithered by absolute column parity, so the pattern
							// scrolls along with the data.  CHUNK_COLS is even, so the parity of i is the parity of the column.
							if ((flags & FLAG_MULTI_ADDR) && (i & 1)) st |= STATE_DIM;
						}
					}
					const m = mk[x];
					if (m !== 0) { st |= m << STATE_MARKER_SHIFT; mk[x] = 0; }
					buf[o] = bTop;
					buf[o + 1] = rTop;
					buf[o + 2] = cBottom;
					buf[o + 3] = st;
				}
			}
		}
	}

	/** Returns a Map(column -> marker kind: 1 = hop address change, 2 = destination change) for a row, or null if none. */
	_markersFor(s, ttl, c0, c1, ms)
	{
		const ev = s.routeEvents;
		if (ev.length === 0) return null;
		const t0 = c0 * ms, t1 = (c1 + 1) * ms;
		// Binary search for the first event >= t0.
		let a = 0, b = ev.length;
		while (a < b)
		{
			const m = (a + b) >> 1;
			if (ev[m].t < t0) a = m + 1; else b = m;
		}
		let map = null;
		for (let i = a; i < ev.length && ev[i].t < t1; i++)
		{
			const e = ev[i];
			if (e.initial) continue;
			let kind;
			if (e.kind === 'addr' && e.ttl === ttl) kind = 1;
			else if (e.kind === 'dest' && ttl !== 0) kind = 2;
			else continue;
			if (!map) map = new Map();
			const col = Math.floor(e.t / ms);
			if (!map.has(col) || kind === 1) map.set(col, kind);
		}
		return map;
	}

	/** Random access to one column of one hop (slow path, used for look-behind and hit testing). */
	/**
	 * How far back (in columns) fill-forward looks for earlier pings.  Fill decisions depend only on the pings within
	 * this distance, so an incremental update gives the same result as painting from scratch.
	 */
	_fillLookback()
	{
		return Math.min(4096, Math.max(3 * (this._fillCols() + 1), Math.ceil(30000 / this.msPerCol)));
	}

	/** Columns (absolute, newest first) of up to the three most recent non-empty columns in [c - maxBack, c). */
	_recentPings(s, ms, ttl, c, maxBack)
	{
		const out = [];
		const zoom = s.zooms.get(ms);
		if (!zoom) return out;
		const stop = c - maxBack;
		let col = c - 1;
		while (col >= stop && out.length < 3)
		{
			const ci = Math.floor(col / CHUNK_COLS);
			const base = ci * CHUNK_COLS;
			const chunk = zoom.chunks.get(ci);
			const h = chunk ? chunk.hops.get(ttl) : null;
			if (h)
			{
				const N = h.n, iStop = Math.max(stop - base, 0);
				for (let i = col - base; i >= iStop && out.length < 3; i--)
					if (N[i] > 0) out.push(base + i);
			}
			col = base - 1;
		}
		return out;
	}

	_col(s, ms, ttl, c)
	{
		const zoom = s.zooms.get(ms);
		if (!zoom) return null;
		const ci = Math.floor(c / CHUNK_COLS);
		const chunk = zoom.chunks.get(ci);
		if (!chunk) return null;
		const h = chunk.hops.get(ttl);
		if (!h) return null;
		const i = c - ci * CHUNK_COLS;
		return { n: h.n, lost: h.lost, sum: h.sum, min: h.min, max: h.max, addr: h.addr, flags: h.flags, i };
	}

	// ------------------------------------------------------------------ overlay

	_rowName(row, cfg)
	{
		const s = row.session;
		if (row.ttl === 0) return s.meta.displayName;
		if (row.unreached) return `* no reply: ${s.meta.target || 'destination'} not reached`;
		const a = row.labelAddr !== ADDR_NONE ? s.addresses.get(row.labelAddr) : null;
		if (!a) return row.collapsed ? '* no reply' : (row.stats.n > 0 ? '* no reply' : '');
		const showNames = cfg.drawServerNames ?? true;
		return showNames && a.host ? `${a.host} [${a.ip}]` : a.ip;
	}

	/**
	 * Draws the row labels and statistics.  Text drawing is the most expensive part of a frame, so the labels are first
	 * built as plain data and only redrawn when that data (or the canvas size) changed: panning or new data usually
	 * leaves the label text as it was.
	 */
	_drawOverlay(rc_)
	{
		const { cfg } = rc_;
		const dpr = this.dpr;
		const labels = [];
		for (const row of this.rows)
		{
			if (row.collapsed)
			{
				labels.push({ collapsed: true, y0: row.y0, plotH: row.plotH, text: `${row.ttl}  ${this._rowName(row, cfg) || '* no reply'}` });
				continue;
			}
			const st = row.stats;
			const parts = [];
			if (cfg.drawPacketLoss ?? true)
				parts.push(st.n > 0 ? `${formatPct(st.lost / st.n * 100)} loss` : '');
			const ints = [];
			if ((cfg.drawLastPing ?? true) && st.last != null) ints.push(`last ${Math.round(st.last)}`);
			if ((cfg.drawAverage ?? true) && st.succ > 0) ints.push(`avg ${Math.round(st.sum / st.succ)}`);
			if ((cfg.drawJitter ?? false) && st.jitterN > 0) ints.push(`jitter ${Math.round(st.jitterSum / st.jitterN)}`);
			if ((cfg.drawMinMax ?? false) && st.succ > 0) ints.push(`min ${st.min}`, `max ${st.max}`);
			if (ints.length) parts.push(ints.join('  '));
			const hasSwatch = row.strip && row.labelAddr !== ADDR_NONE;
			labels.push({
				y0: row.y0, plotH: row.plotH, strip: row.strip,
				prefix: row.ttl === 0 ? '' : `${row.ttl}  `,
				name: this._rowName(row, cfg),
				others: row.votes.size - 1,
				stats: parts.filter(p => p).join('   '),
				dim: st.n === 0,
				lossy: st.n > 0 && st.lost > 0,
				swatch: hasSwatch ? ADDRESS_PALETTE_CSS[s_addrIndex(row, row.labelAddr) % ADDRESS_PALETTE_CSS.length] : null,
				limits: cfg.drawLimitText ? [String(Math.round(row.hi)), String(Math.round(row.lo))] : null,
			});
		}
		const key = JSON.stringify([this.W, this.H, dpr, this.emptyMessage, labels]);
		if (key === this.labelKey) return;
		this.labelKey = key;
		perfCount(this, 'labelDraws');

		const ctx = this.ctxOverlay;
		ctx.setTransform(1, 0, 0, 1, 0, 0);
		ctx.clearRect(0, 0, this.W, this.H);
		ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
		ctx.textBaseline = 'top';
		const wCss = this.W / dpr;

		for (const l of labels)
		{
			const yCss = l.y0 / dpr;
			if (l.collapsed)
			{
				setFont(ctx, FONT_SMALL);
				ctx.fillStyle = '#6c6c78';
				ctx.fillText(l.text, 5, yCss + Math.max(0, (l.plotH / dpr - 11) / 2));
				continue;
			}
			setFont(ctx, FONT_MAIN);
			const othersText = l.others > 0 ? `  +${l.others}` : '';
			const statsW = l.stats ? textWidth(ctx, l.stats) : 0;
			const othersW = othersText ? textWidth(ctx, othersText) : 0;
			const swatchW = l.swatch ? 12 : 0;
			// In a narrow window, shorten the name rather than push the statistics out of view.
			const nameRoom = wCss - 14 - swatchW - othersW - (l.stats ? statsW + 16 : 0) - textWidth(ctx, l.prefix);
			const head = l.prefix + fitText(ctx, l.name, Math.max(nameRoom, 40));
			const headW = textWidth(ctx, head);
			const y = yCss + l.strip / dpr + 2;
			ctx.fillStyle = 'rgba(0,0,0,0.62)';
			ctx.fillRect(1, y - 1, headW + othersW + (l.stats ? statsW + 16 : 0) + 8 + swatchW, 16);
			let x = 5;
			if (l.swatch)
			{
				// Swatch matching the address strip color.
				ctx.fillStyle = l.swatch;
				ctx.fillRect(x, y + 3, 8, 8);
				x += 12;
			}
			ctx.fillStyle = l.dim ? '#9a9aa6' : '#f0f0f0';
			ctx.fillText(head, x, y);
			x += headW;
			if (othersText)
			{
				ctx.fillStyle = '#8fd8ff';
				ctx.fillText(othersText, x, y);
				x += othersW;
			}
			if (l.stats)
			{
				ctx.fillStyle = l.lossy ? '#ff9a9a' : '#b8c4b8';
				ctx.fillText(l.stats, x + 16, y);
			}
			if (l.limits)
			{
				const [hiT, loT] = l.limits;
				setFont(ctx, FONT_SMALL);
				ctx.fillStyle = '#c0c0c0';
				ctx.fillText(hiT, wCss - textWidth(ctx, hiT) - 4, y);
				ctx.textBaseline = 'bottom';
				ctx.fillText(loT, wCss - textWidth(ctx, loT) - 4, (l.y0 + l.plotH) / dpr - 1);
				ctx.textBaseline = 'top';
			}
		}

		if (this.emptyMessage)
		{
			setFont(ctx, '14px "Segoe UI", system-ui, sans-serif');
			ctx.fillStyle = '#777';
			const tw = textWidth(ctx, this.emptyMessage);
			ctx.fillText(this.emptyMessage, (wCss - tw) / 2, this.H / dpr / 2 - 8);
		}
	}

	/** Draws the hover crosshair and the value under the pointer on its own canvas, so pointer moves do not redraw text. */
	_drawHover()
	{
		const ctx = this.ctxHover;
		const dpr = this.dpr;
		ctx.setTransform(1, 0, 0, 1, 0, 0);
		ctx.clearRect(0, 0, this.W, this.H);
		if (!this.hover) return;
		ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
		const xDev = Math.floor(this.hover.x * dpr);
		const xCss = (xDev + 0.5) / dpr;
		ctx.strokeStyle = 'rgba(255,255,255,0.45)';
		ctx.lineWidth = 1 / dpr;
		ctx.beginPath();
		ctx.moveTo(xCss, 0);
		ctx.lineTo(xCss, this.H / dpr);
		ctx.stroke();
		const row = this.rowAt(this.hover.y * dpr);
		if (!row || row.collapsed) return;
		const yDev = Math.floor(this.hover.y * dpr);
		const plotTop = row.y0 + row.strip, plotBottom = row.y0 + row.plotH;
		if (yDev < plotTop || yDev >= plotBottom) return;
		const yCss = (yDev + 0.5) / dpr;
		ctx.beginPath();
		ctx.moveTo(0, yCss);
		ctx.lineTo(this.W / dpr, yCss);
		ctx.stroke();
		const msVal = row.lo + (plotBottom - yDev) / (plotBottom - plotTop) * (row.hi - row.lo);
		const t = `${Math.round(msVal)} ms`;
		setFont(ctx, FONT_SMALL);
		ctx.textBaseline = 'top';
		const tw = textWidth(ctx, t);
		const lx = this.hover.x + 8 + tw > this.W / dpr ? this.hover.x - tw - 8 : this.hover.x + 8;
		ctx.fillStyle = 'rgba(0,0,0,0.7)';
		ctx.fillRect(lx - 2, yCss - 14, tw + 4, 13);
		ctx.fillStyle = '#fff';
		ctx.fillText(t, lx, yCss - 13);
	}

	// ------------------------------------------------------------------ zoom animation

	/*
	 * Columns exist only at whole zoom levels (the server aggregates them), so a zoom step cannot be drawn at in-between
	 * levels.  Instead the renderer switches to the new level at once and, for ZOOM_ANIM_MS, stretches two images
	 * horizontally on the GPU (CSS transforms): the new view, and a snapshot of the old one.  Both are mapped to an
	 * in-between view that zooms smoothly about the point that stays fixed.  The finer of the two images is shown on top:
	 * it is the one that is shrunk, so the coarser one, being stretched, fills the edges around it.  Until the new level's
	 * data has loaded, the snapshot stays on top.  The time axis is redrawn for the in-between view every frame.
	 */

	/** Starts (or, during an animation, retargets) a zoom animation from what is on screen now. */
	_beginZoomAnim()
	{
		const a = this.zoomAnim;
		if (a)
		{
			// Another step while animating: continue from the view on screen; the snapshot of the first view stays.
			a.from = a.shown;
			a.start = performance.now();
			a.fadeFrom = null;
			return;
		}
		const snapView = this._captureSnapshot();
		if (!snapView) return;
		this.zoomAnim = { from: snapView, shown: snapView, snapView, start: performance.now(), snapOnTop: null, fadeFrom: null };
	}

	/** Copies the bars as last drawn into the snapshot canvas.  Returns the view they show, or null. */
	_captureSnapshot()
	{
		if (this.drawnRightCol == null || this.drawnMsPerCol == null || this.backend.lost) return null;
		let c = this.snapCanvas;
		if (!c)
		{
			c = this.snapCanvas = document.createElement('canvas');
			c.style.cssText = this.bars.style.cssText;
			c.style.display = 'none';
			this.snapCtx = c.getContext('2d', { alpha: false });
			this.bars.before(c);
		}
		if (c.width !== this.W || c.height !== this.H)
		{
			c.width = this.W;
			c.height = this.H;
		}
		c.style.width = this.bars.style.width;
		c.style.height = this.bars.style.height;
		const lc = this.drawnRightCol - this.W + 1;
		// A WebGL canvas keeps its image only until it is shown; repaint it from the column texture (cheap) to copy it.
		if (this.backend.isGl) this.backend.draw(this._frameParams(lc), this.rows);
		this.snapCtx.drawImage(this.bars, 0, 0);
		return { t0: lc * this.drawnMsPerCol, ms: this.drawnMsPerCol };
	}

	_endZoomAnim()
	{
		if (!this.zoomAnim) return;
		this.zoomAnim = null;
		this.bars.style.transform = '';
		this.snapCanvas.style.display = 'none';
		this.snapCanvas.style.opacity = '';
		this.axisDirty = true;
	}

	/** Places the two images for this frame of the zoom animation, or ends it. */
	_applyZoomAnim(lc)
	{
		const a = this.zoomAnim;
		const now = performance.now();
		const p = Math.min(1, (now - a.start) / this.zoomAnimMs);
		if (p >= 1 && (this.viewLoaded || now - a.start > this.zoomAnimMs + ZOOM_SNAPSHOT_HOLD_MS))
		{
			this._endZoomAnim();
			return;
		}
		const to = { t0: lc * this.msPerCol, ms: this.msPerCol };
		const from = a.from;
		const e = 1 - Math.pow(1 - p, 3); // ease out: fast at first, so the response to the wheel feels immediate
		// Zoom geometrically about the screen position that shows the same time in both views.  (It is the anchor of the
		// zoom, unless the new view was clamped at the end of the data.)
		const ms = from.ms * Math.pow(to.ms / from.ms, e);
		let t0;
		const dms = from.ms - to.ms;
		if (Math.abs(dms) > to.ms * 1e-9) t0 = from.t0 + (to.t0 - from.t0) / dms * (from.ms - ms);
		else t0 = from.t0 + (to.t0 - from.t0) * e;
		a.shown = { t0, ms };

		const place = (canvas, view) =>
		{
			const k = view.ms / ms, tx = (view.t0 - t0) / ms / this.dpr;
			canvas.style.transform = `translateX(${tx}px) scaleX(${k})`;
		};
		const snap = this.snapCanvas;
		place(this.bars, to);
		place(snap, a.snapView);
		snap.style.display = '';
		const snapOnTop = !this.viewLoaded || a.snapView.ms < to.ms;
		if (snapOnTop !== a.snapOnTop)
		{
			if (snapOnTop) this.bars.after(snap);
			else this.bars.before(snap);
			a.snapOnTop = snapOnTop;
		}
		// A shrunken snapshot on top of loaded data fades out over the last part of the animation instead of vanishing.
		let opacity = 1;
		if (snapOnTop && this.viewLoaded)
		{
			if (a.fadeFrom == null) a.fadeFrom = Math.max(p, 0.6);
			if (p > a.fadeFrom) opacity = 1 - (p - a.fadeFrom) / (1 - a.fadeFrom);
		}
		snap.style.opacity = opacity < 1 ? String(opacity) : '';
		this._drawAxis(t0, ms);
	}

	// ------------------------------------------------------------------ time axis

	/** Draws the time axis for a view whose left edge is at `startMs` and whose columns are `ms` wide (both may be fractional). */
	_drawAxis(startMs, ms)
	{
		const ctx = this.ctxAxis;
		const dpr = this.dpr;
		const W = this.W, H = this.axisH;
		ctx.setTransform(1, 0, 0, 1, 0, 0);
		ctx.fillStyle = '#0b0b10';
		ctx.fillRect(0, 0, W, H);
		ctx.fillStyle = '#3a3a48';
		ctx.fillRect(0, 0, W, 1);

		const pxPerMs = 1 / ms;
		const minSpacingDev = 110 * dpr;
		let interval = AXIS_INTERVALS[AXIS_INTERVALS.length - 1];
		for (const iv of AXIS_INTERVALS) if (iv * pxPerMs >= minSpacingDev) { interval = iv; break; }

		const endMs = startMs + W * ms;
		const tz = -new Date(startMs).getTimezoneOffset() * 60000;
		let t = Math.ceil((startMs + tz) / interval) * interval - tz;
		setFont(ctx, `${Math.round(11 * dpr)}px "Segoe UI", system-ui, sans-serif`);
		ctx.textBaseline = 'middle';
		for (; t <= endMs; t += interval)
		{
			const x = Math.floor((t - startMs) / ms);
			ctx.fillStyle = '#5a5a6a';
			ctx.fillRect(x, 1, 1, Math.round(5 * dpr));
			const { label, isMidnight } = this._axisLabel(t, interval);
			const lx = x + Math.round(3 * dpr);
			if (lx + textWidth(ctx, label) > W - this.axisReserve * dpr) continue; // would be cut off at the right edge
			ctx.fillStyle = isMidnight ? '#e6e6a0' : '#b4b4c0';
			ctx.fillText(label, lx, H / 2 + 1);
		}

		// Route change ticks.
		for (const s of dataStore.getSessions())
		{
			for (const e of s.routeEvents)
			{
				if (e.initial || e.t < startMs || e.t >= endMs) continue;
				const x = Math.floor((e.t - startMs) / ms);
				ctx.fillStyle = e.kind === 'dest' ? cssOf(C.destMarker) : cssOf(C.marker);
				const tri = Math.round(4 * dpr);
				ctx.beginPath();
				ctx.moveTo(x - tri, 1);
				ctx.lineTo(x + tri + 1, 1);
				ctx.lineTo(x + 0.5, 1 + tri * 1.5);
				ctx.closePath();
				ctx.fill();
			}
		}
	}

	/** Formats an axis tick label.  Date formatting is slow, so labels are cached (the axis redraws as time passes). */
	_axisLabel(t, interval)
	{
		const key = t + ':' + (interval >= 86400000 ? 'd' : interval >= 60000 ? 'm' : 's');
		let v = this.axisLabels.get(key);
		if (v) return v;
		const d = new Date(t);
		const isMidnight = d.getHours() === 0 && d.getMinutes() === 0 && d.getSeconds() === 0;
		let label;
		if (interval >= 86400000) label = AXIS_FMT.day.format(d);
		else if (isMidnight) label = AXIS_FMT.weekday.format(d);
		else if (interval >= 60000) label = AXIS_FMT.minute.format(d);
		else label = AXIS_FMT.second.format(d);
		v = { label, isMidnight };
		if (this.axisLabels.size > 2000) this.axisLabels.clear();
		this.axisLabels.set(key, v);
		return v;
	}

	// ------------------------------------------------------------------ hit testing

	rowAt(yDev)
	{
		for (const row of this.rows)
			if (yDev >= row.y0 && yDev < row.y0 + row.h) return row;
		return null;
	}

	/** Describes what is under a CSS-pixel position, for the tooltip. */
	hitTest(cssX, cssY)
	{
		const xDev = Math.floor(cssX * this.dpr), yDev = Math.floor(cssY * this.dpr);
		if (xDev < 0 || xDev >= this.W || yDev < 0 || yDev >= this.H) return null;
		const row = this.rowAt(yDev);
		if (!row) return null;
		const rc = this.drawnRightCol != null ? this.drawnRightCol : this.currentRightCol();
		const lc = rc - this.W + 1;
		const ms = this.msPerCol;
		let c = lc + xDev;
		const s = row.session;
		let d = this._col(s, ms, row.ttl, c);
		let filled = false;
		if (!d || d.n[d.i] === 0)
		{
			// The same fill-forward rule as the bars (see _fillAllows).
			const maxBack = this._fillLookback();
			const back = this._recentPings(s, ms, row.ttl, c, maxBack);
			if (back.length > 0 && _fillAllows(c, back[0], back[1] ?? -Infinity, back[2] ?? -Infinity, this._fillCols(), maxBack))
			{
				c = back[0];
				d = this._col(s, ms, row.ttl, c);
				filled = true;
			}
		}
		const info = {
			row,
			ttl: row.ttl,
			sessionName: s.meta.displayName,
			startMs: c * ms,
			endMs: (c + 1) * ms,
			msPerCol: ms,
			filled,
			n: 0, lost: 0, min: null, max: null, avg: null, address: null, host: null, multi: false,
			isDestination: false,
			// The gray area at the live edge: pings sent here have not been answered (or timed out) yet.
			pending: !(d && d.n[d.i] > 0) && xDev >= row.pendX0 && xDev <= row.pendX1,
		};
		if (d && d.n[d.i] > 0)
		{
			const i = d.i;
			info.n = d.n[i];
			info.lost = d.lost[i];
			const succ = info.n - info.lost;
			if (succ > 0)
			{
				info.min = d.min[i];
				info.max = d.max[i];
				info.avg = d.sum[i] / succ;
			}
			const a = d.addr[i];
			if (a !== ADDR_NONE)
			{
				const ad = s.addresses.get(a);
				info.address = ad ? ad.ip : '?';
				info.host = ad ? ad.host : '';
				info.isDestination = a === s.targetAddrId;
			}
			info.multi = (d.flags[i] & FLAG_MULTI_ADDR) !== 0;
		}
		// Route events in this column.
		info.events = s.routeEvents.filter(e => !e.initial && e.t >= info.startMs && e.t < info.endMs && (e.kind === 'dest' || e.ttl === row.ttl))
			.map(e => e.kind === 'dest'
				? `Destination moved to hop ${e.ttl}` + (e.value ? ` (was ${e.value})` : '')
				: `Now answering from ${(s.addresses.get(e.value) || {}).ip || '?'}`);
		return info;
	}
}

function newStats()
{
	return { n: 0, lost: 0, succ: 0, sum: 0, min: Infinity, max: -Infinity, last: null, lastCol: -Infinity, jitterSum: 0, jitterN: 0, firstAvg: null, prevAvg: null };
}

/** Adds columns [i0, i1] of a hop chunk (whose first column is `base`) to running row statistics. */
function accumulateColumns(h, base, i0, i1, st, votes, recentFrom, recentVotes)
{
	const N = h.n, L = h.lost, S = h.sum, MIN = h.min, MAX = h.max, A = h.addr;
	for (let i = i0; i <= i1; i++)
	{
		const n = N[i];
		if (n === 0) continue;
		const lost = L[i], succ = n - lost;
		st.n += n;
		st.lost += lost;
		if (succ === 0) continue;
		st.succ += succ;
		st.sum += S[i];
		if (MIN[i] < st.min) st.min = MIN[i];
		if (MAX[i] > st.max) st.max = MAX[i];
		const avg = S[i] / succ;
		if (base + i > st.lastCol) { st.lastCol = base + i; st.last = succ === 1 ? S[i] : avg; }
		if (st.prevAvg !== null) { st.jitterSum += Math.abs(avg - st.prevAvg); st.jitterN++; }
		else if (st.firstAvg === null) st.firstAvg = avg;
		st.prevAvg = avg;
		const a = A[i];
		if (a !== ADDR_NONE)
		{
			votes.set(a, (votes.get(a) || 0) + succ);
			if (recentVotes !== null && base + i >= recentFrom) recentVotes.set(a, (recentVotes.get(a) || 0) + succ);
		}
	}
}

/** Statistics of a whole hop chunk, cached until the chunk changes. lastCol is relative to the chunk. */
function chunkSummary(h)
{
	if (h.summary && h.summaryVersion === h.version) return h.summary;
	const st = newStats();
	const votes = new Map();
	accumulateColumns(h, 0, 0, CHUNK_COLS - 1, st, votes, 0, null);
	st.votes = votes;
	h.summary = st;
	h.summaryVersion = h.version;
	return st;
}

/** Columns per block summary within a chunk (see accumulateRange). */
const BLOCK_COLS = 16;

/** Statistics of one block of BLOCK_COLS columns of a hop chunk, cached until the chunk changes. lastCol is relative to the chunk. */
function blockSummary(h, b)
{
	if (h.blockVersion !== h.version)
	{
		h.blocks = new Array(CHUNK_COLS / BLOCK_COLS);
		h.blockVersion = h.version;
	}
	let sm = h.blocks[b];
	if (sm === undefined)
	{
		sm = newStats();
		const votes = new Map();
		accumulateColumns(h, 0, b * BLOCK_COLS, b * BLOCK_COLS + BLOCK_COLS - 1, sm, votes, 0, null);
		sm.votes = votes;
		h.blocks[b] = sm;
	}
	return sm;
}

/**
 * Adds columns [i0, i1] of a hop chunk to running row statistics, using cached block summaries for whole blocks (that
 * do not straddle `recentFrom`) and single columns elsewhere.  The result equals accumulateColumns over the same range.
 */
function accumulateRange(h, base, i0, i1, st, votes, recentFrom, recentVotes)
{
	let i = i0;
	while (i <= i1)
	{
		const b = Math.floor(i / BLOCK_COLS), bStart = b * BLOCK_COLS, bEnd = bStart + BLOCK_COLS - 1;
		if (i === bStart && bEnd <= i1)
		{
			const allRecent = base + bStart >= recentFrom, noneRecent = base + bEnd < recentFrom;
			if (allRecent || noneRecent)
			{
				mergeStats(st, votes, blockSummary(h, b), base, allRecent ? recentVotes : null);
				i = bEnd + 1;
				continue;
			}
		}
		const end = Math.min(i1, bEnd);
		accumulateColumns(h, base, i, end, st, votes, recentFrom, recentVotes);
		i = end + 1;
	}
}

/**
 * Adds a chunk or block summary to running row statistics, as if its columns had been accumulated one by one.  Its
 * address votes also go to `recentVotes` when that is given (all of its columns are in the recent region).
 */
function mergeStats(st, votes, sm, base, recentVotes)
{
	if (sm.n === 0) return;
	st.n += sm.n;
	st.lost += sm.lost;
	if (sm.succ === 0) return;
	st.succ += sm.succ;
	st.sum += sm.sum;
	if (sm.min < st.min) st.min = sm.min;
	if (sm.max > st.max) st.max = sm.max;
	if (base + sm.lastCol > st.lastCol) { st.lastCol = base + sm.lastCol; st.last = sm.last; }
	if (st.prevAvg !== null) { st.jitterSum += Math.abs(sm.firstAvg - st.prevAvg); st.jitterN++; }
	else if (st.firstAvg === null) st.firstAvg = sm.firstAvg;
	st.jitterSum += sm.jitterSum;
	st.jitterN += sm.jitterN;
	st.prevAvg = sm.prevAvg;
	for (const [a, v] of sm.votes)
	{
		votes.set(a, (votes.get(a) || 0) + v);
		if (recentVotes) recentVotes.set(a, (recentVotes.get(a) || 0) + v);
	}
}

function mod(a, n)
{
	const m = a % n;
	return m < 0 ? m + n : m;
}

function s_addrIndex(row, addr)
{
	return row.session.addressIndexAtHop(row.ttl, addr);
}

/**
 * Fill-forward rule: an empty column x shows the most recent ping p1 when it lies where no newer ping was due yet.  When
 * the next ping is due is judged from the recent spacing between pings (the smaller of the last two gaps, since one of
 * them may span an outage) plus 10% for timing jitter, and never less than `fillCols` (the current ping interval).  So
 * data recorded before the ping rate was changed keeps its solid bars, while a missing ping still leaves a gap.  Only
 * pings within `maxBack` columns of x count (p2 and p3 are -Infinity when unknown).
 */
function _fillAllows(x, p1, p2, p3, fillCols, maxBack)
{
	const lo = x - maxBack;
	let gap = Infinity;
	if (p2 >= lo) gap = p1 - p2;
	if (p3 >= lo) gap = Math.min(gap, p2 - p3);
	let allow = fillCols;
	if (gap !== Infinity) allow = Math.max(allow, gap - 1 + (gap >= 2 ? Math.ceil(gap * 0.1) : 0));
	return x - p1 <= allow;
}

const CONFIG_NUMBER_DEFAULTS = { badThreshold: 100, worseThreshold: 200, upperLimit: 300, lowerLimit: 0, scalingMethodID: 0 };

/**
 * The configuration with its numeric settings made usable: while the user edits a field it can be empty or not a
 * number (the editor previews every keystroke), and such a value falls back to the default instead of breaking the
 * scale arithmetic.
 */
function sanitizeConfig(cfg)
{
	const out = { ...(cfg || {}) };
	for (const [key, def] of Object.entries(CONFIG_NUMBER_DEFAULTS))
	{
		const v = out[key];
		out[key] = (typeof v === 'number' && Number.isFinite(v)) ? v
			: (typeof v === 'string' && v.trim() !== '' && Number.isFinite(+v)) ? +v : def;
	}
	return out;
}

/** Shortens text with an ellipsis so that it fits in `maxWidth` (in the context's current font). */
function fitText(ctx, text, maxWidth)
{
	if (!text || textWidth(ctx, text) <= maxWidth) return text;
	let lo = 0, hi = text.length;
	while (lo < hi)
	{
		const mid = (lo + hi + 1) >> 1;
		if (textWidth(ctx, text.slice(0, mid) + '…') <= maxWidth) lo = mid; else hi = mid - 1;
	}
	return text.slice(0, lo).trimEnd() + '…';
}

function formatPct(p)
{
	if (p === 0) return '0%';
	if (p < 0.1) return '<0.1%';
	if (p < 10) return p.toFixed(1) + '%';
	return Math.round(p) + '%';
}
