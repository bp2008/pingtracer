/**
 * Renders the ping graphs onto canvases.
 *
 * Performance model
 * -----------------
 * - Every canvas's backing store is sized in exact device pixels and the canvases are snapped to the device pixel grid,
 *   so the browser composites them 1:1 without any resampling.  (Resampling a canvas whose size or position is not
 *   pixel-aligned was the main cost of the previous renderer.)
 * - One device pixel column == one time column.  Columns are aligned to absolute time (column c covers
 *   [c * msPerCol, (c + 1) * msPerCol)), so scrolling is always a whole number of pixels.  Scrolling copies the existing
 *   image sideways with one GPU blit and only the newly exposed columns are drawn.
 * - Bars are drawn into ImageData via a Uint32Array, only for "dirty" regions: newly exposed columns, columns whose data
 *   changed, the live edge (pending pings), and rows whose vertical scale changed.
 * - Text (labels, statistics, hover crosshair) lives on a separate overlay canvas that is redrawn at most a few times per
 *   second unless the user is interacting.
 * - Nothing here touches Vue reactivity.
 */
import dataStore, { CHUNK_COLS, ADDR_NONE, FLAG_MULTI_ADDR } from './DataStore';

/** Milliseconds per device-pixel column for each zoom step. */
export const ZOOM_LEVELS = [
	10, 20, 50, 100, 200, 250, 500, 1000, 2000, 5000, 10000, 15000, 30000, 60000,
	120000, 300000, 600000, 900000, 1800000, 3600000, 7200000, 14400000,
];

const SCALING_CLASSIC = 0, SCALING_ZOOM = 1, SCALING_ZOOM_UNLIMITED = 2, SCALING_FIXED = 3;

function rgb(r, g, b) { return ((0xFF << 24) | (b << 16) | (g << 8) | r) >>> 0; }
function mix(c1, c2, f)
{
	const r = (c1 & 0xFF) + (((c2 & 0xFF) - (c1 & 0xFF)) * f);
	const g = ((c1 >> 8) & 0xFF) + ((((c2 >> 8) & 0xFF) - ((c1 >> 8) & 0xFF)) * f);
	const b = ((c1 >> 16) & 0xFF) + ((((c2 >> 16) & 0xFF) - ((c1 >> 16) & 0xFF)) * f);
	return rgb(r | 0, g | 0, b | 0);
}
function cssOf(u32) { return `rgb(${u32 & 0xFF},${(u32 >> 8) & 0xFF},${(u32 >> 16) & 0xFF})`; }

const C = {
	bg: rgb(0, 0, 0),
	bgBad: rgb(0x23, 0x23, 0x00),
	bgWorse: rgb(0x28, 0x00, 0x00),
	good: rgb(64, 150, 64),
	bad: rgb(150, 150, 0),
	worse: rgb(240, 230, 0),
	lost: rgb(255, 0, 0),
	lostPartial: rgb(235, 20, 20),
	pending: rgb(26, 26, 32),
	collapsed: rgb(12, 12, 15),
	outside: rgb(12, 12, 20),
	separator: rgb(48, 48, 58),
	marker: rgb(0, 200, 255),
	destMarker: rgb(230, 0, 230),
};
const RANGE_DIM = 0.42;
const C_GOOD_RANGE = mix(C.bg, C.good, RANGE_DIM);
const C_BAD_RANGE = mix(C.bg, C.bad, RANGE_DIM);
const C_WORSE_RANGE = mix(C.bg, C.worse, RANGE_DIM);

/** Colors for the address strip; index = order in which the address first appeared at that hop. */
export const ADDRESS_PALETTE = [
	rgb(0, 175, 175), rgb(255, 150, 0), rgb(175, 115, 255), rgb(255, 85, 160),
	rgb(150, 215, 0), rgb(80, 160, 255), rgb(230, 200, 60), rgb(190, 190, 190),
];
export const ADDRESS_PALETTE_CSS = ADDRESS_PALETTE.map(cssOf);

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
		this.ctxBars = this.bars.getContext('2d', { alpha: false });
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
		this.overlayDirty = true;
		this.overlayStatsStale = false;
		this.lastOverlayDraw = 0;
		this.axisDirty = true;
		this.lastAxisRightCol = null;
		this.hover = null;
		this.imgCache = new Map();
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
			this.imgCache.clear();
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
		// Do not scroll endlessly into the past beyond the oldest session.
		const minMs = this._earliestStart();
		if (minMs != null)
		{
			const minRc = Math.floor(minMs / this.msPerCol) + Math.floor(this.W * 0.75);
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
		let i = ZOOM_LEVELS.indexOf(this.msPerCol);
		if (i < 0) i = ZOOM_LEVELS.findIndex(z => z >= this.msPerCol);
		i = Math.max(0, Math.min(ZOOM_LEVELS.length - 1, i + direction));
		this.setZoom(ZOOM_LEVELS[i], anchorX);
	}

	/** Zooms to show the whole time span of all sessions. */
	fitAll()
	{
		const start = this._earliestStart();
		const end = this._latestEnd();
		if (start == null || end == null) return;
		const span = Math.max(1000, end - start);
		const want = span / (this.W * 0.96);
		let level = ZOOM_LEVELS[ZOOM_LEVELS.length - 1];
		for (const z of ZOOM_LEVELS) if (z >= want) { level = z; break; }
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
			this.statsDirty = true;
			this.overlayDirty = true;
			this.axisDirty = true;
			return;
		}
		if (ch.msPerCol !== this.msPerCol || this.drawnRightCol == null) return;
		const lc = this.drawnRightCol - this.W + 1;
		const x0 = Math.max(0, ch.c0 - lc), x1 = Math.min(this.W, ch.c1 - lc);
		if (x1 <= x0) return;
		// A filled-forward bar may extend past the changed column.
		this._markX(x0, Math.min(this.W, x1 + this._fillCols() + 1));
		this.statsDirty = true;
		this.overlayStatsStale = true;
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
	resetPerf() { this.perf = { frames: 0, totalMs: 0, maxMs: 0, fullRedraws: 0, regions: 0, pixels: 0 }; }

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

		if (rc !== this.drawnRightCol) this.statsDirty = true;
		let layoutChanged = false;
		if (this.statsDirty)
		{
			layoutChanged = this._computeRows(sessions, lc, rc, cfg);
			this.statsDirty = false;
			this.overlayStatsStale = true;
		}
		if (layoutChanged) this.needFull = true;

		const ctx = this.ctxBars;
		if (!this.needFull && this.drawnRightCol != null && rc !== this.drawnRightCol)
		{
			const d = rc - this.drawnRightCol;
			if (Math.abs(d) >= this.W) this.needFull = true;
			else
			{
				// Scroll the existing pixels by whole device pixels, then redraw only what was exposed.
				ctx.globalCompositeOperation = 'copy';
				ctx.drawImage(this.bars, -d, 0);
				ctx.globalCompositeOperation = 'source-over';
				if (d > 0)
				{
					this.dirtyX0 = this.dirtyX0 === Infinity ? Infinity : this.dirtyX0 - d;
					this.dirtyX1 = this.dirtyX1 === -Infinity ? -Infinity : this.dirtyX1 - d;
					this._markX(this.W - d, this.W);
				}
				else
				{
					this.dirtyX0 = this.dirtyX0 === Infinity ? Infinity : this.dirtyX0 - d;
					this.dirtyX1 = this.dirtyX1 === -Infinity ? -Infinity : this.dirtyX1 - d;
					this._markX(0, -d);
				}
			}
		}
		if (this.live && rc !== this.drawnRightCol)
		{
			// Pending pings near the live edge change appearance as time passes.
			let timeout = 4000, interval = 1000;
			for (const s of sessions) { timeout = s.meta.timeoutMs || timeout; interval = s.meta.intervalMs || interval; }
			const liveCols = Math.ceil((timeout + interval * 2) / this.msPerCol) + 2;
			this._markX(Math.max(0, this.W - liveCols), this.W);
		}

		const renderCtx = { now, cfg, lc, rc, fillCols: this._fillCols() };
		if (this.needFull)
		{
			this._renderRegion(renderCtx, 0, this.W, this.rows, 0, this.H);
			this.needFull = false;
			this.dirtyRows.clear();
		}
		else
		{
			for (const row of this.rows)
			{
				if (!this.dirtyRows.has(row.key)) continue;
				this._renderRegion(renderCtx, 0, this.W, [row], row.y0, row.y0 + row.h);
			}
			this.dirtyRows.clear();
			const x0 = Math.max(0, this.dirtyX0), x1 = Math.min(this.W, this.dirtyX1);
			if (x1 > x0)
				this._renderRegion(renderCtx, x0, x1, this.rows, 0, this.H);
		}
		this.dirtyX0 = Infinity;
		this.dirtyX1 = -Infinity;
		this.drawnRightCol = rc;
		this.drawnMsPerCol = this.msPerCol;

		const t = performance.now();
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
		const st = { n: 0, lost: 0, succ: 0, sum: 0, min: Infinity, max: -Infinity, last: null, lastCol: -Infinity, jitterSum: 0, jitterN: 0 };
		const votes = new Map();
		// Addresses near the right edge of the view decide the label, so it describes the route "now" (in view terms).
		const recentFrom = rc - Math.max(30, Math.floor(this.W * 0.1));
		const recentVotes = new Map();
		let prevAvg = null;
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
				const N = h.n, L = h.lost;
				for (let i = i0; i <= i1; i++)
				{
					const n = N[i];
					if (n === 0) continue;
					const lost = L[i], succ = n - lost;
					st.n += n;
					st.lost += lost;
					if (succ > 0)
					{
						st.succ += succ;
						st.sum += h.sum[i];
						if (h.min[i] < st.min) st.min = h.min[i];
						if (h.max[i] > st.max) st.max = h.max[i];
						const avg = h.sum[i] / succ;
						if (base + i > st.lastCol) { st.lastCol = base + i; st.last = succ === 1 ? h.sum[i] : avg; }
						if (prevAvg != null) { st.jitterSum += Math.abs(avg - prevAvg); st.jitterN++; }
						prevAvg = avg;
						const a = h.addr[i];
						if (a !== ADDR_NONE)
						{
							votes.set(a, (votes.get(a) || 0) + succ);
							if (base + i >= recentFrom) recentVotes.set(a, (recentVotes.get(a) || 0) + succ);
						}
					}
				}
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
	}

	// ------------------------------------------------------------------ bars

	_image(w, h)
	{
		const key = w + 'x' + h;
		let img = this.imgCache.get(key);
		if (!img)
		{
			if (this.imgCache.size > 16) this.imgCache.clear();
			img = this.ctxBars.createImageData(w, h);
			img.u32 = new Uint32Array(img.data.buffer);
			this.imgCache.set(key, img);
		}
		return img;
	}

	/** Renders device columns [x0, x1) of the given rows, which lie within device rows [yTop, yBottom). */
	_renderRegion(rc_, x0, x1, rows, yTop, yBottom)
	{
		const w = x1 - x0, h = yBottom - yTop;
		if (w <= 0 || h <= 0) return;
		const img = this._image(w, h);
		const px = img.u32;
		this.perf.regions++;
		this.perf.pixels += w * h;
		if (w === this.W && h === this.H) this.perf.fullRedraws++;
		px.fill(C.bg);
		for (const row of rows)
			this._renderRow(rc_, px, w, x0, yTop, row);
		this.ctxBars.putImageData(img, x0, yTop);
	}

	_renderRow(rc_, px, w, x0, yTop, row)
	{
		const { cfg, lc, now } = rc_;
		const s = row.session;
		const ms = this.msPerCol;
		const ttl = row.ttl;
		const top = row.y0 - yTop;
		const sepY = row.h > row.plotH ? top + row.plotH : -1;
		if (sepY >= 0)
			px.fill(C.separator, sepY * w, sepY * w + w);

		if (row.collapsed)
		{
			px.fill(C.collapsed, top * w, (top + row.plotH) * w);
			return;
		}

		const plotTop = top + row.strip;
		const plotBottom = top + row.plotH; // exclusive
		const plotH = plotBottom - plotTop;
		const lo = row.lo, hi = row.hi;
		const scale = plotH / (hi - lo);
		const yOf = (v) => Math.max(plotTop, Math.min(plotBottom, Math.round(plotBottom - (v - lo) * scale)));
		const bad = cfg.badThreshold ?? 100, worse = cfg.worseThreshold ?? 200;
		const yWorse = yOf(worse), yBad = yOf(bad);
		const sessionStart = s.meta.startMs;
		const sessionEnd = s.meta.endMs != null ? s.meta.endMs : Infinity;
		const timeoutMs = s.meta.timeoutMs || 4000, intervalMs = s.meta.intervalMs || 1000;
		// Shade pings awaiting a reply, but only on rows that are still being probed (not beyond the destination).
		const probed = s.meta.running && !(s.meta.mode === 'trace' && s.meta.destTtl > 0 && ttl > s.meta.destTtl);
		const pendingFrom = this.live && probed ? now - timeoutMs - intervalMs : Infinity;
		const fillCols = rc_.fillCols;
		const isStrip = row.strip > 0;
		const minCurtain = Math.max(1, Math.round(this.dpr));

		// Route-change markers for this row, keyed by column.
		const markers = this._markersFor(s, ttl, lc + x0, lc + x0 + w - 1, ms);

		// State for fill-forward: the last non-empty column and its values.
		let lastCol = -Infinity, lN = 0, lLost = 0, lSum = 0, lMax = 0, lAddr = ADDR_NONE, lFlags = 0;
		if (fillCols > 0)
		{
			for (let c = lc + x0 - 1; c >= lc + x0 - fillCols; c--)
			{
				const d = this._col(s, ms, ttl, c);
				if (d && d.n[d.i] > 0)
				{
					lastCol = c; lN = d.n[d.i]; lLost = d.lost[d.i]; lSum = d.sum[d.i]; lMax = d.max[d.i]; lAddr = d.addr[d.i]; lFlags = d.flags[d.i];
					break;
				}
			}
		}

		let chunkIndex = NaN, hc = null;
		const zoom = s.zooms.get(ms);
		for (let x = 0; x < w; x++)
		{
			const c = lc + x0 + x;
			const ci = Math.floor(c / CHUNK_COLS);
			if (ci !== chunkIndex)
			{
				chunkIndex = ci;
				const chunk = zoom ? zoom.chunks.get(ci) : null;
				hc = chunk ? chunk.hops.get(ttl) || null : null;
			}
			const i = c - ci * CHUNK_COLS;
			let n = 0, lost = 0, sum = 0, max = 0, addr = ADDR_NONE, flags = 0;
			if (hc && hc.n[i] > 0)
			{
				n = hc.n[i]; lost = hc.lost[i]; sum = hc.sum[i]; max = hc.max[i]; addr = hc.addr[i]; flags = hc.flags[i];
				lastCol = c; lN = n; lLost = lost; lSum = sum; lMax = max; lAddr = addr; lFlags = flags;
			}
			else if (c - lastCol <= fillCols)
			{
				n = lN; lost = lLost; sum = lSum; max = lMax; addr = lAddr; flags = lFlags;
			}

			const colStartMs = c * ms;
			let idx = plotTop * w + x;
			if (n === 0)
			{
				let color = C.bg;
				if ((colStartMs + ms) <= sessionStart || colStartMs > sessionEnd) color = C.outside;
				else if (colStartMs >= pendingFrom && colStartMs <= now) color = C.pending;
				if (color !== C.bg)
					for (let y = plotTop; y < plotBottom; y++, idx += w) px[idx] = color;
				else
				{
					// Threshold zone backgrounds.
					for (let y = plotTop; y < plotBottom; y++, idx += w)
						px[idx] = y < yWorse ? C.bgWorse : y < yBad ? C.bgBad : C.bg;
				}
			}
			else
			{
				const succ = n - lost;
				const lossFrac = lost / n;
				let barTop = plotBottom, rangeTop = plotBottom, barColor = C.good, rangeColor = C_GOOD_RANGE;
				if (succ > 0)
				{
					const avg = sum / succ;
					barTop = yOf(avg);
					rangeTop = yOf(max);
					if (barTop === plotBottom && avg > lo) barTop = plotBottom - 1; // always show at least one pixel
					if (avg >= worse) { barColor = C.worse; rangeColor = C_WORSE_RANGE; }
					else if (avg >= bad) { barColor = C.bad; rangeColor = C_BAD_RANGE; }
				}
				if (lossFrac >= 1)
				{
					for (let y = plotTop; y < plotBottom; y++, idx += w) px[idx] = C.lost;
				}
				else
				{
					// Partial loss hangs from the top of the column like a curtain whose height is the loss fraction,
					// so it can be read quantitatively and light loss spread over many pings stays unobtrusive.
					const curtainBottom = lost > 0 ? plotTop + Math.max(minCurtain, Math.round(lossFrac * plotH)) : plotTop;
					for (let y = plotTop; y < plotBottom; y++, idx += w)
					{
						let color;
						if (y < curtainBottom) color = C.lostPartial;
						else if (y >= barTop) color = barColor;
						else if (y >= rangeTop) color = rangeColor;
						else color = y < yWorse ? C.bgWorse : y < yBad ? C.bgBad : C.bg;
						px[idx] = color;
					}
				}
			}

			// Address strip.
			if (isStrip)
			{
				let color = C.bg;
				if (n > 0 && addr !== ADDR_NONE)
				{
					color = ADDRESS_PALETTE[s.addressIndexAtHop(ttl, addr) % ADDRESS_PALETTE.length];
					if (flags & FLAG_MULTI_ADDR) color = (x & 1) ? mix(color, C.bg, 0.6) : color;
				}
				let si = top * w + x;
				for (let y = 0; y < row.strip; y++, si += w) px[si] = color;
			}

			// Route change marker: dashed vertical line through the plot.
			if (markers !== null)
			{
				const m = markers.get(c);
				if (m !== undefined)
				{
					let mi = plotTop * w + x;
					const dash = Math.max(2, Math.round(3 * this.dpr));
					for (let y = plotTop; y < plotBottom; y++, mi += w)
						if (((y - plotTop) / dash | 0) % 2 === 0) px[mi] = m;
				}
			}
		}
	}

	/** Returns a Map(column -> marker color) of route changes affecting a row, or null if none. */
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
			let color;
			if (e.kind === 'addr' && e.ttl === ttl) color = C.marker;
			else if (e.kind === 'dest' && ttl !== 0) color = C.destMarker;
			else continue;
			if (!map) map = new Map();
			const col = Math.floor(e.t / ms);
			if (!map.has(col) || color === C.marker) map.set(col, color);
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
			const d = new Date(t);
			const isMidnight = d.getHours() === 0 && d.getMinutes() === 0 && d.getSeconds() === 0;
			let label;
			if (interval >= 86400000) label = d.toLocaleDateString([], { month: 'short', day: 'numeric' });
			else if (isMidnight) label = d.toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' });
			else if (interval >= 60000) label = d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
			else label = d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
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
