# ADR-0005: Shared Memory for V1 Local Frame Transport

Date: 2026-08-24
Status: Accepted

## Context

Rendered frames are high-rate local data, while session negotiation, metadata, and control are comparatively low-rate. Sending every frame through the localhost API would mix those concerns and add avoidable overhead.

## Decision

Use one shared-memory buffer per active render session as the preferred v1 local frame transport. Keep session metadata and control on the localhost HTTP/API surface. Put frame transport behind an abstraction that does not assume all future consumers are local.

## Consequences

The local consumer can receive frames efficiently. M3 implements one opaque local-namespaced mapping per session, a 128-byte versioned header, two RGBA8888-premultiplied pixel slots, and an odd/even publication seqlock. Consumers retry if the publication version changes while copying. The Host owns mapping lifetime; explicit/expiring leases and 15-second zero-consumer grace provide deterministic cleanup after clean or crashed consumers. Network frame transport remains deferred and can use a different implementation of the transport boundary.
