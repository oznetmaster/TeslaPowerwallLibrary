# TeslaPowerwallLibrary 2.2.0

17 October 2026.

This release expands local Powerwall 3 telemetry and corrects remote-meter and cloud status reporting. It targets .NET Framework 4.7.2 and .NET 10, preserves existing public signatures and retains the classic gateway connection defaults.

## Battery health and local telemetry

- Request 23 additional PW3 battery-health, inverter, safety and identity signals through the default June 2024 component query. The six BMS additions expose lifetime discharge, average capacity, cycle count, over-discharge count and reported self-discharge rate.
- Preserve numeric, text and boolean values through the existing typed signal/component models, including real zero and false. Missing values stay unavailable; no state-of-health percentage or undocumented unit conversion is inferred.
- Extend typed site configuration with nominal energy/power, raw site-meter limits, panel current, grid-code metadata and battery commission date.
- Recognize both `trm_mb` and `trm_wifi` remote-meter configuration so location, enabled CT slots and scaling are retained.

Vendor query text, signatures and signed bytes are unchanged. Additional June 2024 signal names use unsigned request variables.

## Gateway uptime

`Powerwall.GetLocalGatewayUptimeAsync()` and `PowerwallTedapiClient.GetGatewayUptimeAsync()` return a nullable `LocalGatewayUptime` with `Uptime`, `GatewayTime` and `StartedAt`. The library matches `SYNC_Uptime` to the actual sync-board serial and uses the gateway clock from the same response. Per-connection boot estimates tolerate publication jitter and reset with the session. Counter precision is consistent on both target runtimes.

Supported TEDAPI `StatusAsync()` and `UptimeAsync()` also expose this measurement, retaining their existing duration-string contract. TEDAPI status may read detailed controller telemetry to obtain uptime; normal caching still applies. Sites without a suitable sync board or usable clock/counter return null. This measures gateway sync-board uptime, not the uptime of each battery or inverter.

## Query formats remain supported

`June2024` and `June2026` identify captured query formats, not library release dates or device firmware age. Neither is marked obsolete. June 2024 remains the default, as it was when TEDAPI support shipped in 2.1.0.

The June 2026 capture already selects 17 of the 23 additional PW3 signals. Its captured signed queries do not select the six additional BMS counters or `SYNC_Uptime`, and cannot accept these additions through the June 2024 variable mechanism. Those values remain unavailable under June 2026. There is no automatic cross-version fallback.

## Cloud status correction and compatibility

Owner/Fleet status no longer presents installation date as gateway boot time or supplies a hard-coded firmware hash and sync type.

| Property | Behavior in 2.2.0 |
| --- | --- |
| `StartTime` | Null when the backend does not report an actual gateway boot time. |
| `UpTimeSeconds` | Existing nullable duration string; null when uptime is unavailable. |
| `GitHash` | Reported firmware hash for TEDAPI; null for cloud mappings without this information. |
| `InstallationDate` | New nullable property retaining the cloud installation date separately. |
| `SyncType` | `"unknown"` for Owner/Fleet status mappings. |

`StartTime`, `UpTimeSeconds` and `GitHash` were already nullable strings; their public types are unchanged. Applications displaying installation metadata should use `InstallationDate`. Consumers that relied on the previous fabricated values will observe this correction.

The assembly identity remains `2.0.0.0`; NuGet/file versions become `2.2.0`/`2.2.0.0`. API/package validation against released 2.1.0 passes on both targets without compatibility suppressions. Runtime dependency versions are unchanged. Deploy the complete resolved dependency graph. Upgrades from 2.0 should also follow [the 2.1 guide](UPGRADING-2.1.md); upgrades from 1.x require [the serialization/logging migration](MIGRATION-SystemTextJson.md).

## Desktop app and console

- Desktop details label battery-health units and show site metadata, sync-board uptime and a boot estimate. Component slot indexes distinguish repeated or unreported serials; slots are not presented as a count of physical batteries.
- Desktop gateway information and console `status` show installation date separately from boot time.
- Console `local-uptime` reads typed nullable gateway uptime in both one-shot and interactive modes. `local-components` and `local-configuration` expose the expanded typed data.

## Validation and limits

On Windows, all 424 offline library tests passed on each runtime, all 51 console tests passed on each runtime, and 126 desktop tests passed. Read-only live checks passed on both runtimes: four Owner cases, four Fleet cases, classic gateway telemetry, signed June 2024 and signed June 2026. The development device returned the additional BMS readings and usable gateway uptime under June 2024, while June 2026 correctly left those unavailable. Signal/slot counts do not establish physical battery counts.

These checks did not change device settings, enroll keys, switch the grid or start diagnostic procedures. Basic/setup-network and installer-bearer transport additions have offline wire coverage; this release's live additions were validated through the native gateway and signed LAN. The existing .NET Framework test build emits CS1685 for duplicate `RuntimeHelpers`; it does not cause test failures. This release does not claim new Linux/Mono, multi-device or physical diagnostic coverage.

See [local access](LOCAL-ACCESS.md), [test instructions](TeslaPowerwallLibrary.Tests/README.md), [development history](DEVELOPMENT-HISTORY.md) and the [changelog](CHANGELOG.md) for capabilities, evidence and earlier changes. Protocol reference work follows [pypowerwall 0.20.0](https://github.com/jasonacox/pypowerwall/releases/tag/v0.20.0).
