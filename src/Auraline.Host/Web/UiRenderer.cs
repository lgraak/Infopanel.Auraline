using System.Net;
using System.Text;
using Auraline.Host.Configuration;
using Auraline.Host.Diagnostics;
using Auraline.Host.Providers;
using Auraline.Host.RenderSessions;

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
        <section><h2>Providers</h2><strong>{health.ProviderSummary.Connected}/{health.ProviderSummary.Enabled} connected</strong><p>{health.ProviderSummary.Configured} configured · {health.Providers.Sum(item => item.SourceCount)} discovered sources</p></section>
        <section><h2>Configuration</h2><strong>{health.ProductConfiguration?.ProfileCount ?? 0} profiles</strong><p>{health.ProductConfiguration?.SourceGroupCount ?? 0} source groups</p></section>
        <section><h2>Consumers</h2><strong>{health.RenderSessions?.TotalConsumerLeases ?? 0} active leases</strong><p>{health.RenderSessions?.ActiveSessionCount ?? 0} render sessions</p></section></div>
        <p>Default profile: <code>{E(health.ProductConfiguration?.DefaultProfileId ?? "—")}</code></p>
        {(health.ConfigurationError is null ? "" : $"<p class=error>{E(health.ConfigurationError)}</p>")}
        {(startupResult is { Succeeded: false } ? $"<p class=error>Windows startup registration failed: {E(startupResult.Error ?? "Unknown error")}</p>" : "")}
        <h2>Configured providers</h2>{ProviderTable(health.Providers)}
        {WaveformCard(health.Waveform)}
        <h2>Host preferences</h2>
        <form method="post" action="/settings"><label><input type="checkbox" name="startWithWindows" value="true" {(configuration.Host.StartWithWindows ? "checked" : "")}> Start Auraline with Windows</label>
        <label>Theme <select name="theme">{ThemeOptions(configuration.Host.Theme)}</select></label><button>Save settings</button></form>
        """);

    public static string Providers(IReadOnlyList<ProviderStatus> providers, ProductConfigurationStore products, string theme, string? error = null)
    {
        var rows = new StringBuilder();
        foreach (var provider in providers)
        {
            var dependencies = products.GetProviderDependencies(provider.Id);
            rows.Append($"<tr><td><form class=stack method=post action=\"/providers/{U(provider.Id)}/save\"><input name=friendlyName value=\"{E(provider.FriendlyName)}\" required><input name=endpoint value=\"{E(provider.Endpoint)}\" required><label><input type=checkbox name=enabled {(provider.Enabled ? "checked" : "")}> Enabled</label><button>Save</button></form></td><td><code>{E(provider.Id)}</code></td><td><span class=state>{E(provider.State.ToString())}</span></td><td>{E(provider.LastError ?? "—")}</td><td>");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/toggle\"><input type=hidden name=enabled value=\"{(!provider.Enabled).ToString().ToLowerInvariant()}\"><button>{(provider.Enabled ? "Disable" : "Enable")}</button></form> ");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/reconnect\"><button {(provider.Enabled ? "" : "disabled")}>Reconnect</button></form> ");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/refresh\"><button {(provider.Enabled && provider.State == ProviderLifecycleState.Connected ? "" : "disabled")}>Refresh Sources</button></form> ");
            rows.Append($"<form class=inline method=post action=\"/providers/{U(provider.Id)}/delete\"><button class=danger {(dependencies.Count > 0 ? "disabled" : "")}>Delete</button></form>{(dependencies.Count == 0 ? "" : $"<p class=small>Referenced by {E(string.Join("; ", dependencies))}</p>")}</td></tr>");
        }
        return Page("Providers", theme, $"<h1>Providers</h1><p>Resonance Signal connections retry automatically while enabled. IDs remain stable after creation.</p>{Error(error)}<div class=table-wrap><table><thead><tr><th>Configuration</th><th>Stable ID</th><th>State</th><th>Last reason</th><th>Actions</th></tr></thead><tbody>{rows}</tbody></table></div><section><h2>Add provider</h2><form method=post action=/providers><label>Stable ID <input name=id pattern=\"[a-z0-9][a-z0-9-]*\" required></label><label>Name <input name=friendlyName required></label><label>Endpoint <input name=endpoint value=\"http://127.0.0.1:48480\" required></label><label><input type=checkbox name=enabled checked> Enabled</label><button>Add provider</button></form></section>");
    }

    public static string Sources(IReadOnlyList<ProviderStatus> providers, ProductConfigurationStore products, string theme)
    {
        var rows = new StringBuilder();
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers)
            foreach (var source in provider.Sources)
            {
                currentKeys.Add($"{provider.Id}\0{source.SourceId}");
                var fresh = provider.State == ProviderLifecycleState.Connected;
                rows.Append($"<tr><td>{E(source.DisplayName ?? "Unnamed source")}</td><td>{E(fresh ? source.Availability : $"stale/offline ({source.Availability})")}</td><td>{E(source.Kind)}</td><td>{E(provider.FriendlyName)}</td><td>{source.ChannelCount?.ToString() ?? "—"}</td><td>{source.SampleRateHz?.ToString() ?? "—"}</td><td><details><summary>Details</summary>Evidence: {(fresh ? "fresh discovery" : "retained runtime snapshot")}<br>Provider: <code>{E(provider.Id)}</code><br>Source: <code>{E(source.SourceId)}</code><br>Default playback: {source.DefaultPlayback}<br>Products: {E(string.Join(", ", source.SupportedProducts))}</details></td></tr>");
            }
        foreach (var source in products.SourceCatalog.Sources.Where(source => !currentKeys.Contains($"{source.ProviderId}\0{source.SourceId}")))
            rows.Append($"<tr><td>{E(source.DisplayName ?? "Unnamed source")}</td><td>stale/offline</td><td>{E(source.Kind)}</td><td><code>{E(source.ProviderId)}</code></td><td>{source.ChannelCount?.ToString() ?? "—"}</td><td>{source.SampleRateHz?.ToString() ?? "—"}</td><td><details><summary>Details</summary>Evidence: persisted last-known metadata from {E(source.ObservedAtUtc.ToString("O"))}<br>Source: <code>{E(source.SourceId)}</code><br>Last availability: {E(source.Availability)}</details></td></tr>");
        if (rows.Length == 0) rows.Append("<tr><td colspan=7>No sources have been discovered yet.</td></tr>");
        return Page("Sources", theme, $"<h1>Sources</h1><p>Source IDs are provider-owned opaque values. Stale metadata preserves configuration intent but is never presented as current availability.</p><div class=table-wrap><table><thead><tr><th>Name</th><th>State</th><th>Type</th><th>Provider</th><th>Channels</th><th>Sample rate</th><th>Metadata</th></tr></thead><tbody>{rows}</tbody></table></div>");
    }

    public static string SourceGroups(ProductConfigurationStore products, IReadOnlyList<ProviderStatus> providers, string theme, string? error = null)
    {
        var profiles = products.GetProfiles();
        var rows = string.Join("", products.GetGroups().Select(group =>
        {
            var status = products.ResolveGroup(group.Id, providers);
            var references = profiles.Count(profile => profile.SourceGroupId.Equals(group.Id, StringComparison.OrdinalIgnoreCase));
            var isDefault = products.Catalog.DefaultSourceGroupId.Equals(group.Id, StringComparison.OrdinalIgnoreCase);
            return $"<tr><td>{E(group.FriendlyName)} {(isDefault ? "<span class=badge>Default</span>" : "")}</td><td><code>{E(group.Id)}</code></td><td>{group.Members.Count}</td><td><span class=state>{E(status.Availability)}</span></td><td>{references}</td><td><a class=button href=\"/source-groups/{U(group.Id)}/edit\">Edit</a> <form class=inline method=post action=\"/source-groups/{U(group.Id)}/duplicate\"><button>Duplicate</button></form> <form class=inline method=post action=\"/source-groups/{U(group.Id)}/set-default\"><button {(isDefault ? "disabled" : "")}>Set default</button></form> <form class=inline method=post action=\"/source-groups/{U(group.Id)}/delete\"><button class=danger {(references > 0 || isDefault ? "disabled" : "")}>Delete</button></form></td></tr>";
        }));
        return Page("Source Groups", theme, $"<h1>Source Groups</h1><p>Groups preserve ordered source intent. Unavailable members remain configured; cross-provider groups are allowed by the model but M5 rendering supports only the local logical Default Playback member.</p>{Error(error)}<div class=table-wrap><table><thead><tr><th>Name</th><th>Stable ID</th><th>Members</th><th>Status</th><th>Profiles</th><th>Actions</th></tr></thead><tbody>{rows}</tbody></table></div><section><h2>Create group</h2><form method=post action=/source-groups><label>Name <input name=friendlyName required></label><label>Initial source {SourceOptions(products, providers, null, false)}</label><button>Create</button></form></section>");
    }

    public static string SourceGroupEditor(SourceGroupDefinition group, ProductConfigurationStore products, IReadOnlyList<ProviderStatus> providers, string theme, string? error = null)
    {
        var status = products.ResolveGroup(group.Id, providers);
        var memberRows = string.Join("", status.Members.Select((member, index) => $"<li><strong>{E(MemberLabel(member.Member, member.Source))}</strong> — {E(member.Resolution.ToString())}: {E(member.Reason)}<input type=hidden name=member value=\"{E(MemberKey(member.Member))}\"></li>"));
        return Page("Edit Source Group", theme, $"<p><a href=/source-groups>← Source Groups</a></p><h1>Edit {E(group.FriendlyName)}</h1>{Error(error)}<section><form class=stack method=post action=\"/source-groups/{U(group.Id)}/save\"><label>Name <input name=friendlyName value=\"{E(group.FriendlyName)}\" required></label><p>Current ordered members</p><ol>{memberRows}</ol><label>Add or replace members {SourceOptions(products, providers, group, true)}</label><p class=small>Hold Ctrl to select multiple sources. Saving selected values replaces the member list; leave the selection empty to retain the current members.</p><button>Save group</button><a class=button href=/source-groups>Cancel</a></form></section>");
    }

    public static string Profiles(ProductConfigurationStore products, RenderSessionManager sessions, IReadOnlyList<ProviderStatus> providers, string theme, string? error = null)
    {
        var groups = products.GetGroups();
        var diagnostics = sessions.GetDiagnostics();
        var rows = string.Join("", products.GetProfiles().Select(profile =>
        {
            var isDefault = products.Catalog.DefaultProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase);
            var inUse = diagnostics.Sessions.Any(item => item.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) && item.ConsumerCount > 0);
            var runtime = products.IsRuntimeSupported(profile) ? products.ResolveGroup(profile.SourceGroupId, providers).Availability : "unsupported-runtime";
            return $"<tr><td>{E(profile.FriendlyName)} {(isDefault ? "<span class=badge>Default</span>" : "")} {(inUse ? "<span class=badge>In use</span>" : "")}</td><td><code>{E(profile.Id)}</code></td><td>{E(profile.VisualizationType)}</td><td>{E(groups.First(item => item.Id.Equals(profile.SourceGroupId, StringComparison.OrdinalIgnoreCase)).FriendlyName)}</td><td>{E(runtime)}</td><td>r{profile.Revision}</td><td><a class=button href=\"/profiles/{U(profile.Id)}/edit\">Edit</a> <form class=inline method=post action=\"/profiles/{U(profile.Id)}/duplicate\"><button>Duplicate</button></form> <form class=inline method=post action=\"/profiles/{U(profile.Id)}/set-default\"><button {(isDefault ? "disabled" : "")}>Set default</button></form> <form class=inline method=post action=\"/profiles/{U(profile.Id)}/delete\"><button class=danger {(isDefault || inUse ? "disabled" : "")}>Delete</button></form></td></tr>";
        }));
        var groupOptions = string.Join("", groups.Select(group => $"<option value=\"{E(group.Id)}\">{E(group.FriendlyName)}</option>"));
        return Page("Profiles", theme, $"<h1>Profiles</h1><p>Stable IDs survive renames. Saving an in-use profile hot-applies its new revision; 60 FPS remains a bounded sanity mode.</p>{Error(error)}<div class=table-wrap><table><thead><tr><th>Name</th><th>Stable ID</th><th>Type</th><th>Source group</th><th>Runtime</th><th>Revision</th><th>Actions</th></tr></thead><tbody>{rows}</tbody></table></div><section><h2>Create profile</h2><form method=post action=/profiles><label>Name <input name=friendlyName required></label><label>Source group <select name=sourceGroupId>{groupOptions}</select></label><button>Create profile</button></form></section>");
    }

    public static string ProfileEditor(ProfileDefinition profile, IReadOnlyList<SourceGroupDefinition> groups, string theme, string? error = null)
    {
        var groupOptions = string.Join("", groups.Select(group => $"<option value=\"{E(group.Id)}\" {(group.Id.Equals(profile.SourceGroupId, StringComparison.OrdinalIgnoreCase) ? "selected" : "")}>{E(group.FriendlyName)}</option>"));
        return Page("Edit Profile", theme, $$$$$"""
        <p><a href=/profiles>← Profiles</a></p><h1>Edit {{{{{E(profile.FriendlyName)}}}}}</h1>{{{{{Error(error)}}}}}
        <div class=editor-grid><section><form id=profile-form class=stack method=post action="/profiles/{{{{{U(profile.Id)}}}}}/save">
        <label>Name <input id=friendlyName name=friendlyName value="{{{{{E(profile.FriendlyName)}}}}}" required></label>
        <label>Source group <select id=sourceGroupId name=sourceGroupId>{{{{{groupOptions}}}}}</select></label>
        <label>Waveform color <input id=color name=color type=color value="{{{{{E(profile.Waveform.Color)}}}}}"></label>
        <label>Scale <select id=scaleMode name=scaleMode><option value=automatic {{{{{(profile.Waveform.ScaleMode == WaveformScaleMode.Automatic ? "selected" : "")}}}}}>Automatic</option><option value=fixed {{{{{(profile.Waveform.ScaleMode == WaveformScaleMode.Fixed ? "selected" : "")}}}}}>Fixed</option></select></label>
        <label>Fixed scale <input id=fixedScale name=fixedScale type=number min=.05 max=10 step=.05 value="{{{{{profile.Waveform.FixedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}"></label>
        <label><input id=smoothingEnabled name=smoothingEnabled type=checkbox {{{{{(profile.Waveform.SmoothingEnabled ? "checked" : "")}}}}}> Smoothing</label>
        <label>Smoothing amount <input id=smoothingAmount name=smoothingAmount type=range min=0 max=1 step=.05 value="{{{{{profile.Waveform.SmoothingAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}"></label>
        <label>Target FPS <select id=targetFps name=targetFps><option value=30 {{{{{(profile.Waveform.TargetFps == 30 ? "selected" : "")}}}}}>30 FPS</option><option value=60 {{{{{(profile.Waveform.TargetFps == 60 ? "selected" : "")}}}}}>60 FPS (sanity mode)</option></select></label>
        <p class=small>Centered line, transparent background, automatic thickness, and Host-owned idle/unavailable visuals are fixed in M5.</p>
        <div><button>Save</button> <a class=button href=/profiles>Cancel</a></div></form></section>
        <section><h2>Live preview</h2><figure class=waveform-preview><img id=preview width=480 height=180 alt="Live profile preview"><figcaption id=previewStatus>Uses the actual Host waveform renderer. Only Save publishes these settings.</figcaption></figure></section></div>
        <script>
        const profileId={{{{{Js(profile.Id)}}}}};const revision={{{{{profile.Revision}}}}};let timer,objectUrl;
        function workingCopy(){return{schemaVersion:1,id:profileId,friendlyName:document.querySelector('#friendlyName').value,visualizationType:'waveform',sourceGroupId:document.querySelector('#sourceGroupId').value,revision:revision,waveform:{style:'centered-line',color:document.querySelector('#color').value,scaleMode:document.querySelector('#scaleMode').value,fixedScale:Number(document.querySelector('#fixedScale').value),smoothingEnabled:document.querySelector('#smoothingEnabled').checked,smoothingAmount:Number(document.querySelector('#smoothingAmount').value),targetFps:Number(document.querySelector('#targetFps').value),background:'transparent'}}}
        async function refresh(){try{const response=await fetch('/api/v1/profile-preview/render.png',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(workingCopy())});if(!response.ok){const value=await response.json();throw new Error(value.error||'Preview failed')}const blob=await response.blob();if(objectUrl)URL.revokeObjectURL(objectUrl);objectUrl=URL.createObjectURL(blob);document.querySelector('#preview').src=objectUrl;document.querySelector('#previewStatus').textContent='Live working-copy preview · not saved';}catch(error){document.querySelector('#previewStatus').textContent=error.message}}
        document.querySelector('#profile-form').addEventListener('input',()=>{clearTimeout(timer);timer=setTimeout(refresh,150)});refresh();setInterval(refresh,1000);
        </script>
        """);
    }

    public static string Placeholder(string title, string milestone, string theme) =>
        Page(title, theme, $"<h1>{E(title)}</h1><section><p>{E(title)} functionality arrives in {E(milestone)}. This navigation entry establishes the intended product shape only.</p></section>");

    public static string Diagnostics(DiagnosticsSnapshot snapshot, string theme)
    {
        var health = snapshot.Health;
        var providerRows = string.Join("", snapshot.Providers.Select(provider => $"<tr><td>{E(provider.FriendlyName)}</td><td><code>{E(provider.Endpoint)}</code></td><td>{E(provider.State.ToString())}</td><td>{provider.Sources.Count}</td><td>{provider.ReconnectCount}</td><td>{(provider.RetryDelayMs is null ? "—" : $"{provider.RetryDelayMs:F0} ms")}</td><td>{E(provider.LastError ?? "—")}</td></tr>"));
        var sourceRows = string.Join("", snapshot.Providers.SelectMany(provider => provider.Sources).Select(source => $"<tr><td>{E(source.DisplayName ?? source.SourceId)}</td><td>{E(source.Availability)}</td><td>{E(source.ProviderId)}</td><td>{(source.DefaultPlayback ? "Default Playback" : "Explicit")}</td></tr>"));
        var groupRows = string.Join("", snapshot.SourceGroups.Select(group => $"<tr><td>{E(group.Group.FriendlyName)}</td><td><code>{E(group.Group.Id)}</code></td><td>{E(group.Availability)}</td><td>{group.Members.Count(item => item.Resolution is SourceMemberResolution.Unresolved or SourceMemberResolution.Ambiguous)}</td></tr>"));
        var profileRows = string.Join("", snapshot.Profiles.Select(profile => $"<tr><td>{E(profile.FriendlyName)}</td><td><code>{E(profile.Id)}</code></td><td>r{profile.Revision}</td><td>{profile.Waveform.TargetFps}</td></tr>"));
        var selfTest = snapshot.LatestSelfTest is null
            ? "<p>Not run during this Host session.</p>"
            : $"<p><strong>{E(snapshot.LatestSelfTest.OverallResult)}</strong> in {snapshot.LatestSelfTest.DurationMs} ms ({E(snapshot.LatestSelfTest.EndedAtUtc.ToString("O"))})</p><table><tbody>{string.Join("", snapshot.LatestSelfTest.Stages.Select(stage => $"<tr><td>{E(stage.Name)}</td><td>{E(stage.Status.ToString())}</td><td>{E(stage.Reason)}</td><td>{stage.DurationMs} ms</td></tr>"))}</tbody></table>";
        return Page("Diagnostics", theme, $$$"""
        <h1>Diagnostics</h1><p>Current-run evidence for beta troubleshooting. <a href=/health>Concise health API</a> · <a href=/api/v1/diagnostics>Diagnostics API</a></p>
        <section><h2>Beta Readiness</h2><ul><li>Host: <strong>{{{E(snapshot.HostVersion)}}}</strong> ({{{E(snapshot.ReleaseChannel)}}})</li><li>Health: {{{E(health.HostStatus)}}}</li><li>Provider health: {{{health.ProviderSummary.Connected}}}/{{{health.ProviderSummary.Enabled}}} enabled connected</li><li>Waveform health: {{{E(health.Waveform?.VisualState ?? "unavailable")}}}</li><li>InfoPanel compatibility: contract {{{snapshot.ContractVersion}}}; plugin version is visible inside InfoPanel when connected</li></ul><p class=error>{{{E(snapshot.ExternalReleaseGate)}}}</p></section>
        <section><h2>Build / Versions</h2><ul><li>Auraline Host: {{{E(snapshot.HostVersion)}}}</li><li>Host/plugin contract: {{{snapshot.ContractVersion}}}</li><li>Resonance Signal protocol: {{{snapshot.ResonanceSignalProtocolVersion}}}</li><li>OS: {{{E(snapshot.OperatingSystem)}}}</li><li>Runtime: {{{E(snapshot.Runtime)}}}; {{{E(snapshot.Architecture)}}}</li></ul></section>
        <section><h2>Providers</h2><div class=table-wrap><table><thead><tr><th>Name</th><th>Endpoint</th><th>State</th><th>Sources</th><th>Reconnects</th><th>Backoff</th><th>Last error</th></tr></thead><tbody>{{{providerRows}}}</tbody></table></div></section>
        <section><h2>Sources</h2><div class=table-wrap><table><thead><tr><th>Source</th><th>Availability</th><th>Provider</th><th>Intent</th></tr></thead><tbody>{{{sourceRows}}}</tbody></table></div></section>
        <section><h2>Source Groups</h2><div class=table-wrap><table><thead><tr><th>Name</th><th>ID</th><th>Availability</th><th>Unresolved</th></tr></thead><tbody>{{{groupRows}}}</tbody></table></div></section>
        <section><h2>Profiles</h2><div class=table-wrap><table><thead><tr><th>Name</th><th>ID</th><th>Revision</th><th>FPS</th></tr></thead><tbody>{{{profileRows}}}</tbody></table><p>Schema {{{ProductCatalogDocument.CurrentSchemaVersion}}}; validation failures {{{health.ProductConfiguration?.ValidationFailureCount ?? 0}}}; save failures {{{health.ProductConfiguration?.SaveFailureCount ?? 0}}}.</p></div></section>
        {{{WaveformCard(health.Waveform)}}}{{{RenderSessionsCard(health.RenderSessions)}}}
        <section><h2>InfoPanel Consumers</h2><p>Active consumer leases: {{{health.RenderSessions?.TotalConsumerLeases ?? 0}}}. Session IDs, dimensions, cadence, sequence, frame age, and errors are shown above and in InfoPanel's Auraline diagnostics entries. No additional polling is performed.</p></section>
        <section><h2>Logging</h2><p>Current level: <strong>{{{E(snapshot.LogLevel)}}}</strong>. Debug is temporary and resets to Info when Host restarts. Files roll at 10 MiB with seven retained.</p><form method=post action=/api/v1/diagnostics/log-level><button name=level value=Info>Info</button><button name=level value=Debug>Debug</button></form></section>
        <section><h2>Self-Test</h2>{{{selfTest}}}<form method=post action=/diagnostics/self-test><button>Run Host self-test</button></form><p class=small>The isolated test never uses an active consumer lease or changes saved configuration.</p></section>
        <section><h2>Export</h2><button type=button onclick="copySummary()">Copy diagnostics summary</button> <form class=inline method=post action=/api/v1/diagnostics/export><button>Export diagnostics</button></form><p class=small>No audio samples, waveform samples, or frame pixels are exported. Obvious usernames, profile paths, hostnames, and secret-like values are redacted. Technical provider, endpoint, source, and profile names may remain.</p><pre id=copyStatus></pre></section>
        <script>async function copySummary(){const r=await fetch('/api/v1/diagnostics/summary');const t=await r.text();await navigator.clipboard.writeText(t);document.querySelector('#copyStatus').textContent='Diagnostics summary copied.'}</script>
        """);
    }

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
            $"<tr><td><code>{E(session.SessionId)}</code></td><td><code>{E(session.ProfileId)}</code> r{session.ProfileRevision}</td><td>{session.Width}×{session.Height}</td><td>{session.TargetFps}</td><td>{session.ActualFps:F1}</td><td>{session.ConsumerCount}</td><td>{E(session.State)}</td><td>{session.PublishedSequence}</td><td>{session.HotApplyCount}</td><td>{session.AllocationSize}</td></tr>"));
        return $"<section><h2>Render Sessions</h2><p>{diagnostics.ActiveSessionCount} sessions, {diagnostics.TotalConsumerLeases} leases, cap {diagnostics.SessionCap}. Created {diagnostics.SessionCreationCount}; torn down {diagnostics.TeardownCount}; evicted {diagnostics.EvictionCount}; rejected {diagnostics.RejectedSessionCount}; hot-applied {diagnostics.HotApplyCount}.</p><div class=table-wrap><table><thead><tr><th>Session</th><th>Profile</th><th>Size</th><th>Target FPS</th><th>Actual FPS</th><th>Consumers</th><th>State</th><th>Sequence</th><th>Hot applies</th><th>Bytes</th></tr></thead><tbody>{rows}</tbody></table></div></section>";
    }

    private static string FormatOptionalDoubleMs(double? value) => value is null ? "—" : $"{value:F1} ms";

    private static string ThemeOptions(string selected) => string.Join("", new[] { "system", "light", "dark" }.Select(theme => $"<option value={theme} {(theme == selected ? "selected" : "")}>{char.ToUpperInvariant(theme[0]) + theme[1..]}</option>"));

    public static string ErrorPage(string title, string message, string returnPath, string theme) =>
        Page(title, theme, $"<h1>{E(title)}</h1>{Error(message)}<p><a class=button href=\"{E(returnPath)}\">Return</a></p>");

    private static string SourceOptions(ProductConfigurationStore products, IReadOnlyList<ProviderStatus> providers, SourceGroupDefinition? selectedGroup, bool multiple)
    {
        var selected = selectedGroup?.Members.Select(MemberKey).ToHashSet(StringComparer.Ordinal) ?? [];
        var options = new List<(string Key, string Label, bool Stale)>();
        foreach (var provider in providers)
        {
            options.Add(($"intent|{provider.Id}|{ProductDefaults.DefaultLogicalSourceIntent}", $"{provider.FriendlyName} — Logical Default Playback", false));
            options.AddRange(provider.Sources.Select(source => ($"source|{provider.Id}|{source.SourceId}", $"{provider.FriendlyName} — {source.DisplayName ?? source.SourceId}", false)));
        }
        foreach (var source in products.SourceCatalog.Sources)
            if (!options.Any(item => item.Key == $"source|{source.ProviderId}|{source.SourceId}"))
                options.Add(($"source|{source.ProviderId}|{source.SourceId}", $"{source.ProviderId} — {source.DisplayName ?? source.SourceId} (last known)", true));
        var html = string.Join("", options.DistinctBy(item => item.Key).Select(item => $"<option value=\"{E(item.Key)}\" {(selected.Contains(item.Key) ? "selected" : "")}>{E(item.Label)}</option>"));
        return $"<select name=members {(multiple ? "multiple size=8" : "")}>{html}</select>";
    }

    private static string MemberKey(SourceReference member) => !string.IsNullOrWhiteSpace(member.LogicalIntent)
        ? $"intent|{member.ProviderId}|{member.LogicalIntent}"
        : $"source|{member.ProviderId}|{member.SourceId}";

    private static string MemberLabel(SourceReference member, LastKnownSource? source) =>
        source?.DisplayName ?? member.LastKnownDisplayName ?? member.LogicalIntent ?? member.SourceId ?? "Unresolved source";

    private static string Error(string? message) => string.IsNullOrWhiteSpace(message) ? "" : $"<p class=error>{E(message)}</p>";
    private static string Js(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static string Page(string title, string theme, string body) => $$$"""
    <!doctype html><html lang="en" data-theme="{{{E(theme)}}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{{E(title)}}} · Auraline</title>
    <style>
    :root{color-scheme:light dark;--bg:#f4f5f8;--panel:#fff;--text:#1c2030;--muted:#646b7c;--accent:#7656d6;--line:#d9dce5;--error:#b42318}@media(prefers-color-scheme:dark){:root{--bg:#11131a;--panel:#1b1e28;--text:#f2f3f7;--muted:#aeb4c4;--line:#343949;--error:#ff8a80}}html[data-theme=light]{color-scheme:light;--bg:#f4f5f8;--panel:#fff;--text:#1c2030;--muted:#646b7c;--line:#d9dce5;--error:#b42318}html[data-theme=dark]{color-scheme:dark;--bg:#11131a;--panel:#1b1e28;--text:#f2f3f7;--muted:#aeb4c4;--line:#343949;--error:#ff8a80}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 system-ui,sans-serif}nav{background:#171927;padding:10px 22px;display:flex;align-items:center;gap:20px;flex-wrap:wrap}nav strong{color:white;margin-right:12px;display:flex;align-items:center;gap:9px}nav strong img{width:28px;height:28px;object-fit:contain}nav a{color:#d9d3ff;text-decoration:none}main{max-width:1200px;margin:28px auto;padding:0 20px}section,.table-wrap,table{background:var(--panel)}section{border:1px solid var(--line);border-radius:10px;padding:18px;margin:18px 0}.cards,.editor-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:15px;margin-bottom:24px}table{width:100%;border-collapse:collapse}th,td{padding:11px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top}.table-wrap{overflow:auto;border:1px solid var(--line);border-radius:10px}code{word-break:break-all}form{display:flex;gap:16px;align-items:center;flex-wrap:wrap}form.inline{display:inline}form.stack{display:flex;flex-direction:column;align-items:stretch}input,button,select,.button{padding:7px 10px}.button{display:inline-block;border:1px solid var(--line);border-radius:3px;color:var(--text);text-decoration:none;background:var(--panel)}button.danger{color:var(--error)}.error{color:var(--error);font-weight:600}.state{font-weight:600}.badge{font-size:12px;background:var(--accent);color:white;border-radius:10px;padding:2px 7px}p,.small{color:var(--muted)}.small{font-size:13px}.waveform-preview{margin:18px 0 0}.waveform-preview img{display:block;max-width:100%;height:auto;border:1px solid var(--line);border-radius:6px;background:repeating-conic-gradient(#222 0 25%,#292929 0 50%) 0/16px 16px}.waveform-preview figcaption{margin-top:7px;color:var(--muted);font-size:13px}
    </style></head><body><nav><strong><img src="/assets/auraline-mark.png" alt="">Auraline</strong>{{{string.Join("", Navigation.Select(item => $"<a href=\"{item.Path}\">{item.Label}</a>"))}}}</nav><main>{{{body}}}</main></body></html>
    """;

    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string U(string value) => Uri.EscapeDataString(value);
}
