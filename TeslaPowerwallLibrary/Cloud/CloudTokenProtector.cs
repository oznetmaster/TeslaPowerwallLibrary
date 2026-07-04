// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#if !NETFRAMEWORK
using System.Runtime.Versioning;
#endif
using System.Security.Cryptography;
using System.Text;

namespace TeslaPowerwallLibrary.Cloud;

/// <summary>
/// Protects small secrets (Tesla Owners API tokens) at rest for the library-owned cloud token cache using the
/// Windows Data Protection API (DPAPI), scoped to the current user. DPAPI is available both on .NET Framework
/// (built into the platform) and on modern .NET running on Windows (via the ProtectedData package), so a value
/// protected by a process running under one target framework can still be recovered by a process running
/// under the other, as long as both run as the same Windows user. On non-Windows platforms values are stored
/// as plaintext, matching the upstream behavior of a plaintext token cache. Each stored value records whether
/// it was protected so a later load knows whether to decrypt.
/// </summary>
internal static class CloudTokenProtector
	{
	// Additional entropy bound to this library so protected values cannot be trivially unprotected by
	// another application running as the same user.
	private static readonly byte[] _entropy = Encoding.UTF8.GetBytes ("TeslaPowerwallLibrary.Cloud.TokenCache.v1");

	/// <summary>Gets a value indicating whether at-rest DPAPI encryption is applied to stored token values.</summary>
	public static bool IsActive => IsSupported;

	/// <summary>
	/// Encrypts <paramref name="plaintext"/> when encryption is active; otherwise returns it unchanged.
	/// </summary>
	/// <param name="plaintext">The secret to protect.</param>
	/// <returns>The protected (base64) value, or the original plaintext when encryption is unavailable.</returns>
	public static string? Protect (string? plaintext)
		{
		if (string.IsNullOrEmpty (plaintext) || !IsSupported)
			return plaintext;

		var protectedBytes = ProtectedData.Protect (
			Encoding.UTF8.GetBytes (plaintext!), _entropy, DataProtectionScope.CurrentUser);
		return Convert.ToBase64String (protectedBytes);
		}

	/// <summary>
	/// Reverses <see cref="Protect"/>. When <paramref name="wasProtected"/> is <see langword="false"/> the
	/// value is returned as-is; otherwise it is decrypted, returning <see langword="null"/> when the value
	/// cannot be decrypted on the current runtime or platform.
	/// </summary>
	/// <param name="stored">The stored value.</param>
	/// <param name="wasProtected">Whether the value was DPAPI-protected when written.</param>
	/// <returns>The recovered plaintext, or <see langword="null"/> when a protected value cannot be decrypted.</returns>
	public static string? Unprotect (string? stored, bool wasProtected)
		{
		if (string.IsNullOrEmpty (stored))
			return stored;

		if (!wasProtected)
			return stored;

		if (!IsSupported)
			return null;

		try
			{
			var bytes = ProtectedData.Unprotect (
				Convert.FromBase64String (stored!), _entropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString (bytes);
			}
		catch (Exception exc) when (exc is FormatException or CryptographicException)
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
