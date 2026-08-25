using System.Text.Json.Serialization;
using Auraline.Host.Providers;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;

namespace Auraline.Host.Configuration;

public sealed record CreateSourceGroupRequest(
    [property: JsonPropertyName("friendly_name")] string FriendlyName,
    [property: JsonPropertyName("members")] IReadOnlyList<SourceReference> Members);

public sealed record CreateProfileRequest(
    [property: JsonPropertyName("friendly_name")] string FriendlyName,
    [property: JsonPropertyName("source_group_id")] string SourceGroupId);

public static class ConfigurationApi
{
    public static void MapConfigurationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/providers", (ProviderManager providers) => Results.Json(providers.GetStatuses()));
        app.MapPost("/api/v1/providers", async (ProviderConfiguration provider, ProviderManager providers, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => Results.Json(await providers.AddAsync(provider, cancellationToken), statusCode: StatusCodes.Status201Created)));
        app.MapPut("/api/v1/providers/{providerId}", async (string providerId, ProviderConfiguration provider, ProviderManager providers, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => Results.Json(await providers.UpdateAsync(providerId, provider, cancellationToken))));
        app.MapDelete("/api/v1/providers/{providerId}", async (string providerId, ProviderManager providers, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => { await providers.DeleteAsync(providerId, cancellationToken); return Results.NoContent(); }));
        app.MapPost("/api/v1/providers/{providerId}/reconnect", async (string providerId, ProviderManager providers) =>
            await ExecuteAsync(async () => { await providers.ReconnectAsync(providerId); return Results.NoContent(); }));
        app.MapPost("/api/v1/providers/{providerId}/refresh-sources", async (string providerId, ProviderManager providers, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => { await providers.RefreshSourcesAsync(providerId, cancellationToken); return Results.NoContent(); }));

        app.MapGet("/api/v1/source-groups", (ProductConfigurationStore store, ProviderManager providers) =>
            Results.Json(store.GetGroups().Select(group => store.ResolveGroup(group.Id, providers.GetStatuses()))));
        app.MapGet("/api/v1/source-groups/{groupId}", (string groupId, ProductConfigurationStore store, ProviderManager providers) =>
            Execute(() => Results.Json(store.ResolveGroup(groupId, providers.GetStatuses()))));
        app.MapPost("/api/v1/source-groups", async (CreateSourceGroupRequest request, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => Results.Json(await store.CreateGroupAsync(request.FriendlyName, request.Members, cancellationToken), statusCode: StatusCodes.Status201Created)));
        app.MapPut("/api/v1/source-groups/{groupId}", async (string groupId, SourceGroupDefinition group, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                if (!group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)) return Error(StatusCodes.Status400BadRequest, "Route and source-group IDs must match.");
                return Results.Json(await store.SaveGroupAsync(group, cancellationToken: cancellationToken));
            }));
        app.MapPost("/api/v1/source-groups/{groupId}/duplicate", async (string groupId, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => Results.Json(await store.DuplicateGroupAsync(groupId, cancellationToken), statusCode: StatusCodes.Status201Created)));
        app.MapPost("/api/v1/source-groups/{groupId}/set-default", async (string groupId, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => { await store.SetDefaultGroupAsync(groupId, cancellationToken); return Results.NoContent(); }));
        app.MapDelete("/api/v1/source-groups/{groupId}", async (string groupId, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => { await store.DeleteGroupAsync(groupId, cancellationToken); return Results.NoContent(); }));

        app.MapGet("/api/v1/profiles/{profileId}", (string profileId, ProductConfigurationStore store) => Execute(() => Results.Json(store.GetProfile(profileId))));
        app.MapPost("/api/v1/profiles", async (CreateProfileRequest request, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => Results.Json(await store.CreateProfileAsync(request.FriendlyName, request.SourceGroupId, cancellationToken), statusCode: StatusCodes.Status201Created)));
        app.MapPut("/api/v1/profiles/{profileId}", async (string profileId, ProfileDefinition profile, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                if (!profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) return Error(StatusCodes.Status400BadRequest, "Route and profile IDs must match.");
                return Results.Json(await store.SaveProfileAsync(profile, cancellationToken: cancellationToken));
            }));
        app.MapPost("/api/v1/profiles/{profileId}/duplicate", async (string profileId, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => Results.Json(await store.DuplicateProfileAsync(profileId, cancellationToken), statusCode: StatusCodes.Status201Created)));
        app.MapPost("/api/v1/profiles/{profileId}/set-default", async (string profileId, ProductConfigurationStore store, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => { await store.SetDefaultProfileAsync(profileId, cancellationToken); return Results.NoContent(); }));
        app.MapDelete("/api/v1/profiles/{profileId}", async (string profileId, ProductConfigurationStore store, RenderSessionManager sessions, CancellationToken cancellationToken) =>
            await ExecuteAsync(async () => { await store.DeleteProfileAsync(profileId, sessions.IsProfileInUse, cancellationToken); return Results.NoContent(); }));

        app.MapPost("/api/v1/profile-preview/render.png", (ProfileDefinition workingCopy, ProductConfigurationStore store,
            IWaveformRenderStateSource waveform, WaveformRenderer renderer, HttpResponse response) => Execute(() =>
        {
            var errors = ProductConfigurationValidator.ValidateProfile(workingCopy, store.GetGroups().Select(item => item.Id).ToArray());
            if (errors.Count > 0) return Error(StatusCodes.Status400BadRequest, string.Join(" ", errors));
            if (!store.IsRuntimeSupported(workingCopy))
                return Error(StatusCodes.Status409Conflict, "M5 preview supports the local logical Default Playback source group; this group is preserved but not renderable yet.");
            var snapshot = waveform.CaptureRenderState();
            var rendered = renderer.Render(snapshot.ProcessedFrame, snapshot.VisualState, 480, 180, 1, DateTimeOffset.UtcNow,
                workingCopy.Waveform.TargetFps, Environment.TickCount, workingCopy.Waveform);
            response.Headers.CacheControl = "no-store";
            return Results.Bytes(renderer.EncodePng(rendered), "image/png");
        }));
    }

    private static IResult Execute(Func<IResult> action)
    {
        try { return action(); }
        catch (Exception ex) { return FromException(ex); }
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (Exception ex) { return FromException(ex); }
    }

    private static IResult FromException(Exception exception) => exception switch
    {
        KeyNotFoundException => Error(StatusCodes.Status404NotFound, exception.Message),
        ConfigurationDependencyException => Error(StatusCodes.Status409Conflict, exception.Message),
        InvalidDataException or ArgumentException or InvalidOperationException => Error(StatusCodes.Status400BadRequest, exception.Message),
        _ => throw exception
    };

    private static IResult Error(int statusCode, string message) => Results.Json(new { error = message }, statusCode: statusCode);
}
