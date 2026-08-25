using System.Text.Json.Serialization;
using Auraline.Contracts;

namespace Auraline.Host.RenderSessions;

public sealed record AttachRenderSessionRequest(
    [property: JsonPropertyName("contract_major")] int ContractMajor,
    [property: JsonPropertyName("contract_minor")] int ContractMinor,
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("target_fps")] int? TargetFps);

public static class RenderSessionApi
{
    public static void MapRenderSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/render-sessions/attach", (AttachRenderSessionRequest request, RenderSessionManager sessions) =>
        {
            try
            {
                var attachment = sessions.Attach(
                    request.ProfileId,
                    request.Width,
                    request.Height,
                    request.TargetFps ?? 30,
                    new ContractVersion(request.ContractMajor, request.ContractMinor));
                return Results.Json(attachment, statusCode: StatusCodes.Status201Created);
            }
            catch (NotSupportedException ex)
            {
                return Error(StatusCodes.Status426UpgradeRequired, ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return Error(StatusCodes.Status404NotFound, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Error(StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (RenderSessionCapacityException ex)
            {
                return Error(StatusCodes.Status409Conflict, ex.Message);
            }
        });

        app.MapPost("/api/v1/render-sessions/{sessionId}/leases/{leaseId}/heartbeat", (
            string sessionId,
            string leaseId,
            RenderSessionManager sessions) =>
        {
            var lease = sessions.Heartbeat(sessionId, leaseId);
            return lease is null
                ? Error(StatusCodes.Status404NotFound, "The render session or lease is missing or expired.")
                : Results.Json(lease);
        });

        app.MapDelete("/api/v1/render-sessions/{sessionId}/leases/{leaseId}", (
            string sessionId,
            string leaseId,
            RenderSessionManager sessions) =>
            sessions.Detach(sessionId, leaseId)
                ? Results.NoContent()
                : Error(StatusCodes.Status404NotFound, "The render session or lease is missing or expired."));

        app.MapGet("/api/v1/render-sessions", (RenderSessionManager sessions) => Results.Json(sessions.GetDiagnostics()));
        app.MapGet("/api/v1/render-sessions/{sessionId}", (string sessionId, RenderSessionManager sessions) =>
        {
            var diagnostic = sessions.GetDiagnostic(sessionId);
            return diagnostic is null
                ? Error(StatusCodes.Status404NotFound, "The render session does not exist.")
                : Results.Json(diagnostic);
        });
    }

    private static IResult Error(int statusCode, string message) =>
        Results.Json(new { error = message }, statusCode: statusCode);
}
