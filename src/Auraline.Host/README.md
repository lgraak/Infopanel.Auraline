# Auraline.Host

The Host project owns per-user configuration, startup registration, single-instance coordination, the loopback web UI/API, bounded logging, Resonance Signal status/source discovery, provider retry lifecycle, and the first Host-owned waveform pipeline.

Reusable configuration, provider, web, and lifecycle contracts remain free of direct Windows APIs. `Platform/Windows` owns the Windows Forms tray, HKCU startup adapter, `%LOCALAPPDATA%` mapping, and Windows single-instance primitives. The composition root selects those implementations for the current Windows-only executable. Linux implementations and binaries remain deferred.

M2 adds waveform stream protocol decoding, channel-preserving processing, SkiaSharp rendering, and a loopback diagnostics PNG snapshot produced from the real renderer. Source-group/profile editing, render sessions, and frame transport remain deferred. See the repository README for build, run, and preview instructions.
