# InfoPanel.Auraline

InfoPanel.Auraline is a Windows-first visualization platform that turns portable audio data from [Resonance Signal](https://github.com/lgraak/resonance-signal) into reusable rendered visuals. The current executable support is Windows only; Linux binaries and integrations are not implemented or supported. Reusable product logic is intentionally kept behind cross-platform boundaries so Linux support can be added later without replacing the Auraline core.

M3 implements Host-owned render sessions and the first local frame transport while preserving the established boundaries.

## What works in M3

Auraline Host now runs as a single per-user Windows tray application and also:

- runs a loopback-only web UI and `GET /health` API;
- maintains human-readable per-user JSON configuration;
- supports current-user Windows startup registration;
- keeps an enabled default provider named `Local Resonance Signal` at `127.0.0.1:48480`;
- performs Resonance Signal v1 status/source discovery via `/v1/status` and `/v1/sources`;
- supports provider enable/disable, reconnect, and source refresh lifecycle;
- runs Dashboard, Providers, Sources, Source Groups, Profiles, and Diagnostics navigation;
- consumes and validates waveform protocol events and frames with `default-playback` intent;
- decodes and processes channel-preserving waveform data into combined mono;
- normalizes and smooths waveform frames for visual stability;
- renders an oscilloscope-style centered waveform using SkiaSharp with transparent background;
- tracks waveform metrics and exposes waveform health + intent metadata in `/health`, Dashboard, and Diagnostics;
- creates render sessions lazily for the stable `default-profile` plus requested dimensions;
- shares one session, scheduler, and published frame stream among compatible consumers;
- renders exact dynamic dimensions at 30 FPS by default, with 60 FPS accepted by the same scheduler;
- publishes only rendered premultiplied RGBA8888 pixels through a platform-neutral transport contract;
- uses one opaque Windows shared-memory mapping with two frame slots per active session;
- protects readers from torn frames with an odd/even publication seqlock and retry validation;
- tracks explicit and expiring consumer leases, retains idle sessions for 15 seconds, and evicts LRU zero-consumer sessions at the default 32-session cap;
- exposes versioned `/api/v1/render-sessions/...` negotiation, heartbeat, detach, and diagnostics endpoints; and
- runs bounded rolling Serilog logging.

The Source Groups and Profiles pages remain placeholders. M3 is Host/transport-focused and does not include stereo rendering, multi-source mixing, full profile editing, Linux transport, or functional InfoPanel runtime integration.

## Prerequisites

- Windows 10 or 11;
- the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or a newer SDK able to target .NET 8; and
- Resonance Signal running on `127.0.0.1:48480` for live provider discovery. The Host still starts and remains usable when the provider is offline.

## Build and test

From the repository root:

```powershell
dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config
dotnet build InfoPanel.Auraline.sln --configuration Debug --no-restore
dotnet test InfoPanel.Auraline.sln --configuration Debug --no-build --no-restore
dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore
```

Release build:

```powershell
dotnet build InfoPanel.Auraline.sln --configuration Release --no-restore
```

## Run Auraline Host

```powershell
dotnet run --project src/Auraline.Host/Auraline.Host.csproj
```

The Host listens only on:

```text
http://127.0.0.1:48481/
```

Port `48481` is configurable in the JSON file and intentionally does not conflict with Resonance Signal's default `48480` port.

On the first successful start, Auraline opens the Dashboard in the default browser and records first-run completion. Later ordinary starts remain tray-only. The tray menu provides **Open Auraline**, **Reconnect Providers**, and **Exit**. Starting the executable again signals the existing per-user instance to open the UI and then exits the duplicate.

The Dashboard includes **Start Auraline with Windows** and System, Light, and Dark theme settings. Startup uses the current user's standard `Run` registry entry and requires no administrator privileges. A registration failure is shown on the Dashboard and does not crash the Host.

### Observe the waveform and M3 render sessions

Start Resonance Signal on its default loopback listener, play audio through Windows Default Playback, and open `http://127.0.0.1:48481/diagnostics`. The Waveform Engine card reports the live stream metadata, state, counters, and render timing, and shows the latest `320x120` PNG snapshot produced by the real Host renderer. Refresh the page to update the snapshot; it remains a bounded diagnostics preview rather than the high-rate transport.

The Render Sessions card reports active sessions, leases, dimensions, target/actual frame rate, publication sequence, render-plus-publication timing, allocation size, grace state, and lifecycle counters. To prove the real cross-process Windows transport, run a separate probe process while Host is running:

```powershell
dotnet run --project tests/Auraline.TransportProbe --no-build -- --width 320 --height 120 --fps 30 --seconds 4
```

The probe attaches through HTTP, opens only the returned opaque shared-memory resource, validates complete frames and advancing sequence/pixels, heartbeats when needed, and detaches cleanly. Use `--width 640 --height 240` for a distinct session or `--fps 60` for the supported higher cadence. `--abrupt` intentionally skips detach for stale-lease acceptance.

## Render-session control API

All routes remain bound to `127.0.0.1` with the Host. The v1 control surface is:

```text
POST   /api/v1/render-sessions/attach
POST   /api/v1/render-sessions/{sessionId}/leases/{leaseId}/heartbeat
DELETE /api/v1/render-sessions/{sessionId}/leases/{leaseId}
GET    /api/v1/render-sessions
GET    /api/v1/render-sessions/{sessionId}
```

Attach accepts contract major/minor, `default-profile`, dimensions from 16 through 2048, and target FPS 30 or 60. Contract and shared-memory layout compatibility use major-version matching; an unsupported major fails explicitly.

## Per-user files

Mutable state never belongs in the source or installation directory:

```text
%LOCALAPPDATA%\Auraline\
├─ config\host.json
└─ logs\auraline-YYYYMMDD.log
```

Configuration uses schema version 1 and is written through a same-directory temporary file followed by atomic replacement. A malformed file is preserved unchanged; the Host starts with safe in-memory defaults, reports degraded configuration health, and blocks configuration writes until the file is repaired.

Logs default to Information, roll daily or at 10 MB, and retain seven files. Auraline does not log audio samples, credentials, or secret material.

## Provider behavior

Enabled providers connect and discover sources automatically. An unavailable provider is shown as `Reconnecting` with a concise current-run reason while the Host and UI remain available. Retry delays follow `500 ms`, `1 s`, `2 s`, then cap at `5 s` indefinitely. Success resets the sequence. Disabling a provider or exiting the Host cancels its retry loop.

Auraline consumes provider-owned source metadata and treats source IDs and discovery revisions as opaque. It does not enumerate Windows audio devices, retain native endpoint IDs, choose the Windows default endpoint, or assume a future active waveform stream can migrate.

## Repository layout

```text
src/Auraline.Host/          Windows tray Host plus reusable loopback, persistence, and provider logic
src/Auraline.Contracts/     Host/plugin contract-version foundation without UI dependencies
src/InfoPanel.Auraline/     Build-only plugin boundary; no functional integration yet
tests/Auraline.Host.Tests/  Durable Host/config/provider and waveform tests
tests/Auraline.TransportProbe/  External Windows shared-memory consumer proof
docs/                       Architecture, roadmap, decisions, handoffs, and standards
```

## Current limitations in M3

M2 does not implement:

- LAN-hosted API access;
- functional InfoPanel runtime integration;
- Linux/local or network frame transport;
- multi-source mixing and stereo render modes;
- source-group/profile editing workflow.

Source count, channel count, and sample rate in Sources are populated when a waveform stream starts. Channel/sample metadata return to null when stream telemetry is unavailable, which is expected behavior for this proof.

For the full ownership and architecture intent, see [architecture](docs/architecture.md), [roadmap](docs/roadmap.md), and [decision records](docs/decisions/README.md).
