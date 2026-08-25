# ADR-0007: Keep Auraline frame transport abstract before platform-specific contracts

Date: 2026-08-25
Status: Accepted

## Context

M3 introduces render-session transport and frame movement from Auraline Host to InfoPanel.Auraline.

Windows InfoPanel currently presents a plugin lifecycle model that is not directly matched by Linux plugin-image writer interfaces.

Linux InfoPanel exposes explicit plugin image contracts (`IPluginImageProvider`, `PluginImageDescriptor`, `IPluginImageWriter`) and `plugin-image://{pluginId}/{imageId}` URIs.

If Windows shared-memory behavior became the de-facto contract, it would block Linux portability and force the transport model into Windows-specific constraints too early.

## Decision

Auraline defines transport as an explicit abstraction independent of plugin contracts.

- Keep render-session semantics and frame transport in platform-agnostic Auraline contracts.
- Define `IAuralineFrameTransport` with producer/consumer roles and versioned frame transfer semantics.
- Treat `Windows shared-memory` as the first concrete local transport implementation only.
- Keep plugin image/provider specifics behind platform adapters (not inside the renderer transport contract).
- Keep M3 session dimensions, frame keys, and stale-frame policy in Auraline transport/domain.

## Consequences

1. Windows shared-memory remains the first local transport for the v1 runtime.
2. Linux-specific transport alternatives can be added by implementing the same Auraline transport interfaces.
3. InfoPanel.Auraline can remain mostly shared for session orchestration, with platform adapters for plugin-image integration.
4. Transport behavior is no longer coupled to one InfoPanel repository implementation detail.
5. Future network transport work can reuse the same Auraline session and frame abstractions.

## M3 implementation note

`Auraline.Contracts` now defines OS-neutral session descriptors, consumer leases, frame publication/read results, and publisher/reader/factory interfaces. The concrete `MemoryMappedFile` implementation and its opaque resource naming live under `Auraline.Host/Platform/Windows`; neither Windows nor InfoPanel types appear in the shared contracts or render-session domain. The descriptor exposes only transport kind/version, opaque resource name, allocation/header/slot geometry, and pixel format needed by a compatible local adapter.

## M4 implementation note

`InfoPanel.Auraline/Core` implements profile/session/lease/reconnect orchestration without Windows memory mapping or InfoPanel types. `Platform/Windows` supplies the read-only layout-v1 consumer, and `Adapters` supplies the InfoPanel writer bridge. Source-level tests enforce those boundaries. The existing M3 transport layout and Host renderer semantics are unchanged.

Direct Windows acceptance confirmed the boundary at two simultaneous dimensions, 30/60 FPS, plugin reload, and Host restart. InfoPanel consumed Host-rendered pixels through the adapter; no waveform renderer or sample path was added to the plugin.
