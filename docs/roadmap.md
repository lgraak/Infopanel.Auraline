# Auraline Roadmap

This roadmap distinguishes shipped milestone scope from deferred intent. M4 is the first true end-to-end proof; M5 is the first persistent product-configuration milestone.

## Milestone checklist

- [x] M0: Repository and architecture skeleton
- [x] M1: Auraline Host core
- [x] M2: Waveform engine
- [x] M3: Render-session and IPC layer
- [x] M4: InfoPanel.Auraline end-to-end integration
- [x] M5: Host configuration UI and persistent profiles
- [x] M6: Diagnostics and beta readiness

## Milestones

### M0: Repository and architecture skeleton

Establish approachable project documentation, architecture boundaries, decision records, repository layout, standards, and a continuation handoff. No functional runtime is included.

### M1: Auraline Host core

Create the .NET 8 solution and executable Host foundation. Establish the provider/source catalog, per-user JSON configuration, single-instance tray lifecycle, loopback health/UI shell, bounded logging, Windows startup setting, and Resonance Signal v1 status/discovery lifecycle. Source-group/profile navigation is present but their editable models remain deferred. Rendering, waveform streaming, render sessions, and InfoPanel runtime integration remain out of M1.

### M2: Waveform engine

Implement and test the first Host-owned renderer: combined mono, centered trace, transparent background, selectable solid color, automatic normalization, basic smoothing, fixed attack/decay, dynamic dimensions, 30 FPS default, and explicit idle/reconnecting/disconnected states.
M2 now includes a Host-owned websocket waveform path (`default-playback`), protocol validation, combined-mono DSP pipeline, SkiaSharp rendering, state machine, and a loopback diagnostics PNG snapshot of the real renderer output.

### M3: Render-session and IPC layer

Implement lazy render-session lifecycle, compatible-session sharing, the 15-second teardown grace period, configurable concurrent-session safety cap, safe idle eviction, and the local shared-memory frame transport behind a transport abstraction.

M3 now includes the stable temporary `default-profile`, profile/dimension/FPS-compatible sharing, 25-second renewable leases, exact-dimension 30/60 FPS scheduling without deadline backlog, a default cap of 32, deterministic idle LRU eviction, versioned two-slot Windows shared memory with seqlock validation, loopback v1 session control/diagnostics, and an external cross-process probe. Linux transport and functional InfoPanel use remain deferred.

### M4: InfoPanel.Auraline end-to-end integration

Build the thin InfoPanel adapter, bind it by stable profile ID, negotiate a render session, transfer frames, and display the waveform in InfoPanel. This is the first true end-to-end proof and the point at which hands-on product validation begins.

The M4 repository implementation includes the Windows plugin, profile catalog, exact consumer-demand sizing, shared-memory reader, reconnect/lease lifecycle, InfoPanel image adapter, manual package, diagnostics, and focused tests. Direct acceptance passed in the matching local Windows InfoPanel prerequisite runtime for Active and Idle display, transparency and color, resize cleanup, two differently sized consumers, plugin unload/reload, explicit unavailable state, and automatic Host-restart recovery. Direct mapping measurements observed about 27–28.5 InfoPanel publishes per second at the 30 FPS setting and about 48–51.5 at the 60 FPS setting while Host sessions ran about 29.8 and 57.6 FPS; the latter is a bounded sanity mode rather than full 60 FPS display acceptance. The prerequisite remains local and is not present in the older installed public preview.

### M5: Host configuration UI

M5 expands the localhost-only Host shell into functional provider, source, source-group, profile, and diagnostics pages. It preserves M4 `host.json` and stable `default-profile` identity while adding atomic per-object product configuration, last-known source evidence, dependency-safe CRUD, default promotion, duplicate, revisioned profile saves, real-renderer working-copy preview, and hot apply to active sessions.

The persistent model accepts explicit-source, multi-source, and cross-provider groups, but the current waveform runtime still renders only a single local logical Default Playback member. Unsupported groups remain visible and fail preview/session attach clearly; mixing is not implied by configuration support.

### M6: Diagnostics and beta readiness

M6 formalizes the loopback `/api/v1/diagnostics` surface while keeping `/health` concise. The Host exposes build/runtime/provider/source/profile/waveform/session state, temporary Info/Debug control, a bounded current-run isolated self-test, redacted Markdown summary, and user-initiated ZIP export with bounded logs and no audio/sample/pixel payloads.

Host and plugin now use coherent `0.1.0-beta.1` prerelease versioning. A repeatable PowerShell target builds a framework-dependent Windows x64 Host plus the exact M4 four-file plugin, tester README, and SHA-256 manifest into one combined ZIP. Public distribution remains gated on shipping the separate InfoPanel consumer-dimension prerequisite.

## Intended beta flow

1. Prove core behavior locally.
2. Have the user perform initial hands-on testing.
3. Release to a small beta group.
4. Collect diagnostics and feedback.
5. Continue appropriate deferred feature work in parallel.
6. Incorporate beta findings into later releases.

## Deferred and post-proof-of-concept work

These ideas are intentionally retained without expanding the initial proof:

### Waveform presentation

- stereo-split and stereo-overlay modes;
- filled or mirrored waveform styles;
- additional smoothing and interpolation algorithms beyond M5's bounded amount control;
- configurable attack and decay;
- line-thickness controls;
- glow and blur;
- magnitude-based color blending with several configurable color stops;
- solid or gradient backgrounds; and
- background images with fit, tint, and opacity controls.

### Additional renderers

- spectrum;
- VU;
- spectrogram;
- phase; and
- other visualization types through an extensible renderer model.

### Sources and mixing

- multi-source and cross-provider source groups;
- per-source gain and weighting; and
- advanced mix policies.

### Operations and consumers

- rolling diagnostic history and sparklines;
- LAN Host access, gated on authentication and TLS design;
- generic web, browser, and OBS-style consumers;
- network frame transport;
- a possible Windows service split if a demonstrated use case emerges;
- profile history and rollback;
- update checking and later auto-update support;
- richer installer integration; and
- README screenshots or GIFs after the UI stabilizes.

### Future Linux enablement

Linux enablement remains outside the current M0-M6 Windows proof-of-concept milestones. Before claiming Linux support, add and validate a Linux Host shell, tray/status notifier, XDG configuration/data/log locations, Linux autostart integration, packaging, and cross-platform installation documentation. Compare the actual Windows and Linux InfoPanel plugin APIs before deciding whether the plugin remains one project or gains platform-specific adapters. Perform Linux runtime validation against Resonance Signal only after its Linux provider is available. No Linux tray framework, startup mechanism, package, or binary is selected by the current roadmap.
