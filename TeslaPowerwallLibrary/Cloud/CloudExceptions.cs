// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Thrown when the Tesla Owners (cloud) authentication file is missing or unreadable.
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
