// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Local;

/// <summary>
/// Thrown when authentication with the local Powerwall gateway fails (for example, an incorrect customer password).
/// </summary>
public class LoginException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="LoginException"/> class.</summary>
	public LoginException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="LoginException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public LoginException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="LoginException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public LoginException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}

/// <summary>
/// Thrown when the local Powerwall gateway cannot be reached over the network.
/// </summary>
public class PowerwallConnectionException : PowerwallException
	{
	/// <summary>Initializes a new instance of the <see cref="PowerwallConnectionException"/> class.</summary>
	public PowerwallConnectionException ()
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallConnectionException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public PowerwallConnectionException (string message)
		: base (message)
		{
		}

	/// <summary>Initializes a new instance of the <see cref="PowerwallConnectionException"/> class with a specified error message and inner exception.</summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public PowerwallConnectionException (string message, Exception innerException)
		: base (message, innerException)
		{
		}
	}
