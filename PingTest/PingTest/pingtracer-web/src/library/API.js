import { HTMLToText } from '/src/library/Util';
/**
 * Executes an API call to the specified method, using the specified arguments.  Returns a promise which resolves with any graceful response from the server.  Rejects if an error occurred that prevents the normal functioning of the API (e.g. the server was unreachable or returned an entirely unexpected response such as HTTP 500).
 * @param {String} method Server route, e.g. "Auth/Login"
 * @param {Object} args arguments
 * @returns {Promise} A promise which resolves with any graceful response from the server.
 */
export default async function ExecAPI(method, args)
{
	if (!args)
		args = {};
	let response = await fetch(method, {
		method: 'POST',
		headers: {
			'Accept': 'application/json',
			'Content-Type': 'application/json',
			'X-WebProxy-CSRF-Protection': '1'
		},
		body: JSON.stringify(args)
	})
	if (response.status === 200 || response.status === 418) // 418 (I'm a teapot) in our case is a synonym for success in a way that prevents the browser from offering to remember credentials that were submitted.
		return await response.json();
	else
		throw new ApiError(await response.text());
}
export class ApiError extends Error
{
	constructor(message, data)
	{
		super(message);
		this.name = "ApiError";
		this.data = data;
	}
}
