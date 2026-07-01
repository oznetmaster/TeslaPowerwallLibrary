// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace TeslaPowerwallLibrary;

/// <summary>
/// Input validation helpers for Powerwall host names, IP addresses, and customer email addresses.
/// </summary>
public static class Validation
	{
	// Mirrors the simple EMAIL_REGEX used by pypowerwall: a non-whitespace local part, '@',
	// a non-whitespace domain, '.', and a non-whitespace top-level label.
	private static readonly Regex _emailRegex = new (@"^\S+@\S+\.\S+$", RegexOptions.CultureInvariant);

	/// <summary>
	/// Determines whether the specified value is a valid bare host: an IPv4 address, an IPv6 address,
	/// or a DNS host name / FQDN. A trailing <c>:port</c> suffix is <b>not</b> accepted by this method.
	/// </summary>
	/// <param name="value">The host value to validate.</param>
	/// <returns><see langword="true"/> when the value is a valid bare host; otherwise <see langword="false"/>.</returns>
	public static bool IsValidHost (string? value) =>
		value is not null && Uri.CheckHostName (value) != UriHostNameType.Unknown;

	/// <summary>
	/// Determines whether the specified value is an IPv4 or IPv6 address.
	/// </summary>
	/// <param name="value">The value to validate.</param>
	/// <returns><see langword="true"/> when the value parses as an IP address; otherwise <see langword="false"/>.</returns>
	public static bool IsValidIpAddress (string? value) =>
		value is not null && IPAddress.TryParse (value, out _);

	/// <summary>
	/// Determines whether the specified value is a syntactically valid customer email address.
	/// </summary>
	/// <param name="value">The value to validate.</param>
	/// <returns><see langword="true"/> when the value matches the expected email pattern; otherwise <see langword="false"/>.</returns>
	public static bool IsValidEmail (string? value) =>
		value is not null && _emailRegex.IsMatch (value);
	}
