/**
 * WebSocket connection to the PingTracer server.
 *
 * Control plane: JSON text frames, dispatched to listeners by their `type` field.
 * Data plane: binary frames.  Live ping batches are handed to the `live` listeners as a DataView (parsed by the
 * DataStore without intermediate allocations); query responses resolve the promise returned by `query()`.
 *
 * Also keeps an estimate of the server's clock (see `serverNow()`), because every timestamp in the data plane comes
 * from the server's monotonic clock and the live edge of the graph must agree with it exactly.
 */

export const FrameType = Object.freeze({
	LiveBatch: 0x11,
	QueryResult: 0x12,
});

const QUERY_TIMEOUT_MS = 30000;

function clientClock()
{
	return performance.timeOrigin + performance.now();
}

class PingTracerSocket
{
	constructor()
	{
		this.ws = null;
		this.listeners = new Map();
		this.reconnectTimer = null;
		this.reconnectDelay = 500;
		this.nextQueryId = 1;
		this.pendingQueries = new Map();
		this.connected = false;
		this.shouldConnect = false;

		// Clock synchronization: offset = serverClock - clientClock.
		this.clockOffset = 0;
		this.clockSamples = [];
		this.clockTimer = null;
	}

	connect()
	{
		this.shouldConnect = true;
		if (this.ws && (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING))
			return;
		const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
		const ws = new WebSocket(`${protocol}//${location.host}/ws`);
		ws.binaryType = 'arraybuffer';
		this.ws = ws;

		ws.onopen = () =>
		{
			this.connected = true;
			this.reconnectDelay = 500;
			this.clockSamples = [];
			this._emit('connected');
			this._startClockSync();
		};
		ws.onmessage = (e) =>
		{
			if (e.data instanceof ArrayBuffer)
			{
				try { this._handleBinary(e.data); }
				catch (err) { console.error('Failed to decode binary frame', err); }
				return;
			}
			let msg;
			try { msg = JSON.parse(e.data); }
			catch (err) { console.error('Bad JSON frame', err); return; }
			if (msg.type === 'time')
				this._handleTime(msg);
			else if (msg.type === 'hello' || msg.type === 'status')
				this._roughClock(msg.serverTime);
			this._emit(msg.type, msg);
		};
		ws.onclose = () =>
		{
			if (this.ws !== ws) return;
			this.ws = null;
			const wasConnected = this.connected;
			this.connected = false;
			clearInterval(this.clockTimer);
			this.clockTimer = null;
			for (const p of this.pendingQueries.values())
			{
				clearTimeout(p.timer);
				p.reject(new Error('Connection closed'));
			}
			this.pendingQueries.clear();
			if (wasConnected) this._emit('disconnected');
			if (this.shouldConnect) this._scheduleReconnect();
		};
		ws.onerror = () => { /* onclose follows */ };
	}

	disconnect()
	{
		this.shouldConnect = false;
		clearTimeout(this.reconnectTimer);
		this.reconnectTimer = null;
		if (this.ws) this.ws.close();
	}

	_scheduleReconnect()
	{
		if (this.reconnectTimer) return;
		this.reconnectTimer = setTimeout(() =>
		{
			this.reconnectTimer = null;
			this.reconnectDelay = Math.min(this.reconnectDelay * 1.6, 10000);
			this.connect();
		}, this.reconnectDelay);
	}

	send(obj)
	{
		if (this.ws && this.ws.readyState === WebSocket.OPEN)
		{
			this.ws.send(JSON.stringify(obj));
			return true;
		}
		return false;
	}

	on(type, cb)
	{
		if (!this.listeners.has(type)) this.listeners.set(type, []);
		this.listeners.get(type).push(cb);
		return () => this.off(type, cb);
	}

	off(type, cb)
	{
		const arr = this.listeners.get(type);
		if (!arr) return;
		const i = arr.indexOf(cb);
		if (i >= 0) arr.splice(i, 1);
	}

	_emit(type, data)
	{
		const arr = this.listeners.get(type);
		if (!arr) return;
		for (const cb of arr)
		{
			try { cb(data); }
			catch (err) { console.error(`Error in "${type}" listener`, err); }
		}
	}

	// ----- Clock -----

	/** Current time on the server's clock, in Unix epoch milliseconds. */
	serverNow()
	{
		return clientClock() + this.clockOffset;
	}

	_roughClock(serverTime)
	{
		if (typeof serverTime === 'number' && this.clockSamples.length === 0)
			this.clockOffset = serverTime - clientClock();
	}

	_startClockSync()
	{
		let burst = 0;
		const ping = () => this.send({ action: 'time', c: clientClock() });
		ping();
		const burstTimer = setInterval(() =>
		{
			if (++burst >= 5) clearInterval(burstTimer);
			ping();
		}, 250);
		this.clockTimer = setInterval(ping, 30000);
	}

	_handleTime(msg)
	{
		const now = clientClock();
		const rtt = now - msg.c;
		if (rtt < 0 || rtt > 10000) return;
		this.clockSamples.push({ rtt, offset: msg.s - (msg.c + rtt / 2) });
		if (this.clockSamples.length > 12) this.clockSamples.shift();
		// Use the sample with the lowest round trip time; it has the least uncertainty.
		let best = this.clockSamples[0];
		for (const s of this.clockSamples) if (s.rtt < best.rtt) best = s;
		this.clockOffset = best.offset;
	}

	// ----- Data plane -----

	/**
	 * Requests aggregated columns.  Column k of the result covers [(startCol + k) * msPerCol, (startCol + k + 1) * msPerCol).
	 * Resolves with { sessionId, msPerCol, startCol, colCount, hops: [{ ttl, count, col, n, lost, min, max, sum, addr, flags }] }.
	 */
	query(sessionId, msPerCol, startCol, colCount)
	{
		const qid = this.nextQueryId++;
		return new Promise((resolve, reject) =>
		{
			const timer = setTimeout(() =>
			{
				this.pendingQueries.delete(qid);
				reject(new Error('Query timed out'));
			}, QUERY_TIMEOUT_MS);
			this.pendingQueries.set(qid, { resolve, reject, timer });
			if (!this.send({ action: 'query', qid, sessionId, msPerCol, startCol, colCount }))
			{
				clearTimeout(timer);
				this.pendingQueries.delete(qid);
				reject(new Error('Not connected'));
			}
		});
	}

	_handleBinary(buf)
	{
		const view = new DataView(buf);
		const type = view.getUint8(0);
		if (type === FrameType.LiveBatch)
			this._emit('live', view);
		else if (type === FrameType.QueryResult)
			this._handleQueryResult(view);
		else
			console.warn('Unknown binary frame type', type);
	}

	_handleQueryResult(v)
	{
		let o = 1;
		const qid = v.getUint32(o, true); o += 4;
		const sessionId = v.getUint16(o, true); o += 2;
		const msPerCol = v.getUint32(o, true); o += 4;
		const startCol = v.getFloat64(o, true); o += 8;
		const colCount = v.getUint16(o, true); o += 2;
		const hopCount = v.getUint16(o, true); o += 2;
		const hops = [];
		for (let h = 0; h < hopCount; h++)
		{
			const ttl = v.getUint8(o); o += 1;
			const count = v.getUint16(o, true); o += 2;
			const col = new Uint16Array(count), n = new Uint16Array(count), lost = new Uint16Array(count);
			const min = new Uint16Array(count), max = new Uint16Array(count), addr = new Uint16Array(count);
			const sum = new Float64Array(count), flags = new Uint8Array(count);
			for (let i = 0; i < count; i++)
			{
				col[i] = v.getUint16(o, true);
				n[i] = v.getUint16(o + 2, true);
				lost[i] = v.getUint16(o + 4, true);
				min[i] = v.getUint16(o + 6, true);
				max[i] = v.getUint16(o + 8, true);
				sum[i] = v.getFloat32(o + 10, true);
				addr[i] = v.getUint16(o + 14, true);
				flags[i] = v.getUint8(o + 16);
				o += 17;
			}
			hops.push({ ttl, count, col, n, lost, min, max, sum, addr, flags });
		}
		const p = this.pendingQueries.get(qid);
		if (!p) return;
		clearTimeout(p.timer);
		this.pendingQueries.delete(qid);
		p.resolve({ sessionId, msPerCol, startCol, colCount, hops });
	}
}

const socket = new PingTracerSocket();
export default socket;
