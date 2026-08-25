# Auraline Windows Beta Testing

The first beta version is `0.1.0-beta.1`. Host and plugin versions move together; later prerelease fixes increment the `beta.N` suffix. Stable `1.0.0` is not implied by earlier internal milestone labels.

Public beta distribution requires an InfoPanel build containing the generic plugin image consumer-dimension capability used by InfoPanel.Auraline. That prerequisite is an external release gate and is not currently present in a public upstream InfoPanel build.

Build the combined framework-dependent Windows x64 package from repository root:

```powershell
.\build\Build-Beta.ps1
```

The resulting ignored `dist/Auraline-0.1.0-beta.1-win-x64.zip` contains separate `Host` and `InfoPanel.Plugin/InfoPanel.Auraline` trees, tester instructions, and per-file SHA-256 checksums. The plugin remains exactly four files and excludes InfoPanel-owned contracts and Skia assemblies.

Before reporting a problem, open Host **Diagnostics**, run the isolated self-test, copy the Markdown summary, and export the redacted ZIP. Exports are user-initiated and local. They contain current version/configuration/provider/source/session state and up to seven bounded recent log files; they contain no audio, waveform samples, or frame pixels. Obvious usernames, profile paths, hostnames, and secret-like values are redacted, while useful technical names may remain.

Clean-machine validation requires Windows x64, .NET 8 Desktop Runtime x64, compatible Resonance Signal protocol v1, and the matching InfoPanel prerequisite. This repository does not claim that another machine has been tested until that acceptance is performed.

Use [the beta report template](beta-report-template.md). Installation, rollback, update, and full limitations are included inside the package README.
