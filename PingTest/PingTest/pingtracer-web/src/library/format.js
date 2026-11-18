/** Formats a duration in milliseconds compactly, e.g. "250 ms", "1.5 s", "12 min", "3.2 h", "4 days". */
export function formatSpan(ms)
{
	if (ms < 1000) return Math.round(ms) + ' ms';
	const s = ms / 1000;
	if (s < 60) return trim(s) + ' s';
	const m = s / 60;
	if (m < 60) return trim(m) + ' min';
	const h = m / 60;
	if (h < 48) return trim(h) + ' h';
	return trim(h / 24) + ' days';
}

function trim(v)
{
	return v >= 10 || Number.isInteger(v) ? String(Math.round(v)) : v.toFixed(1).replace(/\.0$/, '');
}

export function formatClock(ms, withMs = false)
{
	const d = new Date(ms);
	let s = d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
	if (withMs) s = s.replace(/(\d+:\d+:\d+)/, `$1.${String(d.getMilliseconds()).padStart(3, '0')}`);
	return s;
}

/** Formats the time range covered by one graph column. */
export function formatColumnRange(startMs, endMs, msPerCol)
{
	const d = new Date(startMs);
	const datePart = d.toDateString() === new Date().toDateString() ? '' : d.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ', ';
	if (msPerCol < 1000) return datePart + formatClock(startMs, true);
	if (msPerCol === 1000) return datePart + formatClock(startMs, false);
	return datePart + formatClock(startMs) + ' – ' + formatClock(endMs) + `  (${formatSpan(endMs - startMs)})`;
}

export function formatDateTime(ms)
{
	const d = new Date(ms);
	return d.toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' }) + ' ' + d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

export function formatBytes(n)
{
	if (n < 1024) return n + ' B';
	if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
	if (n < 1024 * 1024 * 1024) return (n / 1024 / 1024).toFixed(1) + ' MB';
	return (n / 1024 / 1024 / 1024).toFixed(2) + ' GB';
}
