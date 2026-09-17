# Development and validation history

See the [product changelog](CHANGELOG.md) for shipped changes. This document preserves test, CI and build history. Dated development entries describe work at that time, not a published product version or completed acceptance. Version headings identify the release alongside which development work was recorded.

## Where changes belong

- Product changelog and product release notes: shipped behavior, API, compatibility, fixes and runtime dependencies. Mention validation briefly when it helps explain a fix.
- This history: test coverage, CI, build tooling, work on pending versions. Split mixed entries so the product effect remains easy to find.
- Testing and workflow guides: current setup and operating instructions.
- Test-only or documentation-only changes do not require a product release.

<!-- development-history -->

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## CI validation - 2026-09-15 (no package release)

- Revalidate the current default-branch source after successful release workflows, including version commits created by GitHub Actions.
- Allow maintainers to configure exact-source, App-specific checks that must pass before publishing through `RELEASE_REQUIRED_CHECKS`; missing, failed or unconfirmed checks block the release.

## [1.2.5] - 2026-09-15

- Windows helper for dedicated Owner and Fleet test authorizations, with encrypted atomic token persistence, exclusive session ownership and access-token-only remote test inputs. Local gateway test authentication is not implemented.

- CI checks for both supported library runtimes, credential handling and the Setup build.

### Validation

- 118 library tests passed on both net472 and .NET 10; 47 credential, callback and privacy tests passed on Windows.

- Dedicated Owner and Fleet authorizations each passed three read-only live tests on Windows and three on a remote test host. Automatic region discovery passed with a North America/Asia-Pacific account; EU routing is covered by simulated HTTP tests.

- The Setup app builds and its callback handling has offline coverage. Its new embedded Fleet sign-in flow has not yet completed an end-to-end interactive validation; the manual browser fallback remains available.

## [1.2.0] - 2026-07-10

- MSTest-based deterministic unit test coverage (`TeslaPowerwallLibrary.Tests`).

## [1.0.2] - 2026-07-06

- GitHub Pages deploy now retries on transient failure.

[1.2.4]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v0.2.0-preview...v1.0.0
[0.2.0-preview]: https://github.com/oznetmaster/TeslaPowerwallLibrary/compare/v0.1.0-preview1...v0.2.0-preview
[0.1.0-preview1]: https://github.com/oznetmaster/TeslaPowerwallLibrary/releases/tag/v0.1.0-preview1