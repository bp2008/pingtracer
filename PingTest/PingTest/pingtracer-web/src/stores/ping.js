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

	// Rolling buffer size from server
	const cacheSize = ref(360000);

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
			if (msg.cacheSize)
				cacheSize.value = msg.cacheSize;
		});

		pingTracerWS.on('started', (msg) =>
		{
			isRunning.value = true;
			targets.value = [];
			pingData.value = {};

			// --- TEMPORARY: generate 1 hour of simulated ping data at 10 pings/sec ---
			const SIM_DURATION_SEC = 3600;
			const SIM_RATE = 10; // pings per second
			const SIM_TOTAL = SIM_DURATION_SEC * SIM_RATE;
			const SIM_TARGET_ID = '__sim__';

			const simData = new Array(SIM_TOTAL);
			const baseTime = Date.now() - SIM_DURATION_SEC * 1000;
			const msPerPing = 1000 / SIM_RATE;

			// State machine for realistic ping simulation
			let baseLatency = 15 + Math.random() * 10; // 15-25ms base
			let jitter = 0;
			let inBurst = false;
			let burstRemaining = 0;
			let burstSeverity = 0;

			for (let i = 0; i < SIM_TOTAL; i++)
			{
				const t = baseTime + i * msPerPing;

				// Slowly drift baseline
				if (i % 1000 === 0)
					baseLatency = 12 + Math.random() * 20;

				// Start a burst of outliers occasionally (~0.3% chance per ping)
				if (!inBurst && Math.random() < 0.003)
				{
					inBurst = true;
					burstRemaining = 1 + Math.floor(Math.random() * 5); // 1-5 adjacent outliers
					burstSeverity = Math.random(); // 0-1 controls how bad
				}

				if (inBurst)
				{
					burstRemaining--;
					if (burstRemaining <= 0) inBurst = false;

					// Packet loss in severe bursts (~30% of burst pings when severity > 0.7)
					if (burstSeverity > 0.7 && Math.random() < 0.3)
					{
						simData[i] = { t, ms: 0, s: 11010 }; // TimedOut
						continue;
					}

					// Spike: 80-800ms depending on severity
					const spike = 80 + burstSeverity * 700 + Math.random() * 50;
					simData[i] = { t, ms: Math.round(spike), s: 0 };
					continue;
				}

				// Normal ping with small jitter (Gaussian-ish via sum of randoms)
				jitter = (Math.random() + Math.random() + Math.random() - 1.5) * 4;
				const ms = Math.max(1, Math.round(baseLatency + jitter));
				simData[i] = { t, ms, s: 0 };
			}

			// Inject simulated target
			const simExisting = targets.value.find(t => t.id === SIM_TARGET_ID);
			if (!simExisting)
			{
				targets.value = [...targets.value, {
					id: SIM_TARGET_ID,
					displayName: 'Simulated Data (1hr @ 10/sec)',
					address: '127.0.0.1'
				}];
			}
			pingData.value[SIM_TARGET_ID] = simData;
			// --- END TEMPORARY ---
		});

		pingTracerWS.on('stopped', () =>
		{
			isRunning.value = false;
		});

		pingTracerWS.on('targetAdded', (msg) =>
		{
			// Deduplicate: on reconnect, server resends all targets
			const existing = targets.value.find(t => t.id === msg.id);
			if (existing)
			{
				existing.displayName = msg.displayName;
				existing.address = msg.address;
			}
			else
			{
				targets.value = [...targets.value, {
					id: msg.id,
					displayName: msg.displayName,
					address: msg.address
				}];
			}
			if (!pingData.value[msg.id])
				pingData.value[msg.id] = [];
			if (msg.cacheSize)
				cacheSize.value = msg.cacheSize;
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
		// Update configDetails locally so the editor reflects changes immediately
		if (config.guid && config.guid === selectedConfigGuid.value)
			configDetails.value = { ...config };
	}

	function deleteConfig(guid)
	{
		pingTracerWS.deleteConfig(guid);
	}

	function setPingRate(rate, pingsPerSecond)
	{
		pingTracerWS.setPingRate(rate, pingsPerSecond);
	}

	function disconnect()
	{
		pingTracerWS.disconnect();
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
		cacheSize,
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
	};
});
