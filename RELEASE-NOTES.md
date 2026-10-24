# TeslaPowerwallLibrary 2.3.0

24 October 2026.

This minor release expands local TEDAPI telemetry with fourteen PW2 MSA signals, conservative per-battery counter pairing and power ratings that respect inverter derating. It targets .NET Framework 4.7.2 and .NET 10 and preserves existing public signatures, runtime dependencies, assembly identity and classic gateway defaults.

## Power ratings

TEDAPI `/api/system_status` now fills `max_charge_power`, `max_discharge_power` and `max_apparent_power`. The configured site rating is converted from kW to W and capped by eligible inverter nominal power when reported. A 10 kW site with two inverters derated to 4.6 kW each therefore reports 9200 rather than 10000. Either available source can supply the limit; if neither is usable the fields remain null.

The new nullable, XML-documented `SystemStatus.InverterNominalUsablePower` reports the eligible inverter sum in watts. It is zero when no inverter supplies a usable capability, as normally occurs on PW3's legacy PINV bus. Missing/incomplete messages, invalid ratings and overflow cannot produce a fabricated finite capability. These ratings describe compatibility limits, not instantaneous charging availability.

## PW2 telemetry

- Append fourteen inverter, thermal-controller and sync-board signal names to the June 2024 detailed-controller query's unsigned variables. Captured query text, signatures and component types remain unchanged.
- Preserve zero, false, text and null through the existing typed `LocalSignal`/component models and component-qualified vitals maps.
- Populate battery `EnergyCharged`, `EnergyDischarged` and `ReactivePowerOut` only when every reported thermal-controller package forms a unique, complete adjacent inverter-module pair. Module and package serials are not interchangeable. Serial-less slots are ignored; ambiguous layouts leave the new battery readings absent and preserve existing readings.

June 2024 remains the default query set. June 2026 remains supported and unchanged; its captured signed queries cannot request the fourteen added names. Hardware and firmware determine which requested signals contain actual measurements.

## Compatibility and validation

NuGet/file versions are `2.3.0`/`2.3.0.0`; assembly identity remains `2.0.0.0`. API/package validation compares both targets with released 2.2.0 without compatibility suppressions. Existing dependency versions and application connection/control defaults are unchanged.

All 462 offline library cases passed per framework in Debug and Release. Release companion checks passed 51 console cases per runtime, 126 desktop cases and 59 credential-tool cases. Transport regressions cover setup-network, signed LAN and bearer request envelopes, immutable captures, cache reuse, forced refresh, derated and unavailable limits, and conservative PW2 pairing. XML references are included for both frameworks and DocFX metadata/HTML generation passes.

Read-only signed LAN checks passed both query sets on both runtimes on the development PW3 installation, firmware 26.34.0. Both returned maximum ratings of 11040 from the configured 11.04 kW rating and usable legacy inverter power 0. June 2024 returned all fourteen added names with two populated false readings; June 2026 returned none of the added names, as expected. Unsupported PW2 measurements remained unavailable. This is PW3 compatibility evidence; PW2 module pairing and populated PW2 lifetime counters have offline coverage, not local PW2 hardware validation.

No device settings, grid state, keys or diagnostic procedures were changed during these read-only checks. The existing .NET Framework test-build warning about duplicate `RuntimeHelpers` remains; no warning suppressions were added.

See the [changelog](CHANGELOG.md), [local-access guide](LOCAL-ACCESS.md), [test guide](TeslaPowerwallLibrary.Tests/README.md) and [development history](DEVELOPMENT-HISTORY.md). Protocol reference work follows [pypowerwall 0.21.0](https://github.com/jasonacox/pypowerwall/releases/tag/v0.21.0).
