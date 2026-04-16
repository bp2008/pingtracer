<template>
	<div class="toolbar">
		<div class="toolbar-group">
			<select v-model="selectedGuid" @change="onConfigChange" :disabled="isRunning" class="config-select">
				<option value="" disabled>Select configuration...</option>
				<option v-for="cfg in configurations" :key="cfg.guid" :value="cfg.guid">
					{{ cfg.displayName }}
				</option>
			</select>
			<button @click="$emit('editConfig')" class="btn btn-sm" title="Edit configuration">&#9998;</button>
			<button @click="$emit('newConfig')" class="btn btn-sm" title="New configuration">+</button>
		</div>

		<div class="toolbar-group">
			<button v-if="!isRunning" @click="$emit('start')" :disabled="!selectedGuid" class="btn btn-start">
				&#9654; Start
			</button>
			<button v-else @click="$emit('stop')" class="btn btn-stop">
				&#9724; Stop
			</button>
		</div>

		<div class="toolbar-group status-group">
			<span class="status-label" :class="{ 'status-active': isRunning }">{{ status }}</span>
			<span class="ping-counts" v-if="successfulPings > 0 || failedPings > 0">
				<span class="count-success">{{ successfulPings }}</span>
				/
				<span class="count-fail">{{ failedPings }}</span>
			</span>
		</div>

		<div class="toolbar-group toolbar-right">
			<button @click="$emit('showLog')" class="btn btn-sm" title="Show log">Log</button>
			<span class="connection-dot" :class="{ connected: connected }" :title="connected ? 'Connected' : 'Disconnected'"></span>
		</div>
	</div>
</template>

<script>
export default {
	name: 'ToolBar',
	props: {
		configurations: { type: Array, default: () => [] },
		selectedConfigGuid: { type: String, default: null },
		isRunning: { type: Boolean, default: false },
		status: { type: String, default: 'Idle' },
		successfulPings: { type: Number, default: 0 },
		failedPings: { type: Number, default: 0 },
		connected: { type: Boolean, default: false },
	},
	emits: ['selectConfig', 'start', 'stop', 'editConfig', 'newConfig', 'showLog'],
	data()
	{
		return {
			selectedGuid: this.selectedConfigGuid || '',
		};
	},
	watch: {
		selectedConfigGuid(val)
		{
			this.selectedGuid = val || '';
		}
	},
	methods: {
		onConfigChange()
		{
			if (this.selectedGuid)
				this.$emit('selectConfig', this.selectedGuid);
		}
	}
};
</script>

<style scoped>
.toolbar {
	display: flex;
	align-items: center;
	gap: 12px;
	padding: 6px 10px;
	background: #1e1e2e;
	border-bottom: 1px solid #333;
	flex-shrink: 0;
}

.toolbar-group {
	display: flex;
	align-items: center;
	gap: 4px;
}

.toolbar-right {
	margin-left: auto;
}

.config-select {
	background: #2a2a3a;
	color: #e0e0e0;
	border: 1px solid #444;
	border-radius: 3px;
	padding: 4px 8px;
	font-size: 13px;
	min-width: 180px;
}

.btn {
	background: #333348;
	color: #e0e0e0;
	border: 1px solid #555;
	border-radius: 3px;
	padding: 4px 10px;
	cursor: pointer;
	font-size: 13px;
	white-space: nowrap;
}
.btn:hover { background: #444460; }
.btn:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-sm { padding: 4px 6px; }

.btn-start { background: #2a5020; border-color: #3a7030; }
.btn-start:hover { background: #3a6030; }

.btn-stop { background: #702020; border-color: #903030; }
.btn-stop:hover { background: #803030; }

.status-group {
	gap: 8px;
}

.status-label {
	color: #888;
	font-size: 13px;
}
.status-active {
	color: #8f8;
}

.ping-counts {
	font-size: 12px;
	color: #aaa;
}
.count-success { color: #6c6; }
.count-fail { color: #f66; }

.connection-dot {
	display: inline-block;
	width: 8px;
	height: 8px;
	border-radius: 50%;
	background: #666;
}
.connection-dot.connected {
	background: #4c4;
}
</style>
