<template>
	<div class="dlg-overlay" @mousedown.self="$emit('close')" @keydown.esc="$emit('close')" tabindex="-1" ref="root">
		<div class="dlg history">
			<div class="dlg-header">
				<h3>Saved sessions</h3>
				<button class="dlg-close" @click="$emit('close')">&times;</button>
			</div>
			<div class="dlg-body">
				<p v-if="isRunning" class="note">Stop the current session to open a saved one.</p>
				<div v-if="!history" class="muted">Loading…</div>
				<template v-else>
					<div v-if="history.files.length === 0" class="muted empty">
						No saved sessions yet. Sessions are saved automatically while monitoring (see Settings).
					</div>
					<table v-else class="files">
						<thead>
							<tr><th>Started</th><th>Duration</th><th>Target</th><th>Size</th><th></th></tr>
						</thead>
						<tbody>
							<tr v-for="f in history.files" :key="f.fileName" :class="{ current: f.fileName === openFileName }"
								@dblclick="open(f)">
								<td>{{ formatDateTime(f.startMs) }}</td>
								<td>{{ formatSpan(Math.max(0, f.lastWriteMs - f.startMs)) }}</td>
								<td class="target" :title="f.fileName">
									{{ f.displayName }}
									<span class="mode">{{ f.mode === 'trace' ? 'route' : 'direct' }}</span>
								</td>
								<td class="num">{{ formatBytes(f.sizeBytes) }}</td>
								<td><button class="btn small" :disabled="isRunning" @click="open(f)">Open</button></td>
							</tr>
						</tbody>
					</table>
					<div class="dir muted" :title="history.directory">Folder: {{ history.directory }}</div>
				</template>
			</div>
			<div class="dlg-footer">
				<button class="btn" @click="refresh">Refresh</button>
				<div class="spacer"></div>
				<button class="btn" @click="$emit('close')">Close</button>
			</div>
		</div>
	</div>
</template>

<script>
import { formatSpan, formatDateTime, formatBytes } from '@/library/format';

export default {
	name: 'HistoryDialog',
	props: {
		history: { type: Object, default: null },
		isRunning: Boolean,
		openFileName: { type: String, default: null },
	},
	emits: ['close', 'open', 'refresh'],
	mounted()
	{
		this.$refs.root.focus();
		this.refresh();
	},
	methods: {
		formatSpan, formatDateTime, formatBytes,
		refresh() { this.$emit('refresh'); },
		open(f)
		{
			if (this.isRunning) return;
			this.$emit('open', f.fileName);
		},
	},
};
</script>

<style scoped>
.history { width: 760px; }
.note { color: #ffcf6b; margin-bottom: 10px; }
.empty { padding: 24px 0; text-align: center; }
.files { width: 100%; border-collapse: collapse; font-size: 12.5px; }
.files th { text-align: left; color: #9898aa; font-weight: 500; padding: 4px 6px; border-bottom: 1px solid #2e2e3e; }
.files td { padding: 5px 6px; border-bottom: 1px solid #23232f; }
.files tbody tr:hover { background: #22222e; }
.files tr.current { background: #1f2a3a; }
.target { max-width: 280px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mode { color: #7c7c90; font-size: 11px; margin-left: 4px; }
.num { text-align: right; font-variant-numeric: tabular-nums; }
.btn.small { padding: 2px 10px; font-size: 12px; }
.dir { margin-top: 10px; font-size: 11px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
