# InfoPanel.Auraline

InfoPanel.Auraline turns portable audio data from [Resonance Signal](https://github.com/lgraak/resonance-signal) into Host-rendered visuals for InfoPanel. Resonance Signal remains the provider, Auraline Host owns configuration, waveform processing, rendering, and diagnostics, and the InfoPanel plugin remains a thin consumer adapter.

Auraline is currently `0.1.0-beta.1` for Windows x64. M6 adds first-class diagnostics, an isolated Host self-test, safe Markdown/ZIP evidence export, temporary Debug logging, and a reproducible combined beta package. Public distribution remains gated on a compatible InfoPanel build containing the generic image consumer-dimension capability used by this plugin.

## Run the Host

Prerequisites are Windows 10/11 x64, an x64 .NET 8 Desktop runtime/SDK, Resonance Signal protocol v1 on `http://127.0.0.1:48480`, and the matching InfoPanel prerequisite described below.

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

## Diagnostics and beta reports

Open **Diagnostics** in the Host UI to inspect build/runtime metadata, providers, sources, source groups, profiles, waveform counters, render sessions/leases, logging, and beta readiness. **Run Host self-test** creates only isolated temporary rendering/transport resources. Provider or source unavailability is reported as an environmental skip; it does not require InfoPanel.

**Copy diagnostics summary** produces compact redacted Markdown. **Export diagnostics** produces a timestamped ZIP with current metadata and up to seven recent bounded logs. Neither output contains audio samples, waveform samples, or rendered frame pixels. Obvious usernames, profile paths, hostnames, and secret-like values are redacted; useful technical endpoint/provider/profile names may remain. Use [the beta report template](docs/beta-report-template.md).

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

Build the combined framework-dependent Windows x64 beta package with:

```powershell
.\build\Build-Beta.ps1
```

It creates an ignored `dist/Auraline-0.1.0-beta.1-win-x64.zip` containing separate Host and four-file plugin folders, newcomer instructions, and SHA-256 checksums. See [beta testing](docs/beta-testing.md).

## Current limitations

- The current renderer consumes only one logical Default Playback source; source-group mixing and cross-provider rendering remain deferred.
- The InfoPanel consumer-demand prerequisite remains local and unpublished upstream.
- Packaging is manual; the combined installer is deferred.
- Windows x64 is the only supported runtime; the .NET 8 Desktop Runtime is required by the framework-dependent package.
- Default Playback is the fully proven source path; explicit and multi-source runtime mixing are deferred.
- 30 FPS is the normal validated target. 60 FPS is supported, but InfoPanel display cadence has measured below target.
- Stereo modes, advanced colors/effects/backgrounds, LAN/network consumers, Linux runtime, automatic updates, and a final installer are deferred.
- Linux Host, transport, and InfoPanel integration are not implemented or supported.
- LAN access, network frame transport, additional renderer types, stereo modes, glow/blur, multicolor gradients, and profile history remain deferred.

For ownership and lifecycle details, see [architecture](docs/architecture.md), [platform integration](docs/infopanel-platform-integration.md), [roadmap](docs/roadmap.md), [decision records](docs/decisions/README.md), and the dated M5 handoff when published.
