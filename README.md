# InfoPanel.Auraline

InfoPanel.Auraline is a Windows visualization platform that will turn portable audio data from [Resonance Signal](https://github.com/lgraak/resonance-signal) into reusable rendered visuals. M1 now includes the executable Auraline Host foundation. Waveform rendering and the functional InfoPanel plugin begin in later milestones.

## What works in M1

Auraline Host now runs as a single per-user Windows tray application. It has no normal application window and provides:

- a loopback-only web UI and `GET /health` API;
- human-readable per-user JSON configuration;
- current-user Windows startup registration;
- an enabled default provider named `Local Resonance Signal` at `127.0.0.1:48480`;
- Resonance Signal v1 status and source discovery using `/v1/status` and `/v1/sources`;
- provider enable, disable, reconnect, automatic retry, and source-refresh lifecycle;
- Dashboard, Providers, Sources, Source Groups, Profiles, and Diagnostics navigation;
- bounded rolling Serilog files; and
- a small shared contract-version foundation.

The Source Groups and Profiles pages are honest placeholders. M1 does not render or transport waveform frames and does not contain a functional InfoPanel integration.

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

On the first successful start, Auraline opens the Dashboard in the default browser and records that first-run completion. Later ordinary starts remain tray-only. The tray menu provides **Open Auraline**, **Reconnect Providers**, and **Exit**. Starting the executable again signals the existing per-user instance to open the UI and then exits the duplicate.

The Dashboard includes **Start Auraline with Windows** and System, Light, and Dark theme settings. Startup uses the current user's standard `Run` registry entry and requires no administrator privileges. A registration failure is shown on the Dashboard and does not crash the Host.

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
src/Auraline.Host/          Windows tray Host, loopback UI/API, persistence, and providers
src/Auraline.Contracts/     Host/plugin contract-version foundation without UI dependencies
src/InfoPanel.Auraline/     Build-only plugin boundary; no functional integration yet
tests/Auraline.Host.Tests/  Durable Host/config/provider lifecycle tests
docs/                       Architecture, roadmap, decisions, handoffs, and standards
```

## Current limitations and M2

M1 does not consume waveform frames, render a waveform, create render sessions, use shared-memory transport, mix sources, edit source groups/profiles, or integrate with InfoPanel at runtime. Channel count and sample rate remain blank in the Sources table because Resonance Signal v1 discovery does not expose them; those fields arrive with a waveform stream.

M2 adds the first Host-owned waveform engine while preserving the provider, configuration, process, and loopback boundaries established here. See the [architecture](docs/architecture.md), [roadmap](docs/roadmap.md), and [decision records](docs/decisions/README.md).
