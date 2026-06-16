using System.Text.Json.Serialization;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var configuredDatabasePath = builder.Configuration["Threadline:DatabasePath"];
builder.Services.AddSingleton(SqliteOptions.LocalAppData(string.IsNullOrWhiteSpace(configuredDatabasePath) ? null : configuredDatabasePath));
builder.Services.AddSingleton<SqliteThreadlineStore>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IProviderConnectionRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IThreadlineStoreInitializer>(sp => sp.GetRequiredService<SqliteThreadlineStore>());

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton(new CapturePolicy(DefaultRules.Create(DateTimeOffset.UtcNow)));
builder.Services.AddSingleton<ContextPreviewBuilder>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<ProviderConnectionService>();
builder.Services.AddSingleton<PromptComposer>();

var app = builder.Build();

foreach (var initializer in app.Services.GetServices<IThreadlineStoreInitializer>())
{
    await initializer.InitializeAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Threadline.Service", storage = "sqlite" }));

app.MapGet("/sessions/active", async (ISessionRepository repository, CancellationToken ct) =>
{
    var session = await repository.GetActiveSessionAsync(ct);
    return session is null ? Results.NotFound() : Results.Ok(session);
});

app.MapPost("/sessions", async (StartSessionRequest request, SessionService sessions, CancellationToken ct) =>
{
    var session = await sessions.StartAsync(request.Name, request.Provider, ct);
    return Results.Created($"/sessions/{session.Id}", session);
});

app.MapPost("/sessions/{sessionId}/events/preview", async (string sessionId, AppendContextEventRequest request, SessionService sessions, IClock clock, CancellationToken ct) =>
{
    var contextEvent = request.ToContextEvent(sessionId, clock.UtcNow);
    return Results.Ok(await sessions.PreviewContextAsync(contextEvent, ct));
});

app.MapPost("/sessions/{sessionId}/events", async (string sessionId, AppendContextEventRequest request, SessionService sessions, IClock clock, CancellationToken ct) =>
{
    var contextEvent = request.ToContextEvent(sessionId, clock.UtcNow);
    var saved = await sessions.AppendContextAsync(contextEvent, ct);
    return Results.Json(saved, statusCode: StatusCodes.Status202Accepted);
});

app.MapGet("/sessions/{sessionId}/events/recent", async (string sessionId, int? take, ISessionRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetRecentEventsAsync(sessionId, take ?? 20, ct)));

app.MapPost("/sessions/{sessionId}/summaries", async (string sessionId, SaveSummaryRequest request, ISessionRepository repository, CancellationToken ct) =>
{
    await repository.SaveSummaryAsync(sessionId, request.Summary, ct);
    return Results.NoContent();
});

app.MapPost("/sessions/{sessionId}/prompt", async (string sessionId, ComposePromptRequest request, ISessionRepository repository, PromptComposer promptComposer, CancellationToken ct) =>
{
    var events = await repository.GetRecentEventsAsync(sessionId, request.TakeRecentEvents ?? 20, ct);
    var summary = await repository.GetLatestSummaryAsync(sessionId, ct);
    return Results.Ok(promptComposer.Compose(new ThreadlinePromptContext(request.Question, request.CurrentWindow, summary, events)));
});

app.MapGet("/providers", async (ProviderConnectionService providers, CancellationToken ct) =>
    Results.Ok(await providers.ListAsync(ct)));

app.MapGet("/providers/{providerName}", async (string providerName, ProviderConnectionService providers, CancellationToken ct) =>
{
    var provider = await providers.GetAsync(providerName, ct);
    return provider is null ? Results.NotFound() : Results.Ok(provider);
});

app.MapPost("/providers", async (SaveProviderConnectionRequest request, ProviderConnectionService providers, IClock clock, CancellationToken ct) =>
{
    var connection = new ProviderConnection(
        string.IsNullOrWhiteSpace(request.Id) ? $"prv_{Guid.NewGuid():N}" : request.Id,
        request.ProviderName,
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

app.MapGet("/audit/recent", async (string? sessionId, int? take, IAuditRepository audit, CancellationToken ct) =>
    Results.Ok(await audit.GetRecentAuditEventsAsync(sessionId, take ?? 50, ct)));

app.Run();

public sealed record StartSessionRequest(string Name, string? Provider = null);
public sealed record AppendContextEventRequest(ContextSource Source, string ContextType, string Content, string? ApplicationName = null, string? ProcessName = null, string? WindowTitle = null, string? Uri = null, SensitivityLevel Sensitivity = SensitivityLevel.Normal, bool UserApproved = false, IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ContextEvent ToContextEvent(string sessionId, DateTimeOffset timestamp) =>
        ContextEvent.Create(sessionId, Source, ContextType, Content, timestamp, ApplicationName, ProcessName, WindowTitle, Uri, Sensitivity, UserApproved, Metadata);
}
public sealed record ComposePromptRequest(string Question, string? CurrentWindow = null, int? TakeRecentEvents = 20);
public sealed record SaveSummaryRequest(string Summary);
public sealed record SaveProviderConnectionRequest(string ProviderName, ProviderAuthType AuthType, string? Id = null, string? CredentialReference = null, string? BaseUrl = null, string? DefaultModel = null, ProviderConnectionStatus Status = ProviderConnectionStatus.NeedsConfiguration, DateTimeOffset? CreatedAt = null, IReadOnlyDictionary<string, string>? Metadata = null);

internal static class DefaultRules
{
    public static IReadOnlyList<CaptureRule> Create(DateTimeOffset now) =>
    [
        CaptureRule.Create(CaptureRuleType.ProcessName, "1Password", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "KeePass", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "Bitwarden", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Private Browsing", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "InPrivate", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Patient Chart", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "bankofamerica.com", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "chase.com", CaptureRuleAction.Block, now)
    ];
}
