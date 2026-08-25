# InfoPanel.Auraline

InfoPanel.Auraline turns portable audio data from [Resonance Signal](https://github.com/lgraak/resonance-signal) into Host-rendered visuals for InfoPanel. Resonance Signal remains the provider, Auraline Host owns configuration, waveform processing, and rendering, and the InfoPanel plugin remains a thin consumer adapter.

M5 adds a complete loopback-only configuration UI and persistent provider, source-group, and profile management. Existing M4 `host.json` settings migrate in place, `default-profile` retains its stable identity, and saving a profile hot-applies the new revision to active render sessions without changing their session IDs, leases, dimensions, cadence, or transport mappings.

## Run the Host

Prerequisites are Windows 10/11, an x64 .NET 8 runtime/SDK, Resonance Signal on `http://127.0.0.1:48480`, and the matching InfoPanel 1.4.x prerequisite described below.

```powershell
dotnet run --project src/Auraline.Host/Auraline.Host.csproj --configuration Release
```

Open `http://127.0.0.1:48481`. The dashboard links to Providers, Sources, Source Groups, Profiles, and Diagnostics. Host and provider endpoints remain restricted to numeric HTTP loopback addresses.

## Persistent configuration

M5 keeps the M1-compatible Host settings/provider document and stores independently editable product objects separately:

```text
%LOCALAPPDATA%\Auraline\config\
├─ host.json
├─ catalog.json
├─ sources.json
├─ source-groups\<stable-id>.json
└─ profiles\<stable-id>.json
```

Writes use same-directory atomic replacement. Malformed product configuration is preserved and reported rather than overwritten. The first M5 start bootstraps `default-source-group` and the existing `default-profile`, so existing InfoPanel bindings remain valid.

Providers support create, edit, enable/disable, reconnect, refresh, and dependency-safe deletion. Sources retain their last-known provider snapshot and explicitly show when that evidence is stale or offline. Source groups retain logical intent and resolve conservatively: ambiguous identity never silently selects a source. M5 persists explicit-source, multi-source, and cross-provider groups, but the current waveform runtime renders only the single local logical `default-playback` group; unsupported groups fail preview and session attach clearly.

Profiles support create, duplicate, rename, edit, default promotion, dependency-safe deletion, and a live working-copy preview. The first renderer exposes trace color, centered line, automatic or fixed scale, bounded smoothing, 30/60 FPS, and transparent background. Cancel discards the working copy; Save increments the persistent revision and hot-applies it to active sessions.

## InfoPanel selection

The plugin retrieves the current profile catalog when InfoPanel opens its configuration properties. Choices display friendly profile names while persisting the stable ID in brackets. Active image consumers continue using M4's versioned shared-memory contract and recover across Host restarts.

The required `IPluginImageConsumerAware` InfoPanel prerequisite is still a local checkpoint and is not present in installed InfoPanel `1.4.0-preview.2.43`. Populate `src/InfoPanel.Auraline/references` from the matching local x64 build as described in that folder's README.

## Build and test

```powershell
dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config
dotnet build InfoPanel.Auraline.sln --configuration Debug --no-restore
dotnet build InfoPanel.Auraline.sln --configuration Release --no-restore
dotnet test InfoPanel.Auraline.sln --configuration Debug --no-build --no-restore
dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore
```

The Release build creates the exact manual plugin package:

```text
src/InfoPanel.Auraline/artifacts/InfoPanel.Auraline/
├─ InfoPanel.Auraline.dll
├─ Auraline.Contracts.dll
├─ InfoPanel.Auraline.deps.json
└─ PluginInfo.ini
```

Copy that complete folder to `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline\` only while following the activation and rollback guidance in [the plugin README](src/InfoPanel.Auraline/README.md).

## Current limitations

- The current renderer consumes only one logical Default Playback source; source-group mixing and cross-provider rendering remain deferred.
- The InfoPanel consumer-demand prerequisite remains local and unpublished upstream.
- Packaging is manual; the combined installer is deferred.
- Linux Host, transport, and InfoPanel integration are not implemented or supported.
- LAN access, network frame transport, additional renderer types, stereo modes, glow/blur, multicolor gradients, and profile history remain deferred.

For ownership and lifecycle details, see [architecture](docs/architecture.md), [platform integration](docs/infopanel-platform-integration.md), [roadmap](docs/roadmap.md), [decision records](docs/decisions/README.md), and the dated M5 handoff when published.
