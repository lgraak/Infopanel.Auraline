namespace Auraline.Host.Diagnostics;

public static class DiagnosticsApi
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/diagnostics", (DiagnosticsService diagnostics) => Results.Json(diagnostics.GetSnapshot()));
        app.MapGet("/api/v1/diagnostics/summary", (DiagnosticsService diagnostics) => Results.Text(diagnostics.CreateMarkdownSummary(), "text/markdown"));
        app.MapPost("/api/v1/diagnostics/self-test", async (DiagnosticsService diagnostics, CancellationToken cancellationToken) =>
            Results.Json(await diagnostics.RunSelfTestAsync(cancellationToken)));
        app.MapPost("/diagnostics/self-test", async (DiagnosticsService diagnostics, CancellationToken cancellationToken) =>
        {
            await diagnostics.RunSelfTestAsync(cancellationToken);
            return Results.Redirect("/diagnostics");
        });
        app.MapPost("/api/v1/diagnostics/log-level", async (HttpRequest request, DiagnosticLogLevel level) =>
        {
            var form = await request.ReadFormAsync();
            level.Set(form["level"].ToString());
            return Results.Redirect("/diagnostics");
        });
        app.MapPost("/api/v1/diagnostics/export", (DiagnosticsService diagnostics) =>
        {
            var export = diagnostics.CreateExport();
            return Results.File(export.Content, "application/zip", export.FileName);
        });
    }
}
