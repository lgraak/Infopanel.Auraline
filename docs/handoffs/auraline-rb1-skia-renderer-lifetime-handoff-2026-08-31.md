# Auraline RB1 Skia Renderer-Lifetime Handoff

Date: 2026-08-31T16:24:44-07:00
Status: implemented and validated; installed-Host soak pending
Project: `lgraak/Infopanel.Auraline`
Repository: `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`
Branch: `main`
Implementation HEAD: `0683aaf335236b90227f15575420a7cf747734f2` (starting revision; the RB1 changes and this handoff share the containing commit)
Standards revision: `46278c6b5d5f1ea687c16fce473967e402fa3c52`
Executor: Codex desktop
Model: not exposed
Effort: not exposed
Previous handoff: `docs/handoffs/auraline-standards-reconciliation-current-state-handoff-2026-08-31.md`
Containing handoff commit: not self-recordable; use final Executor response and authoritative remote readback.

## Objective and Outcome

RB1 isolated a definite native-lifetime defect in the long-running SkiaSharp render path and applied the smallest safe correction. Every rendered frame created an `SKPath` but did not dispose it, leaving native path resources dependent on finalization while the shared renderer served waveform processing, render sessions, previews, and diagnostics. The path is now deterministically disposed by its render invocation. Focused concurrency, state, session, hot-apply, teardown, and bounded soak validation passed without changing pixels, session behavior, transport, plugin behavior, or profile schema.

The historical `System.AccessViolationException` was not reproduced during this bounded milestone, so the unmanaged `SKPath` leak is a demonstrated defect strongly consistent with the reported long-run `sk_canvas_draw_path` failure, not artifact-level proof of the historical crash's exclusive cause. The separate one-to-two-second display stall was not investigated and is not inferred to share this cause.

## Governing References

- `.project-standards.toml` and `AGENTS.md`: exact standards adoption, project boundaries, validation, and publication authority.
- `aeons/project-standards` at `46278c6b5d5f1ea687c16fce473967e402fa3c52`: adopted bootstrap, prompt, and handoff authority.
- User-supplied `Auraline RB1: Long-Run SkiaSharp Crash Reproduction and Renderer-Lifetime Diagnosis`: milestone scope, exclusions, stop conditions, validation, and authorization.
- `docs/architecture.md`, `README.md`, and `tests/README.md`: durable renderer/session architecture and validation guidance.
- `docs/handoffs/auraline-standards-reconciliation-current-state-handoff-2026-08-31.md`: prior verified state and reliability-blocker context.

## Current Verified State

- Fresh preflight fetch found local `main` clean and synchronized with `origin/main` at `0683aaf335236b90227f15575420a7cf747734f2`, divergence `0 0`, before RB1 changes.
- `WaveformRenderer` is registered as a singleton and can be called concurrently by the waveform engine, independent render-session schedulers, working-copy preview, and diagnostics self-test paths.
- The renderer retains only managed/value-type configuration. Per-call native-backed objects are `SKSurface`, surface-owned `SKCanvas`, two `SKPaint` instances, `SKPath`, `SKImage`, `SKBitmap`, and, for PNG encoding, `SKImage` and `SKData`. All except `SKPath` were already invocation-local and deterministically disposed; `SKImageInfo`, `SKColor`, and `SKRect` are value types.
- Session profile hot apply replaces immutable managed settings. Session teardown cancels and awaits the scheduler before disposing its transport. No Skia object crosses a render call, session, or thread boundary.
- A packaged `0.1.0-beta.1` Host was already active as PID 27548 from the previously extracted beta package, product version `0.1.0-beta.1+a46e085b218b917fec9c9b1d3122b07ac2f2868c`, started `2026-08-30T01:31:12.0935252Z`. It was inspected read-only and left running.

## Work Completed

- Added deterministic disposal to the per-frame `SKPath` in `WaveformRenderer.Render`; no lock, renderer split, dependency change, or visual-path rewrite was introduced.
- Added a source-level regression guard that failed against the pre-fix renderer and passes only when `BuildPath` ownership is scoped with `using`.
- Added shared-renderer concurrency coverage using eight workers, 2,000 render calls, all four visual states, variable dimensions, 30/60 FPS metadata, fixed/automatic scale inputs, smoothing changes, and concurrent PNG encoding.
- Added a configurable high-rate renderer soak with four workers and a 1-300 second bound; normal tests use one second and RB1 explicitly exercised 60 seconds in Release.
- Added three repeated session-manager cycles with four simultaneous dimensioned sessions, both 30 and 60 FPS, profile revisions/hot apply, hundreds of publications, and verified transport teardown.
- Made the test-only mutable profile catalog use volatile reads/writes so the new scheduler stress does not introduce a test data race.
- Documented the confirmed invocation-local native ownership rule and RB1 stress coverage.

## Decisions and Constraints

- The correction is deliberately limited to the one proven unmanaged lifetime gap. Existing Skia text API obsolescence warnings remain out of scope.
- No global renderer lock was added: the renderer has no retained native state, all Skia objects are now call-local, and concurrent coverage passes.
- The active packaged Host was not stopped, replaced, or treated as evidence for the source fix. A second checkout Host would only signal the existing single instance, so installed/live Host soak was not practical without an unauthorized runtime interruption.
- No crash dump exists because neither the focused tests nor the bounded soak crashed.
- The reported display stall remains an independent issue. RB1 contains no timing, batching, transport, or visual-behavior changes intended to address it.

## Validation and Evidence

- Pre-fix regression evidence: `RendererLifetimeTests.PerFrameSkiaPathIsDisposedByTheRenderInvocation` failed because `using var path = BuildPath(...)` was absent; the same test passed after the one-line fix.
- Focused Debug coverage: four RB1 tests passed, including shared renderer concurrency, the configurable soak, path ownership, and repeated multi-session hot apply/teardown.
- Release bounded soak: `AURALINE_RENDERER_SOAK_SECONDS=60` with `RendererLifetimeTests.SharedRendererCompletesBoundedHighRateSoak` passed in 1.01 minutes with four concurrent workers, alternating 30/60 FPS inputs, all visual states, changing profile settings, and periodic PNG encoding.
- `dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config`: passed in the approved host context. The first sandboxed attempt failed only because the per-user NuGet configuration was unreadable there.
- Debug build: passed with 0 warnings and 0 errors.
- Release build: passed with 0 warnings and 0 errors using current incremental outputs; the three pre-existing Skia text API warnings remain unchanged and appeared when the renderer project was recompiled during focused test runs.
- Full Debug test suite: passed 83/83 Host tests and 34/34 plugin tests, 117/117 total.
- `dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore`: passed.
- `gitleaks dir . --no-banner --redact`: scanned approximately 2.08 MB; no leaks found.
- `git diff --check`: passed. Final diff was reviewed for scope, ownership, visual behavior, transport/session contracts, and unintended artifacts.
- Not run: installed corrected Host soak, crash-dump capture, InfoPanel acceptance, TransportProbe, package build, release mutation, Resonance Signal mutation, or display-stall investigation.

## Unresolved Items

- Historical-crash causality is not proven by a captured failing process or dump. RB1 proves and fixes the unmanaged `SKPath` lifetime defect that matches the reported long-run native draw-path failure, and the bounded corrected soak does not crash.
- The corrected source has not yet been exercised as the installed single Host instance because the existing packaged beta process was preserved.
- The separate intermittent one-to-two-second display stall remains uninvestigated.
- Public beta still depends on the separately documented compatible distributable InfoPanel build gate.

## Files Changed

- `src/Auraline.Host/Waveform/WaveformRenderer.cs`: deterministically disposes each per-frame `SKPath`.
- `tests/Auraline.Host.Tests/RendererLifetimeTests.cs`: path-lifetime guard, concurrent renderer/preview stress, and configurable bounded soak.
- `tests/Auraline.Host.Tests/RenderSessionManagerTests.cs`: repeated 30/60 FPS multi-session hot-apply/teardown stress and thread-safe test catalog.
- `docs/architecture.md`: confirmed stateless shared-renderer and invocation-local Skia ownership rule.
- `tests/README.md`: RB1 coverage and soak-duration control.
- `docs/handoffs/auraline-rb1-skia-renderer-lifetime-handoff-2026-08-31.md`: this checkpoint.

## Publication and Runtime State

- Working tree implementation: RB1 fix, tests, architecture note, test guidance, and handoff only.
- Local commit: this handoff and scoped changes share the containing commit; obtain its exact SHA from the final Executor response.
- Remote publication: authorized normal fast-forward publication is recorded by final authoritative remote readback, not self-claimed here.
- Deployment or activation: none performed. The running packaged beta remains the prior `a46e085b...` build and does not contain RB1.
- Runtime acceptance: automated in-process corrected renderer/session stress passed; installed corrected Host/InfoPanel acceptance remains pending.
- Release/package state: unchanged; no package, version, tag, Draft Release, or installer was created or modified.

## Safety, Rollback, and Access

- No InfoPanel, Resonance Signal, installed files, user configuration, provider settings, startup registration, or active Host process was modified.
- The code rollback is a normal later Git revert of the RB1 commit if review finds a regression; do not rewrite shared history.
- Before any future installed Host/plugin activation, use the supported tray Exit path and verify the relevant process is stopped. Do not overwrite active or locked plugin files.

## Do Not Redo

- Do not reintroduce finalizer-dependent ownership for `SKPath` or another per-frame native Skia object.
- Do not serialize all rendering with a global lock without new evidence of shared native state.
- Do not treat the bounded test soak as installed multi-hour Host acceptance or as a captured reproduction of the historical crash.
- Do not combine the separate display stall with this lifetime defect without direct evidence.
- Do not rebuild or publish `0.1.0-beta.1` as part of RB1.

## Milestone Learning Candidates

### Make native render ownership explicit at the allocation site

- Evidence: one per-frame `SKPath` lacked deterministic disposal while every surrounding native-backed Skia object already had call-local ownership; the historical crash occurred in the native path draw call after long runtime.
- Lesson: retained stateless renderers can safely serve concurrent callers when every native-backed object is invocation-local and deterministically disposed; relying on finalization turns sustained frame rate into unmanaged lifetime pressure.
- Project relevance: future renderer implementations and visualization blocks should preserve this allocation-site ownership rule and include a bounded concurrent soak.

## Next Recommended Action

At the next Observer-approved maintenance window, run the corrected Host as the sole installed instance under crash-dump capture with multiple 30/60 FPS sessions for a multi-hour soak, while keeping display-stall investigation out of that acceptance run.
