using Threadline.Core;
using Threadline.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton(new CapturePolicy(DefaultRules.Create(DateTimeOffset.UtcNow)));
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<PromptComposer>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Threadline.Service" }));
app.MapPost("/sessions", async (StartSessionRequest request, SessionService sessions, CancellationToken ct) => Results.Created("/sessions", await sessions.StartAsync(request.Name, request.Provider, ct)));
app.MapPost("/sessions/{sessionId}/events", async (string sessionId, AppendContextEventRequest request, SessionService sessions, IClock clock, CancellationToken ct) =>
{
    var contextEvent = ContextEvent.Create(sessionId, request.Source, request.ContextType, request.Content, clock.UtcNow, request.ApplicationName, request.ProcessName, request.WindowTitle, request.Uri, request.Sensitivity, request.UserApproved, request.Metadata);
    var saved = await sessions.AppendContextAsync(contextEvent, ct);
    return Results.Accepted($"/sessions/{sessionId}/events/{saved.Id}", saved);
});
app.MapGet("/sessions/{sessionId}/events/recent", async (string sessionId, int? take, ISessionRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentEventsAsync(sessionId, take ?? 20, ct)));
app.MapPost("/sessions/{sessionId}/prompt", async (string sessionId, ComposePromptRequest request, ISessionRepository repository, PromptComposer promptComposer, CancellationToken ct) =>
{
    var events = await repository.GetRecentEventsAsync(sessionId, request.TakeRecentEvents ?? 20, ct);
    var summary = await repository.GetLatestSummaryAsync(sessionId, ct);
    return Results.Ok(promptComposer.Compose(new ThreadlinePromptContext(request.Question, request.CurrentWindow, summary, events)));
});
app.Run();

public sealed record StartSessionRequest(string Name, string? Provider = null);
public sealed record AppendContextEventRequest(ContextSource Source, string ContextType, string Content, string? ApplicationName = null, string? ProcessName = null, string? WindowTitle = null, string? Uri = null, SensitivityLevel Sensitivity = SensitivityLevel.Normal, bool UserApproved = true, IReadOnlyDictionary<string, string>? Metadata = null);
public sealed record ComposePromptRequest(string Question, string? CurrentWindow = null, int? TakeRecentEvents = 20);

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
