// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// CA1507 (use nameof) does not apply here: JsonProperty names are the external wire-format contract,
// not references to the local member names they happen to be attached to.
#pragma warning disable CA1507

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Local;

/// <summary>Customer login sent directly to the local gateway, never to the Tesla cloud.</summary>
internal sealed record LocalLoginRequest
	{
	/// <summary>Always false: authentication must not turn off the site manager.</summary>
	[JsonPropertyName ("force_sm_off")]
	#pragma warning disable CA1822 // JSON requires an instance property; the safety value must stay immutable.
	public bool ForceSiteManagerOff => false;
#pragma warning restore CA1822
	/// <summary>Local account role.</summary>
	[JsonPropertyName ("username")]
	public string? Username
		{
		get; init;
		}
	/// <summary>Gateway customer password.</summary>
	[JsonPropertyName ("password")]
	public string? Password
		{
		get; init;
		}
	/// <summary>Local login account label.</summary>
	[JsonPropertyName ("email")]
	public string? Email
		{
		get; init;
		}
	/// <summary>Client time-zone information.</summary>
	[JsonPropertyName ("clientInfo")]
	public LocalClientInfo? ClientInfo
		{
		get; init;
		}
	}

/// <summary>Local login client metadata.</summary>
internal sealed record LocalClientInfo
	{
	/// <summary>Configured site time zone.</summary>
	[JsonPropertyName ("timezone")]
	public string? Timezone
		{
		get; init;
		}
	}

/// <summary>
/// The <c>response</c> body of the Tesla™ Energy Gateway <c>/api/login/Basic</c> endpoint.
/// </summary>
internal sealed record LocalLoginResponse
	{
	/// <summary>The bearer token issued when the client authenticates in <c>token</c> mode.</summary>
	[JsonPropertyName ("token")]
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
	[JsonPropertyName ("Authorization")]
	public string? Authorization { get; init; }

	/// <summary>The cached gateway session cookie, used in <c>cookie</c> auth mode.</summary>
	[JsonPropertyName ("AuthCookie")]
	public string? AuthCookie { get; init; }

	/// <summary>The cached gateway user-record cookie, used in <c>cookie</c> auth mode.</summary>
	[JsonPropertyName ("UserRecord")]
	public string? UserRecord { get; init; }
	}

#pragma warning restore CA1507