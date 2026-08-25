# ADR-0005: Shared Memory for V1 Local Frame Transport

Date: 2026-08-24
Status: Accepted

## Context

Rendered frames are high-rate local data, while session negotiation, metadata, and control are comparatively low-rate. Sending every frame through the localhost API would mix those concerns and add avoidable overhead.

## Decision

Use one shared-memory buffer per active render session as the preferred v1 local frame transport. Keep session metadata and control on the localhost HTTP/API surface. Put frame transport behind an abstraction that does not assume all future consumers are local.

## Consequences

The local consumer can receive frames efficiently, but session ownership, synchronization, naming, access control, cleanup, and crash recovery require explicit design in M3. Network frame transport remains deferred and can use a different implementation of the transport boundary.
