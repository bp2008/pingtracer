/**
 * WebSocket service for communicating with the PingTracer C# backend.
 * Provides real-time ping data streaming and command interface.
 */
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
	}

	/**
	 * Connect to the PingTracer WebSocket server.
	 */
	connect()
	{
		if (this.ws && (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING))
			return;

		this.isConnecting = true;
		const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
		const url = `${protocol}//${location.host}/ws`;

		this.ws = new WebSocket(url);

		this.ws.onopen = () =>
		{
			this.isConnecting = false;
			this.reconnectDelay = 1000;
			this._emit('connected');
		};

		this.ws.onmessage = (event) =>
		{
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
			this._emit('disconnected');
			this._scheduleReconnect();
		};

		this.ws.onerror = (err) =>
		{
			console.error('WebSocket error:', err);
		};
	}

	/**
	 * Disconnect from the WebSocket server.
	 */
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

	/**
	 * Send a message to the server.
	 * @param {Object} msg
	 */
	send(msg)
	{
		if (this.ws && this.ws.readyState === WebSocket.OPEN)
			this.ws.send(JSON.stringify(msg));
	}

	// --- Commands ---

	selectConfig(guid)
	{
		this.send({ action: 'selectConfig', guid });
	}

	start()
	{
		this.send({ action: 'start' });
	}

	stop()
	{
		this.send({ action: 'stop' });
	}

	getConfigurations()
	{
		this.send({ action: 'getConfigurations' });
	}

	saveConfig(config)
	{
		this.send({ action: 'saveConfig', config });
	}

	deleteConfig(guid)
	{
		this.send({ action: 'deleteConfig', guid });
	}

	requestPingData(targetId, count, offset)
	{
		this.send({ action: 'requestPingData', targetId, count, offset });
	}

	// --- Event System ---

	/**
	 * Register an event listener.
	 * @param {string} event
	 * @param {Function} callback
	 */
	on(event, callback)
	{
		if (!this.listeners[event])
			this.listeners[event] = [];
		this.listeners[event].push(callback);
	}

	/**
	 * Remove an event listener.
	 * @param {string} event
	 * @param {Function} callback
	 */
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
}

/** Singleton instance */
const pingTracerWS = new PingTracerWS();
export default pingTracerWS;
