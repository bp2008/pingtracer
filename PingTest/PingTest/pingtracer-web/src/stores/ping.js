import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import pingTracerWS from '@/library/WebSocketService';

/**
 * Pinia store for the route-aware data plane.
 *
 * Per-series ping data is stored in two places, both keyed by
 * `${sessionIndex}:${hopNumber}:${seriesStartUtc}`:
 *   - liveTail: incremental {t, ms} points capped at LIVE_TAIL_CAP (drawn on
 *     the trailing edge of the graph, no server round-trip).
 *   - history: aggregated {t, min, max, avg, lossPct, samples} buckets fetched
 *     via queryData, populated when the viewport extends beyond the live tail.
 *
 * `hopHistory[sessionIndex][hopNumber]` is the chronological list of all
 * series segments seen for a given hop position (active + closed). Used by
 * the graph to render a single row per hop position even when its address
 * has changed mid-window.
 */

const LIVE_TAIL_CAP = 5000;
const VIEWPORT_QUERY_DEBOUNCE_MS = 120;

export function keyFor(sessionIndex, hopNumber, seriesStartUtc)
{
	return `${sessionIndex}:${hopNumber}:${seriesStartUtc}`;
}

export const usePingStore = defineStore('ping', () =>
{
	// --- Control plane (JSON) ---
	const connected = ref(false);
	const configurations = ref([]);
	const selectedConfigGuid = ref(null);
	const configDetails = ref(null);
	const isRunning = ref(false);
	const status = ref('Idle');
	const successfulPings = ref(0);
	const failedPings = ref(0);
	const logMessages = ref([]);
	const errors = ref([]);

	// --- Data plane (binary) ---
	// sessions: [{ index, displayName, targetAddress, sessionStartUtc }]
	const sessions = ref([]);
	// routes: { [sessionIndex]: { timestampUtc, hops: [hopEntry|null] } }
	const routes = ref({});
	// hopHistory: { [sessionIndex]: { [hopNumber]: [{address, hostname, seriesStartUtc, seriesEndUtc|null}] } }
	const hopHistory = ref({});
	// liveTail: { [key]: [{ t, ms }] }
	const liveTail = ref({});
	// history: { [key]: { points: [{ t, min, max, avg, lossPct, samples }], address, hostname, seriesEndUtc, queriedStart, queriedEnd, queriedMaxPoints } }
	const history = ref({});

	// Debounced viewport query state (per session)
	const _queryTimers = {};   // sessionIndex -> setTimeout handle
	const _lastQuery = {};     // sessionIndex -> { startUtc, endUtc, maxPoints }

	const selectedConfig = computed(() =>
		configurations.value.find(c => c.guid === selectedConfigGuid.value) || null);

	function _resetData()
	{
		sessions.value = [];
		routes.value = {};
		hopHistory.value = {};
		liveTail.value = {};
		history.value = {};
		for (const k of Object.keys(_queryTimers))
		{
			clearTimeout(_queryTimers[k]);
			delete _queryTimers[k];
		}
		for (const k of Object.keys(_lastQuery)) delete _lastQuery[k];
	}

	function _ensureHopHistory(sessionIndex, hopNumber)
	{
		let bySession = hopHistory.value[sessionIndex];
		if (!bySession)
		{
			bySession = {};
			hopHistory.value = { ...hopHistory.value, [sessionIndex]: bySession };
		}
		let arr = bySession[hopNumber];
		if (!arr)
		{
			arr = [];
			bySession[hopNumber] = arr;
		}
		return arr;
	}

	function _recordSeries(sessionIndex, hopNumber, address, hostname, seriesStartUtc, seriesEndUtc)
	{
		const arr = _ensureHopHistory(sessionIndex, hopNumber);
		const existing = arr.find(s => s.seriesStartUtc === seriesStartUtc && s.address === address);
		if (existing)
		{
			if (hostname && existing.hostname !== hostname) existing.hostname = hostname;
			if (seriesEndUtc != null && existing.seriesEndUtc !== seriesEndUtc) existing.seriesEndUtc = seriesEndUtc;
			return;
		}
		arr.push({ address, hostname: hostname || '', seriesStartUtc, seriesEndUtc: seriesEndUtc || null });
		arr.sort((a, b) => a.seriesStartUtc - b.seriesStartUtc);
		// Trigger reactivity for nested object mutation.
		hopHistory.value = { ...hopHistory.value };
	}

	function connect()
	{
		pingTracerWS.on('connected', () => { connected.value = true; });
		pingTracerWS.on('disconnected', () => { connected.value = false; });

		pingTracerWS.on('configurations', (msg) =>
		{
			configurations.value = msg.configurations;
			if (msg.selectedGuid)
				selectedConfigGuid.value = msg.selectedGuid;
		});

		pingTracerWS.on('configSelected', (msg) => { selectedConfigGuid.value = msg.guid; });
		pingTracerWS.on('configDetails', (msg) => { configDetails.value = msg.config; });
		pingTracerWS.on('configSaved', () => {});
		pingTracerWS.on('configDeleted', () => {});

		pingTracerWS.on('status', (msg) =>
		{
			status.value = msg.status;
			isRunning.value = msg.isRunning;
			successfulPings.value = msg.successfulPings;
			failedPings.value = msg.failedPings;
		});

		pingTracerWS.on('started', () =>
		{
			isRunning.value = true;
			_resetData();
		});

		pingTracerWS.on('stopped', () =>
		{
			isRunning.value = false;
			// Retain sessions/routes/history so the user can still scroll back.
		});

		// --- Binary data-plane events ---

		pingTracerWS.on('sessionTopology', (msg) =>
		{
			sessions.value = msg.sessions;
			const validIndexes = new Set(msg.sessions.map(s => s.index));
			for (const k of Object.keys(routes.value))
				if (!validIndexes.has(parseInt(k, 10))) delete routes.value[k];
			for (const k of Object.keys(hopHistory.value))
				if (!validIndexes.has(parseInt(k, 10))) delete hopHistory.value[k];
			for (const k of Object.keys(liveTail.value))
				if (!validIndexes.has(parseInt(k.split(':')[0], 10))) delete liveTail.value[k];
			for (const k of Object.keys(history.value))
				if (!validIndexes.has(parseInt(k.split(':')[0], 10))) delete history.value[k];
		});

		pingTracerWS.on('routeUpdate', (msg) =>
		{
			routes.value = { ...routes.value, [msg.sessionIndex]: { timestampUtc: msg.timestampUtc, hops: msg.hops } };
			for (const hop of msg.hops)
			{
				if (!hop) continue;
				_recordSeries(msg.sessionIndex, hop.hopNumber, hop.address, hop.hostname, hop.seriesStartUtc, null);
			}
		});

		pingTracerWS.on('hostnameUpdated', (msg) =>
		{
			// Patch every hop entry across all routes whose address matches.
			let changed = false;
			const next = { ...routes.value };
			for (const idx of Object.keys(next))
			{
				const route = next[idx];
				let routeChanged = false;
				const hops = route.hops.map(h =>
				{
					if (h && h.address === msg.address && h.hostname !== msg.hostname)
					{
						routeChanged = true;
						return { ...h, hostname: msg.hostname };
					}
					return h;
				});
				if (routeChanged)
				{
					next[idx] = { ...route, hops };
					changed = true;
				}
			}
			if (changed) routes.value = next;

			// Update hopHistory entries with matching address.
			let historyChanged = false;
			for (const sIdx of Object.keys(hopHistory.value))
			{
				const bySession = hopHistory.value[sIdx];
				for (const hopNum of Object.keys(bySession))
				{
					for (const seg of bySession[hopNum])
					{
						if (seg.address === msg.address && seg.hostname !== msg.hostname)
						{
							seg.hostname = msg.hostname;
							historyChanged = true;
						}
					}
				}
			}
			if (historyChanged) hopHistory.value = { ...hopHistory.value };
		});

		pingTracerWS.on('pingUpdate', (msg) =>
		{
			const k = keyFor(msg.sessionIndex, msg.hopNumber, msg.seriesStartUtc);
			let arr = liveTail.value[k];
			if (!arr)
			{
				arr = [];
				liveTail.value[k] = arr;
			}
			arr.push({ t: msg.t, ms: msg.ms });
			if (arr.length > LIVE_TAIL_CAP)
				arr.splice(0, arr.length - LIVE_TAIL_CAP);

			if (msg.ms === 0xFFFF) failedPings.value++;
			else successfulPings.value++;
		});

		pingTracerWS.on('hopDeactivated', (msg) =>
		{
			const bySession = hopHistory.value[msg.sessionIndex];
			if (!bySession) return;
			const arr = bySession[msg.hopNumber];
			if (!arr) return;
			let changed = false;
			for (const seg of arr)
			{
				if (seg.address === msg.address && seg.seriesEndUtc == null)
				{
					seg.seriesEndUtc = msg.seriesEndUtc;
					changed = true;
				}
			}
			if (changed) hopHistory.value = { ...hopHistory.value };
		});

		pingTracerWS.on('aggregatedData', (msg) =>
		{
			const next = { ...history.value };
			for (const s of msg.series)
			{
				const k = keyFor(msg.sessionIndex, s.hopNumber, s.seriesStartUtc);
				next[k] = {
					points: s.points,
					address: s.address,
					hostname: s.hostname,
					seriesEndUtc: s.seriesEndUtc || null,
				};
				// Backfill hopHistory with metadata for series we discovered via query.
				_recordSeries(msg.sessionIndex, s.hopNumber, s.address, s.hostname, s.seriesStartUtc, s.seriesEndUtc || null);
			}
			history.value = next;
		});

		pingTracerWS.on('log', (msg) =>
		{
			logMessages.value.push(msg.message);
			if (logMessages.value.length > 1000)
				logMessages.value.splice(0, logMessages.value.length - 1000);
		});

		pingTracerWS.on('error', (msg) =>
		{
			errors.value.push(msg.message);
			if (errors.value.length > 100)
				errors.value.splice(0, errors.value.length - 100);
		});

		pingTracerWS.connect();
	}

	function selectConfig(guid) { pingTracerWS.selectConfig(guid); }
	function startPinging() { pingTracerWS.start(); }
	function stopPinging() { pingTracerWS.stop(); }

	function saveConfig(config)
	{
		pingTracerWS.saveConfig(config);
		if (config.guid && config.guid === selectedConfigGuid.value)
			configDetails.value = { ...config };
	}

	function deleteConfig(guid) { pingTracerWS.deleteConfig(guid); }
	function setPingRate(rate, pingsPerSecond) { pingTracerWS.setPingRate(rate, pingsPerSecond); }
	function disconnect() { pingTracerWS.disconnect(); }
	function clearErrors() { errors.value = []; }

	function queryData(sessionIndex, startTimeUtc, endTimeUtc, maxPointsPerHop)
	{
		return pingTracerWS.queryData(sessionIndex, startTimeUtc, endTimeUtc, maxPointsPerHop);
	}

	/**
	 * Fire a debounced queryData for the given viewport. Called every time
	 * the viewport changes; only the last call within the debounce window
	 * actually hits the wire.
	 *
	 * Pass `force=true` to skip the dedupe check (useful for reconnects).
	 */
	function requestViewportData(sessionIndex, startUtc, endUtc, maxPointsPerHop, force = false)
	{
		if (!force)
		{
			const last = _lastQuery[sessionIndex];
			if (last && last.startUtc === startUtc && last.endUtc === endUtc && last.maxPoints === maxPointsPerHop)
				return; // already covered
		}
		_lastQuery[sessionIndex] = { startUtc, endUtc, maxPoints: maxPointsPerHop };

		if (_queryTimers[sessionIndex]) clearTimeout(_queryTimers[sessionIndex]);
		_queryTimers[sessionIndex] = setTimeout(() =>
		{
			delete _queryTimers[sessionIndex];
			pingTracerWS.queryData(sessionIndex, startUtc, endUtc, maxPointsPerHop)
				.catch(err => console.warn('queryData failed:', err.message));
		}, VIEWPORT_QUERY_DEBOUNCE_MS);
	}

	return {
		// State
		connected,
		configurations,
		selectedConfigGuid,
		configDetails,
		isRunning,
		status,
		successfulPings,
		failedPings,
		sessions,
		routes,
		hopHistory,
		liveTail,
		history,
		logMessages,
		errors,
		// Computed
		selectedConfig,
		// Actions
		connect,
		selectConfig,
		startPinging,
		stopPinging,
		saveConfig,
		deleteConfig,
		setPingRate,
		disconnect,
		clearErrors,
		queryData,
		requestViewportData,
	};
});
