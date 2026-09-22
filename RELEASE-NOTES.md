# TeslaPowerwallLibrary 2.0.0

This major release replaces Newtonsoft.Json and log4net with System.Text.Json and application-owned Microsoft ILogger logging. Both .NET Framework 4.7.2 and .NET 10 remain supported.

## Consumer migration

Existing facade methods, low-level constructors and raw JSON string methods remain available. Model serialization attributes now belong to System.Text.Json. Object-valued telemetry and history results contain ordinary .NET dictionaries, lists and scalar values. Applications that deserialize library models using Newtonsoft.Json or cast opaque values to its types must update.

Configure logging through `PowerwallOptions.Logger`. The library defaults to silent logging; the application owns the logger, its context, filtering, scopes and lifetime.

## Fixes

- Cloud and Fleet operation writes send only explicitly requested fields, preserve numeric zero reserve and invalidate cached site configuration after attempted changes.
- Empty operation changes do not write.
- Local gateway changes preserve the unscaled reserve when a full configuration write is needed.
- The string converter supports dictionary-key serialization in merged assemblies.
- Local applications and tools use the updated serialization and logging APIs.

## Validation

All 142 deterministic library tests passed on each supported target, along with 59 credential and tool tests. Three direct read-only live tests passed separately against Owner and Fleet APIs on both targets. Clean NuGet consumers passed public model serialization, caller-provided logging and dependency checks.

Live validation covers reads only; physical mode and reserve changes are covered by synthetic request regressions. TEDAPI remains an unimplemented scaffold.
