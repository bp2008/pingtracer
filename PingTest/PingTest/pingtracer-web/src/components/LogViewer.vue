<template>
	<div class="log-dock" :style="{ height: height + 'px' }">
		<div class="resize-handle" @mousedown.prevent="startResize" title="Drag to resize"></div>
		<div class="log-header">
			<span class="title">Event log</span>
			<span class="count">{{ messages.length }} entries</span>
			<div class="spacer"></div>
			<button class="hdr-btn" @click="copyAll" :title="copied ? 'Copied' : 'Copy the log to the clipboard'">{{ copied ? 'Copied' : 'Copy' }}</button>
			<button class="hdr-btn close" @click="$emit('close')" title="Close the log">&times;</button>
		</div>
		<div class="log-body" ref="body" @scroll="onScroll">
			<div v-for="(line, idx) in lines" :key="idx" class="log-line" :class="line.cls"><span
				v-if="line.stamp" class="stamp">{{ line.stamp }}</span>{{ line.text }}</div>
			<div v-if="messages.length === 0" class="log-empty">No log messages yet.</div>
		</div>
	</div>
</template>

<script>
const HEIGHT_KEY = 'pingtracer.logHeight';
const STAMP = /^((?:\d{4}-\d\d-\d\d )?\d\d:\d\d:\d\d) {2}/;

export default {
	name: 'LogViewer',
	props: {
		messages: { type: Array, default: () => [] },
	},
	emits: ['close'],
	data()
	{
		let height = 190;
		try { height = parseInt(localStorage.getItem(HEIGHT_KEY), 10) || height; } catch { /* ignore */ }
		return { height: Math.max(80, Math.min(600, height)), stickToBottom: true, copied: false };
	},
	computed: {
		/** Each line split into its timestamp ("2026-11-22 14:03:05", or a time alone from older servers) and text. */
		lines()
		{
			return this.messages.map((line) =>
			{
				const m = STAMP.exec(line);
				const text = m ? line.slice(m[0].length) : line;
				return { stamp: m ? m[1] : '', text, cls: this.classify(text) };
			});
		},
	},
	watch: {
		messages()
		{
			if (this.stickToBottom) this.$nextTick(this.scrollToBottom);
		},
	},
	mounted() { this.$nextTick(this.scrollToBottom); },
	beforeUnmount()
	{
		document.removeEventListener('mousemove', this.onResize);
		document.removeEventListener('mouseup', this.endResize);
	},
	methods: {
		scrollToBottom()
		{
			const el = this.$refs.body;
			if (el) el.scrollTop = el.scrollHeight;
		},
		onScroll()
		{
			const el = this.$refs.body;
			this.stickToBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 4;
		},
		classify(line)
		{
			if (/lost|failed|unable|error|outage/i.test(line)) return 'bad';
			if (/route|hop \d+ is now|shortened|lengthened/i.test(line)) return 'route';
			if (/resumed|reached/i.test(line)) return 'good';
			return '';
		},
		startResize(e)
		{
			this.resizeStart = { y: e.clientY, h: this.height };
			document.addEventListener('mousemove', this.onResize);
			document.addEventListener('mouseup', this.endResize);
		},
		onResize(e)
		{
			const max = Math.max(120, window.innerHeight - 200);
			this.height = Math.max(80, Math.min(max, this.resizeStart.h + (this.resizeStart.y - e.clientY)));
		},
		endResize()
		{
			document.removeEventListener('mousemove', this.onResize);
			document.removeEventListener('mouseup', this.endResize);
			try { localStorage.setItem(HEIGHT_KEY, String(this.height)); } catch { /* ignore */ }
		},
		async copyAll()
		{
			try
			{
				await navigator.clipboard.writeText(this.messages.join('\n'));
				this.copied = true;
				setTimeout(() => { this.copied = false; }, 1500);
			}
			catch { /* clipboard unavailable */ }
		},
	},
};
</script>

<style scoped>
.log-dock {
	position: relative;
	flex-shrink: 0;
	display: flex;
	flex-direction: column;
	background: #111118;
	border-top: 1px solid #2c2c3a;
}
.resize-handle {
	position: absolute;
	top: -3px;
	left: 0;
	right: 0;
	height: 6px;
	cursor: ns-resize;
	z-index: 2;
}
.log-header {
	display: flex;
	align-items: center;
	gap: 10px;
	padding: 3px 8px 3px 12px;
	background: #17171f;
	border-bottom: 1px solid #23232f;
	font-size: 12px;
}
.title { font-weight: 600; color: #d0d0dc; }
.count { color: #6c6c7c; }
.spacer { flex: 1; }
.hdr-btn {
	background: none;
	border: 1px solid transparent;
	color: #a0a0b0;
	cursor: pointer;
	font-size: 12px;
	padding: 1px 6px;
	border-radius: 3px;
}
.hdr-btn:hover { border-color: #44445a; color: #fff; }
.hdr-btn.close { font-size: 16px; line-height: 1; }
.log-body {
	flex: 1;
	overflow-y: auto;
	padding: 4px 12px;
	font-family: Consolas, 'Courier New', monospace;
	font-size: 12px;
	color: #c8c8d0;
}
.log-line { padding: 1px 0 1px 21ch; text-indent: -21ch; white-space: pre-wrap; word-break: break-word; }
.stamp { display: inline-block; width: 21ch; text-indent: 0; color: #6c6c7c; }
.log-line.bad { color: #ff9b9b; }
.log-line.route { color: #7fd6ff; }
.log-line.good { color: #9fe49f; }
.log-empty { color: #666; padding: 12px 0; }
</style>
