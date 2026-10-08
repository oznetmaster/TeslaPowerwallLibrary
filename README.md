# TeslaPowerwallLibrary

**Version 2.1.0** adds local TEDAPI support. Existing 2.0 callers retain their public API and classic gateway defaults. See [upgrading to 2.1](UPGRADING-2.1.md).

For shipped changes, see the [changelog](CHANGELOG.md). Test, CI and build history is recorded separately in [development and validation history](DEVELOPMENT-HISTORY.md).


A .NET client library for the Tesla™ Powerwall™ Energy Gateway, providing classic gateway and TEDAPI local access alongside Tesla Owner and Fleet cloud APIs to status, power flow, energy history, and control operations.

Tesla and Powerwall are trademarks of Tesla, Inc. This project is an independent, unofficial .NET library and is not affiliated with or endorsed by Tesla.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](TeslaPowerwallLibrary/LICENSE)

## Supported Platforms

| Target Framework | Supported |
| --- | --- |
| .NET 10 | ✅ |
| .NET Framework 4.7.2 | ✅ |

## Overview

`TeslaPowerwallLibrary` provides a strongly typed, async-first .NET wrapper around the classic gateway, local TEDAPI, Tesla Owner and Tesla Fleet APIs. It supports:

- Local gateway and TEDAPI telemetry, diagnostics and explicit controls; calendar history uses Owner or Fleet
- Signed Powerwall 3 LAN access, separate cloud-assisted key enrollment, and hostname/IP configuration with optional discovery
- Nullable power readings that distinguish unreported values from measured zeroes
- Tesla Owners (cloud) API access, with OAuth credentials acquired through the companion login tools and optional token persistence
- Control operations such as backup reserve level, operating mode, grid charging, grid export, and Storm Watch (cloud only)
- Response caching with configurable expiry to reduce load on the gateway
- Multi-target support for .NET Framework 4.7.2 and .NET 10

This .NET library is an independent implementation; behavioral and compatibility reference work in this project draws on the upstream [pypowerwall](https://pypi.org/project/pypowerwall/) project by Jason A. Cox and its public documentation.

### Connection modes

| Connection | Main advantage | Main limitation |
| --- | --- | --- |
| Owner cloud (`CloudMode`) | Remote site access, calendar history and Storm Watch control. | Internet and Tesla OAuth credentials required; readings reflect cloud updates. |
| Fleet cloud (`FleetApi`) | Remote site access and calendar history through a registered Fleet application. | Requires application setup and user authorization; this library does not expose Fleet Storm Watch. |
| Classic local (`Gateway`) | Direct customer-authenticated reads without cloud credentials or a signing key. | Available readings and controls depend on hardware and firmware; basic Powerwall 3 reads do not provide full TEDAPI diagnostics. |
| Local TEDAPI | Fresh device telemetry and detailed diagnostics without cloud access during operation. | Requires a compatible local transport and credentials; it does not provide the cloud calendar-history archive. |

To obtain Owner or Fleet credentials, follow [Using the Setup app](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html#using-the-setup-app). The standalone Windows tool is provided in the GitHub release assets.

See [choosing a connection](LOCAL-ACCESS.md#choosing-a-connection) for credentials, advantages and limitations of each local transport, data freshness, and combining local readings with cloud history.

> **Gateway hardware compatibility:** Available endpoints depend on hardware and firmware. Basic customer-authenticated power, charge and grid readings and signed TEDAPI LAN reads have been exercised on a Powerwall 3. An unavailable endpoint or omitted reading remains unavailable. See [local access and validation limits](LOCAL-ACCESS.md).

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
	 Password = "your-customer-password",
	 AllowLocalControl = false,
	 NoLocalSessionPersistence = true
};

using var powerwall = new Powerwall(options);
await powerwall.ConnectAsync();

var readings = await powerwall.GetPowerReadingsAsync();
// Each power value is in watts; null means unavailable, not zero.
```

### Connect to Powerwall 3 over signed LAN

Select `PowerwallLocalProtocol.TedapiSigned` and supply the local customer password plus a previously verified, caller-owned RSA-4096 key. See the [local-access guide](LOCAL-ACCESS.md) for a complete example, enrollment through an existing Owner or Fleet account, discovery and query selection. Key enrollment is a separate authorization operation; normal local connections do not use the cloud.

The library reads only when called. `CacheExpireSeconds` controls response reuse, not a polling loop. The consumer chooses when to read. `GetPowerReadingsAsync()` preserves missing flows as null; `PowerAsync()` retains its released zero-default contract for existing callers.

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

FleetAPI mode is token-based: supply a `FleetApiClientId` (registered at [developer.tesla.com](https://developer.tesla.com/)) and, on the first run, a `FleetApiRefreshToken` obtained separately via the Tesla FleetAPI OAuth flow. The core library accepts tokens; the companion Setup app provides interactive Fleet sign-in (see [Using the Setup app](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html#using-the-setup-app)). The library persists the (possibly rotated) client id, tokens, and selected site internally, keyed by `Email`, the same way it does for cloud mode — later runs can omit `FleetApiRefreshToken` entirely. `FleetApiAccessToken` is optional even on a first connect: when omitted (or stale), the library silently derives a new one from the refresh token. When a non-empty `FleetApiAuthPath` is supplied, that location is authoritative — no fallback is attempted, and an inaccessible path throws `PowerwallFleetApiTokenCacheStorageException` instead of silently continuing without persistence:

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

### Application-controlled logging

Set `PowerwallOptions.Logger` to a caller-owned `Microsoft.Extensions.Logging.ILogger`.
The same logger is used by the facade, backend, transport and token cache, retaining its
category and active scopes. The library uses only the logging abstractions package,
configures no providers, and never disposes your logger. Omitting it disables logging.

```csharp
var logger = loggerFactory.CreateLogger("Powerwall.House");
using var scope = logger.BeginScope("House energy connection");
using var powerwall = new Powerwall(new PowerwallOptions
{
    FleetApi = true,
    FleetApiClientId = "your-client-id",
    FleetApiRefreshToken = "your-refresh-token",
    NoFleetApiTokenPersistence = true,
    Logger = logger
});
await powerwall.ConnectAsync();
```

Here `loggerFactory` is supplied by the host application. The local test console supplies
its own console logger when verbose logging is enabled; the credential helper supplies a
sanitizing logger for authentication diagnostics. See [migration notes](https://github.com/oznetmaster/TeslaPowerwallLibrary/blob/v2.0.0/MIGRATION-SystemTextJson.md)
for serialization compatibility and consumer dependency changes.

### Obtaining a FleetAPI refresh token (`TeslaPowerwallLibrary.Login`)

For the provided Windows tool, follow [Using the Setup app](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html#using-the-setup-app). The code below is for applications integrating the login helper directly.

For repeated Fleet authorization with an already registered application, the Setup app now offers **Sign in to Tesla**. It skips partner registration, can remember application settings encrypted for your Windows account, and automatically captures the callback and exchanges its code in an embedded Tesla sign-in window. A manual browser fallback is available. Initial application registration remains a separate option.

For credentials issued specifically to tests, see [dedicated test credentials](TeslaPowerwallLibrary.TestCredentials/README.md). Owner and Fleet profiles maintain their own refresh-token rotation; local hardware fixtures use separately configured, encrypted local credentials. The helper is included in the GitHub release assets; see its guide for usage and limitations.

The initial `FleetApiRefreshToken` isn't hand-entered from Tesla's docs — it comes from completing Tesla's FleetAPI OAuth setup once. `TeslaPowerwallLibrary.Login` exposes this as a small set of stateless, non-interactive steps using upstream `pypowerwall`'s `fleetapi.setup()` wizard as a protocol reference, via the static `TeslaFleetApiLogin` class. The library performs no browser automation and stores nothing itself — the caller supplies its own registered Client ID/Secret, domain, and redirect URI (from [developer.tesla.com](https://developer.tesla.com/)), opens the authorize URL itself, and captures the resulting authorization code:

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

### Read energy and calendar history (Owner or Fleet)

`GetCalendarHistoryAsync` returns the raw JSON body for any history `kind` (`power`, `soe`, `energy`, `backup`, `self_consumption`, `time_of_use_energy`, or `savings`), mirroring the upstream Python library's behavior. For the kinds with a verified, stable schema, typed convenience methods deserialize that JSON directly into strongly typed records (via System.Text.Json `[JsonPropertyName]` mappings, no hand-written parsing) so callers do not need to do it themselves:

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
- `TeslaPowerwallLibrary.App` — a WPF desktop app with live energy charts, system status, and site/account management
- `TeslaPowerwallLibrary.TestConsole` — a command-line and interactive test harness covering the library's read and control operations
- `TeslaPowerwallLibrary.Setup` — a small standalone WPF app wrapping `TeslaPowerwallLibrary.Login` that performs Tesla cloud login or the FleetAPI setup/registration wizard (partner token, partner registration, PEM verification, authorize, and code exchange) and displays the resulting tokens
- `TeslaPowerwallLibrary.Tests`, `.App.Tests` and `.TestConsole.Tests` — NUnit tests for the library, desktop presentation and console

## Documentation

- [API documentation](https://oznetmaster.github.io/TeslaPowerwallLibrary/)
- [Obtain Owner or Fleet credentials with the Setup app](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html#using-the-setup-app)
- [Local protocols, setup, capabilities and validation boundaries](LOCAL-ACCESS.md)
- [Compatibility and upgrading to 2.1](UPGRADING-2.1.md)
- [Tests, adapter dependencies and live-test separation](TeslaPowerwallLibrary.Tests/README.md)

See [CHANGELOG.md](CHANGELOG.md) for release history and [RELEASE-NOTES.md](RELEASE-NOTES.md) for this release.

## Desktop app and console

Release assets include the .NET 10 Windows desktop app and the console for net472 and .NET 10 for Windows. The desktop app supports local telemetry, detailed diagnostics, per-session control permission and a configurable refresh interval (zero selects manual refresh). The console reads on demand and shares its local command catalogue between interactive and one-shot use.

For local connections, the desktop app can combine recorded LAN samples with earlier history from a saved Owner or Fleet account associated with the same site. LAN samples replace overlapping cloud samples. Completed history is cached locally; the application, not the library, owns collection and storage. See [desktop app and console setup](LOCAL-ACCESS.md#desktop-app-and-console).

## Acknowledgements

Behavioral and compatibility reference work in this project draws on the upstream [pypowerwall](https://pypi.org/project/pypowerwall/) project by Jason A. Cox and its public documentation. Bundled TEDAPI protocol definitions and signed query resources retain the [upstream MIT notice](TeslaPowerwallLibrary/Tedapi/Protocol/UPSTREAM-LICENSE.txt), also included in the NuGet package. The Tesla OAuth 2.0 PKCE login flow also references [tesla_auth](https://github.com/adriankumpf/tesla_auth) (Rust) by Adrian Kumpf.

## License

MIT © 2026 Neil Colvin — see [LICENSE](TeslaPowerwallLibrary/LICENSE).


## Unit tests

Offline tests use NUnit 5.0.0, NUnit3TestAdapter 6.3.0, Microsoft.NET.Test.Sdk 18.7.0 and NUnit.Analyzers 4.14.0. Library tests run on net472 and net10.0; console tests run on net472 and net10.0-windows; WPF presentation tests run on net10.0-windows. Use Visual Studio Test Explorer or the commands in the [test guide](TeslaPowerwallLibrary.Tests/README.md).

```powershell
dotnet test TeslaPowerwallLibrary.Tests/TeslaPowerwallLibrary.Tests.csproj -c Release --filter "TestCategory!=Live" -p:GenerateDocfxDocumentation=false
```

Offline tests need neither credentials nor a Powerwall. Live tests are explicit and separate; control tests require per-operation authorization. To check backward compatibility, package validation compares the new 2.1.0 assemblies for both target frameworks with the previous 2.0.0 release. No API differences are suppressed.

## Continuous integration tests

The [Unit tests workflow](.github/workflows/unit-tests.yml) runs on pull requests and pushes to the main development branch. Separate Windows jobs test **net472** and **.NET 10**, retaining a result file for each suite/runtime. Live tests are excluded; no account credentials or physical devices are needed. These checks do not publish packages or releases.

### Fleet account region (1.2.5)

Fleet connections default to automatic account-region discovery through Tesla's authenticated user-region endpoint before reading sites. The account region can differ from the physical site's location. Explicit FleetApiRegion values na, eu and cn remain supported; China requires its separate registration and explicit region. Automatic discovery rejects missing, mismatched or unrecognized endpoint responses instead of silently using a region. The region is resolved once per connection. Windows and remote read-only live validation passed; EU routing is covered by simulated HTTP tests, not an EU-account hardware test.

## Publishing when local hardware is unavailable

The publish/release workflows support an explicit manual override when the processor or local self-hosted GitHub Actions runner is unavailable. Select `skip_hardware_checks` and provide a single-line `hardware_skip_reason`. Use the workflow's normal source and version controls. The override applies only to that invocation and is recorded with the exact source revision in its warning and job summary; it does not create a passing hardware-test result.

GitHub-hosted validation remains mandatory for the checked-out source, and the normal build, tests and packaging steps still run. Wait for the configured hosted workflows to pass, or run them on the same source revision first. None of these hosted checks needs the local runner or processor. Automatic tag/release-triggered runs retain the normal hardware checks; use a manual invocation of the updated release workflow when an offline override is needed.
## NUnit 5 test tooling

All maintained NUnit suites use the official NUnit 5.0.0 framework. Async exception assertions are awaited, and discarded-task warnings fail test builds. Processor test packages use CrestronHomeNUnit SDK 2.2.0; workflow and Android suites, where provided, use the released 2.2.0 adapter. Tests remain available in Visual Studio, VS Code and the command line. Live and manual tests still require their documented devices and permissions. This is a test-tooling update; the published product version and runtime behavior are unchanged.

## NUnit 5 test package

Test package **1.1.0** uses **NUnit 5.0.0**. It is independent of the product version. [Download package](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/download/v2.0.0/TeslaPowerwallLibrary.ProcessorTests-1.1.0.pkg), [documentation](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/download/v2.0.0/TeslaPowerwallLibrary.ProcessorTests-1.1.0-Documentation.zip), [validation](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/download/v2.0.0/TeslaPowerwallLibrary.ProcessorTests-1.1.0.validation.json), [exact source revisions](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/download/v2.0.0/TeslaPowerwallLibrary.ProcessorTests-1.1.0.sources.json), and [SHA-256 checksums](https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/download/v2.0.0/TeslaPowerwallLibrary.ProcessorTests-1.1.0-SHA256SUMS.txt) are attached to the existing product release. No product binary or NuGet version changed for this test update.

Validated on 1 October 2026: 142 offline cases passed in each of two runs from the packaged assembly on Windows. All suite identities were checked against source discovery. Live/manual tests and execution on the processor were not repeated during this migration; earlier hardware results do not certify this new package.
