# ADR-0006: Windows-First Implementation with Cross-Platform Boundaries

Date: 2026-08-25
Status: Accepted

## Context

Auraline's first supported runtime is Windows, but provider consumption, product models, waveform processing, rendering logic, and contracts should not require replacement when Linux support is added. M1 placed reusable and Windows shell code in one Host project, so platform ownership must be explicit before M2 adds substantial waveform and rendering logic.

## Decision

Windows remains the first and only currently supported runtime. Linux support is planned, not implemented. Reusable product logic must remain OS-agnostic .NET code wherever practical. Platform integrations belong behind narrow interfaces or in explicitly named platform namespaces and directories. Each new Windows-specific capability must document its responsibility and expected deferred Linux counterpart.

Keep the current solution structure while these boundaries remain coherent. Defer a physical core or `Auraline.Host.Windows`/`Auraline.Host.Linux` project split until Linux implementation or build evidence demonstrates that it is necessary.

## Consequences

The Windows Host can continue using Windows Forms, HKCU startup, `%LOCALAPPDATA%`, and Windows single-instance primitives without coupling provider, configuration, web, contract, or future waveform logic to those APIs. Linux implementations will be added at the platform boundary after their actual runtime requirements are known. The current `net8.0-windows` Host target is not evidence of Linux runtime support.

M2 waveform protocol, sample processing, stream/render state, renderer abstraction, rendered-frame contract, and metrics must remain OS-agnostic wherever technically reasonable. Treat SkiaSharp as cross-platform unless package or runtime evidence establishes otherwise.

## Portability audit evidence

The 2026-08-25 audit found Contracts, the InfoPanel scaffold, provider models/client/retry lifecycle, configuration schemas and validation, loopback guard, health contracts, and UI rendering free of direct Windows API dependencies. Windows Forms tray ownership moved under `Platform/Windows`; current-user paths now enter through `IPlatformPaths`; single-instance coordination implements `ISingleInstanceCoordinator`; and HKCU startup remains behind `IStartupRegistration` with a testable registry adapter. No Linux dependency, project split, waveform implementation, or external contract change was introduced.
