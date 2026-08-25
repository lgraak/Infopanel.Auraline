# Auraline Architecture

## Status and scope

This document records the intended initial architecture for InfoPanel.Auraline. At M0 these components and behaviors are documented, not implemented. The first end-to-end proof is planned for M4.

## Product and component boundaries

Resonance Signal is the audio-data provider and owns audio capture, Windows audio-device discovery, provider-side source identity, and provider protocol behavior. Auraline is a client of Resonance Signal. It must not duplicate provider responsibilities.

Auraline Host is independent of InfoPanel and will launch independently with Windows. It owns provider connections, the domain model, rendering, render sessions, configuration, diagnostics, and control surfaces. InfoPanel.Auraline launches with InfoPanel and remains as thin as practical: it binds to a stable profile ID, negotiates a render session, transports frames, and displays them. It does not process waveform samples or contain product/rendering logic.

```text
Resonance Signal
    ↓
Auraline Host
    ↓
profiles / source groups / render engine
    ↓
render-session transport
    ↓
InfoPanel.Auraline
    ↓
InfoPanel
```

See [ADR-0002](decisions/0002-host-owned-rendering.md).

## Technology direction

The initial implementation direction is:

- C# and .NET 8;
- Windows as the v1 target;
- SkiaSharp for rendering;
- ASP.NET Core for the localhost Host API and web configuration surface;
- a lightweight Razor/server-rendered UI with limited JavaScript rather than a heavy single-page application;
- Serilog for logging; and
- Auraline.Contracts for contracts that genuinely need to be shared between the Host and plugin.

These are settled initial directions unless implementation evidence exposes a concrete incompatibility. No packages or production dependencies are introduced in M0. See [ADR-0001](decisions/0001-initial-implementation-stack.md).

## Process, configuration, and storage

Auraline Host will be a per-user tray application, not a Windows service. It will enforce a single Host instance per user and launch independently with Windows. First run will open the local web UI. Later starts will remain tray-only unless a critical startup failure needs to be surfaced.

The Host web/API surface will bind only to localhost in v1 and will not require authentication while it remains local-only. Any future LAN exposure requires authentication before enablement and should also define appropriate transport security. See [ADR-0003](decisions/0003-host-process-and-api-boundary.md).

The long-term installer target is `C:\Program Files\Auraline\`. Per-user configuration, state, and logs belong under `%LOCALAPPDATA%\Auraline\`; v1 configuration will use human-readable JSON and does not need to roam between machines. See [ADR-0004](decisions/0004-per-user-json-configuration.md).

## Domain model

```text
Providers
  ↓
Sources
  ↓
Source Groups
  ↓
Profiles
  ↓
Render Sessions
  ↓
Consumers
```

### Providers and sources

Multiple Resonance Signal providers are supported by design. Each provider has a stable ID, friendly name, endpoint, `Enabled` state, connection state, and last-error reason. Enabling a provider automatically reconnects it, and a successful connection automatically refreshes source discovery. Manual **Reconnect** and **Refresh Sources** actions are planned.

Initial configuration bootstraps an enabled provider named `Local Resonance Signal` at `127.0.0.1:48480`. Bootstrap configuration may be created while the provider is offline.

Sources are provider-owned observations with provider-authoritative identity and metadata. Auraline does not persist or reason from native Windows endpoint IDs.

### Source groups

Source groups have stable IDs and friendly names. One default source group is bootstrapped. There is no source-group `Enabled` state in v1.

The model permits future groups to span providers and to configure per-source gain. V1 uses equal gain with automatic normalization. If some members of a future group disappear, rendering continues with surviving sources while the group is visibly marked degraded.

### Profiles

Profiles have stable IDs and friendly names, reference one source group, and own visualization/rendering configuration. They are reusable and have no `Enabled` state in v1. One default profile always exists. InfoPanel bindings store stable profile IDs, never mutable display names.

### Render sessions and consumers

Consumers request render sessions from the Host. A session is keyed by at least profile and output dimensions; identical compatible requests may share a session. Consumers receive rendered frames and health metadata without taking ownership of rendering logic.

## Source identity and reconnect behavior

The first proof uses Resonance Signal's logical source intent:

```text
ws://127.0.0.1:48480/v1/waveform?source=default-playback
```

Auraline must not enumerate Windows audio devices, retain native endpoint IDs, infer or select the current Windows default endpoint, or assume an active Resonance Signal stream migrates when the endpoint changes.

For each connection attempt, Auraline retains logical `default-playback` intent and treats `stream_started.source_id` plus other stream metadata as authoritative. Every `stream_stopped` event and WebSocket close is terminal for that stream. Auraline resets continuity state across the boundary and, when continuation policy permits, opens a new logical Default Playback stream. The replacement may have a new `StreamId`, source identity, format, and zero-based timeline.

Reconnect guidance from the provider is interpreted as follows:

- `retry_now`: reconnect immediately;
- `wait_for_source`: use exponential backoff of approximately 500 ms, 1 s, 2 s, then 5 s, remaining capped around 5 s;
- a successful `stream_started` resets the backoff; and
- background retry may continue indefinitely at the capped interval.

Provider/source rebinding is conservative: exact identity is preferred, high-confidence matching is used only when necessary, and ambiguous matches remain unresolved rather than silently binding to a different source.

## Rendering and session architecture

The Host performs all rendering; the InfoPanel plugin does not process waveform samples. Rendering accepts dynamic dimensions.

Render sessions are created lazily and are keyed by at least profile plus dimensions. Compatible sessions may be shared. The intended v1 high-rate local frame transport is one shared-memory buffer per active render session, behind an abstraction that can later support network transport. Localhost HTTP remains appropriate for lower-rate metadata and control. See [ADR-0005](decisions/0005-shared-memory-frame-transport.md).

After the last consumer leaves, a session receives a 15-second grace period before teardown. An internal configurable safety cap limits concurrent sessions. If the cap is reached, idle sessions may be evicted before rejecting a new request; active sessions are never evicted to admit another session.

The default frame rate is 30 FPS. The architecture should permit 60 FPS without making it a v1 default.

## Waveform v1 intent

The first renderer remains deliberately narrow:

- one logical Default Playback source;
- a combined mono, centered oscilloscope-style trace;
- transparent background;
- one selectable solid trace color;
- a fixed sensible line thickness;
- automatic normalization;
- basic smoothing sufficient to avoid flicker;
- internally fixed attack/decay behavior;
- 30 FPS by default;
- dynamic dimensions; and
- no glow, blur, stereo split, or magnitude-based multicolor gradient.

Visual states are part of the renderer contract:

- **Active:** waveform only.
- **Idle:** subtle low-amplitude drift, automatically dimmed.
- **Reconnecting:** tasteful dimming with a subtle `Reconnecting…` indication.
- **Disconnected or unavailable:** an explicit visible status so failure cannot be mistaken for silence.
- **Degraded future group:** surviving sources continue to render while degraded health remains visible.

Normal labels and titles belong to InfoPanel, not the Auraline rendering surface.

## Deferred boundaries

Network frame transport, LAN access, generic browser/OBS-style consumers, additional visualization types, advanced mixing, and richer waveform styling remain post-proof-of-concept work. They are retained in the [roadmap](roadmap.md), not implied by the initial implementation.
