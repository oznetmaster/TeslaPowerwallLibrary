// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.FleetApi;

/// <summary>
/// Thrown when the Tesla™ FleetAPI configuration file is missing or unreadable.
/// </summary>
public class PowerwallFleetApiNoTeslaAuthFileException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiNoTeslaAuthFileException"/> class.</summary>
	public PowerwallFleetApiNoTeslaAuthFileException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiNoTeslaAuthFileException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallFleetApiNoTeslaAuthFileException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiNoTeslaAuthFileException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallFleetApiNoTeslaAuthFileException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a FleetAPI call is attempted before a Tesla FleetAPI session has been established.
/// </summary>
public class PowerwallFleetApiTeslaNotConnectedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiTeslaNotConnectedException"/> class.</summary>
	public PowerwallFleetApiTeslaNotConnectedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiTeslaNotConnectedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallFleetApiTeslaNotConnectedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiTeslaNotConnectedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallFleetApiTeslaNotConnectedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a requested operation is not implemented for Tesla FleetAPI mode.
/// </summary>
public class PowerwallFleetApiNotImplementedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiNotImplementedException"/> class.</summary>
	public PowerwallFleetApiNotImplementedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiNotImplementedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallFleetApiNotImplementedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiNotImplementedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallFleetApiNotImplementedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a FleetAPI response payload cannot be parsed or is otherwise invalid.
/// </summary>
public class PowerwallFleetApiInvalidPayloadException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiInvalidPayloadException"/> class.</summary>
	public PowerwallFleetApiInvalidPayloadException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiInvalidPayloadException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallFleetApiInvalidPayloadException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiInvalidPayloadException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallFleetApiInvalidPayloadException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a Tesla FleetAPI endpoint has been permanently removed by Tesla and the server responds
/// with HTTP 410 (Gone). Callers should switch to the current replacement endpoint.
/// </summary>
public class PowerwallFleetApiEndpointRemovedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiEndpointRemovedException"/> class.</summary>
	public PowerwallFleetApiEndpointRemovedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiEndpointRemovedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallFleetApiEndpointRemovedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiEndpointRemovedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallFleetApiEndpointRemovedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when an explicitly configured FleetAPI token cache location (a non-empty
/// <see cref="PowerwallOptions.FleetApiAuthPath"/>) cannot be read or written - for example, the directory
/// cannot be created, or the process lacks permission to access it. An explicit location has no fallback:
/// this exception surfaces the failure immediately instead of silently continuing without persistence, which
/// matters most on runtimes without a writable per-user profile folder (for example Mono-hosted embedded
/// environments), where the caller supplies a known storage location itself.
/// </summary>
public class PowerwallFleetApiTokenCacheStorageException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiTokenCacheStorageException"/> class.</summary>
	public PowerwallFleetApiTokenCacheStorageException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiTokenCacheStorageException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallFleetApiTokenCacheStorageException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallFleetApiTokenCacheStorageException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallFleetApiTokenCacheStorageException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
