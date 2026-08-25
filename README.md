# InfoPanel.Auraline

InfoPanel.Auraline is a Windows-first visualization platform that turns portable audio data from [Resonance Signal](https://github.com/lgraak/resonance-signal) into Host-rendered visuals for InfoPanel. Resonance Signal remains the provider, Auraline Host owns waveform processing and rendering, and the InfoPanel plugin is only a consumer adapter.

M4 implements and directly validates the first Windows InfoPanel integration. The dated M4 handoff records the exact local prerequisite build and interactive runtime evidence; do not treat repository tests alone, or the older installed InfoPanel preview, as equivalent proof. Linux InfoPanel integration remains planned and is not implemented or supported.

## What M4 implements

- A loopback-only Auraline Host profile catalog at `GET /api/v1/profiles`.
- A real InfoPanel 1.4.x plugin with Host-managed endpoint, stable profile ID, and 30/60 FPS configuration.
- Consumer-demand-driven render sessions at the exact final InfoPanel pixel dimensions.
- Read-only consumption of M3's versioned, double-slot Windows shared-memory transport.
- Direct RGBA8888-premultiplied transfer into InfoPanel's Skia-backed image writer.
- First-frame resize handover, lease heartbeat/detach, bounded reconnect, Host restart recovery, and a 1.5-second stale-frame grace.
- Two image outputs, `waveform` and `waveform-2`, so different-size InfoPanel elements can own independent Host sessions. Compatible same-size requests still share one Host render loop.
- Low-volume plugin diagnostics for connection, version, profile, sessions, dimensions, FPS, latest sequence/age, reconnect count, and last error.
- A manual development/beta package under `src/InfoPanel.Auraline/artifacts/InfoPanel.Auraline`.

The plugin does not capture audio, process samples, render waveform geometry, persist pixels, or use the diagnostics PNG endpoint as a frame path.

## Prerequisites

- Windows 10 or 11 and an x64 .NET 8 runtime/SDK.
- Resonance Signal listening on numeric loopback `127.0.0.1:48480`.
- Auraline Host listening on numeric loopback `127.0.0.1:48481`.
- A current InfoPanel 1.4.x build containing the optional `IPluginImageConsumerAware` contract. The prerequisite is currently a local InfoPanel checkpoint and has not been published upstream; installed InfoPanel `1.4.0-preview.2.43` is too old.

## Build and test

Populate `src/InfoPanel.Auraline/references` from the matching InfoPanel 1.4.x x64 build as described in that folder's README, then run from the repository root:

```powershell
dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config
dotnet build InfoPanel.Auraline.sln --configuration Debug --no-restore
dotnet build InfoPanel.Auraline.sln --configuration Release --no-restore
dotnet test InfoPanel.Auraline.sln --configuration Debug --no-build --no-restore
dotnet format InfoPanel.Auraline.sln --verify-no-changes --no-restore
```

The Release build creates this installable folder without copying assemblies supplied by InfoPanel itself:

```text
src/InfoPanel.Auraline/artifacts/InfoPanel.Auraline/
├─ InfoPanel.Auraline.dll
├─ Auraline.Contracts.dll
├─ InfoPanel.Auraline.deps.json
└─ PluginInfo.ini
```

## Windows proof-of-concept workflow

1. Start Resonance Signal and confirm `http://127.0.0.1:48480/v1/status` is ready.
2. Start Auraline Host:

   ```powershell
   dotnet run --project src/Auraline.Host/Auraline.Host.csproj --configuration Release
   ```

3. Confirm `http://127.0.0.1:48481/health` and `http://127.0.0.1:48481/api/v1/profiles` respond.
4. Copy the complete package folder to `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline\`. Remove it by exiting InfoPanel and deleting that one plugin folder.
5. Start the matching InfoPanel 1.4.x prerequisite build. Open **Plugins**, find **Auraline**, and activate or reload it if it is not already active.
6. In the profile designer, open the plugin sensor tree and select **Auraline → Images → Auraline Waveform**. Add it as an HTTP image display item. Use **Auraline Waveform 2** for a second independently sized output.
7. Open the Auraline plugin configuration to keep the default endpoint/profile or select another Host-returned friendly name. The persisted value retains the stable profile ID in brackets.
8. Resize the display item and inspect `GET /api/v1/render-sessions`: the active session dimensions should follow the final rendered pixel demand, and superseded leases should detach after the new frame is valid.

When audio is active, InfoPanel displays Host-rendered motion. When playback stops, it displays the Host-rendered Idle state unchanged. A Host or transport failure retains the last frame briefly, then replaces it with a transparent `Auraline unavailable` surface until reconnection.

## Troubleshooting

- **Plugin will not load:** verify InfoPanel includes `IPluginImageConsumerAware`, the folder is named `InfoPanel.Auraline`, all four package files are present, and InfoPanel was restarted or the module was reloaded after installation.
- **Host unavailable:** confirm only `http://127.0.0.1:<port>` is configured and that `/api/v1/profiles` responds. `localhost`, HTTPS, LAN addresses, and non-loopback hosts are rejected intentionally.
- **Profile unresolved:** keep or reselect the stable ID returned by the Host. The plugin does not silently fall back to an unrelated profile.
- **Blank or stale image:** inspect the plugin's Auraline diagnostic entries and `/api/v1/render-sessions`; mapping/layout errors are rejected and renegotiated rather than read as pixels.
- **No waveform motion:** use `/health` to distinguish Host/plugin connectivity from Resonance Signal and waveform state. Idle or unavailable waveform visuals are owned by the Host.

## Current limitations

- The InfoPanel consumer-demand prerequisite is local and not yet present in the installed public preview build.
- Packaging is manual; the combined Auraline installer is deferred.
- M5 profile/source-group editing, stereo, advanced visual styling, LAN transport, Linux transport, and Linux InfoPanel integration are not implemented.
- The plugin offers two independent output slots; multiple display items bound to one slot share that slot's largest active dimension because InfoPanel exposes one producer buffer per image ID.

For ownership and lifecycle details, see [architecture](docs/architecture.md), [platform integration](docs/infopanel-platform-integration.md), [roadmap](docs/roadmap.md), and the [decision records](docs/decisions/README.md).
