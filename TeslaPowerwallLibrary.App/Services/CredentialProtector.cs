// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace TeslaPowerwallLibrary.App.Services;

/// <summary>
/// Protects and unprotects small secrets (such as the Powerwall™ password and Tesla tokens) at rest using the
/// Windows Data Protection API (DPAPI), scoped to the current user. Secrets are never written in plaintext.
/// </summary>
internal static class CredentialProtector
	{
	private static readonly byte[] _entropy = Encoding.UTF8.GetBytes ("TeslaPowerwallLibrary.App.v1");

	/// <summary>
	/// Encrypts <paramref name="plaintext"/> and returns it as a base64 string, or <see langword="null"/>
	/// when there is nothing to protect or DPAPI is unavailable on the current platform.
	/// </summary>
	/// <param name="plaintext">The secret to protect.</param>
	/// <returns>The protected, base64-encoded value, or <see langword="null"/>.</returns>
	public static string? Protect (string? plaintext)
		{
		if (string.IsNullOrEmpty (plaintext) || !IsSupported)
			return null;

		var protectedBytes = ProtectedData.Protect (Encoding.UTF8.GetBytes (plaintext!), _entropy, DataProtectionScope.CurrentUser);
		return Convert.ToBase64String (protectedBytes);
		}

	/// <summary>
	/// Decrypts a value previously produced by <see cref="Protect"/>, or returns <see langword="null"/>
	/// when the input is empty, DPAPI is unavailable, or the data cannot be decrypted by the current user.
	/// </summary>
	/// <param name="protectedBase64">The protected, base64-encoded value.</param>
	/// <returns>The recovered plaintext, or <see langword="null"/>.</returns>
	public static string? Unprotect (string? protectedBase64)
		{
		if (string.IsNullOrEmpty (protectedBase64) || !IsSupported)
			return null;

		try
			{
			var plainBytes = ProtectedData.Unprotect (Convert.FromBase64String (protectedBase64!), _entropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString (plainBytes);
			}
		catch (CryptographicException)
			{
			return null;
			}
		catch (FormatException)
			{
			return null;
			}
		}

	[SupportedOSPlatformGuard ("windows")]
	private static bool IsSupported => OperatingSystem.IsWindows ();
	}
