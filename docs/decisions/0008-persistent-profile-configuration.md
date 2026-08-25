# ADR-0008: Persistent profile configuration and saved-revision hot apply

Status: Accepted

## Context

M4 exposed one temporary `default-profile` and keyed render sessions by profile ID, dimensions, and cadence. M5 must make providers, source groups, and profiles independently editable without invalidating existing InfoPanel bindings or changing the established session, lease, and shared-memory contracts.

Configuration failures must remain recoverable and inspectable. Browser preview must be truthful to the real renderer without persisting every edit or mutating active consumers.

## Decision

- Retain the M1 `host.json` schema for Host settings and provider migration.
- Store product catalog metadata, last-known sources, each source group, and each profile in separate schema-versioned JSON documents beneath the per-user configuration directory.
- Write each document through a same-directory temporary file, durable flush, and atomic replacement. Preserve malformed input and fail product persistence closed.
- Bootstrap `default-source-group` and preserve `default-profile` as stable IDs.
- Give each profile a monotonic saved revision. Active session render loops read the latest saved revision/settings while retaining session ID, lease, geometry, cadence key, scheduler, and transport mapping.
- Preview an unsaved browser working copy through the same waveform renderer and current render-state source. Preview does not persist, create a render session, or expose raw samples.
- Persist source intent conservatively. Exact identity is preferred, a unique provider-scoped name/kind match may rebind, and ambiguity remains unresolved.
- Permit explicit-source, multi-source, and cross-provider groups in configuration, but reject preview/session attach when the current single-source runtime cannot render the group.

## Consequences

Existing M4 bindings migrate without ID churn, profile saves are visible without consumer renegotiation, and malformed objects can be repaired without losing their original bytes. Independent documents reduce the blast radius of ordinary edits.

The Host has two related configuration stores during migration: `host.json` for established Host/provider settings and the product catalog layout for sources/groups/profiles. A future schema unification requires a separately designed migration.

Persisting a group does not claim the renderer can mix it. UI and APIs must keep unsupported-runtime state explicit until mixing is implemented.
