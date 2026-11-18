import { defineStore } from 'pinia';
import { ref, computed, shallowRef } from 'vue';
import socket from '@/library/WebSocketService';
import dataStore from '@/library/DataStore';

/** How long an error notice stays on screen. */
const ERROR_TOAST_MS = 8000;

/**
 * Reactive control-plane state: connection, configurations, run status, logs, and light session metadata.
 * Ping data itself lives in the non-reactive DataStore.
 */
export const usePingStore = defineStore('ping', () =>
{
	const connected = ref(false);
	const configurations = ref([]);
	const selectedConfigGuid = ref(null);
	const configDetails = ref(null);
	const isRunning = ref(false);
	const status = ref('Idle');
	const sentCount = ref(0);
	const lostCount = ref(0);
	const logMessages = shallowRef([]);
	const errors = ref([]);
	const settings = ref(null);
	const history = ref(null);
	const view = ref({ source: 'live', fileName: null });
	/** Light, reactive copy of session metadata for the UI chrome (not used for rendering graphs). */
	const sessions = ref([]);

	const selectedConfig = computed(() => configurations.value.find(c => c.guid === selectedConfigGuid.value) || null);

	let wired = false;
	function connect()
	{
		if (!wired)
		{
			wired = true;
			socket.on('connected', () => { connected.value = true; });
			socket.on('disconnected', () => { connected.value = false; });
			socket.on('configurations', (m) =>
			{
				configurations.value = m.configurations;
				selectedConfigGuid.value = m.selectedGuid || null;
			});
			socket.on('configSelected', (m) => { selectedConfigGuid.value = m.guid; });
			socket.on('configDetails', (m) => { configDetails.value = m.config; });
			socket.on('configDeleted', (m) =>
			{
				if (configDetails.value && configDetails.value.guid === m.guid) configDetails.value = null;
			});
			socket.on('status', (m) =>
			{
				status.value = m.status;
				isRunning.value = m.isRunning;
				sentCount.value = m.sent;
				lostCount.value = m.lost;
			});
			socket.on('settings', (m) => { settings.value = m; });
			socket.on('history', (m) => { history.value = m; });
			socket.on('view', (m) => { view.value = { source: m.source, fileName: m.fileName }; sessions.value = []; });
			socket.on('session', (m) =>
			{
				const list = sessions.value.slice();
				const i = list.findIndex(s => s.id === m.id);
				if (i >= 0) list[i] = m; else list.push(m);
				sessions.value = list;
			});
			socket.on('logHistory', (m) => { logMessages.value = m.messages.slice(); });
			socket.on('log', (m) =>
			{
				const arr = logMessages.value.length >= 1000 ? logMessages.value.slice(-900) : logMessages.value.slice();
				arr.push(m.message);
				logMessages.value = arr;
			});
			socket.on('error', (m) =>
			{
				const id = Date.now() + Math.random();
				errors.value = [...errors.value.slice(-20), { id, message: m.message }];
				// Error notices go away on their own (or when clicked); the event log keeps a record of problems.
				setTimeout(() => dismissError(id), ERROR_TOAST_MS);
			});
			// Make sure the data store is instantiated so it hears session messages.
			void dataStore;
		}
		socket.connect();
	}

	function disconnect() { socket.disconnect(); }
	function selectConfig(guid) { socket.send({ action: 'selectConfig', guid }); }
	function start() { socket.send({ action: 'start' }); }
	function stop()
	{
		status.value = 'Stopping...';
		socket.send({ action: 'stop' });
	}
	function saveConfig(config) { socket.send({ action: 'saveConfig', config }); }
	function deleteConfig(guid) { socket.send({ action: 'deleteConfig', guid }); }
	function setPingRate(rate, pingsPerSecond) { socket.send({ action: 'setPingRate', rate, pingsPerSecond }); }
	function saveSettings(s) { socket.send({ action: 'saveSettings', settings: s }); }
	function listHistory() { socket.send({ action: 'listHistory' }); }
	function openHistory(fileName) { socket.send({ action: 'openHistory', fileName }); }
	function closeHistory() { socket.send({ action: 'closeHistory' }); }
	function dismissError(id) { errors.value = errors.value.filter(e => e.id !== id); }

	return {
		connected, configurations, selectedConfigGuid, configDetails, isRunning, status, sentCount, lostCount,
		logMessages, errors, settings, history, view, sessions, selectedConfig,
		connect, disconnect, selectConfig, start, stop, saveConfig, deleteConfig, setPingRate, saveSettings,
		listHistory, openHistory, closeHistory, dismissError,
	};
});
