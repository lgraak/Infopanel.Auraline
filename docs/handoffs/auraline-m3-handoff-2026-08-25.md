# Auraline M3 Render Sessions and Windows Local Frame Transport Handoff

Date: 2026-08-25T10:34:06.324-07:00
Status: completed and published
Model: GPT-5 Codex
Effort: High
Repository: InfoPanel.Auraline (`D:\Aeons\Git\Infopanel.Auraline`)
Branch: `main`
HEAD: `4a4ba9673573f15efd4afcccb272b0415637d24e` (published M3 implementation checkpoint)
Authoritative remote: `origin` at `https://github.com/lgraak/Infopanel.Auraline.git`

> This handoff is a continuation checkpoint, not authoritative truth. Current
> repository, remote, runtime, and test evidence wins if it conflicts with this
> document.

## Objective

Implement and validate M3 render-session lifecycle plus the first local frame
transport. The objective was fully achieved: compatible consumers share lazy
Host-owned render sessions, Windows shared memory publishes complete dynamic-size
frames across a real process boundary, leases/grace/cap cleanup are bounded, and
the versioned loopback control API and diagnostics are usable by the future M4
adapter. Functional InfoPanel integration, Linux/network transport, and full
profile management remain excluded.

## Authoritative Sources

- `README.md`, `docs/architecture.md`, and `docs/roadmap.md`: durable product,
  boundary, and milestone authority.
- `docs/infopanel-platform-integration.md`: durable M4 consumer-boundary audit.
- `docs/decisions/0005-shared-memory-frame-transport.md`,
  `docs/decisions/0006-windows-first-cross-platform-boundaries.md`, and
  `docs/decisions/0007-auraline-frame-transport-abstraction.md`: durable transport
  and portability decisions.
- `docs/handoffs/auraline-m2-live-acceptance-handoff-2026-08-25.md` and
  `docs/handoffs/auraline-infopanel-platform-audit-handoff-2026-08-25.md`:
  inherited checkpoints, reverified against current repository state.
- `docs/standards/ai-project-prompt-standard-v1.md` and
  `docs/standards/ai-project-handoff-standard-v1.md`: execution and handoff rules.
- Fresh repository, test, `origin`, localhost API, and cross-process probe evidence
  recorded below is time-sensitive and outranks inherited handoff claims.

## Execution Context

- Windows and PowerShell in the managed Codex workspace; repository root
  `D:\Aeons\Git\Infopanel.Auraline`.
- Actual execution model: GPT-5 Codex; reasoning effort: High.
- No repository-local `AGENTS.md` was present; the supplied Chris Codex working
  instructions governed.
- The initial fetch and .NET restore required narrowly scoped elevation because
  the managed filesystem blocks `.git/FETCH_HEAD` and the user NuGet configuration.
- Runtime acceptance used the Debug Host and separate Debug probe executables.
  Both temporary Host processes were stopped; `127.0.0.1:48481` was verified not
  responding afterward.

## Current Repository State

- Preflight branch and HEAD: `main` at
  `61deaea4d009bd70f2d1a18f3c044940fc05039f`.
- Working tree before M3: clean.
- Upstream: `origin/main`; authoritative fetch completed successfully and fresh
  divergence was `0 0` at the preflight SHA.
- Commit: `4a4ba9673573f15efd4afcccb272b0415637d24e`
  (`Build Auraline render-session transport`).
- Authoritative remote readback: `git fetch`, `origin/main`, and
  `git ls-remote origin refs/heads/main` all returned the same full SHA;
  divergence was `0 0` and the post-push working tree was clean.
- Preserved unrelated changes: none were present; the working tree contains only
  intended M3 implementation, tests, documentation, and this handoff.

## Current Known-Good State

- Published M3 implementation commit `4a4ba9673573f15efd4afcccb272b0415637d24e`
  passed restore, Debug/Release builds, 57 tests, format
  verification, Gitleaks, Markdown-link validation, portability checks, and
  `git diff --check` on 2026-08-25.
- Resonance Signal returned protocol v1 ready on `127.0.0.1:48480`; Auraline Host
  reported provider `Connected`, source count 1, a live 48 kHz two-channel stream,
  and later the expected `Idle` state after active playback stopped.
- Cross-process probes observed complete, changing frames at 320x120, 480x180,
  and 640x240, including shared consumers and 30/60 FPS operation.

## Completed Work

- Added OS-neutral render-session keys/descriptors/states, transport descriptors,
  leases, frame publication/read results, and publisher/reader/factory interfaces.
- Added stable temporary profile ID `default-profile`; full M5 profile management
  can replace its source without changing session identity semantics.
- Added a Host-owned manager with lazy session creation, compatible sharing,
  distinct-dimension/cadence sessions, one scheduler per session, 25-second
  renewable leases, explicit detach, stale expiry, and 15-second teardown grace.
- Added a default configurable cap of 32. Capacity eviction stops the deterministic
  LRU zero-consumer session and releases its transport before creating replacement
  work; validly leased sessions are never evicted.
- Added 30 FPS default and 60 FPS support. Missed deadlines reset from current time
  and never queue historical renders.
- Reused M2 renderer and latest processed waveform/visual state at exact negotiated
  dimensions; no second waveform implementation or M2 pixel-semantic change exists.
- Added one opaque `Local\\Auraline.Frame.<guid>` mapping per session under
  `Platform/Windows`; no global mapping exists.
- Implemented layout v1: 128-byte `AURL` header, two fixed RGBA8888-premultiplied
  slots, geometry/bounds/FPS/sequence/UTC metadata, active slot, and aligned
  publication version. Writer odd/even seqlock publication plus reader before/after
  validation prevents accepting a concurrent partial copy.
- Added loopback `/api/v1/render-sessions` attach, heartbeat, detach, collection,
  and per-session diagnostic routes with clear 400/404/409/426 failures.
- Added Host health/UI diagnostics for sessions, leases, dimensions, target/actual
  FPS, sequences, render-plus-publication timing, allocation, grace, and global
  lifecycle counters.
- Added `Auraline.TransportProbe`, a real external process that negotiates via HTTP,
  opens the returned mapping, validates layout/content/sequence, heartbeats, and
  detaches or intentionally exits abruptly.
- Updated architecture, roadmap, ADR implementation notes, project READMEs, and
  testing instructions. M4 remains unimplemented.

## Decisions Made

- A compatible runtime lookup key is profile ID + width + height + target FPS.
  Profile and dimensions remain the semantic minimum; cadence is a compatibility
  property so a 60 FPS consumer does not silently inherit an existing 30 FPS loop.
- Lease timeout is 25 seconds and normal probe heartbeat cadence is 8 seconds,
  avoiding a tight liveness requirement while bounding crashed consumers.
- The grace timer starts when the last valid lease is removed or detected expired;
  rendering and mapping ownership continue during grace so compatible reattach is
  allocation- and session-stable.
- Double slots plus one global publication seqlock were selected over a process-wide
  lock. Consumers read latest-only and retry a concurrent write; no queue exists.
- Pixel layout remains M2 `rgba8888-premul`: byte order R, G, B, A, premultiplied
  alpha, stride `width * 4`. Only header metadata and rendered pixels are mapped.
- `MemoryMappedFile`, resource naming, and pointer/atomic mechanics remain under
  Windows platform ownership. Shared contracts and the session domain contain no
  Windows, registry, or InfoPanel types.
- Linux transport, LAN/network transport, InfoPanel plugin types, profile editing,
  stereo/multi-source rendering, and advanced visuals remain deferred.

## Files Changed

- `InfoPanel.Auraline.sln`: includes the external transport probe project.
- `README.md`: M3 behavior, control API, probe use, and current limitations.
- `docs/architecture.md`: implemented session, lease, scheduler, and layout detail.
- `docs/roadmap.md`: marks M3 complete without marking M4 complete.
- `docs/decisions/0005-shared-memory-frame-transport.md`: concrete Windows layout
  and cleanup consequences.
- `docs/decisions/0007-auraline-frame-transport-abstraction.md`: concrete contract
  and Windows-adapter ownership.
- `docs/handoffs/auraline-m3-handoff-2026-08-25.md`: this checkpoint.
- `src/Auraline.Contracts/README.md`: M3 shared-contract scope.
- `src/Auraline.Contracts/RenderSessionContracts.cs`: session, lease, frame, and
  transport contracts.
- `src/Auraline.Host/Auraline.Host.csproj`: M3 version and unsafe blocks required
  for aligned cross-process volatile publication.
- `src/Auraline.Host/Program.cs`: composition and versioned API registration.
- `src/Auraline.Host/README.md`: Host session/transport ownership and layout.
- `src/Auraline.Host/Platform/Windows/WindowsSharedMemoryFrameTransport.cs`:
  Windows publisher, reader, factory, and layout.
- `src/Auraline.Host/RenderSessions/RenderSessionApi.cs`: v1 HTTP contracts/routes.
- `src/Auraline.Host/RenderSessions/RenderSessionManager.cs`: lifecycle, leases,
  cap/eviction, scheduler, metrics, and cleanup.
- `src/Auraline.Host/Waveform/WaveformContracts.cs` and
  `src/Auraline.Host/Waveform/WaveformEngineService.cs`: latest M2 render-state
  snapshot boundary consumed by M3.
- `src/Auraline.Host/Web/HealthContract.cs` and
  `src/Auraline.Host/Web/UiRenderer.cs`: machine/human session diagnostics.
- `tests/Auraline.Host.Tests/ContractAndHealthTests.cs`: reconciled health shape.
- `tests/Auraline.Host.Tests/RenderSessionApiTests.cs`: HTTP success/error paths.
- `tests/Auraline.Host.Tests/RenderSessionManagerTests.cs`: deterministic lifecycle,
  sharing, stale lease, grace, LRU/cap, scheduler, and shutdown coverage.
- `tests/Auraline.Host.Tests/WaveformPortabilityTests.cs`: contract/domain Windows
  API leakage checks.
- `tests/Auraline.Host.Tests/WindowsSharedMemoryFrameTransportTests.cs`: layout,
  bounds, sequence, multi-reader, concurrency, compatibility, and cleanup coverage.
- `tests/Auraline.TransportProbe/Auraline.TransportProbe.csproj` and
  `tests/Auraline.TransportProbe/Program.cs`: separate-process consumer.
- `tests/README.md`: M3 coverage and probe instructions.
- Generated `bin/` and `obj/` artifacts were validation outputs and remain ignored.

## Validation Completed

- `git fetch origin --prune`: passed; fresh preflight HEAD/origin divergence `0 0`.
- `dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config`: passed after
  narrowly scoped elevation; all five projects restored/current.
- `dotnet build InfoPanel.Auraline.sln --configuration Debug --no-restore`: passed,
  0 errors. Three existing SkiaSharp obsolete-text API warnings can appear on a
  non-incremental build; M3 did not introduce or modify those M2 calls.
- `dotnet build InfoPanel.Auraline.sln --configuration Release --no-restore`: passed,
  0 errors with the same three existing M2 warnings.
- `dotnet test InfoPanel.Auraline.sln --configuration Debug --no-build --no-restore`:
  passed, 57/57 tests.
- `dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore`: passed.
- Portability source checks: passed as part of the 57-test suite; Windows-only types
  are absent from waveform, shared contracts, and render-session domain sources.
- `gitleaks dir . --no-banner --redact`: passed; about 1.09 MB scanned, no leaks.
- Repository-relative Markdown link checker: passed across all 25 Markdown files,
  including this handoff.
- `git diff --check` and staged `git diff --cached --check`: passed.
- Static transport inspection found no waveform/audio sample fields or persistence
  in shared contracts, session manager, or Windows mapping implementation; the
  mapping contains only rendered-frame metadata and pixels.
- Live 30 FPS: `320x120` sequence 1 to 121 over four seconds, changing pixels,
  29.88 FPS observed; later idle rendering sequence 1 to 61, changing pixels,
  29.85 FPS while Host health reported `Idle`.
- Shared session: two overlapping `320x120@30` probes received session
  `89060dcf9cd84fa48dde5a03bce64816`, independent leases, changing pixels, and
  about 29.77/29.96 FPS; unit evidence also confirms one transport/scheduler.
- Dynamic dimensions: `640x240@30` received distinct session
  `a73a9adef9c44bed85e08bbb1a834a65`, sequence 1 to 120, changing pixels,
  29.65 FPS.
- 60 FPS sanity: `320x120@60` received distinct session
  `d65828da42234dd6abdb12fa93a13fb3`, sequence 1 to 240, changing pixels,
  59.70 FPS.
- Performance snapshot: `640x240@30` reported about 30.11 actual FPS and 0.814 ms
  average render-plus-publication with 1,228,928-byte allocation;
  `320x120@60` reported about 60.20 actual FPS and 0.517 ms average with
  307,328-byte allocation. This is a sanity observation, not a benchmark.
- Grace reuse: repeated/overlapping `320x120@30` probes reused the same session
  across clean detach and within-grace reattach. Grace expiry removed sessions.
- Abrupt consumer: `480x180@30` probe exited without detach. Its one lease expired,
  session entered Grace without affecting other work, continued sequence advancement,
  then tore down. Final diagnostics reported 0 sessions, 0 leases, 4 creations,
  and 4 teardowns.
- Mapping cleanup: unit coverage verifies the opaque mapping name cannot reopen
  after the Host owner disposes it.
- Temporary Host cleanup: both acceptance Host processes were stopped and
  `127.0.0.1:48481` was confirmed closed.
- Not run: functional InfoPanel validation, Linux transport/runtime validation,
  formal benchmarking, and LAN/network validation; all are outside M3.

## Production State Versus Repository State

- Implemented: complete M3 behavior described above at
  `4a4ba9673573f15efd4afcccb272b0415637d24e`.
- Committed: implementation, tests, probe, documentation, and initial handoff in
  `4a4ba9673573f15efd4afcccb272b0415637d24e`.
- Pushed: authoritative `origin/main` read back at the same SHA with divergence
  `0 0`.
- Deployed or activated: no production deployment exists; two temporary local Host
  runs were used only for acceptance and then stopped.
- Runtime-validated: real Resonance provider, Host, loopback API, separate probe,
  Windows mapping, active/idle pixels, sharing, dimensions, 30/60 FPS, grace,
  stale expiry, and cleanup were directly observed.
- Documented or planned only: M4 InfoPanel integration, Linux/local transport,
  network transport, full profiles, and later visualization features.
- Unverified: behavior on machines other than this Windows acceptance environment.

## Unresolved Issues and Unverified Assumptions

- The three SkiaSharp obsolete text API warnings predate M3 and remain in the M2
  reconnecting/unavailable overlay. They do not fail current builds and were not
  expanded into this packet.
- `default-profile` is intentionally a stable temporary identity, not M5 profile
  storage/editing.
- The performance observations are local sanity data, not formal or cross-machine
  benchmarks.

## Safety, Rollback, and Access Considerations

- No force push, reset, stash, clean, history rewrite, branch creation, LAN exposure,
  Windows registry change, Resonance modification, or InfoPanel modification occurred.
- Shared-memory names are opaque and local-session scoped. Host owns the writer and
  mapping lifetime; consumers receive read semantics through the descriptor.
- Rollback is the ordinary revert of the scoped M3 commit after stopping Host. There
  is no database, migration, persisted audio, or external service state to roll back.
- The live run used existing per-user Auraline configuration/log paths; no secrets or
  sample payloads were logged or committed.
- Push authorization is explicit in the M3 packet; authoritative readback remains a
  separate required gate.

## Do Not Redo or Reopen

- Do not move Windows shared-memory types into `Auraline.Contracts`, the waveform
  renderer, or the render-session domain.
- Do not replace the per-session two-slot/seqlock protocol with a global lock or
  frame queue without measured evidence that changes the tradeoff.
- Do not key a session by consumer identity or mutate live dimensions under existing
  consumers; attach the new key and detach the old lease.
- Do not implement profile editing/history, Linux/network transport, or functional
  InfoPanel integration as an M3 correction.
- Do not repeat the 30/60 FPS, shared-session, dynamic-dimension, grace, or abrupt
  consumer investigations unless code/environment evidence changes.
- Continue to reverify repository/remote/runtime state; this handoff does not replace
  fresh evidence.

## Next Recommended Action

Implement the bounded M4 InfoPanel.Auraline Windows end-to-end integration against
the M3 versioned session/transport consumer contracts while preserving shared-core
semantics and the deferred Linux platform-adapter boundary.
