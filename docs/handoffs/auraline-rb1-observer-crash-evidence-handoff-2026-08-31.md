# Auraline RB1 Observer Crash-Evidence Handoff

Date: 2026-08-31T16:39:32-07:00
Status: diagnosis strengthened; implementation committed locally; publication and corrected-Host acceptance pending
Project: `lgraak/Infopanel.Auraline`
Repository: `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`
Branch: `main`
Implementation HEAD: `f19cd1c48fcfef67632a21e46306043a14ea006d`
Standards revision: `46278c6b5d5f1ea687c16fce473967e402fa3c52`
Executor: Codex desktop
Model: not exposed
Effort: not exposed
Previous handoff: `docs/handoffs/auraline-rb1-skia-renderer-lifetime-handoff-2026-08-31.md`
Containing handoff commit: not self-recordable; use final Executor response and authoritative remote readback.

## Objective and Outcome

Reconcile the completed RB1 renderer-lifetime fix with the subsequently supplied diagnostics archive and current Windows crash evidence, then provide the Observer one self-contained report of crash causality, performance implications, repository state, and remaining gates.

The additional evidence upgrades the diagnosis from a definite unmanaged lifetime defect merely consistent with the crash to a high-confidence causal mechanism: the pre-fix renderer made `SKPath` lifetime finalizer-dependent, and SkiaSharp 3.116.1 passed its raw native handle to `sk_canvas_draw_path` without an internal `GC.KeepAlive`. Because the caller did not use the path after `DrawPath`, an unfortunately timed collection could finalize and release the native path while the native draw was using it. Three independent crashes have the same access-violation code, `coreclr.dll` offset, and native draw-path frame; the callers span both render sessions and the waveform engine.

This does not establish that the separate one-to-two-second display stall has the same cause. The lifetime defect could contribute GC/finalizer pressure, but current diagnostics show fast rendering and a separate high-volume reconnect/exception-log storm that is a stronger independent performance suspect. No performance fix, deployment, package change, release action, InfoPanel mutation, or Resonance Signal mutation is included.

## Governing References

- `.project-standards.toml` and `AGENTS.md`: exact standards adoption and Auraline-specific ownership, scope, validation, publication, runtime, and release boundaries.
- `aeons/project-standards` at `46278c6b5d5f1ea687c16fce473967e402fa3c52`: adopted bootstrap, prompt, and handoff authority.
- `src/Auraline.Host/Waveform/WaveformRenderer.cs` and `src/Auraline.Host/RenderSessions/RenderSessionManager.cs`: renderer native ownership, scheduler callers, diagnostic metrics, and implemented fix.
- `docs/architecture.md` and `tests/README.md`: durable native ownership rule and RB1 stress/soak coverage.
- External diagnostic evidence: `auraline-diagnostics-20260831-163006.zip`, SHA-256 `BCE97DBD496B10F35298D1D7345FAC2EBFA8C51007CE2196F9AEB01C612CB0`, exported 2026-08-31T16:30:10-07:00. The archive is not a repository artifact.
- Windows Application event log providers `.NET Runtime` and `Application Error`: current-machine crash evidence not included in the Auraline diagnostic archive.
- `docs/handoffs/auraline-rb1-skia-renderer-lifetime-handoff-2026-08-31.md`: implemented fix, original diagnosis, validation, and preserved runtime boundary.

## Current Verified State

### Repository and publication

- After a fresh `origin` fetch, local `main` is clean at `f19cd1c48fcfef67632a21e46306043a14ea006d`, one commit ahead of `origin/main` at `0683aaf335236b90227f15575420a7cf747734f2`; divergence is `1 0` before adding this handoff.
- Commit `f19cd1c48fcfef67632a21e46306043a14ea006d` is `Dispose per-frame Skia path` and contains the one-line runtime fix, focused tests, architecture/test documentation, and the previous RB1 handoff.
- A direct push to `origin/main` was attempted but rejected by the managed approval layer because default-branch publication requires fresh explicit approval. No bypass or alternate publication was attempted; the remote remains unchanged.

### Crash evidence

Windows records three Auraline `System.AccessViolationException` failures with exception code `0xc0000005`, faulting module `coreclr.dll`, fault offset `0x00000000001d4660`, and `sk_canvas_draw_path` at the top of the managed/native stack:

| Crash time (local) | Process uptime | Managed caller | Report ID |
| --- | ---: | --- | --- |
| 2026-08-26T17:04:55-07:00 | 0.415 hours | `RenderSessionManager.SessionRuntime.RenderAsync` | `2b7356a4-ebfc-4214-b626-b93e4fb82b23` |
| 2026-08-28T16:52:06-07:00 | 7.359 hours | `WaveformEngineService.RenderFrame` | `fb80170e-622f-4545-b345-90d1a5c3400d` |
| 2026-08-29T08:58:45-07:00 | 11.983 hours | `RenderSessionManager.SessionRuntime.RenderAsync` | `f4bf39a1-bb20-42d6-b65b-428be628cb02` |

The varying uptime and two independent callers are consistent with a timing-dependent native lifetime race in the shared renderer, not a deterministic frame threshold or session-only state defect. A separate 2026-08-27 unhandled `TaskCanceledException` occurred during shutdown in `WaveformEngineService.StopAsync`; it faulted through `KERNELBASE.dll` with `0xe0434352` and is not classified as one of the Skia crashes.

### Native lifetime mechanism

- Before `f19cd1c`, each render created an `SKPath`, passed it to `canvas.DrawPath`, and allowed the managed wrapper to become unreachable without deterministic disposal. All surrounding native-backed Skia objects already had deterministic call-local ownership.
- Reflection over the exact local SkiaSharp 3.116.1 assembly shows `SKCanvas.DrawPath(SKPath, SKPaint)` calls `get_Handle()` three times and then `sk_canvas_draw_path(IntPtr, IntPtr, IntPtr)`. The method contains no `GC.KeepAlive`.
- The implemented `using var path = BuildPath(...)` makes the path a post-draw disposal obligation. This keeps the managed owner live across `DrawPath` and releases the native path immediately after drawing, removing both the use-after-free window and finalizer-dependent accumulation.
- No process dump is available to inspect the exact invalid pointer. The diagnosis is therefore high-confidence causal evidence, not direct dump-level proof of the released handle.

### Current-run diagnostics and performance

- The supplied archive represents the still-running pre-fix packaged Host, not commit `f19cd1c`. At export it had one active 300×300 session targeting 30 FPS, 4,215,288 rendered/published frames, latest render time 1.2221 ms, exponentially weighted average render time 1.0719 ms, and no current meaningful error.
- Its reported 25.468 actual FPS is `rendered_frames / wall-clock seconds since session creation`. It includes workstation sleep, suspension, and other wall-clock gaps; it is not a live cadence percentile and does not by itself prove steady underperformance.
- The archive contains approximately 54.50 MB of retained logs and 18,650 occurrences of connection refusal at `127.0.0.1:48480`. The summary reports 6,800 provider reconnects and zero current waveform reconnects. The retained logs contain no AccessViolation, Skia draw-path stack, `coreclr.dll`, or out-of-memory entry because they cover the current surviving run and native termination can occur before Serilog records it.
- Repeated exception construction, complete stack logging, synchronous file-sink work, two-second disk flush cadence, and 10 MB file rollover are credible performance-stall suspects, but no frame-interval/GC/log timestamp correlation has been captured. They remain a hypothesis, not a diagnosed stall cause.
- At 2026-08-31T16:39:32-07:00, the active pre-fix packaged Host was PID 27548, product version `0.1.0-beta.1+a46e085b218b917fec9c9b1d3122b07ac2f2868c`, running since 2026-08-30T01:31:12Z with approximately 328 MiB working set and 327 MiB private memory. This is one point-in-time snapshot, not a growth curve.

## Work Completed

- Preserved the RB1 implementation at `f19cd1c`: deterministic per-frame `SKPath` ownership with no lock, visual change, session change, transport change, dependency change, or schema change.
- Preserved focused coverage for path disposal, eight-worker concurrent rendering and PNG encoding, all renderer visual states, 30/60 FPS inputs, profile changes, repeated four-session hot apply, and teardown.
- Inspected the supplied diagnostics archive read-only, verified its SHA-256, parsed its current session metrics, and scanned all retained logs for crash, out-of-memory, and reconnect signatures.
- Correlated `.NET Runtime` event 1026 with `Application Error` event 1000 for all three access violations and decoded each process start time to establish uptime.
- Inspected the exact SkiaSharp 3.116.1 managed wrapper IL to establish raw-handle forwarding and absence of `GC.KeepAlive`.
- Separated three states for Observer review: high-confidence crash diagnosis and fix, unresolved installed corrected-Host acceptance, and unresolved performance-stall causality.

## Decisions and Constraints

- Treat the pre-fix path lifetime as the leading crash root cause. The specific mechanism is a timing-dependent native handle lifetime/use-after-free window, with finalizer pressure as a secondary effect; it is no longer described only as a generic unmanaged leak.
- Retain the smallest fix. Deterministic call-local ownership removes the demonstrated lifetime gap without a global renderer lock or architecture change.
- Do not claim the performance stall is fixed. Path ownership may reduce GC/finalizer churn, but the approximately 1.07 ms average render duration argues against waveform drawing cost exhausting the 33.3 ms 30 FPS budget.
- Treat the reconnect/exception-log storm as a separate performance hypothesis requiring timestamp correlation. Do not change retry, logging, or Resonance behavior inside RB1.
- Keep the running packaged Host untouched. It does not contain `f19cd1c`, so its continued survival or later failure cannot accept or reject the corrected implementation.
- Keep the previous direct-push rejection as an authorization boundary. Repository state must not be published through an alternate branch, force push, other credentials, or a workaround.

## Validation and Evidence

- Pre-fix lifetime regression: `RendererLifetimeTests.PerFrameSkiaPathIsDisposedByTheRenderInvocation` failed before the fix because the path lacked invocation-scoped disposal; it passed afterward.
- Focused Debug RB1 coverage: four renderer/session lifetime and concurrency tests passed.
- Release bounded soak: four concurrent workers completed 60 seconds across active, idle, reconnecting, and unavailable states, alternating 30/60 FPS metadata, changing settings, and periodic PNG encoding without crash.
- Required solution validation: restore passed in the approved host context; Debug and Release builds passed; full Debug suite passed 83/83 Host plus 34/34 plugin tests, 117/117 total; format verification passed.
- Security and diff validation: Gitleaks scanned approximately 2.09 MB with no leaks; `git diff --check` passed; final implementation diff was reviewed.
- Diagnostic archive: 560,137-byte ZIP verified at SHA-256 `BCE97DBD496B10F35298D1D7345FAC2EBFA8C51007CE2196F9AEB01C612CB0`; all retained logs were scanned read-only.
- Windows crash correlation: three event 1026/1000 pairs agree on `0xc0000005`, `coreclr.dll`, fault offset `0x00000000001d4660`, and `sk_canvas_draw_path`; callers and decoded uptimes are recorded above.
- SkiaSharp wrapper inspection: exact 3.116.1 IL calls three `get_Handle()` methods followed by `sk_canvas_draw_path(IntPtr, IntPtr, IntPtr)` and contains no `GC.KeepAlive`.
- WER follow-up: no matching Windows Error Reporting event 1001, queued report, archived report, or dump was found for Auraline.
- Not run: corrected packaged/installed Host, multi-hour corrected Host soak, crash-dump capture, frame-interval or GC-pause trace, log/reconnect timing correlation, InfoPanel acceptance, TransportProbe, package build, release mutation, or external deployment.

## Unresolved Items

- The exact invalid native pointer is not dump-proven. The repeated event stacks, identical offset, caller diversity, source lifetime gap, and wrapper IL provide high-confidence causality but cannot substitute for pointer-level dump inspection.
- Commit `f19cd1c` is local only. It is not on `origin/main`, packaged, deployed, or active in the running Host.
- Corrected-Host multi-hour runtime acceptance remains outstanding.
- The one-to-two-second display stall remains separately unresolved. No frame-interval, GC, CPU scheduling, disk I/O, reconnect, or log-flush correlation exists yet.
- The current reconnect/log volume is operationally noisy and a plausible stall contributor, but RB1 does not establish whether it blocks rendering or display publication.
- The separate shutdown `TaskCanceledException` event remains outside RB1 and should not be conflated with the Skia access violations.
- Public beta still depends on the separately documented compatible distributable InfoPanel build gate.

## Files Changed

- `docs/handoffs/auraline-rb1-observer-crash-evidence-handoff-2026-08-31.md`: this self-contained Observer report. No product, test, package, configuration, or runtime file changed during this evidence-reconciliation step.

## Publication and Runtime State

- Implementation: complete in local commit `f19cd1c48fcfef67632a21e46306043a14ea006d`.
- Observer evidence handoff: added after `f19cd1c`; its containing commit is reported externally after any authorized commit.
- Remote publication: not complete. Fresh `origin/main` remains `0683aaf335236b90227f15575420a7cf747734f2`; local `main` was one commit ahead before this handoff.
- Deployment/activation: none. The active packaged beta remains the pre-fix `a46e085b...` build.
- Runtime acceptance: corrected in-process tests and bounded soak passed; corrected packaged/installed Host acceptance has not run.
- Release/package: unchanged. No version, ZIP, checksum manifest, tag, Draft Release, installer, or public release changed.

## Safety, Rollback, and Access

- The diagnostics ZIP, Windows event logs, WER locations, SkiaSharp assembly, and active process were inspected read-only. No dump, extraction directory, or temporary repository artifact was created.
- No active Host, InfoPanel process, installed file, user configuration, startup registration, provider, Resonance Signal process, or release state was modified.
- Roll back the implementation only through a normal later Git revert if Observer review rejects it; do not rewrite history. The new handoff is evidence-only and can be superseded by a later checkpoint without altering historical evidence.

## Do Not Redo

- Do not repeat the renderer ownership inventory unless source or SkiaSharp version changes; the only pre-fix native ownership gap and exact wrapper behavior are recorded here.
- Do not describe the three access violations as ordinary provider failures; provider refusal is a separate managed/retry condition.
- Do not treat the current surviving pre-fix process or 4.2 million rendered frames as disproof of a nondeterministic lifetime race.
- Do not use 25.468 lifetime-average FPS as a live cadence measurement.
- Do not claim RB1 fixes the display stall or expand RB1 into retry/logging/performance changes.
- Do not publish through an alternate route to bypass the direct-main approval requirement.

## Milestone Learning Candidates

### Native wrappers need caller-visible lifetime through P/Invoke

- Evidence: the only undisposed per-frame object was `SKPath`; SkiaSharp 3.116.1 extracts its handle and performs the native draw without `GC.KeepAlive`; three failures occur in that native call across independent callers.
- Lesson: deterministic disposal after the native call is also a managed-rooting guarantee across the call, not merely leak prevention.
- Project relevance: every future Skia-backed renderer should keep native owners live through P/Invoke and dispose them at the allocation site.

### Host diagnostics do not replace operating-system crash evidence

- Evidence: the current-run export contains no access-violation signature, while Windows Application events preserve three identical native failures from prior runs.
- Lesson: abrupt native termination can preclude application logging; long-run acceptance needs OS event correlation and configured dump capture.
- Project relevance: future reliability packets should collect both bounded Host diagnostics and OS crash artifacts.

### Lifetime FPS and reconnect logs are insufficient stall telemetry

- Evidence: the export shows approximately 1.07 ms average render time, a 25.468 lifetime-average FPS, 18,650 refusal entries, and 54.50 MB of logs, but no per-frame interval, GC-pause, or log-flush timestamps tied to a reported stall.
- Lesson: diagnosing intermittent cadence loss requires correlated interval and pause telemetry rather than aggregate FPS or log volume alone.
- Project relevance: any later stall milestone should add bounded correlation before changing the renderer, scheduler, retry policy, or logging.

## Next Recommended Action

Observer should explicitly authorize a normal fast-forward publication of local RB1 commit `f19cd1c48fcfef67632a21e46306043a14ea006d` plus this evidence-handoff commit to `origin/main`; that authorization does not include deployment, packaging, release publication, or the subsequent corrected-Host soak.
