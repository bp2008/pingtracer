/**
 * WebSocket service for communicating with the PingTracer C# backend.
 *
 * Mixed-protocol transport:
 * - Control plane (configurations, status, logs, errors) is JSON text frames.
 * - Data plane (route topology, ping completions, hostname updates, query results)
 *   is little-endian binary frames. See PingWebSocketHandler.cs for the matching
 *   encoder. Every binary frame begins with [FrameType:u8][SessionIndex:u8].
 */

const FrameType = Object.freeze({
	RouteUpdate: 0x01,
	HostnameUpdated: 0x02,
	PingUpdate: 0x03,
	AggregatedData: 0x04,
	HopDeactivated: 0x05,
	SessionTopology: 0x06,
});

class FrameReader
{
	constructor(buffer)
	{
		this.view = new DataView(buffer);
		this.offset = 0;
	}

	get atEnd() { return this.offset >= this.view.byteLength; }

	u8() { return this.view.getUint8(this.offset++); }

	u16() { const v = this.view.getUint16(this.offset, true); this.offset += 2; return v; }

	u32() { const v = this.view.getUint32(this.offset, true); this.offset += 4; return v; }

	// uint64 → Number. Timestamps in ms stay safely below Number.MAX_SAFE_INTEGER (year 287396).
	u64()
	{
		const v = this.view.getBigUint64(this.offset, true);
		this.offset += 8;
		return Number(v);
	}

	ip()
	{
		const len = this.u8();
		if (len === 0) return null;
		const bytes = new Uint8Array(this.view.buffer, this.view.byteOffset + this.offset, len);
		this.offset += len;
		if (len === 4) return `${bytes[0]}.${bytes[1]}.${bytes[2]}.${bytes[3]}`;
		// IPv6 — group into 8 hex words, no compression. Good enough for display.
		const parts = [];
		for (let i = 0; i < 16; i += 2)
			parts.push(((bytes[i] << 8) | bytes[i + 1]).toString(16));
		return parts.join(':');
	}

	utf8String()
	{
		const len = this.u16();
		if (len === 0) return '';
		const bytes = new Uint8Array(this.view.buffer, this.view.byteOffset + this.offset, len);
		this.offset += len;
		return new TextDecoder('utf-8').decode(bytes);
	}
}

class PingTracerWS
{
	constructor()
	{
		/** @type {WebSocket|null} */
		this.ws = null;
		this.reconnectTimeout = null;
		this.reconnectDelay = 1000;
		this.maxReconnectDelay = 30000;
		this.listeners = {};
		this.isConnecting = false;

		// queryData uses an integer correlation ID echoed back in the response frame.
		this._nextQueryId = 1;
		this._pendingQueries = new Map(); // queryId -> {resolve, reject, timeoutId}
	}

	connect()
	{
		if (this.ws && (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING))
			return;

		this.isConnecting = true;
		const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
		const url = `${protocol}//${location.host}/ws`;

		this.ws = new WebSocket(url);
		this.ws.binaryType = 'arraybuffer';

		this.ws.onopen = () =>
		{
			this.isConnecting = false;
			this.reconnectDelay = 1000;
			this._emit('connected');
		};

		this.ws.onmessage = (event) =>
		{
			if (event.data instanceof ArrayBuffer)
			{
				try { this._handleBinary(event.data); }
				catch (e) { console.error('Failed to decode binary frame:', e); }
				return;
			}

			try
			{
				const msg = JSON.parse(event.data);
				this._emit(msg.type, msg);
			}
			catch (e)
			{
				console.error('Failed to parse WebSocket message:', e);
			}
		};

		this.ws.onclose = () =>
		{
			this.isConnecting = false;
			// Reject pending queries — the server has lost session-index context anyway.
			for (const [, p] of this._pendingQueries)
			{
				clearTimeout(p.timeoutId);
				try { p.reject(new Error('WebSocket closed')); } catch {}
			}
			this._pendingQueries.clear();
			this._emit('disconnected');
			this._scheduleReconnect();
		};

		this.ws.onerror = (err) =>
		{
			console.error('WebSocket error:', err);
		};
	}

	disconnect()
	{
		if (this.reconnectTimeout)
		{
			clearTimeout(this.reconnectTimeout);
			this.reconnectTimeout = null;
		}
		if (this.ws)
		{
			this.ws.close();
			this.ws = null;
		}
	}

	send(msg)
	{
		if (this.ws && this.ws.readyState === WebSocket.OPEN)
			this.ws.send(JSON.stringify(msg));
	}

	// --- Commands ---

	selectConfig(guid) { this.send({ action: 'selectConfig', guid }); }
	start() { this.send({ action: 'start' }); }
	stop() { this.send({ action: 'stop' }); }
	getConfigurations() { this.send({ action: 'getConfigurations' }); }
	saveConfig(config) { this.send({ action: 'saveConfig', config }); }
	deleteConfig(guid) { this.send({ action: 'deleteConfig', guid }); }
	setPingRate(rate, pingsPerSecond) { this.send({ action: 'setPingRate', rate, pingsPerSecond }); }

	/**
	 * Query historical aggregated data for one session. Resolves with a
	 * { sessionIndex, queryId, series: [{ hopNumber, address, hostname,
	 * seriesStartUtc, seriesEndUtc, points: [{ t, min, max, avg, lossPct, samples }] }] }
	 * shape. Rejects after timeoutMs if no response arrives.
	 */
	queryData(sessionIndex, startTimeUtc, endTimeUtc, maxPointsPerHop, timeoutMs = 10000)
	{
		const queryId = this._nextQueryId++;
		return new Promise((resolve, reject) =>
		{
			const timeoutId = setTimeout(() =>
			{
				this._pendingQueries.delete(queryId);
				reject(new Error('queryData timed out'));
			}, timeoutMs);
			this._pendingQueries.set(queryId, { resolve, reject, timeoutId });

			this.send({
				action: 'queryData',
				queryId,
				sessionIndex,
				startTimeUtc,
				endTimeUtc,
				maxPointsPerHop
			});
		});
	}

	// --- Event System ---

	on(event, callback)
	{
		if (!this.listeners[event])
			this.listeners[event] = [];
		this.listeners[event].push(callback);
	}

	off(event, callback)
	{
		if (!this.listeners[event]) return;
		this.listeners[event] = this.listeners[event].filter(cb => cb !== callback);
	}

	_emit(event, data)
	{
		if (this.listeners[event])
			this.listeners[event].forEach(cb =>
			{
				try { cb(data); }
				catch (e) { console.error('Error in WS listener:', e); }
			});
	}

	_scheduleReconnect()
	{
		if (this.reconnectTimeout) return;
		this.reconnectTimeout = setTimeout(() =>
		{
			this.reconnectTimeout = null;
			this.reconnectDelay = Math.min(this.reconnectDelay * 1.5, this.maxReconnectDelay);
			this.connect();
		}, this.reconnectDelay);
	}

	_handleBinary(buf)
	{
		const r = new FrameReader(buf);
		const type = r.u8();
		const sessionIndex = r.u8();

		switch (type)
		{
			case FrameType.SessionTopology: this._emit('sessionTopology', this._readSessionTopology(r)); break;
			case FrameType.RouteUpdate: this._emit('routeUpdate', this._readRouteUpdate(r, sessionIndex)); break;
			case FrameType.HostnameUpdated: this._emit('hostnameUpdated', this._readHostnameUpdated(r)); break;
			case FrameType.PingUpdate: this._emit('pingUpdate', this._readPingUpdate(r, sessionIndex)); break;
			case FrameType.AggregatedData: this._handleAggregatedData(r, sessionIndex); break;
			case FrameType.HopDeactivated: this._emit('hopDeactivated', this._readHopDeactivated(r, sessionIndex)); break;
			default: console.warn('Unknown binary frame type:', type);
		}
	}

	_readSessionTopology(r)
	{
		const count = r.u8();
		const sessions = [];
		for (let i = 0; i < count; i++)
		{
			const index = r.u8();
			const targetAddress = r.ip();
			const displayName = r.utf8String();
			const sessionStartUtc = r.u64();
			sessions.push({ index, targetAddress, displayName, sessionStartUtc });
		}
		return { sessions };
	}

	_readRouteUpdate(r, sessionIndex)
	{
		const timestampUtc = r.u64();
		const hopCount = r.u8();
		const hops = [];
		for (let i = 0; i < hopCount; i++)
		{
			const hopNumber = r.u8();
			const isPresent = r.u8();
			if (isPresent === 0)
			{
				hops.push(null);
			}
			else
			{
				const address = r.ip();
				const hostname = r.utf8String();
				const seriesStartUtc = r.u64();
				hops.push({ hopNumber, address, hostname, seriesStartUtc });
			}
		}
		return { sessionIndex, timestampUtc, hops };
	}

	_readHostnameUpdated(r)
	{
		const address = r.ip();
		const hostname = r.utf8String();
		return { address, hostname };
	}

	_readPingUpdate(r, sessionIndex)
	{
		const hopNumber = r.u8();
		const seriesStartUtc = r.u64();
		const t = r.u64();
		const ms = r.u16();
		return { sessionIndex, hopNumber, seriesStartUtc, t, ms };
	}

	_readHopDeactivated(r, sessionIndex)
	{
		const hopNumber = r.u8();
		const address = r.ip();
		const seriesEndUtc = r.u64();
		return { sessionIndex, hopNumber, address, seriesEndUtc };
	}

	_handleAggregatedData(r, sessionIndex)
	{
		const queryId = r.u32();
		const seriesCount = r.u8();
		const series = [];
		for (let i = 0; i < seriesCount; i++)
		{
			const hopNumber = r.u8();
			const address = r.ip();
			const hostname = r.utf8String();
			const seriesStartUtc = r.u64();
			const seriesEndUtc = r.u64(); // 0 = still active
			const pointCount = r.u32();
			const points = new Array(pointCount);
			for (let j = 0; j < pointCount; j++)
			{
				const t = r.u64();
				let min = r.u16(); if (min === 0xFFFF) min = null;
				let max = r.u16(); if (max === 0xFFFF) max = null;
				let avg = r.u16(); if (avg === 0xFFFF) avg = null;
				const lossPct = r.u16() / 100.0;
				const samples = r.u32();
				points[j] = { t, min, max, avg, lossPct, samples };
			}
			series.push({ hopNumber, address, hostname, seriesStartUtc, seriesEndUtc, points });
		}

		const payload = { sessionIndex, queryId, series };
		const pending = this._pendingQueries.get(queryId);
		if (pending)
		{
			clearTimeout(pending.timeoutId);
			this._pendingQueries.delete(queryId);
			try { pending.resolve(payload); } catch (e) { console.error(e); }
		}
		this._emit('aggregatedData', payload);
	}
}

const pingTracerWS = new PingTracerWS();
export default pingTracerWS;
