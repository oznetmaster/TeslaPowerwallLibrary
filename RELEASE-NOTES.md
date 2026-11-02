# TeslaPowerwallLibrary 2.4.0

This minor release adds currently available PW2 battery charge and discharge power and corrects negative scaled battery percentages below the reserve floor.

`SystemStatus.InstantaneousMaxChargePower` and `InstantaneousMaxDischargePower` report nullable watts, separate from static nameplate limits. June 2024 TEDAPI requests `TEPOD` components and their `POD_available_charge_power` / `POD_available_dischg_power` signals. Values are counted once per identified module, using the last usable value for each direction. Each direction requires complete, finite, nonnegative measurements; real zeroes remain zero, while incomplete, unavailable or overflowing totals stay null. Serial-less slots cannot inflate the total. The console's `system` command displays both fields.

Captured query text, signatures and existing component order are preserved. June 2026 signed queries remain unchanged and cannot request these additions. PW3 responses without POD measurements and Owner/Fleet projections without instantaneous telemetry report null rather than a fabricated zero. Classic gateway status retains its reported values. Existing per-battery counters, normal caching and request counts are covered by regressions on all three TEDAPI authentication transports. No battery dispatch controls or new background polling are added.

`LevelAsync(scale: true)` now stops at 0% when the raw battery percentage is at or below the 5% reserve. Raw percentages, unavailable readings and scaling above the reserve retain their existing behavior. The facade correction applies across transports.

Offline validation covers module deduplication, serial-less slots, incomplete measurements, independent charge/discharge totals, zeroes, invalid values, overflow, serialization, classic gateway values, Owner/Fleet nulls, wire captures and the reserve boundary. PW2 evidence is synthetic; no physical PW2 validation is claimed. Successful processor checks, when run, validate the configured suite and do not establish physical PW2 behavior.

NuGet/file versions are `2.4.0`/`2.4.0.0`; assembly identity remains `2.0.0.0`. Public API additions are compatible and runtime dependencies are unchanged. Package validation compares both targets with released 2.3.2 without compatibility suppressions. Windows desktop, console, setup and credential-tool bundles are built from this release source and include the updated library where referenced. See the [changelog](CHANGELOG.md), [local-access guide](LOCAL-ACCESS.md) and [development history](DEVELOPMENT-HISTORY.md).
