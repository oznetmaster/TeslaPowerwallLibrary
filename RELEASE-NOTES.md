# TeslaPowerwallLibrary 2.5.1

This patch release fixes synchronization and lifetime races without adding commands, dependencies or public method signatures. Assembly identity remains `2.0.0.0`; NuGet/file versions are `2.5.1`/`2.5.1.0`. Package validation compares both targets with public 2.5.0.

Owner and Fleet connections share one completed OAuth refresh among overlapping callers, including delayed unauthorized responses. An individual cancellation after a refresh has been sent does not discard newly rotated credentials. Disposal still cancels the transport. Token notifications remain ordered so an older callback cannot overwrite newer credentials.

Independent cloud reads can overlap. Authentication, site changes, controls and reconnects wait for admitted reads. Older fetches cannot overwrite newer cached observations. Classic and TEDAPI transports retain their existing request serialization. Disposal rejects new calls, cancels admitted calls and defers resource cleanup until they drain; late responses cannot restore a disposed session. Reconnecting an existing facade reuses its backend and current credentials.

Token-cache updates lock the complete read/modify/write across instances and processes and replace the file atomically. This prevents lost accounts/fields and truncated files. It does not coordinate OAuth rotation across independently constructed clients using the same account: use one long-lived client per credential set, and have the application own credential distribution between processes.

Configure options and read-route selection before starting calls; returned mutable models remain caller-owned snapshots. Token event handlers must be thread-safe and non-blocking, and must not synchronously wait for another operation on the same client. Queue asynchronous application work instead. Disposing from a token callback is supported. Synchronous disposal starts cancellation immediately; admitted asynchronous calls must finish before owned resources are released.

Fake-transport regressions exercise rotation, cancellation, delayed responses, caches, site changes, reconnects and disposal on both target frameworks. Native lifetime energy fetch failures are checked for all three TEDAPI authentication modes: failures throw, previously returned models remain intact and recovery returns advancing counters. These fixtures do not establish real Tesla token-revocation behavior or physical Powerwall behavior. No live Powerwall control or real-token rotation is part of this audit. Current validation results are recorded in DEVELOPMENT-HISTORY.md.

The solution now enforces its existing analyzers/style policy with warnings as errors in Debug and ordinary warnings in Release, while retaining strict XML checks in both. The unused internal RuntimeHelpers compatibility type is removed. The redundant .NET Framework `System.Net.Http` 4.3.4 compatibility NuGet dependency is replaced by the framework reference to avoid conflicting reference assemblies in independent package consumers; library DLL bytes remain unchanged by this packaging correction. No new runtime dependencies are added.

# TeslaPowerwallLibrary 2.5.0

This minor release adds a validated battery-level API that can request a fresh observation. It lets consumers establish availability without copying JSON parsing or Tesla app scaling into their own code.

`GetBatteryLevelAsync(scale: true, force: true, cancellationToken: token)` performs one battery observation and applies the shared 0% floor at the raw 5% reserve. With `scale: false`, it returns the raw percentage. With `force: false`, it retains the transport's normal measurement cache. Forced reads retain authentication and device backoff. Unavailable responses remain null; transport exceptions and cancellation propagate.

Missing, malformed, nonnumeric or nonfinite battery percentages return null. Numeric zero remains a real reading. A fresh failure cannot fall back to the previously cached percentage. Callers that use the result for availability must distinguish null from zero.

The existing `LevelAsync(bool, CancellationToken)` signature, parsing defaults and cache behavior remain unchanged. Both battery APIs use a single scaling implementation. The optional fresh-read path adds no background polling or state-changing commands.

Offline regression coverage verifies reserve boundaries, raw/scaled agreement, cache reuse and forced replacement, request counts, invalid measurements, cancellation, transport failures and legacy parsing compatibility. Processor execution of these synthetic fixtures does not establish physical Powerwall behavior. PW2 instantaneous power limits from 2.4.0 remain available; battery dispatch controls are not added.

NuGet/file versions are `2.5.0`/`2.5.0.0`; assembly identity remains `2.0.0.0`. Runtime dependencies are unchanged. Package API validation compares both targets with public 2.4.0 without suppressions. Windows desktop, console, setup and credential-tool release bundles use this source and include the updated library where referenced.
