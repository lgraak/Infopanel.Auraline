# InfoPanel.Auraline

InfoPanel.Auraline is a planned Windows visualization platform for turning audio data into reusable rendered visuals. The first integration will display a waveform in [InfoPanel](https://github.com/lanceseidman/InfoPanel), but the rendering host is intentionally designed as its own product rather than as plugin-only code.

This repository currently contains the M0 architecture and repository skeleton only. It does **not** yet contain a working host, renderer, transport, configuration UI, or InfoPanel plugin.

## How Auraline fits with Resonance Signal

[Resonance Signal](https://github.com/lgraak/resonance-signal) is the audio-data provider. It owns audio capture, Windows device discovery, source identity, and the provider protocol. Auraline consumes that protocol; it does not capture audio or choose Windows audio devices itself.

Auraline is split into two main runtime components:

- **Auraline Host** will connect to providers, manage sources and profiles, render frames, expose localhost configuration/control, and own render-session transport. It will launch independently with Windows as a per-user tray application.
- **InfoPanel.Auraline** will launch with InfoPanel and remain a thin display/transport adapter. It will select a profile and present frames produced by the Host rather than processing waveform samples itself.

Shared messages and models that genuinely need to cross that boundary will live in **Auraline.Contracts**.

## First proof of concept

The first functional proof is intentionally narrow:

1. Auraline Host connects to a local Resonance Signal provider using logical `default-playback` source intent.
2. The Host renders a combined mono, centered oscilloscope-style waveform at dynamic dimensions.
3. A render session publishes local frames for a thin InfoPanel.Auraline consumer.
4. InfoPanel displays the resulting waveform end to end.

That proof arrives in M4. The current M0 milestone establishes only the durable plan and boundaries for later implementation.

## Architecture at a glance

```text
Resonance Signal
    ↓
Auraline Host
    ↓
profiles / source groups / render engine
    ↓
render-session transport
    ↓
InfoPanel.Auraline
    ↓
InfoPanel
```

Development initially targets Windows and .NET 8. The intended implementation uses C#, SkiaSharp, ASP.NET Core with a lightweight server-rendered UI, Serilog, and shared contracts where appropriate. The first visualization is a waveform; other renderers and generic or network consumers are deferred until after the proof of concept.

The expected eventual installation layout is:

- Application binaries: `C:\Program Files\Auraline\`
- Per-user configuration, state, and logs: `%LOCALAPPDATA%\Auraline\`

## Repository layout

```text
docs/                       Architecture, roadmap, decisions, handoffs, and standards
src/Auraline.Host/          Future independent rendering and configuration host
src/Auraline.Contracts/     Future shared cross-component contracts
src/InfoPanel.Auraline/     Future thin InfoPanel adapter
tests/                      Future automated test projects
```

## Development status

There is nothing to build or run at M0 because no .NET solution or project has been created yet. Build and test instructions will be added with the first executable milestone rather than claiming a workflow that does not exist.

Start with:

- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Architecture decisions](docs/decisions/README.md)
- [Project prompt standard](docs/standards/ai-project-prompt-standard-v1.md)
- [Project handoff standard](docs/standards/ai-project-handoff-standard-v1.md)
- [Handoff history](docs/handoffs/)
