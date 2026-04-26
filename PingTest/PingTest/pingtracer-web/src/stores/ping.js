import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import pingTracerWS from '@/library/WebSocketService';

/**
 * Pinia store for the route-aware data plane.
 *
 * `routes` mirrors the server's RouteSnapshot per session. `liveTail` accumulates
 * incoming `pingUpdate` points capped at LIVE_TAIL_CAP per (session, hop, series),
 * keyed `${sessionIndex}:${hopNumber}:${seriesStartUtc}`. Use `keyFor(...)` to
 * compute keys consistently. `history` holds the last queryData response per key.
 */

const LIVE_TAIL_CAP = 5000;

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
	// sessions: [{ index, displayName, targetAddress }]
	const sessions = ref([]);
	// routes: { [sessionIndex]: { timestampUtc, hops: [hopEntry|null] } }
	const routes = ref({});
	// liveTail: { [key]: [{ t, ms }] }
	const liveTail = ref({});
	// history: { [key]: { points: [{ t, min, max, avg, lossPct, samples }], queryWindow: { startTimeUtc, endTimeUtc } } }
	const history = ref({});

	const selectedConfig = computed(() =>
		configurations.value.find(c => c.guid === selectedConfigGuid.value) || null);

	function _resetData()
	{
		sessions.value = [];
		routes.value = {};
		liveTail.value = {};
		history.value = {};
	}

	function _trimDeadKeys(sessionIndex, route)
	{
		// Drop liveTail/history keys that no longer match a present series in this session.
		const valid = new Set();
		for (const hop of route.hops)
			if (hop) valid.add(keyFor(sessionIndex, hop.hopNumber, hop.seriesStartUtc));

		const prefix = `${sessionIndex}:`;
		for (const k of Object.keys(liveTail.value))
		{
			if (k.startsWith(prefix) && !valid.has(k))
				delete liveTail.value[k];
		}
		for (const k of Object.keys(history.value))
		{
			if (k.startsWith(prefix) && !valid.has(k))
				delete history.value[k];
		}
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
			// Drop routes/tails belonging to indexes that no longer exist.
			const validIndexes = new Set(msg.sessions.map(s => s.index));
			for (const k of Object.keys(routes.value))
				if (!validIndexes.has(parseInt(k, 10))) delete routes.value[k];
			for (const k of Object.keys(liveTail.value))
				if (!validIndexes.has(parseInt(k.split(':')[0], 10))) delete liveTail.value[k];
			for (const k of Object.keys(history.value))
				if (!validIndexes.has(parseInt(k.split(':')[0], 10))) delete history.value[k];
		});

		pingTracerWS.on('routeUpdate', (msg) =>
		{
			routes.value = { ...routes.value, [msg.sessionIndex]: { timestampUtc: msg.timestampUtc, hops: msg.hops } };
			_trimDeadKeys(msg.sessionIndex, { hops: msg.hops });
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
			const route = routes.value[msg.sessionIndex];
			if (!route) return;
			// We don't have seriesStartUtc here, so just leave the route as the
			// next routeUpdate will refresh hop state. No-op for now is fine.
		});

		pingTracerWS.on('aggregatedData', (msg) =>
		{
			// Replace history for each returned series.
			const next = { ...history.value };
			for (const s of msg.series)
			{
				const k = keyFor(msg.sessionIndex, s.hopNumber, s.seriesStartUtc);
				next[k] = { points: s.points, address: s.address, hostname: s.hostname, seriesEndUtc: s.seriesEndUtc };
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
	};
});
