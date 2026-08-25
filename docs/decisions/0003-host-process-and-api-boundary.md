# ADR-0003: Per-User Tray Host and Localhost-Only V1 API

Date: 2026-08-24
Status: Accepted

## Context

Auraline needs an independently running configuration and rendering owner without taking on service-account complexity or exposing an unauthenticated control surface to the network.

## Decision

Run one Auraline Host instance per user as a Windows tray application that launches independently with Windows. First run opens the local web UI; subsequent starts remain tray-only unless a critical startup failure occurs. Bind the v1 web/API surface to localhost only and do not require authentication while it remains local-only. Require authentication before any future LAN exposure.

## Consequences

Configuration and rendering operate in the interactive user's context. V1 avoids service lifecycle and credential complexity. LAN access cannot be enabled as a simple bind-address change; it requires an explicit security design, including authentication and appropriate transport security.

## M1 implementation evidence

The Host is a Windows `WinExe` with a Windows Forms `NotifyIcon`, no ordinary main window, a per-user named synchronization object, and named-pipe duplicate signaling. The loopback server defaults to numeric `127.0.0.1:48481`; provider endpoint validation also rejects non-loopback and non-HTTP values. Current-user startup registration uses the standard `Run` key and requires no elevation.
