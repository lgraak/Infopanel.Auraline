# Auraline.Host

The M1 Windows tray Host lives here. It owns per-user configuration, startup registration, single-instance coordination, the loopback web UI/API, bounded logging, Resonance Signal status/source discovery, and provider retry lifecycle.

Reusable configuration, provider, web, and lifecycle contracts remain free of direct Windows APIs. `Platform/Windows` owns the Windows Forms tray, HKCU startup adapter, `%LOCALAPPDATA%` mapping, and Windows single-instance primitives. The composition root selects those implementations for the current Windows-only executable. Linux implementations and binaries remain deferred.

Waveform streaming/rendering, source-group/profile editing, render sessions, and frame transport remain deferred. See the repository README for build and run instructions.
