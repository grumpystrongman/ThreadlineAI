using System.Text.RegularExpressions;

namespace Threadline.Windows.Services;

public sealed record RedactionResult(
    string Text,
    int RedactionCount,
    IReadOnlyList<string> Categories);

public static class SensitiveContentRedactor
{
    private static readonly (string Category, Regex Pattern, string Replacement)[] Rules =
    [
        ("email", new Regex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[redacted-email]"),
        ("ssn", new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled), "[redacted-ssn]"),
        ("phone", new Regex(@"(?<!\d)(?:\+?1[\s.-]?)?(?:\(?\d{3}\)?[\s.-]?)\d{3}[\s.-]?\d{4}(?!\d)", RegexOptions.Compiled), "[redacted-phone]"),
        ("credit-card-like-number", new Regex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled), "[redacted-card-like-number]"),
        ("api-key", new Regex("\\b(?:api[_-]?key|secret|token|password|pwd)\\s*[:=]\\s*['\\\"]?[^\\s'\\\";,]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[redacted-secret]"),
        ("bearer-token", new Regex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{12,}", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Bearer [redacted-token]"),
        ("connection-string-password", new Regex(@"\bPassword\s*=\s*[^;\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Password=[redacted]")
    ];

    public static RedactionResult Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RedactionResult(string.Empty, 0, []);
        }

        var redacted = value;
        var count = 0;
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            var matches = rule.Pattern.Matches(redacted);
            if (matches.Count == 0) continue;

            count += matches.Count;
            categories.Add(rule.Category);
            redacted = rule.Pattern.Replace(redacted, rule.Replacement);
        }

        return new RedactionResult(redacted, count, categories.ToList());
    }
}
