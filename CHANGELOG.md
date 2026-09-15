# Changelog

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## CI validation - 2026-09-15 (no package release)

- Revalidate the current default-branch source after successful release workflows, including version commits created by GitHub Actions.
- Allow maintainers to configure exact-source, App-specific checks that must pass before publishing through `RELEASE_REQUIRED_CHECKS`; missing, failed or unconfirmed checks block the release.

All notable changes are documented here. This project follows [Semantic Versioning](https://semver.org/).

## [1.2.5] - 2026-09-15

### Fixed

- Use Tesla's documented Fleet authentication endpoint for refresh, authorization-code and partner-token requests. Refresh requests use form encoding.
- Discover the authenticated Fleet account's region before reading sites. The default is now `auto`; explicit `na`, `eu` and `cn` overrides remain supported. Validate discovered regional endpoints before forwarding credentials. China requires separate registration and an explicit region.

### Added

- Windows helper for dedicated Owner and Fleet test authorizations, with encrypted atomic token persistence, exclusive session ownership and access-token-only remote test inputs. Local gateway test authentication is not implemented.
- Existing-application sign-in in the Setup app, optional encrypted application settings, embedded callback validation and code exchange, and a manual browser fallback.
- CI checks for both supported library runtimes, credential handling and the Setup build.

### Validation

- 118 library tests passed on both net472 and .NET 10; 47 credential, callback and privacy tests passed on Windows.
- Dedicated Owner and Fleet authorizations each passed three read-only live tests on Windows and three on a remote test host. Automatic region discovery passed with a North America/Asia-Pacific account; EU routing is covered by simulated HTTP tests.
- The Setup app builds and its callback handling has offline coverage. Its new embedded Fleet sign-in flow has not yet completed an end-to-end interactive validation; the manual browser fallback remains available.

## [1.2.4] - 2026-09-07

### Fixed

- `PowerwallCloudClient.SetOperationAsync` (Tesla cloud mode) no longer unconditionally sends `backup_reserve_percent=0` on mode-only operation writes. Previously, changing only the operating mode (`real_mode`) silently zeroed out the site's configured backup reserve. The reserve is now only written when the caller's payload actually includes `backup_reserve_percent`, matching the behavior already present in `PowerwallFleetApiClient`.

## [1.2.3] - 2026-07-31

### Changed

- Removed source-header and license lines that implied shared copyright with the upstream Python `pypowerwall` project; this is an independent .NET implementation, not ported/copied source.
- Added reference-only acknowledgements for `pypowerwall` and `tesla_auth` in the README.

## [1.2.2] - 2026-07-10

### Fixed

- Corrected inaccurate `PackageReleaseNotes`: a Client Secret is not required for FleetAPI connect.

## [1.2.1] - 2026-07-10

### Fixed

- Corrected stale `PackageReleaseNotes` that still described the 1.1.1 refactor.

## [1.2.0] - 2026-07-10

### Added

- Tesla FleetAPI support (token-based access using a caller-supplied Client ID and refresh token).
- Standalone `TeslaPowerwallLibrary.Setup` WPF wizard for Tesla cloud login and FleetAPI partner token/registration/PEM verification/authorize/code exchange.
- MSTest-based deterministic unit test coverage (`TeslaPowerwallLibrary.Tests`).

## [1.1.1] - 2026-07-08

### Changed

- Replaced hand-written JSON parsing with typed models across the cloud, local, and login layers.

## [1.1.0] - 2026-07-08

### Added

- `HistoryPeriod` enum for typed calendar-history APIs.
- Calendar-history endpoints now return JSON-populated typed models (energy, power, state of energy, self-consumption, backup) instead of raw JSON; console and app updated accordingly.

### Fixed

- Formatting errors.

## [1.0.3] - 2026-07-07

### Added

- Storm Watch cloud control (read/set) across the library, console, and app.

## [1.0.2] - 2026-07-06

### Fixed

- `CloudTokensRefreshed` gating for refresh-token-only bootstrap.

### Changed

- GitHub Pages deploy now retries on transient failure.

## [1.0.1] - 2026-07-06

### Changed

- Use stable `Microsoft.Bcl.AsyncInterfaces`/`Microsoft.Bcl.Memory` 10.0.9 instead of the 11.0.0 preview.

## [1.0.0] - 2026-07-06

Initial stable release.

## [0.2.0-preview] - 2026-07-06

### Added

- DocFX API reference and usage article for `TeslaPowerwallLibrary.Login`.

### Changed

- Made credential cache optional.
- Proper handling of email for credentials.

### Fixed

- Trademark notice issue.
- Battery label display.

## [0.1.0-preview1] - 2026-07-05

Initial public preview release, including:

- Local network access to the Powerwall gateway (status, power flow, history, and control).
- Tesla Owners (cloud) API access, including interactive OAuth login and token persistence.
- Separate `TeslaPowerwallLibrary.Login` library for Tesla cloud login.
- `TeslaPowerwallLibrary.App` WPF dashboard with live energy charts and site/account management.
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
