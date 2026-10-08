# Local access — 2.1

This guide describes local access in version 2.1.0. TEDAPI support was added in this release. See [compatibility notes](UPGRADING-2.1.md).

## Choosing a connection

For a Powerwall 3 on the home network, signed TEDAPI is the preferred starting point for current readings and diagnostics. Use an Owner or Fleet connection when remote access or historical calendar data is needed. These connections serve different purposes and can be used together; selecting a local protocol never silently enables cloud access.

Use the provided Windows Setup tool to obtain cloud credentials: [Owner and Fleet setup instructions](https://oznetmaster.github.io/TeslaPowerwallLibrary/articles/login.html#using-the-setup-app). These account credentials are separate from local equipment passwords and signing keys.

| Connection | Network and credentials | Advantages | Limitations |
| --- | --- | --- | --- |
| Owner cloud (`CloudMode = true`) | Internet access and Owner OAuth authorization for the Tesla account. Companion login tools acquire the credentials. | Access authorized sites remotely; retrieve calendar history; read and change Storm Watch through this library. | Requires Tesla cloud availability and token management. Current readings follow cloud update cadence and do not include the full local TEDAPI diagnostic data. |
| Fleet cloud (`FleetApi = true`) | Internet access, a registered Fleet application/client ID and the user's Fleet OAuth authorization. Companion setup/login tools support this flow. | Access authorized sites remotely; retrieve calendar history and use supported energy controls through the Fleet API. | Additional application setup, permissions and region configuration. Cloud update cadence still applies. This library does not expose Storm Watch through Fleet. |
| Classic local (`LocalProtocol = Gateway`) | Reachable local hostname/IP and the gateway customer password. | Direct basic readings without a cloud account or signing-key enrollment; preserves existing gateway integrations. | Endpoints vary by hardware/firmware. Powerwall 3 basic reads are not a substitute for detailed TEDAPI telemetry. No cloud calendar-history archive. |
| Signed local TEDAPI (`TedapiSigned`) | Reachable Powerwall 3 over Ethernet or home Wi-Fi, the local customer password and an already verified RSA-4096 signing key. | Fresh device readings, detailed telemetry and diagnostics, and explicitly enabled local controls. Normal operation has no cloud dependency. Both home-network interfaces use the same protocol and credentials. | Initial key enrollment uses Owner or Fleet and requires device verification. Requires compatible firmware and local network reachability. No cloud calendar-history archive; signed access alone does not route component reads to follower devices. |
| Setup-network TEDAPI (`Tedapi`) | Reachable setup Wi-Fi interface and the full equipment-label password (`GatewayPassword`). | Local reads without cloud authorization or signing-key enrollment; supports explicit follower-device reads and an optional read fallback for signed LAN. | The consumer must arrange access to the separate setup network. Read-only in this library; setup-network hardware and failover have offline coverage only. |
| Installer-bearer TEDAPI (`TedapiBearer`) | Reachable compatible gateway and the full equipment-label password (`GatewayPassword`) for installer authentication. | An additional local read transport for compatible gateways, without cloud authorization or signing-key enrollment. | Hardware/firmware dependent, read-only in this library and offline-tested only. It is not an automatic fallback for signed Powerwall 3 access. |

### Freshness, history and controls

Local reads obtain the device's currently reported measurements without waiting for the cloud reporting cycle. They are request/response reads, not push notifications or a guarantee that every underlying sensor changes on every request. Cloud reads may lag the device; this library does not promise a fixed cloud update interval. Reading either connection more often does not force its source to produce newer measurements. Response caching can also reuse an earlier result; `CacheExpireSeconds` controls that reuse, independently of how often the consumer calls the library.

The library starts no polling loop. The desktop app has a configurable local refresh interval (zero means manual-only), and the console reads on demand. The desktop app can combine recorded local samples with earlier Owner or Fleet history for the same site, preferring local samples where they overlap and caching retrieved history. This hybrid behavior belongs to the desktop app; local library connections remain independent of the cloud.

TEDAPI controls require signed access and explicit `AllowLocalControl = true`; setup-network and installer-bearer modes remain read-only. Classic gateway access retains its released control defaults, so set `AllowLocalControl = false` explicitly when read-only access is intended. The desktop app and console start local sessions read-only unless control permission is explicitly requested. Supported commands are not identical across all connections; Storm Watch is an Owner-cloud feature in this library.

Signed Ethernet and home Wi-Fi reads have been exercised on one Powerwall 3 on both target runtimes. This does not establish compatibility with every device, firmware or transport. See [validation status](#current-validation-boundary) for the detailed hardware boundaries.

## Connections and data flow

The library makes requests when a consumer calls it. It does not start a polling loop or subscribe to unsolicited Powerwall events. The upstream TEDAPI implementation uses request/response HTTPS; no device-push subscription is implemented here. Application events raised after a read do not remove the cost of polling.

`PowerwallOptions.LocalProtocol` selects:

- `Gateway`: customer-authenticated local HTTPS endpoints.
- `Tedapi`: full label-password authentication, normally through the device's setup Wi-Fi network.
- `TedapiSigned`: customer authentication and an already verified RSA-4096 signing key over Ethernet or home Wi-Fi. The same signed protocol and credentials apply to both network interfaces.
- `TedapiBearer`: installer-authenticated envelopes on compatible gateways using the full label password; offline-tested only.

Every local mode stays local. There is no cloud login or cloud fallback during local runtime. Key enrollment is a separate, explicit operation on an authenticated Owner or Fleet connection.

`Host` accepts a hostname or IP address, optionally with an HTTPS port. `.local` names use the operating system resolver. `PowerwallDiscovery.ResolveAsync` resolves an explicit address; `DiscoverAsync` browses IPv4 mDNS/DNS-SD advertisements and returns unverified candidates. Discovery does not scan a subnet, authenticate candidates or select a device. The tested Powerwall resolved by hostname but returned no candidates during the discovery browse, so manual hostname/IP configuration remains necessary for that installation.

## Signing-key enrollment

Reuse the existing Owner or Fleet login flow and select the intended energy site. Create or load the signing key in the consumer's secure storage, then call:

```csharp
var registration = await cloudPowerwall.RegisterLocalKeyAsync(
    signingKey, "My local Powerwall client", cancellationToken);
var status = await cloudPowerwall.GetLocalKeyStatusAsync(signingKey, cancellationToken);
```

Only the public key is submitted. The caller owns the RSA object and its persistence.

The reported states are `PendingVerification`, `VerificationTimedOut`, `Verified`, `Removed`, and `Unknown`. Only `Verified` confirms authorization. A successful HTTP response alone does not. After an uncertain registration result, read the same key's status before retrying.

Coordinate physical confirmation before registration. The upstream reports an approximately ten-minute window and a Powerwall 3 On/Off-switch procedure. Follow the correct equipment/region instructions, distinguish the On/Off switch from the DC isolator, and account for grid availability and any separate solar inverter before operating equipment. This library never performs physical confirmation or automatically re-registers a timed-out key.

References: [upstream enrollment instructions](https://github.com/jasonacox/pypowerwall/blob/3892e7c9352db80bffc6b5bda15a68d07062a25b/README.md), [Tesla Powerwall 3 switch diagram](https://energylibrary.tesla.com/docs/Public/EnergyStorage/Powerwall/3/OwnerManual/en-emea-apac/GUID-DFC60EF8-F70D-4049-80D6-3E591A970C90.html), and [Tesla owner's manual](https://energylibrary.tesla.com/docs/Public/EnergyStorage/Powerwall/3/OwnerManual/Print/en-emea-apac/Powerwall-3-Owner-Manual-EN.pdf).

## Signed local reads

A Windows consumer can use an existing CNG key without exporting its private material:

```csharp
using System.Security.Cryptography;
using TeslaPowerwallLibrary;

using var stored = CngKey.Open(
    "TeslaPowerwallLibrary.LocalClient",
    CngProvider.MicrosoftSoftwareKeyStorageProvider);
using RSA signingKey = new RSACng(stored);
using var powerwall = new Powerwall(new PowerwallOptions
{
    Host = "powerwall-hostname.local",
    Password = customerPassword,
    LocalProtocol = PowerwallLocalProtocol.TedapiSigned,
    LocalSigningKey = signingKey,
    AllowLocalControl = false
});

await powerwall.ConnectAsync(cancellationToken);
var telemetry = await powerwall.GetLocalTelemetryAsync(
    force: true, cancellationToken: cancellationToken);
var components = await powerwall.GetLocalComponentsAsync(
    cancellationToken: cancellationToken);
var configuration = await powerwall.GetLocalConfigurationAsync(
    cancellationToken: cancellationToken);
```

Other platforms may supply their own RSA-4096 implementation and secure key storage. Dispose the connection before disposing its signing key.

The typed reads include controller power/energy/grid state, alerts, component signals, detailed meter channels, non-secret operating configuration, and backup events. Missing fields stay null. `GetPowerReadingsAsync()` uses nullable readings; the older `PowerAsync()` alone retains zero defaults for compatibility. Signal names and units follow the device; a missing signal is not a zero reading. Estimated backup time is an estimate from energy and load, not a reported runtime guarantee.

`CacheExpireSeconds` controls reuse of previous responses; it is not a polling interval. `force: true` requests another read but does not bypass a device rate-limit cooldown.

`LocalQueryVersion` defaults to `TedapiQueryVersion.June2024`. The optional `June2026` path sends newer vendor-signed GraphQL query captures. There is no automatic cross-version fallback; firmware and vendor signature changes can affect which set is accepted. The captured signal sets differ. Both sets have now returned signed telemetry on the development Powerwall 3; the default June 2024 set also includes the additional fan and temperature signals documented upstream.

## Additional local telemetry and multiple devices

`GetLocalComponentDiagnosticsAsync` returns identified fan speeds/targets/duty, temperatures and PV inputs. `LocalDiagnosticsProjection.Create` projects an already collected `LocalDeviceSnapshot` without another request. PV input power is explicitly calculated from reported DC voltage and current; connection state is never inferred from standby or voltage. Missing/incomplete legacy messages are excluded from normalized readings and shown as unavailable in the desktop detailed view. The underlying typed message retains its availability flags.

`GetLocalSystemInformationAsync` returns firmware and update metadata. `GetLocalNativeMeterAggregatesAsync` explicitly reads the local customer meter endpoint, including lifetime counters when supported. Installer bearer and customer sessions are separate. Firmware metadata can contain a staged download URL; applications should avoid logging that entire object.

Firmware updates are normally delivered [automatically by Tesla](https://www.tesla.com/en_GB/support/energy/powerwall/mobile-app/software-updates). These APIs only report firmware and update information; reading them does not start an update. Legacy Powerwall update arrays preserve null slots and native progress units.

Legacy component firmware hashes retain their numeric array representation. Subassembly identity, PV string diagnostic flags, enable-line state and non-secret remote-service status are typed. Remote-meter readings retain the reported numeric `rssiDb` signal strength without assigning a default for missing data. No service-session identifier is exposed by the remote-service model.

For a multi-Powerwall installation, a consumer may create and authenticate a separate `PowerwallTedapiClient` with `LocalProtocol = PowerwallLocalProtocol.Tedapi` on an accessible setup-network host, then supply it as `PowerwallOptions.LocalFollowerConnection` when constructing the signed LAN client. Each connection keeps its own authentication and per-device cache. The consumer owns both lifetimes and must dispose the signed client before its follower connection. This option never changes network adapters, connects to Wi-Fi, authenticates another client or falls back to a cloud API. By default it only routes explicit follower component reads.

`EnableLocalReadFailover = true` additionally allows supported controller reads to use that already authenticated setup-network connection. Both connections must identify the same controller. Three consecutive LAN network, request-timeout or HTTP 500/502/504 failures activate the alternate route; an initial LAN connection failure can use it immediately. Caller cancellation, authentication rejection, malformed responses and device rate-limit cooldown do not activate failover. Telemetry, components, configuration, system information and native meter reads support this route, including typed projections composed from those reads.

`LocalReadFailoverRetryInterval` sets the minimum delay before a subsequent read attempts LAN recovery (60 seconds by default). There is no background retry or polling task. Recovery must preserve the controller identity, and concurrent reads share one recovery attempt. `PowerwallTedapiClient.IsUsingLocalReadFallback` reports the selected route, not whether the latest read succeeded. Signed-only reads and all controls remain on the primary connection; they are never replayed through the alternate. Cold-start fallback cannot run signed-only operations until a supported read successfully establishes LAN. These behaviors have simulated-endpoint tests on both runtimes; setup-network failover has not been exercised on the house installation.

## Explicit local controls

TEDAPI control is disabled by default. `AllowLocalControl = true` enables explicit signed requests; connecting and reading never issue a control command. Classic `Gateway` access retains its released control and session-persistence defaults. Set `AllowLocalControl = false` and `NoLocalSessionPersistence = true` explicitly for a read-only, memory-only classic connection, as the desktop app and console do.

Signed local methods include partial operating-setting updates, manual backup scheduling/cancellation, and grid island/reconnect commands. Writes require a matching acknowledgement; that acknowledgement is not proof of the physical outcome. Configuration changes use a fresh file hash for optimistic concurrency. Uncertain writes are not replayed automatically.

All control framing has offline coverage. Local reserve and operating-mode changes were confirmed and restored on the development Powerwall 3. Grid switching and backup scheduling were not exercised on the live house system.

## Desktop app and console

The window title and sidebar identify the site by name. Local connections read the name from TEDAPI configuration, falling back to a previously confirmed site association when available. The hostname remains separate in connection details.

The desktop app Local screen selects the transport, hostname, key name, query version and refresh interval. Settings also exposes the interval while connected, including in read-only mode. Applying it updates both the cadence and cache lifetime, preserves control permission and remembers the new preference. A zero local refresh interval means manual-only; a positive interval is the delay in seconds after a completed refresh. The local response cache uses the same configured interval; zero disables it. Scheduled reads respect that cache, while an explicit TEDAPI refresh bypasses it. A request timeout reports a failed read and permits the next scheduled attempt; disconnect cancellation stops the loop. The Home page also supports an explicit refresh. One TEDAPI controller response supplies its power, battery and grid snapshot. Detailed component/meter reads are explicit actions on the System page and do not start another polling loop.

The desktop app Energy page plots and saves real LAN readings independently of cloud access. New samples append to the existing chart series; they do not rebuild the chart. Gaps remain gaps. The app records receipt times and retains missing measurements as missing. Saved samples reload after restart. The current calendar period advances at midnight, while an explicitly selected historical period stays selected. Weekly history uses Monday through Sunday, matching the cloud calendar response; empty and future buckets are gaps, not zero consumption. Period changes redraw the range, units and data; ordinary LAN arrivals retain the same series. Day plots use the same 0.01 kW display precision as the numeric reading, without rounding stored watts. The Day power graph gives LAN readings priority: cloud points are suppressed within continuous, valid LAN coverage for the selected component, and cloud lines cannot bridge those intervals. Missing LAN values and polling gaps leave cloud history available. Cloud averages are purple and LAN readings use the selected component colour. Week, Month, Year and Lifetime show accumulated kWh as bars; Day retains kW lines. Source selection updates existing chart collections on normal arrivals. The database is keyed by the authenticated local device identity, so a changed address does not create a new physical system.

The Powerwall Day graph uses signed power: positive values mean discharge and negative values mean charge, with that meaning identified on the axis. Longer periods show only the reported battery contribution to household consumption, excluding battery export to the grid and without subtracting charging. Missing contribution data remains a gap. Older cache entries are refreshed once when that contribution is requested; a current response that omits it is not repeatedly fetched. Lifetime samples are aggregated into calendar months, preserving missing months. Date and time labels remain horizontal in every period, including after changing periods on an already displayed chart.

A saved Owner or Fleet account supplies earlier calendar history on demand. On first setup, select the cloud site belonging to the local Powerwall. The app then binds that site to the authenticated device identifier independently of credentials. A different history account must have access to the same site; it cannot silently associate a different one. Once bound, the site picker is hidden. No automatic provider fallback or cloud polling loop is installed. A working cloud sign-in is needed for uncached history. The tested Powerwall's local configuration reports its site name but does not expose the cloud energy-site ID, so the confirmed cloud association supplies that ID.

Retrieved typed energy samples are stored in `%LocalAppData%\TeslaPowerwallLibrary\energy-history.sqlite`. Cache keys separate the provider/account, site, aggregation period, time zone and requested boundaries. A cache hit is checked before cloud authentication, including after app restart. Current periods and empty responses expire after five minutes; a nonempty completed period fetched at least 24 hours after its end is reused without automatic refresh. **Refresh from cloud** explicitly replaces a settled result if Tesla corrects it. Failed or empty refreshes preserve earlier successful data and display a warning. The page labels the source and retrieval time. A Day graph spans midnight to midnight in the computer's local time zone, with points at their actual timestamps; it does not fill missing hours with zero readings.

SQLite belongs only to the .NET 10 Windows application. The net472/net10 library has no database dependency or automatic history collection. Other consumers choose their own persistence suitable for their runtime, including Mono. The application uses `Microsoft.Data.Sqlite` for its desktop database.

While connected through Owner or Fleet, Settings provides separate actions to prepare a Windows key, register it, and check verification. Registration requires explicit readiness confirmation. Private keys stay in the Windows user key store; saved passwords remain DPAPI-protected. Permission to change local settings is not persisted. Signed local Settings also provides backup-event reads and explicit maximum-backup and grid-connection controls. Power-affecting actions require session permission and a separate confirmation. Switching connections clears old Home readings.

Console examples:

```text
TeslaPowerwallTestConsole local-discover
TeslaPowerwallTestConsole local-key create --local-key MyPowerwallKey
TeslaPowerwallTestConsole --cloud local-key status --local-key MyPowerwallKey
TeslaPowerwallTestConsole --cloud local-key register --local-key MyPowerwallKey --ready-for-physical-verification
TeslaPowerwallTestConsole --host powerwall-hostname.local --local-protocol TedapiSigned --local-key MyPowerwallKey local-telemetry
```

Use existing protected console settings or `PW_PASSWORD` for the local customer password; avoid placing secrets in shell history. The following commands are shared by the command-line and interactive interfaces, including argument validation and control guards:

| Library capability | Console command |
| --- | --- |
| Basic and detailed controller reads | `local-telemetry`, `local-detailed` |
| Controller or selected configured device components | `local-components [device-din]` |
| Multi-device snapshot and availability | `local-devices` |
| Fan, temperature and PV diagnostics | `local-diagnostics` |
| Configured meter channels and summaries | `local-meters`, `local-meter-aggregates` |
| Native customer meter counters | `local-native-meters` |
| Hardware, firmware and update status | `local-system` |
| IEEE 2030.5 service metadata | `local-ieee20305` |
| Stored inverter and protection-test results (read only) | `local-inverter-tests`, `local-protection-tests` |
| Non-secret operating configuration | `local-configuration` |
| Device identity and current read-fallback state | `local-connection` |
| Existing local backup events | `local-backup-events` |
| Partial settings update using the current configuration hash | `local-settings --reserve <percent> --mode <mode> --grid-charging <true\|false> --grid-export <rule>`; supply only fields to change |
| Explicit manual-backup and grid commands | `local-backup-start <minutes>`, `local-backup-cancel`, `local-off-grid`, `local-reconnect-grid` |
| Advertised local host discovery | `local-discover` |

`--local-query-version June2026` selects the newer query set. Existing general commands (`power`, `level`, `timeremaining`, `vitals`, `alerts`, `operation`, and grid-setting commands) also use the selected local backend. Key preparation, cloud-assisted enrollment and verification status remain one-shot `local-key create`, `local-key register` and `local-key status` commands. Registration retains its separate readiness guard; it is not part of a local connection or telemetry read.

Local writes require a signed session started with `--allow-local-control`; command-line controls also require an explicit `--local-protocol TedapiSigned`. The permission is never persisted. A partial `local-settings` update preserves omitted fields, including distinguishing an omitted value from explicit reserve zero or disabled grid charging. No control is executed on connection. The command acknowledgement is not proof of physical actuation.

For a separately accessible setup-network route, supply `--local-setup-host <host>` and the full equipment-label password in `PW_SETUP_PASSWORD`. The console authenticates and owns this separate read-only client, retains it through the session and disposes it after the primary connection. Neither the route nor its password is persisted. No adapter or Wi-Fi connection is changed. The route supports follower reads by default; add `--local-read-failover` to opt into controller read failover and optionally `--local-retry-seconds <positive-seconds>` (default 60). `local-connection` reports which read route is currently selected without forcing a new telemetry request.

The console has dedicated NUnit tests on .NET Framework 4.7.2 and .NET 10 for Windows, covering the shared parser, command guards, partial updates and setup-route credential separation. Read-only interactive checks on both runtimes exercised the existing signed Powerwall connection, detailed telemetry and backup events. Setup-network failover and interactive console power-control execution remain offline-tested only. The library reversible settings fixture has separately confirmed and restored reserve, operating mode and Owner Storm Watch on both runtimes.


## Current validation boundary

- Offline library tests run on .NET Framework 4.7.2 and .NET 10.
- Desktop presentation/configuration tests run on .NET 10 for Windows with NUnit and its test adapter.
- One Powerwall 3 completed key verification and successful signed LAN reads over Ethernet and its home Wi-Fi connection on both target runtimes, including detailed controller data on .NET Framework 4.7.2. The home Wi-Fi checks confirmed the same device identity using the existing password and signing key; no settings or power controls were changed. This does not validate the separate setup Wi-Fi transport.
- The June 2026 query set also passed read-only checks on the same Powerwall 3 using both target runtimes. Setup Wi-Fi transport and other hardware variants require separate hardware validation. Reserve and operating mode passed reversible local checks; grid switching and backup scheduling remain untested on hardware.
- The pinned upstream TEDAPI functional scope is implemented, with deliberate compatibility differences and hardware limits recorded below. Configured Neurio and Tesla remote-meter channels now preserve physical meter identity, original CT slots, enabled assignments, per-slot scaling and missing measurements. `GetLocalMeterReadingsAsync` and `LocalMeterProjection.Create` expose this typed interpretation; the desktop detailed view and console `local-meters` command use it. Existing typed component reads already expose the reported Powerwall 3 fan, temperature, PV-string and self-test signals.
- `GetLocalDeviceSnapshotAsync` collects configured Powerwall 3 devices sequentially and attaches measurements only to matching identities and expansion packs. Setup-network TEDAPI supports configured follower routes; signed LAN can use an explicitly supplied, already authenticated `LocalFollowerConnection`. Without one, signed LAN reports followers unavailable. Legacy bus battery measurements require matching identities and aligned arrays, and missing messages are not reused as current readings. The single Powerwall 3 signed path passed read-only hardware tests on both runtimes; multiple-device and legacy hardware behavior has offline coverage only.
- `GetLocalMeterAggregatesAsync` provides typed summaries from configured Neurio/remote-meter channels, retaining authoritative controller power where present and converting reported watt-seconds to watt-hours. Unreported fields remain null. SYNC Meter X/Y, Meter Z, ISLANDER and inverter/component voltage, frequency and measured phase currents fill otherwise missing fields. Both the legacy bus and newer SYNC component representation are handled. Missing current is not estimated from power, voltage or an assumed power factor. The existing basic meter endpoint is unchanged.
- Classic gateway protobuf vitals and explicit setup Wi-Fi follower routing have offline coverage on both runtimes. Their hardware paths have not been validated. Signed LAN rejects follower routing unless an explicit setup-network follower connection is supplied.
- No release has been published for this revision.

## Upstream parity review

The pinned reference's implemented TEDAPI functionality is covered by typed library APIs and consumer integration. This comparison excludes its fabricated compatibility responses and unrestricted raw endpoint access. It is not a claim of hardware validation for every variant or firmware.

| Capability | Current implementation and evidence |
| --- | --- |
| Signed LAN, setup-network Basic, installer bearer | All three transports have offline framing/authentication tests. Signed LAN has live evidence on both runtimes. |
| Controller/component reads, configuration, firmware and native meter counters | Typed APIs; both signed query sets passed live reads on the development Powerwall 3. |
| Battery/expansion identity and multiple devices | Typed snapshot with explicit per-device failures. Offline multi-device and follower tests; one Powerwall 3 tested live. |
| Fan, temperature, PV string, alert and meter helpers | Typed projections with source identities and missing-value handling. SYNC X/Y and MSA Z bus/component representations are covered. |
| Explicit settings, backup events and grid controls | Control opt-in and offline request/acknowledgement tests. Signed local reserve and operating-mode changes were confirmed and restored on the development Powerwall 3, on net472 and .NET 10. Backup scheduling and grid-switching commands have not been exercised live. |
| LAN-to-setup-network read failover | Explicit opt-in using an already authenticated, caller-owned connection to the same controller. Bounded recovery on subsequent reads, with offline routing and identity tests. No automatic network setup, cloud fallback or redirected controls. |
| Detailed diagnostic metadata | Attributed models cover phase detection, inverter self-test results, legacy firmware progress groups, IEEE 2030.5 and protection-test results. An automated query-field audit checks both bundled query sets, excluding only the remote-service session credential. Unknown scalar representations and object/list cardinality are preserved without guessing units or inventing measurements. |
| Derived/default values in upstream compatibility dictionaries | Deliberate differences: no fabricated zero readings, assumed phase topology, estimated RMS current or synthetic installation data. Existing vitals maps are projected from typed measurements. |

The field-type probe is read-only and records JSON field names/kinds, not their values. It covers both query versions and every array slot. Null sections do not establish their populated schema. `LocalDiagnosticScalar` preserves the actual number, text or boolean; numeric tokens retain their precision, and conversion to a numeric CLR value is explicit. `LocalDiagnosticRecords<T>` retains a single object versus a record list, original order and null slots. Known fields use attributed models; objects and arrays cannot be hidden inside a diagnostic scalar. No fallback units, timestamps, phase meanings or completed-test values are synthesized.

`GetLocalIeee20305Async`, `GetLocalInverterSelfTestsAsync` and `GetLocalProtectionTestStatusAsync` expose the three supplemental read-only vendor queries. They use the captured June 2026 definitions independently of the normal telemetry query selection and therefore require firmware that accepts those signatures. The corresponding methods on `PowerwallTedapiClient` omit the `Local` prefix. Each query has its own cache and supports forced reads. Partial or rejected responses are not cached. Desktop detailed refresh and the shared console command catalogue expose these reads; the desktop app and console omit IEEE provisioning PINs from display. Reading status never initiates a diagnostic procedure.

Synthetic fixtures verify populated diagnostic branches, nullable values, precision, container shapes and all three authentication transports. This provides model and transport coverage, not evidence that a self-test or phase-detection procedure has run on the development hardware. The independently reviewed [Go controller model](https://github.com/ygelfand/go-powerwall/blob/9694d852c8e9f008fa2d7e6a3a83dbc4c2f29c62/internal/powerwall/device_controller_response.go) supplies corroborating bus-field types but leaves several diagnostic trees untyped. No firmware update, self-test or phase-detection procedure was started to populate those fields.

Upstream's tariff, installer/customer registration, solar-brand, topology and other mock compatibility responses are not actual TEDAPI implementations. This client does not copy those fabricated values. Typed known native-meter access is provided; there is no new arbitrary raw-JSON native-endpoint API.

The protocol/query reference is [pypowerwall](https://github.com/jasonacox/pypowerwall/tree/3892e7c9352db80bffc6b5bda15a68d07062a25b), commit `3892e7c9352db80bffc6b5bda15a68d07062a25b`. Its MIT notice is retained with the bundled protocol resources.

### Reversible setting validation

The explicitly authorized local checks changed backup reserve by one percentage point and briefly changed operating mode, then confirmed the original settings through fresh reads. Storm Watch was separately changed and restored through the Owner API; it is not a local TEDAPI preference. These tests establish setting read-back, not the resulting energy dispatch behavior.

Hardware testing exposed a configuration replacement rejection after JSON re-serialization. Local updates now retain the original configuration bytes outside the requested values, including unknown fields, ordering, whitespace, escaping and numeric representations. Both setting changes then passed on the same device. The precise formatting sensitivity has not been isolated. Configuration data remains private; public control inputs are typed. Structured TEDAPI RPC errors are reported without replaying commands or exposing device diagnostic payloads.

The desktop Settings page and console `local-connection` command show the configured hostname and freshly resolved IP addresses. These addresses are name-resolution candidates, not a claim about the current TCP socket. The desktop prefers IPv4 when available and hides the resolved-address row when the configured value is already an IP literal. `ResolveLanAsync` can supplement normal DNS with a bounded query to active Ethernet/Wi-Fi gateways for a single-label DHCP hostname or its `.local` equivalent. The console uses the same lookup; IPv6 interface scopes are retained as fallback. Neither lookup changes system DNS or authenticates an address. The hostname remains configured so DHCP changes do not replace it with a stale IP address.

A follow-up read-only probe also exercised the June 2026 `IEEE20305Query`, `PinvSelfTestQuery` and `ProtectionTripTestQuery` using the pinned vendor query bytes and signatures. All three were accepted. IEEE 2030.5 nested fields and inverter self-tests remained null. Protection tests returned eight result entries with string status/type fields, but every measurement and timestamp was null. No procedure was started, and these supplemental reads do not establish populated measurement schemas.

### Release-candidate validation, 8 October 2026

After preserving the 2.0 contracts, the complete offline Release suite passed: 394 library tests on each runtime, 50 console tests on each runtime, 125 desktop tests and 59 credential/tool tests. SDK package validation against published 2.0.0 passed on both targets without API suppressions. Generated documentation built without warnings or errors. This run did not repeat hardware operations; the earlier evidence below remains the live validation boundary.

### Completed parity validation, 8 October 2026

All 373 offline library tests passed on each of net472 and .NET 10; all 49 console tests passed on each target; all 90 desktop tests passed on .NET 10 for Windows. The query-field audit checks 697 selected field occurrences in the legacy resource and 438 in the June 2026 resource, including one explicitly excluded remote-service credential per resource.

Both signed query versions and the new typed diagnostic facade passed read-only hardware tests on both runtimes. The typed IEEE section was returned, inverter-test metadata remained absent, and eight protection result entries were returned. Those reads do not establish populated measurements where the device reports null. An initial simultaneous framework run hit the gateway login rate limit on .NET 10; the separate rerun passed all three tests. Live tests must select one framework at a time.

Implementation parity is complete for the pinned scope described above. Setup Wi-Fi, multiple physical devices and other hardware variants retain offline rather than live evidence. No grid switching, backup scheduling, self-test, phase-detection or firmware-update procedure was executed. This functionality is included in 2.1.0.

Desktop Settings starts with local controls read-only unless the connection was explicitly opened with control permission. The Local controls card offers **Enable controls for this session**, which reconnects to the same local host using the existing credentials and signing key. Enabling controls sends no settings commands; backup reserve, operating mode and grid preferences change only after Apply. The button changes to **Return to read-only** while controls are enabled; it restores a connection with control writes disabled without applying pending edits. If reconnection fails, the app retains and explicitly reports the previous permission state. Permission is not saved and is cleared on disconnect. A failed unlock retains the previous read-only connection.

The System page labels the device response as **Alerts and status codes**, preserves the exact identifiers and shows the response receipt time. Firmware includes routine statuses alongside diagnostic flags; the app does not assign undocumented severity or occurrence times. These entries refresh when the page opens or Reload is chosen.
