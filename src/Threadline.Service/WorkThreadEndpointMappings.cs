using Threadline.Core;
using Threadline.Infrastructure.Security;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Service;

public static class WorkThreadEndpointMappings
{
    public static WebApplication MapThreadlineWorkThreadApi(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty).RequireThreadlineLocalAccess();

        api.MapGet("/work-threads/active", async (IWorkThreadRepository repository, CancellationToken ct) =>
        {
            var thread = await repository.GetActiveWorkThreadAsync(ct);
            IResult result = thread is null ? Results.NotFound() : Results.Ok(thread);
            return result;
        });

        api.MapGet("/work-threads", async (int? take, IWorkThreadRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.ListWorkThreadsAsync(take ?? 25, ct)));

        api.MapPost("/work-threads", async (StartWorkThreadRequest request, IWorkThreadRepository repository, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Work thread title is required.");
            var thread = WorkThread.Create(request.Title, clock.UtcNow, request.Description);
            await repository.SaveWorkThreadAsync(thread, ct);
            return Results.Created($"/work-threads/{thread.Id}", thread);
        });

        api.MapPost("/work-threads/{workThreadId}/resume", async (string workThreadId, IWorkThreadRepository repository, IClock clock, CancellationToken ct) =>
        {
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();
            var resumed = thread.Resume(clock.UtcNow);
            await repository.SaveWorkThreadAsync(resumed, ct);
            return Results.Ok(resumed);
        });

        api.MapPost("/work-threads/{workThreadId}/rename", async (string workThreadId, RenameWorkThreadRequest request, IWorkThreadRepository repository, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Work thread title is required.");
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();
            var renamed = thread.Rename(request.Title, clock.UtcNow, request.Description ?? thread.Description);
            await repository.SaveWorkThreadAsync(renamed, ct);
            return Results.Ok(renamed);
        });

        api.MapPost("/work-threads/{workThreadId}/close", async (string workThreadId, IWorkThreadRepository repository, IClock clock, CancellationToken ct) =>
        {
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();
            var closed = thread.Close(clock.UtcNow);
            await repository.SaveWorkThreadAsync(closed, ct);
            return Results.Ok(closed);
        });

        api.MapGet("/work-threads/{workThreadId}/export", async (string workThreadId, SqlitePrivacyAndMaintenanceStore maintenance, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var export = await maintenance.ExportWorkThreadAsync(workThreadId, ct);
            if (export is null) return Results.NotFound();
            await audit.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.ContextPreviewed, clock.UtcNow, $"Work thread exported: {workThreadId}", new Dictionary<string, string> { ["workThreadId"] = workThreadId }), ct);
            return Results.Ok(export);
        });

        api.MapDelete("/work-threads/{workThreadId}", async (string workThreadId, SqlitePrivacyAndMaintenanceStore maintenance, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var deleted = await maintenance.DeleteWorkThreadAsync(workThreadId, ct);
            if (!deleted) return Results.NotFound();
            await audit.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.ContextBlocked, clock.UtcNow, $"Work thread deleted: {workThreadId}", new Dictionary<string, string> { ["workThreadId"] = workThreadId }), ct);
            return Results.NoContent();
        });

        api.MapGet("/work-threads/{workThreadId}/context-events", async (string workThreadId, int? take, IWorkThreadRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.GetRecentWorkContextEventsAsync(workThreadId, take ?? 20, ct)));

        api.MapPost("/work-threads/{workThreadId}/context-events", async (string workThreadId, SaveWorkContextEventRequest request, IWorkThreadRepository repository, CapturePolicy capturePolicy, SecretRedactor redactor, IAuditRepository audit, IClock clock, CancellationToken ct) =>
        {
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();

            var probe = ContextEvent.Create(
                sessionId: workThreadId,
                source: ContextSource.Manual,
                contextType: request.SourceType,
                content: request.ContentSummary ?? request.SourceName,
                timestamp: clock.UtcNow,
                applicationName: request.AppName,
                processName: null,
                windowTitle: request.WindowTitle,
                uri: request.Url,
                sensitivity: SensitivityLevel.Normal,
                userApproved: true);
            var decision = capturePolicy.Evaluate(probe);
            if (!decision.IsAllowed || decision.RequiresExplicitApproval)
            {
                await audit.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.ContextBlocked, clock.UtcNow, decision.Reason, new Dictionary<string, string>
                {
                    ["workThreadId"] = workThreadId,
                    ["sourceType"] = request.SourceType,
                    ["sourceName"] = request.SourceName,
                    ["appName"] = request.AppName ?? string.Empty,
                    ["url"] = request.Url ?? string.Empty
                }), ct);
                return Results.Json(new { error = "Work Thread context was blocked by privacy policy.", reason = decision.Reason }, statusCode: StatusCodes.Status403Forbidden);
            }

            var redactedSummary = request.ContentSummary is null ? null : redactor.Redact(request.ContentSummary);
            var contextEvent = WorkContextEvent.Create(
                workThreadId,
                request.SourceType,
                request.SourceName,
                request.CaptureMode,
                clock.UtcNow,
                request.AppName,
                request.WindowTitle,
                request.Url,
                redactedSummary);
            await repository.AppendWorkContextEventAsync(contextEvent, ct);
            return Results.Json(contextEvent, statusCode: StatusCodes.Status202Accepted);
        });

        api.MapGet("/work-threads/{workThreadId}/messages", async (string workThreadId, int? take, IWorkThreadRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.GetConversationMessagesAsync(workThreadId, take ?? 100, ct)));

        api.MapPost("/work-threads/{workThreadId}/messages", async (string workThreadId, SaveConversationMessageRequest request, IWorkThreadRepository repository, SecretRedactor redactor, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content)) return Results.BadRequest("Message content is required.");
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();
            var message = ConversationMessage.Create(workThreadId, request.Role, redactor.Redact(request.Content), clock.UtcNow, request.ContextReceiptId);
            await repository.AppendConversationMessageAsync(message, ct);
            return Results.Json(message, statusCode: StatusCodes.Status202Accepted);
        });

        api.MapPost("/work-threads/{workThreadId}/context-receipts", async (string workThreadId, SaveContextReceiptRequest request, IWorkThreadRepository repository, SecretRedactor redactor, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UsedSourcesJson)) return Results.BadRequest("Used sources are required.");
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();
            var receipt = ContextReceiptRecord.Create(workThreadId, redactor.Redact(request.UsedSourcesJson), clock.UtcNow, request.NotUsedSourcesJson is null ? null : redactor.Redact(request.NotUsedSourcesJson), request.Limitations is null ? null : redactor.Redact(request.Limitations));
            await repository.SaveContextReceiptAsync(receipt, ct);
            return Results.Created($"/work-threads/{workThreadId}/context-receipts/{receipt.Id}", receipt);
        });

        api.MapPost("/work-threads/{workThreadId}/artifacts", async (string workThreadId, SaveArtifactRequest request, IWorkThreadRepository repository, IArtifactHistoryRepository history, SecretRedactor redactor, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content)) return Results.BadRequest("Artifact content is required.");
            var thread = await repository.GetWorkThreadAsync(workThreadId, ct);
            if (thread is null) return Results.NotFound();
            var artifact = WorkArtifact.Create(workThreadId, request.ArtifactType, request.Title, redactor.Redact(request.Content), clock.UtcNow, request.ContextReceiptId);
            await repository.SaveArtifactAsync(artifact, ct);
            await history.SaveArtifactVersionAsync(artifact, "saved", "work-thread.artifacts.save", ct);
            return Results.Created($"/work-threads/{workThreadId}/artifacts/{artifact.Id}", artifact);
        });

        api.MapGet("/work-threads/{workThreadId}/artifacts", async (string workThreadId, int? take, IWorkThreadRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.GetArtifactsAsync(workThreadId, take ?? 25, ct)));

        api.MapGet("/work-threads/{workThreadId}/artifacts/{artifactId}/history", async (string workThreadId, string artifactId, IArtifactHistoryRepository history, CancellationToken ct) =>
            Results.Ok((await history.GetArtifactHistoryAsync(artifactId, ct)).Where(version => string.Equals(version.WorkThreadId, workThreadId, StringComparison.OrdinalIgnoreCase))));

        api.MapGet("/work-threads/{workThreadId}/artifacts/{artifactId}/export", async (string workThreadId, string artifactId, IWorkThreadRepository repository, CancellationToken ct) =>
        {
            var artifact = (await repository.GetArtifactsAsync(workThreadId, 100, ct))
                .FirstOrDefault(item => string.Equals(item.Id, artifactId, StringComparison.OrdinalIgnoreCase));
            if (artifact is null) return Results.NotFound();
            return Results.Ok(new ArtifactExportResponse(BuildExportFileName(artifact), "text/markdown", artifact.Content, artifact));
        });

        api.MapPost("/work-threads/{workThreadId}/artifacts/{artifactId}/regenerate", async (string workThreadId, string artifactId, RegenerateArtifactRequest request, ThreadlineActionExecutionService actions, CancellationToken ct) =>
            Results.Ok(await actions.ExecuteAsync("artifact.regenerate", new ThreadlineActionRunRequest(workThreadId, request.Transcript, request.ContextSummary, artifactId), ct)));

        return app;
    }

    private static string BuildExportFileName(WorkArtifact artifact)
    {
        var safeTitle = new string(artifact.Title.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = artifact.ArtifactType;
        return $"{safeTitle}-{artifact.UpdatedAt:yyyyMMdd-HHmmss}.md";
    }
}
