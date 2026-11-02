// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Read-only service-mode information, without access credentials.</summary>
public sealed record LocalSupportMode
	{
	/// <summary>Reported remote-service state.</summary>
	/// <value>Reported remote-service state.</value>
	[JsonPropertyName ("remoteService")]
	public LocalRemoteServiceStatus? RemoteService { get; init; }

	}

/// <summary>Remote-service status; reading it never enables a service session.</summary>
public sealed record LocalRemoteServiceStatus
	{
	/// <summary>Whether remote service is enabled.</summary>
	/// <value>Whether remote service is enabled.</value>
	[JsonPropertyName ("isEnabled")]
	public bool? IsEnabled { get; init; }

	/// <summary>Service expiry timestamp as reported on the wire.</summary>
	/// <value>Service expiry timestamp as reported on the wire.</value>
	[JsonPropertyName ("expiryTime")]
	public string? ExpiryTime { get; init; }

	}
