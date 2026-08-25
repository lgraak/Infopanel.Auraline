# Auraline Roadmap

This roadmap describes intent, not shipped functionality. M4 is the first true end-to-end proof.

## Milestone checklist

- [x] M0: Repository and architecture skeleton
- [x] M1: Auraline Host core
- [x] M2: Waveform engine
- [ ] M3: Render-session and IPC layer
- [ ] M4: InfoPanel.Auraline end-to-end integration
- [ ] M5: Host configuration UI
- [ ] M6: Diagnostics and beta readiness

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

### M4: InfoPanel.Auraline end-to-end integration

Build the thin InfoPanel adapter, bind it by stable profile ID, negotiate a render session, transfer frames, and display the waveform in InfoPanel. This is the first true end-to-end proof and the point at which hands-on product validation begins.

### M5: Host configuration UI

Expand the M1 localhost-only ASP.NET Core status/control shell into the complete configuration UI for provider definition editing, source groups, profiles, and richer health. Preserve the v1 local-only security boundary; manual provider enable/disable, reconnect, refresh, startup, and theme controls already exist.

### M6: Diagnostics and beta readiness

Add actionable health reporting, secret-safe diagnostics, packaging readiness, and the operational evidence needed for a small beta. Reconcile documentation and installer behavior with the system that actually exists.

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
- configurable smoothing and interpolation algorithms;
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

- short rolling diagnostic history and sparklines;
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
