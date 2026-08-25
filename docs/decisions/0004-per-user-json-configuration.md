# ADR-0004: Per-User Human-Readable JSON Configuration

Date: 2026-08-24
Status: Accepted

## Context

V1 needs inspectable configuration and state for a single-user tray Host. Roaming and multi-user machine-wide configuration are not requirements.

## Decision

Store per-user configuration, state, and logs under `%LOCALAPPDATA%\Auraline\`. Use human-readable JSON for v1 configuration. Install application binaries under `C:\Program Files\Auraline\` when an installer is introduced.

## Consequences

Configuration is supportable and easy to inspect without a database dependency. Schema evolution, atomic writes, validation, and secret handling must be designed when persistence is implemented; M0 does not define their mechanisms.

## M1 implementation evidence

Schema version 1 lives at `%LOCALAPPDATA%\Auraline\config\host.json`; rolling logs live beside it under `logs\`. Writes serialize to a same-directory temporary file and replace the destination atomically. Missing configuration bootstraps one stable local provider. Malformed or invalid configuration is preserved unchanged, reported through Host health/UI, and made read-only until repaired rather than silently reset.
