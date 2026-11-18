<template>
	<div class="dlg-overlay" @mousedown.self="$emit('close')" @keydown.esc="$emit('close')" tabindex="-1" ref="root">
		<div class="dlg settings">
			<div class="dlg-header">
				<h3>Settings</h3>
				<button class="dlg-close" @click="$emit('close')">&times;</button>
			</div>
			<div class="dlg-body" v-if="form">
				<div class="field">
					<label>Ping timeout</label>
					<input type="number" min="200" max="30000" step="100" v-model.number="form.pingTimeoutMs" /> ms
					<div class="help">A ping with no reply after this long is counted as lost.</div>
				</div>
				<div class="field">
					<label>Maximum hops</label>
					<input type="number" min="1" max="255" v-model.number="form.maxHops" />
					<div class="help">The largest TTL probed while searching for the destination.</div>
				</div>
				<div class="field">
					<label>Ping payload size</label>
					<input type="number" min="0" max="65500" v-model.number="form.payloadBytes" /> bytes
				</div>
				<div class="field">
					<label>Keep data for</label>
					<select v-model.number="form.retentionSeconds">
						<option v-for="o in retentionOptions" :key="o.v" :value="o.v">{{ o.label }}</option>
					</select>
					<div class="help">Older data is discarded from memory while monitoring.</div>
				</div>
				<div class="field">
					<label class="check"><input type="checkbox" v-model="form.saveSessionData" /> Save sessions to disk</label>
					<div class="help">Saved sessions can be reopened from History. Folder: {{ settings.dataDirectory }}</div>
				</div>
				<p class="muted">Changes apply the next time monitoring starts.</p>
			</div>
			<div class="dlg-footer">
				<div class="spacer"></div>
				<button class="btn" @click="$emit('close')">Cancel</button>
				<button class="btn primary" @click="save">Save</button>
			</div>
		</div>
	</div>
</template>

<script>
const DAY = 86400;

export default {
	name: 'SettingsDialog',
	props: {
		settings: { type: Object, default: null },
	},
	emits: ['close', 'save'],
	data()
	{
		return { form: this.settings ? { ...this.settings } : null };
	},
	computed: {
		retentionOptions()
		{
			const opts = [
				{ v: 6 * 3600, label: '6 hours' },
				{ v: DAY, label: '1 day' },
				{ v: 3 * DAY, label: '3 days' },
				{ v: 7 * DAY, label: '1 week' },
				{ v: 30 * DAY, label: '30 days' },
			];
			if (this.form && !opts.some(o => o.v === this.form.retentionSeconds))
				opts.push({ v: this.form.retentionSeconds, label: `${(this.form.retentionSeconds / DAY).toFixed(1)} days` });
			return opts;
		},
	},
	watch: {
		settings(v) { if (v && !this.form) this.form = { ...v }; },
	},
	mounted() { this.$refs.root.focus(); },
	methods: {
		save()
		{
			this.$emit('save', { ...this.form });
		},
	},
};
</script>

<style scoped>
.settings { width: 480px; }
.settings input[type='number'] { width: 100px; }
</style>
