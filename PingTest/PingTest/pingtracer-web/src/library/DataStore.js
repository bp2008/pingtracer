/**
 * Client-side model of everything the graphs display.  Deliberately NOT reactive: Vue never sees the ping data, so
 * nothing re-renders through the reactivity system and the renderer reads typed arrays directly.
 *
 * Data is cached as time-aligned "columns".  For a zoom level of `msPerCol`, column c covers Unix time
 * [c * msPerCol, (c + 1) * msPerCol).  Because columns are aligned to absolute time, any cached column stays valid as the
 * view scrolls, and a live ping can be added to exactly one column per cached zoom level.
 *
 * Columns are cached in chunks of CHUNK_COLS, loaded from the server with column queries.  Live pings received while a
 * chunk is loading are folded into it and then superseded by the query response (the server guarantees the response
 * already includes them; see PingWebSocketHandler).
 */
import socket from './WebSocketService';

export const CHUNK_COLS = 256;
// 64 chunks x 256 columns is about a dozen screen widths per zoom level; roughly 5 KB per hop per chunk.
const MAX_CHUNKS_PER_ZOOM = 64;
const MAX_ZOOMS_PER_SESSION = 6;
const MAX_CONCURRENT_QUERIES = 6;

const EMPTY = 0, LOADING = 1, LOADED = 2;

export const ADDR_NONE = 0xFFFF;
export const RTT_LOST = 0xFFFF;
export const FLAG_MULTI_ADDR = 1;

/** Aggregated statistics for up to CHUNK_COLS columns of one hop. */
export class HopChunk
{
	constructor()
	{
		this.n = new Uint16Array(CHUNK_COLS);
		this.lost = new Uint16Array(CHUNK_COLS);
		this.min = new Uint16Array(CHUNK_COLS);
		this.max = new Uint16Array(CHUNK_COLS);
		this.sum = new Float64Array(CHUNK_COLS);
		this.addr = new Uint16Array(CHUNK_COLS).fill(ADDR_NONE);
		this.flags = new Uint8Array(CHUNK_COLS);
		/** Incremented on every change; lets readers cache summaries of this chunk. */
		this.version = 0;
	}

	add(i, rtt, addr)
	{
		this.version++;
		if (this.n[i] === 0)
		{
			this.min[i] = 0xFFFF;
			this.max[i] = 0;
			this.addr[i] = ADDR_NONE;
		}
		if (this.n[i] < 0xFFFF) this.n[i]++;
		if (rtt === RTT_LOST)
		{
			if (this.lost[i] < 0xFFFF) this.lost[i]++;
			return;
		}
		this.sum[i] += rtt;
		if (rtt < this.min[i]) this.min[i] = rtt;
		if (rtt > this.max[i]) this.max[i] = rtt;
		const a = this.addr[i];
		if (a === ADDR_NONE) this.addr[i] = addr;
		else if (a !== addr)
		{
			this.flags[i] |= FLAG_MULTI_ADDR;
			this.addr[i] = addr; // the most recent responder wins for live data
		}
	}

	remove(i, rtt)
	{
		if (this.n[i] === 0) return;
		this.version++;
		this.n[i]--;
		if (rtt === RTT_LOST)
		{
			if (this.lost[i] > 0) this.lost[i]--;
		}
		else
			this.sum[i] = Math.max(0, this.sum[i] - rtt);
		if (this.n[i] === 0)
		{
			this.lost[i] = 0;
			this.sum[i] = 0;
			this.flags[i] = 0;
			this.addr[i] = ADDR_NONE;
		}
		else if (this.n[i] === this.lost[i])
		{
			this.sum[i] = 0;
			this.addr[i] = ADDR_NONE;
		}
	}
}

class Chunk
{
	constructor(index)
	{
		this.index = index;
		this.state = EMPTY;
		/** @type {Map<number, HopChunk>} ttl -> data */
		this.hops = new Map();
		this.touched = 0;
		this.generation = 0;
	}

	hop(ttl, create)
	{
		let h = this.hops.get(ttl);
		if (!h && create)
		{
			h = new HopChunk();
			this.hops.set(ttl, h);
		}
		return h;
	}
}

class ZoomCache
{
	constructor(msPerCol)
	{
		this.msPerCol = msPerCol;
		/** @type {Map<number, Chunk>} */
		this.chunks = new Map();
		this.touched = 0;
	}
}

export class SessionData
{
	constructor(meta)
	{
		this.id = meta.id;
		this.meta = meta;
		/** @type {Map<number, {ip: string, host: string}>} */
		this.addresses = new Map();
		/** Route events sorted by time: { t, kind: 'addr'|'dest'|'target', ttl, value, initial } */
		this.routeEvents = [];
		/** ttl -> array of address ids in order of first appearance at that hop */
		this.hopAddresses = new Map();
		this.repliedTtls = new Set(meta.repliedTtls || []);
		/** @type {Map<number, ZoomCache>} */
		this.zooms = new Map();
		/** Ids of every address the session has pinged as its destination (its host name can resolve differently over time). */
		this.targetAddrIds = new Set();
	}

	/** True if an address id is (or was) the session's destination. */
	isTargetAddr(addrId)
	{
		return addrId !== ADDR_NONE && this.targetAddrIds.has(addrId);
	}

	/** Recomputes the destination address ids from the session's target addresses. */
	_updateTargets()
	{
		const ips = new Set(this.meta.targets || []);
		if (this.meta.target) ips.add(this.meta.target);
		this.targetAddrIds.clear();
		for (const [id, a] of this.addresses)
			if (ips.has(a.ip)) this.targetAddrIds.add(id);
	}

	/** Index of an address among the addresses seen at a hop (0 for the first), used to pick a stable color. */
	addressIndexAtHop(ttl, addrId)
	{
		const list = this.hopAddresses.get(ttl);
		if (!list) return 0;
		const i = list.indexOf(addrId);
		return i < 0 ? 0 : i;
	}

	addressCountAtHop(ttl)
	{
		const list = this.hopAddresses.get(ttl);
		return list ? list.length : 0;
	}

	_noteHopAddress(ttl, addrId)
	{
		let list = this.hopAddresses.get(ttl);
		if (!list) this.hopAddresses.set(ttl, list = []);
		if (!list.includes(addrId)) list.push(addrId);
	}
}

class DataStore
{
	constructor()
	{
		/** @type {Map<number, SessionData>} */
		this.sessions = new Map();
		/** Session ids in display order. */
		this.order = [];
		this.view = { source: 'live', fileName: null };
		this.listeners = new Set();
		this.activeQueries = 0;
		this.queryQueue = [];
		this.touchCounter = 0;
		/** Incremented whenever metadata changes (sessions, addresses, route events). */
		this.metaVersion = 0;

		socket.on('view', (m) => this._onView(m));
		socket.on('session', (m) => this._onSession(m));
		socket.on('addresses', (m) => this._onAddresses(m));
		socket.on('routeEvents', (m) => this._onRouteEvents(m));
		socket.on('live', (v) => this._onLive(v));
		socket.on('disconnected', () => this.clear());
	}

	/**
	 * Subscribes to changes. The callback receives one of:
	 *   { type: 'meta' }
	 *   { type: 'columns', sessionId, msPerCol, c0, c1 }  (columns [c0, c1) changed)
	 */
	subscribe(cb)
	{
		this.listeners.add(cb);
		return () => this.listeners.delete(cb);
	}

	_notify(change)
	{
		for (const cb of this.listeners) cb(change);
	}

	_meta()
	{
		this.metaVersion++;
		this._notify({ type: 'meta' });
	}

	clear()
	{
		this.sessions.clear();
		this.order = [];
		this.queryQueue = [];
		this._meta();
	}

	getSessions()
	{
		const list = [];
		for (const id of this.order)
		{
			const s = this.sessions.get(id);
			if (s) list.push(s);
		}
		return list;
	}

	// ----- Control-plane messages -----

	_onView(m)
	{
		this.view = { source: m.source, fileName: m.fileName };
		this.sessions.clear();
		this.order = [];
		this.queryQueue = [];
		this._meta();
	}

	_onSession(m)
	{
		let s = this.sessions.get(m.id);
		if (!s)
		{
			s = new SessionData(m);
			this.sessions.set(m.id, s);
			this.order.push(m.id);
		}
		else
		{
			const endChanged = s.meta.endMs !== m.endMs;
			s.meta = m;
			s.repliedTtls = new Set(m.repliedTtls || []);
			s._updateTargets();
			if (endChanged)
			{
				// Chunks that were assumed empty because they were in the future may now be in range; reload them.
				for (const z of s.zooms.values())
					for (const c of z.chunks.values())
						if (c.state === LOADED && c.assumedEmpty) c.state = EMPTY;
			}
		}
		this._meta();
	}

	_onAddresses(m)
	{
		const s = this.sessions.get(m.sessionId);
		if (!s) return;
		for (const a of m.items)
		{
			const prev = s.addresses.get(a.id);
			s.addresses.set(a.id, { ip: a.ip || (prev && prev.ip) || '', host: a.host || (prev && prev.host) || '' });
		}
		s._updateTargets();
		this._meta();
	}

	_onRouteEvents(m)
	{
		const s = this.sessions.get(m.sessionId);
		if (!s) return;
		for (const e of m.items)
		{
			s.routeEvents.push(e);
			if (e.kind === 'addr') s._noteHopAddress(e.ttl, e.value);
		}
		// Events normally arrive in order; keep the array sorted regardless.
		const arr = s.routeEvents;
		for (let i = arr.length - m.items.length; i > 0 && i < arr.length; i++)
		{
			if (arr[i - 1].t > arr[i].t)
			{
				arr.sort((a, b) => a.t - b.t);
				break;
			}
		}
		this._meta();
	}

	// ----- Live data -----

	_onLive(v)
	{
		const count = v.getUint16(1, true);
		let o = 3;
		// Track the changed column range per (session, zoom) to send one notification each.
		const dirty = new Map();
		for (let i = 0; i < count; i++, o += 16)
		{
			const sessionId = v.getUint16(o, true);
			const ttl = v.getUint8(o + 2);
			const kind = v.getUint8(o + 3);
			const t = v.getFloat64(o + 4, true);
			const rtt = v.getUint16(o + 12, true);
			const addr = v.getUint16(o + 14, true);
			const s = this.sessions.get(sessionId);
			if (!s) continue;
			if (kind === 0 && rtt !== RTT_LOST && !s.repliedTtls.has(ttl))
			{
				s.repliedTtls.add(ttl);
				this._meta();
			}
			for (const z of s.zooms.values())
			{
				const col = Math.floor(t / z.msPerCol);
				const ci = Math.floor(col / CHUNK_COLS);
				const chunk = z.chunks.get(ci);
				if (!chunk || chunk.state === EMPTY) continue;
				const h = chunk.hop(ttl, kind === 0);
				if (!h) continue;
				const i2 = col - ci * CHUNK_COLS;
				if (kind === 0) h.add(i2, rtt, addr);
				else h.remove(i2, rtt);
				const key = sessionId + ':' + z.msPerCol;
				const d = dirty.get(key);
				if (!d) dirty.set(key, { sessionId, msPerCol: z.msPerCol, c0: col, c1: col + 1 });
				else
				{
					if (col < d.c0) d.c0 = col;
					if (col + 1 > d.c1) d.c1 = col + 1;
				}
			}
		}
		for (const d of dirty.values())
			this._notify({ type: 'columns', ...d });
	}

	// ----- Column access -----

	_zoom(s, msPerCol)
	{
		let z = s.zooms.get(msPerCol);
		if (!z)
		{
			z = new ZoomCache(msPerCol);
			s.zooms.set(msPerCol, z);
			if (s.zooms.size > MAX_ZOOMS_PER_SESSION)
			{
				let oldest = null;
				for (const zz of s.zooms.values())
					if (zz !== z && (!oldest || zz.touched < oldest.touched)) oldest = zz;
				if (oldest) s.zooms.delete(oldest.msPerCol);
			}
		}
		z.touched = ++this.touchCounter;
		return z;
	}

	/**
	 * Makes sure columns [c0, c1] of a session at a zoom level are loaded or loading.  Returns true if all are loaded.
	 */
	ensure(sessionId, msPerCol, c0, c1, serverNow)
	{
		const s = this.sessions.get(sessionId);
		if (!s) return true;
		const z = this._zoom(s, msPerCol);
		const touch = ++this.touchCounter;
		const first = Math.floor(c0 / CHUNK_COLS), last = Math.floor(c1 / CHUNK_COLS);
		let allLoaded = true;
		// Prefetch one chunk on each side so panning does not immediately hit unloaded data.
		for (let ci = first - 1; ci <= last + 1; ci++)
		{
			let chunk = z.chunks.get(ci);
			if (!chunk)
			{
				chunk = new Chunk(ci);
				z.chunks.set(ci, chunk);
			}
			chunk.touched = touch;
			if (chunk.state === EMPTY)
			{
				const startMs = ci * CHUNK_COLS * msPerCol;
				const endMs = (ci + 1) * CHUNK_COLS * msPerCol;
				const sessionEnd = s.meta.endMs != null ? s.meta.endMs : serverNow + 60000;
				if (startMs > sessionEnd || endMs <= s.meta.startMs)
				{
					// Nothing can exist here yet (or ever). Live data will fill it in if it is in the future.
					chunk.state = LOADED;
					chunk.assumedEmpty = true;
				}
				else
				{
					chunk.state = LOADING;
					this._enqueueQuery(s, z, chunk, ci >= first && ci <= last);
				}
			}
			if (ci >= first && ci <= last && chunk.state !== LOADED) allLoaded = false;
		}
		this._evict(z);
		return allLoaded;
	}

	_evict(z)
	{
		if (z.chunks.size <= MAX_CHUNKS_PER_ZOOM) return;
		const list = [...z.chunks.values()].filter(c => c.state !== LOADING).sort((a, b) => a.touched - b.touched);
		let excess = z.chunks.size - MAX_CHUNKS_PER_ZOOM;
		for (const c of list)
		{
			if (excess-- <= 0) break;
			z.chunks.delete(c.index);
		}
	}

	_enqueueQuery(s, z, chunk, urgent)
	{
		const job = { s, z, chunk };
		if (urgent) this.queryQueue.unshift(job);
		else this.queryQueue.push(job);
		this._pumpQueries();
	}

	_pumpQueries()
	{
		while (this.activeQueries < MAX_CONCURRENT_QUERIES && this.queryQueue.length > 0)
		{
			const { s, z, chunk } = this.queryQueue.shift();
			if (this.sessions.get(s.id) !== s || s.zooms.get(z.msPerCol) !== z || z.chunks.get(chunk.index) !== chunk)
				continue; // discarded while queued
			this.activeQueries++;
			const generation = ++chunk.generation;
			socket.query(s.id, z.msPerCol, chunk.index * CHUNK_COLS, CHUNK_COLS)
				.then((res) =>
				{
					// Applied synchronously in the same task as the message, before any later live batch is processed.
					if (chunk.generation !== generation) return;
					chunk.hops.clear();
					for (const hop of res.hops)
					{
						const h = chunk.hop(hop.ttl, true);
						for (let i = 0; i < hop.count; i++)
						{
							const c = hop.col[i];
							h.n[c] = hop.n[i];
							h.lost[c] = hop.lost[i];
							h.min[c] = hop.min[i];
							h.max[c] = hop.max[i];
							h.sum[c] = hop.sum[i];
							h.addr[c] = hop.addr[i];
							h.flags[c] = hop.flags[i];
						}
					}
					chunk.state = LOADED;
					const c0 = chunk.index * CHUNK_COLS;
					this._notify({ type: 'columns', sessionId: s.id, msPerCol: z.msPerCol, c0, c1: c0 + CHUNK_COLS, bulk: true });
				}, () =>
				{
					if (chunk.generation === generation) chunk.state = EMPTY;
				})
				.finally(() =>
				{
					this.activeQueries--;
					this._pumpQueries();
				});
		}
	}

	/**
	 * Returns the HopChunk holding column `col` for a hop, or null.  `outIndex[0]` receives the index within the chunk.
	 * Hot path: callers iterate columns and should cache the chunk across consecutive columns.
	 */
	getChunk(session, msPerCol, ci)
	{
		const z = session.zooms.get(msPerCol);
		if (!z) return null;
		const c = z.chunks.get(ci);
		return c && c.state !== EMPTY ? c : null;
	}

	/** Invalidates all cached columns (e.g. after a reconnect). */
	invalidateAll()
	{
		for (const s of this.sessions.values()) s.zooms.clear();
		this._meta();
	}
}

const dataStore = new DataStore();
export default dataStore;
