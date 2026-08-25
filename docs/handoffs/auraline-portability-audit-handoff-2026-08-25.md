# Auraline Portability Audit Handoff

Date: 2026-08-25 08:55:34 -07:00
Status: Completed locally; publication evidence pending
Model: GPT-5 Codex
Effort: High
Repository: InfoPanel.Auraline at `D:\Aeons\Git\Infopanel.Auraline`
Branch: `main`
HEAD: `1aa8ebe034d02950ae2bb997ca6ab15cce3f5ed7` at preflight; implementation commit pending
Authoritative remote: `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`

> This handoff is a continuation checkpoint, not authoritative truth. Current
> repository, remote, runtime, and test evidence wins if it conflicts with this
> document.

## Objective

Audit the complete M1 solution for Windows-specific coupling, correct bounded leakage, establish the Windows-first/cross-platform architecture before M2, add focused tests, and publish the scoped result when safe. The implementation and validation are complete. No Linux implementation, major project split, M2 waveform work, dependency change, or external contract change was made.

## Authoritative Sources

- Current source, project files, tests, and Git evidence in this repository governed the audit.
- `README.md`, `docs/architecture.md`, `docs/roadmap.md`, and `docs/decisions/`: durable product and architecture boundaries.
- `docs/standards/ai-project-prompt-standard-v1.md` and `docs/standards/ai-project-handoff-standard-v1.md`: execution, validation, publication, and handoff requirements.
- `docs/handoffs/auraline-m1-handoff-2026-08-25.md`: prior checkpoint, verified against current Git before use.
- Fresh Windows process, filesystem, registry, TCP, HTTP, and provider observations from 2026-08-25: time-sensitive runtime evidence.
- `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`: authoritative publication target.

## Execution Context

- Windows and PowerShell in repository root `D:\Aeons\Git\Infopanel.Auraline`; .NET SDK resolved the repository's .NET 8 targets.
- No repository-local `AGENTS.md` exists. User-supplied root instructions and repository standards governed.
- The managed environment required narrowly scoped elevated access for Git metadata writes, NuGet/build artifacts, registry readback, and the bounded GUI process launch.
- The complete referenced portability packet was recovered before work began. The audit included all tracked source/projects/tests, solution and NuGet configuration, relevant ADRs, both standards, and the M1 handoff.

## Current Repository State

- Branch and HEAD: `main`; preflight `HEAD` was `1aa8ebe034d02950ae2bb997ca6ab15cce3f5ed7` (`Record Auraline M1 publication evidence`). Implementation commit: `IMPLEMENTATION_COMMIT_PENDING`.
- Working tree: only the intended portability code, tests, documentation, ADR, and this handoff are present before publication.
- Upstream and synchronization: a fresh `git fetch --prune origin` completed; preflight local `HEAD` and `origin/main` matched with divergence `0 0`.
- Commit and authoritative remote readback: `PUBLICATION_PENDING`.
- Preserved unrelated changes: none existed at preflight; no reset, stash, clean, overwrite, or destructive synchronization occurred.

## Current Known-Good State

- On 2026-08-25 the complete solution restored, built in Debug and Release with zero warnings/errors, passed all 22 tests, and passed formatting verification.
- The Release Host ran tray-only with no main window, listened only on `127.0.0.1:48481`, returned healthy M1 status, connected to one live local Resonance Signal provider, discovered one source, preserved the existing per-user path and quoted HKCU startup value, admitted only one process after a duplicate launch, and stopped cleanly after validation.

## Completed Work

- Classified every M1 source area and retained the existing solution structure because no major project split is required to establish the current boundary.
- Kept generic path construction in `AuralinePaths`; moved current-user `%LOCALAPPDATA%` resolution behind `IPlatformPaths` and `WindowsPlatformPaths`.
- Kept startup behavior behind `IStartupRegistration`; moved the HKCU implementation into `Platform/Windows` and added a narrow injectable registry adapter for behavior testing.
- Added `ISingleInstanceCoordinator`; moved the semaphore/named-pipe implementation and Windows Forms tray shell into `Platform/Windows`.
- Confirmed the browser launcher already has an interface and the provider, configuration, web, contracts, and plugin-scaffold logic do not directly consume Windows-only APIs.
- Added focused tests for the existing Windows path layout and quoted startup registration/delete behavior. Existing fake browser, provider connector, delay, and root-path tests continue to prove reusable logic can run without real platform integrations.
- Added ADR-0006 and durable architecture, roadmap, README, Host boundary, and test-scope documentation, including explicit M2 portability guardrails and honest Linux deferrals.

| Area | Current status | Correct boundary? | Linux counterpart / future work |
| --- | --- | --- | --- |
| Tray | Windows Forms shell under `Platform/Windows` | Yes | Linux tray/status notifier; framework deferred |
| Autostart | HKCU `Run` behind `IStartupRegistration` | Yes | XDG desktop or systemd-user approach after runtime inspection |
| Paths | `%LOCALAPPDATA%\Auraline\` behind `IPlatformPaths` | Yes after refactor | XDG configuration/data/log mapping |
| Single instance | Windows semaphore/named pipe behind `ISingleInstanceCoordinator` | Yes after refactor | Equivalent per-user Linux coordination |
| Browser launch | Shell execution behind `IBrowserLauncher` | Yes | Validate shared behavior or supply a Linux adapter |
| Provider client | OS-agnostic HTTP/protocol logic | Yes | Expected shared unchanged |
| Web Host/API | OS-agnostic ASP.NET Core logic inside the Windows-targeted Host project | Yes logically; physical split deferred | Expected shared; build boundary revisited with Linux Host evidence |
| Contracts | Dependency-light `net8.0` project | Yes | Expected shared unchanged |
| InfoPanel scaffold | Dependency-light `net8.0`, non-functional | Yes for current evidence | Compare actual Windows/Linux plugin APIs before selecting future project shape |

## Decisions Made

- Windows remains the only supported executable runtime; Linux is planned but not implemented or claimed.
- Platform-specific work belongs under explicit platform ownership and behind narrow interfaces where reusable consumers otherwise need the responsibility.
- A physical core or Windows/Linux Host project split is deferred. The audit found no evidence that justifies crossing the packet's major-split stop condition now.
- Future waveform protocol/decoding, stream lifecycle, sample/channel processing, normalization, smoothing, render states, renderer/frame contracts, and metrics remain OS-agnostic. SkiaSharp remains a cross-platform choice unless later package/runtime evidence contradicts that assumption.
- Existing Windows behavior, target framework, tray framework, HKCU mechanism, provider contract, loopback binding, and configuration schema remain unchanged.

## Files Changed

- `README.md`: states current Windows-only support, intended Linux-capable architecture, and M2 guardrail.
- `docs/architecture.md`: adds the platform ownership map and future counterparts.
- `docs/roadmap.md`: records Linux enablement after the Windows proof milestones without promoting it into M0-M6.
- `docs/decisions/README.md`: indexes ADR-0006.
- `docs/decisions/0004-per-user-json-configuration.md`: distinguishes the Windows path mapping from generic configuration storage.
- `docs/decisions/0006-windows-first-cross-platform-boundaries.md`: records the durable portability decision and audit evidence.
- `src/Auraline.Host/Configuration/AuralinePaths.cs`: removes embedded current-user Windows path resolution.
- `src/Auraline.Host/Configuration/StartupRegistration.cs`: retains only the platform-neutral startup contract/result.
- `src/Auraline.Host/Lifecycle/ISingleInstanceCoordinator.cs`: adds the platform-neutral coordination contract.
- `src/Auraline.Host/Lifecycle/SingleInstanceCoordinator.cs`: moved to `src/Auraline.Host/Platform/Windows/SingleInstanceCoordinator.cs`.
- `src/Auraline.Host/Lifecycle/TrayApplicationContext.cs`: moved to `src/Auraline.Host/Platform/Windows/TrayApplicationContext.cs`.
- `src/Auraline.Host/Platform/IPlatformPaths.cs`: adds the platform-path contract.
- `src/Auraline.Host/Platform/Windows/WindowsPlatformPaths.cs`: implements the established `%LOCALAPPDATA%\Auraline\` mapping.
- `src/Auraline.Host/Platform/Windows/WindowsStartupRegistration.cs`: owns and adapts the existing HKCU behavior.
- `src/Auraline.Host/Program.cs`: selects Windows implementations at the executable composition root.
- `src/Auraline.Host/README.md`: documents source ownership.
- `tests/Auraline.Host.Tests/PlatformBoundaryTests.cs`: covers Windows paths and startup behavior.
- `tests/Auraline.Host.Tests/SingleInstanceCoordinatorTests.cs` and `TrayApplicationContextTests.cs`: follow the explicit Windows namespace.
- `tests/README.md`: records portability-boundary coverage.
- `docs/handoffs/auraline-portability-audit-handoff-2026-08-25.md`: this checkpoint.
- Generated `bin/` and `obj/` artifacts remained ignored and are excluded.

## Validation Completed

- `git fetch --prune origin` plus local/upstream comparison: passed; preflight divergence `0 0`.
- Repository-wide tracked-file and Windows API/identifier searches: completed across `src/`, projects, solution, `NuGet.Config`, and tests. Remaining direct Windows APIs occur only under `Platform/Windows`; Windows-targeted tests intentionally reference Windows Forms.
- `dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config`: passed; all projects up to date.
- `dotnet build InfoPanel.Auraline.sln --configuration Debug --no-restore`: passed, zero warnings/errors.
- `dotnet build InfoPanel.Auraline.sln --configuration Release --no-restore`: passed, zero warnings/errors.
- `dotnet test InfoPanel.Auraline.sln --configuration Debug --no-build --no-restore`: passed, 22/22.
- `dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed; only expected working-copy line-ending conversion notices were emitted.
- `gitleaks dir . --no-banner --redact` with Gitleaks 8.30.1: scanned approximately 701 KB; no leaks found.
- Release runtime: responsive process with `MainWindowHandle == 0`; loopback listener only at `127.0.0.1:48481`; `/health` returned `healthy`, version `1.0.0-m1`, one enabled/connected provider, one source, and no configuration error.
- Duplicate runtime: second Release apphost exited within five seconds and only the original PID remained, covering the unchanged open-signal path.
- Per-user/runtime readback: schema-v1 configuration remained under `%LOCALAPPDATA%\Auraline\config\host.json`; the existing quoted HKCU Run value still pointed to the Release apphost. The validation Host was stopped and no Auraline process remained.
- Final complete diff, scope, M2 exclusion, documentation claims, and platform-dependency searches: reviewed before staging; no Linux code/dependency, waveform work, shared memory, functional plugin, service, database, framework replacement, external binding, or unrelated change was introduced.
- Not run: Linux runtime/build acceptance, because no Linux Host or Linux support is implemented; live tray-menu clicking, because automated construction/command coverage and tray-only process evidence were sufficient for this bounded refactor.

## Production State Versus Repository State

- Implemented: explicit Windows platform boundaries, path and single-instance interfaces, testable HKCU adapter, tests, ADR, architecture, roadmap, and support-language reconciliation.
- Committed: `IMPLEMENTATION_COMMIT_PENDING`.
- Pushed: `PUBLICATION_PENDING`.
- Deployed or activated: no installed application or production deployment. Runtime validation used the repository Release build and preserved existing per-user Auraline configuration/startup state.
- Runtime-validated: Windows tray-only process shape, loopback listener/health, live provider connection/discovery, duplicate admission, per-user path, startup value, and clean shutdown.
- Documented or planned only: Linux Host/tray/autostart/XDG paths/packaging/InfoPanel validation and all M2 waveform functionality.
- Unverified: Linux runtime behavior and Linux InfoPanel APIs; neither is present in the scoped workspace.

## Unresolved Issues and Unverified Assumptions

- The Host project still targets `net8.0-windows` because it contains the Windows executable shell. Reusable logic is source-isolated but is not independently cross-platform-buildable until evidence justifies a physical project split.
- Linux tray, autostart, single-instance, filesystem, packaging, and InfoPanel plugin details remain intentionally unselected pending actual Linux runtime/API evidence.
- No current evidence contradicts treating SkiaSharp as cross-platform for M2; package/runtime compatibility must still be verified when that dependency is introduced.

## Safety, Rollback, and Access Considerations

- No Linux dependency, administrator requirement, service, scheduled task, firewall change, credential, secret, external binding, audio capture, waveform data, or external repository change was introduced.
- Runtime validation re-applied the already-enabled quoted current-user startup value to the same Release apphost path, then stopped only the validation Host process. Existing configuration and logs were not removed.
- Repository rollback is an ordinary Git revert after publication. Reverting repository code does not remove existing `%LOCALAPPDATA%\Auraline\` state or the HKCU Run value.

## Do Not Redo or Reopen

- Do not perform a physical Host/core/platform project split without Linux implementation or cross-platform build evidence and explicit approval.
- Do not add Linux tray/autostart/packages, select a cross-platform desktop framework, or claim Linux support from this architectural audit.
- Do not put Windows APIs into M2 waveform/protocol/processing/render-state/renderer-contract code merely because the first Host is Windows.
- Do not reopen the existing loopback, provider identity, configuration schema, retry, tray, or HKCU behavior without a concrete compatibility conflict.

## Next Recommended Action

Execute a revised bounded M2 waveform-engine work packet that preserves the OS-agnostic protocol, processing, render-state, renderer-contract, and metrics boundaries established by ADR-0006.
