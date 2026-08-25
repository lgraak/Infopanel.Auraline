# Auraline M5 Host Configuration UI and Persistent Profiles Handoff

Date: 2026-08-25T15:07:57.4744488-07:00
Acceptance completed: 2026-08-25T15:32:44.8873680-07:00
Publication reconciliation: 2026-08-25T15:35:10.0929928-07:00
Status: completed and published; this evidence-only reconciliation follows the published M5 checkpoint
Model: GPT-5 Codex
Effort: High
Repository: InfoPanel.Auraline (`D:\Aeons\Git\Infopanel.Auraline`)
Branch: `main`
HEAD: `0151a7b957776be53026c297ccfbb65fcf1aa765` (published M5 checkpoint; this reconciliation follows it)
Authoritative remote: `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`

> This handoff is a continuation checkpoint, not authoritative truth. Current
> repository, remote, runtime, and test evidence wins if it conflicts with this
> document.

## Objective

Implement M5's functional loopback Host configuration UI and persistent provider,
source-group, and profile model while preserving M4 configuration, stable profile
identity, session/lease semantics, and transport compatibility. Repository,
Host/browser, package-activation, and direct InfoPanel profile-selection acceptance
are complete.
Linux, LAN transport, source mixing, additional visualizers, installer work, and M6
remain excluded.

## Authoritative Sources

- `README.md`, `docs/architecture.md`, `docs/roadmap.md`, and
  `docs/decisions/0008-persistent-profile-configuration.md`: durable M5 behavior,
  ownership, storage, compatibility, and deferral authority.
- `docs/handoffs/auraline-m4-handoff-2026-08-25.md`: inherited M4 runtime and
  publication checkpoint, reverified against current Git and running consumers.
- `docs/standards/ai-project-prompt-standard-v1.md` and
  `docs/standards/ai-project-handoff-standard-v1.md`: execution and handoff rules.
- Fresh Git, build, test, package, API, browser, transport-probe, Host-restart, and
  InfoPanel-consumer evidence collected on 2026-08-25 is time-sensitive and
  outranks inherited claims.

## Execution Context

- Windows 11 and PowerShell in the managed Codex workspace at
  `D:\Aeons\Git\Infopanel.Auraline`; no repository-local `AGENTS.md` exists.
- Resonance Signal and the matching local InfoPanel prerequisite were already
  running. Existing M4 consumers supplied two exact `300x300` and `600x150`
  demands throughout acceptance.
- The in-app browser exercised the real loopback UI. A separate cross-process
  transport probe exercised a selected saved M5 profile.
- Managed access was required for Git fetch, user NuGet configuration, controlled
  Host process restart, and any shared `%ProgramData%` package mutation.

## Current Repository State

- Preflight and current branch/HEAD: `main` at
  `1cdb65b2ee07b51d46c4e7a8719686948007c687`, aligned with `origin/main` at
  divergence `0 0` before M5 changes.
- Working tree: only intended M5 implementation, tests, documentation, and this
  handoff are present; no unrelated user changes were found.
- Commit `0151a7b957776be53026c297ccfbb65fcf1aa765` records the complete M5
  implementation, tests, documentation, and acceptance handoff.
- A fresh pre-push fetch confirmed `origin/main` remained at
  `1cdb65b2ee07b51d46c4e7a8719686948007c687`, local divergence `0 1`, and
  valid fast-forward ancestry. A normal push published M5.
- Post-push fetch and independent `ls-remote` readback returned
  `0151a7b957776be53026c297ccfbb65fcf1aa765` for local HEAD, tracking
  `origin/main`, and authoritative `refs/heads/main`; divergence was `0 0`.
- Authoritative remote was freshly fetched at preflight and remained the M4
  checkpoint. No merge, rebase, reset, stash, clean, or history rewrite occurred.

## Current Known-Good State

- Fresh repository validation passes restore, Debug and Release builds, 103 tests,
  format verification, Gitleaks, Markdown links, package-content validation,
  portability scans, and `git diff --check`.
- The freshly built Release M5 Host is running healthy as `1.0.0-m5`. It loaded
  one provider, one default source group, and three saved profiles; two existing
  InfoPanel consumers recovered as exact `300x300@30` and `600x150@30` sessions
  on `default-profile` revision 3.
- The exact four-file M5 package is active in the matching local InfoPanel
  prerequisite. InfoPanel displayed `Auraline v1.0.0-m5`, refreshed all three
  persistent profiles, persisted the friendly/stable
  `M5 Purple Wave [profile-b7969ef2103143f5a48d3c6792d70023]` choice, moved both
  exact-size consumers to profile revision 2, and then restored
  `Default Waveform [default-profile]` with both consumers on revision 3.

## Completed Work

- Added schema-versioned product catalog, last-known source snapshots, independent
  source-group/profile files, same-directory atomic replacement, migration-safe
  bootstrap, malformed-file preservation, and fail-closed persistence.
- Added provider CRUD plus dependency-safe deletion, retained enable/reconnect/
  refresh behavior, and persisted fresh/stale source evidence.
- Added source-group and profile create/edit/duplicate/default/delete APIs and UI,
  stable IDs, validation, dependency checks, conservative source resolution, and
  explicit unsupported-runtime handling for non-default source groups.
- Added working-copy profile editing and PNG preview through the live waveform
  state and real renderer. Cancel leaves persistence unchanged; Save increments a
  revision.
- Added fixed scale, bounded smoothing, centered-line, trace color, 30/60 FPS, and
  transparent-background profile settings.
- Hot-applied saved profile revisions inside existing render loops with diagnostic
  revision/counter evidence and no session, lease, geometry, cadence, scheduler, or
  mapping replacement.
- Refreshed InfoPanel configuration choices from the current loopback Host catalog
  with a one-second bound while retaining friendly-name/stable-ID formatting and a
  safe fallback.
- Updated the transport probe to accept an explicit profile ID and reconciled M5
  architecture, roadmap, component, test, and decision documentation.

## Decisions Made

- Preserve `host.json` rather than forcing a risky provider/settings migration;
  store independently edited product objects separately.
- Stable IDs are immutable; display names are mutable. Deletion fails on default,
  profile/session, group/profile, and provider/group dependencies.
- Last-known sources are retained but explicitly marked stale/offline. Exact
  identity wins, a unique provider-scoped name/kind match may rebind, and ambiguity
  remains unresolved.
- Configuration capability is not runtime capability. Explicit-source,
  multi-source, and cross-provider groups persist truthfully but fail preview and
  attach until mixing is implemented.
- Preview consumes an unsaved working copy and current render state only; it does
  not persist, create sessions, expose samples, or mutate consumers.
- Saved profile revisions hot-apply. Requested session cadence remains a session
  compatibility key, so changing saved FPS affects subsequent attachments rather
  than silently mutating an existing session's cadence.

## Files Changed

- `README.md`: M5 operation, storage, UI, selection, build, and limitations.
- `docs/architecture.md`, `docs/roadmap.md`: implemented M5 architecture and
  roadmap state.
- `docs/decisions/0008-persistent-profile-configuration.md`,
  `docs/decisions/README.md`: persistent-object and hot-apply decision.
- `docs/handoffs/auraline-m5-handoff-2026-08-25.md`: this checkpoint.
- `src/Auraline.Host/Auraline.Host.csproj`,
  `src/InfoPanel.Auraline/InfoPanel.Auraline.csproj`,
  `src/InfoPanel.Auraline/PluginInfo.ini`: M5 version metadata.
- `src/Auraline.Host/Configuration/AuralinePaths.cs`,
  `ProductConfigurationModels.cs`, `ProductConfigurationValidator.cs`,
  `ProductConfigurationStore.cs`, and `ConfigurationApi.cs`: paths, models,
  validation, atomic storage, resolution, CRUD, dependencies, and preview API.
- `src/Auraline.Host/Program.cs`, `Web/UiRenderer.cs`, and
  `Web/HealthContract.cs`: M5 composition, functional pages, and health.
- `src/Auraline.Host/Providers/ProviderManager.cs`: provider mutation and source
  snapshot integration.
- `src/Auraline.Host/RenderSessions/RenderSessionApi.cs` and
  `RenderSessionManager.cs`: persistent catalog, supported-group checks, and
  revisioned hot apply.
- `src/Auraline.Host/Waveform/WaveformRenderer.cs`: profile scale, smoothing,
  centered line, color, and background settings.
- `src/Auraline.Host/README.md`, `src/InfoPanel.Auraline/README.md`, and
  `tests/README.md`: component and validation guidance.
- `src/InfoPanel.Auraline/AuralinePlugin.cs`: bounded live catalog refresh for
  configuration properties.
- `tests/Auraline.Host.Tests/ProductConfigurationStoreTests.cs`,
  `ConfigurationApiTests.cs`, `RenderSessionApiTests.cs`,
  `RenderSessionManagerTests.cs`, and `WaveformRendererTests.cs`: M5 Host
  behavior and failure-path coverage.
- `tests/InfoPanel.Auraline.Tests/ConfigurationAndPortabilityTests.cs`: catalog
  refresh/fallback and boundary coverage.
- `tests/Auraline.TransportProbe/Program.cs`: selectable profile probe.
- Generated `bin/`, `obj/`, Release package files, runtime configuration, and
  mappings remain excluded from Git.

## Validation Completed

- Fresh fetch and preflight: clean `main`, local/origin divergence `0 0`, exact
  starting HEAD `1cdb65b2ee07b51d46c4e7a8719686948007c687`.
- Publication: staged diff check passed for 32 intended files; commit
  `0151a7b957776be53026c297ccfbb65fcf1aa765` was pushed by normal fast-forward.
  Fresh fetch plus `ls-remote` matched that SHA with numeric divergence `0 0`.
  An initial post-push assertion falsely failed by comparing PowerShell's
  tab-formatted display string; the SHA values were already identical, and the
  corrected numeric assertion passed.
- `dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config`: passed after
  narrow managed access to the user NuGet configuration.
- Debug and Release solution builds: passed with 0 errors and three established
  Skia obsolete-text warnings.
- Debug tests: 34/34 InfoPanel tests and 69/69 Host tests, 103/103 total, passed.
- `dotnet format ... --verify-no-changes --no-restore`: passed after applying the
  formatter to new C# files.
- `gitleaks dir . --no-banner --redact`: passed; about 1.67 MB scanned, no leaks.
- Repository-relative Markdown link check: 28 files, no broken links.
- Package check: exactly `Auraline.Contracts.dll`,
  `InfoPanel.Auraline.deps.json`, `InfoPanel.Auraline.dll`, and
  `PluginInfo.ini`; no InfoPanel/Skia-supplied assemblies.
- Portability checks: memory-mapped-file types remain only under Windows platform
  adapters; InfoPanel/Skia implementation types remain at the plugin edge.
- `git diff --check`: passed. Final diff review found only intended M5 scope.
- Browser CRUD/preview: created and saved `M5 Purple Wave`, verified Cancel did
  not persist an unsaved color, preview changed through the real renderer, created
  an independent duplicate, and preserved the original.
- Hot apply: edited an active `default-profile`; both existing session IDs and
  leases remained stable while revision/hot-apply diagnostics advanced. Defaults
  were restored afterward, leaving revision 3.
- Dependency/default rejection: default profile, default group, referenced group,
  referenced provider, and active profile deletion paths returned conflicts.
- Unsupported group: a real explicit-source group persisted and diagnosed, while
  preview and attach returned clear unsupported-runtime conflicts. Its temporary
  test profile/group were deleted afterward.
- Provider outage: disabling retained a stale/offline source snapshot and degraded
  group status; re-enable, refresh, and reconnect restored Connected/Active state.
- Cross-process probe: selected saved profile at `480x180@30`, observed 193 reads,
  changing content, about 26.32 observed FPS, heartbeat, and clean detach.
- Host persistence restart: three profiles, one group, provider state, and default
  identity survived; both existing InfoPanel consumers recovered automatically.
- Final readback: Host `1.0.0-m5` healthy, default `default-profile`, three
  profiles, one group, two sessions, two leases, exact `300x300@30` and
  `600x150@30`, about 29.34/29.38 actual FPS, saved InfoPanel selection
  `Default Waveform [default-profile]`.
- M5 package activation: the installed four files matched Release SHA-256 values;
  InfoPanel visibly reported `Auraline v1.0.0-m5`, and its plugin-host log
  confirmed initialization, saved-config load, both mappings, and exact resizes.
- Direct profile selection: choosing `M5 Purple Wave` persisted the exact stable
  ID and moved both consumers to that profile/revision without Host or InfoPanel
  restart. Restoring `Default Waveform` persisted `default-profile` and moved
  both consumers back.
- Not run: Linux, LAN, source mixing, final installer, public InfoPanel build, and
  M6 validation; all are excluded, unavailable, or deferred.

## Production State Versus Repository State

- Implemented: bounded M5 repository behavior is complete.
- Committed: `0151a7b957776be53026c297ccfbb65fcf1aa765`; this publication
  reconciliation is a separate evidence-only commit.
- Pushed: M5 is published through
  `0151a7b957776be53026c297ccfbb65fcf1aa765` with authoritative divergence
  `0 0` before this reconciliation.
- Deployed or activated: the M5 Host Release binary and local per-user M5
  configuration are active. The exact M5 four-file InfoPanel package is active
  locally with a verified rollback backup.
- Runtime-validated: Host/UI/configuration persistence, real preview, saved-profile
  probe, dependency failures, outage/stale recovery, hot apply, M4 wire
  compatibility, M5 plugin activation, persistent catalog refresh, stable-ID
  selection, and default restoration passed locally.
- Documented or planned only: mixing, Linux, LAN/network transport, additional
  renderers, installer, and M6.
- Production environment: none; all activation evidence is local acceptance state.

## Unresolved Issues and Unverified Assumptions

- A verified four-file rollback copy exists at
  `C:\ProgramData\InfoPanel\backups\InfoPanel.Auraline.backup-20260825-151659`.
  It was moved out of the plugin-discovery folder after fresh logs proved that
  InfoPanel treats every sibling under `plugins` as a plugin candidate.
- The required InfoPanel consumer-demand prerequisite remains local and unpublished;
  installed public preview `1.4.0-preview.2.43` remains incompatible.
- Three established obsolete Skia text API warnings remain; M5 does not alter those
  calls.
- The local acceptance profiles `M5 Purple Wave` and `M5 Purple Wave Copy`
  remain in per-user configuration. They are local test/product state, not
  repository content.

## Safety, Rollback, and Access Considerations

- No force push, reset, stash, clean, history rewrite, merge, LAN exposure, secret
  handling, or unrelated cleanup occurred.
- Controlled local side effects were Host restarts, product configuration CRUD,
  provider disable/re-enable/reconnect/refresh, two retained profile creations, and
  deletion of the temporary unsupported profile/group. Deleted acceptance objects
  are not recoverable except by recreation.
- Repository rollback is an ordinary revert after publication. Product-object
  rollback requires restoring the corresponding per-user JSON backup or editing
  through the UI.
- Approval was supplied; the backup, replacement, and hash readback completed.
  Rollback requires exiting InfoPanel, copying the exact four backup files over the
  plugin folder, and relaunching the matching local prerequisite.

## Do Not Redo or Reopen

- Do not replace or restructure `host.json` as an M5 cleanup; preserving M4
  migration is intentional.
- Do not infer mixing support from persistent source-group support or bind ambiguous
  sources automatically.
- Do not turn preview into a session, save-on-change path, raw-sample endpoint, or
  alternate renderer.
- Do not change M3/M4 transport, session IDs, lease semantics, or mappings for
  profile hot apply.
- Do not retry an in-place active `%ProgramData%` package overwrite without the
  approved backup-and-reload boundary.

## Next Recommended Action

Begin the bounded M6 diagnostics and beta-readiness milestone while retaining
upstream publication of the InfoPanel consumer-dimension prerequisite as a separate
public beta release gate.
