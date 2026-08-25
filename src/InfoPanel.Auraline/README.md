# InfoPanel.Auraline

M1 provides a buildable .NET 8 class-library scaffold and a reference to Auraline.Contracts. It intentionally has no functional InfoPanel runtime integration.

The future thin plugin will bind to a stable profile ID, negotiate a render session, transport Host-rendered frames, and display them in InfoPanel. It must not capture audio, process waveform samples, or own rendering/product logic.
