<template>
	<div class="dlg-overlay" @mousedown.self="onOutsideClick" @keydown.esc="requestClose" @keydown.enter="onEnter"
		tabindex="-1" ref="root">
		<div class="dlg settings" role="dialog" aria-modal="true" aria-label="Settings">
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
					<label class="check"><input type="checkbox" v-model="form.saveSessionData" /> Save graph data to disk</label>
					<div class="help">Each configuration's graphs are saved (compressed) and reloaded when it is selected, so they continue
						after PingTracer is closed. How much is kept is set in each configuration. Folder: {{ settings.dataDirectory }}</div>
				</div>
				<p class="muted">Changes apply the next time monitoring starts.</p>
			</div>
			<div v-if="error" class="form-error" role="alert">{{ error }}</div>
			<div class="dlg-footer">
				<div class="spacer"></div>
				<button class="btn" @click="$emit('close')">Cancel</button>
				<button class="btn primary" @click="save">Save</button>
			</div>
		</div>
	</div>
</template>

<script>
export default {
	name: 'SettingsDialog',
	props: {
		settings: { type: Object, default: null },
	},
	emits: ['close', 'save'],
	data()
	{
		return { form: this.settings ? { ...this.settings } : null, error: '' };
	},
	watch: {
		settings(v)
		{
			if (v && !this.form)
			{
				this.form = { ...v };
				this.initialState = JSON.stringify(this.form);
			}
		},
	},
	mounted()
	{
		this.initialState = JSON.stringify(this.form);
		this.$refs.root.focus();
	},
	methods: {
		save()
		{
			const f = this.form;
			const inRange = (v, lo, hi) => Number.isInteger(v) && v >= lo && v <= hi;
			if (!inRange(f.pingTimeoutMs, 200, 30000))
				this.error = 'The ping timeout must be a whole number of milliseconds from 200 to 30000.';
			else if (!inRange(f.maxHops, 1, 255))
				this.error = 'Maximum hops must be a whole number from 1 to 255.';
			else if (!inRange(f.payloadBytes, 0, 65500))
				this.error = 'The payload size must be a whole number of bytes from 0 to 65500.';
			else
			{
				this.error = '';
				this.$emit('save', { ...f });
			}
		},
		isDirty() { return JSON.stringify(this.form) !== this.initialState; },
		requestClose()
		{
			if (!this.isDirty() || confirm('Discard your changes to the settings?'))
				this.$emit('close');
		},
		onOutsideClick()
		{
			if (!this.isDirty()) this.$emit('close');
		},
		onEnter(e)
		{
			if (e.target.tagName === 'INPUT' && e.target.type !== 'checkbox')
			{
				e.preventDefault();
				this.save();
			}
		},
	},
};
</script>

<style scoped>
.settings { width: 480px; }
.form-error {
	margin: 0 16px 10px;
	padding: 6px 10px;
	border-radius: 4px;
	background: #3a1414;
	border: 1px solid #7a2a2a;
	color: #ffb4b4;
	font-size: 13px;
}
.settings input[type='number'] { width: 100px; }
</style>
