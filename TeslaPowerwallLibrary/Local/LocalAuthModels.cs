// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using Newtonsoft.Json;

namespace TeslaPowerwallLibrary.Local;

/// <summary>
/// The <c>response</c> body of the Tesla™ Energy Gateway <c>/api/login/Basic</c> endpoint.
/// </summary>
internal sealed record LocalLoginResponse
	{
	/// <summary>The bearer token issued when the client authenticates in <c>token</c> mode.</summary>
	[JsonProperty ("token")]
	public string? Token { get; init; }
	}

/// <summary>
/// The on-disk shape of the local-mode authentication session cache file, covering both supported
/// <see cref="PowerwallLocalClient"/> auth modes: <c>token</c> (via <see cref="Authorization"/>) and
/// <c>cookie</c> (via <see cref="AuthCookie"/> and <see cref="UserRecord"/>).
/// </summary>
internal sealed record LocalAuthCacheEntry
	{
	/// <summary>The cached <c>Authorization</c> header value, used in <c>token</c> auth mode.</summary>
	[JsonProperty ("Authorization")]
	public string? Authorization { get; init; }

	/// <summary>The cached gateway session cookie, used in <c>cookie</c> auth mode.</summary>
	[JsonProperty ("AuthCookie")]
	public string? AuthCookie { get; init; }

	/// <summary>The cached gateway user-record cookie, used in <c>cookie</c> auth mode.</summary>
	[JsonProperty ("UserRecord")]
	public string? UserRecord { get; init; }
	}

#pragma warning restore CA1507
