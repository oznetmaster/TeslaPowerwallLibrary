# Cake Frosting pilot

This is an optional Windows build of the existing solution, six offline desktop
test runs, the NuGet package and five application bundles. Cake.Frosting 6.3.0 is
a build-only dependency in this standalone project, outside the shipping solution.
Its PKCS dependency is pinned to patched 10.0.13; both are recorded in the build
project's package lockfile and do not change shipping runtime dependencies.
The public release workflow and its validation gates are unchanged. This pilot
has no publication, credential setup, live hardware or processor-test target.

The existing `Unit tests` CI workflow now calls this project through a small local
composite action. Its six desktop test runs keep their original independent jobs
and matrix labels, and the credential job still builds the setup application.
The package-compatibility job uses `ReleaseArtifacts` to exercise package/API
validation and all five application bundles without duplicating those six runs.
The public publishing workflow and shared desktop/processor validation gates
are still unchanged.

```powershell
dotnet run --project build/Build.csproj -c Release -- --target=Suite --suite=powerwall --framework=net472
dotnet run --project build/Build.csproj -c Release -- --target=Suite --suite=console-net10 --framework=net10.0-windows
dotnet run --project build/Build.csproj -c Release -- --target=CredentialTools --suite=credentials
dotnet run --project build/Build.csproj -c Release -- --target=ReleaseArtifacts
```

Individual suite names are `library-net472`, `library-net10`, `console-net472`,
`console-net10`, `desktop` and `credentials`. The `powerwall` selector used by the
existing matrix requires a matching framework. Unknown or mismatched selectors
fail. `Suite` restores/builds only its selected test project; the full `Test` target
continues to reuse the one solution build. Both paths share commands, the Live
exclusion filter and a TRX readback that rejects zero-test or unsuccessful runs.
`ReleaseArtifacts` records `desktopTests: false`; it produces files only and is
not evidence that desktop or processor validation ran. It does not publish.

From the repository root, using the .NET SDK selected by `global.json`:

```powershell
dotnet restore build/Build.csproj --locked-mode
dotnet run --project build/Build.csproj -c Release --no-restore -- --target=Verify
```

The default target is also `Verify`. Each run creates a fresh directory beneath
`artifacts/cake-pilot/` and prints its path. Logs, command results, six TRX files,
NuGet output, application publish folders, release assets and a content manifest
are retained there. Existing output directories are rejected, never cleared.
Use `--output=artifacts/cake-pilot/my-run` to select a new directory, or
`--version=2.3.2` to set a three-part version; the default comes from the library
project. No version file is edited.

Task sequence:

```text
Initialize -> Restore -> Build -> Test -> Pack -----> Package -> Verify
                            \-> Applications -----/
```

`Build` runs once in this graph. Test, pack and application publish reuse those
binaries. All test commands explicitly exclude the `Live` category. Pack retains
the library project's existing SDK package/API validation against the public
baseline. Build, pack and application publish receive the Release/CI/version
properties; restore retains the existing workflow's unversioned solution command.
Passing the release version into restore changes companion-project metadata in
project.assets.json and their generated deps files, so it is intentionally avoided
for this parity pilot. DocFX generation is disabled as in the release build.

`Verify` checks package identity, both framework DLL/XML payloads, documentation
and the upstream notice against their source bytes; it also checks that Cake
has not become a runtime dependency. A failure stops subsequent tasks and leaves
the run evidence in place. The full solution includes WPF, Windows credential
tools and .NET Framework tests, so this pilot requires Windows. Concurrent runs
should use separate checkouts, because the existing projects share bin/obj paths.

## Compare with the current workflow

Build reference assets using the existing `dotnet-publish.yml` restore/build,
pack, application-publish and collect steps, with the same source, SDK and version.
Do not run its NuGet push or GitHub release steps. Point the pilot at the resulting
root containing `nuget/` and `release/`:

```powershell
dotnet run --project build/Build.csproj -c Release --no-restore -- --target=Verify --baseline=artifacts/cake-reference --output=artifacts/cake-pilot/comparison
```

The comparison checks every release file and every uncompressed ZIP/NuGet entry
by SHA-256, reporting missing, added or changed entries. ZIP timestamps, compression
and entry order are container details and are not compared. NuGet's generated
OPC relationship IDs and core-properties filename are normalized; relationship
types/targets and the core-properties contents remain checked. This compares
unsigned local outputs, not the signature added by NuGet.org to a public package.
SDK/compiler differences may produce real binary differences and must be explained,
not automatically ignored. Comparison results are in `comparison.json`.

```powershell
dotnet test build/Tests/Build.Tests.csproj -c Release
dotnet run --project build/Build.csproj -c Release -- --tree --target=Verify
```

The manual-only `Cake Frosting pilot` GitHub workflow calls the same build entry
point and retains evidence as an Actions artifact. It does not replace the current
test matrix, processor workflow, release locks, validation-reuse policy or publishing
path. Moving those entry points is a separate decision after this pilot is reviewed.

## Local pilot result — 31 October 2026

Using SDK 10.0.403 and package version 2.3.2, the full target completed successfully
in 52.5 seconds with warm build outputs. This is a local execution time, not a
measurement of GitHub runner provisioning or hardware-test time.

| Offline suite | Passed |
| --- | ---: |
| Library / net472 | 511 |
| Library / net10.0 | 511 |
| Console / net472 | 51 |
| Console / net10.0-windows | 51 |
| Desktop / net10.0-windows | 126 |
| Credential tools / net10.0-windows | 59 |
| **Total** | **1,309** |

The ten build-tool tests passed. SDK package/API validation passed. One NuGet
package and 17 release files (including five application archives) were produced;
all 228 normalized manifest entries matched a clean reference generated using the
existing release commands. No DLL, dependency JSON, notice or document difference
was excluded from comparison. Generated OPC identifiers and ZIP container metadata
are the only comparison normalizations described above.

Local evidence is retained in `artifacts/cake-pilot/verified-20261031/` and
`artifacts/cake-reference-20261031-clean/`. Earlier failed comparisons are retained
in the other dated directories. They exposed a restore-property difference and
cached dependency files in the original warm reference; the clean reference
eliminated the latter without changing the comparison rules.

These are the original local-pilot results, before CI integration. The manual
workflow had not been dispatched and there was no live/processor testing, public
upload, commit, push or release during that first validation. Later CI evidence
is recorded separately in the development history and GitHub run linked there.
