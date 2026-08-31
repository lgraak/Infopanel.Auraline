# Auraline Repository Instructions

## Project identity and ownership

- Project: `lgraak/Infopanel.Auraline`; this checkout and its configured `origin` are the authoritative project repository and remote.
- Read `.project-standards.toml`, then the named portable standards at the exact adopted revision before substantive work.
- The Observer owns product direction, feature and release selection, architecture changes, deployment, and merge approval. The Executor performs only the authorized Auraline milestone and stops after validation and handoff.

## Project-specific boundaries

- Resonance Signal owns audio capture, waveform data, device discovery, source identity, and provider protocol behavior. Auraline consumes that contract; it does not reimplement those responsibilities.
- Auraline Host owns provider connections, processing, rendering, profiles, source groups, configuration, diagnostics, render sessions, and transport. `InfoPanel.Auraline` remains a thin frame consumer and InfoPanel adapter.
- Keep implementation Windows-first while preserving narrow platform boundaries and OS-agnostic reusable logic. Linux runtime, packaging, and adapters require an explicit milestone.
- Host API, provider endpoints, and local transport remain numeric-loopback-only. LAN or network exposure requires separate authentication and transport-security design.
- Never persist raw audio or waveform samples. Diagnostics and exports must also exclude rendered frame pixels and retain current redaction and bounded-log behavior.
- Preserve stable profile and source-group IDs across renames and edits. Treat provider source IDs as opaque provider-owned observations.
- Host and plugin versions move together. The current line is `0.1.0-beta.1`: framework-dependent Windows x64 Host plus the exact four-file plugin package. Do not change version or package boundaries without explicit authorization.
- A public beta remains gated on a distributable InfoPanel 1.4-compatible build with plugin image consumer-dimension support.
- Before replacing an active InfoPanel plugin, use InfoPanel's tray **Exit** command and verify the process is stopped. Do not force-terminate InfoPanel or overwrite active or locked plugin files.

## Project-specific validation

- Restore/build/test/format when code or build inputs change: `dotnet restore InfoPanel.Auraline.sln --configfile NuGet.Config`, Debug and Release builds, `dotnet test`, and `dotnet format --verify-no-changes` as documented in `README.md`.
- Run `./build/Build-Beta.ps1` and verify the archive/checksums only when packaging inputs or release artifacts change.
- For documentation or governance-only work, validate adoption paths and repository links, run `git diff --check`, review the final diff, and do not rerun runtime acceptance without a runtime-relevant change.

## Explicit exceptions

- None.
