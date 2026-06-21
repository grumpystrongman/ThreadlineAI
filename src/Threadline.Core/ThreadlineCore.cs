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
public enum CaptureRuleSource { Default, User, Organization, Runtime }

public sealed record CaptureRule(string Id, CaptureRuleType RuleType, string Pattern, CaptureRuleAction Action, DateTimeOffset CreatedAt, CaptureRuleSource Source = CaptureRuleSource.Default)
{
    public static CaptureRule Create(CaptureRuleType ruleType, string pattern, CaptureRuleAction action, DateTimeOffset now, CaptureRuleSource source = CaptureRuleSource.Default) =>
        new($"rule_{Guid.NewGuid():N}", ruleType, pattern.Trim(), action, now, source);
}

public sealed record CaptureDecision(bool IsAllowed, string Reason, bool RequiresExplicitApproval = false, CaptureRule? MatchedRule = null)
{
    public static CaptureDecision Allow(string reason = "Allowed", CaptureRule? matchedRule = null) => new(true, reason, false, matchedRule);
    public static CaptureDecision Ask(string reason, CaptureRule? matchedRule = null) => new(true, reason, true, matchedRule);
    public static CaptureDecision Block(string reason, CaptureRule? matchedRule = null) => new(false, reason, false, matchedRule);
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
                CaptureRuleAction.Allow => CaptureDecision.Allow($"Allowed by {rule.Source} rule {rule.Id}", rule),
                CaptureRuleAction.Ask => CaptureDecision.Ask($"Approval required by {rule.Source} rule {rule.Id}", rule),
                CaptureRuleAction.Block => CaptureDecision.Block($"Blocked by {rule.Source} rule {rule.Id}", rule),
                _ => CaptureDecision.Ask("Unknown rule action", rule)
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

public enum RedactionKind
{
    GenericSecret,
    ApiKey,
    BearerToken,
    Jwt,
    PrivateKey,
    ConnectionString,
    EmailAddress,
    PhoneNumber,
    SocialSecurityNumber,
    UrlSecret,
    PhiMarker
}

public sealed record RedactionFinding(RedactionKind Kind, string Label, int StartIndex, int Length);
public sealed record RedactionResult(string OriginalText, string RedactedText, IReadOnlyList<RedactionFinding> Findings)
{
    public bool WasRedacted => Findings.Count > 0;
}

public sealed class SecretRedactor
{
    private static readonly RedactionRule[] Rules =
    [
        new(RedactionKind.PrivateKey, "private key", new Regex("-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled)),
        new(RedactionKind.ApiKey, "OpenAI-style API key", new Regex(@"sk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled)),
        new(RedactionKind.BearerToken, "bearer token", new Regex(@"(?i)(authorization:\s*)bearer\s+[A-Za-z0-9._\-]{20,}", RegexOptions.Compiled), PreservePrefix),
        new(RedactionKind.BearerToken, "bearer token", new Regex(@"(?i)\bbearer\s+[A-Za-z0-9._\-]{20,}", RegexOptions.Compiled)),
        new(RedactionKind.Jwt, "JWT", new Regex(@"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\b", RegexOptions.Compiled)),
        new(RedactionKind.ConnectionString, "connection string", new Regex(@"(?i)\b(server|data source|user id|uid|password|pwd)\s*=\s*[^;\r\n]+(;\s*(server|data source|user id|uid|password|pwd)\s*=\s*[^;\r\n]+)+", RegexOptions.Compiled)),
        new(RedactionKind.UrlSecret, "URL secret parameter", new Regex(@"(?i)([?&](api[_-]?key|token|access_token|client_secret|password)=)[^&\s]+", RegexOptions.Compiled), PreservePrefix),
        new(RedactionKind.GenericSecret, "named secret", new Regex("(?i)(api[_-]?key|secret|token|password|client_secret)\\s*[:=]\\s*['\"]?[^\\s'\"]+", RegexOptions.Compiled), PreserveAssignmentPrefix),
        new(RedactionKind.EmailAddress, "email address", new Regex(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new(RedactionKind.PhoneNumber, "phone number", new Regex(@"\b(?:\+?1[\s.\-]?)?(?:\(?\d{3}\)?[\s.\-]?)\d{3}[\s.\-]?\d{4}\b", RegexOptions.Compiled)),
        new(RedactionKind.SocialSecurityNumber, "social security number", new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)),
        new(RedactionKind.PhiMarker, "medical record number", new Regex(@"(?i)\b(MRN|medical record number|patient id)\s*[:#=]?\s*[A-Za-z0-9\-]{5,}\b", RegexOptions.Compiled))
    ];

    public string Redact(string input) => Analyze(input).RedactedText;

    public RedactionResult Analyze(string? input)
    {
        var original = input ?? string.Empty;
        var redacted = original;
        var findings = new List<RedactionFinding>();

        foreach (var rule in Rules)
        {
            var matches = rule.Pattern.Matches(redacted);
            if (matches.Count == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                findings.Add(new RedactionFinding(rule.Kind, rule.Label, match.Index, match.Length));
            }

            redacted = rule.Pattern.Replace(redacted, match => rule.Replace(match));
        }

        return new RedactionResult(original, redacted, findings);
    }

    private static string PreserveAssignmentPrefix(Match match)
    {
        var value = match.Value;
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex >= 0)
        {
            return value[..separatorIndex] + ": [REDACTED]";
        }

        separatorIndex = value.IndexOf('=');
        return separatorIndex >= 0 ? value[..separatorIndex] + "=[REDACTED]" : "[REDACTED]";
    }

    private static string PreservePrefix(Match match) =>
        match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value + "[REDACTED]" : "[REDACTED]";

    private sealed record RedactionRule(RedactionKind Kind, string Label, Regex Pattern, Func<Match, string>? ReplacementFactory = null)
    {
        public string Replace(Match match) => ReplacementFactory is null ? "[REDACTED]" : ReplacementFactory(match);
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
        var system = """
You are Threadline, a context-aware Windows sidecar assistant for real work. Your job is not to give a shallow summary. Produce useful, executive-quality analysis grounded only in the supplied approved context.

Rules:
- Use the supplied context and be explicit when context is thin or missing.
- Never pretend you can see page text unless browser-extension context or other supplied page text is present.
- If the source is only an app/window title, say that the browser extension is needed for deeper page-level understanding.
- Prefer depth over speed: explain what matters, why it matters, what evidence supports it, what is uncertain, and what the user should do next.
- Avoid generic filler. Make the answer operationally useful.
""";
        var user = new StringBuilder();
        user.AppendLine("USER QUESTION:").AppendLine(context.UserQuestion.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(context.CurrentWindow)) user.AppendLine("CURRENT RESOLVED CONTEXT:").AppendLine(context.CurrentWindow).AppendLine();
        if (!string.IsNullOrWhiteSpace(context.SessionSummary)) user.AppendLine("SESSION MEMORY SUMMARY:").AppendLine(context.SessionSummary).AppendLine();
        user.AppendLine("RECENT APPROVED EVENTS:");
        foreach (var item in context.RelevantEvents.OrderBy(e => e.Timestamp)) user.AppendLine($"- [{item.Timestamp:u}] {item.Source}/{item.ContextType}: {Trim(item.Content, 2400)}");
        user.AppendLine();
        user.AppendLine("ANSWER FORMAT:");
        user.AppendLine("1. Bottom line — the practical answer in plain English.");
        user.AppendLine("2. What the context actually shows — cite the supplied context/events, not external knowledge.");
        user.AppendLine("3. Deeper analysis — meaning, implications, patterns, and why this matters.");
        user.AppendLine("4. Gaps / uncertainty — what Threadline cannot know yet, including whether browser-extension context is needed.");
        user.AppendLine("5. Recommended next actions — concrete steps the user can take now.");
        return [LlmMessage.System(system), LlmMessage.User(user.ToString())];
    }

    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "... [trimmed]";
}
