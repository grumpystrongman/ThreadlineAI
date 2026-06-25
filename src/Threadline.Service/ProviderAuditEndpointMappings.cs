using Threadline.Core;

namespace Threadline.Service;

public static class ProviderAuditEndpointMappings
{
    public static WebApplication MapThreadlineProviderAuditApi(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty).RequireThreadlineLocalAccess();

        api.MapGet("/audit/recent", async (string? sessionId, int? take, IAuditRepository audit, CancellationToken ct) =>
        {
            var events = await audit.GetRecentAuditEventsAsync(string.IsNullOrWhiteSpace(sessionId) ? null : sessionId, RequestValidator.ClampTake(take, 50), ct);
            return Results.Ok(events);
        });

        api.MapGet("/audit/provider-context", async (string? sessionId, int? take, IAuditRepository audit, CancellationToken ct) =>
        {
            var events = await audit.GetRecentAuditEventsAsync(string.IsNullOrWhiteSpace(sessionId) ? null : sessionId, RequestValidator.ClampTake(take, 100), ct);
            return Results.Ok(events.Where(IsProviderContextAuditEvent).ToArray());
        });

        return app;
    }

    private static bool IsProviderContextAuditEvent(AuditEvent auditEvent) =>
        auditEvent.EventType is AuditEventType.ProviderCallStarted or AuditEventType.ProviderCallCompleted or AuditEventType.ProviderCallFailed;
}
