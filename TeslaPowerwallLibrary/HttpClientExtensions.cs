// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

/// <summary>
/// HTTP client extensions used by the Tesla Powerwall client.
/// </summary>
public static class HttpClientExtensions
	{
	/// <summary>
	/// Sends an HTTP PATCH request to the specified URI.
	/// </summary>
	/// <param name="client">The HTTP client instance.</param>
	/// <param name="requestUri">The request URI.</param>
	/// <param name="content">Optional request content.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The HTTP response message.</returns>
	public static Task<HttpResponseMessage> PatchAsync (this HttpClient client, string requestUri, HttpContent? content, CancellationToken cancellationToken = default)
		{
		var request = new HttpRequestMessage (new HttpMethod ("PATCH"), requestUri)
			{
			Content = content
			};
		return client.SendAsync (request, cancellationToken);
		}
	}
