# Auraline.Host

The Host project owns per-user configuration, startup registration, single-instance coordination, the loopback web UI/API, bounded logging and temporary log-level control, Resonance Signal status/source discovery, provider retry lifecycle, waveform pipeline, render-session lifecycle, transport publication, self-test, and redacted diagnostics export.

Reusable configuration, provider, web, waveform, render-session, and transport contracts remain free of direct Windows APIs. `Platform/Windows` owns the Windows Forms tray, HKCU startup adapter, `%LOCALAPPDATA%` mapping, Windows single-instance primitives, and shared-memory transport. The composition root selects those implementations for the current Windows-only executable. Linux implementations and binaries remain deferred.

M5 adds a schema-versioned product catalog, last-known source snapshots, independently persisted source groups and profiles, dependency-safe CRUD APIs, and functional server-rendered management pages. The profile editor previews an unsaved working copy through the real renderer; Save atomically increments the profile revision and the existing render loop hot-applies it without replacing sessions, leases, geometry, cadence, or mappings.

The Windows layout remains M3's 128-byte header and two fixed pixel slots. M5 does not change the M4 session API or transport contract. Explicit-source, multi-source, and cross-provider groups can be stored and diagnosed, but the current waveform runtime rejects them until source mixing is implemented. Linux transport and Host binaries remain deferred. See the repository README for the configuration layout, UI workflow, API behavior, and probe instructions.

M6 retains Info logging by default and the existing 10 MiB/seven-file rolling bounds. Diagnostics can switch to Debug for the current process only; restart returns to Info. `/health` remains stable and concise, while `/api/v1/diagnostics` exposes current versions and runtime metrics. The self-test creates an isolated 64x32 transport and never attaches an active render-session lease. Summary and ZIP export apply deterministic redaction and exclude all sample and pixel payloads.

The beta Host is published framework-dependent for `win-x64`; testers need the .NET 8 Desktop Runtime x64. Build the combined package with `build/Build-Beta.ps1` from repository root.
