# Tests

`Auraline.Host.Tests` covers configuration bootstrap/load/save/validation, malformed-file preservation, provider transitions/reconnect/refresh/cancellation, retry policy, Windows single-instance signaling, tray resources, Windows path layout and startup-registration adapters, loopback request protection, Resonance Signal v1 parsing, waveform processing/rendering, health serialization, and contract compatibility.

M3 coverage includes lazy render-session creation, compatible sharing, dimension separation, leases, heartbeat/detach/stale expiry, 15-second grace/reattach/teardown, deterministic idle LRU eviction, active-cap rejection, 30/60 FPS acceptance, single-scheduler ownership, shutdown, HTTP API status behavior, shared-memory layout/version/geometry, monotonic sequence, multiple readers, concurrent torn-frame rejection, and source-level portability guards.

`Auraline.TransportProbe` is a separate executable rather than an in-process unit test. With Host running, it negotiates through `/api/v1`, opens the returned Windows mapping, validates the RGBA8888-premultiplied geometry, observes sequence and pixel advancement, heartbeats for longer runs, and detaches. Pass `--abrupt` to leave lease cleanup to expiry. It is not the InfoPanel plugin.
