# TeslaPowerwallLibrary

A .NET client library for the Tesla™ Powerwall™ Energy Gateway, providing direct local-network access and Tesla Owners (cloud) API access to status, power flow, energy history, and control operations.

Tesla and Powerwall are trademarks of Tesla, Inc. This project is an independent, unofficial .NET library and is not affiliated with or endorsed by Tesla.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](TeslaPowerwallLibrary/LICENSE)

## Supported Platforms

| Target Framework | Supported |
| --- | --- |
| .NET 10 | ✅ |
| .NET Framework 4.7.2 | ✅ |

## Overview

`TeslaPowerwallLibrary` provides a strongly typed, async-first .NET wrapper around the Tesla Energy Gateway's local API and the Tesla Owners cloud API. It supports:

- Local network access to the Powerwall gateway (status, power flow, grid status, system status, and energy/calendar history)
- Tesla Owners (cloud) API access, including interactive OAuth login and automatic token persistence
- Control operations such as backup reserve level, operating mode, grid charging, grid export, and Storm Watch (cloud only)
- Response caching with configurable expiry to reduce load on the gateway
- Multi-target support for .NET Framework 4.7.2 and .NET 10

This .NET library is an independent implementation; behavioral and compatibility reference work in this project draws on the upstream [pypowerwall](https://pypi.org/project/pypowerwall/) project by Jason A. Cox and its public documentation.

### Connection modes

| Mode | Status | Notes |
| --- | --- | --- |
| Local | ✅ Implemented | Direct HTTPS/JSON access to the gateway using the customer password. Targets the Gateway 2 / Powerwall+ local REST API (see hardware note below). |
| Cloud | ✅ Implemented | Tesla Owners API access using interactive OAuth login and cached tokens. Works regardless of gateway generation. |
| FleetAPI | ✅ Implemented | Token-based access to the Tesla Fleet API using a caller-supplied Client ID and refresh token. Covers profile, energy product information, energy product commands (reserve, mode, grid charging, grid export), energy/calendar history, and vitals. Storm Watch is not exposed. No interactive login; the library persists tokens internally just like cloud mode (`FleetApiAuthPath`/`NoFleetApiTokenPersistence`). |
| TEDAPI | 🚧 Not yet implemented | Scaffolded protobuf-based local link-local access for a future milestone; currently throws a not-implemented exception. |

> **Gateway hardware compatibility:** Local mode's plain HTTPS/JSON REST API (`/api/login/Basic` plus endpoints such as `/api/system_status/soe` and `/api/meters/aggregates`) is the original Gateway 2 / Powerwall+ local interface and is well established on that hardware. On Powerwall 3, Tesla replaced this local REST API with TEDAPI (protobuf-encoded, RSA-signed); the plain REST endpoints return `403 Unable to GET to resource` on a Powerwall 3 gateway. Powerwall 3 owners need TEDAPI support (not yet implemented — see the table above) for local access; Cloud mode works today regardless of gateway generation.

## Installation

The library is available as the `TeslaPowerwallLibrary` NuGet package.

```powershell
dotnet add package TeslaPowerwallLibrary
```

## Quick Start

### Connect to a local gateway

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 Host = "10.0.1.99",
	 Password = "your-customer-password"
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();

var status = await powerwall.StatusAsync();
```

### Connect using the Tesla Owners cloud API

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 Email = "you@example.com",
	 CloudMode = true,
	 AccessToken = "your-access-token",
	 RefreshToken = "your-refresh-token"
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();
```

After the first successful cloud connect, the library persists the (possibly rotated) tokens internally, keyed by `Email`, so later runs can omit `AccessToken` and `RefreshToken` entirely. `AccessToken` is optional even on a first connect: when omitted (or stale), the library silently derives a new one from `RefreshToken`. When a non-empty `AuthPath` is supplied, that location is authoritative — no fallback is attempted, and an inaccessible path throws `PowerwallCloudTokenCacheStorageException` instead of silently continuing without persistence.

### Cloud mode without library-owned token storage

Set `NoCloudTokenPersistence` when the host has no suitable place for the library to keep a file (for example a Mono-hosted embedded environment). No cache file is ever read or written; `AuthPath` is ignored, and `Email` is not validated since it is otherwise used only as the cache key. Only `RefreshToken` needs to be supplied on every run — `AccessToken` remains optional and is silently (re)derived from it when absent or stale. Because `AccessToken` was not supplied here, `CloudTokensRefreshed` only fires when Tesla rotates the refresh token itself, and `e.AccessToken` is `null` in that case:

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 CloudMode = true,
	 RefreshToken = "your-refresh-token",
	 NoCloudTokenPersistence = true
};

using var powerwall = new Powerwall(options);
powerwall.CloudTokensRefreshed += (sender, e) =>
{
	 // Only raised here because RefreshToken alone was supplied above: fires when Tesla rotates the
	 // refresh token itself (not on every access-token renewal), and e.AccessToken is null.
	 // Persist e.RefreshToken using your own storage so the next run can reuse it.
};

await powerwall.ConnectAsync();
```

### Connect using Tesla FleetAPI

FleetAPI mode is token-based: supply a `FleetApiClientId` (registered at [developer.tesla.com](https://developer.tesla.com/)) and, on the first run, a `FleetApiRefreshToken` obtained separately via the Tesla FleetAPI OAuth flow. There is no interactive browser login for this mode, but the library persists the (possibly rotated) client id, tokens, and selected site internally, keyed by `Email`, the same way it does for cloud mode — later runs can omit `FleetApiRefreshToken` entirely. `FleetApiAccessToken` is optional even on a first connect: when omitted (or stale), the library silently derives a new one from the refresh token. When a non-empty `FleetApiAuthPath` is supplied, that location is authoritative — no fallback is attempted, and an inaccessible path throws `PowerwallFleetApiTokenCacheStorageException` instead of silently continuing without persistence:

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 Email = "you@example.com",
	 FleetApi = true,
	 FleetApiClientId = "your-client-id",
	 FleetApiRefreshToken = "your-refresh-token"
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();
```

#### FleetAPI mode without library-owned token storage

Set `NoFleetApiTokenPersistence` when the host has no suitable place for the library to keep a file (for example a Mono-hosted embedded environment). No cache file is ever read or written; `FleetApiAuthPath` is ignored. `FleetApiRefreshToken` must be supplied on every run — `FleetApiAccessToken` remains optional and is silently (re)derived from it when absent or stale. Because `FleetApiAccessToken` was not supplied here, `FleetApiTokensRefreshed` only fires when Tesla rotates the refresh token itself, and `e.AccessToken` is `null` in that case:

```csharp
using TeslaPowerwallLibrary;

var options = new PowerwallOptions
{
	 FleetApi = true,
	 FleetApiClientId = "your-client-id",
	 FleetApiRefreshToken = "your-refresh-token",
	 NoFleetApiTokenPersistence = true
};

using var powerwall = new Powerwall(options);
powerwall.FleetApiTokensRefreshed += (sender, e) =>
{
	 // Only raised here because FleetApiRefreshToken alone was supplied above: fires when Tesla rotates the
	 // refresh token itself (not on every access-token renewal), and e.AccessToken is null.
	 // Persist e.RefreshToken using your own storage so the next run can reuse it.
};

await powerwall.ConnectAsync();
```

FleetAPI mode covers profile, energy product information, energy product commands (backup reserve, operation mode, grid charging, and grid export), energy/calendar history, and vitals; Storm Watch is intentionally not exposed in FleetAPI mode.

### Obtaining a FleetAPI refresh token (`TeslaPowerwallLibrary.Login`)

For repeated Fleet authorization with an already registered application, the Setup app now offers **Sign in to Tesla**. It skips partner registration, can remember application settings encrypted for your Windows account, and automatically captures the callback and exchanges its code in an embedded Tesla sign-in window. A manual browser fallback is available. Initial application registration remains a separate option.

For credentials issued specifically to tests, see [dedicated test credentials](TeslaPowerwallLibrary.TestCredentials/README.md). Owner and Fleet profiles maintain their own refresh-token rotation; local test integration is reserved for the upcoming local access work. The helper is included in the 1.2.5 GitHub release; see its guide for usage and limitations.

The initial `FleetApiRefreshToken` isn't hand-entered from Tesla's docs — it comes from completing Tesla's FleetAPI OAuth setup once. `TeslaPowerwallLibrary.Login` exposes this as a small set of stateless, non-interactive steps adapted from upstream `pypowerwall`'s `fleetapi.setup()` wizard, via the static `TeslaFleetApiLogin` class. The library performs no browser automation and stores nothing itself — the caller supplies its own registered Client ID/Secret, domain, and redirect URI (from [developer.tesla.com](https://developer.tesla.com/)), opens the authorize URL itself, and captures the resulting authorization code:

```csharp
using TeslaPowerwallLibrary.Login;

// 1. Sanity-check that Tesla can reach your hosted PEM public key.
if (!await TeslaFleetApiLogin.VerifyPemKeyAsync("example.com"))
	 throw new InvalidOperationException("PEM key not reachable at https://example.com/.well-known/appspecific/com.tesla.3p.public-key.pem");

// 2. Generate a partner token (client_credentials grant) and register the partner account.
//    Registration is idempotent — safe to call again on an already-registered domain.
var partnerToken = await TeslaFleetApiLogin.GetPartnerTokenAsync(clientId, clientSecret, audience);
var registration = await TeslaFleetApiLogin.RegisterPartnerAccountAsync(partnerToken.PartnerToken!, audience, "example.com");

// 3. Build the authorize URL, have the user visit it, and capture the "code" from the redirect
//    to your own registered redirect URI.
var (authorizeUrl, state) = TeslaFleetApiLogin.BuildAuthorizeUrl(clientId, redirectUri);

// 4. Exchange the authorization code for FleetAPI tokens.
var login = await TeslaFleetApiLogin.ExchangeCodeAsync(clientId, clientSecret, code, redirectUri, audience);
if (login.Status == TeslaFleetApiLoginStatus.Success)
	 {
	 // login.Tokens.AccessToken / login.Tokens.RefreshToken — pass RefreshToken as
	 // PowerwallOptions.FleetApiRefreshToken to connect, or display/copy it for later use.
	 }
```

`audience` is the regional FleetAPI base URL matching `PowerwallOptions.FleetApiRegion` (`https://fleet-api.prd.na.vn.cloud.tesla.com`, `.eu.`, or `.cn.`). The test console's interactive `login fleetapisetup` command drives this same flow end-to-end, prompting for the Client ID/Secret, domain, and redirect URI, then connecting with the resulting refresh token. When running the console interactively in FleetAPI mode (`--fleet-api`) with no cached or supplied refresh token, it now offers to run this same setup wizard automatically, mirroring how cloud mode offers the browser login.

### Read energy and calendar history (cloud mode only)

`GetCalendarHistoryAsync` returns the raw JSON body for any history `kind` (`power`, `soe`, `energy`, `backup`, `self_consumption`, `time_of_use_energy`, or `savings`), mirroring the upstream Python library's behavior. For the kinds with a verified, stable schema, typed convenience methods deserialize that JSON directly into strongly typed records (via Newtonsoft.Json `[JsonProperty]` mappings, no hand-written parsing) so callers do not need to do it themselves:

```csharp
IReadOnlyList<EnergyHistoryPoint> energy = await powerwall.GetEnergyCalendarHistoryAsync(HistoryPeriod.Day);
IReadOnlyList<PowerHistoryPoint> power = await powerwall.GetPowerCalendarHistoryAsync(HistoryPeriod.Day);
IReadOnlyList<StateOfEnergyHistoryPoint> soe = await powerwall.GetStateOfEnergyCalendarHistoryAsync(HistoryPeriod.Day);
IReadOnlyList<SelfConsumptionHistoryPoint> selfConsumption = await powerwall.GetSelfConsumptionCalendarHistoryAsync(HistoryPeriod.Day);
BackupHistory backup = await powerwall.GetBackupCalendarHistoryAsync(HistoryPeriod.Day);
```

Each record exposes Tesla's raw fields plus a few computed convenience properties layered on top — for example `EnergyHistoryPoint` sums and converts the raw watt-hour fields into `SolarKwh`, `HomeKwh`, `FromGridKwh`, `ToGridKwh`, `BatteryChargeKwh`, and `BatteryDischargeKwh`.

`time_of_use_energy` and `savings` have no typed model yet (Tesla returns an empty payload for both unless a time-of-use tariff is configured); call `GetCalendarHistoryAsync` directly for those. `GetHistoryAsync` (the older, non-calendar-aligned `/history` endpoint) has been permanently removed by Tesla and always throws `PowerwallCloudEndpointRemovedException`; use the calendar-history methods above instead.

## Repository Contents

- `TeslaPowerwallLibrary` — the main library project published to NuGet
- `TeslaPowerwallLibrary.Login` — shared Tesla cloud OAuth login library (interactive WebView2-based browser login), used by both the app and the test console; not published to NuGet, distributed as a DLL attached to each [GitHub release](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases) — see the [Tesla cloud login guide](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html)
- `TeslaPowerwallLibrary.App` — a WPF dashboard application with live energy charts, system status, and site/account management
- `TeslaPowerwallLibrary.TestConsole` — a command-line and interactive test harness covering the library's read and control operations
- `TeslaPowerwallLibrary.Setup` — a small standalone WPF app wrapping `TeslaPowerwallLibrary.Login` that performs Tesla cloud login or the FleetAPI setup/registration wizard (partner token, partner registration, PEM verification, authorize, and code exchange) and displays the resulting tokens
- `TeslaPowerwallLibrary.Tests` — NUnit-based deterministic unit test coverage

## Documentation

Public documentation for this repository is available on GitHub Pages:

- https://oznetmaster.github.io/TeslaPowerwallLibrary/

See [CHANGELOG.md](CHANGELOG.md) for release history and [RELEASE-NOTES.md](RELEASE-NOTES.md) for this release.

## Acknowledgements

Behavioral and compatibility reference work in this project draws on the upstream [pypowerwall](https://pypi.org/project/pypowerwall/) project by Jason A. Cox and its public documentation. The Tesla OAuth 2.0 PKCE login flow also references [tesla_auth](https://github.com/adriankumpf/tesla_auth) (Rust) by Adrian Kumpf.

## License

MIT © 2026 Neil Colvin — see [LICENSE](TeslaPowerwallLibrary/LICENSE).


## Unit tests

`TeslaPowerwallLibrary.Tests` uses official NUnit 4.6.1 and NUnit3TestAdapter, targeting `net472` and `net10.0`. Run the tests in Visual Studio Test Explorer, or:

```powershell
dotnet test TeslaPowerwallLibrary.Tests/TeslaPowerwallLibrary.Tests.csproj -c Release
```

The suite covers facade guards, mode selection, token-cache behavior, model deserialization, history parsing, host validation and public enum contracts. It does not operate a Powerwall or require live credentials. The MSTest-to-NUnit migration keeps the fixtures in this repository and does not require a new library or NuGet release.

## Continuous integration tests

The [Unit tests workflow](.github/workflows/unit-tests.yml) runs on pull requests and pushes to the main development branch. Separate Windows jobs test **net472** and **.NET 10**, retaining a result file for each suite/runtime. Live tests are excluded; no account credentials or physical devices are needed. These checks do not publish packages or releases.

### Fleet account region (1.2.5)

Fleet connections default to automatic account-region discovery through Tesla's authenticated user-region endpoint before reading sites. The account region can differ from the physical site's location. Explicit FleetApiRegion values na, eu and cn remain supported; China requires its separate registration and explicit region. Automatic discovery rejects missing, mismatched or unrecognized endpoint responses instead of silently using a region. The region is resolved once per connection. Windows and remote read-only live validation passed; EU routing is covered by simulated HTTP tests, not an EU-account hardware test.
