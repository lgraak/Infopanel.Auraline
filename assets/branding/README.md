# Auraline branding assets

`auraline-banner.png` is the approved wide artwork for the public repository
README. Preserve the supplied `2172x724` PNG unchanged; it is separate from the
canonical product mark and is not an application or tray icon source.

`auraline-mark.png` is the canonical Auraline product artwork supplied and owned
by the project. Preserve it at its original `1254x1254` resolution and do not
overwrite it while generating derivatives.

Run `build/Build-Branding.ps1` on Windows to reproduce the committed assets under
`generated/`. The tray treatment keeps the central angular A and waveform band
while clipping away most outer circular detail so the silhouette remains legible
at Windows tray sizes. Application-icon frames use that treatment through 48 px
and the full mark from 64 px upward.

InfoPanel's current local `PluginInfo.ini` parser supports only name,
description, author, version, and website metadata. It has no plugin-icon field,
so the four-file InfoPanel plugin package remains unchanged.
