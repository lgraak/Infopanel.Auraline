# Auraline

![Auraline product mark](assets/branding/auraline-mark.png)

Auraline is the visualization and configuration layer between [Resonance Signal](https://github.com/lgraak/resonance-signal) and [InfoPanel](https://github.com/habibrehmansg/infopanel). Resonance Signal supplies live audio waveform data; Auraline turns it into transparent, exact-size visualization frames that InfoPanel can display.

> [!IMPORTANT]
> Auraline `0.1.0-beta.1` is a Windows x64 prerelease. It currently requires an InfoPanel 1.4-compatible build containing plugin image consumer-dimension support. The generic capability is being prepared for upstream contribution to InfoPanel. The stock/public InfoPanel preview does not currently satisfy this requirement.

## What Auraline does

Auraline does not capture audio. It connects to Resonance Signal, lets you choose which discovered source to visualize, stores reusable visualization profiles, and renders the resulting frames in the separate Auraline Host process.

```text
Resonance Signal
    ↓
Provider
    ↓
Source
    ↓
Source Group
    ↓
Profile
    ↓
InfoPanel.Auraline
    ↓
InfoPanel
```

The Host owns connections, configuration, rendering, and diagnostics. The InfoPanel.Auraline plugin is a thin consumer that selects a saved profile and publishes Host-rendered images to InfoPanel.

## Requirements

- Windows 10 or 11 x64.
- [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0). The beta Host is framework-dependent.
- A compatible Resonance Signal build providing loopback consumer protocol v1, normally at `http://127.0.0.1:48480`.
- An InfoPanel 1.4-compatible build containing plugin image consumer-dimension support. This capability is not in the current stock/public preview.
- `Auraline-0.1.0-beta.1-win-x64.zip`.

## Install the beta

The beta is a portable manual package rather than an installer:

```text
Auraline-0.1.0-beta.1-win-x64.zip
├─ Host/
├─ InfoPanel.Plugin/
│  └─ InfoPanel.Auraline/
├─ Branding/
├─ README.md
└─ checksums.txt
```

### Install Auraline Host

1. Extract the ZIP to a temporary folder.
2. Copy `Host` to a stable per-user location such as `%LOCALAPPDATA%\Programs\Auraline`.
3. Start `Auraline.Host.exe` from that folder.
4. On the first successful run, Auraline opens its local UI at `http://127.0.0.1:48481`. Later launches remain available from the Auraline tray icon.

The Host is independent of InfoPanel and should remain in its own folder. Configuration is stored under `%LOCALAPPDATA%\Auraline\config`, and bounded logs are stored under `%LOCALAPPDATA%\Auraline\logs`.

Do not manually install this beta into Program Files. A future Windows installer is intended to install the Host there, place the plugin in the correct InfoPanel folder, and support normal upgrades and removal.

### Install InfoPanel.Auraline

1. Fully exit InfoPanel with its supported tray **Exit** command. Closing an ordinary window may leave InfoPanel running.
2. Copy the complete `InfoPanel.Plugin\InfoPanel.Auraline` folder to:

   ```text
   %ProgramData%\InfoPanel\plugins\InfoPanel.Auraline\
   ```

3. Confirm the destination contains exactly these four files:

   ```text
   Auraline.Contracts.dll
   InfoPanel.Auraline.deps.json
   InfoPanel.Auraline.dll
   PluginInfo.ini
   ```

4. Start the compatible InfoPanel build and enable or load the **Auraline** plugin. Its configuration should expose **Auraline Host Endpoint**, **Auraline Profile**, and **Target FPS**.

Do not copy InfoPanel-owned contract DLLs, SkiaSharp, or `libSkiaSharp` into the plugin folder. Writing to `%ProgramData%` may require administrator approval.

## Get a waveform on screen

1. Start Resonance Signal and confirm its local consumer service is running.
2. Start Auraline Host.
3. Open `http://127.0.0.1:48481` from the tray icon if the UI is not already open.
4. On **Dashboard** or **Providers**, confirm **Local Resonance Signal** is connected.
5. On **Sources**, confirm **Default Playback** or the expected source is available.
6. Open **Profiles** and use or edit **Default Waveform**. Save any changes that should reach InfoPanel.
7. Start the compatible InfoPanel build and enable or load **Auraline**.
8. In Auraline plugin configuration, keep `http://127.0.0.1:48481` unless you deliberately changed the local Host endpoint, select the Auraline profile, and start with **30 FPS**.
9. Add the **Auraline Waveform** image output to the desired InfoPanel profile and resize it as needed.
10. Play audio through Default Playback and confirm the waveform moves.

## Auraline web interface

The Host UI is a loopback-only management surface. It is organized around the path from Resonance Signal connection to saved visualization.

### Dashboard

**Dashboard** is the high-level health page. Check the Host health and version, provider connection count, discovered sources, configured source groups and profiles, current default profile ID, waveform state, and active render sessions/consumer leases. It also contains the Start with Windows and theme preferences.

### Providers

A **Provider** is an instance of Resonance Signal that Auraline connects to. Most beta users need only the bootstrapped **Local Resonance Signal** provider at `http://127.0.0.1:48480`.

Each provider has a friendly name, stable ID, loopback endpoint, **Enabled** state, connection state, and last failure reason. **Reconnect** restarts its current connection attempt. **Refresh Sources** requests a new discovery snapshot from a connected provider. The configuration model supports multiple providers, although the fully proven beta path uses the local provider.

### Sources

A **Source** is an audio source exposed by Resonance Signal. The Sources page is primarily discovery and status information: it shows the display name, provider, availability, kind, channel/sample-rate evidence when available, and whether metadata is fresh or retained from an offline provider.

Source IDs are opaque provider-owned observations, not permanent hardware identities. Auraline preserves last-known metadata so configuration intent remains visible, but labels it stale/offline rather than presenting it as current. **Default Playback** is the fully proven beta path.

### Source Groups

A **Source Group** tells a profile which configured audio input or inputs it should visualize:

```text
Source       = an available audio input
Source Group = a reusable selection of audio inputs
Profile      = how that selection should look
```

The configuration model can persist explicit, multi-source, and cross-provider groups. The current renderer supports only the single local logical Default Playback group. Unsupported groups remain editable but preview or session attachment fails clearly; multi-source mixing is not implemented in this beta.

### Profiles

A **Profile** is the complete saved visualization recipe selected by InfoPanel. Its stable internal ID survives a rename; the friendly name is what people see. A profile references a Source Group and owns its waveform rendering settings.

The Profiles page supports create, duplicate, rename/edit, default promotion, and dependency-safe deletion. Saving an in-use profile creates a new revision and hot-applies it to active render sessions.

## Profile editor

### Name

The friendly profile name shown in Auraline and InfoPanel. Renaming it does not change the stable profile ID stored by InfoPanel.

### Source Group

Selects the configured audio input group the visualization uses. For the proven beta workflow, use the group containing the local logical Default Playback member.

### Waveform color

Sets the solid `#RRGGBB` waveform line color. The current background remains transparent. This changes only the visualization; it does not change or process audio output.

### Scale

**Automatic** uses Auraline's standard adaptive waveform processing and the renderer's normal display scale. It keeps quiet and loud material visually useful, so displayed height should not be treated as a fixed absolute loudness reference.

**Fixed** uses the configured **Fixed scale** as a final display multiplier after the Host's normal waveform processing. This gives direct control over on-screen height, but it is still a visualization setting rather than an absolute level meter.

### Fixed scale

The valid range is `0.05` through `10`, in `0.05` steps. Larger values make the waveform taller; smaller values make it shorter. Values that push samples beyond the available range clamp at the top or bottom. Start at `1.0`, then adjust for the desired visual height.

### Smoothing

The checkbox enables an additional spatial smoothing pass over the displayed waveform. **Smoothing amount** ranges from `0` to `1` in `0.05` steps. Lower values preserve more immediate detail and can look more energetic or jagged; higher values produce a calmer, steadier curve. This affects rendered pixels only and does not modify the audio.

### Target FPS

- **30 FPS** is the default and recommended beta setting. It is the most thoroughly validated and uses less rendering/update work.
- **60 FPS** is supported and may look smoother, but it costs more work and InfoPanel's measured display cadence has sometimes remained below a perfect 60 FPS.

### Current fixed presentation

This beta exposes one visualization type and style: a centered-line waveform with automatic line thickness and transparent background. Stereo modes, gradients, glow, alternate backgrounds, and other renderer types are deferred.

### Preview, Save, and Cancel

> [!NOTE]
> Editing any field updates **Live preview** with the real Auraline renderer. The preview is a working copy: unsaved changes do not affect InfoPanel. **Save** validates and persists the profile, increments its revision, and hot-applies it to active consumers. **Cancel** leaves the page and discards the unsaved working copy.

## Use profiles in InfoPanel

The Auraline plugin exposes these configuration fields:

- **Auraline Host Endpoint**: normally `http://127.0.0.1:48481`; only numeric loopback HTTP endpoints are accepted.
- **Auraline Profile**: shows the friendly profile name while retaining the stable profile ID in brackets.
- **Target FPS**: `30` or `60`; begin with `30`.

It also exposes two image outputs:

- **Auraline Waveform** (`waveform`): the primary output for ordinary use.
- **Auraline Waveform 2** (`waveform-2`): an optional second independently sized output.

Add the desired output as an image in an InfoPanel profile and resize the element normally. The compatible InfoPanel build reports the active consumer dimensions to the plugin, and Auraline automatically renders at the requested pixel size. Two differently sized elements should use the two distinct outputs.

## Diagnostics and troubleshooting

Open **Diagnostics** in the Host UI to inspect beta readiness, build/runtime versions, providers, sources, source groups, profiles, waveform health, render sessions, and InfoPanel consumer leases.

- **Run Host self-test** validates isolated Host rendering and transport without changing saved configuration or joining an active consumer session.
- **Copy diagnostics summary** copies a compact redacted Markdown report.
- **Export diagnostics** creates a ZIP containing current redacted state and up to seven bounded recent log files.
- **Info** is the normal logging level. **Debug** is temporary and resets to Info when the Host restarts.

Diagnostics intentionally exclude raw audio, waveform samples, and rendered frame pixels. Obvious usernames, profile paths, hostnames, and secret-like values are redacted by default; useful technical endpoint, provider, source, and profile names may remain.

When a waveform does not appear:

1. Is Resonance Signal running?
2. Does **Providers** show **Local Resonance Signal** as Connected?
3. Does **Sources** show Default Playback or the expected source?
4. Does **Run Host self-test** pass or report only an expected environmental skip?
5. Are you using an InfoPanel build containing the required consumer-dimension capability?
6. Does InfoPanel.Auraline show the expected profile and Host endpoint?
7. If the issue remains, export diagnostics and attach them to a report using the [beta report template](docs/beta-report-template.md).

## Current beta limitations

- Windows x64 only; Linux runtime and packaging are deferred.
- A compatible InfoPanel consumer-dimension build is required; public upstream support is pending.
- Default Playback is the fully proven source path. Explicit-source rendering and multi-source/cross-provider mixing are deferred.
- 30 FPS is recommended. 60 FPS is supported, but InfoPanel may display below the target cadence.
- Stereo visualizations are deferred.
- Advanced gradients, glow/blur, alternate styles, and configurable backgrounds are deferred.
- LAN/network consumers and network frame transport are deferred.
- There is no automatic updater.
- There is no final installer; installation, upgrades, rollback, and removal are manual.

These are deliberate beta boundaries. The core local Default Playback waveform path, persistent profiles, exact-size rendering, diagnostics, tray lifecycle, and Host restart recovery have been exercised in the compatible local Windows environment.

## Build and development

```powershell
dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config
dotnet build InfoPanel.Auraline.sln --configuration Debug --no-restore
dotnet build InfoPanel.Auraline.sln --configuration Release --no-restore
dotnet test InfoPanel.Auraline.sln --configuration Debug --no-build --no-restore
dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore
```

The InfoPanel contract assemblies are not published as NuGet packages. Populate the ignored `src/InfoPanel.Auraline/references` directory from the matching InfoPanel 1.4-compatible build as described in its README.

Build the combined package with:

```powershell
.\build\Build-Beta.ps1
```

It creates the ignored `dist/Auraline-0.1.0-beta.1-win-x64.zip` with separate Host and four-file plugin folders, newcomer instructions, and per-file SHA-256 checksums.

## More information

- [Architecture and ownership boundaries](docs/architecture.md)
- [InfoPanel platform integration](docs/infopanel-platform-integration.md)
- [Roadmap and deferred work](docs/roadmap.md)
- [Beta testing and reporting](docs/beta-testing.md)
- [Decision records](docs/decisions/README.md)

README screenshots or GIFs are a future improvement after the UI stabilizes.
