/**
 * Renderer self-test, run from the browser console:
 *
 *     await __pingtracer.selfTest()                 // thorough, one to two minutes
 *     await __pingtracer.selfTest({ quick: true })  // a few seconds
 *
 * Uses whatever data is currently displayed (open a saved session for the most thorough test).  It checks that
 * 1. the WebGL2 and Canvas 2D backends paint identical pixels at many zoom levels and positions,
 * 2. incremental painting (scrolling, data arriving, rescaled rows) gives the same image as painting from scratch, and
 * 3. reports frame costs.
 * This module is loaded on demand only and is not part of the normal application bundle.
 */
import dataStore from './DataStore';
import { ZOOM_LEVELS } from './GraphRenderer';

const sleep = (ms) => new Promise(res => setTimeout(res, ms));

function diff(a, b, W)
{
	if (a.length !== b.length) return { n: -1, note: 'size mismatch' };
	let n = 0, first = null;
	for (let i = 0; i < a.length; i += 4)
	{
		if (a[i] !== b[i] || a[i + 1] !== b[i + 1] || a[i + 2] !== b[i + 2])
		{
			if (n++ === 0) first = { x: (i / 4) % W, y: Math.floor(i / 4 / W), a: [a[i], a[i + 1], a[i + 2]], b: [b[i], b[i + 1], b[i + 2]] };
		}
	}
	return { n, first };
}

/** Lets both renderers draw and load data for a while. */
async function pump(renderers, frames, gap = 40)
{
	for (let k = 0; k < frames; k++)
	{
		for (const r of renderers) r.frame();
		await sleep(gap);
	}
}

function setView(renderers, msPerCol, rightCol)
{
	for (const r of renderers)
	{
		r.msPerCol = msPerCol;
		r.live = false;
		r.rightCol = rightCol;
		r.invalidateAll();
	}
}

/** Repaints both renderers from scratch and compares their pixels. */
function compareFull(gl, cpu)
{
	for (const r of [gl, cpu]) { r.invalidateAll(); r.frame(); }
	return diff(gl.backend.readPixels(), cpu.backend.readPixels(), gl.W);
}

let running = false;

export async function runSelfTest(renderer, GraphRenderer, options = {})
{
	// Two runs at once would move each other's views and report false differences.
	if (running) return { ok: false, error: 'A self-test is already running.' };
	running = true;
	try { return await runSelfTestOnce(renderer, GraphRenderer, options); }
	finally { running = false; }
}

async function runSelfTestOnce(renderer, GraphRenderer, options)
{
	const log = options.log || ((m) => console.log('[selftest] ' + m));
	const report = { ok: true, backend: renderer.backendName, checks: [], timings: {} };
	const fail = (name, detail) => { report.ok = false; report.checks.push({ name, ok: false, detail }); log(`FAIL ${name} ${JSON.stringify(detail)}`); };
	const pass = (name) => { report.checks.push({ name, ok: true }); log(`ok   ${name}`); };

	const sessions = dataStore.getSessions();
	if (sessions.length === 0) return { ok: false, error: 'No session is displayed.' };
	if (!renderer.backend.isGl) return { ok: false, error: 'WebGL2 is not active, so there is nothing to compare against.' };
	let start = Infinity, end = -Infinity;
	for (const s of sessions)
	{
		start = Math.min(start, s.meta.startMs);
		end = Math.max(end, s.meta.endMs != null ? s.meta.endMs : renderer.opts.serverNow());
	}

	// A Canvas 2D reference renderer covering exactly the same device pixels.
	const rect = renderer.root.getBoundingClientRect();
	const div = document.createElement('div');
	div.style.cssText = `position:fixed;z-index:-10;pointer-events:none;left:${rect.left}px;top:${rect.top}px;width:${rect.width}px;height:${rect.height}px;`;
	document.body.appendChild(div);
	const realNow = renderer.opts.serverNow;
	const fixedNow = realNow();
	const nowFn = () => fixedNow;
	const cpu = new GraphRenderer(div, { ...renderer.opts, serverNow: nowFn, backend: 'cpu', onViewChanged: null });
	const saved = { msPerCol: renderer.msPerCol, live: renderer.live, rightCol: renderer.rightCol };
	renderer.opts.serverNow = nowFn;
	const both = [renderer, cpu];
	try
	{
		// Use exactly the same device size (the reference box may straddle device pixels differently).
		cpu.resizeObserver.disconnect();
		cpu.setDeviceSize(renderer.W, renderer.H + renderer.axisH, renderer.dpr);
		if (cpu.W !== renderer.W || cpu.H !== renderer.H)
			return { ok: false, error: `Reference size ${cpu.W}x${cpu.H} differs from ${renderer.W}x${renderer.H}` };
		report.size = `${renderer.W}x${renderer.H} device px`;

		// 1. Backend equivalence over zoom levels and positions.
		const span = end - start;
		let zooms = ZOOM_LEVELS.filter(z => z * renderer.W >= Math.min(span, 60000) / 50 && z * renderer.W / 50 <= span);
		let positions = [end + 0.02 * renderer.W * 1000, start + span * 0.5, start + span * 0.1];
		if (options.quick)
		{
			zooms = [zooms[0], zooms[Math.floor(zooms.length / 3)], zooms[Math.floor(zooms.length * 2 / 3)], zooms[zooms.length - 1]]
				.filter((z, i, a) => z !== undefined && a.indexOf(z) === i);
			positions = [end];
		}
		for (const ms of zooms)
		{
			for (const p of positions)
			{
				const at = Math.max(start + renderer.W * ms * 0.5, Math.min(p, end + renderer.W * ms * 0.02));
				setView(both, ms, Math.floor(at / ms));
				await pump(both, 12);
				const d = compareFull(renderer, cpu);
				const name = `pixels identical at ${ms} ms/px, view end ${new Date(at).toISOString()}`;
				if (d.n === 0) pass(name); else fail(name, d);
			}
		}

		// 2. Incremental painting equals painting from scratch, for both backends.
		let seed = 12345;
		const rand = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; };
		const incZooms = options.quick ? [...new Set([zooms[0], zooms[zooms.length - 1]])] : zooms.filter((_, i) => i % 2 === 0);
		for (const ms of incZooms)
		{
			setView(both, ms, Math.floor((start + span * 0.5) / ms));
			await pump(both, 10);
			for (let step = 0; step < 20; step++)
			{
				const k = rand();
				const d = k < 0.6 ? Math.round((rand() - 0.5) * 60) : k < 0.85 ? Math.round((rand() - 0.5) * renderer.W) : Math.round((rand() - 0.5) * 3 * renderer.W);
				for (const r of both) r.panBy(d);
				await pump(both, 1 + Math.floor(rand() * 3), 25);
			}
			for (const r of both) { r.panBy(1); r.frame(); }
			// WebGL skips painting when nothing changed, and its drawing buffer is only readable in the task that painted
			// it, so repaint from the incrementally maintained column texture before reading.
			renderer.backend.draw(renderer._frameParams(renderer.drawnRightCol - renderer.W + 1), renderer.rows);
			const incGl = renderer.backend.readPixels(), incCpu = cpu.backend.readPixels();
			for (const r of both) { r.invalidateAll(); r.frame(); }
			const fullGl = renderer.backend.readPixels(), fullCpu = cpu.backend.readPixels();
			const a = diff(incGl, fullGl, renderer.W), b = diff(incCpu, fullCpu, renderer.W), c = diff(fullGl, fullCpu, renderer.W);
			const name = `incremental painting at ${ms} ms/px after 20 random pans`;
			if (a.n === 0 && b.n === 0 && c.n === 0) pass(name); else fail(name, { webgl: a, canvas2d: b, webglVsCanvas2d: c });
		}

		// 3. Timings (CPU time spent in frame(); GPU work is asynchronous).
		const ms = zooms[Math.floor(zooms.length / 2)] || renderer.msPerCol;
		setView(both, ms, Math.floor((start + span * 0.5) / ms));
		await pump(both, 15);
		const time = (fn, n = 60) => { const t = performance.now(); for (let i = 0; i < n; i++) fn(); return +((performance.now() - t) / n).toFixed(3); };
		let dir = 1;
		report.timings.webglFullRedrawMs = time(() => { renderer.needFull = true; renderer.frame(); });
		report.timings.webglPanOneColumnMs = time(() => { renderer.panBy(dir); dir = -dir; renderer.frame(); });
		report.timings.statsLabelsAxisMs = time(() => { renderer.invalidateAll(); renderer.needFull = false; renderer.frame(); });
		report.timings.note = 'Canvas 2D timings are not measured here: reading pixels back makes the browser move that canvas to the CPU.';
		log(`timings ${JSON.stringify(report.timings)}`);
	}
	finally
	{
		renderer.opts.serverNow = realNow;
		renderer.msPerCol = saved.msPerCol;
		renderer.live = saved.live;
		renderer.rightCol = saved.rightCol;
		renderer.invalidateAll();
		cpu.destroy();
		div.remove();
	}
	log(report.ok ? `PASSED (${report.checks.length} checks)` : 'FAILED');
	return report;
}
