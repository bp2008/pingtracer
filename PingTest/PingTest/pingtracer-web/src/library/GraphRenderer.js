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
 * - Text (labels, statistics, hover crosshair) lives on a separate overlay canvas that is redrawn at most a few times per
 *   second unless the user is interacting.
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
		this.axis = this._canvas();
		this.backend = this._createBackend(opts.backend);
		this.ctxOverlay = this.overlay.getContext('2d');
		this.ctxAxis = this.axis.getContext('2d', { alpha: false });

		this.dpr = 1;
		this.W = 0;
		this.H = 0;
		this.axisH = 0;
		this.msPerCol = 500;
		this.live = true;
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
		this.layer.remove();
	}

	_canvas()
	{
		const c = document.createElement('canvas');
		c.style.cssText = 'position:absolute;left:0;display:block;image-rendering:pixelated;';
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
		if (entry && entry.devicePixelContentBoxSize && entry.devicePixelContentBoxSize[0])
		{
			W = entry.devicePixelContentBoxSize[0].inlineSize;
			totalH = entry.devicePixelContentBoxSize[0].blockSize;
		}
		this.setDeviceSize(W, totalH, dpr);
	}

	/** Sizes the canvases to exactly W x totalH device pixels (graph plus time axis). */
	setDeviceSize(W, totalH, dpr)
	{
		const axisH = Math.round(22 * dpr);
		const H = Math.max(1, totalH - axisH);
		W = Math.max(1, W);
		this.dpr = dpr;
		if (W !== this.W || H !== this.H || axisH !== this.axisH)
		{
			this.W = W;
			this.H = H;
			this.axisH = axisH;
			for (const c of [this.bars, this.overlay])
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

	/** Jumps to the newest data: the live edge while monitoring, otherwise the end of the displayed sessions. */
	setLive()
	{
		const sessions = dataStore.getSessions();
		if (sessions.length > 0 && !sessions.some(s => s.meta.running))
		{
			this._setRightCol(Math.floor(this._latestEnd() / this.msPerCol) + Math.floor(this.W * 0.02));
			return;
		}
		if (this.live) return;
		this.live = true;
		this._viewChanged();
	}

	/** Pans by a number of device pixels (positive = towards older data). */
	panBy(dxDevice)
	{
		const rc = this.currentRightCol() - Math.round(dxDevice);
		this._setRightCol(rc);
	}

	_setRightCol(rc)
	{
		const now = this.nowCol;
		if (rc >= now)
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
			if (rc < minRc) rc = Math.min(minRc, now);
		}
		if (rc >= now)
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
	 * which case it stays live.
	 */
	setZoom(msPerCol, anchorX = null)
	{
		if (msPerCol === this.msPerCol) return;
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

	zoomStep(direction, anchorX = null)
	{
		const ms = this.msPerCol;
		let next;
		if (direction > 0) next = ZOOM_LEVELS.find(z => z > ms) ?? ZOOM_LEVELS[ZOOM_LEVELS.length - 1];
		else next = [...ZOOM_LEVELS].reverse().find(z => z < ms) ?? ZOOM_LEVELS[0];
		this.setZoom(next, anchorX);
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
		this.overlayDirty = true;
	}

	// ------------------------------------------------------------------ data notifications

	_onData(ch)
	{
		if (ch.type === 'meta')
		{
			// Session bounds, route events or address lists may have changed: recompute everything (cheap).
			this.invalidateAll();
			return;
		}
		if (ch.msPerCol !== this.msPerCol || this.drawnRightCol == null) return;
		const lc = this.drawnRightCol - this.W + 1;
		const x0 = Math.max(0, ch.c0 - lc), x1 = Math.min(this.W, ch.c1 - lc);
		if (x1 <= x0) return;
		// A filled-forward bar may extend past the changed column.
		this._markX(x0, Math.min(this.W, x1 + this._fillCols() + 1));
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
		return Math.max(0, Math.round(interval / this.msPerCol) - 1);
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
		const cfg = this.opts.getConfig() || {};
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
		for (const s of sessions)
			dataStore.ensure(s.id, this.msPerCol, lc, rc, now);

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
			now, cfg, lc, rc, fillCols: this._fillCols(),
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
			this.overlayDirty = false;
			this.overlayStatsStale = false;
			this.lastOverlayDraw = t;
		}
		if (this.axisDirty || rc !== this.lastAxisRightCol)
		{
			this._drawAxis(renderCtx);
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
			// Drop trailing silent rows (TTLs beyond the last hop that answers).
			while (sessionRows.length > 0 && sessionRows[sessionRows.length - 1].silent) sessionRows.pop();
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
					mergeStats(st, votes, chunkSummary(h), base); // whole chunk in view: use its cached summary
				else
					accumulateColumns(h, base, i0, i1, st, votes, recentFrom, recentVotes);
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
		const { lc, fillCols, bad, worse, minCurtain } = rc_;
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

			// Fill-forward state: the last non-empty column (as a view x) and its values.
			let lastX = -Infinity, lN = 0, lLost = 0, lSum = 0, lMax = 0, lAddr = ADDR_NONE, lFlags = 0;
			if (fillCols > 0)
			{
				for (let c = lc + x0 - 1; c >= lc + x0 - fillCols; c--)
				{
					const d = this._col(s, ms, ttl, c);
					if (d && d.n[d.i] > 0)
					{
						lastX = c - lc; lN = d.n[d.i]; lLost = d.lost[d.i]; lSum = d.sum[d.i]; lMax = d.max[d.i]; lAddr = d.addr[d.i]; lFlags = d.flags[d.i];
						break;
					}
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
						lastX = x; lN = n; lLost = lost; lSum = sum; lMax = max; lAddr = addr; lFlags = flags;
					}
					else if (x - lastX <= fillCols)
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
		const a = row.labelAddr !== ADDR_NONE ? s.addresses.get(row.labelAddr) : null;
		if (!a) return row.collapsed ? '* no reply' : (row.stats.n > 0 ? '* no reply' : '');
		const showNames = cfg.drawServerNames ?? true;
		return showNames && a.host ? `${a.host} [${a.ip}]` : a.ip;
	}

	_drawOverlay(rc_)
	{
		const { cfg } = rc_;
		const ctx = this.ctxOverlay;
		const dpr = this.dpr;
		ctx.setTransform(1, 0, 0, 1, 0, 0);
		ctx.clearRect(0, 0, this.W, this.H);
		ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
		ctx.textBaseline = 'top';
		const fontMain = '12px "Segoe UI", system-ui, sans-serif';
		const fontSmall = '10.5px "Segoe UI", system-ui, sans-serif';

		for (const row of this.rows)
		{
			const yCss = row.y0 / dpr;
			if (row.collapsed)
			{
				ctx.font = fontSmall;
				ctx.fillStyle = '#6c6c78';
				const label = `${row.ttl}  ${this._rowName(row, cfg) || '* no reply'}`;
				ctx.fillText(label, 5, yCss + Math.max(0, (row.plotH / dpr - 11) / 2));
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

			let name = this._rowName(row, cfg);
			const others = row.votes.size - 1;
			const prefix = row.ttl === 0 ? '' : `${row.ttl}  `;
			const statsText = parts.filter(p => p).join('   ');

			ctx.font = fontMain;
			const nameText = prefix + name + (others > 0 ? `  +${others}` : '');
			const nameW = ctx.measureText(nameText).width;
			const statsW = statsText ? ctx.measureText(statsText).width : 0;
			const y = yCss + row.strip / dpr + 2;
			const boxW = nameW + (statsText ? statsW + 16 : 0) + 8 + (row.strip && row.labelAddr !== ADDR_NONE ? 12 : 0);
			ctx.fillStyle = 'rgba(0,0,0,0.62)';
			ctx.fillRect(1, y - 1, boxW, 16);
			let x = 5;
			if (row.strip && row.labelAddr !== ADDR_NONE)
			{
				// Swatch matching the address strip color.
				ctx.fillStyle = ADDRESS_PALETTE_CSS[s_addrIndex(row, row.labelAddr) % ADDRESS_PALETTE_CSS.length];
				ctx.fillRect(x, y + 3, 8, 8);
				x += 12;
			}
			ctx.fillStyle = row.stats.n === 0 ? '#9a9aa6' : '#f0f0f0';
			ctx.fillText(prefix + name, x, y);
			x += ctx.measureText(prefix + name).width;
			if (others > 0)
			{
				ctx.fillStyle = '#8fd8ff';
				ctx.fillText(`  +${others}`, x, y);
				x += ctx.measureText(`  +${others}`).width;
			}
			if (statsText)
			{
				const lossPct = st.n > 0 ? st.lost / st.n : 0;
				ctx.fillStyle = lossPct > 0 ? '#ff9a9a' : '#b8c4b8';
				ctx.fillText(statsText, x + 16, y);
			}

			if (cfg.drawLimitText)
			{
				ctx.font = fontSmall;
				ctx.fillStyle = '#c0c0c0';
				const hiT = String(Math.round(row.hi)), loT = String(Math.round(row.lo));
				const wCss = this.W / dpr;
				ctx.fillText(hiT, wCss - ctx.measureText(hiT).width - 4, y);
				ctx.textBaseline = 'bottom';
				ctx.fillText(loT, wCss - ctx.measureText(loT).width - 4, (row.y0 + row.plotH) / dpr - 1);
				ctx.textBaseline = 'top';
			}
		}

		if (this.emptyMessage)
		{
			ctx.font = '14px "Segoe UI", system-ui, sans-serif';
			ctx.fillStyle = '#777';
			const tw = ctx.measureText(this.emptyMessage).width;
			ctx.fillText(this.emptyMessage, (this.W / dpr - tw) / 2, this.H / dpr / 2 - 8);
		}

		// Hover crosshair.
		if (this.hover)
		{
			const xDev = Math.floor(this.hover.x * dpr);
			const xCss = (xDev + 0.5) / dpr;
			ctx.strokeStyle = 'rgba(255,255,255,0.45)';
			ctx.lineWidth = 1 / dpr;
			ctx.beginPath();
			ctx.moveTo(xCss, 0);
			ctx.lineTo(xCss, this.H / dpr);
			ctx.stroke();
			const row = this.rowAt(this.hover.y * dpr);
			if (row && !row.collapsed)
			{
				const yDev = Math.floor(this.hover.y * dpr);
				const plotTop = row.y0 + row.strip, plotBottom = row.y0 + row.plotH;
				if (yDev >= plotTop && yDev < plotBottom)
				{
					const yCss2 = (yDev + 0.5) / dpr;
					ctx.beginPath();
					ctx.moveTo(0, yCss2);
					ctx.lineTo(this.W / dpr, yCss2);
					ctx.stroke();
					const msVal = row.lo + (plotBottom - yDev) / (plotBottom - plotTop) * (row.hi - row.lo);
					const t = `${Math.round(msVal)} ms`;
					ctx.font = fontSmall;
					const tw = ctx.measureText(t).width;
					const lx = this.hover.x + 8 + tw > this.W / dpr ? this.hover.x - tw - 8 : this.hover.x + 8;
					ctx.fillStyle = 'rgba(0,0,0,0.7)';
					ctx.fillRect(lx - 2, yCss2 - 14, tw + 4, 13);
					ctx.fillStyle = '#fff';
					ctx.fillText(t, lx, yCss2 - 13);
				}
			}
		}
	}

	// ------------------------------------------------------------------ time axis

	_drawAxis(rc_)
	{
		const { lc, rc } = rc_;
		const ctx = this.ctxAxis;
		const dpr = this.dpr;
		const W = this.W, H = this.axisH;
		const ms = this.msPerCol;
		ctx.setTransform(1, 0, 0, 1, 0, 0);
		ctx.fillStyle = '#0b0b10';
		ctx.fillRect(0, 0, W, H);
		ctx.fillStyle = '#3a3a48';
		ctx.fillRect(0, 0, W, 1);

		const pxPerMs = 1 / ms;
		const minSpacingDev = 110 * dpr;
		let interval = AXIS_INTERVALS[AXIS_INTERVALS.length - 1];
		for (const iv of AXIS_INTERVALS) if (iv * pxPerMs >= minSpacingDev) { interval = iv; break; }

		const startMs = lc * ms, endMs = (rc + 1) * ms;
		const tz = -new Date(startMs).getTimezoneOffset() * 60000;
		let t = Math.ceil((startMs + tz) / interval) * interval - tz;
		ctx.font = `${Math.round(11 * dpr)}px "Segoe UI", system-ui, sans-serif`;
		ctx.textBaseline = 'middle';
		for (; t <= endMs; t += interval)
		{
			const x = Math.floor(t / ms) - lc;
			ctx.fillStyle = '#5a5a6a';
			ctx.fillRect(x, 1, 1, Math.round(5 * dpr));
			const { label, isMidnight } = this._axisLabel(t, interval);
			ctx.fillStyle = isMidnight ? '#e6e6a0' : '#b4b4c0';
			ctx.fillText(label, x + Math.round(3 * dpr), H / 2 + 1);
		}

		// Route change ticks.
		for (const s of dataStore.getSessions())
		{
			for (const e of s.routeEvents)
			{
				if (e.initial || e.t < startMs || e.t >= endMs) continue;
				const x = Math.floor(e.t / ms) - lc;
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
			const fill = this._fillCols();
			for (let k = 1; k <= fill; k++)
			{
				const dd = this._col(s, ms, row.ttl, c - k);
				if (dd && dd.n[dd.i] > 0) { d = dd; c -= k; filled = true; break; }
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

/** Adds a chunk summary to running row statistics, as if its columns had been accumulated one by one. */
function mergeStats(st, votes, sm, base)
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
	for (const [a, v] of sm.votes) votes.set(a, (votes.get(a) || 0) + v);
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

function formatPct(p)
{
	if (p === 0) return '0%';
	if (p < 0.1) return '<0.1%';
	if (p < 10) return p.toFixed(1) + '%';
	return Math.round(p) + '%';
}
