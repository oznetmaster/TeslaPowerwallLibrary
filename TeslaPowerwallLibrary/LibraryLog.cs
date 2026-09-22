using Microsoft.Extensions.Logging;

namespace TeslaPowerwallLibrary;

// Generated logging implementations check the level before formatting and preserve caller scopes.
internal static partial class LibraryLog
	{
	[LoggerMessage (EventId = 1, Level = LogLevel.Warning, Message = "Failed to connect using Local mode: {Message1}")]
	internal static partial void FailedToConnectUsingLocalMode (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 2, Level = LogLevel.Warning, Message = "Failed to connect using Cloud mode: {Message1}")]
	internal static partial void FailedToConnectUsingCloudMode (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 3, Level = LogLevel.Warning, Message = "Failed to connect using FleetAPI mode: {Message1}")]
	internal static partial void FailedToConnectUsingFleetAPIMode (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 4, Level = LogLevel.Error, Message = "Unable to determine mode to connect.")]
	internal static partial void UnableToDetermineModeToConnect (ILogger logger);

	[LoggerMessage (EventId = 5, Level = LogLevel.Debug, Message = "Tesla cloud mode enabled")]
	internal static partial void TeslaCloudModeEnabled (ILogger logger);

	[LoggerMessage (EventId = 6, Level = LogLevel.Debug, Message = "Connected to Tesla cloud - using site {ResolvedSiteId1} for {Email2}")]
	internal static partial void ConnectedToTeslaCloudUsingSiteFor (ILogger logger, object? ResolvedSiteId1, object? Email2);

	[LoggerMessage (EventId = 7, Level = LogLevel.Error, Message = "Invalid siteid - value is null or empty.")]
	internal static partial void InvalidSiteidValueIsNullOrEmpty (ILogger logger);

	[LoggerMessage (EventId = 8, Level = LogLevel.Error, Message = "No sites found for {Email1}.")]
	internal static partial void NoSitesFoundFor (ILogger logger, object? Email1);

	[LoggerMessage (EventId = 9, Level = LogLevel.Debug, Message = "Changed site to {SiteId1} ({SiteName2}) for {Email3}")]
	internal static partial void ChangedSiteToFor (ILogger logger, object? SiteId1, object? SiteName2, object? Email3);

	[LoggerMessage (EventId = 10, Level = LogLevel.Error, Message = "Site {SiteId1} not found for {Email2}.")]
	internal static partial void SiteNotFoundFor (ILogger logger, object? SiteId1, object? Email2);

	[LoggerMessage (EventId = 11, Level = LogLevel.Debug, Message = "Raw poll is not supported in cloud mode for {Api1}")]
	internal static partial void RawPollIsNotSupportedInCloudModeFor (ILogger logger, object? Api1);

	[LoggerMessage (EventId = 12, Level = LogLevel.Debug, Message = "Request for {Api}")]
	internal static partial void RequestFor (ILogger logger, object? Api);

	[LoggerMessage (EventId = 13, Level = LogLevel.Debug, Message = "API {Api} uses simulated data")]
	internal static partial void APIUsesSimulatedData (ILogger logger, object? Api);

	[LoggerMessage (EventId = 14, Level = LogLevel.Debug, Message = " -- cloud: Request for {Api1}")]
	internal static partial void CloudRequestFor (ILogger logger, object? Api1);

	[LoggerMessage (EventId = 15, Level = LogLevel.Debug, Message = " -- cloud: Returning cached {Name1} data")]
	internal static partial void CloudReturningCachedData (ILogger logger, object? Name1);

	[LoggerMessage (EventId = 16, Level = LogLevel.Debug, Message = " -- cloud: Retrieved {Name1} data")]
	internal static partial void CloudRetrievedData (ILogger logger, object? Name1);

	[LoggerMessage (EventId = 17, Level = LogLevel.Warning, Message = "Site {SiteId1} not found for {Email2} - defaulting to first site.")]
	internal static partial void SiteNotFoundForDefaultingToFirstSite (ILogger logger, object? SiteId1, object? Email2);

	[LoggerMessage (EventId = 18, Level = LogLevel.Error, Message = "Unable to refresh Tesla cloud token: {Message1}")]
	internal static partial void UnableToRefreshTeslaCloudToken (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 19, Level = LogLevel.Error, Message = "Tesla cloud token refresh failed (HTTP {StatusCode1}).")]
	internal static partial void TeslaCloudTokenRefreshFailedHTTP (ILogger logger, object? StatusCode1);

	[LoggerMessage (EventId = 20, Level = LogLevel.Error, Message = "Tesla cloud token refresh response did not contain an access token.")]
	internal static partial void TeslaCloudTokenRefreshResponseDidNotContainAn (ILogger logger);

	[LoggerMessage (EventId = 21, Level = LogLevel.Debug, Message = "Tesla cloud access token refreshed.")]
	internal static partial void TeslaCloudAccessTokenRefreshed (ILogger logger);

	[LoggerMessage (EventId = 22, Level = LogLevel.Error, Message = "Unable to parse Tesla cloud token refresh response: {Message1}")]
	internal static partial void UnableToParseTeslaCloudTokenRefreshResponse (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 23, Level = LogLevel.Error, Message = "Unable to map API response to {ResponseType}")]
	internal static partial void UnableToMapAPIResponseTo (ILogger logger, object? ResponseType);

	[LoggerMessage (EventId = 24, Level = LogLevel.Error, Message = "Timeout waiting for Tesla cloud API {Uri1}")]
	internal static partial void TimeoutWaitingForTeslaCloudAPI (ILogger logger, object? Uri1);

	[LoggerMessage (EventId = 25, Level = LogLevel.Error, Message = "Unable to connect to Tesla cloud API {Uri1} - {Message2}")]
	internal static partial void UnableToConnectToTeslaCloudAPI (ILogger logger, object? Uri1, object? Message2);

	[LoggerMessage (EventId = 26, Level = LogLevel.Debug, Message = "Tesla cloud session expired - attempting token refresh")]
	internal static partial void TeslaCloudSessionExpiredAttemptingTokenRefresh (ILogger logger);

	[LoggerMessage (EventId = 27, Level = LogLevel.Error, Message = "Tesla cloud API {Uri1} unauthorized and token refresh failed - run setup to renew tokens")]
	internal static partial void TeslaCloudAPIUnauthorizedAndTokenRefreshFailedRun (ILogger logger, object? Uri1);

	[LoggerMessage (EventId = 28, Level = LogLevel.Error, Message = "Tesla cloud API {Uri1} returned HTTP 410 (Gone) - endpoint permanently removed")]
	internal static partial void TeslaCloudAPIReturnedHTTPGoneEndpointPermanentlyRemoved (ILogger logger, object? Uri1);

	[LoggerMessage (EventId = 29, Level = LogLevel.Error, Message = "Tesla cloud API {Uri1} returned HTTP {StatusCode2}")]
	internal static partial void TeslaCloudAPIReturnedHTTP (ILogger logger, object? Uri1, object? StatusCode2);

	[LoggerMessage (EventId = 30, Level = LogLevel.Warning, Message = "Unable to read Tesla cloud token cache '{FilePath1}': {Message2}")]
	internal static partial void UnableToReadTeslaCloudTokenCache (ILogger logger, object? FilePath1, object? Message2);

	[LoggerMessage (EventId = 31, Level = LogLevel.Warning, Message = "Unable to write Tesla cloud token cache '{FilePath1}': {Message2}")]
	internal static partial void UnableToWriteTeslaCloudTokenCache (ILogger logger, object? FilePath1, object? Message2);

	[LoggerMessage (EventId = 32, Level = LogLevel.Error, Message = "Unable to refresh Tesla FleetAPI token: {Message1}")]
	internal static partial void UnableToRefreshTeslaFleetAPIToken (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 33, Level = LogLevel.Error, Message = "Tesla FleetAPI token refresh failed (HTTP {StatusCode1}).")]
	internal static partial void TeslaFleetAPITokenRefreshFailedHTTP (ILogger logger, object? StatusCode1);

	[LoggerMessage (EventId = 34, Level = LogLevel.Error, Message = "Tesla FleetAPI token refresh response did not contain an access token.")]
	internal static partial void TeslaFleetAPITokenRefreshResponseDidNotContainAn (ILogger logger);

	[LoggerMessage (EventId = 35, Level = LogLevel.Debug, Message = "Tesla FleetAPI access token refreshed.")]
	internal static partial void TeslaFleetAPIAccessTokenRefreshed (ILogger logger);

	[LoggerMessage (EventId = 36, Level = LogLevel.Error, Message = "Unable to parse Tesla FleetAPI token refresh response: {Message1}")]
	internal static partial void UnableToParseTeslaFleetAPITokenRefreshResponse (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 37, Level = LogLevel.Error, Message = "Tesla FleetAPI account region could not be resolved to a recognized endpoint.")]
	internal static partial void TeslaFleetAPIAccountRegionCouldNotBeResolvedTo (ILogger logger);

	[LoggerMessage (EventId = 38, Level = LogLevel.Error, Message = "Timeout waiting for Tesla FleetAPI {Uri1}")]
	internal static partial void TimeoutWaitingForTeslaFleetAPI (ILogger logger, object? Uri1);

	[LoggerMessage (EventId = 39, Level = LogLevel.Error, Message = "Unable to connect to Tesla FleetAPI {Uri1} - {Message2}")]
	internal static partial void UnableToConnectToTeslaFleetAPI (ILogger logger, object? Uri1, object? Message2);

	[LoggerMessage (EventId = 40, Level = LogLevel.Debug, Message = "Tesla FleetAPI session expired - attempting token refresh")]
	internal static partial void TeslaFleetAPISessionExpiredAttemptingTokenRefresh (ILogger logger);

	[LoggerMessage (EventId = 41, Level = LogLevel.Error, Message = "Tesla FleetAPI {Uri1} unauthorized and token refresh failed - supply a valid refresh token")]
	internal static partial void TeslaFleetAPIUnauthorizedAndTokenRefreshFailedSupplyA (ILogger logger, object? Uri1);

	[LoggerMessage (EventId = 42, Level = LogLevel.Error, Message = "Tesla FleetAPI {Uri1} returned HTTP 410 (Gone) - endpoint permanently removed")]
	internal static partial void TeslaFleetAPIReturnedHTTPGoneEndpointPermanentlyRemoved (ILogger logger, object? Uri1);

	[LoggerMessage (EventId = 43, Level = LogLevel.Error, Message = "Tesla FleetAPI {Uri1} returned HTTP {StatusCode2}")]
	internal static partial void TeslaFleetAPIReturnedHTTP (ILogger logger, object? Uri1, object? StatusCode2);

	[LoggerMessage (EventId = 44, Level = LogLevel.Debug, Message = "Tesla FleetAPI mode enabled")]
	internal static partial void TeslaFleetAPIModeEnabled (ILogger logger);

	[LoggerMessage (EventId = 45, Level = LogLevel.Debug, Message = "Connected to Tesla FleetAPI - using site {ResolvedSiteId1} for {Email2}")]
	internal static partial void ConnectedToTeslaFleetAPIUsingSiteFor (ILogger logger, object? ResolvedSiteId1, object? Email2);

	[LoggerMessage (EventId = 46, Level = LogLevel.Debug, Message = "Changed site to {SiteId1} for {Email2}")]
	internal static partial void ChangedSiteToFor46 (ILogger logger, object? SiteId1, object? Email2);

	[LoggerMessage (EventId = 47, Level = LogLevel.Debug, Message = "Raw poll is not supported in FleetAPI mode for {Api1}")]
	internal static partial void RawPollIsNotSupportedInFleetAPIModeFor (ILogger logger, object? Api1);

	[LoggerMessage (EventId = 48, Level = LogLevel.Debug, Message = " -- fleetapi: Request for {Api1}")]
	internal static partial void FleetapiRequestFor (ILogger logger, object? Api1);

	[LoggerMessage (EventId = 49, Level = LogLevel.Debug, Message = " -- fleetapi: Returning cached {Name1} data")]
	internal static partial void FleetapiReturningCachedData (ILogger logger, object? Name1);

	[LoggerMessage (EventId = 50, Level = LogLevel.Debug, Message = " -- fleetapi: Retrieved {Name1} data")]
	internal static partial void FleetapiRetrievedData (ILogger logger, object? Name1);

	[LoggerMessage (EventId = 51, Level = LogLevel.Warning, Message = "Unable to read Tesla FleetAPI token cache '{FilePath1}': {Message2}")]
	internal static partial void UnableToReadTeslaFleetAPITokenCache (ILogger logger, object? FilePath1, object? Message2);

	[LoggerMessage (EventId = 52, Level = LogLevel.Warning, Message = "Unable to write Tesla FleetAPI token cache '{FilePath1}': {Message2}")]
	internal static partial void UnableToWriteTeslaFleetAPITokenCache (ILogger logger, object? FilePath1, object? Message2);

	[LoggerMessage (EventId = 53, Level = LogLevel.Debug, Message = "Tesla local mode enabled")]
	internal static partial void TeslaLocalModeEnabled (ILogger logger);

	[LoggerMessage (EventId = 54, Level = LogLevel.Debug, Message = "Error during logout: {Message1}")]
	internal static partial void ErrorDuringLogout (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 55, Level = LogLevel.Debug, Message = "Rate limit cooldown period - Pausing API calls")]
	internal static partial void RateLimitCooldownPeriodPausingAPICalls (ILogger logger);

	[LoggerMessage (EventId = 56, Level = LogLevel.Error, Message = "Timeout waiting for Powerwall API {Api1} - check network connectivity to {Host2}")]
	internal static partial void TimeoutWaitingForPowerwallAPICheckNetworkConnectivityTo (ILogger logger, object? Api1, object? Host2);

	[LoggerMessage (EventId = 57, Level = LogLevel.Error, Message = "Unable to connect to Powerwall at {Host1} - {Message2} - check that the gateway is reachable and powered on")]
	internal static partial void UnableToConnectToPowerwallAtCheckThatThe (ILogger logger, object? Host1, object? Message2);

	[LoggerMessage (EventId = 58, Level = LogLevel.Debug, Message = "Empty response from Powerwall at {Url1}")]
	internal static partial void EmptyResponseFromPowerwallAt (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 59, Level = LogLevel.Debug, Message = "ERROR Timeout waiting for Powerwall API {Url1}")]
	internal static partial void ERRORTimeoutWaitingForPowerwallAPI (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 60, Level = LogLevel.Debug, Message = "ERROR Unable to connect to Powerwall at {Url1}: {Message2}")]
	internal static partial void ERRORUnableToConnectToPowerwallAt (ILogger logger, object? Url1, object? Message2);

	[LoggerMessage (EventId = 61, Level = LogLevel.Error, Message = "{Err1} - check that the gateway is reachable on the network")]
	internal static partial void CheckThatTheGatewayIsReachableOnTheNetwork (ILogger logger, object? Err1);

	[LoggerMessage (EventId = 62, Level = LogLevel.Warning, Message = "Login failed: HTTP {StatusCode1}")]
	internal static partial void LoginFailedHTTP (ILogger logger, object? StatusCode1);

	[LoggerMessage (EventId = 63, Level = LogLevel.Warning, Message = "Login failed: {Message1}")]
	internal static partial void LoginFailed (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 64, Level = LogLevel.Debug, Message = "loaded auth from cache file {CacheFile1} ({AuthMode2} authmode)")]
	internal static partial void LoadedAuthFromCacheFileAuthmode (ILogger logger, object? CacheFile1, object? AuthMode2);

	[LoggerMessage (EventId = 65, Level = LogLevel.Debug, Message = "no auth cache file: {Message1}")]
	internal static partial void NoAuthCacheFile (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 66, Level = LogLevel.Debug, Message = "unable to cache auth session - continuing: {Message1}")]
	internal static partial void UnableToCacheAuthSessionContinuing (ILogger logger, object? Message1);

	[LoggerMessage (EventId = 67, Level = LogLevel.Error, Message = "404 Powerwall API not found at {Url1}")]
	internal static partial void PowerwallAPINotFoundAt (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 68, Level = LogLevel.Error, Message = "Firmware {Version1} detected - Does not support vitals API - disabling.")]
	internal static partial void FirmwareDetectedDoesNotSupportVitalsAPIDisabling (ILogger logger, object? Version1);

	[LoggerMessage (EventId = 69, Level = LogLevel.Error, Message = "429 Rate limited by Powerwall API at {Url1} - Activating 5 minute cooldown")]
	internal static partial void RateLimitedByPowerwallAPIAtActivatingMinuteCooldown (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 70, Level = LogLevel.Debug, Message = "Session Expired - Trying to get a new one")]
	internal static partial void SessionExpiredTryingToGetANewOne (ILogger logger);

	[LoggerMessage (EventId = 71, Level = LogLevel.Error, Message = "Unable to establish session with Powerwall at {Url1} - check password")]
	internal static partial void UnableToEstablishSessionWithPowerwallAtCheckPassword (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 72, Level = LogLevel.Error, Message = "403 Unauthorized by Powerwall API at {Url1} - Endpoint disabled in this firmware or user lacks permission")]
	internal static partial void UnauthorizedByPowerwallAPIAtEndpointDisabledInThis (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 73, Level = LogLevel.Error, Message = "503 Service Unavailable at {Url1} - Activating 5 minute API cooldown")]
	internal static partial void ServiceUnavailableAtActivatingMinuteAPICooldown (ILogger logger, object? Url1);

	[LoggerMessage (EventId = 74, Level = LogLevel.Error, Message = "Unhandled HTTP response code {Status1} at {Url2}")]
	internal static partial void UnhandledHTTPResponseCodeAt (ILogger logger, object? Status1, object? Url2);

	[LoggerMessage (EventId = 75, Level = LogLevel.Error, Message = "Server-side problem at Powerwall API (status code {Status1}) at {Url2}")]
	internal static partial void ServerSideProblemAtPowerwallAPIStatusCodeAt (ILogger logger, object? Status1, object? Url2);

	[LoggerMessage (EventId = 76, Level = LogLevel.Debug, Message = " -- local: Returning cached {Api1}")]
	internal static partial void LocalReturningCached (ILogger logger, object? Api1);

	}