import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import pingTracerWS from '@/library/WebSocketService';

export const usePingStore = defineStore('ping', () =>
{
	// --- State ---
	const connected = ref(false);
	const configurations = ref([]);
	const selectedConfigGuid = ref(null);
	const configDetails = ref(null);
	const isRunning = ref(false);
	const status = ref('Idle');
	const successfulPings = ref(0);
	const failedPings = ref(0);
	const targets = ref([]);
	const logMessages = ref([]);
	const errors = ref([]);

	// Each target's ping data: { [targetId]: PingLog[] }
	const pingData = ref({});

	// --- Computed ---
	const selectedConfig = computed(() =>
	{
		return configurations.value.find(c => c.guid === selectedConfigGuid.value) || null;
	});

	// --- Actions ---
	function connect()
	{
		pingTracerWS.on('connected', () =>
		{
			connected.value = true;
		});

		pingTracerWS.on('disconnected', () =>
		{
			connected.value = false;
		});

		pingTracerWS.on('configurations', (msg) =>
		{
			configurations.value = msg.configurations;
			if (msg.selectedGuid)
				selectedConfigGuid.value = msg.selectedGuid;
		});

		pingTracerWS.on('configSelected', (msg) =>
		{
			selectedConfigGuid.value = msg.guid;
		});

		pingTracerWS.on('configDetails', (msg) =>
		{
			configDetails.value = msg.config;
		});

		pingTracerWS.on('configSaved', () =>
		{
			// Configurations list will be updated via the 'configurations' message
		});

		pingTracerWS.on('configDeleted', () =>
		{
			// Configurations list will be updated via the 'configurations' message
		});

		pingTracerWS.on('status', (msg) =>
		{
			status.value = msg.status;
			isRunning.value = msg.isRunning;
			successfulPings.value = msg.successfulPings;
			failedPings.value = msg.failedPings;
		});

		pingTracerWS.on('started', (msg) =>
		{
			isRunning.value = true;
			targets.value = [];
			pingData.value = {};
		});

		pingTracerWS.on('stopped', () =>
		{
			isRunning.value = false;
		});

		pingTracerWS.on('targetAdded', (msg) =>
		{
			targets.value = [...targets.value, {
				id: msg.id,
				displayName: msg.displayName,
				address: msg.address
			}];
			pingData.value[msg.id] = [];
		});

		pingTracerWS.on('targetRemoved', (msg) =>
		{
			targets.value = targets.value.filter(t => t.id !== msg.id);
			delete pingData.value[msg.id];
		});

		pingTracerWS.on('ping', (msg) =>
		{
			if (!pingData.value[msg.targetId])
				pingData.value[msg.targetId] = [];
			pingData.value[msg.targetId].push({
				t: msg.t,
				ms: msg.ms,
				s: msg.s
			});

			// Update counters
			if (msg.s === 0) // IPStatus.Success = 0
				successfulPings.value++;
			else
				failedPings.value++;
		});

		pingTracerWS.on('pingBulk', (msg) =>
		{
			if (!pingData.value[msg.targetId])
				pingData.value[msg.targetId] = [];
			// Prepend bulk data (since it's historical)
			const existing = pingData.value[msg.targetId];
			pingData.value[msg.targetId] = [...msg.pings.filter(p => p !== null), ...existing];
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

	function selectConfig(guid)
	{
		pingTracerWS.selectConfig(guid);
	}

	function startPinging()
	{
		pingTracerWS.start();
	}

	function stopPinging()
	{
		pingTracerWS.stop();
	}

	function saveConfig(config)
	{
		pingTracerWS.saveConfig(config);
	}

	function deleteConfig(guid)
	{
		pingTracerWS.deleteConfig(guid);
	}

	function clearErrors()
	{
		errors.value = [];
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
		targets,
		pingData,
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
		clearErrors,
	};
});
