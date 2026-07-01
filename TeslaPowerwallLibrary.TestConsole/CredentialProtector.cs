// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#if !NETFRAMEWORK
using System.Runtime.Versioning;
#endif
using System.Security.Cryptography;
using System.Text;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Protects and unprotects small secrets (such as the Powerwall password) at rest using the Windows
/// Data Protection API (DPAPI), scoped to the current user. On platforms without DPAPI support the
/// secret is simply not persisted.
/// </summary>
internal static class CredentialProtector
	{
	private static readonly byte[] _entropy = Encoding.UTF8.GetBytes ("TeslaPowerwallLibrary.TestConsole.v1");

	/// <summary>
	/// Encrypts <paramref name="plaintext"/> and returns it as a base64 string, or <see langword="null"/>
	/// when there is nothing to protect or DPAPI is unavailable on the current platform.
	/// </summary>
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

#if NETFRAMEWORK
	private static bool IsSupported => true;
#else
	[SupportedOSPlatformGuard ("windows")]
	private static bool IsSupported => OperatingSystem.IsWindows ();
#endif
	}
