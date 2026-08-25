# Auraline.Host

The Host project owns per-user configuration, startup registration, single-instance coordination, the loopback web UI/API, bounded logging, Resonance Signal status/source discovery, provider retry lifecycle, waveform pipeline, render-session lifecycle, and transport publication.

Reusable configuration, provider, web, waveform, render-session, and transport contracts remain free of direct Windows APIs. `Platform/Windows` owns the Windows Forms tray, HKCU startup adapter, `%LOCALAPPDATA%` mapping, Windows single-instance primitives, and shared-memory transport. The composition root selects those implementations for the current Windows-only executable. Linux implementations and binaries remain deferred.

M5 adds a schema-versioned product catalog, last-known source snapshots, independently persisted source groups and profiles, dependency-safe CRUD APIs, and functional server-rendered management pages. The profile editor previews an unsaved working copy through the real renderer; Save atomically increments the profile revision and the existing render loop hot-applies it without replacing sessions, leases, geometry, cadence, or mappings.

The Windows layout remains M3's 128-byte header and two fixed pixel slots. M5 does not change the M4 session API or transport contract. Explicit-source, multi-source, and cross-provider groups can be stored and diagnosed, but the current waveform runtime rejects them until source mixing is implemented. Linux transport and Host binaries remain deferred. See the repository README for the configuration layout, UI workflow, API behavior, and probe instructions.
