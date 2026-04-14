/**
 * Splits the given string on ',' and ' ', removing empty entries.
 * @param {String} str String to split.
 */
export function splitExitpointHostList(str)
{
	return str.split(/,| /).filter(Boolean);
}
export function Clamp(i, min, max)
{
	if (i < min)
		return min;
	if (i > max)
		return max;
	if (isNaN(i))
		return min;
	return i;
}
var escape = document.createElement('textarea');
export function EscapeHTML(html)
{
	escape.textContent = html;
	return escape.innerHTML;
}
export function UnescapeHTML(html)
{
	escape.innerHTML = html;
	return escape.textContent;
}
export function HtmlAttributeEncode(str)
{
	if (typeof str !== "string")
		return "";
	var sb = new Array("");
	for (var i = 0; i < str.length; i++)
	{
		var c = str.charAt(i);
		switch (c)
		{
			case '"':
				sb.push("&quot;");
				break;
			case '\'':
				sb.push("&#39;");
				break;
			case '&':
				sb.push("&amp;");
				break;
			case '<':
				sb.push("&lt;");
				break;
			case '>':
				sb.push("&gt;");
				break;
			default:
				sb.push(c);
				break;
		}
	}
	return sb.join("");
}
var htmlToTextConvert = document.createElement('div');
/**
 * Given a string of HTML, returns the innerText representation.
 * @param {String} html HTML to get text out of
 */
export function HTMLToText(html)
{
	htmlToTextConvert.innerHTML = html;
	let text = htmlToTextConvert.innerText;
	htmlToTextConvert.innerHTML = "";
	return text;
}
/**
 * Escapes a minimal set of characters (\, *, +, ?, |, {, [, (,), ^, $, ., #, and white space) by replacing them with their escape codes. This instructs the regular expression engine to interpret these characters literally rather than as metacharacters.
 * @param {String} str String to escape.
 */
export function escapeRegExp(str)
{
    return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Formats the given number of bytes as a string with a suffix ('B', 'KiB', 'MiB', etc.) using multiples of 1024.
 * @param {Number} bytes Number of bytes.
 * @param {Number} decimals Number of decimal places to include in the string.
 */
export function formatBytes2(bytes, decimals)
{
	if (bytes == 0) return '0 B';
	var negative = bytes < 0;
	if (negative)
		bytes = -bytes;
	var k = 1024,
		dm = typeof decimals != "undefined" ? decimals : 2,
		sizes = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB', 'EiB', 'ZiB', 'YiB'],
		i = Math.floor(Math.log(bytes) / Math.log(k));
	return (negative ? '-' : '') + (bytes / Math.pow(k, i)).toFloat(dm) + ' ' + sizes[i];
}
/**
* Formats the given number of bytes as a string with a suffix ('B', 'KB', 'MB', etc.) using multiples of 1000.
 * @param {Number} bytes Number of bytes.
 * @param {Number} decimals Number of decimal places to include in the string.
 */
export function formatBytesF10(bytes, decimals)
{
	if (bytes == 0) return '0 B';
	var negative = bytes < 0;
	if (negative)
		bytes = -bytes;
	var k = 1000,
		dm = typeof decimals != "undefined" ? decimals : 2,
		sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB'],
		i = Math.floor(Math.log(bytes) / Math.log(k));
	return (negative ? '-' : '') + (bytes / Math.pow(k, i)).toFloat(dm) + ' ' + sizes[i];
}
String.prototype.toFloat = function (digits)
{
	return parseFloat(this.toFixed(digits));
};
Number.prototype.toFloat = function (digits)
{
	return parseFloat(this.toFixed(digits));
};
String.prototype.padLeft = function (len, c)
{
	var pads = len - this.length;
	if (pads > 0)
	{
		var sb = [];
		var pad = c || "&nbsp;";
		for (var i = 0; i < pads; i++)
			sb.push(pad);
		sb.push(this);
		return sb.join("");
	}
	return this;
};
String.prototype.padRight = function (len, c)
{
	var pads = len - this.length;
	if (pads > 0)
	{
		var sb = [];
		sb.push(this);
		var pad = c || "&nbsp;";
		for (var i = 0; i < pads; i++)
			sb.push(pad);
		return sb.join("");
	}
	return this;
};
Number.prototype.padLeft = function (len, c)
{
	return this.toString().padLeft(len, c);
};
Number.prototype.padRight = function (len, c)
{
	return this.toString().padRight(len, c);
};