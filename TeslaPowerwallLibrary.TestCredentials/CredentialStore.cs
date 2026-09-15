// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

using System.Security.Cryptography;
using System.Text.Json;

namespace TeslaPowerwallLibrary.TestCredentials;

// Private storage records must never be logged or attached to test results.
internal sealed class CredentialState
	{
	public string Mode { get; set; } = "";
	public string ClientId { get; set; } = "";
	public string RefreshToken { get; set; } = "";
	public string? AccessToken
		{
		get; set;
		}
	public string SiteId { get; set; } = "";
	public string Region { get; set; } = "auto";
	public bool InProgress
		{
		get; set;
		}
	public bool Prepared
		{
		get; set;
		}
	}

internal sealed class CredentialStore : IDisposable
	{
	private readonly FileStream _lock;
	private readonly string _path;
	private readonly object _gate = new ();
	internal string InputsDirectory
		{
		get;
		}
	internal CredentialState? Current
		{
		get; private set;
		}
	internal static readonly JsonSerializerOptions JsonOptions = new () { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

	internal CredentialStore (string directory)
		{
		Directory.CreateDirectory (directory);
		_path = Path.Combine (directory, "credentials.dat");
		InputsDirectory = Path.Combine (directory, "RunInputs");
		_lock = new FileStream (Path.Combine (directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		try
			{
			if (File.Exists (_path))
				{
				byte[] clear = ProtectedData.Unprotect (File.ReadAllBytes (_path), null, DataProtectionScope.CurrentUser);
				try
					{
					Current = JsonSerializer.Deserialize<CredentialState> (clear, JsonOptions) ?? throw new InvalidDataException ();
					}
				finally { CryptographicOperations.ZeroMemory (clear); }
				Validate (Current);
				}
			}
		catch { _lock.Dispose (); throw; }
		}

	internal static void Validate (CredentialState state)
		{
		if (state.Mode == "local")
			throw new NotSupportedException ("Local test credentials will be supported with the local driver connection implementation.");
		if (state.Mode is not ("fleet" or "cloud") || string.IsNullOrWhiteSpace (state.RefreshToken)
			|| string.IsNullOrEmpty (state.SiteId) || !state.SiteId.All (c => c is >= '0' and <= '9')
			|| (state.Mode == "fleet" && (string.IsNullOrWhiteSpace (state.ClientId) || state.Region is not ("auto" or "na" or "eu" or "cn"))))
			throw new InvalidDataException ("Specify cloud or fleet, a dedicated test refresh token and numeric site ID; Fleet also requires its client ID and region.");
		}

	internal void Initialize (CredentialState state)
		{
		if (File.Exists (_path))
			throw new InvalidOperationException ("Initial credentials cannot overwrite an existing profile's rotated credentials.");
		Validate (state);
		state.AccessToken = null;
		state.InProgress = false;
		state.Prepared = false;
		Current = state;
		Save ();
		}

	internal void Begin ()
		{
		if (Current is null)
			throw new InvalidOperationException ("Initialize a dedicated test profile first.");
		if (Current.InProgress)
			throw new InvalidOperationException ("The previous operation did not complete. This profile is held for recovery; credentials will not be retried automatically.");
		Current.InProgress = true;
		Current.Prepared = false;
		// Commit before network I/O so a process failure cannot silently reuse an uncertain token.
		Save ();
		}

	internal void PersistTokens (string? accessToken, string? refreshToken)
		{
		lock (_gate)
			{
			if (Current is null || !Current.InProgress)
				throw new InvalidOperationException ("No credential operation is active.");
			if (!string.IsNullOrWhiteSpace (refreshToken))
				Current.RefreshToken = refreshToken;
			// Bootstrap notifications may omit accessToken; never keep an older access token then.
			Current.AccessToken = accessToken;
			Save ();
			}
		}

	internal void WriteInputs ()
		{
		if (Current is null || !Current.InProgress || string.IsNullOrWhiteSpace (Current.AccessToken))
			throw new InvalidOperationException ("No current access token is available for this run.");
		Directory.CreateDirectory (InputsDirectory);
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes (new
			{
			enabled = false,
			mode = Current.Mode,
			accessToken = Current.AccessToken,
			clientId = Current.ClientId,
			siteId = Current.SiteId,
			region = Current.Region
			}, JsonOptions);
		try
			{
			AtomicWrite (Path.Combine (InputsDirectory, "LiveTestSettings.json"), bytes);
			}
		finally { CryptographicOperations.ZeroMemory (bytes); }
		}

	internal void Complete ()
		{
		if (Current is null || !Current.InProgress)
			throw new InvalidOperationException ("No credential operation is active.");
		string inputs = Path.Combine (InputsDirectory, "LiveTestSettings.json");
		if (File.Exists (inputs))
			File.Delete (inputs);
		Current.InProgress = false;
		Current.Prepared = false;
		Save ();
		}

	internal void MarkPrepared ()
		{
		if (Current is null || !Current.InProgress || string.IsNullOrWhiteSpace (Current.AccessToken))
			throw new InvalidOperationException ("Authentication has not completed.");
		Current.Prepared = true;
		Save ();
		}

	internal void ReleaseStoppedRun ()
		{
		if (Current is null || !Current.InProgress || !Current.Prepared)
			throw new InvalidOperationException ("An uncertain authentication operation cannot be released as a stopped test run.");
		Complete ();
		}

	private void Save ()
		{
		byte[] clear = JsonSerializer.SerializeToUtf8Bytes (Current, JsonOptions);
		try
			{
			AtomicWrite (_path, ProtectedData.Protect (clear, null, DataProtectionScope.CurrentUser));
			}
		finally { CryptographicOperations.ZeroMemory (clear); }
		}

	private static void AtomicWrite (string path, byte[] bytes)
		{
		string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			using (var file = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				file.Write (bytes);
				file.Flush (flushToDisk: true);
				}
			if (File.Exists (path))
				File.Replace (temporary, path, null);
			else
				File.Move (temporary, path);
			}
		finally { if (File.Exists (temporary)) File.Delete (temporary); }
		}

	public void Dispose () => _lock.Dispose ();
	}