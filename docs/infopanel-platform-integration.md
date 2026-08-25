# InfoPanel Platform Integration Audit and M4 Addendum

Date: 2026-08-25
Status: pre-M3 audit completed; M4 Windows authority reconciled and directly accepted

## M4 Windows authority addendum

M4 reverified the installed application and current Windows source before implementation. The applicable Windows authority is now the local `habibrehmansg/infopanel` `1.4.x` checkout at implementation checkpoint `d7021153e31809abba3f4399adacec9c34e4c610` (current handoff-only HEAD `8ef8692cbd0de54db3377380b6722df1da3eae1a`). It adds the backwards-compatible optional `IPluginImageConsumerAware` contract and per-consumer final pixel demands. That prerequisite is committed locally but is not present on `origin/1.4.x` or in installed InfoPanel `1.4.0-preview.2.43`. The older Windows findings below remain historical pre-M3 evidence rather than the current implementation contract.

The current 1.4.x plugin path now provides:

- isolated plugin-host processes with automatic sidecar configuration;
- `IPluginImageProvider`, host-owned `IPluginImageWriter`, and `plugin-image://{pluginId}/{imageId}` bindings;
- double-buffered Skia image mappings and producer-owned resize notifications;
- replacement consumer-demand snapshots containing image ID, independent consumer ID, and final scaled width/height; and
- replay across plugin-host restart while legacy plugins retain the existing scaling fallback.

M4 therefore uses the existing writer contract directly. `InfoPanel.Auraline/Core` owns portable Host/profile/session/lease state, `Platform/Windows` reads Auraline shared memory, and `Adapters` copies validated RGBA8888-premultiplied pixels into InfoPanel's inactive writer bitmap before invalidation. No upstream InfoPanel source was modified by M4.

One InfoPanel image ID has one producer buffer. For exact simultaneous different-size proof, Auraline exposes `waveform` and `waveform-2`. Multiple consumers of one output select its largest current demand and use InfoPanel's existing scaling fallback; the two output IDs can hold distinct Host sessions and dimensions concurrently.

Direct M4 acceptance on 2026-08-25 used that exact local prerequisite build. InfoPanel loaded both outputs, supplied `600x150` and `300x300` demands, displayed Active and Idle Host-rendered frames with the expected transparency and color, consumed about 30 FPS and about 59 FPS, released both leases on plugin unload, restored them on reload, showed the explicit unavailable surface during a controlled Host outage, and recovered both new sessions without restarting InfoPanel. The dated M4 handoff separates this local runtime proof from public-build availability.

## 1) Scope and authority

This audit is intentionally documentation-only and compares the current Windows and Linux InfoPanel plugin/image stacks before InfoPanel.Auraline M3 begins implementation.

Reference points:

- Auraline repository: `D:\Aeons\Git\Infopanel.Auraline` (HEAD `c327f9d79ce1541b43439db1d1a0f93ac573ccf5`, branch `main`).
- Windows InfoPanel authority clone: `https://github.com/emaspa/infopanel-1` at branch `1.3.x`, revision `9433ec8cf1adb8c846ad47f7a5871d515faf97dc`.
- Linux InfoPanel authority clone: `https://github.com/emaspa/InfoPanel-linux` at branch `main`, revision `0ad91117a4c009c820cb9998160fb2e1378b6d07`.
- Sources inspected for Windows: `InfoPanel.Plugins/IPlugin.cs`, `InfoPanel.Plugins.Loader/PluginWrapper.cs`, `InfoPanel/Monitors/PluginMonitor.cs`, `InfoPanel.Models/Displays/ModelExtensions`.
- Sources inspected for Linux: `src/InfoPanel.Plugins.Graphics`, `src/InfoPanel.AudioSpectrum/AudioSpectrumPlugin.cs`, `src/InfoPanel.Sensors/PluginMonitor.cs`, `src/InfoPanel.App/AppHost.cs`, `src/InfoPanel.App/Views/DisplayWindow.axaml.cs`, `src/InfoPanel.Rendering`.

## 2) Audit objective checklist outcomes

- Confirm plugin contracts, image provider/writer ownership, lifecycle, demand behavior, and frame timing on both platforms.
- Compare plugin-image identity/path conventions and dynamic sizing behavior.
- Recommend M3 transport and plugin split boundaries before implementation.
- Keep recommendations evidence-backed and explicit where platform behavior is unknown.

## 3) Windows plugin model snapshot (current revision)

1. Plugin contract is intentionally small and host-managed.
2. `IPlugin` exposes metadata and control (`Id`, `Name`, `Description`, `ConfigFilePath`, `UpdateInterval`, `Initialize`, `Load`, `Update`, `UpdateAsync`, `Close`).
3. `PluginWrapper` manages wrapper lifecycle and runs `UpdateAsync` on a recurring internal task when `UpdateInterval > 0`.
4. `PluginWrapper` invokes manual `Update()` only when no interval-based worker is running.
5. Plugin modules are started on monitor startup and updated in a fixed periodic main loop.
6. No public plugin-image writer/provider interface is observed in the Windows code paths inspected.
7. No built-in `plugin-image://` scheme and no Linux-style image descriptor contract are present in the inspected Windows plugin pipeline.
8. No Windows plugin image provider path was found in the inspected code paths; plugin visuals are represented through `ImageDisplayItem` plus host image loading and rendering behavior (file/url/rtsp/sizing/caching variants).
9. Dynamic frame versioning, host-demand gating, and explicit dimension negotiation are not visible at the plugin contract level in the inspected Windows branch.
10. Image buffers for plugins appear to be resolved through host-side image display/decoding behavior rather than a plugin-provided image-writer interface.

## 4) Linux plugin model snapshot (current revision)

1. Plugin graphics are explicit and host-consumable.
2. `InfoPanel.Plugins.Graphics` defines `PluginImageDescriptor(id, name, width, height)`, `IPluginImageProvider`, and `IPluginImageWriter` (`Bitmap`, `Width`, `Height`, `Invalidate`, `Resize`, `Dispose`).
3. `AudioSpectrumPlugin` implements `IPluginImageProvider` and publishes at least `"spectrum"` with explicit descriptor sizing.
4. Provider pushes frame-ready signals by mutating a writer-backed bitmap and calling `Invalidate()`.
5. Linux `PluginWrapper` includes an injected `UpdateGate`; both async and manual update paths check the gate.
6. `PluginMonitor` materializes plugin-demand state (`SensorDemand`) and uses it to gate both demand-driven and periodic plugin work.
7. Idle plugin management includes an inactivity timer and automatic stop/start behavior for interval-driven plugins.
8. `plugin-image://{pluginId}/{imageId}` URIs are surfaced in sensor containers.
9. `AppHost` wires `PluginImageSource` to resolve `{pluginId}/{imageId}` image refs into a writer-backed map (`IMAGEWRITERS`).
10. Linux rendering pipeline includes versioned bitmap caches and stale/heartbeat-aware shared-frame consumers.

## 5) Concern comparison

| Concern | Windows InfoPanel | Linux InfoPanel | Compatible? | Auraline implication |
| --- | --- | --- | --- | --- |
| Plugin base interface | `IPlugin` (`Update`, `UpdateAsync`, lifecycle methods) | `IPlugin` plus graphics-specific graphics contracts (`IPluginImageProvider`) | Partial | Base lifecycle concepts map; graphics contracts require adapter or shim for parity |
| Plugin image provider | No analogous provider interface observed | `IPluginImageProvider` + descriptors/writer contract | No (v1) | Implement adapter layer in InfoPanel.Auraline so plugin image emission is not Windows-only coupling |
| Image descriptor | `image` metadata exists, not `IPluginImageProvider` descriptors | `PluginImageDescriptor` includes `id/name/width/height` | No | Use normalized transport-facing descriptor model in Auraline shared code; translate per platform |
| Image identity | No `pluginId/imageId` scheme in inspected contract | `plugin-image://{pluginId}/{imageId}` | No (currently) | Keep URI scheme for Linux compatibility and introduce equivalent mapping on Windows |
| Pixel format expectations | Not explicitly contract-based in inspected plugin model | Writer-based bitmap pipeline (bitmap ownership is explicit) | Unknown | Convert/normalize in platform adapters; avoid baking Skia frame pixel format into plugin contracts |
| Buffer ownership | Appears host-owned image loading path for standard items; plugin buffer ownership not explicit in contracts | Explicit writer ownership by plugin provider side (`Bitmap`/invalidate) with host resolver | No | Design Auraline boundary as frame consumer only; host transport owns frame contract |
| Dynamic dimensions | Width/height is part of display item metadata, not a plugin-image contract | Image descriptors expose width/height; writer supports resize and image-size negotiation | Partial | Keep Auraline dimension keys at render-session level and adapt to writer-specific resizing behavior |
| Frame/version signaling | No explicit plugin-image version counter in inspected contract | Writer invalidate/version behavior plus shared-frame version cache | No | Transport boundary should rely on renderer-produced frame versioning, not plugin-image contract |
| Refresh/FPS | Interval/task-based update loop; manual updates on demand window | Demand-gated with periodic updates; cache-aware consumer behavior; 33ms plugin cadence in `AudioSpectrum` | Partial | Keep Auraline transport decoupled from plugin scheduler semantics |
| Config lifecycle | Plugin config is plugin-specific; image settings not part of shared transport contract | `AudioSpectrum` uses image config-driven sizing and provider integration | Partial | Maintain minimal Auraline plugin config; keep rendering intent in Host UI |
| Plugin reload | Monitors maintain wrapper lifecycle and close/restart behavior | Explicit reload and idle-stop/start paths with teardown/setup of image providers | Partial | Auraline must tolerate re-init and reconnect events at session level |
| Transport responsibility | No shared-memory frame transport in plugin contract | No transport contract in plugin model; images arrive through writer->cache pipeline | Not shared | Keep transport entirely outside plugin contracts in both hosts |
| Threading expectations | Wrapper/work-loop model and periodic background update scheduling | Dedicated background update and explicit demand gate; lock-conscious shared-frame cache/consumer pipeline | Partial | Keep thread-safety inside plugin adapter + Auraline transport |

## 6) Determinants for Auraline plugin strategy

### Most likely shape

Use a shared InfoPanel.Auraline core for Host protocol consumption, render-session orchestration, and frame publishing logic, with narrow platform adapters for InfoPanel interactions.

### Why this is still plausible

1. Both codebases share the same project intent and general plugin lifecycle structure.
2. Core rendering ownership and transport logic is not platform-specific in principle.
3. Divergence is concentrated in image-provider contracts and scheduler semantics on the plugin edge.

### Why not split yet

1. No evidence requires immediate split of renderer/protocol logic.
2. Existing Windows-first boundaries already isolate OS-specific host shell logic cleanly.
3. A bounded shared code path should reduce duplication and keep M3 decisions testable.

## 7) M3 transport boundary recommendation

Evidence supports keeping the renderer/transport contract at the Auraline layer and not baking shared-memory semantics into plugin contracts.

Recommended abstraction:

1. `IAuralineFrameTransport` in shared Auraline code, with `IAuralineFramePublisher` and `IAuralineFrameConsumer`-style split.
2. `WindowsSharedMemoryFrameTransport` as the first implementation.
3. Platform-local adapters where plugin-specific image handling, URI mapping, and writer details are translated.
4. Plugin output contract to stay "rendered frame bytes + framing metadata" only.

This preserves Linux portability even though Windows shared memory may be the first concrete transport.

## 8) Dynamic sizing and frame-rate comparison

1. Dynamic frame dimensions are explicit in `plugin-image` metadata on Linux.
2. Windows currently lacks a comparable plugin-image descriptor system in the inspected revision, so adaptation is needed if Auraline pushes width/height from sessions through plugin image providers.
3. Linux already includes cache-by-size and versioned consume paths; this is compatible with Auraline session keys that include dimensions.
4. Linux plugin cadence evidence includes 30/33ms behavior in `AudioSpectrum` plus demand-gated polling/idle logic.
5. Windows evidence indicates plugin update loops exist but no equivalent explicit plugin-image dimension metadata; if no shim is added, shared sizing behavior is likely to happen via host image decode/render stage.

## 9) Lifecycle and demand alignment

1. Linux explicitly models demand and idleness for plugins and can stop/start interval updates automatically.
2. Windows has periodic updates and monitor lifecycle hooks but no explicit sensor-demand gate in inspected code paths.
3. Auraline should continue to support:
   - lazy session creation,
   - grace-window behavior after consumer departure,
   - reconnect/re-init tolerance,
   - no-hard-fail when plugins/requires disappear temporarily.
4. Linux's idle-stop behavior suggests demand-driven work is beneficial and should be mirrored at the Auraline transport/session boundary (consumer presence + grace policy).

## 10) Configuration and deployment implications

1. Auraline should keep configuration in Host web UI/config files, not plugin-heavy provider UI.
2. Minimal plugin settings should remain:
   - Host endpoint/connection intent,
   - selected profile identifier,
   - transport enablement and debugging controls.
3. Windows and Linux plugin dependency sets should stay separate to avoid forcing shared-memory semantics through Linux plugin contracts.

## 11) Pixel format contract and conversion

1. M2 currently emits render output through the established Skia frame contract.
2. Windows plugin image contract evidence does not define a frame pixel format contract equivalent to Linux writer semantics.
3. M3 should keep conversion and buffer layout responsibility in the Auraline transport adapter, with explicit conversion tests at transport boundaries, not inside plugin contracts.

## 12) Unresolved questions for runtime validation

1. Exact Windows InfoPanel pathway required to inject Host frames while preserving thin plugin boundaries.
2. Whether Windows InfoPanel can consume a `plugin-image://...` equivalent without introducing new transport leakage.
3. Whether Windows plugin loader can safely expose a shim writer interface without broad refactor.
4. Whether Auraline needs a Windows-only image staging step (temporary bitmap cache vs direct pixel transfer) before transport to InfoPanel.
5. Final frame metadata expectations between Auraline consumer and InfoPanel renderer (timestamping, heartbeat, and stale-frame behavior).

## 13) Decision output from this audit

1. Evidence supports keeping transport abstract and allowing `shared-memory` to be a first Windows local implementation.
2. Evidence supports a shared Auraline core + platform adapters, not immediate core split.
3. The next safe milestone action is an M3 transport and render-session implementation that binds to explicit session keys and adapters, with Windows and Linux consumption paths verified separately.
