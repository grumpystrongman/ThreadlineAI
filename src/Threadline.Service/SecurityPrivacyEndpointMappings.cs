using Threadline.Core;
using Threadline.Infrastructure.Security;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Service;

public static class SecurityPrivacyEndpointMappings
{
    public static WebApplication MapThreadlineSecurityPrivacyApi(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty).RequireThreadlineLocalAccess();

        api.MapGet("/privacy/status", (ThreadlineServiceOptions options, PrivacyRuntimeState runtime) =>
            Results.Ok(new PrivacyStatusResponse(
                options.RequireApiToken,
                options.ApiTokenPath,
                options.RetentionDays,
                options.LocalOnlyMode,
                options.CorsAllowedOrigins,
                runtime.Rules.Count)));

        api.MapGet("/privacy/exclusions", async (SqlitePrivacyAndMaintenanceStore store, CancellationToken ct) =>
            Results.Ok(await store.ListPrivacyExclusionsAsync(ct)));

        api.MapPost("/privacy/exclusions/apps", async (PrivacyExclusionRequest request, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken ct) =>
            await AddExclusionAsync(CaptureRuleType.ApplicationName, request, store, runtime, ct));

        api.MapPost("/privacy/exclusions/processes", async (PrivacyExclusionRequest request, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken ct) =>
            await AddExclusionAsync(CaptureRuleType.ProcessName, request, store, runtime, ct));

        api.MapPost("/privacy/exclusions/domains", async (PrivacyExclusionRequest request, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken ct) =>
            await AddExclusionAsync(CaptureRuleType.DomainContains, request, store, runtime, ct));

        api.MapPost("/privacy/exclusions/uris", async (PrivacyExclusionRequest request, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken ct) =>
            await AddExclusionAsync(CaptureRuleType.UriContains, request, store, runtime, ct));

        api.MapPost("/privacy/never-send", async (NeverSendRequest request, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken ct) =>
        {
            var created = new List<CaptureRule>();
            if (!string.IsNullOrWhiteSpace(request.AppName)) created.Add(await store.AddPrivacyExclusionAsync(CaptureRuleType.ApplicationName, request.AppName, request.Reason ?? "Never send this app", ct));
            if (!string.IsNullOrWhiteSpace(request.ProcessName)) created.Add(await store.AddPrivacyExclusionAsync(CaptureRuleType.ProcessName, request.ProcessName, request.Reason ?? "Never send this process", ct));
            if (!string.IsNullOrWhiteSpace(request.Domain)) created.Add(await store.AddPrivacyExclusionAsync(CaptureRuleType.DomainContains, NormalizeDomain(request.Domain), request.Reason ?? "Never send this domain", ct));
            if (!string.IsNullOrWhiteSpace(request.Uri)) created.Add(await store.AddPrivacyExclusionAsync(CaptureRuleType.UriContains, request.Uri, request.Reason ?? "Never send this URI", ct));
            if (created.Count == 0) return Results.BadRequest(new { error = "Provide at least one appName, processName, domain, or uri value." });
            foreach (var rule in created) runtime.Upsert(rule);
            return Results.Created("/privacy/exclusions", created);
        });

        api.MapDelete("/privacy/exclusions/{ruleId}", async (string ruleId, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken ct) =>
        {
            var removed = await store.DeletePrivacyExclusionAsync(ruleId, ct);
            if (!removed) return Results.NotFound();
            runtime.Remove(ruleId);
            return Results.NoContent();
        });

        api.MapPost("/privacy/retention/apply", async (ThreadlineServiceOptions options, SqlitePrivacyAndMaintenanceStore store, CancellationToken ct) =>
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
            return Results.Ok(await store.PurgeExpiredAsync(cutoff, ct));
        });

        api.MapDelete("/privacy/local-data", async (SqlitePrivacyAndMaintenanceStore store, DpapiProtectedSecretStore secrets, PrivacyRuntimeState runtime, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var databaseResult = await store.ClearAllLocalDataAsync(ct);
            var deletedSecrets = await secrets.DeleteAllSecretsAsync(ct);
            runtime.Replace(runtime.Rules.Where(rule => rule.Source != CaptureRuleSource.User));
            await audit.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.SecretDeleted, clock.UtcNow, "All local Threadline data was cleared.", new Dictionary<string, string>
            {
                ["deletedSecrets"] = deletedSecrets.ToString()
            }), ct);
            return Results.Ok(new { database = databaseResult, deletedSecrets });
        });

        return app;
    }

    private static async Task<IResult> AddExclusionAsync(CaptureRuleType ruleType, PrivacyExclusionRequest request, SqlitePrivacyAndMaintenanceStore store, PrivacyRuntimeState runtime, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Pattern))
        {
            return Results.BadRequest(new { error = "An exclusion pattern is required." });
        }

        var pattern = ruleType == CaptureRuleType.DomainContains ? NormalizeDomain(request.Pattern) : request.Pattern.Trim();
        var rule = await store.AddPrivacyExclusionAsync(ruleType, pattern, request.Reason, cancellationToken);
        runtime.Upsert(rule);
        return Results.Created($"/privacy/exclusions/{rule.Id}", rule);
    }

    private static string NormalizeDomain(string value)
    {
        var trimmed = value.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return uri.Host;
        return trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? trimmed[4..] : trimmed;
    }
}
