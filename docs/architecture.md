# Auraline Architecture

## Status and scope

This document records the initial architecture for InfoPanel.Auraline. M1 implements the Windows tray Host, loopback UI/API, per-user configuration, provider lifecycle, and source-discovery foundation. Rendering, render sessions, shared-memory transport, and functional InfoPanel integration remain planned for M2-M4.

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

These are settled initial directions unless implementation evidence exposes a concrete incompatibility. M1 uses ASP.NET Core and Windows Forms from the .NET 8 shared frameworks plus Serilog's ASP.NET Core and rolling-file packages. SkiaSharp remains deferred until rendering begins. See [ADR-0001](decisions/0001-initial-implementation-stack.md).

## Cross-platform and platform boundaries

Auraline is Windows-first, with Linux as an intended future target. The current executable Host remains a `net8.0-windows` Windows Forms application; no Linux Host, tray integration, autostart implementation, packaging, or runtime support exists today.

Reusable Auraline responsibilities must remain OS-agnostic .NET code wherever technically reasonable. This includes provider/protocol consumption, source and configuration models, validation, reconnect policy, web/API contracts, and future waveform processing, render-state logic, rendering abstractions, rendered-frame contracts, and metrics. `Auraline.Contracts` and the InfoPanel scaffold already target `net8.0`. The reusable code currently housed in `Auraline.Host` has no direct dependency on Windows APIs, although the Host project as a whole targets Windows because it contains the executable shell.

Current platform ownership is explicit:

| Responsibility | Current implementation | Future Linux responsibility |
| --- | --- | --- |
| Tray shell | Windows Forms `NotifyIcon` under `Platform/Windows` | Linux tray/status notifier, framework deferred |
| Autostart | HKCU `Run` behind `IStartupRegistration` | XDG desktop or systemd-user mechanism, selected after runtime inspection |
| Per-user paths | `%LOCALAPPDATA%\Auraline\` behind `IPlatformPaths` | XDG configuration/data/log paths |
| Single instance | Local-namespaced semaphore and named pipe behind `ISingleInstanceCoordinator` | Equivalent per-user Linux coordination |
| Browser launch | `IBrowserLauncher` with shell execution | Validate the existing implementation or replace only its platform adapter |
| Provider, configuration, web, and contracts | OS-agnostic logic | Shared unchanged |

New platform-specific behavior must be isolated behind a narrow boundary and document both its platform responsibility and the deferred Linux counterpart. A physical `Auraline.Host.Windows`/`Auraline.Host.Linux` or core project split is deferred until Linux implementation evidence requires it; the current bounded separation does not justify a large project restructure. See [ADR-0006](decisions/0006-windows-first-cross-platform-boundaries.md).

M2 must preserve this boundary. The Resonance Signal waveform client and decoder, stream lifecycle, reconnect policy, waveform sample model, channel/mono processing, normalization, smoothing, idle/reconnecting/unavailable state, renderer abstraction, rendered-frame contract, and metrics remain OS-agnostic. SkiaSharp is treated as cross-platform unless package or runtime evidence establishes otherwise. The first Windows consumer is not a reason to introduce Windows APIs into waveform or rendering core logic.

## Process, configuration, and storage

Auraline Host is a Windows-specific `WinExe` tray application, not a Windows service. A per-user named synchronization object admits one instance and a per-user named pipe lets a duplicate signal the primary instance to open the web UI. The first successful run opens the UI and persists completion; later starts remain tray-only. Current-user startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and surfaces failure without terminating the Host.

The Host web/API surface binds explicitly to `http://127.0.0.1:48481` by default and does not require authentication while it remains local-only. State-changing browser requests reject cross-site origins/fetch context so another website cannot silently submit the local forms. Provider endpoints in M1 configuration are likewise limited to numeric HTTP loopback addresses. Any future LAN exposure requires authentication before enablement and should also define appropriate transport security. The Windows composition root supplies platform paths, autostart, single-instance coordination, tray, and browser services; reusable configuration/provider/web logic consumes platform-neutral values or interfaces. See [ADR-0003](decisions/0003-host-process-and-api-boundary.md).

The long-term installer target is `C:\Program Files\Auraline\`. M1 stores schema-versioned JSON at `%LOCALAPPDATA%\Auraline\config\host.json` and bounded rolling logs under `%LOCALAPPDATA%\Auraline\logs\`. Configuration writes use a same-directory temporary file and atomic replacement. Malformed configuration is preserved, reported, and not overwritten by later settings actions. See [ADR-0004](decisions/0004-per-user-json-configuration.md).

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

Multiple Resonance Signal providers are supported by design. Each provider has a stable ID, friendly name, endpoint, `Enabled` state, connection state, and last-error reason. Enabling a provider automatically reconnects it, and a successful connection automatically refreshes source discovery. Manual **Reconnect** and **Refresh Sources** actions are implemented in the Providers page, and the tray can reconnect all enabled providers.

Initial configuration bootstraps an enabled provider named `Local Resonance Signal` at `127.0.0.1:48480`. Bootstrap configuration may be created while the provider is offline.

Sources are provider-owned observations with provider-authoritative identity and metadata. Auraline does not persist or reason from native Windows endpoint IDs.

M1 implements provider states `Disabled`, `Disconnected`, `Connecting`, `Connected`, and `Reconnecting`. Enabled providers use a cancellable `500 ms`, `1 s`, `2 s`, `5 s` capped retry sequence. A successful status/discovery cycle resets provider backoff. A low-frequency 15-second status/discovery probe refreshes current provider evidence without producing successful-poll log spam. Disabled providers and Host shutdown cancel active waits.

### Current Resonance Signal v1 evidence

M1 was implemented against Resonance Signal `main` at `1da75ecb771eebfec597aaa8d4c64f8863b46381` and its `docs/consumer-protocol.md`. The Host uses only:

- `GET /v1/status` for readiness and protocol version 1;
- `GET /v1/sources` for complete replaceable discovery snapshots; and
- the documented loopback endpoint root, defaulting to `http://127.0.0.1:48480`.

Discovery supplies opaque `source_id`, nullable `display_name`, `kind`, `availability`, point-in-time `default_playback`, and `supported_products`. It does not supply channel count or sample rate; those are therefore nullable Host source metadata until a later waveform `stream_started` event. M1 does not open `/v1/waveform` as a probe and does not persist the current-run source catalog because the provider contract does not define snapshot persistence as durable identity state. A non-v1 provider response is surfaced as an explicit compatibility failure.

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
