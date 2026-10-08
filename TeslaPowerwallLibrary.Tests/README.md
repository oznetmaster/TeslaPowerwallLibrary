# Library tests

The library tests target .NET Framework 4.7.2 and .NET 10. The console tests target .NET Framework 4.7.2 and .NET 10 for Windows. Run the .NET Framework target on Windows. The desktop presentation tests target .NET 10 for Windows and do not contact a device. Real chart regressions use offscreen, non-activating WPF windows; they do not take desktop focus.

The library, desktop and console test projects currently use NUnit 4.6.1, NUnit3TestAdapter 6.3.0, Microsoft.NET.Test.Sdk 18.7.0 and NUnit.Analyzers 4.14.0. Exact dependencies are in [the library test project](https://github.com/oznetmaster/TeslaPowerwallLibrary/blob/master/TeslaPowerwallLibrary.Tests/TeslaPowerwallLibrary.Tests.csproj), [the desktop test project](https://github.com/oznetmaster/TeslaPowerwallLibrary/blob/master/TeslaPowerwallLibrary.App.Tests/TeslaPowerwallLibrary.App.Tests.csproj) and [the console test project](https://github.com/oznetmaster/TeslaPowerwallLibrary/blob/master/TeslaPowerwallLibrary.TestConsole.Tests/TeslaPowerwallLibrary.TestConsole.Tests.csproj). Visual Studio discovers them through the adapter; command-line runs use the same fixtures.

## Offline checks

Run from the solution directory:

```text
dotnet test TeslaPowerwallLibrary.Tests/TeslaPowerwallLibrary.Tests.csproj -c Release --filter "TestCategory!=Live" -p:GenerateDocfxDocumentation=false
dotnet test TeslaPowerwallLibrary.App.Tests/TeslaPowerwallLibrary.App.Tests.csproj -c Release -p:GenerateDocfxDocumentation=false
dotnet test TeslaPowerwallLibrary.TestConsole.Tests/TeslaPowerwallLibrary.TestConsole.Tests.csproj -c Release -p:GenerateDocfxDocumentation=false
```

Offline fixtures use simulated responses and generated temporary keys. They do not need account credentials, register signing keys, or issue hardware commands. The console tests cover the shared interactive/one-shot command parser, denial of control without explicit session permission, rejection of invalid inputs before connection, preservation of zero/false partial settings and separation of setup-network credentials.

Compatibility regressions exercise the released `PowerSnapshot` double contract, nullable `PowerReadings`, both facade paths, real zero versus missing data, classic gateway constructor/default behavior, explicit read-only/memory-only settings and cached session reuse. The console also checks actual formatted output for missing and zero readings. `dotnet pack` compares both assemblies with the published 2.0.0 package using SDK package validation; no API differences are suppressed.

Library coverage includes local sessions, both TEDAPI query formats, typed serialization, battery/expansion identity and units, explicit control framing, classic vitals decoding, follower routing, and the desktop's handling of unavailable values and connection changes.

## Explicit cloud validation

Read-only Live cases validate site selection, typed readings and operating configuration. Run them separately through dedicated Owner and Fleet [credential-helper sessions](https://github.com/oznetmaster/TeslaPowerwallLibrary/blob/master/TeslaPowerwallLibrary.TestCredentials/README.md), because the APIs use distinct connections. EnableLiveTests must be true with the helper-generated private LiveTestSettings.json input. Refresh tokens never enter the test host.

Key registration is a separate explicit fixture and additionally requires EnableLocalKeyEnrollment. Registration changes authorization and starts a limited physical-verification window. Do not include it in ordinary telemetry runs or automatically retry an uncertain registration.

## Explicit local validation

LocalReadOnlyLiveTests is marked Live and Explicit. Select a specific test by its fully qualified name. The fixture uses TESLA_LOCAL_TEST_HOST and TESLA_LOCAL_TEST_LABEL_PASSWORD_FILE; the latter names a Windows user-protected DPAPI file. Signed checks require the prepared Windows CNG key and completed physical verification. No local read-only test changes operating settings, grid state, backup events or firmware.

ReadOnlySignedTedapiTelemetry tests the default June 2024 query set; ReadOnlySignedJune2026Telemetry tests the optional newer set. These calls use the local host only, without Owner or Fleet credentials. Other tests check the customer gateway API, setup transport, and discovery. ReadOnlyControllerFieldTypes inspects only field names and JSON kinds in a controller response, for both query versions and every array slot, without logging field values. It does not infer the shape of null sections. Unsupported hardware paths must be recorded as unverified, not assumed to work from an offline pass.

See [local access](../LOCAL-ACCESS.md) for the current hardware evidence and parity boundaries. Processor tests remain a separate manual workflow.

## Explicit reversible settings checks

`ReversibleSettingsLiveTests` is categorized `Live` and marked `Explicit`. It never runs as part of the offline suite. Run only one selected case at a time after operator authorization. `TESLA_APPROVED_SETTING_TEST` must exactly match `reserve`, `mode` or `stormwatch`; other cases are skipped. The existing local host and DPAPI label-password file settings are required, together with `TESLA_LOCAL_TEST_DIN` for an exact device identity check. Tests require healthy mains and a closed grid contactor before writing. The Windows key is the existing local development CNG key; these tests do not enroll keys.

Storm Watch additionally requires `TESLA_LOCAL_TEST_CLOUD_SITE`, `TESLA_LOCAL_TEST_OWNER_EMAIL` and `TESLA_LOCAL_TEST_OWNER_CACHE`. It uses the explicitly selected Owner account and site. It does not substitute manual maximum backup for Storm Watch.

Each test records its original value in a non-secret result attachment before writing, confirms the change with fresh reads, and attempts restoration in `finally`, including after a lost acknowledgement. Restoration uses separate requests and must be independently confirmed. A restoration failure stops subsequent cases in that fixture. A process termination can prevent `finally` from running: inspect the saved restoration record and current device settings before resuming. Run frameworks sequentially, never simultaneously against the same device.

The local HTTP test probe also verifies that only the requested configuration path changes before allowing a write. `TESLA_LOCAL_DIAGNOSTIC_DRYRUN=1` blocks that write before transmission for read-only serialization diagnosis; this deliberately fails the live-change assertion. Optional private rejection diagnostics are written only when `TESLA_PRIVATE_REJECTION_LOG` is explicitly supplied; do not commit that file.

`ReadOnlySupplementalQueryFieldTypes` is a separate explicit, read-only research fixture. `TESLA_LOCAL_TEST_QUERY_REFERENCE` points to the pinned June 2026 query reference JSON. It executes only the three listed vendor-signed status queries, reports JSON field kinds without values, and never sends a self-test mutation. Its use of the internal transport is confined to the test project; it does not add an arbitrary public query API.

## Typed diagnostic coverage

`LocalQueryCoverageTests` compares every field selected by both bundled vendor query sets with an attributed response destination. Its sole credential exclusion is `system.supportMode.remoteService.sessionId`; the existing privacy regression confirms that the session credential is not exposed. `LocalExtendedDiagnosticsTests` covers populated synthetic metadata, exact numeric tokens, missing values and single-record/list shapes. These fixtures do not claim hardware execution of the underlying diagnostic procedures.

Transport regressions check the exact signed supplemental query bytes for Basic, signed LAN and installer bearer, independent caches, forced refresh and rejection of partial responses. The console and desktop presentation tests verify that reported zero/false values remain visible and IEEE provisioning PINs are omitted.

`ReadOnlyTypedExtendedDiagnostics` exercises the three public facade methods using the existing verified LAN key. It logs section availability and result counts, not provisioning values. Run live tests with an explicit `-f net472` or `-f net10.0`, one framework at a time: simultaneous target-framework runners can exceed the gateway's login rate limit. A 429 is a rate-limit failure, not a successful hardware test. No live diagnostic fixture starts an inverter test, protection trip, phase detection or firmware update.
