using Microsoft.Extensions.Logging;
// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See TeslaPowerwallLibrary/LICENSE.

namespace TeslaPowerwallLibrary.TestCredentials;

internal static class TestPreparation
	{
	internal static PowerwallOptions OptionsFor (CredentialState state, ILogger? logger = null)
		{
		CredentialStore.Validate (state);
		bool fleet = state.Mode == "fleet";
		return new PowerwallOptions
			{
			Logger = logger,
			FleetApi = fleet,
			CloudMode = !fleet,
			FleetApiClientId = fleet ? state.ClientId : null,
			FleetApiRefreshToken = fleet ? state.RefreshToken : null,
			FleetApiAccessToken = fleet ? state.AccessToken : null,
			RefreshToken = fleet ? null : state.RefreshToken,
			AccessToken = fleet ? null : state.AccessToken,
			FleetApiRegion = state.Region,
			SiteId = state.SiteId,
			NoFleetApiTokenPersistence = true,
			NoCloudTokenPersistence = true,
			Timeout = TimeSpan.FromSeconds (25)
			};
		}

	internal static async Task PrepareAsync (CredentialStore store, CancellationToken cancellationToken,
		Func<PowerwallOptions, ICredentialConnection>? connectionFactory = null, ILogger? logger = null)
		{
		store.Begin ();
		CredentialState state = store.Current!;
		using var client = (connectionFactory ?? (options => new LibraryCredentialConnection (options))) (OptionsFor (state, logger));
		// Persist synchronously before the library can proceed after a rotation notification.
		Exception? persistenceFailure = null;
		void Persist (string? access, string? refresh)
			{
			try
				{
				store.PersistTokens (access, refresh);
				}
			catch (Exception exception) { persistenceFailure = exception; throw; }
			}
		client.TokensChanged += Persist;
		if (!await client.ConnectAsync (cancellationToken) || persistenceFailure is not null)
			throw new InvalidOperationException ("Test authentication did not complete. The credential profile remains held for recovery.");
		if (client.SiteId != state.SiteId)
			throw new InvalidOperationException ("The authenticated site did not match the configured test site.");
		store.PersistTokens (client.AccessToken, client.RefreshToken);
		store.WriteInputs ();
		}
	}

internal interface ICredentialConnection : IDisposable
	{
	event Action<string?, string?>? TokensChanged;
	Task<bool> ConnectAsync (CancellationToken cancellationToken);
	string? SiteId
		{
		get;
		}
	string? AccessToken
		{
		get;
		}
	string? RefreshToken
		{
		get;
		}
	}

internal sealed class LibraryCredentialConnection : ICredentialConnection
	{
	private readonly Powerwall _client;
	private readonly bool _fleet;
	internal LibraryCredentialConnection (PowerwallOptions options)
		{
		_fleet = options.FleetApi;
		_client = new Powerwall (options);
		_client.FleetApiTokensRefreshed += (_, tokens) => TokensChanged?.Invoke (tokens.AccessToken, tokens.RefreshToken);
		_client.CloudTokensRefreshed += (_, tokens) => TokensChanged?.Invoke (tokens.AccessToken, tokens.RefreshToken);
		}
	public event Action<string?, string?>? TokensChanged;
	public Task<bool> ConnectAsync (CancellationToken cancellationToken) => _client.ConnectAsync (cancellationToken);
	public string? SiteId => _fleet ? _client.FleetApiSiteId : _client.CloudSiteId;
	public string? AccessToken => _fleet ? _client.FleetApiAccessToken : _client.CloudAccessToken;
	public string? RefreshToken => _fleet ? _client.FleetApiRefreshToken : _client.CloudRefreshToken;
	public void Dispose () => _client.Dispose ();
	}