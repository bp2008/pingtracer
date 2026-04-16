<template>
	<div class="modal-overlay" @mousedown.self="$emit('close')">
		<div class="log-panel">
			<div class="log-header">
				<h3>Output Log</h3>
				<button class="btn-close" @click="$emit('close')">&times;</button>
			</div>
			<div class="log-body" ref="logBody">
				<div v-for="(line, idx) in messages" :key="idx" class="log-line">{{ line }}</div>
				<div v-if="messages.length === 0" class="log-empty">No log messages yet.</div>
			</div>
		</div>
	</div>
</template>

<script>
export default {
	name: 'LogViewer',
	props: {
		messages: { type: Array, default: () => [] }
	},
	emits: ['close'],
	watch: {
		messages()
		{
			this.$nextTick(() =>
			{
				const el = this.$refs.logBody;
				if (el) el.scrollTop = el.scrollHeight;
			});
		}
	},
	mounted()
	{
		this.$nextTick(() =>
		{
			const el = this.$refs.logBody;
			if (el) el.scrollTop = el.scrollHeight;
		});
	}
};
</script>

<style scoped>
.modal-overlay {
	position: fixed;
	top: 0;
	left: 0;
	right: 0;
	bottom: 0;
	background: rgba(0, 0, 0, 0.5);
	display: flex;
	align-items: center;
	justify-content: center;
	z-index: 1000;
}

.log-panel {
	background: #1a1a2a;
	border: 1px solid #444;
	border-radius: 6px;
	width: 700px;
	max-width: 95vw;
	height: 500px;
	max-height: 80vh;
	display: flex;
	flex-direction: column;
	color: #e0e0e0;
}

.log-header {
	display: flex;
	justify-content: space-between;
	align-items: center;
	padding: 10px 16px;
	border-bottom: 1px solid #333;
}

.log-header h3 {
	margin: 0;
	font-size: 15px;
}

.btn-close {
	background: none;
	border: none;
	color: #aaa;
	font-size: 20px;
	cursor: pointer;
}

.btn-close:hover {
	color: #fff;
}

.log-body {
	flex: 1;
	overflow-y: auto;
	padding: 8px 12px;
	font-family: 'Consolas', 'Courier New', monospace;
	font-size: 12px;
}

.log-line {
	padding: 1px 0;
	white-space: pre-wrap;
	word-break: break-all;
}

.log-empty {
	color: #666;
	text-align: center;
	padding: 20px;
}
</style>
