# Development and validation history

## 2.1 release preparation — 8 October 2026

- Preserved the released non-nullable `PowerSnapshot` and `PowerAsync()` contract. Added nullable `PowerReadings`/`GetPowerReadingsAsync()` for the desktop, console and new consumers.
- Preserved classic gateway constructor, control and session-persistence defaults. New TEDAPI modes remain read-only by default; companion apps explicitly select read-only, memory-only local sessions.
- API package validation against public NuGet 2.0.0 passed for net472 and net10.0 without suppressions. AssemblyVersion stays 2.0.0.0; package/file versions become 2.1.0/2.1.0.0.
- All 394 offline library tests passed on each runtime; 50 console tests passed on each runtime; 125 desktop tests and 59 credential/tool tests passed on .NET 10 Windows. The complete offline solution passed in an isolated Release output directory (1,072 test executions). This preparation did not execute hardware controls.
- README, upgrade guide, local guide, release notes and DocFX source updated for 2.1. Hosted workflows include the new companion suites and package compatibility check; these edits have local validation only until pushed.
- The credential-test project now links the history-site model required by its linked app settings source. DocFX generated the site with zero warnings/errors; generated API signatures, guide content and local links were checked.
- Final package inspection caught and corrected a multi-target outer-build omission of the bundled protocol MIT notice. The package includes the notice byte for byte, both XML references and the release guides. Fresh isolated NuGet consumers compiled and ran on net472 and .NET 10, checking both reading contracts, protocol defaults and retained assembly identity.
- Release preparation completed for 2.1.0. Historical live evidence and limits remain in LOCAL-ACCESS.md.

## Local API revision — included in 2.1.0, 8 October 2026

Current outcome: the pinned upstream TEDAPI functional scope is implemented. Hardware coverage is documented separately in [local access](LOCAL-ACCESS.md); this revision is included in 2.1.0.

- Completed attributed phase-detection, inverter self-test, legacy firmware-progress, IEEE 2030.5 and protection-test metadata. Firmware-defined scalar representations and object/list shapes are retained without guessed units or fabricated values.
- Added an automated audit of every selected field in both bundled query sets. The sole explicit exclusion is the remote-service session credential. Additional meter identity, bus timestamp, inverter fan-test state and battery-alert fields found by that audit are mapped.
- Added three typed supplemental read APIs and matching interactive/one-shot console commands. Desktop detailed refresh includes their results. Presentation tests keep missing values distinct from zero/false and omit provisioning PINs from both consumers.
- Final offline validation: 373 library tests on each of net472 and .NET 10; 49 console tests on each runtime; 90 desktop tests on .NET 10 for Windows. The tests include synthetic populated diagnostic records; they do not claim physical diagnostic execution.
- Read-only live validation: both signed query versions and the new typed diagnostic facade passed on each runtime. The first simultaneous framework run encountered a login HTTP 429 on .NET 10; a separate .NET 10 run passed all three tests. Live test instructions now explicitly require one framework at a time.
- Local reserve and operating mode were changed, confirmed and restored on both runtimes. Owner Storm Watch was separately changed, confirmed and restored. Grid switching, backup scheduling, phase detection, self-tests and firmware updates were not executed.
- Configured hostname and freshly resolved IP addresses are available in desktop Settings and console `local-connection`. The desktop app was not launched during final background validation.

- Refined Energy charts to suppress cloud data and interpolation wherever valid LAN readings cover the selected component. Missing LAN measurements and outages retain cloud backfill; live updates preserve existing chart objects. All kWh periods now use bars, while Day keeps kW lines. Added source-priority, correction, polling-gap and rendered-chart regressions. All 102 desktop NUnit tests pass in Release, including zero-baseline checks for positive and negative energy bars. Rendered source-priority and bar-chart images were inspected. The single running desktop instance was restarted with the updated Release build.

- Reproduced mixed tilted/horizontal Month labels in the live app and a WPF regression: reused label geometries retained 45-degree rotation from Week. All periods now consistently use horizontal labels. The regression checks the rendered label rotations after period changes, rather than only the axis property. All 102 desktop tests passed, and the Release build completed without warnings or errors.

Earlier implementation and validation milestones (counts below record their respective stages):

- Authorized reversible hardware checks exposed a local configuration write failure hidden by offline fixtures. Preserving the original configuration bytes outside requested values fixed reserve and mode updates on the development device. Each change and restoration passed on net472 and .NET 10; Storm Watch was separately confirmed/restored through Owner. Added structured RPC rejection handling and offline restoration/byte-preservation coverage. No grid switching, backup scheduling, self-test, or firmware update was performed.

- Implemented typed local TEDAPI reads, explicit signed controls, key enrollment/status and hostname/discovery support.
- Kept local runtime independent of cloud connections and polling owned by library consumers.
- Updated desktop and console for local connection choices, secure Windows signing keys, explicit enrollment, and typed data reads.
- Made missing power measurements nullable; consumers must handle unavailable readings.
- Verified the prepared key after coordinated physical confirmation. Signed LAN read-only telemetry, components and configuration passed on .NET 10 and .NET Framework 4.7.2.
- Added offline tests for session invalidation, both query formats, missing values, battery/expansion identity and units, desktop refresh settings and control guards.
- Confirmed the optional June 2026 query set on the local Powerwall 3; retained the June 2024 default for its additional signal coverage.
- Verified typed local battery summaries on both runtimes and added explicit console/desktop backup and grid controls without executing live power commands.
- Implemented classic gateway protobuf vitals decoding, bounded binary response reads, and explicit setup Wi-Fi follower queries with separate device caches and no implicit transport fallback.
- At this stage, full upstream parity and application coverage were still under review; live power-control testing had not yet been performed.
- Fixed the desktop hostname editor by adding the editable WPF template part. Discovery now shows progress and results beside its button, preserves manual entry, and handles timeout/network errors. Added real WPF editing and offline discovery regressions; 20 desktop tests pass.
- Added desktop-only hybrid energy history with explicit saved Owner/Fleet selection, host/site association, SQLite persistence and cache-before-authentication reads. Added provisional refresh, stale-data fallback, settled-period reuse and manual correction refresh. The library retains no database dependency.
- Corrected the empty local Energy display and full-day time axis. All 38 desktop tests pass, including database restart/isolation/cancellation tests and real WPF bindings plus chart rendering. LAN telemetry remains functional; the live Fleet history check failed while retrieving the site list, so live cloud-history rendering remains unverified.
- Added persistent actual LAN samples and incremental graph updates; new readings retain the chart's series, axes and existing points. Owner site discovery and Portincaple day-history rendering passed live, including locally cached history after restart. Fleet history sign-in still requires renewed authorization.
- Associated the local hardware identifier with one cloud history site independently of credentials and addresses. Desktop regressions cover retained identity and rejection of conflicting reassignment; 45 desktop tests pass.
- Added typed meter configuration and Neurio/remote-meter channel projection, preserving sparse CT indexes, scaling, explicit zero and missing data. Added typed legacy bus measurements, identified device collection and configured-meter summaries. All 270 offline library tests pass on each of net472 and .NET 10; both runtimes also passed read-only signed hardware checks of device collection and aggregate summaries. Complete upstream parity remains in progress as documented in local access.
- Added typed system firmware/update information, installer bearer transport and explicit native meter-counter reads. The 277 offline library tests pass on each runtime; these new paths still need live validation where hardware supports them.
- Fixed calendar rollover, chart period redraw, week alignment and missing-history gaps. Added actual WPF pixel regressions for period switching and incremental LAN updates; 65 desktop tests pass. Local caching follows the configured polling interval, and the desktop title/sidebar show the site name.
- Extended fan/PV/temperature diagnostics, legacy firmware hash and service-state models, native counters, and SYNC/Meter Z/inverter aggregate contributions. Both June 2024 and June 2026 signed reads passed on both runtimes on 8 October. Read-only field inspection established additional wire types without logging values.
- Added explicit caller-owned setup-network follower support with per-device caches, cancellation, snapshot integration and lifetime tests. Automatic LAN/Wi-Fi failover remains unimplemented.
- Verified the rebuilt desktop's site title, live readings, Week/Month data and scale changes, and explicit detailed telemetry. Fixed stale bus-value presentation, fractional-second day boundaries, rounded backup time and polling recovery after request timeout. The desktop suite reached 77 passing tests. Remaining parity gaps are listed explicitly in local access; this is still unreleased work.
- Corrected the Powerwall graph: Day identifies signed charge/discharge, while longer periods use reported positive battery-to-home contribution. Preserved missing contribution values, added a one-time upgrade of older history-cache entries, aggregated Lifetime by month, and kept long-period date labels horizontal. All 87 desktop tests pass, including WPF rendering and database migration regressions. The updated Release app builds; the latest graph changes have not been visually checked in an interactive session.
- Added opt-in read failover from signed LAN to an explicitly supplied, authenticated setup-network client. Tests cover both query versions, all supported read routes, cold/warm recovery, matching identity, concurrent recovery, cooldown, cancellation, caller ownership and prevention of control redirection. No automatic network setup or cloud fallback is added. Live setup-network failover remains unvalidated.
- Preserved legacy Powerwall firmware-progress slots and added nullable remote-meter signal strength using the upstream numeric fixture. Firmware information is read-only; no update or self-test was initiated to obtain diagnostic data. All 326 offline library tests pass on each target runtime. Console and desktop Release builds succeed. The current source also passed both signed query versions in read-only hardware tests on each runtime (four live checks).
- Unified local console commands between one-shot and interactive modes, closing gaps for backup/grid controls, per-device component reads, partial settings updates and current read-route status. Added explicit setup-network host, per-invocation failover and recovery-delay options with a separate environment-supplied label password; these options are not persisted. Invalid setup options are rejected before saving primary connection preferences.
- Added a dedicated console NUnit project to the solution with 45 passing offline tests on each target runtime. Both console executables also passed read-only interactive hardware checks for connection identity, June 2026 controller telemetry and backup events. No control command was executed on hardware.
- Expanded the read-only field-type probe to both query versions and all array slots. Added typed protection-trip-test activity from the observed June 2026 boolean field and displayed it in desktop diagnostics. The library suite now passes 330 tests on each runtime, and the console/desktop builds succeed. The existing net472 library-test RuntimeHelpers warning remains.
- Rechecked the pinned Python query/transport code, a separate Go controller model and Tesla-API protobuf documentation. The unresolved phase-detection, self-test, legacy progress and IEEE 2030.5 response sections remain untyped or null in these sources and normal hardware captures. Their field names alone do not establish safe populated model types. At that stage, full typed metadata parity was explicitly incomplete; no update, self-test or phase-detection action was triggered to create a sample.
- See [local access](LOCAL-ACCESS.md) for current behavior and validation limits.


See the [product changelog](CHANGELOG.md) for shipped changes. This document preserves test, CI and build history. Dated development entries describe work at that time, not a published product version or completed acceptance. Version headings identify the release alongside which development work was recorded.

## Where changes belong

- Product changelog and product release notes: shipped behavior, API, compatibility, fixes and runtime dependencies. Mention validation briefly when it helps explain a fix.
- This history: test coverage, CI, build tooling, work on pending versions. Split mixed entries so the product effect remains easy to find.
- Testing and workflow guides: current setup and operating instructions.
- Test-only or documentation-only changes do not require a product release.

<!-- development-history -->

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## CI validation - 2026-09-15 (no package release)

- Revalidate the current default-branch source after successful release workflows, including version commits created by GitHub Actions.
- Allow maintainers to configure exact-source, App-specific checks that must pass before publishing through `RELEASE_REQUIRED_CHECKS`; missing, failed or unconfirmed checks block the release.

## [1.2.5] - 2026-09-15

- Windows helper for dedicated Owner and Fleet test authorizations, with encrypted atomic token persistence, exclusive session ownership and access-token-only remote test inputs. Local gateway test authentication is not implemented.

- CI checks for both supported library runtimes, credential handling and the Setup build.

### Validation

- 118 library tests passed on both net472 and .NET 10; 47 credential, callback and privacy tests passed on Windows.

- Dedicated Owner and Fleet authorizations each passed three read-only live tests on Windows and three on a remote test host. Automatic region discovery passed with a North America/Asia-Pacific account; EU routing is covered by simulated HTTP tests.

- The Setup app builds and its callback handling has offline coverage. Its new embedded Fleet sign-in flow has not yet completed an end-to-end interactive validation; the manual browser fallback remains available.

## [1.2.0] - 2026-07-10

- MSTest-based deterministic unit test coverage (`TeslaPowerwallLibrary.Tests`).

## [1.0.2] - 2026-07-06

- GitHub Pages deploy now retries on transient failure.

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

### Desktop local controls and alert presentation

Added a session-only Settings control unlock without changing the library's read-only default. Offline tests exercise actual WPF reserve, mode and grid-charging controls, reject unexpected writes during unlock, retain the original connection after authentication failure, and clear permission on disconnect. System alert presentation retains firmware codes and response receipt time without treating every entry as a warning.
