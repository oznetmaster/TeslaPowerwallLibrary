# TeslaPowerwallLibrary 2.3.2

Fix false solar-production dips during brief TEDAPI remote-meter communication dropouts. When `SolarMeterComms` is active and controller solar power is missing or 0–10 W, complete configured wired/Wi-Fi remote-meter samples within 60 seconds of the gateway clock can supply the retained solar power. Load power is corrected by the same delta so the power flows remain consistent.

The correction applies to ordinary power helpers and detailed meter aggregates. Every enabled solar CT and participating meter must have a usable reading; invalid, incomplete, expired or non-positive totals cannot trigger a hold. Ordinary polling fetches supplementary configuration and detailed telemetry only for an eligible alert/solar reading. Supplementary-read failures retain the successful controller result; cancellation and selected read routes are respected.

Nullable `MeterReading.PowerHoldAgeSeconds` identifies corrected solar/load readings and the oldest retained sample's age. Solar `LastCommunicationTime` retains that sample's timestamp. Genuine zero production outside the alert, raw telemetry, explicit native meter readings, Owner/Fleet mappings, measured currents and cumulative energy counters keep their existing behavior. There is no new application-side last-good-value cache.

Offline regressions cover both remote-meter families, age and power boundaries, missing and invalid data, scaling, mixed sources, multiple meters, serialization, caching, cancellation, failures and recovery across all three local authentication transports and both TEDAPI query versions. A live remote-meter dropout has not been reproduced locally; the correction's device behavior is covered by offline fixtures.

NuGet/file versions are `2.3.2`/`2.3.2.0`; assembly identity remains `2.0.0.0`. Existing public signatures and runtime dependencies remain compatible. Package validation compares both targets with released 2.3.1 without compatibility suppressions. See the [changelog](CHANGELOG.md), [local-access guide](LOCAL-ACCESS.md) and [development history](DEVELOPMENT-HISTORY.md).
