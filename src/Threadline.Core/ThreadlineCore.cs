using System.Text;
using System.Text.RegularExpressions;

namespace Threadline.Core;

public enum ContextSource { Unknown, ActiveWindow, Browser, PowerShell, Terminal, UiAutomation, Screenshot, UserSelection, Manual }
public enum SensitivityLevel { Normal, ContainsPersonalData, ContainsSecret, ContainsPhi, Blocked }
public enum SessionStatus { Active, Paused, Ended }

public sealed record ThreadlineSession(string Id, string Name, DateTimeOffset CreatedAt, SessionStatus Status, string? ActiveProvider = null, DateTimeOffset? EndedAt = null)
{
    public static ThreadlineSession Start(string name, DateTimeOffset now, string? provider = null) => new($"ses_{Guid.NewGuid():N}", name.Trim(), now, SessionStatus.Active, provider);
    public ThreadlineSession Pause() => this with { Status = SessionStatus.Paused };
    public ThreadlineSession Resume() => this with { Status = SessionStatus.Active };
    public ThreadlineSession End(DateTimeOffset now) => this with { Status = SessionStatus.Ended, EndedAt = now };
}

public sealed record ContextEvent(string Id, string SessionId, DateTimeOffset Timestamp, ContextSource Source, string ContextType, string Content, string? ApplicationName = null, string? ProcessName = null, string? WindowTitle = null, string? Uri = null, SensitivityLevel Sensitivity = SensitivityLevel.Normal, bool UserApproved = true, IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ContextEvent Create(string sessionId, ContextSource source, string contextType, string content, DateTimeOffset timestamp, string? applicationName = null, string? processName = null, string? windowTitle = null, string? uri = null, SensitivityLevel sensitivity = SensitivityLevel.Normal, bool userApproved = true, IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"evt_{Guid.NewGuid():N}", sessionId, timestamp, source, contextType, content, applicationName, processName, windowTitle, uri, sensitivity, userApproved, metadata);
}

public enum CaptureRuleType { ApplicationName, ProcessName, WindowTitleContains, DomainContains, UriContains }
public enum CaptureRuleAction { Allow, Ask, Block }
public sealed record CaptureRule(string Id, CaptureRuleType RuleType, string Pattern, CaptureRuleAction Action, DateTimeOffset CreatedAt)
{
    public static CaptureRule Create(CaptureRuleType ruleType, string pattern, CaptureRuleAction action, DateTimeOffset now) => new($"rule_{Guid.NewGuid():N}", ruleType, pattern.Trim(), action, now);
}

public sealed record CaptureDecision(bool IsAllowed, string Reason, bool RequiresExplicitApproval = false)
{
    public static CaptureDecision Allow(string reason = "Allowed") => new(true, reason);
    public static CaptureDecision Ask(string reason) => new(true, reason, true);
    public static CaptureDecision Block(string reason) => new(false, reason);
}

public sealed class CapturePolicy
{
    private readonly IReadOnlyList<CaptureRule> _rules;
    public CapturePolicy(IEnumerable<CaptureRule> rules) => _rules = rules.ToArray();

    public CaptureDecision Evaluate(ContextEvent contextEvent)
    {
        if (contextEvent.Sensitivity is SensitivityLevel.Blocked or SensitivityLevel.ContainsSecret)
            return CaptureDecision.Block($"Blocked sensitivity: {contextEvent.Sensitivity}");

        foreach (var rule in _rules)
        {
            if (!Matches(rule, contextEvent)) continue;
            return rule.Action switch
            {
                CaptureRuleAction.Allow => CaptureDecision.Allow($"Allowed by rule {rule.Id}"),
                CaptureRuleAction.Ask => CaptureDecision.Ask($"Approval required by rule {rule.Id}"),
                CaptureRuleAction.Block => CaptureDecision.Block($"Blocked by rule {rule.Id}"),
                _ => CaptureDecision.Ask("Unknown rule action")
            };
        }

        return contextEvent.Source == ContextSource.Screenshot
            ? CaptureDecision.Ask("Screenshots require explicit approval by default.")
            : CaptureDecision.Allow("No blocking rule matched.");
    }

    private static bool Matches(CaptureRule rule, ContextEvent contextEvent)
    {
        static bool Contains(string? value, string pattern) => !string.IsNullOrWhiteSpace(value) && value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        return rule.RuleType switch
        {
            CaptureRuleType.ApplicationName => Contains(contextEvent.ApplicationName, rule.Pattern),
            CaptureRuleType.ProcessName => Contains(contextEvent.ProcessName, rule.Pattern),
            CaptureRuleType.WindowTitleContains => Contains(contextEvent.WindowTitle, rule.Pattern),
            CaptureRuleType.DomainContains => Contains(contextEvent.Uri, rule.Pattern),
            CaptureRuleType.UriContains => Contains(contextEvent.Uri, rule.Pattern),
            _ => false
        };
    }
}

public sealed class SecretRedactor
{
    private static readonly Regex[] Patterns =
    [
        new Regex("(?i)(api[_-]?key|secret|token|password)\\s*[:=]\\s*['\"]?[^\\s'\"]+", RegexOptions.Compiled),
        new Regex(@"sk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled),
        new Regex(@"(?i)bearer\s+[A-Za-z0-9._\-]{20,}", RegexOptions.Compiled),
        new Regex(@"(?i)(authorization:\s*)[^\r\n]+", RegexOptions.Compiled),
        new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)
    ];

    public string Redact(string input)
    {
        var result = input ?? string.Empty;
        foreach (var pattern in Patterns)
        {
            result = pattern.Replace(result, match => match.Value.Contains(':') ? $"{match.Value.Split(':', 2)[0]}: [REDACTED]" : match.Value.Contains('=') ? $"{match.Value.Split('=', 2)[0]}=[REDACTED]" : "[REDACTED]");
        }
        return result;
    }
}

public sealed record LlmProviderCapabilities(bool SupportsStreaming, bool SupportsVision, bool SupportsToolUse, int MaxInputTokens = 0);
public sealed record LlmMessage(string Role, string Content)
{
    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);
}
public sealed record LlmRequest(string Model, IReadOnlyList<LlmMessage> Messages, double Temperature = 0.2, int? MaxOutputTokens = null, IReadOnlyDictionary<string, string>? Metadata = null);
public sealed record LlmResponse(string ProviderName, string Model, string Content, IReadOnlyDictionary<string, string>? Metadata = null);

public interface ILlmProvider
{
    string Name { get; }
    LlmProviderCapabilities Capabilities { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

public interface ISessionRepository
{
    Task<ThreadlineSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ThreadlineSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default);
    Task SaveSessionAsync(ThreadlineSession session, CancellationToken cancellationToken = default);
    Task AppendEventAsync(ContextEvent contextEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContextEvent>> GetRecentEventsAsync(string sessionId, int take, CancellationToken cancellationToken = default);
    Task SaveSummaryAsync(string sessionId, string summary, CancellationToken cancellationToken = default);
    Task<string?> GetLatestSummaryAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IClock { DateTimeOffset UtcNow { get; } }

public sealed record ThreadlinePromptContext(string UserQuestion, string? CurrentWindow, string? SessionSummary, IReadOnlyList<ContextEvent> RelevantEvents, bool IncludeEvidence = true);

public sealed class PromptComposer
{
    public IReadOnlyList<LlmMessage> Compose(ThreadlinePromptContext context)
    {
        var system = "You are Threadline, a Windows contextual AI assistant. Help the user understand their current approved work session. Use only the supplied context. If context is missing, say exactly what is missing.";
        var user = new StringBuilder();
        user.AppendLine("USER QUESTION:").AppendLine(context.UserQuestion.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(context.CurrentWindow)) user.AppendLine("CURRENT WINDOW:").AppendLine(context.CurrentWindow).AppendLine();
        if (!string.IsNullOrWhiteSpace(context.SessionSummary)) user.AppendLine("SESSION SUMMARY:").AppendLine(context.SessionSummary).AppendLine();
        user.AppendLine("RELEVANT APPROVED EVENTS:");
        foreach (var item in context.RelevantEvents.OrderBy(e => e.Timestamp)) user.AppendLine($"- [{item.Timestamp:u}] {item.Source}/{item.ContextType}: {Trim(item.Content, 1600)}");
        user.AppendLine().AppendLine("ANSWER FORMAT:").AppendLine("1. Direct answer").AppendLine("2. Evidence from supplied context").AppendLine("3. Recommended next steps");
        return [LlmMessage.System(system), LlmMessage.User(user.ToString())];
    }

    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "... [trimmed]";
}
