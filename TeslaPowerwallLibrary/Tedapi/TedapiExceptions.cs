// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>
/// Thrown when the TEDAPI authentication material (gateway password) is missing or unreadable.
/// </summary>
public class PowerwallTedapiNoTeslaAuthFileException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNoTeslaAuthFileException"/> class.</summary>
	public PowerwallTedapiNoTeslaAuthFileException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNoTeslaAuthFileException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallTedapiNoTeslaAuthFileException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNoTeslaAuthFileException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallTedapiNoTeslaAuthFileException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a TEDAPI call is attempted before a connection to the gateway has been established.
/// </summary>
public class PowerwallTedapiNotConnectedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNotConnectedException"/> class.</summary>
	public PowerwallTedapiNotConnectedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNotConnectedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallTedapiNotConnectedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNotConnectedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallTedapiNotConnectedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a requested operation is not implemented for TEDAPI mode.
/// </summary>
public class PowerwallTedapiNotImplementedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNotImplementedException"/> class.</summary>
	public PowerwallTedapiNotImplementedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNotImplementedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallTedapiNotImplementedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiNotImplementedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallTedapiNotImplementedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when a TEDAPI response payload cannot be parsed or is otherwise invalid.
/// </summary>
public class PowerwallTedapiInvalidPayloadException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiInvalidPayloadException"/> class.</summary>
	public PowerwallTedapiInvalidPayloadException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiInvalidPayloadException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallTedapiInvalidPayloadException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallTedapiInvalidPayloadException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallTedapiInvalidPayloadException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
