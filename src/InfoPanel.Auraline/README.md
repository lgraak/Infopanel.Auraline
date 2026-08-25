# InfoPanel.Auraline plugin

This project is the thin Windows InfoPanel adapter for Auraline M4. It owns InfoPanel lifecycle/configuration, profile discovery, render-session leases, latest-frame consumption, and InfoPanel image publication. Resonance Signal consumption, waveform processing, rendering, color, smoothing, and idle-state visuals remain entirely in Auraline Host.

## Source layout

- `Core/`: HTTP client, stable profile/session orchestration, lease/reconnect policy, demand selection, and diagnostics. Source guards keep Windows and InfoPanel implementation types out.
- `Platform/Windows/`: the read-only M3 shared-memory reader and layout validation.
- `Adapters/`: InfoPanel writer/pixel adaptation and stable-ID profile choice formatting.
- `AuralinePlugin.cs`: current InfoPanel lifecycle, configuration, two image descriptors, and low-volume diagnostic entries.

## Compile references

The current InfoPanel contracts are not published as NuGet packages. Populate the ignored `references/` binaries from the exact InfoPanel 1.4.x prerequisite build; see `references/README.md`. These Host-supplied assemblies are compile/test inputs and are deliberately excluded from the plugin package.

## Output and installation

```powershell
dotnet build src/InfoPanel.Auraline/InfoPanel.Auraline.csproj --configuration Release --no-restore
```

The build recreates `artifacts/InfoPanel.Auraline`. Copy that complete directory to `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline\`, then restart InfoPanel or use its supported module reload action. The required folder convention is `InfoPanel.Auraline/InfoPanel.Auraline.dll`.

Remove the beta by exiting InfoPanel and deleting only `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline\`. No Host, Resonance Signal, or InfoPanel binaries belong in the plugin folder.

## Image outputs

- `waveform`: primary Auraline waveform output.
- `waveform-2`: second independent output for different-size simultaneous display elements.

InfoPanel sends replacement demand snapshots. For one output, the plugin selects the largest active area and owns one producer buffer/session. Binding two different-size items to the two output IDs creates independent exact-size sessions. Resize uses first-valid-frame handover before detaching the prior lease.
