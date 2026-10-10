// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class LiveSiteTests
	{
	/// <summary>Portable local inputs for the same read-only suite on Windows and Mono.</summary>
	public sealed partial class LiveSettings
		{
		/// <summary>Gets or sets Gateway, Setup Wi-Fi, or Signed LAN.</summary>
		[JsonPropertyName ("localProtocol")] public string? LocalProtocol { get; set; }
		/// <summary>Gets or sets the local DNS hostname or IP address.</summary>
		[JsonPropertyName ("host")] public string? Host { get; set; }
		/// <summary>Gets or sets the customer password, or full label password for setup Wi-Fi.</summary>
		[JsonPropertyName ("password")] public string? Password { get; set; }
		/// <summary>Gets or sets an already registered RSA-4096 private key in portable XML form.</summary>
		[JsonPropertyName ("localSigningKey")] public string? LocalSigningKey { get; set; }
		/// <summary>Gets or sets an existing Windows-only CNG key; processor runs require portable XML instead.</summary>
		[JsonPropertyName ("localSigningKeyName")] public string? LocalSigningKeyName { get; set; }
		/// <summary>Gets or sets the shared cache interval; zero selects fifteen seconds.</summary>
		[JsonPropertyName ("localRefreshIntervalSeconds")] public int LocalRefreshIntervalSeconds { get; set; }

		/// <summary>Checks local inputs without network access or key enrollment.</summary>
		public void ValidateLocal ()
			{
			if (LocalProtocol is not ("Gateway" or "Setup Wi-Fi" or "Signed LAN")
				|| string.IsNullOrWhiteSpace (Password) || string.IsNullOrWhiteSpace (Host)
				|| !Uri.TryCreate ("https://" + Host, UriKind.Absolute, out Uri? endpoint)
				|| endpoint.HostNameType == UriHostNameType.Unknown || endpoint.UserInfo.Length != 0
				|| endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
				|| (LocalRefreshIntervalSeconds != 0 && (LocalRefreshIntervalSeconds < 15 || LocalRefreshIntervalSeconds > 3600)))
				throw new InvalidDataException ("Invalid local live settings. Credentials withheld.");
			if (LocalProtocol == "Signed LAN")
				{
				using RSA key = OpenSigningKey ();
				}
			}

		/// <summary>Builds read-only options without cloud fallback or persistent sessions.</summary>
		/// <param name="key">The imported signing key, owned and disposed by the test fixture.</param>
		/// <returns>Options for the selected local protocol.</returns>
		public PowerwallOptions LocalOptions (out RSA? key)
			{
			ValidateLocal ();
			key = LocalProtocol == "Signed LAN" ? OpenSigningKey () : null;
			return new PowerwallOptions
				{
				Host = Host!, Password = Password!, GatewayPassword = Password!,
				LocalProtocol = LocalProtocol switch
					{
					"Gateway" => PowerwallLocalProtocol.Gateway,
					"Setup Wi-Fi" => PowerwallLocalProtocol.Tedapi,
					_ => PowerwallLocalProtocol.TedapiSigned
					},
				LocalSigningKey = key, AllowLocalControl = false, NoLocalSessionPersistence = true,
				CacheExpireSeconds = LocalRefreshIntervalSeconds == 0 ? 15 : LocalRefreshIntervalSeconds,
				Timeout = TimeSpan.FromSeconds (20)
				};
			}

		private RSA OpenSigningKey ()
			{
			if (!string.IsNullOrWhiteSpace (LocalSigningKeyName))
				{
				if (!string.IsNullOrWhiteSpace (LocalSigningKey) || 
#if NETFRAMEWORK
			Environment.OSVersion.Platform != PlatformID.Win32NT
#else
			!OperatingSystem.IsWindows ()
#endif
			)
					throw new InvalidDataException ("Select one signing key source. A Windows-only key cannot be used by processor tests.");
				using CngKey stored = CngKey.Open (LocalSigningKeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
				var windowsKey = new RSACng (stored);
				if (windowsKey.KeySize == 4096) return windowsKey;
				windowsKey.Dispose ();
				throw new InvalidDataException ("Signed LAN requires RSA-4096.");
				}
			var key = new RSACryptoServiceProvider { PersistKeyInCsp = false };
			try
				{
				if (string.IsNullOrWhiteSpace (LocalSigningKey)) throw new CryptographicException ();
				key.FromXmlString (LocalSigningKey);
				if (key.KeySize != 4096 || key.PublicOnly) throw new CryptographicException ();
				return key;
				}
			catch
				{
				key.Dispose ();
				throw new InvalidDataException ("Signed LAN requires an already registered RSA-4096 private key. Credentials withheld.");
				}
			}
		}
	}
