# InfoPanel compile references

These binaries are deliberately not committed. Populate this directory from the
matching InfoPanel 1.4.x build that includes `IPluginImageConsumerAware`:

- `InfoPanel.Plugins.dll`
- `InfoPanel.Plugins.Graphics.dll`
- `SkiaSharp.dll`
- `libSkiaSharp.dll`

The first two assemblies must come from the local InfoPanel prerequisite
checkpoint recorded in the M4 handoff. The Skia managed and native assemblies
must match that host build. Production references use `Private=false`; InfoPanel
supplies them at runtime and they are not copied into the plugin package. The
native assembly is copied only into the test output so the pixel adapter can be
verified outside the InfoPanel process.
