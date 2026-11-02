# TeslaPowerwallLibrary 2.5.0

This minor release adds a validated battery-level API that can request a fresh observation. It lets consumers establish availability without copying JSON parsing or Tesla app scaling into their own code.

`GetBatteryLevelAsync(scale: true, force: true, cancellationToken: token)` performs one battery observation and applies the shared 0% floor at the raw 5% reserve. With `scale: false`, it returns the raw percentage. With `force: false`, it retains the transport's normal measurement cache. Forced reads retain authentication and device backoff. Unavailable responses remain null; transport exceptions and cancellation propagate.

Missing, malformed, nonnumeric or nonfinite battery percentages return null. Numeric zero remains a real reading. A fresh failure cannot fall back to the previously cached percentage. Callers that use the result for availability must distinguish null from zero.

The existing `LevelAsync(bool, CancellationToken)` signature, parsing defaults and cache behavior remain unchanged. Both battery APIs use a single scaling implementation. The optional fresh-read path adds no background polling or state-changing commands.

Offline regression coverage verifies reserve boundaries, raw/scaled agreement, cache reuse and forced replacement, request counts, invalid measurements, cancellation, transport failures and legacy parsing compatibility. Processor execution of these synthetic fixtures does not establish physical Powerwall behavior. PW2 instantaneous power limits from 2.4.0 remain available; battery dispatch controls are not added.

NuGet/file versions are `2.5.0`/`2.5.0.0`; assembly identity remains `2.0.0.0`. Runtime dependencies are unchanged. Package API validation compares both targets with public 2.4.0 without suppressions. Windows desktop, console, setup and credential-tool release bundles use this source and include the updated library where referenced.
