# ADR-0001: Initial Implementation Stack

Date: 2026-08-24
Status: Accepted

## Context

Auraline needs a Windows-first implementation stack suitable for a tray Host, an InfoPanel plugin, high-frequency 2D rendering, and a lightweight local configuration surface.

## Decision

Use C# and .NET 8. Use SkiaSharp for rendering, ASP.NET Core for the localhost API and web configuration surface, lightweight Razor/server-rendered pages with limited JavaScript, and Serilog for logging. Put genuinely shared Host/plugin contracts in Auraline.Contracts.

## Consequences

The first implementation aligns with InfoPanel's .NET ecosystem and avoids a separate heavy frontend stack. These libraries are architectural selections only at M0; dependencies will be added in the milestone that uses them and validated against actual compatibility then.
