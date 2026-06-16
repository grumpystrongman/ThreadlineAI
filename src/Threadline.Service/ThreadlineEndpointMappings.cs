using Threadline.Core;
using Threadline.Infrastructure;

namespace Threadline.Service;

public static class ThreadlineEndpointMappings
{
    public static WebApplication MapThreadlineHealth(this WebApplication app, ThreadlineServiceOptions options)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "Threadline.Service",
            storage = "sqlite",
            authRequired = options.RequireApiToken,
            maxContextCharacters = options.MaxContextCharacters
        }));

        return app;
    }

    public static WebApplication MapThreadlineApi(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty).RequireThreadlineLocalAccess();

        api.MapGet("/sessions/active", async (ISessionRepository repository, CancellationToken ct) =>
        {
            var session = await repository.GetActiveSessionAsync(ct);
            IResult result = session is null ? Results.NotFound() : Results.Ok(session);
            return result;
        });

        api.MapPost("/sessions", async (StartSessionRequest request, SessionService sessions, ThreadlineServiceOptions options, CancellationToken ct) =>
        {
            var invalid = RequestValidator.ValidateSessionName(request.Name, options);
            if (invalid is not null) return invalid;

            var provider = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider.Trim();
            var session = await sessions.StartAsync(request.Name.Trim(), provider, ct);
            return Results.Created($"/sessions/{session.Id}", session);
        });

        api.MapPost("/sessions/{sessionId}/events/preview", async (string sessionId, AppendContextEventRequest request, SessionService sessions, IClock clock, ThreadlineServiceOptions options, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var invalidContext = RequestValidator.ValidateContext(request, options);
            if (invalidContext is not null) return invalidContext;

            var contextEvent = request.ToContextEvent(sessionId, clock.UtcNow);
            return Results.Ok(await sessions.PreviewContextAsync(contextEvent, ct));
        });

        api.MapPost("/sessions/{sessionId}/events", async (string sessionId, AppendContextEventRequest request, SessionService sessions, IClock clock, ThreadlineServiceOptions options, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var invalidContext = RequestValidator.ValidateContext(request, options);
            if (invalidContext is not null) return invalidContext;

            var contextEvent = request.ToContextEvent(sessionId, clock.UtcNow);
            var saved = await sessions.AppendContextAsync(contextEvent, ct);
            return Results.Json(saved, statusCode: StatusCodes.Status202Accepted);
        });

        api.MapGet("/sessions/{sessionId}/events/recent", async (string sessionId, int? take, ISessionRepository repository, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            return Results.Ok(await repository.GetRecentEventsAsync(sessionId, take ?? 20, ct));
        });

        api.MapPost("/sessions/{sessionId}/summaries", async (string sessionId, SaveSummaryRequest request, ISessionRepository repository, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var invalidSummary = RequestValidator.ValidateSummary(request.Summary);
            if (invalidSummary is not null) return invalidSummary;

            await repository.SaveSummaryAsync(sessionId, request.Summary.Trim(), ct);
            return Results.NoContent();
        });

        api.MapPost("/sessions/{sessionId}/prompt", async (string sessionId, ComposePromptRequest request, ISessionRepository repository, PromptComposer promptComposer, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var invalidQuestion = RequestValidator.ValidateQuestion(request.Question);
            if (invalidQuestion is not null) return invalidQuestion;

            var events = await repository.GetRecentEventsAsync(sessionId, request.TakeRecentEvents ?? 20, ct);
            var summary = await repository.GetLatestSummaryAsync(sessionId, ct);
            return Results.Ok(promptComposer.Compose(new ThreadlinePromptContext(request.Question.Trim(), request.CurrentWindow, summary, events)));
        });

        api.MapGet("/providers", async (ProviderConnectionService providers, CancellationToken ct) =>
            Results.Ok(await providers.ListAsync(ct)));

        api.MapGet("/providers/{providerName}", async (string providerName, ProviderConnectionService providers, CancellationToken ct) =>
        {
            var invalidProvider = RequestValidator.ValidateProviderName(providerName);
            if (invalidProvider is not null) return invalidProvider;

            var provider = await providers.GetAsync(providerName.Trim(), ct);
            IResult result = provider is null ? Results.NotFound() : Results.Ok(provider);
            return result;
        });

        api.MapPost("/providers", async (SaveProviderConnectionRequest request, ProviderConnectionService providers, IClock clock, CancellationToken ct) =>
        {
            var invalidProvider = RequestValidator.ValidateProviderName(request.ProviderName);
            if (invalidProvider is not null) return invalidProvider;

            var connection = new ProviderConnection(
                string.IsNullOrWhiteSpace(request.Id) ? $"prv_{Guid.NewGuid():N}" : request.Id,
                request.ProviderName.Trim(),
                request.AuthType,
                request.CredentialReference,
                request.BaseUrl,
                request.DefaultModel,
                request.Status,
                request.CreatedAt ?? clock.UtcNow,
                clock.UtcNow,
                request.Metadata);

            var saved = await providers.SaveAsync(connection, ct);
            return Results.Created($"/providers/{saved.ProviderName}", saved);
        });

        api.MapGet("/audit/recent", async (string? sessionId, int? take, IAuditRepository audit, CancellationToken ct) =>
            Results.Ok(await audit.GetRecentAuditEventsAsync(sessionId, take ?? 50, ct)));

        api.MapGet("/adapters", async (IAdapterRegistry registry, CancellationToken ct) =>
            Results.Ok(await registry.ListAsync(ct)));

        api.MapPost("/adapters", async (RegisterAdapterRequest request, IAdapterRegistry registry, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var invalidAdapter = RequestValidator.ValidateAdapter(request);
            if (invalidAdapter is not null) return invalidAdapter;

            var registration = AdapterRegistration.Create(request.Kind, request.DisplayName, request.Permissions, clock.UtcNow, request.Version, request.Metadata);
            var saved = await registry.RegisterAsync(registration, ct);
            await audit.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.AdapterRegistered, clock.UtcNow, $"Adapter registered: {saved.DisplayName}"), ct);
            return Results.Created($"/adapters/{saved.Id}", saved);
        });

        api.MapPost("/adapters/{adapterId}/heartbeat", async (string adapterId, IAdapterRegistry registry, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(adapterId) || !adapterId.StartsWith("adp_", StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "A valid Threadline adapter id is required." });
            }

            var updated = await registry.MarkSeenAsync(adapterId, clock.UtcNow, ct);
            if (updated is null)
            {
                return Results.NotFound();
            }

            await audit.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.AdapterHeartbeat, clock.UtcNow, $"Adapter heartbeat: {updated.DisplayName}"), ct);
            return Results.Ok(updated);
        });

        return app;
    }
}
