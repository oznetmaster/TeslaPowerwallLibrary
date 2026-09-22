# Serialization and logging migration

The library now uses System.Text.Json and Microsoft.Extensions.Logging.Abstractions on
both .NET Framework 4.7.2 and .NET 10. Newtonsoft.Json and log4net are no longer library
dependencies. The login library and local tools have been updated as well.

## Public API compatibility

- Existing facade methods, raw-string methods, model property names and low-level client
  constructors remain available. Additional constructors accept a caller-owned ILogger.
- Model serialization attributes are now System.Text.Json attributes. Consumers which
  themselves deserialize library models with Newtonsoft.Json must switch to
  System.Text.Json or provide their own mappings. Serialized JSON formatting and escaping
  may differ; compare JSON values rather than literal response text.
- Vitals, backup event details and grid faults returned by the library use ordinary CLR
  dictionaries, lists and scalar values. They no longer contain JObject, JArray or JValue;
  they do not substitute JsonElement or JsonNode into those object-valued results.
  Consumers casting opaque values to Newtonsoft types must use CLR containers instead.
- The deliberately raw history, polling and command APIs still return strings. Stable
  response and request schemas use attribute-mapped models internally. Opaque history
  and telemetry data use generic CLR containers; no schema-specific JSON tree traversal
  is needed. Existing typed calendar-history methods remain available.
- Existing settings files and token-cache field names are retained. No credential reset
  or live authentication is needed for the migration.

## Logging

Set `PowerwallOptions.Logger` to the application's ILogger. Its category, scopes, filters
and providers are controlled by the application. Null means no logging. The library
does not configure global logging or dispose the supplied logger. Structured log events
are implemented using Microsoft's logging source generator.

Static offline token-cache convenience methods have no connection context and use the
default silent logger. Connection-owned token-cache operations use the supplied logger.

## Operation writes

Cloud and Fleet writes include only the fields requested by the caller. A mode-only
change no longer writes a reserve command; zero reserve stays numeric zero. Calls with
neither field return null without writing. Local gateway writes retain both fields and
preserve the raw reserve when back-filling it; unavailable settings do not invent a zero
reserve. Non-finite reserve values and empty mode strings are rejected.

Operation attempts invalidate the backend site-configuration cache, including after a
partial failure, so subsequent reads do not reuse stale pre-write settings. Tesla can
still apply commands asynchronously; cache invalidation is not an acknowledgement of
physical completion.

## Local applications and downstream consumers

The test console, credential diagnostics, dashboard settings and login code have been
migrated. Downstream applications that merge dependencies must include the new
System.Text.Json and Microsoft logging dependency closure. Applications may adapt
ILogger to their existing logging infrastructure while retaining ownership of the
logger's context and lifetime.

The .NET test SDK can itself retain a Newtonsoft.Json dependency. That test-runner
dependency is distinct from the library and local runtime tools.

## Validation

All 142 deterministic tests passed on both supported targets and twice after assembly
merging. Direct read-only library tests also passed separately against Owner and Fleet
APIs. Explicit property-name conversion fixes dictionary-key serialization after
assembly merging.
