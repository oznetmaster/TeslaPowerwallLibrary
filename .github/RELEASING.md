# Public releases and completed validation

Normal tag releases require successful hosted and configured processor checks for the exact release commit. Publishing restores/builds, validates the package API, packages and publishes; it does not execute the desktop suites a second time. Public tags and NuGet versions have three numeric parts.

For a release-only follow-up commit, manually run **Publish .NET Package** with `version` and `validation_commit`. The latter must be a full ancestor SHA whose hosted and processor checks completed successfully within the last 24 hours. The script compares the commits: only root Markdown, documentation-site files, and the specifically listed release-automation files may differ. Runtime sources, tests, projects, dependencies, compiler settings and validation-workflow changes require fresh checks. The checked SHA and release SHA appear in the run summary; this is reuse of an existing pass, not a new execution.

Recently completed manual desktop and processor runs can also be used independently:

- Select `skip_desktop_checks` and supply `desktop_skip_reason` to use the desktop results you have reviewed.
- Select `skip_hardware_checks` and supply `hardware_skip_reason` to use the processor results you have reviewed. This existing option also supports an explicitly recorded unavailable-hardware exception.
- Each reason must identify the tested source, completion time and report location. Desktop evidence should include frameworks and pass counts; processor evidence should include suite results, test-instance cleanup and reservation release. The operator is responsible for confirming the results cover the release inputs; free-text evidence is an operator attestation, not automatic verification of private reports.

Both overrides require an explicit manual workflow invocation and a nonempty single-line reason. A desktop override preserves processor and other required checks; a processor override preserves desktop and other required checks. Selecting both records both attestations. The summary identifies the release source, bypassed checks and reasons, and explicitly does not claim that new tests ran. Tag pushes cannot select these overrides.

Manual invocation publishes both the NuGet package and the GitHub release/assets from the checked-out commit, just like the normal tag path. The post-release workflow reuses existing successful or running default-branch validation instead of dispatching it again.
