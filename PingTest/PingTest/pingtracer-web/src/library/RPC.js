import { HTMLToText } from '/src/library/Util';
import { ApiError } from '/src/library/API';
/**
 * Executes an API call to the specified method, using the specified arguments.  Returns a promise which resolves with any graceful response from the server.  Rejects if an error occurred that prevents the normal functioning of the API (e.g. the server was unreachable or returned an entirely unexpected response such as HTTP 500).
 * @param {String} method Server route, e.g. "Auth/Login"
 * @param {Object} args arguments
 * @returns {Promise} A promise which resolves with any graceful response from the server.
 */
async function ExecRPC(method, args)
{
	if (args)
	{
		for (let arg of args)
			method += "/" + encodeURIComponent(arg);
	}
	let response = await fetch(method, {
		method: 'POST',
		headers: {
			'Accept': 'application/json',
			'X-WebProxy-CSRF-Protection': '1'
		}
	});
	if (response.status === 200 || response.status === 418) // 418 (I'm a teapot) in our case is a synonym for success in a way that prevents the browser from offering to remember credentials that were submitted.
		return await response.json();
	else
		throw new ApiError(await response.text());
}
function createRPC(execFn, path = "")
{
	const proxy = new Proxy(function () { }, {
		get(target, prop)
		{
			if (typeof prop === "symbol" || prop === "then") return undefined;
			// Chain property accesses, building the path
			return createRPC(execFn, path ? `${path}/${prop}` : prop);
		},
		apply(target, thisArg, args)
		{
			// When called as a function, execute ExecAPI
			return execFn(path, args);
		},
	});
	return proxy;
}
export default createRPC(ExecRPC);
