# Changelog

All notable changes are documented here. This project follows [Semantic Versioning](https://semver.org/).

This changelog records shipped features, fixes, compatibility and runtime dependency changes. See [development and validation history](DEVELOPMENT-HISTORY.md) for tests, CI, build tooling and work not yet released.

## 2.1.0 — 2026-10-08

### Added

- Typed local TEDAPI access over signed Powerwall 3 LAN, setup-network Basic and installer bearer transports, with explicit signing-key enrollment through Owner or Fleet.
- Hostname/IP configuration, DNS-SD discovery, LAN address resolution, configurable response caches and opt-in local read failover.
- Typed controller, device, battery, meter, firmware and diagnostic data, plus guarded settings, backup and grid commands.
- `GetPowerReadingsAsync()` and nullable `PowerReadings`, preserving missing telemetry while retaining the existing `PowerAsync()` contract.
- Desktop local connections, detailed telemetry, session control permissions, configurable refresh, hybrid local/cloud history and improved charts; matching console commands on both runtimes.

### Fixed

- Local session handling, cancellation, endpoint cooldowns and cache invalidation after changes.
- Preserve the original configuration outside explicitly requested TEDAPI setting updates.
- Distinguish missing battery-to-home energy from zero when presenting history.

Existing public signatures and classic gateway defaults are retained. See [upgrading to 2.1](UPGRADING-2.1.md) and [local access](LOCAL-ACCESS.md) for compatibility and hardware limits.

## 2.0.0 — 2026-09-22

### Changed

- Replace Newtonsoft.Json with System.Text.Json attribute mappings. Dynamic result values now use ordinary .NET dictionaries, lists and scalar values. Consumers deserializing library models with Newtonsoft or casting results to Newtonsoft types must migrate; existing raw JSON string APIs remain available.
- Replace log4net with caller-owned Microsoft.Extensions.Logging.ILogger through PowerwallOptions.Logger. The application controls logging category, scopes and providers; the library does not dispose the logger.
- Update local applications and tools to the new serialization and logging APIs. See [migration notes](MIGRATION-SystemTextJson.md).

### Fixed

- Send only requested fields for Cloud/Fleet operation changes; preserve numeric zero reserve and avoid empty writes. Invalidate the backend configuration cache after attempted changes, including partial failures.
- Preserve unscaled reserve values when filling local gateway configuration writes, and reject invalid or unavailable settings.
- Support dictionary-key serialization in merged assemblies.

## 1.2.5 — 2026-09-15

### Fixed

- Use Tesla's documented Fleet authentication endpoint for refresh, authorization-code and partner-token requests. Refresh requests use form encoding.

- Discover the authenticated Fleet account's region before reading sites. The default is now `auto`; explicit `na`, `eu` and `cn` overrides remain supported. Validate discovered regional endpoints before forwarding credentials. China requires separate registration and an explicit region.

### Added

- Existing-application sign-in in the Setup app, optional encrypted application settings, embedded callback validation and code exchange, and a manual browser fallback.

- The new embedded Fleet sign-in flow has not yet completed end-to-end interactive validation; the manual browser fallback remains available.

## 1.2.4 — 2026-09-07

[Compare changes][1.2.4]

### Fixed

- `PowerwallCloudClient.SetOperationAsync` (Tesla cloud mode) no longer unconditionally sends `backup_reserve_percent=0` on mode-only operation writes. Previously, changing only the operating mode (`real_mode`) silently zeroed out the site's configured backup reserve. The reserve is now only written when the caller's payload actually includes `backup_reserve_percent`, matching the behavior already present in `PowerwallFleetApiClient`.

## 1.2.3 — 2026-07-31

[Compare changes][1.2.3]

### Changed

- Removed source-header and license lines that implied shared copyright with the upstream Python `pypowerwall` project; this is an independent .NET implementation, not ported/copied source.

- Added reference-only acknowledgements for `pypowerwall` and `tesla_auth` in the README.

## 1.2.2 — 2026-07-10

[Compare changes][1.2.2]

### Fixed

- Corrected inaccurate `PackageReleaseNotes`: a Client Secret is not required for FleetAPI connect.

## 1.2.1 — 2026-07-10

[Compare changes][1.2.1]

### Fixed

- Corrected stale `PackageReleaseNotes` that still described the 1.1.1 refactor.

## 1.2.0 — 2026-07-10

[Compare changes][1.2.0]

### Added

- Tesla FleetAPI support (token-based access using a caller-supplied Client ID and refresh token).

- Standalone `TeslaPowerwallLibrary.Setup` WPF wizard for Tesla cloud login and FleetAPI partner token/registration/PEM verification/authorize/code exchange.

## 1.1.1 — 2026-07-08

[Compare changes][1.1.1]

### Changed

- Replaced hand-written JSON parsing with typed models across the cloud, local, and login layers.

## 1.1.0 — 2026-07-08

[Compare changes][1.1.0]

### Added

- `HistoryPeriod` enum for typed calendar-history APIs.

- Calendar-history endpoints now return JSON-populated typed models (energy, power, state of energy, self-consumption, backup) instead of raw JSON; console and app updated accordingly.

### Fixed

- Formatting errors.

## 1.0.3 — 2026-07-07

[Compare changes][1.0.3]

### Added

- Storm Watch cloud control (read/set) across the library, console, and app.

## 1.0.2 — 2026-07-06

[Compare changes][1.0.2]

### Fixed

- `CloudTokensRefreshed` gating for refresh-token-only bootstrap.

## 1.0.1 — 2026-07-06

[Compare changes][1.0.1]

### Changed

- Use stable `Microsoft.Bcl.AsyncInterfaces`/`Microsoft.Bcl.Memory` 10.0.9 instead of the 11.0.0 preview.

## 1.0.0 — 2026-07-06

[Compare changes][1.0.0]

Initial stable release.

## 0.2.0-preview — 2026-07-06

[Compare changes][0.2.0-preview]

### Added

- DocFX API reference and usage article for `TeslaPowerwallLibrary.Login`.

### Changed

- Made credential cache optional.

- Proper handling of email for credentials.

### Fixed

- Trademark notice issue.

- Battery label display.

## 0.1.0-preview1 — 2026-07-05

[Compare changes][0.1.0-preview1]

Initial public preview release, including:

- Local network access to the Powerwall gateway (status, power flow, history, and control).

- Tesla Owners (cloud) API access, including interactive OAuth login and token persistence.

- Separate `TeslaPowerwallLibrary.Login` library for Tesla cloud login.

- `TeslaPowerwallLibrary.App` WPF desktop app with live energy charts and site/account management.

- `TeslaPowerwallLibrary.TestConsole` command-line/interactive test harness.

[1.2.4]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v0.2.0-preview...v1.0.0
[0.2.0-preview]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v0.1.0-preview1...v0.2.0-preview
[0.1.0-preview1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/tag/v0.1.0-preview1