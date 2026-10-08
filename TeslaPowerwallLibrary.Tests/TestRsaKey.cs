// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Provides ephemeral test keys without requiring Windows CNG or persistent key storage.</summary>
internal static class TestRsaKey
	{
	private static readonly Lazy<RSAParameters> _parameters = new (CreateParameters);

	/// <summary>Imports a separate disposable instance of the process-local test key.</summary>
	/// <returns>A caller-owned 4096-bit RSA key that is never registered with a device.</returns>
	internal static RSA Create ()
		{
#if NETFRAMEWORK
		RSA key = new RSACryptoServiceProvider { PersistKeyInCsp = false };
#else
		RSA key = RSA.Create ();
#endif
		try
			{
			key.ImportParameters (_parameters.Value);
			return key;
			}
		catch
			{
			key.Dispose ();
			throw;
			}
		}

	private static RSAParameters CreateParameters ()
		{
		// Generate once per process: repeated 4096-bit generation is expensive on embedded runtimes.
#if NETFRAMEWORK
		using RSA key = new RSACryptoServiceProvider (4096) { PersistKeyInCsp = false };
#else
		using RSA key = RSA.Create (4096);
#endif
		return key.ExportParameters (true);
		}
	}
