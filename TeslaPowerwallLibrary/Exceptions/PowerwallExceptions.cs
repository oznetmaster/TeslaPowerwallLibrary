// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary;

/// <summary>
/// Base type for all exceptions raised by the Tesla™ Powerwall™ client library.
/// </summary>
public class PowerwallException : Exception
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallException"/> class.</summary>
	public PowerwallException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when one or more configuration parameters supplied to the Powerwall client are invalid or mutually exclusive.
/// </summary>
public class PowerwallInvalidConfigurationException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallInvalidConfigurationException"/> class.</summary>
	public PowerwallInvalidConfigurationException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallInvalidConfigurationException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallInvalidConfigurationException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallInvalidConfigurationException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallInvalidConfigurationException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when an attempt is made to set a battery backup reserve level outside the valid range (0 - 100 percent).
/// </summary>
public class InvalidBatteryReserveLevelException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="InvalidBatteryReserveLevelException"/> class.</summary>
	public InvalidBatteryReserveLevelException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="InvalidBatteryReserveLevelException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public InvalidBatteryReserveLevelException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="InvalidBatteryReserveLevelException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public InvalidBatteryReserveLevelException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when an operation that requires an energy-site-capable backend (site selection, grid
/// charging/export configuration, or energy history) is invoked while connected in a mode that does not
/// support it (for example <see cref="PowerwallMode.Local"/>).
/// </summary>
public class PowerwallNotSupportedException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallNotSupportedException"/> class.</summary>
	public PowerwallNotSupportedException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallNotSupportedException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallNotSupportedException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallNotSupportedException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallNotSupportedException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
