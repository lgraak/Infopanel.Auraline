# ADR-0002: Host-Owned Rendering and Thin Consumers

Date: 2026-08-24
Status: Accepted

## Context

Rendering inside the InfoPanel plugin would couple Auraline product behavior to one consumer and duplicate processing for future consumers.

## Decision

Auraline Host owns provider connections, waveform processing, visualization logic, and final frame rendering. InfoPanel.Auraline remains a thin profile-binding, session-transport, and display adapter. It does not process waveform samples. Normal labels and titles remain InfoPanel concerns.

## Consequences

Rendering behavior remains reusable and independently testable, while the plugin stays small. The Host must provide explicit render-session lifecycle and frame transport contracts.
