# ADR-0001: Initial Implementation Stack

Date: 2026-08-24
Status: Accepted

## Context

Auraline needs a Windows-first implementation stack suitable for a tray Host, an InfoPanel plugin, high-frequency 2D rendering, and a lightweight local configuration surface.

## Decision

Use C# and .NET 8. Use SkiaSharp for rendering, ASP.NET Core for the localhost API and web configuration surface, lightweight Razor/server-rendered pages with limited JavaScript, and Serilog for logging. Put genuinely shared Host/plugin contracts in Auraline.Contracts.

## Consequences

The first implementation aligns with InfoPanel's .NET ecosystem and avoids a separate heavy frontend stack. These libraries are architectural selections only at M0; dependencies will be added in the milestone that uses them and validated against actual compatibility then.

## M1 implementation evidence

M1 targets `net8.0-windows` for Auraline Host and `net8.0` for the contracts and plugin scaffold. The Host uses the ASP.NET Core and Windows Forms shared frameworks plus `Serilog.AspNetCore` 8.0.3 and `Serilog.Sinks.File` 6.0.0. No frontend framework or rendering dependency is present; SkiaSharp remains deferred until M2 uses it.
