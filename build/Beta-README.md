# Auraline 0.1.0-beta.1 for Windows x64

Auraline turns local Resonance Signal waveform data into Host-rendered images for InfoPanel. This is a prerelease beta; updates and removal are manual.

## Requirements

- Windows 10 or 11 x64.
- [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0) because the Host package is framework-dependent.
- Resonance Signal with loopback consumer protocol v1, normally at `http://127.0.0.1:48480`.
- A compatible InfoPanel build containing the generic plugin image consumer-dimension capability. Public upstream availability is pending.

## Install and first run

1. Copy `Host` to a stable per-user folder such as `%LOCALAPPDATA%\Programs\Auraline`.
2. Start `Auraline.Host.exe`. The first run opens `http://127.0.0.1:48481`; the tray icon controls later access and exit.
3. Exit InfoPanel completely from its tray icon.
4. Copy `InfoPanel.Plugin\InfoPanel.Auraline` to `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline` as one complete folder.
5. Start the compatible InfoPanel build. In Auraline plugin configuration, keep the loopback Host endpoint, choose a profile, and select 30 FPS for normal use.
6. In the Host UI, create or edit a profile and add the `Auraline Waveform` visualization in InfoPanel.

Configuration is under `%LOCALAPPDATA%\Auraline\config`; bounded logs are under `%LOCALAPPDATA%\Auraline\logs`. Ordinary per-user operation should not require elevation; writing the shared InfoPanel plugin folder may require administrator approval.

## Troubleshooting and diagnostics

Open Host **Diagnostics**, run **Host self-test**, then use **Copy diagnostics summary** or **Export diagnostics**. The export contains redacted current state and bounded logs. It never contains audio samples, waveform samples, or rendered frame pixels. Technical endpoint, provider, source, and profile names may remain.

If InfoPanel is absent, the Host and self-test still operate. If Resonance Signal or Default Playback is unavailable, the self-test reports environmental stages as skipped rather than mislabelling Auraline as internally broken. Verify `checksums.txt` before replacing an existing installation.

## Removal, rollback, and updates

Exit InfoPanel and Auraline Host. Remove only the copied `InfoPanel.Auraline` plugin folder and Host folder. Keep `%LOCALAPPDATA%\Auraline` to preserve profiles, or back it up and remove it for a complete per-user reset. Disable **Start with Windows** before removing the Host. To update, repeat the copy steps with both Host and plugin from the same beta package.

## Current limitations

- Windows x64 only; a matching InfoPanel consumer-dimension prerequisite is required and public upstream support is pending.
- Default Playback is the proven source path. Explicit/multi-source runtime mixing is not implemented.
- 30 FPS is the normal validated target. 60 FPS is supported, but InfoPanel display cadence has measured below target.
- Stereo visualization, advanced colors/effects/backgrounds, LAN/network consumers, Linux runtime, automatic updates, and a final installer are deferred.
