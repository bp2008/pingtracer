/**
 * Bar-drawing backends.
 *
 * The renderer reduces the visible data to one record per (row, device column) in a "column buffer" (Uint16 RGBA, one
 * texture row per graph row), stored as a ring indexed by absolute column modulo the view width.  All vertical positions
 * are precomputed integers, relative to the top of the graph row:
 *   R = top of the average bar (the bar fills [R, plotBottom))
 *   G = top of the min..max range shading (fills [G, R))
 *   B = bottom of the partial-loss curtain (fills [plotTop, B))
 *   A = packed state (see STATE_* below)
 * A backend only compares the pixel's row against these numbers, so both backends produce identical pixels.
 *
 * - GlBars (WebGL2) uploads only changed columns and repaints the whole graph with one small draw per row.  Every pixel
 *   is chosen by a fragment shader, so a full repaint costs the CPU almost nothing and scrolling is just a different ring
 *   offset.
 * - CpuBars (Canvas 2D fallback) rasterizes the same buffer into ImageData.  It is used when WebGL2 is unavailable, and
 *   as a reference to verify the shader pixel for pixel.
 */

export const KIND_EMPTY = 0, KIND_DATA = 1, KIND_OUTSIDE = 2, KIND_LOST = 3;
export const STATE_KIND_MASK = 3;
export const STATE_STRIP_SHIFT = 2; // 4 bits: address palette index + 1 (0 = no address)
export const STATE_DIM = 64; // strip drawn dimmed (multi-address column, odd absolute column)
export const STATE_MARKER_SHIFT = 7; // 2 bits: 0 none, 1 hop address change, 2 destination change
export const STATE_LEVEL_SHIFT = 9; // 2 bits: 0 good, 1 bad, 2 worse (bar color)

export function rgb(r, g, b) { return ((0xFF << 24) | (b << 16) | (g << 8) | r) >>> 0; }
export function mix(c1, c2, f)
{
	const r = (c1 & 0xFF) + (((c2 & 0xFF) - (c1 & 0xFF)) * f);
	const g = ((c1 >> 8) & 0xFF) + ((((c2 >> 8) & 0xFF) - ((c1 >> 8) & 0xFF)) * f);
	const b = ((c1 >> 16) & 0xFF) + ((((c2 >> 16) & 0xFF) - ((c1 >> 16) & 0xFF)) * f);
	return rgb(r | 0, g | 0, b | 0);
}
export function cssOf(u32) { return `rgb(${u32 & 0xFF},${(u32 >> 8) & 0xFF},${(u32 >> 16) & 0xFF})`; }

export const C = {
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
const BAR_COLORS = [C.good, C.bad, C.worse];
const RANGE_COLORS = BAR_COLORS.map(c => mix(C.bg, c, RANGE_DIM));

/** Colors for the address strip; index = order in which the address first appeared at that hop. */
export const ADDRESS_PALETTE = [
	rgb(0, 175, 175), rgb(255, 150, 0), rgb(175, 115, 255), rgb(255, 85, 160),
	rgb(150, 215, 0), rgb(80, 160, 255), rgb(230, 200, 60), rgb(190, 190, 190),
];
const ADDRESS_PALETTE_DIM = ADDRESS_PALETTE.map(c => mix(c, C.bg, 0.6));

/*
 * Per-frame parameters shared by all rows: { W, H, lcMod, dash }
 * Per-row parameters (fields of each row object):
 *   { texRow, y0, h, plotH, strip, collapsed, yWorse, yBad, pendX0, pendX1 }
 * yWorse / yBad are the rows (relative to the row top) where the threshold background zones end.
 */

// ---------------------------------------------------------------------------------------------------------- WebGL2

function glColor(u32) { return `vec4(${(u32 & 0xFF)}.0/255.0, ${((u32 >> 8) & 0xFF)}.0/255.0, ${((u32 >> 16) & 0xFF)}.0/255.0, 1.0)`; }
function glArray(name, colors) { return `const vec4 ${name}[${colors.length}] = vec4[${colors.length}](${colors.map(glColor).join(', ')});`; }

const VERTEX_SHADER = `#version 300 es
uniform vec4 uRect;   // x0, y0, x1, y1 in device pixels, origin top-left
uniform vec2 uSize;   // W, H
void main()
{
	float x = (gl_VertexID & 1) == 0 ? uRect.x : uRect.z;
	float y = (gl_VertexID >> 1) == 0 ? uRect.y : uRect.w;
	gl_Position = vec4(x / uSize.x * 2.0 - 1.0, 1.0 - y / uSize.y * 2.0, 0.0, 1.0);
}`;

const FRAGMENT_SHADER = `#version 300 es
precision highp float;
precision highp int;
precision highp usampler2D;
uniform usampler2D uCols;
uniform int uW, uH, uLcMod, uDash;
uniform int uTexRow, uY0, uPlotH, uStrip, uCollapsed, uYWorse, uYBad, uPendX0, uPendX1;
out vec4 o;
${glArray('PALETTE', ADDRESS_PALETTE)}
${glArray('PALETTE_DIM', ADDRESS_PALETTE_DIM)}
${glArray('BAR', BAR_COLORS)}
${glArray('RANGE', RANGE_COLORS)}

void main()
{
	int x = int(gl_FragCoord.x);
	int y = uH - 1 - int(gl_FragCoord.y) - uY0; // device row relative to the top of this graph row
	if (y >= uPlotH) { o = ${glColor(C.separator)}; return; }
	if (uCollapsed != 0) { o = ${glColor(C.collapsed)}; return; }
	int tx = uLcMod + x;
	if (tx >= uW) tx -= uW;
	ivec4 d = ivec4(texelFetch(uCols, ivec2(tx, uTexRow), 0));
	int st = d.a;
	int kind = st & ${STATE_KIND_MASK};
	if (y < uStrip)
	{
		int pi = (st >> ${STATE_STRIP_SHIFT}) & 15;
		if (kind == ${KIND_EMPTY} || kind == ${KIND_OUTSIDE} || pi == 0) { o = ${glColor(C.bg)}; return; }
		o = (st & ${STATE_DIM}) != 0 ? PALETTE_DIM[(pi - 1) & 7] : PALETTE[(pi - 1) & 7];
		return;
	}
	int marker = (st >> ${STATE_MARKER_SHIFT}) & 3;
	if (marker != 0 && ((y - uStrip) / uDash) % 2 == 0) { o = marker == 1 ? ${glColor(C.marker)} : ${glColor(C.destMarker)}; return; }
	if (kind == ${KIND_OUTSIDE}) { o = ${glColor(C.outside)}; return; }
	if (kind == ${KIND_LOST}) { o = ${glColor(C.lost)}; return; }
	vec4 zone = y < uYWorse ? ${glColor(C.bgWorse)} : y < uYBad ? ${glColor(C.bgBad)} : ${glColor(C.bg)};
	if (kind == ${KIND_EMPTY}) { o = (x >= uPendX0 && x <= uPendX1) ? ${glColor(C.pending)} : zone; return; }
	int level = (st >> ${STATE_LEVEL_SHIFT}) & 3;
	if (y < d.b) o = ${glColor(C.lostPartial)};
	else if (y >= d.r) o = BAR[level];
	else if (y >= d.g) o = RANGE[level];
	else o = zone;
}`;

const UNIFORMS = ['uCols', 'uW', 'uH', 'uLcMod', 'uDash', 'uTexRow', 'uY0', 'uPlotH', 'uStrip', 'uCollapsed',
	'uYWorse', 'uYBad', 'uPendX0', 'uPendX1', 'uRect', 'uSize'];

export class GlBars
{
	/** Returns a WebGL2 backend for the canvas, or null if WebGL2 is not usable. */
	static create(canvas, onRestored)
	{
		let gl;
		try
		{
			gl = canvas.getContext('webgl2', {
				alpha: false, antialias: false, depth: false, stencil: false, premultipliedAlpha: false,
				preserveDrawingBuffer: false, powerPreference: 'low-power',
			});
		}
		catch { gl = null; }
		if (!gl) return null;
		const b = new GlBars(canvas, gl, onRestored);
		return b.init() ? b : null;
	}

	constructor(canvas, gl, onRestored)
	{
		this.isGl = true;
		this.canvas = canvas;
		this.gl = gl;
		this.lost = false;
		this.texW = 0;
		this.texH = 0;
		this.W = 0;
		this.H = 0;
		canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); this.lost = true; });
		canvas.addEventListener('webglcontextrestored', () =>
		{
			this.lost = false;
			this.texW = this.texH = 0;
			if (this.init() && onRestored) onRestored();
		});
	}

	init()
	{
		const gl = this.gl;
		const compile = (type, src) =>
		{
			const sh = gl.createShader(type);
			gl.shaderSource(sh, src);
			gl.compileShader(sh);
			if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS))
			{
				console.error('PingTracer shader error:', gl.getShaderInfoLog(sh));
				return null;
			}
			return sh;
		};
		const vs = compile(gl.VERTEX_SHADER, VERTEX_SHADER);
		const fs = compile(gl.FRAGMENT_SHADER, FRAGMENT_SHADER);
		if (!vs || !fs) return false;
		const prog = gl.createProgram();
		gl.attachShader(prog, vs);
		gl.attachShader(prog, fs);
		gl.linkProgram(prog);
		if (!gl.getProgramParameter(prog, gl.LINK_STATUS))
		{
			console.error('PingTracer shader link error:', gl.getProgramInfoLog(prog));
			return false;
		}
		this.prog = prog;
		this.u = {};
		for (const name of UNIFORMS) this.u[name] = gl.getUniformLocation(prog, name);
		this.vao = gl.createVertexArray();
		this.tex = null;
		this.maxTex = gl.getParameter(gl.MAX_TEXTURE_SIZE);
		gl.disable(gl.DITHER);
		gl.disable(gl.BLEND);
		gl.disable(gl.DEPTH_TEST);
		return true;
	}

	/** True if a view of this width can be handled. */
	supportsWidth(W) { return W <= this.maxTex; }

	resize(W, H)
	{
		this.W = W;
		this.H = H;
	}

	/** Makes sure the column texture has the given size. Returns true if it was (re)allocated, losing its content. */
	ensureTexture(W, rowsCap)
	{
		if (this.lost) return false;
		if (W === this.texW && rowsCap === this.texH) return false;
		const gl = this.gl;
		if (this.tex) gl.deleteTexture(this.tex);
		this.tex = gl.createTexture();
		gl.bindTexture(gl.TEXTURE_2D, this.tex);
		gl.texStorage2D(gl.TEXTURE_2D, 1, gl.RGBA16UI, W, rowsCap);
		gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
		gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
		gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
		gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
		this.texW = W;
		this.texH = rowsCap;
		return true;
	}

	/** Uploads ring columns [tx0, tx1) of buffer rows [row0, row0 + nRows) (the buffer's row stride is W texels). */
	upload(buf, W, row0, nRows, tx0, tx1)
	{
		if (this.lost || nRows <= 0 || tx1 <= tx0) return;
		const gl = this.gl;
		gl.bindTexture(gl.TEXTURE_2D, this.tex);
		gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
		gl.pixelStorei(gl.UNPACK_ROW_LENGTH, W);
		gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, tx0);
		gl.pixelStorei(gl.UNPACK_SKIP_ROWS, row0);
		gl.texSubImage2D(gl.TEXTURE_2D, 0, tx0, row0, tx1 - tx0, nRows, gl.RGBA_INTEGER, gl.UNSIGNED_SHORT, buf);
		gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
		gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, 0);
		gl.pixelStorei(gl.UNPACK_SKIP_ROWS, 0);
	}

	/** Repaints the whole graph. */
	draw(fp, rows)
	{
		if (this.lost) return;
		const gl = this.gl, u = this.u;
		gl.viewport(0, 0, fp.W, fp.H);
		gl.clearColor(0, 0, 0, 1);
		gl.clear(gl.COLOR_BUFFER_BIT);
		if (rows.length === 0 || !this.tex) return;
		gl.useProgram(this.prog);
		gl.bindVertexArray(this.vao);
		gl.activeTexture(gl.TEXTURE0);
		gl.bindTexture(gl.TEXTURE_2D, this.tex);
		gl.uniform1i(u.uCols, 0);
		gl.uniform1i(u.uW, fp.W);
		gl.uniform1i(u.uH, fp.H);
		gl.uniform1i(u.uLcMod, fp.lcMod);
		gl.uniform1i(u.uDash, fp.dash);
		gl.uniform2f(u.uSize, fp.W, fp.H);
		for (const row of rows)
		{
			gl.uniform4f(u.uRect, 0, row.y0, fp.W, row.y0 + row.h);
			gl.uniform1i(u.uTexRow, row.texRow);
			gl.uniform1i(u.uY0, row.y0);
			gl.uniform1i(u.uPlotH, row.plotH);
			gl.uniform1i(u.uStrip, row.strip);
			gl.uniform1i(u.uCollapsed, row.collapsed ? 1 : 0);
			gl.uniform1i(u.uYWorse, row.yWorse);
			gl.uniform1i(u.uYBad, row.yBad);
			gl.uniform1i(u.uPendX0, row.pendX0);
			gl.uniform1i(u.uPendX1, row.pendX1);
			gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
		}
	}

	/** Reads back the current image as RGBA bytes, top row first (diagnostics). Call in the same task as draw(). */
	readPixels()
	{
		const gl = this.gl;
		const W = this.W, H = this.H;
		const raw = new Uint8Array(W * H * 4);
		gl.readPixels(0, 0, W, H, gl.RGBA, gl.UNSIGNED_BYTE, raw);
		const out = new Uint8Array(W * H * 4);
		for (let y = 0; y < H; y++)
			out.set(raw.subarray((H - 1 - y) * W * 4, (H - y) * W * 4), y * W * 4);
		return out;
	}

	/** Blocks until the GPU has finished (diagnostics). */
	finish()
	{
		const px = new Uint8Array(4);
		this.gl.readPixels(0, 0, 1, 1, this.gl.RGBA, this.gl.UNSIGNED_BYTE, px);
	}
}

// ---------------------------------------------------------------------------------------------------------- Canvas 2D

export class CpuBars
{
	constructor(canvas)
	{
		this.isGl = false;
		this.canvas = canvas;
		this.ctx = canvas.getContext('2d', { alpha: false });
		this.imgCache = new Map();
		this.W = 0;
		this.H = 0;
		this.scratch = null;
	}

	supportsWidth() { return true; }

	resize(W, H)
	{
		this.W = W;
		this.H = H;
		this.imgCache.clear();
		this.scratch = {
			solid: new Uint32Array(W), // color of the whole plot column if it is uniform (0 = layered)
			empty: new Uint8Array(W), // 1 = threshold-zone background only
			curtain: new Int32Array(W),
			barTop: new Int32Array(W),
			rangeTop: new Int32Array(W),
			barColor: new Uint32Array(W),
			rangeColor: new Uint32Array(W),
			strip: new Uint32Array(W),
			marker: new Uint32Array(W),
		};
	}

	ensureTexture() { return false; }

	/** Moves the existing image sideways by d device pixels (positive = towards older data). */
	scroll(d)
	{
		const ctx = this.ctx;
		ctx.globalCompositeOperation = 'copy';
		ctx.drawImage(this.canvas, -d, 0);
		ctx.globalCompositeOperation = 'source-over';
	}

	_image(w, h)
	{
		const key = w + 'x' + h;
		let img = this.imgCache.get(key);
		if (!img)
		{
			if (this.imgCache.size > 16) this.imgCache.clear();
			img = this.ctx.createImageData(w, h);
			img.u32 = new Uint32Array(img.data.buffer);
			this.imgCache.set(key, img);
		}
		return img;
	}

	/** Paints device columns [x0, x1) of the given rows, which lie within device rows [yTop, yBottom). */
	render(fp, buf, x0, x1, rows, yTop, yBottom)
	{
		const w = x1 - x0, h = yBottom - yTop;
		if (w <= 0 || h <= 0) return;
		const img = this._image(w, h);
		const px = img.u32;
		px.fill(C.bg);
		for (const row of rows)
			this._renderRow(fp, buf, px, w, x0, yTop, row);
		this.ctx.putImageData(img, x0, yTop);
	}

	_renderRow(fp, buf, px, w, x0, yTop, row)
	{
		const top = row.y0 - yTop;
		if (row.h > row.plotH)
			px.fill(C.separator, (top + row.plotH) * w, (top + row.plotH) * w + w);
		if (row.collapsed)
		{
			px.fill(C.collapsed, top * w, (top + row.plotH) * w);
			return;
		}
		const W = fp.W;
		const s = this.scratch;
		const plotTop = row.strip, plotBottom = row.plotH; // relative to the row top
		const rowBase = row.texRow * W;

		// Pass 1: decode each column.
		let tx = fp.lcMod + x0;
		if (tx >= W) tx -= W;
		for (let x = 0; x < w; x++)
		{
			const o = (rowBase + tx) * 4;
			if (++tx === W) tx = 0;
			const st = buf[o + 3];
			const kind = st & STATE_KIND_MASK;
			const pi = (st >> STATE_STRIP_SHIFT) & 15;
			s.strip[x] = (kind === KIND_DATA || kind === KIND_LOST) && pi !== 0 ? ((st & STATE_DIM) ? ADDRESS_PALETTE_DIM : ADDRESS_PALETTE)[(pi - 1) & 7] : C.bg;
			const marker = (st >> STATE_MARKER_SHIFT) & 3;
			s.marker[x] = marker === 1 ? C.marker : marker === 2 ? C.destMarker : 0;
			s.empty[x] = 0;
			s.solid[x] = 0;
			if (kind === KIND_OUTSIDE) s.solid[x] = C.outside;
			else if (kind === KIND_LOST) s.solid[x] = C.lost;
			else if (kind === KIND_EMPTY)
			{
				const vx = x0 + x;
				if (vx >= row.pendX0 && vx <= row.pendX1) s.solid[x] = C.pending;
				else s.empty[x] = 1;
			}
			else
			{
				const level = (st >> STATE_LEVEL_SHIFT) & 3;
				s.barTop[x] = buf[o];
				s.rangeTop[x] = buf[o + 1];
				s.curtain[x] = buf[o + 2];
				s.barColor[x] = BAR_COLORS[level];
				s.rangeColor[x] = RANGE_COLORS[level];
			}
		}

		// Pass 2: paint row by row (sequential memory access).  Most pixels equal the pixel above, so each device row
		// starts as a copy of the previous one and only columns with a color boundary at this row are recomputed.
		const strip = s.strip, marker = s.marker, solid = s.solid, empty = s.empty;
		const curtain = s.curtain, barTop = s.barTop, rangeTop = s.rangeTop, barColor = s.barColor, rangeColor = s.rangeColor;
		for (let y = 0; y < plotTop; y++)
		{
			const base = (top + y) * w;
			for (let x = 0; x < w; x++) px[base + x] = strip[x];
		}
		const yWorse = row.yWorse, yBad = row.yBad;
		const lostPartial = C.lostPartial;
		const colorAt = (x, y, zone) =>
		{
			if (solid[x] !== 0) return solid[x];
			if (empty[x] !== 0) return zone;
			if (y < curtain[x]) return lostPartial;
			if (y >= barTop[x]) return barColor[x];
			if (y >= rangeTop[x]) return rangeColor[x];
			return zone;
		};
		// Bucket the columns by the device rows at which their color can change.
		const plotH = plotBottom - plotTop;
		const counts = this._counts(plotH + 1);
		counts.fill(0);
		for (let x = 0; x < w; x++)
		{
			if (solid[x] !== 0 || empty[x] !== 0) continue;
			const a = curtain[x], b = rangeTop[x], c = barTop[x];
			if (a > plotTop && a < plotBottom) counts[a - plotTop]++;
			if (b > plotTop && b < plotBottom && b !== a) counts[b - plotTop]++;
			if (c > plotTop && c < plotBottom && c !== a && c !== b) counts[c - plotTop]++;
		}
		let total = 0;
		for (let i = 0; i <= plotH; i++) { const n = counts[i]; counts[i] = total; total += n; }
		const events = this._events(total);
		for (let x = 0; x < w; x++)
		{
			if (solid[x] !== 0 || empty[x] !== 0) continue;
			const a = curtain[x], b = rangeTop[x], c = barTop[x];
			if (a > plotTop && a < plotBottom) events[counts[a - plotTop]++] = x;
			if (b > plotTop && b < plotBottom && b !== a) events[counts[b - plotTop]++] = x;
			if (c > plotTop && c < plotBottom && c !== a && c !== b) events[counts[c - plotTop]++] = x;
		}
		// counts[i] is now the end of bucket i; bucket i starts at counts[i - 1].
		let zone = plotTop < yWorse ? C.bgWorse : plotTop < yBad ? C.bgBad : C.bg;
		let base = (top + plotTop) * w;
		for (let x = 0; x < w; x++) px[base + x] = colorAt(x, plotTop, zone);
		for (let y = plotTop + 1; y < plotBottom; y++)
		{
			const prev = base;
			base += w;
			const newZone = y < yWorse ? C.bgWorse : y < yBad ? C.bgBad : C.bg;
			if (newZone !== zone)
			{
				// Threshold zone boundary: background pixels change everywhere, so recompute the whole device row.
				zone = newZone;
				for (let x = 0; x < w; x++) px[base + x] = colorAt(x, y, zone);
				continue;
			}
			px.copyWithin(base, prev, prev + w);
			const i = y - plotTop;
			for (let e = counts[i - 1]; e < counts[i]; e++)
			{
				const x = events[e];
				px[base + x] = colorAt(x, y, zone);
			}
		}
		// Route change markers: dashed vertical lines over everything except the address strip.
		const dash = fp.dash;
		for (let x = 0; x < w; x++)
		{
			const m = marker[x];
			if (m === 0) continue;
			for (let y = plotTop; y < plotBottom; y++)
				if ((((y - plotTop) / dash) | 0) % 2 === 0) px[(top + y) * w + x] = m;
		}
	}

	_counts(n)
	{
		if (!this.countsBuf || this.countsBuf.length < n) this.countsBuf = new Int32Array(Math.max(n, 256));
		return this.countsBuf.subarray(0, n);
	}

	_events(n)
	{
		if (!this.eventsBuf || this.eventsBuf.length < n) this.eventsBuf = new Int32Array(Math.max(n, 4096));
		return this.eventsBuf;
	}

	/** Returns the current image as RGBA bytes, top row first (diagnostics). */
	readPixels()
	{
		return new Uint8Array(this.ctx.getImageData(0, 0, this.W, this.H).data.buffer);
	}

	finish()
	{
		this.ctx.getImageData(0, 0, 1, 1);
	}
}
