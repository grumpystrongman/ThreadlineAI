using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;
using Threadline.Infrastructure.Windowing;

namespace Threadline.Service;

public static class ThreadlineEndpointMappings
{
    public static WebApplication MapThreadlineHealth(this WebApplication app, ThreadlineServiceOptions options)
    {
        app.MapGet("/health", (ThreadlineCommercialLifecycleService lifecycle) =>
        {
            var version = lifecycle.BuildVersionInfo();
            return Results.Ok(new
            {
                status = "ok",
                service = "Threadline.Service",
                storage = "sqlite",
                authRequired = options.RequireApiToken,
                maxContextCharacters = options.MaxContextCharacters,
                productVersion = version.ProductVersion,
                serviceVersion = version.ServiceAssemblyVersion,
                apiCompatibility = version.ApiCompatibility,
                expectedBrowserExtensionVersion = version.ExpectedBrowserExtensionVersion
            });
        });

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

        api.MapPost("/sessions/{sessionId}/ask", async (string sessionId, ComposePromptRequest request, ThreadlineAskService askService, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var invalidQuestion = RequestValidator.ValidateQuestion(request.Question);
            if (invalidQuestion is not null) return invalidQuestion;

            try
            {
                return Results.Ok(await askService.AskAsync(sessionId, request, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        api.MapPost("/sessions/{sessionId}/windows/attach", async (string sessionId, AttachWindowRequest request, WindowAttachmentService windows, IClock clock, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;
            var invalidWindow = RequestValidator.ValidateWindow(request);
            if (invalidWindow is not null) return invalidWindow;

            var attachment = await windows.AttachAsync(sessionId, request.ToSnapshot(clock.UtcNow), ct);
            return Results.Created($"/sessions/{sessionId}/windows/{attachment.Id}", attachment);
        });

        api.MapGet("/sessions/{sessionId}/windows/current", async (string sessionId, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var active = await windows.GetActiveAsync(sessionId, ct);
            IResult result = active is null ? Results.NotFound() : Results.Ok(active);
            return result;
        });

        api.MapDelete("/sessions/{sessionId}/windows/current", async (string sessionId, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var detached = await windows.DetachAsync(sessionId, ct);
            IResult result = detached is null ? Results.NotFound() : Results.Ok(detached);
            return result;
        });

        api.MapGet("/sessions/{sessionId}/windows", async (string sessionId, int? take, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            return Results.Ok(await windows.ListAttachmentsAsync(sessionId, take ?? 20, ct));
        });

        api.MapPost("/sessions/{sessionId}/windows/current/preview", async (string sessionId, StoreWindowContextRequest request, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            return Results.Ok(await windows.PreviewActiveWindowContextAsync(sessionId, request.UserApproved, ct));
        });

        api.MapPost("/sessions/{sessionId}/windows/current/store", async (string sessionId, StoreWindowContextRequest request, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var saved = await windows.StoreActiveWindowContextAsync(sessionId, request.UserApproved, ct);
            return Results.Json(saved, statusCode: StatusCodes.Status202Accepted);
        });

        api.MapPost("/sessions/{sessionId}/actions", async (string sessionId, ProposeWindowActionRequest request, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;
            var invalidAction = RequestValidator.ValidateWindowAction(request);
            if (invalidAction is not null) return invalidAction;

            var action = await windows.ProposeActionAsync(sessionId, request.Kind, request.Description, request.Payload, request.UserApproved, request.AttachmentId, request.Risk, ct);
            return Results.Created($"/sessions/{sessionId}/actions/{action.Id}", action);
        });

        api.MapGet("/sessions/{sessionId}/actions", async (string sessionId, int? take, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            return Results.Ok(await windows.ListActionsAsync(sessionId, take ?? 20, ct));
        });

        api.MapPost("/actions/{actionId}/approve", async (string actionId, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidAction = RequestValidator.ValidateActionId(actionId);
            if (invalidAction is not null) return invalidAction;

            return Results.Ok(await windows.ApproveActionAsync(actionId, ct));
        });

        api.MapPost("/actions/{actionId}/complete", async (string actionId, CompleteWindowActionRequest request, WindowAttachmentService windows, CancellationToken ct) =>
        {
            var invalidAction = RequestValidator.ValidateActionId(actionId);
            if (invalidAction is not null) return invalidAction;

            var action = request.Failed
                ? await windows.FailActionAsync(actionId, request.ResultMessage ?? "Window action failed.", ct)
                : await windows.CompleteActionAsync(actionId, request.ResultMessage, ct);
            return Results.Ok(action);
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

        api.MapPost("/providers/{providerName}/credential", async (string providerName, SaveProviderCredentialRequest request, SecretService secrets, ProviderConnectionService providers, IClock clock, CancellationToken ct) =>
        {
            var invalidProvider = RequestValidator.ValidateProviderName(providerName);
            if (invalidProvider is not null) return invalidProvider;

            var invalidCredential = RequestValidator.ValidateProviderCredential(request);
            if (invalidCredential is not null) return invalidCredential;

            var normalizedProvider = providerName.Trim();
            var descriptor = await secrets.SetAsync($"provider/{normalizedProvider.ToLowerInvariant()}/credential", request.SecretValue, request.Metadata, ct);
            var connection = new ProviderConnection(
                $"prv_{Guid.NewGuid():N}",
                normalizedProvider,
                request.AuthType,
                descriptor.Reference,
                request.BaseUrl,
                request.DefaultModel,
                request.Status,
                clock.UtcNow,
                clock.UtcNow,
                new Dictionary<string, string>
                {
                    ["credentialProtectionKind"] = descriptor.ProtectionKind.ToString(),
                    ["credentialName"] = descriptor.Name
                });

            var saved = await providers.SaveAsync(connection, ct);
            return Results.Created($"/providers/{saved.ProviderName}", new { provider = saved, credential = SecretDescriptorResponse.FromDescriptor(descriptor) });
        });

        api.MapGet("/adapters", async (IAdapterRegistry adapters, CancellationToken ct) =>
            Results.Ok(await adapters.ListAsync(ct)));

        api.MapGet("/adapters/{adapterId}", async (string adapterId, IAdapterRegistry adapters, CancellationToken ct) =>
        {
            var invalidAdapterId = ValidateAdapterId(adapterId);
            if (invalidAdapterId is not null) return invalidAdapterId;

            var adapter = await adapters.GetAsync(adapterId.Trim(), ct);
            IResult result = adapter is null ? Results.NotFound() : Results.Ok(adapter);
            return result;
        });

        api.MapPost("/adapters", async (RegisterAdapterRequest request, IAdapterRegistry adapters, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var invalidAdapter = RequestValidator.ValidateAdapter(request);
            if (invalidAdapter is not null) return invalidAdapter;

            var registered = AdapterRegistration.Create(request.Kind, request.DisplayName.Trim(), request.Permissions, clock.UtcNow, request.Version, MetadataHelpers.NormalizeMetadata(request.Metadata));
            await adapters.RegisterAsync(registered, ct);
            await audit.AppendAuditEventAsync(
                AuditEvent.Create(
                    null,
                    AuditEventType.AdapterRegistered,
                    clock.UtcNow,
                    $"Adapter registered: {registered.DisplayName} ({registered.Kind}).",
                    new Dictionary<string, string>
                    {
                        ["adapterId"] = registered.Id,
                        ["adapterKind"] = registered.Kind.ToString(),
                        ["adapterVersion"] = registered.Version ?? "unknown"
                    }),
                ct);
            return Results.Created($"/adapters/{registered.Id}", registered);
        });

        api.MapPost("/adapters/{adapterId}/heartbeat", async (string adapterId, AdapterHeartbeatRequest request, IAdapterRegistry adapters, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var invalidAdapterId = ValidateAdapterId(adapterId);
            if (invalidAdapterId is not null) return invalidAdapterId;

            var registration = await adapters.GetAsync(adapterId.Trim(), ct);
            if (registration is null) return Results.NotFound(new { error = "Adapter is not registered. Register the extension before sending heartbeat." });

            var metadata = MetadataHelpers.MergeMetadata(registration.Metadata, request.Metadata);
            metadata["lastHeartbeatAt"] = clock.UtcNow.ToString("O");
            if (!string.IsNullOrWhiteSpace(request.Version)) metadata["heartbeatVersion"] = request.Version.Trim();

            var updated = registration with
            {
                LastSeenAt = clock.UtcNow,
                Version = string.IsNullOrWhiteSpace(request.Version) ? registration.Version : request.Version.Trim(),
                Metadata = metadata
            };

            await adapters.RegisterAsync(updated, ct);
            await audit.AppendAuditEventAsync(
                AuditEvent.Create(
                    null,
                    AuditEventType.AdapterHeartbeat,
                    clock.UtcNow,
                    $"Adapter heartbeat received: {updated.DisplayName} ({updated.Kind}).",
                    new Dictionary<string, string>
                    {
                        ["adapterId"] = updated.Id,
                        ["adapterKind"] = updated.Kind.ToString(),
                        ["adapterVersion"] = updated.Version ?? "unknown"
                    }),
                ct);

            return Results.Ok(updated);
        });

        return app;
    }

    private static IResult? ValidateAdapterId(string? adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId) || !adapterId.StartsWith("adp_", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "A valid Threadline adapter id is required." });
        }

        return null;
    }

}
