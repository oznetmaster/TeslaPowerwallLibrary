// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Security.Cryptography;

namespace TeslaPowerwallLibrary.Tools;

/// <summary>Opens or explicitly creates a nonexportable local signing key in the current Windows user's key store.</summary>
internal static class LocalSigningKeyStore
	{
	/// <summary>Default shared key name for the console and desktop application.</summary>
	internal const string DefaultKeyName = "TeslaPowerwallLibrary.LocalClient";

	/// <summary>Opens an existing RSA-4096 signing key. Connecting never creates or registers a key.</summary>
	/// <param name="name">Persisted CNG key name.</param>
	/// <returns>A caller-owned RSA instance; dispose it after the connection that uses it.</returns>
	internal static RSA Open (string name)
		{
		ValidateName (name);
		if (!CngKey.Exists (name, CngProvider.MicrosoftSoftwareKeyStorageProvider))
			throw new InvalidOperationException ("The local signing key does not exist. Create and enroll it before connecting.");
		using var key = CngKey.Open (name, CngProvider.MicrosoftSoftwareKeyStorageProvider);
		var rsa = new RSACng (key);
		if (rsa.KeySize != 4096)
			{
			rsa.Dispose ();
			throw new InvalidOperationException ("Local TEDAPI requires an RSA-4096 key.");
			}
		return rsa;
		}

	/// <summary>Creates a key only when no key with this name exists; an existing key is preserved.</summary>
	/// <param name="name">Persisted CNG key name.</param>
	/// <returns>A caller-owned RSA instance for the existing or newly created key.</returns>
	internal static RSA CreateOrOpen (string name)
		{
		ValidateName (name);
		if (!CngKey.Exists (name, CngProvider.MicrosoftSoftwareKeyStorageProvider))
			{
			var parameters = new CngKeyCreationParameters
				{
				Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
				ExportPolicy = CngExportPolicies.None,
				KeyUsage = CngKeyUsages.Signing
				};
			parameters.Parameters.Add (new CngProperty ("Length", BitConverter.GetBytes (4096), CngPropertyOptions.None));
			using var created = CngKey.Create (CngAlgorithm.Rsa, name, parameters);
			}
		return Open (name);
		}

	/// <summary>Rejects empty key names before opening the Windows key store.</summary>
	/// <param name="name">Key name to validate.</param>
	private static void ValidateName (string name)
		{
		if (string.IsNullOrWhiteSpace (name))
			throw new ArgumentException ("A local signing key name is required.", nameof (name));
		}
	}
