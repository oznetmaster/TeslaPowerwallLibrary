// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#if NETFRAMEWORK
using System.Net.Http;

namespace TeslaPowerwallLibrary.Tests;

// The framework content-buffer APIs lack cancellation overloads. SendAsync still
// receives the token; also check it around the completed response-content read.
internal static class TestHttpContentExtensions
	{
	internal static async Task<string> ReadAsStringAsync (this HttpContent content, CancellationToken cancellationToken)
		{
		cancellationToken.ThrowIfCancellationRequested ();
		string result = await content.ReadAsStringAsync ().ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		return result;
		}
	internal static async Task<byte[]> ReadAsByteArrayAsync (this HttpContent content, CancellationToken cancellationToken)
		{
		cancellationToken.ThrowIfCancellationRequested ();
		byte[] result = await content.ReadAsByteArrayAsync ().ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		return result;
		}
	}
#endif