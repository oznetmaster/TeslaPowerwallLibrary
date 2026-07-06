// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Thrown when the Tesla™ Owners (cloud) authentication file is missing or unreadable.
/// </summary>
public class PowerwallCloudNoTeslaAuthFileException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudNoTeslaAuthFileException"/> class.</summary>
	public PowerwallCloudNoTeslaAuthFileException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudNoTeslaAuthFileException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallCloudNoTeslaAuthFileException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudNoTeslaAuthFileException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallCloudNoTeslaAuthFileException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a cloud API call is attempted before a Tesla cloud session has been established.
/// </summary>
public class PowerwallCloudTeslaNotConnectedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudTeslaNotConnectedException"/> class.</summary>
	public PowerwallCloudTeslaNotConnectedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudTeslaNotConnectedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallCloudTeslaNotConnectedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudTeslaNotConnectedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallCloudTeslaNotConnectedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a requested operation is not implemented for Tesla Owners (cloud) mode.
/// </summary>
public class PowerwallCloudNotImplementedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudNotImplementedException"/> class.</summary>
	public PowerwallCloudNotImplementedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudNotImplementedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallCloudNotImplementedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudNotImplementedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallCloudNotImplementedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a cloud API response payload cannot be parsed or is otherwise invalid.
/// </summary>
public class PowerwallCloudInvalidPayloadException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudInvalidPayloadException"/> class.</summary>
	public PowerwallCloudInvalidPayloadException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudInvalidPayloadException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallCloudInvalidPayloadException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudInvalidPayloadException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallCloudInvalidPayloadException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a Tesla Owners (cloud) API endpoint has been permanently removed by Tesla and the
/// server responds with HTTP 410 (Gone). Callers should switch to the current replacement endpoint.
/// </summary>
public class PowerwallCloudEndpointRemovedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudEndpointRemovedException"/> class.</summary>
	public PowerwallCloudEndpointRemovedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudEndpointRemovedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallCloudEndpointRemovedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudEndpointRemovedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallCloudEndpointRemovedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when an explicitly configured Tesla cloud token cache location (a non-empty
/// <see cref="PowerwallOptions.AuthPath"/>) cannot be read or written - for example, the directory cannot be
/// created, or the process lacks permission to access it. An explicit location has no fallback: this
/// exception surfaces the failure immediately instead of silently continuing without persistence, which
/// matters most on runtimes without a writable per-user profile folder (for example Mono-hosted embedded
/// environments), where the caller supplies a known storage location itself.
/// </summary>
public class PowerwallCloudTokenCacheStorageException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudTokenCacheStorageException"/> class.</summary>
	public PowerwallCloudTokenCacheStorageException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudTokenCacheStorageException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallCloudTokenCacheStorageException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallCloudTokenCacheStorageException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallCloudTokenCacheStorageException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
