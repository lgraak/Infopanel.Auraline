# Auraline 0.1.0-beta.1 for Windows x64

![Auraline product mark](Branding/auraline-mark.png)

Auraline is the visualization and configuration layer between Resonance Signal and InfoPanel. Resonance Signal supplies waveform data; Auraline Host turns it into transparent exact-size images for the InfoPanel.Auraline plugin. Auraline does not capture audio directly.

This is a portable prerelease beta. Installation, updates, rollback, and removal are manual.

> Auraline `0.1.0-beta.1` currently requires an InfoPanel 1.4-compatible build containing plugin image consumer-dimension support. The generic capability is being prepared for upstream contribution to InfoPanel. The stock/public InfoPanel preview does not currently work with this beta.

## Package layout

```text
Auraline-0.1.0-beta.1-win-x64.zip
├─ Host/
├─ InfoPanel.Plugin/
│  └─ InfoPanel.Auraline/
├─ Branding/
├─ README.md
└─ checksums.txt
```

## Requirements

- Windows 10 or 11 x64.
- [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0); the Host is framework-dependent.
- Resonance Signal with loopback consumer protocol v1, normally at `http://127.0.0.1:48480`.
- A compatible InfoPanel 1.4 build containing plugin image consumer-dimension support.

## Install and first run

1. Copy `Host` to a stable per-user folder such as `%LOCALAPPDATA%\Programs\Auraline`.
2. Start `Auraline.Host.exe`. The first successful run opens `http://127.0.0.1:48481`; the Auraline tray icon provides later access and the supported **Exit** command.
3. Fully exit InfoPanel through its tray **Exit** command. Do not force-kill it or assume an ordinary window close stopped the process.
4. Copy the complete `InfoPanel.Plugin\InfoPanel.Auraline` folder to `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline`.
5. Confirm the destination has exactly `Auraline.Contracts.dll`, `InfoPanel.Auraline.deps.json`, `InfoPanel.Auraline.dll`, and `PluginInfo.ini`.
6. Start the compatible InfoPanel build and enable or load the **Auraline** plugin.

Configuration is under `%LOCALAPPDATA%\Auraline\config`; bounded logs are under `%LOCALAPPDATA%\Auraline\logs`. Ordinary per-user Host operation should not require elevation, but writing the shared InfoPanel plugin folder may require administrator approval.

Do not copy InfoPanel-owned contract assemblies, SkiaSharp, or `libSkiaSharp` into the plugin folder. Do not install this portable beta into Program Files manually. A future installer is intended to install the Host and plugin and support normal upgrade/removal behavior.

## Get a waveform on screen

1. Start Resonance Signal.
2. Start Auraline Host and open its local web UI.
3. Confirm **Local Resonance Signal** is Connected and **Sources** shows Default Playback.
4. Open **Profiles**, use or edit **Default Waveform**, and **Save** changes intended for InfoPanel.
5. In InfoPanel's Auraline plugin configuration, keep the Host endpoint at `http://127.0.0.1:48481`, choose the profile, and begin with **30 FPS**.
6. Add **Auraline Waveform** to the InfoPanel profile. Use **Auraline Waveform 2** only when a second independently sized output is needed.
7. Play audio and verify motion.

The profile editor's **Live preview** uses the real renderer but is only a working copy. Preview changes do not affect InfoPanel until **Save** validates, persists, and hot-applies them. **Cancel** discards unsaved changes.

## Troubleshooting and diagnostics

Open Host **Diagnostics**, run **Host self-test**, then use **Copy diagnostics summary** or **Export diagnostics**. Check the Provider connection, Default Playback source, expected saved profile, render sessions/consumer leases, and compatible InfoPanel build.

The export contains redacted current state and bounded logs. It never contains raw audio, waveform samples, or rendered frame pixels. Obvious usernames, profile paths, hostnames, and secret-like values are redacted; useful technical endpoint, provider, source, and profile names may remain. **Debug** logging is temporary and resets to **Info** after a Host restart.

If InfoPanel is absent, the Host and self-test still operate. If Resonance Signal or Default Playback is unavailable, the self-test reports environmental stages as skipped rather than mislabelling Auraline as internally broken. Verify `checksums.txt` before replacing an existing installation.

## Removal, rollback, and updates

Exit InfoPanel and Auraline Host through their supported tray **Exit** commands. Remove only the copied `InfoPanel.Auraline` plugin folder and Host folder. Keep `%LOCALAPPDATA%\Auraline` to preserve profiles, or back it up and remove it for a complete per-user reset. Disable **Start Auraline with Windows** before removing the Host. To update, repeat the copy steps with both Host and plugin from the same beta package.

## Current limitations

- Windows x64 only; a matching InfoPanel consumer-dimension prerequisite is required and public upstream support is pending.
- Default Playback is the proven source path. Explicit-source rendering and multi-source/cross-provider mixing are deferred.
- 30 FPS is recommended. 60 FPS is supported, but InfoPanel display cadence may remain below target.
- Stereo visualization, gradients, glow/effects, alternate backgrounds, LAN/network consumers, Linux runtime, automatic updates, and a final installer are deferred.

For a bug report, run self-test, export diagnostics, and use the repository's beta report template or GitHub issue flow.
