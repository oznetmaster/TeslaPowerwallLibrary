# TeslaPowerwallLibrary 2.0.0

This major update replaces Newtonsoft.Json and log4net with System.Text.Json and application-owned Microsoft ILogger logging. Both net472 and net10.0 remain supported.

## Consumer migration

Existing facade methods, low-level constructors and raw JSON string methods remain available. Model serialization attributes now belong to System.Text.Json. Object-valued telemetry and history results contain ordinary .NET dictionaries, lists and scalar values. Applications that deserialize library models using Newtonsoft.Json or cast opaque values to its types must update. Logging is configured through PowerwallOptions.Logger and defaults to silent.

Read [migration notes](MIGRATION-SystemTextJson.md) before upgrading. Crestron consumers must update their merged runtime dependencies and, to retain library diagnostics, supply an ILogger adapter. Desktop test-runner dependencies are not runtime library dependencies.

## Fixes

Cloud/Fleet operation writes now send only explicitly requested fields, preserve numeric zero reserve and invalidate the actual site-configuration cache. Empty changes do not write. Local gateway changes preserve the raw reserve when a full configuration write is needed. The string converter handles dictionary keys in merged assemblies.

## Validation

The local audit passed 142 library tests on each target and 59 credential/tool tests. The dedicated library processor package passed all 142 cases twice on Windows after assembly merging and twice on the development processor, with no failures or skips. All local applications/tools built. These deterministic tests use synthetic Tesla responses; they do not establish live Powerwall command completion. TEDAPI remains an unimplemented scaffold.

Clean NuGet consumers passed on both target frameworks. The associated Crestron driver 1.1.8 is a separate release and requires this library version.


Read-only live validation on 22 September 2026 passed separately against the Owner and Fleet APIs. In each credential session, all three direct library tests passed on net472, .NET 10 and the development processor; all three driver tests passed in the desktop SDK harness and on the processor. The 142 library and 81 driver offline cases also passed twice on the processor in each API run, with no failures or skips. Temporary test instances and package archives were removed and reservations released. No installed driver update, Powerwall setting changes or processor reboot was performed.
