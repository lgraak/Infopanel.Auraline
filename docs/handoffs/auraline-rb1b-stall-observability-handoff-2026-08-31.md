# Auraline RB1B Stall Observability Handoff

Date: 2026-08-31T19:08:40-07:00
Status: implementation and validation complete; publication pending
Project: `lgraak/Infopanel.Auraline`
Repository: `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`
Branch: `main`
Implementation HEAD: starting revision `8a7baad9243212cb5848d29f41798e9edef88005`; implementation and handoff share the later containing commit
Standards revision: `46278c6b5d5f1ea687c16fce473967e402fa3c52`
Executor: Codex desktop
Model: not exposed
Effort: not exposed
Previous handoff: `docs/handoffs/auraline-rb1-observer-crash-evidence-handoff-2026-08-31.md`
Containing handoff commit: not self-recordable; use final Executor response and authoritative remote readback.

## Objective and Outcome

Add bounded current-run evidence to distinguish render-session scheduler lateness, rendering, transport publication, GC/runtime pauses, waveform lifecycle, and provider reconnect activity during the next visible stall. The implementation is observational only: it does not change deadlines, missed-frame policy, render/publication order, provider or waveform retry, logging, transport, profiles, or InfoPanel behavior.

## Governing References

- `.project-standards.toml` and `AGENTS.md`
- `docs/architecture.md`
- `docs/handoffs/auraline-rb1-observer-crash-evidence-handoff-2026-08-31.md`
- Adopted Project Bootstrap, Prompt, and Handoff Standards v1 at `46278c6b5d5f1ea687c16fce473967e402fa3c52`
- Read-only RB1B capture returned by the previous Executor response

## Current Verified State

- Repository preflight was clean `main` at `8a7baad9243212cb5848d29f41798e9edef88005`, equal to `origin/main`.
- The corrected Host soak remained running as PID 27684 from the existing RB1 package path throughout implementation and validation; it was not stopped, replaced, attached, or otherwise disturbed.
- The implementation adds no dependency, profiler, EventPipe collector, persistent telemetry, or per-frame log.
- Final publication evidence is recorded in the final Executor response because this handoff cannot self-record its containing commit.

## Work Completed

- Added process-wide bounded stall observability with an injectable monotonic clock, supported in-process .NET GC metrics, and a 32-entry significant-event ring.
- Added per-session target interval, scheduled deadline, actual render start, current/max scheduler lateness, current/max publication interval and sequence, strict 50/100/250/500 ms counters, renderer duration, transport-publication duration, and render-to-publish duration.
- Added concise waveform open/start/stop/reconnect events with stream ID and stop reason, plus provider reconnect timing/reason evidence.
- Added a readable `Timing / Stall Observability` diagnostics section and included the bounded snapshot in the loopback API, Markdown summary, and diagnostics ZIP.
- Added deterministic focused tests and concise architecture/test documentation.

## Decisions and Constraints

- Monotonic time is used for all interval and lateness calculations; wall clock only labels significant events for user correlation.
- The existing scheduler remains authoritative. Instrumentation calculates the monotonic equivalent of its already-selected delay but never feeds a timing result back into scheduling.
- Threshold counters are allocation-free value types. Only events over 50 ms and lifecycle/reconnect events allocate bounded ring entries; normal frames create no event records.
- GC evidence uses `GC.CollectionCount`, `GC.GetTotalMemory`, `GC.GetGCMemoryInfo`, and `GC.GetTotalPauseDuration`. Unsupported pause evidence is nullable rather than inferred.
- The event ring contains no waveform/audio samples, pixels, secrets, or unbounded history and is not automatically persisted.

## Validation and Evidence

Validation completed against the final scoped implementation:

- Focused timing, diagnostics, and render-session tests: 22/22 passed.
- Full Debug suite: 88/88 Host and 34/34 plugin tests passed, 122/122 total.
- Restore with repository `NuGet.Config`: passed through the approved canonical host context after the sandbox could not read the existing user NuGet configuration.
- Debug build: passed with zero warnings and errors after restore.
- Release build: passed; three pre-existing SkiaSharp obsolete-API warnings remained in `WaveformRenderer` and no new warning was introduced.
- `dotnet format --verify-no-changes`: passed.
- Gitleaks 8.30.1 scanned approximately 2.14 MB with no leaks.
- `git diff --check`: passed; the final diff and handoff structure were reviewed.

The exact commit and authoritative remote readback are reported in the final Executor response after publication.

No instrumented runtime acceptance was performed because deployment and external mutation were explicitly prohibited.

## Unresolved Items

- The new evidence has not yet observed or classified a real visible stall.
- GC APIs expose process-wide aggregate/current-run evidence; they do not identify which managed thread was paused.
- The running corrected-Host crash soak remains the uninstrumented `8a7baad` build until a later explicitly authorized activation milestone.

## Files Changed

- `src/Auraline.Host/Diagnostics/StallObservability.cs`
- `src/Auraline.Host/RenderSessions/RenderSessionManager.cs`
- `src/Auraline.Host/Waveform/WaveformEngineService.cs`
- `src/Auraline.Host/Providers/ProviderManager.cs`
- `src/Auraline.Host/Providers/ProviderModels.cs`
- `src/Auraline.Host/Diagnostics/DiagnosticsService.cs`
- `src/Auraline.Host/Web/UiRenderer.cs`
- `src/Auraline.Host/Program.cs`
- `tests/Auraline.Host.Tests/StallObservabilityTests.cs`
- `tests/Auraline.Host.Tests/DiagnosticsTests.cs`
- `docs/architecture.md`
- `tests/README.md`
- `docs/handoffs/auraline-rb1b-stall-observability-handoff-2026-08-31.md`

## Publication and Runtime State

- Worktree implementation: complete, pending final validation at handoff creation time.
- Commit/publication: authorized; exact local and `origin/main` SHA are supplied after publication.
- Deployment/activation: none authorized or performed.
- Runtime acceptance: deferred; the active corrected Host remains untouched.
- Release/tag/Draft Release: unchanged.

## Safety, Rollback, and Access

Rollback is an ordinary later Git revert if review rejects the instrumentation. No runtime rollback is needed because the instrumented build was not activated. The current Host, WER configuration, InfoPanel, Resonance Signal, user configuration, logs, retry policy, and release state were not modified.

## Do Not Redo

- Do not attach a profiler or add EventPipe merely to duplicate the supported in-process GC evidence.
- Do not treat current/max diagnostic counters as a fix or as stall causality until a visible event is timestamped and correlated.
- Do not deploy this commit while the current RB1 crash soak is still awaiting its separately authorized completion/readback.

## Milestone Learning Candidates

### Scheduler observability can remain control-flow neutral

- Evidence: monotonic deadlines and start/publication timestamps are derived beside the existing wall-clock scheduler without feeding measurements back into deadline selection.
- Lesson: scheduler lateness and publication gaps can be diagnosed without changing missed-frame behavior.
- Project relevance: future performance diagnosis can begin with bounded evidence rather than speculative cadence changes.

## Next Recommended Action

After the current RB1 crash soak completes and is read back, activate an instrumented Host build and reproduce the visible stall with the new bounded timing evidence before proposing any performance fix.
