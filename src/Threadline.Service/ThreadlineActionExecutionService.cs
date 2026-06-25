using System.Text;
using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Service;

public sealed record ThreadlineActionRunRequest(
    string? WorkThreadId = null,
    string? Transcript = null,
    string? ContextSummary = null,
    string? ArtifactId = null,
    string? Title = null,
    string? Content = null);

public sealed class ThreadlineActionExecutionService
{
    private const int MaxSourceCharacters = 12000;
    private readonly ThreadlineActionCatalog _actions;
    private readonly IWorkThreadRepository _workThreads;
    private readonly IArtifactHistoryRepository _artifactHistory;
    private readonly SqliteWorkContinuityMaintenanceStore _maintenance;
    private readonly IClock _clock;

    public ThreadlineActionExecutionService(
        ThreadlineActionCatalog actions,
        IWorkThreadRepository workThreads,
        IArtifactHistoryRepository artifactHistory,
        SqliteWorkContinuityMaintenanceStore maintenance,
        IClock clock)
    {
        _actions = actions;
        _workThreads = workThreads;
        _artifactHistory = artifactHistory;
        _maintenance = maintenance;
        _clock = clock;
    }

    public async Task<ThreadlineActionExecutionResult> ExecuteAsync(string actionId, ThreadlineActionRunRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new ThreadlineActionRunRequest();
        var action = _actions.Get(actionId);
        if (action is null)
        {
            return ThreadlineActionExecutionResult.Failed(actionId, $"Action '{actionId}' is not registered.");
        }

        try
        {
            return action.Kind switch
            {
                ThreadlineActionKind.Summary or
                ThreadlineActionKind.Handoff or
                ThreadlineActionKind.Decisions or
                ThreadlineActionKind.Risks or
                ThreadlineActionKind.NextActions => await CreateArtifactAsync(action, request, cancellationToken),
                ThreadlineActionKind.CopyArtifact => await CopyArtifactAsync(action, request, cancellationToken),
                ThreadlineActionKind.ExportArtifact => await ExportArtifactAsync(action, request, cancellationToken),
                ThreadlineActionKind.RegenerateArtifact => await RegenerateArtifactAsync(action, request, cancellationToken),
                ThreadlineActionKind.ResumeWork => await ResumeWorkAsync(action, request, cancellationToken),
                ThreadlineActionKind.ClearContext => await ClearContextAsync(action, request, cancellationToken),
                ThreadlineActionKind.ClearConversation => await ClearConversationAsync(action, request, cancellationToken),
                ThreadlineActionKind.ClearMemory => await ClearMemoryAsync(action, request, cancellationToken),
                _ => ThreadlineActionExecutionResult.Failed(action.Id, $"Action '{action.DisplayName}' is cataloged but has no execution handler yet.")
            };
        }
        catch (Exception ex)
        {
            return ThreadlineActionExecutionResult.Failed(
                action.Id,
                $"Action '{action.DisplayName}' failed ({ex.GetType().Name}): {ex.Message}",
                request.WorkThreadId,
                new Dictionary<string, string>
                {
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["stackTrace"] = ex.StackTrace ?? string.Empty
                });
        }
    }

    private async Task<ThreadlineActionExecutionResult> CreateArtifactAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var workThread = await EnsureWorkThreadAsync(request.WorkThreadId, cancellationToken);
        var artifactType = ToArtifactType(action.Kind);
        var title = string.IsNullOrWhiteSpace(request.Title) ? ToArtifactTitle(action.Kind) : request.Title.Trim();
        var source = await BuildActionSourceAsync(workThread.Id, request, cancellationToken);
        var content = BuildArtifactContent(artifactType, title, workThread, source, action.Id);
        var artifact = WorkArtifact.Create(workThread.Id, artifactType, title, content, _clock.UtcNow);
        await _workThreads.SaveArtifactAsync(artifact, cancellationToken);
        await _artifactHistory.SaveArtifactVersionAsync(artifact, "generated", action.Id, cancellationToken);

        return ThreadlineActionExecutionResult.Succeeded(
            action.Id,
            $"Saved {title} artifact.",
            workThread.Id,
            artifact,
            new Dictionary<string, string>
            {
                ["artifactId"] = artifact.Id,
                ["artifactType"] = artifact.ArtifactType,
                ["historyOperation"] = "generated"
            });
    }

    private async Task<ThreadlineActionExecutionResult> CopyArtifactAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var located = await FindRequestedArtifactAsync(request, cancellationToken);
        if (located.Artifact is null)
        {
            return ThreadlineActionExecutionResult.Failed(action.Id, "No artifact is available to copy.", located.WorkThread?.Id);
        }

        return ThreadlineActionExecutionResult.Succeeded(
            action.Id,
            $"Artifact ready for copy: {located.Artifact.Title}.",
            located.Artifact.WorkThreadId,
            located.Artifact,
            new Dictionary<string, string>
            {
                ["artifactId"] = located.Artifact.Id,
                ["content"] = located.Artifact.Content
            });
    }

    private async Task<ThreadlineActionExecutionResult> ExportArtifactAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var located = await FindRequestedArtifactAsync(request, cancellationToken);
        if (located.Artifact is null)
        {
            return ThreadlineActionExecutionResult.Failed(action.Id, "No artifact is available to export.", located.WorkThread?.Id);
        }

        var fileName = BuildExportFileName(located.Artifact);
        return ThreadlineActionExecutionResult.Succeeded(
            action.Id,
            $"Artifact export prepared: {fileName}.",
            located.Artifact.WorkThreadId,
            located.Artifact,
            new Dictionary<string, string>
            {
                ["artifactId"] = located.Artifact.Id,
                ["fileName"] = fileName,
                ["contentType"] = "text/markdown",
                ["content"] = located.Artifact.Content
            });
    }

    private async Task<ThreadlineActionExecutionResult> RegenerateArtifactAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var located = await FindRequestedArtifactAsync(request, cancellationToken);
        if (located.Artifact is null || located.WorkThread is null)
        {
            return ThreadlineActionExecutionResult.Failed(action.Id, "No artifact is available to regenerate.", located.WorkThread?.Id);
        }

        var source = await BuildActionSourceAsync(located.Artifact.WorkThreadId, request, cancellationToken);
        var regenerated = located.Artifact with
        {
            Content = BuildArtifactContent(located.Artifact.ArtifactType, located.Artifact.Title, located.WorkThread, source, action.Id),
            UpdatedAt = _clock.UtcNow
        };
        await _workThreads.SaveArtifactAsync(regenerated, cancellationToken);
        await _artifactHistory.SaveArtifactVersionAsync(regenerated, "regenerated", action.Id, cancellationToken);

        return ThreadlineActionExecutionResult.Succeeded(
            action.Id,
            $"Regenerated artifact and saved history: {regenerated.Title}.",
            regenerated.WorkThreadId,
            regenerated,
            new Dictionary<string, string>
            {
                ["artifactId"] = regenerated.Id,
                ["historyOperation"] = "regenerated"
            });
    }

    private async Task<ThreadlineActionExecutionResult> ResumeWorkAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var workThread = await EnsureWorkThreadAsync(request.WorkThreadId, cancellationToken);
        var resumed = workThread.Resume(_clock.UtcNow);
        await _workThreads.SaveWorkThreadAsync(resumed, cancellationToken);
        return ThreadlineActionExecutionResult.Succeeded(action.Id, $"Resumed Work Thread: {resumed.Title}.", resumed.Id, metadata: new Dictionary<string, string> { ["title"] = resumed.Title });
    }

    private async Task<ThreadlineActionExecutionResult> ClearContextAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var workThread = await ResolveRequiredWorkThreadAsync(request.WorkThreadId, cancellationToken);
        if (workThread is null) return ThreadlineActionExecutionResult.Failed(action.Id, "No active Work Thread exists for context clearing.");
        var affected = await _maintenance.ClearContextAsync(workThread.Id, cancellationToken);
        return ThreadlineActionExecutionResult.Succeeded(action.Id, "Cleared Work Thread context without deleting conversation or memory.", workThread.Id, metadata: new Dictionary<string, string> { ["affectedRows"] = affected.ToString() });
    }

    private async Task<ThreadlineActionExecutionResult> ClearConversationAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var workThread = await ResolveRequiredWorkThreadAsync(request.WorkThreadId, cancellationToken);
        if (workThread is null) return ThreadlineActionExecutionResult.Failed(action.Id, "No active Work Thread exists for conversation clearing.");
        var affected = await _maintenance.ClearConversationAsync(workThread.Id, cancellationToken);
        return ThreadlineActionExecutionResult.Succeeded(action.Id, "Cleared Work Thread conversation without deleting context, artifacts, or memory.", workThread.Id, metadata: new Dictionary<string, string> { ["affectedRows"] = affected.ToString() });
    }

    private async Task<ThreadlineActionExecutionResult> ClearMemoryAsync(ThreadlineActionDefinition action, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var workThread = await ResolveRequiredWorkThreadAsync(request.WorkThreadId, cancellationToken);
        if (workThread is null) return ThreadlineActionExecutionResult.Failed(action.Id, "No active Work Thread exists for memory clearing.");
        var affected = await _maintenance.ClearMemoryAsync(workThread.Id, cancellationToken);
        return ThreadlineActionExecutionResult.Succeeded(action.Id, "Cleared Work Thread memory, including context, conversation, artifacts, and history.", workThread.Id, metadata: new Dictionary<string, string> { ["affectedRows"] = affected.ToString() });
    }

    private async Task<WorkThread> EnsureWorkThreadAsync(string? requestedWorkThreadId, CancellationToken cancellationToken)
    {
        var workThread = await ResolveRequiredWorkThreadAsync(requestedWorkThreadId, cancellationToken);
        if (workThread is not null)
        {
            return workThread;
        }

        var created = WorkThread.Create($"Threadline Work {_clock.UtcNow:g}", _clock.UtcNow, "Created by registered action execution.");
        await _workThreads.SaveWorkThreadAsync(created, cancellationToken);
        return created;
    }

    private async Task<WorkThread?> ResolveRequiredWorkThreadAsync(string? requestedWorkThreadId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedWorkThreadId))
        {
            return await _workThreads.GetWorkThreadAsync(requestedWorkThreadId.Trim(), cancellationToken);
        }

        return await _workThreads.GetActiveWorkThreadAsync(cancellationToken);
    }

    private async Task<(WorkThread? WorkThread, WorkArtifact? Artifact)> FindRequestedArtifactAsync(ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var workThread = await ResolveRequiredWorkThreadAsync(request.WorkThreadId, cancellationToken);
        if (workThread is null)
        {
            return (null, null);
        }

        var artifacts = await _workThreads.GetArtifactsAsync(workThread.Id, 50, cancellationToken);
        var artifact = string.IsNullOrWhiteSpace(request.ArtifactId)
            ? artifacts.OrderByDescending(a => a.UpdatedAt).FirstOrDefault()
            : artifacts.FirstOrDefault(a => string.Equals(a.Id, request.ArtifactId.Trim(), StringComparison.OrdinalIgnoreCase));
        return (workThread, artifact);
    }

    private async Task<string> BuildActionSourceAsync(string workThreadId, ThreadlineActionRunRequest request, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        AppendSection(builder, "Current transcript", request.Transcript);
        AppendSection(builder, "Current context", request.ContextSummary);
        AppendSection(builder, "Requested content", request.Content);

        if (builder.Length == 0)
        {
            var messages = await _workThreads.GetConversationMessagesAsync(workThreadId, 100, cancellationToken);
            AppendSection(builder, "Stored conversation", string.Join(Environment.NewLine, messages.Select(message => $"{message.Role}: {message.Content}")));
        }

        var contextEvents = await _workThreads.GetRecentWorkContextEventsAsync(workThreadId, 20, cancellationToken);
        if (contextEvents.Count > 0)
        {
            AppendSection(builder, "Stored context events", string.Join(Environment.NewLine, contextEvents.Select(contextEvent => $"{contextEvent.SourceType}/{contextEvent.SourceName}: {contextEvent.ContentSummary}")));
        }

        var source = builder.ToString().Trim();
        if (source.Length <= MaxSourceCharacters) return source;
        return source[^MaxSourceCharacters..].TrimStart();
    }

    private string BuildArtifactContent(string artifactType, string title, WorkThread workThread, string source, string actionId)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine($"Created: {_clock.UtcNow:g}");
        builder.AppendLine($"Work Thread: {workThread.Title}");
        builder.AppendLine($"Action: {actionId}");
        builder.AppendLine("Source: registered action execution over current conversation, context, and Work Thread memory");
        builder.AppendLine();

        switch (artifactType)
        {
            case "Summary":
                builder.AppendLine("## Summary");
                builder.AppendLine(Summarize(source));
                builder.AppendLine();
                builder.AppendLine("## Source notes");
                builder.AppendLine(Summarize(source, 2000));
                break;
            case "Handoff":
                builder.AppendLine("## Current state");
                builder.AppendLine(Summarize(source));
                builder.AppendLine();
                builder.AppendLine("## Handoff notes");
                builder.AppendLine("- Resume from this Work Thread before taking the next action.");
                builder.AppendLine("- Review the latest context receipt and artifact history before changing direction.");
                builder.AppendLine("- Validate inferred details before acting outside the current app/context boundary.");
                break;
            case "Decisions":
                builder.AppendLine("## Decisions / commitments");
                AppendFilteredLines(builder, source, ["decid", "decision", "choose", "selected", "will ", "agreed", "commit", "approved", "merged"], "No explicit decisions detected yet.");
                break;
            case "Risks":
                builder.AppendLine("## Risks / watchouts");
                AppendFilteredLines(builder, source, ["risk", "warning", "limitation", "blocked", "error", "failed", "not used", "privacy", "degraded", "unsafe"], "No explicit risks detected yet.");
                break;
            case "NextActions":
                builder.AppendLine("## Next actions");
                AppendFilteredLines(builder, source, ["next", "action", "todo", "follow up", "should", "need to", "verify", "validate", "run", "commit", "merge"], "No explicit next actions detected yet.");
                break;
            default:
                builder.AppendLine(Summarize(source, 3000));
                break;
        }

        return builder.ToString().Trim();
    }

    private static void AppendSection(StringBuilder builder, string heading, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (builder.Length > 0) builder.AppendLine().AppendLine();
        builder.AppendLine($"## {heading}");
        builder.AppendLine(value.Trim());
    }

    private static string Summarize(string? value, int maxCharacters = 1200)
    {
        if (string.IsNullOrWhiteSpace(value)) return "No content available yet.";
        var normalized = value.Replace("\r", " ").Trim();
        return normalized.Length <= maxCharacters ? normalized : normalized[..maxCharacters].TrimEnd() + "...";
    }

    private static void AppendFilteredLines(StringBuilder builder, string source, string[] keywords, string fallback)
    {
        var lines = source
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Take(20)
            .ToList();

        if (lines.Count == 0)
        {
            builder.AppendLine($"- {fallback}");
            return;
        }

        foreach (var line in lines)
        {
            builder.AppendLine($"- {line}");
        }
    }

    private static string ToArtifactType(ThreadlineActionKind kind) => kind switch
    {
        ThreadlineActionKind.Summary => "Summary",
        ThreadlineActionKind.Handoff => "Handoff",
        ThreadlineActionKind.Decisions => "Decisions",
        ThreadlineActionKind.Risks => "Risks",
        ThreadlineActionKind.NextActions => "NextActions",
        _ => "Artifact"
    };

    private static string ToArtifactTitle(ThreadlineActionKind kind) => kind switch
    {
        ThreadlineActionKind.Summary => "Thread Summary",
        ThreadlineActionKind.Handoff => "Work Handoff",
        ThreadlineActionKind.Decisions => "Decision Log",
        ThreadlineActionKind.Risks => "Risks and Watchouts",
        ThreadlineActionKind.NextActions => "Next Actions",
        _ => "Threadline Artifact"
    };

    private static string BuildExportFileName(WorkArtifact artifact)
    {
        var safeTitle = new string(artifact.Title.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = artifact.ArtifactType;
        return $"{safeTitle}-{artifact.UpdatedAt:yyyyMMdd-HHmmss}.md";
    }
}
