# Auraline 0.1.0-beta.1

Auraline's first beta brings local, profile-driven audio waveform visualizations to InfoPanel on Windows.

## What is Auraline?

Auraline is the visualization and configuration layer between Resonance Signal and InfoPanel. Resonance Signal supplies live audio waveform data; Auraline Host turns it into transparent, exact-size rendered frames consumed by the InfoPanel.Auraline plugin.

## What's included

- Portable framework-dependent Auraline Host for Windows x64.
- Four-file InfoPanel.Auraline plugin package.
- Local web configuration UI for providers, sources, source groups, and profiles.
- Persistent provider, source-group, and profile configuration with stable identities.
- Saved centered-line waveform profiles with solid color, automatic/fixed display scale, smoothing, and 30/60 FPS targets.
- Real-renderer working-copy preview and saved-profile hot apply.
- Exact-size rendering for InfoPanel image consumers, including two independently sized image outputs.
- Active, idle, reconnecting, and unavailable visualization states.
- Diagnostics, isolated Host self-test, redacted summary, and redacted ZIP export.
- Windows tray lifecycle, first-run browser launch, and product branding.

## Requirements

> **Compatibility prerequisite:** Auraline `0.1.0-beta.1` requires an InfoPanel 1.4-compatible build containing plugin image consumer-dimension support. The generic capability is being prepared for upstream contribution to InfoPanel. The stock/public InfoPanel preview does not currently work with this beta.

- Windows 10 or 11 x64.
- .NET 8 Desktop Runtime x64.
- Resonance Signal with loopback consumer protocol v1.
- The compatible InfoPanel build described above.

## Installation

Download `Auraline-0.1.0-beta.1-win-x64.zip`, verify its SHA-256 checksum, and follow the included README or the repository [installation guide](../../README.md#install-the-beta).

In short: copy `Host` to a stable per-user folder, start `Auraline.Host.exe`, fully exit InfoPanel through its tray **Exit** command, then copy the complete `InfoPanel.Plugin\InfoPanel.Auraline` folder to `%ProgramData%\InfoPanel\plugins\InfoPanel.Auraline`. Start the compatible InfoPanel build, choose an Auraline profile, and add the **Auraline Waveform** image output.

## Known beta limitations

- Windows x64 only.
- Compatible InfoPanel consumer-dimension support is required and public upstream availability is pending.
- Default Playback is the fully proven source path; explicit-source rendering and multi-source/cross-provider mixing are deferred.
- 30 FPS is recommended. 60 FPS is supported, but InfoPanel display cadence may remain below target.
- Stereo modes, gradients, glow/effects, alternate backgrounds, LAN/network consumers, Linux runtime, automatic updates, and a final installer are deferred.

## Reporting problems

Open Auraline Host **Diagnostics**, run **Host self-test**, and use **Export diagnostics**. The export is redacted and excludes raw audio, waveform samples, and rendered pixels. Attach it with the repository [beta report template](../beta-report-template.md) to a GitHub issue.

## Checksums

`Auraline-0.1.0-beta.1-win-x64.zip`

```text
SHA-256  C0CE1CFC28A030BBF46D738941FCDA964C68FD56B7E8638F82AC44D8DE30E68B
```
