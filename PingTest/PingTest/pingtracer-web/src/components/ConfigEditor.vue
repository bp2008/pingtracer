<template>
	<div class="modal-overlay" @mousedown.self="$emit('close')" @keydown.esc="$emit('close')">
		<div class="modal-content">
			<div class="modal-header">
				<h3>{{ isNew ? 'New Configuration' : 'Edit Configuration' }}</h3>
				<button class="btn-close" @click="$emit('close')">&times;</button>
			</div>

			<div class="modal-body">
				<div class="form-group">
					<label>Display Name</label>
					<input v-model="form.displayName" type="text" class="form-input" placeholder="e.g. Google DNS" />
				</div>

				<div class="form-group">
					<label>Hosts <span class="hint">(separated by commas, spaces or new lines; one host traces the route, several hosts are pinged directly. Enter <code>sim:demo</code> for a simulated network.)</span></label>
					<textarea v-model="hostsText" class="form-input form-textarea" rows="3"
						placeholder="8.8.8.8, google.com" ref="hosts"></textarea>
				</div>

				<div class="form-row">
					<div class="form-group">
						<label>Ping Rate</label>
						<div class="rate-control">
							<input v-model.number="form.rate" type="range" min="1" max="10" step="1" />
							<span class="rate-label">{{ rateLabel }}</span>
						</div>
					</div>
					<div class="form-group">
						<label>&nbsp;</label>
						<label class="checkbox-label">
							<input type="checkbox" v-model="form.pingsPerSecond" />
							Pings per second
						</label>
					</div>
				</div>

				<div class="form-row">
					<label class="checkbox-label"><input type="checkbox" v-model="form.doTraceRoute" /> Trace Route</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.reverseDnsLookup" /> Reverse DNS</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.preferIPv4" /> Prefer IPv4</label>
					<label class="checkbox-label" title="Hops that never answer are normally drawn as thin rows. Check this to draw them at full size."><input type="checkbox" v-model="form.monitorUnresponsiveHops" /> Full-size rows for silent hops</label>
				</div>

				<h4>Graph Display</h4>
				<div class="form-row">
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawServerNames" /> Server Names</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawLastPing" /> Last Ping</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawAverage" /> Average</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawJitter" /> Jitter</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawMinMax" /> Min/Max</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawPacketLoss" /> Packet Loss</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.drawLimitText" /> Limit Text</label>
				</div>

				<div class="form-row">
					<div class="form-group form-group-narrow">
						<label>Bad Threshold (ms)</label>
						<input v-model.number="form.badThreshold" type="number" class="form-input" min="0" />
					</div>
					<div class="form-group form-group-narrow">
						<label>Worse Threshold (ms)</label>
						<input v-model.number="form.worseThreshold" type="number" class="form-input" min="0" />
					</div>
					<div class="form-group form-group-narrow">
						<label>Upper Limit (ms)</label>
						<input v-model.number="form.upperLimit" type="number" class="form-input" min="0" />
					</div>
					<div class="form-group form-group-narrow">
						<label>Lower Limit (ms)</label>
						<input v-model.number="form.lowerLimit" type="number" class="form-input" min="0" />
					</div>
				</div>

				<div class="form-group">
					<label>Y-Axis Scaling Method</label>
					<select v-model.number="form.scalingMethodID" class="form-input">
						<option :value="0">Classic (1px = 1ms, zoom out if needed)</option>
						<option :value="1">Zoom (fit data, respect limits)</option>
						<option :value="2">Zoom Unlimited (fit data freely)</option>
						<option :value="3">Fixed (exact range, no zoom)</option>
					</select>
				</div>

				<div class="form-row">
					<label class="checkbox-label"><input type="checkbox" v-model="form.logFailures" /> Log Failures</label>
					<label class="checkbox-label"><input type="checkbox" v-model="form.logSuccesses" /> Log Successes</label>
				</div>
			</div>

			<div v-if="error" class="form-error" role="alert">{{ error }}</div>
			<div class="modal-footer">
				<button v-if="!isNew" class="btn btn-danger" @click="onDelete">Delete</button>
				<div class="spacer"></div>
				<button class="btn" @click="$emit('close')">Cancel</button>
				<button class="btn btn-primary" @click="onSave">Save</button>
			</div>
		</div>
	</div>
</template>

<script>
export default {
	name: 'ConfigEditor',
	props: {
		config: { type: Object, default: null },
	},
	emits: ['save', 'delete', 'close', 'preview'],
	data()
	{
		const defaults = {
			guid: '',
			displayName: '',
			hosts: [],
			rate: 1,
			pingsPerSecond: true,
			doTraceRoute: true,
			reverseDnsLookup: true,
			preferIPv4: true,
			monitorUnresponsiveHops: false,
			drawServerNames: true,
			drawLastPing: true,
			drawAverage: true,
			drawJitter: false,
			drawMinMax: false,
			drawPacketLoss: true,
			drawLimitText: false,
			badThreshold: 100,
			worseThreshold: 200,
			upperLimit: 300,
			lowerLimit: 0,
			scalingMethodID: 0,
			logFailures: true,
			logSuccesses: false,
		};

		const form = this.config ? { ...defaults, ...this.config } : { ...defaults };

		return {
			form,
			hostsText: (form.hosts || []).join(', '),
			error: '',
		};
	},
	computed: {
		isNew() { return !this.form.guid; },
		rateLabel()
		{
			if (this.form.pingsPerSecond)
				return this.form.rate + '/sec';
			return 'every ' + this.form.rate + 's';
		}
	},
	watch: {
		form: {
			deep: true,
			handler(val)
			{
				this.$emit('preview', { ...val });
			}
		}
	},
	mounted()
	{
		this.$nextTick(() => this.$refs.hosts && this.$refs.hosts.focus());
	},
	methods: {
		onSave()
		{
			// Hosts may be separated by commas, spaces or new lines (the server splits them the same way).
			const hosts = this.hostsText
				.split(/[,\s]+/)
				.map(s => s.trim())
				.filter(s => s.length > 0);
			const f = this.form;
			const isNum = (v) => typeof v === 'number' && Number.isFinite(v) && v >= 0;
			if (hosts.length === 0)
				return this.fail('Enter at least one host to ping.', 'hosts');
			if (![f.badThreshold, f.worseThreshold, f.upperLimit, f.lowerLimit].every(isNum))
				return this.fail('Thresholds and limits must be whole numbers of milliseconds, 0 or more.');
			if (f.upperLimit <= f.lowerLimit)
				return this.fail('The upper limit must be greater than the lower limit.');
			this.error = '';
			this.$emit('save', { ...f, hosts });
		},
		fail(message, focusRef)
		{
			this.error = message;
			if (focusRef && this.$refs[focusRef]) this.$refs[focusRef].focus();
		},
		onDelete()
		{
			if (confirm('Delete configuration "' + this.form.displayName + '"?'))
				this.$emit('delete', this.form.guid);
		}
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
	background: rgba(0, 0, 0, 0.6);
	display: flex;
	align-items: center;
	justify-content: center;
	z-index: 1000;
}

.modal-content {
	background: #1e1e2e;
	border: 1px solid #444;
	border-radius: 6px;
	width: 600px;
	max-width: 95vw;
	max-height: 90vh;
	display: flex;
	flex-direction: column;
	color: #e0e0e0;
}

.modal-header {
	display: flex;
	justify-content: space-between;
	align-items: center;
	padding: 12px 16px;
	border-bottom: 1px solid #333;
}

.modal-header h3 {
	margin: 0;
	font-size: 16px;
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

.modal-body {
	padding: 16px;
	overflow-y: auto;
}

.modal-body h4 {
	margin: 12px 0 6px;
	font-size: 13px;
	color: #aaa;
	border-bottom: 1px solid #333;
	padding-bottom: 4px;
}

.modal-footer {
	display: flex;
	gap: 8px;
	padding: 12px 16px;
	border-top: 1px solid #333;
}

.spacer {
	flex: 1;
}

.form-error {
	margin: 0 16px 10px;
	padding: 6px 10px;
	border-radius: 4px;
	background: #3a1414;
	border: 1px solid #7a2a2a;
	color: #ffb4b4;
	font-size: 13px;
}

.form-group {
	margin-bottom: 10px;
}

.form-group label {
	display: block;
	font-size: 12px;
	color: #aaa;
	margin-bottom: 3px;
}

.form-group-narrow {
	flex: 1;
	min-width: 100px;
}

.form-input {
	width: 100%;
	background: #2a2a3a;
	color: #e0e0e0;
	border: 1px solid #444;
	border-radius: 3px;
	padding: 5px 8px;
	font-size: 13px;
	box-sizing: border-box;
}

.form-textarea {
	resize: vertical;
	font-family: monospace;
}

.form-row {
	display: flex;
	flex-wrap: wrap;
	gap: 10px;
	margin-bottom: 10px;
}

.checkbox-label {
	display: flex;
	align-items: center;
	gap: 4px;
	font-size: 13px;
	cursor: pointer;
	white-space: nowrap;
}

.rate-control {
	display: flex;
	align-items: center;
	gap: 8px;
}

.rate-label {
	font-size: 13px;
	min-width: 60px;
}

.hint {
	font-size: 11px;
	opacity: 0.65;
}

.hint code {
	background: #2a2a3a;
	padding: 0 3px;
	border-radius: 2px;
}

.btn {
	background: #333348;
	color: #e0e0e0;
	border: 1px solid #555;
	border-radius: 3px;
	padding: 6px 14px;
	cursor: pointer;
	font-size: 13px;
}

.btn:hover {
	background: #444460;
}

.btn-primary {
	background: #2a5a9a;
	border-color: #3a6aba;
}

.btn-primary:hover {
	background: #3a6aba;
}

.btn-danger {
	background: #702020;
	border-color: #903030;
}

.btn-danger:hover {
	background: #903030;
}
</style>
