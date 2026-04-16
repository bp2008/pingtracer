<template>
	<div class="app-container" @keydown="onKeyDown" tabindex="0">
		<ToolBar :configurations="store.configurations" :selectedConfigGuid="store.selectedConfigGuid"
			:isRunning="store.isRunning" :status="store.status" :successfulPings="store.successfulPings"
			:failedPings="store.failedPings" :connected="store.connected" @selectConfig="store.selectConfig"
			@start="store.startPinging" @stop="store.stopPinging" @editConfig="editCurrentConfig"
			@newConfig="newConfig" @showLog="showLog = true" />

		<div class="graph-area" ref="graphArea">
			<template v-if="store.targets.length > 0">
				<div v-for="(target, idx) in store.targets" :key="target.id" class="graph-container"
					:style="graphStyle(idx)">
					<PingGraph ref="graphs" :pings="store.pingData[target.id] || []"
						:displayName="target.displayName"
						:showTimestamps="idx === store.targets.length - 1" :config="store.configDetails" />
				</div>
			</template>
			<div v-else class="no-graphs">
				<div class="no-graphs-content">
					<p v-if="!store.selectedConfigGuid">Select or create a configuration to begin.</p>
					<p v-else-if="!store.isRunning">Press <strong>Start</strong> to begin pinging.</p>
					<p v-else>Waiting for targets...</p>
				</div>
			</div>
		</div>

		<ConfigEditor v-if="showConfigEditor" :config="editingConfig" @save="onConfigSave" @delete="onConfigDelete"
			@close="showConfigEditor = false" />

		<LogViewer v-if="showLog" :messages="store.logMessages" @close="showLog = false" />

		<div class="error-toast" v-if="store.errors.length > 0" @click="store.clearErrors">
			{{ store.errors[store.errors.length - 1] }}
		</div>
	</div>
</template>

<script>
import ToolBar from '@/components/ToolBar.vue';
import PingGraph from '@/components/PingGraph.vue';
import ConfigEditor from '@/components/ConfigEditor.vue';
import LogViewer from '@/components/LogViewer.vue';
import { usePingStore } from '@/stores/ping';

export default {
	name: 'App',
	components: { ToolBar, PingGraph, ConfigEditor, LogViewer },
	setup()
	{
		const store = usePingStore();
		return { store };
	},
	data()
	{
		return {
			showConfigEditor: false,
			showLog: false,
			editingConfig: null,
		};
	},
	mounted()
	{
		this.store.connect();
	},
	methods: {
		graphStyle(index)
		{
			const count = this.store.targets.length;
			if (count === 0) return {};
			const pct = 100 / count;
			return {
				height: pct + '%',
				borderBottom: index < count - 1 ? '1px solid #555' : 'none',
			};
		},

		editCurrentConfig()
		{
			this.editingConfig = this.store.configDetails ? { ...this.store.configDetails } : null;
			this.showConfigEditor = true;
		},

		newConfig()
		{
			this.editingConfig = null;
			this.showConfigEditor = true;
		},

		onConfigSave(config)
		{
			this.store.saveConfig(config);
			this.showConfigEditor = false;
		},

		onConfigDelete(guid)
		{
			this.store.deleteConfig(guid);
			this.showConfigEditor = false;
		},

		onKeyDown(e)
		{
			const graphs = this.$refs.graphs;
			if (!graphs || graphs.length === 0) return;

			switch (e.key)
			{
				case 'Home':
				case '9':
					graphs.forEach(g => g.scrollToOldest());
					e.preventDefault();
					break;
				case 'End':
				case '0':
					graphs.forEach(g => g.scrollToLive());
					e.preventDefault();
					break;
				case 'PageUp':
				case '-':
					graphs.forEach(g => g.scrollPage(1));
					e.preventDefault();
					break;
				case 'PageDown':
				case '=':
					graphs.forEach(g => g.scrollPage(-1));
					e.preventDefault();
					break;
			}
		}
	}
};
</script>

<style>
*,
*::before,
*::after {
	box-sizing: border-box;
	margin: 0;
	padding: 0;
}

html,
body {
	height: 100%;
	overflow: hidden;
	background: #000;
	color: #e0e0e0;
	font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

#app {
	height: 100%;
}
</style>

<style scoped>
.app-container {
	display: flex;
	flex-direction: column;
	height: 100%;
	outline: none;
}

.graph-area {
	flex: 1;
	display: flex;
	flex-direction: column;
	overflow: hidden;
	background: #000;
}

.graph-container {
	flex-shrink: 0;
}

.no-graphs {
	flex: 1;
	display: flex;
	align-items: center;
	justify-content: center;
}

.no-graphs-content {
	text-align: center;
	color: #666;
	font-size: 16px;
}

.no-graphs-content strong {
	color: #888;
}

.error-toast {
	position: fixed;
	bottom: 20px;
	right: 20px;
	background: #702020;
	color: #fcc;
	padding: 10px 16px;
	border-radius: 4px;
	font-size: 13px;
	cursor: pointer;
	z-index: 2000;
	max-width: 400px;
}
</style>
