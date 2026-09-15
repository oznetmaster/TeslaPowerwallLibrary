// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace TeslaPowerwallLibrary.Setup;

internal sealed class FleetSetupPreferences
	{
	public string ClientId { get; set; } = "";
	public string ClientSecret { get; set; } = "";
	public string Domain { get; set; } = "";
	public string RedirectUri { get; set; } = "";
	public string Region { get; set; } = "na";

	internal static string FilePath => Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "TeslaPowerwallLibrary", "Setup", "fleet-app.dat");
	internal static FleetSetupPreferences? Load (string path)
		{
		if (!File.Exists (path))
			return null;
		byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (path), null, DataProtectionScope.CurrentUser);
		try
			{
			return JsonSerializer.Deserialize<FleetSetupPreferences> (clear);
			}
		finally { CryptographicOperations.ZeroMemory (clear); }
		}

	internal void Save (string path)
		{
		Directory.CreateDirectory (Path.GetDirectoryName (path)!);
		byte[] clear = JsonSerializer.SerializeToUtf8Bytes (this);
		string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			byte[] encrypted = ProtectedData.Protect (clear, null, DataProtectionScope.CurrentUser);
			using (var file = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				file.Write (encrypted);
				file.Flush (true);
				}
			if (File.Exists (path))
				File.Replace (temporary, path, null);
			else
				File.Move (temporary, path);
			}
		finally
			{
			CryptographicOperations.ZeroMemory (clear);
			if (File.Exists (temporary))
				File.Delete (temporary);
			}
		}

	internal static bool IsCallbackAddress (string value, string redirectUri) =>
		Uri.TryCreate (value, UriKind.Absolute, out var callback)
		&& Uri.TryCreate (redirectUri, UriKind.Absolute, out var target)
		&& callback.Scheme == target.Scheme && callback.Host == target.Host && callback.Port == target.Port
		&& callback.AbsolutePath == target.AbsolutePath;

	internal static string ParseCallback (string value, string redirectUri, string expectedState)
		{
		if (string.IsNullOrWhiteSpace (expectedState) || !Uri.TryCreate (value, UriKind.Absolute, out var callback)
			|| !Uri.TryCreate (redirectUri, UriKind.Absolute, out var target)
			|| callback.Scheme != target.Scheme || callback.Host != target.Host || callback.Port != target.Port
			|| callback.AbsolutePath != target.AbsolutePath || callback.Fragment.Length != 0)
			throw new InvalidDataException ("Paste the complete redirected URL from this authorization attempt.");
		var query = new Dictionary<string, string> (StringComparer.Ordinal);
		foreach (string field in callback.Query.TrimStart ('?').Split ('&', StringSplitOptions.RemoveEmptyEntries))
			{
			string[] pair = field.Split ('=', 2);
			string name = Uri.UnescapeDataString (pair[0].Replace ('+', ' '));
			if (!query.TryAdd (name, pair.Length == 2 ? Uri.UnescapeDataString (pair[1].Replace ('+', ' ')) : ""))
				throw new InvalidDataException ("The callback contains repeated parameters. Start a new authorization attempt.");
			}
		if (query.ContainsKey ("error") || !query.TryGetValue ("state", out string? state) || state != expectedState
			|| !query.TryGetValue ("code", out string? code) || string.IsNullOrWhiteSpace (code))
			throw new InvalidDataException ("The callback does not match this authorization attempt or permission was not granted.");
		return code;
		}
	}