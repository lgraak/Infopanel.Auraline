using System.Net;
using System.Text;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;

namespace Auraline.Host.Web;

public static class UiRenderer
{
    private static readonly (string Path, string Label)[] Navigation =
    [
        ("/", "Dashboard"), ("/providers", "Providers"), ("/sources", "Sources"),
        ("/source-groups", "Source Groups"), ("/profiles", "Profiles"), ("/diagnostics", "Diagnostics")
    ];

    public static string Dashboard(HealthContract health, HostConfiguration configuration, StartupRegistrationResult? startupResult) =>
        Page("Dashboard", configuration.Host.Theme, $"""
        <h1>Dashboard</h1>
        <div class="cards"><section><h2>Host</h2><strong>{E(health.HostStatus)}</strong><p>Version {E(health.HostVersion)}</p></section>
        <section><h2>Providers</h2><strong>{health.ProviderSummary.Connected}/{health.ProviderSummary.Enabled} connected</strong><p>{health.ProviderSummary.Configured} configured</p></section></div>
        {(health.ConfigurationError is null ? "" : $"<p class=error>{E(health.ConfigurationError)}</p>")}
        {(startupResult is { Succeeded: false } ? $"<p class=error>Windows startup registration failed: {E(startupResult.Error ?? "Unknown error")}</p>" : "")}
        <h2>Configured providers</h2>{ProviderTable(health.Providers)}
        {WaveformCard(health.Waveform)}
        <h2>Host preferences</h2>
        <form method="post" action="/settings"><label><input type="checkbox" name="startWithWindows" value="true" {(configuration.Host.StartWithWindows ? "checked" : "")}> Start Auraline with Windows</label>
        <label>Theme <select name="theme">{ThemeOptions(configuration.Host.Theme)}</select></label><button>Save settings</button></form>
        """);

    public static string Providers(IReadOnlyList<ProviderStatus> providers, string theme)
    {
        var rows = new StringBuilder();
        foreach (var provider in providers)
        {
            rows.Append($"<tr><td>{E(provider.FriendlyName)}</td><td><code>{E(provider.Endpoint)}</code></td><td>{provider.Enabled}</td><td><span class=state>{E(provider.State.ToString())}</span></td><td>{E(provider.LastError ?? "—")}</td><td>");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/toggle\"><input type=hidden name=enabled value=\"{(!provider.Enabled).ToString().ToLowerInvariant()}\"><button>{(provider.Enabled ? "Disable" : "Enable")}</button></form> ");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/reconnect\"><button {(provider.Enabled ? "" : "disabled")}>Reconnect</button></form> ");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/refresh\"><button {(provider.Enabled && provider.State == ProviderLifecycleState.Connected ? "" : "disabled")}>Refresh Sources</button></form></td></tr>");
        }
        return Page("Providers", theme, $"<h1>Providers</h1><p>Resonance Signal connections are independent and retry automatically while enabled.</p><div class=table-wrap><table><thead><tr><th>Name</th><th>Endpoint</th><th>Enabled</th><th>State</th><th>Last reason</th><th>Actions</th></tr></thead><tbody>{rows}</tbody></table></div>");
    }

    public static string Sources(IReadOnlyList<ProviderStatus> providers, string theme)
    {
        var rows = new StringBuilder();
        foreach (var provider in providers)
            foreach (var source in provider.Sources)
                rows.Append($"<tr><td>{E(source.DisplayName ?? "Unnamed source")}</td><td>{E(source.Availability)}</td><td>{E(source.Kind)}</td><td>{E(provider.FriendlyName)}</td><td>{source.ChannelCount?.ToString() ?? "—"}</td><td>{source.SampleRateHz?.ToString() ?? "—"}</td><td><details><summary>Details</summary><code>{E(source.SourceId)}</code><br>Default playback: {source.DefaultPlayback}<br>Products: {E(string.Join(", ", source.SupportedProducts))}</details></td></tr>");
        if (rows.Length == 0) rows.Append("<tr><td colspan=7>No sources have been discovered yet.</td></tr>");
        return Page("Sources", theme, $"<h1>Sources</h1><p>Source IDs and discovery revisions are provider-owned opaque values. Format details arrive with waveform streams in M2.</p><div class=table-wrap><table><thead><tr><th>Name</th><th>State</th><th>Type</th><th>Provider</th><th>Channels</th><th>Sample rate</th><th>Metadata</th></tr></thead><tbody>{rows}</tbody></table></div>");
    }

    public static string Placeholder(string title, string milestone, string theme) =>
        Page(title, theme, $"<h1>{E(title)}</h1><section><p>{E(title)} functionality arrives in {E(milestone)}. This navigation entry establishes the intended product shape only.</p></section>");

    public static string Diagnostics(HealthContract health, string theme) =>
        Page("Diagnostics", theme, $"<h1>Diagnostics</h1><p>Host version: <strong>{E(health.HostVersion)}</strong></p><p>Health: <strong>{E(health.HostStatus)}</strong></p><p><a href=/health>Machine-readable health API</a></p>{WaveformCard(health.Waveform)}{RenderSessionsCard(health.RenderSessions)}<p>Detailed diagnostics and export arrive in M6.</p>");

    private static string ProviderTable(IReadOnlyList<ProviderHealthContract> providers) =>
        "<table><thead><tr><th>Name</th><th>Enabled</th><th>State</th><th>Sources</th><th>Last reason</th></tr></thead><tbody>" +
        string.Join("", providers.Select(p => $"<tr><td>{E(p.FriendlyName)}</td><td>{p.Enabled}</td><td>{E(p.State)}</td><td>{p.SourceCount}</td><td>{E(p.LastError ?? "—")}</td></tr>")) + "</tbody></table>";

    private static string WaveformCard(Auraline.Host.Waveform.WaveformEngineHealth? health)
    {
        if (health is null) return "<section><h2>Waveform Engine</h2><p>Waveform engine has not started yet.</p></section>";

        return $"<section><h2>Waveform Engine</h2><ul>" +
               $"<li>State: <strong>{E(health.VisualState)}</strong></li>" +
               $"<li>Logical source intent: <code>{E(health.LogicalSourceIntent)}</code></li>" +
               $"<li>Stream: <code>{E(health.StreamId ?? "—")}</code></li>" +
               $"<li>Source: <code>{E(health.SourceId ?? "—")}</code></li>" +
               $"<li>Channels: {E(health.ChannelCount?.ToString() ?? "—")}</li>" +
               $"<li>Sample rate: {E(health.SampleRateHz?.ToString() ?? "—")} Hz</li>" +
               $"<li>Reconnect attempts: {health.ReconnectAttempts}</li>" +
               $"<li>Stream starts: {health.StreamStarts} / stops: {health.StreamStops}</li>" +
               $"<li>Frames: {health.WaveformFrames} received / {health.MalformedFrames} malformed</li>" +
               $"<li>Rendered frames: {health.RenderedFrames}</li>" +
               $"<li>Latest frame age: {FormatOptionalDoubleMs(health.LatestFrameAgeMs)}</li>" +
               $"<li>Last render duration: {FormatOptionalDoubleMs(health.LastRenderDurationMs)}</li>" +
               $"<li>Average render duration: {FormatOptionalDoubleMs(health.AverageRenderDurationMs)}</li>" +
               $"<li>Retry state: {E(health.RetryState)}</li>" +
               $"</ul><figure class=waveform-preview><img src=\"/waveform/preview.png?frame={health.RenderedFrames}\" width=320 height=120 alt=\"Latest waveform renderer frame\"><figcaption>Latest frame from the real M2 renderer. Refresh this page to update the snapshot.</figcaption></figure></section>";
    }

    private static string RenderSessionsCard(Auraline.Host.RenderSessions.RenderSessionDiagnostics? diagnostics)
    {
        if (diagnostics is null) return "<section><h2>Render Sessions</h2><p>Render-session manager has not started yet.</p></section>";
        var rows = string.Join("", diagnostics.Sessions.Select(session =>
            $"<tr><td><code>{E(session.SessionId)}</code></td><td><code>{E(session.ProfileId)}</code></td><td>{session.Width}×{session.Height}</td><td>{session.TargetFps}</td><td>{session.ActualFps:F1}</td><td>{session.ConsumerCount}</td><td>{E(session.State)}</td><td>{session.PublishedSequence}</td><td>{session.AllocationSize}</td></tr>"));
        return $"<section><h2>Render Sessions</h2><p>{diagnostics.ActiveSessionCount} sessions, {diagnostics.TotalConsumerLeases} leases, cap {diagnostics.SessionCap}. Created {diagnostics.SessionCreationCount}; torn down {diagnostics.TeardownCount}; evicted {diagnostics.EvictionCount}; rejected {diagnostics.RejectedSessionCount}.</p><div class=table-wrap><table><thead><tr><th>Session</th><th>Profile</th><th>Size</th><th>Target FPS</th><th>Actual FPS</th><th>Consumers</th><th>State</th><th>Sequence</th><th>Bytes</th></tr></thead><tbody>{rows}</tbody></table></div></section>";
    }

    private static string FormatOptionalDoubleMs(double? value) => value is null ? "—" : $"{value:F1} ms";

    private static string ThemeOptions(string selected) => string.Join("", new[] { "system", "light", "dark" }.Select(theme => $"<option value={theme} {(theme == selected ? "selected" : "")}>{char.ToUpperInvariant(theme[0]) + theme[1..]}</option>"));

    private static string Page(string title, string theme, string body) => $$$"""
    <!doctype html><html lang="en" data-theme="{{{E(theme)}}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{{E(title)}}} · Auraline</title>
    <style>
    :root{color-scheme:light dark;--bg:#f4f5f8;--panel:#fff;--text:#1c2030;--muted:#646b7c;--accent:#7656d6;--line:#d9dce5;--error:#b42318}@media(prefers-color-scheme:dark){:root{--bg:#11131a;--panel:#1b1e28;--text:#f2f3f7;--muted:#aeb4c4;--line:#343949;--error:#ff8a80}}html[data-theme=light]{color-scheme:light;--bg:#f4f5f8;--panel:#fff;--text:#1c2030;--muted:#646b7c;--line:#d9dce5;--error:#b42318}html[data-theme=dark]{color-scheme:dark;--bg:#11131a;--panel:#1b1e28;--text:#f2f3f7;--muted:#aeb4c4;--line:#343949;--error:#ff8a80}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 system-ui,sans-serif}nav{background:#171927;padding:13px 22px;display:flex;gap:20px;flex-wrap:wrap}nav strong{color:white;margin-right:12px}nav a{color:#d9d3ff;text-decoration:none}main{max-width:1100px;margin:28px auto;padding:0 20px}section,.table-wrap,table{background:var(--panel)}section{border:1px solid var(--line);border-radius:10px;padding:18px}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:15px;margin-bottom:24px}table{width:100%;border-collapse:collapse}th,td{padding:11px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top}.table-wrap{overflow:auto;border:1px solid var(--line);border-radius:10px}code{word-break:break-all}form{display:flex;gap:16px;align-items:center;flex-wrap:wrap}form.inline{display:inline}button,select{padding:7px 10px}.error{color:var(--error);font-weight:600}.state{font-weight:600}p{color:var(--muted)}.waveform-preview{margin:18px 0 0}.waveform-preview img{display:block;max-width:100%;height:auto;border:1px solid var(--line);border-radius:6px;background:repeating-conic-gradient(#222 0 25%,#292929 0 50%) 0/16px 16px}.waveform-preview figcaption{margin-top:7px;color:var(--muted);font-size:13px}
    </style></head><body><nav><strong>Auraline</strong>{{{string.Join("", Navigation.Select(item => $"<a href=\"{item.Path}\">{item.Label}</a>"))}}}</nav><main>{{{body}}}</main></body></html>
    """;

    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string U(string value) => Uri.EscapeDataString(value);
}
