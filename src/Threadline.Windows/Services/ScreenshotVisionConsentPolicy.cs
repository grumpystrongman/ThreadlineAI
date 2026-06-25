namespace Threadline.Windows.Services;

public enum ScreenshotVisionAppPolicy
{
    PromptEachTime,
    Allowed,
    Denied
}

public sealed record ScreenshotVisionConsentDecision(
    string AppKey,
    string AppDisplayName,
    ScreenshotVisionAppPolicy AppPolicy,
    bool UserApprovedThisCapture,
    bool RawScreenshotStorageAllowed,
    bool Allowed,
    string Reason)
{
    public bool AppPolicyAllowsCapture => AppPolicy != ScreenshotVisionAppPolicy.Denied;

    public string ToStatusText()
    {
        var policy = AppPolicy switch
        {
            ScreenshotVisionAppPolicy.Allowed => "app allowed",
            ScreenshotVisionAppPolicy.Denied => "app denied",
            _ => "ask each time"
        };

        var capture = Allowed ? "ready for one approved screenshot/OCR" : "blocked";
        return $"Vision: {capture} ({policy}; {Reason})";
    }
}

public sealed class ScreenshotVisionConsentPolicy
{
    private readonly Dictionary<string, ScreenshotVisionAppPolicy> _perAppPolicies = new(StringComparer.OrdinalIgnoreCase);

    private Func<string, string, Task>? _persistPolicy;
    private Func<string, Task>? _removePolicy;

    public void SetPersistenceCallbacks(Func<string, string, Task> persist, Func<string, Task> remove)
    {
        _persistPolicy = persist;
        _removePolicy = remove;
    }

    public void LoadPersistedPolicies(IReadOnlyList<ScreenshotVisionPolicyDto> policies)
    {
        foreach (var entry in policies)
        {
            if (Enum.TryParse<ScreenshotVisionAppPolicy>(entry.Policy, ignoreCase: true, out var parsed) &&
                parsed != ScreenshotVisionAppPolicy.PromptEachTime)
            {
                _perAppPolicies[entry.AppKey] = parsed;
            }
        }
    }

    public ScreenshotVisionConsentDecision Evaluate(ActiveWindowSnapshot window, bool userApprovedThisCapture, bool rawScreenshotStorageAllowed = false)
    {
        var key = BuildAppKey(window);
        var policy = _perAppPolicies.TryGetValue(key, out var savedPolicy)
            ? savedPolicy
            : ScreenshotVisionAppPolicy.PromptEachTime;

        if (policy == ScreenshotVisionAppPolicy.Denied)
        {
            return new ScreenshotVisionConsentDecision(
                key,
                BuildAppDisplayName(window),
                policy,
                userApprovedThisCapture,
                rawScreenshotStorageAllowed,
                false,
                "This app is on the screenshot/OCR deny list.");
        }

        if (!userApprovedThisCapture)
        {
            return new ScreenshotVisionConsentDecision(
                key,
                BuildAppDisplayName(window),
                policy,
                false,
                rawScreenshotStorageAllowed,
                false,
                "No one-time screenshot/OCR approval is active.");
        }

        return new ScreenshotVisionConsentDecision(
            key,
            BuildAppDisplayName(window),
            policy,
            true,
            rawScreenshotStorageAllowed,
            true,
            policy == ScreenshotVisionAppPolicy.Allowed
                ? "User approved this capture and the app is on the allow list."
                : "User approved this capture; this app will still ask each time.");
    }

    public ScreenshotVisionAppPolicy GetPolicy(ActiveWindowSnapshot window)
    {
        var key = BuildAppKey(window);
        return _perAppPolicies.TryGetValue(key, out var policy)
            ? policy
            : ScreenshotVisionAppPolicy.PromptEachTime;
    }

    public void Allow(ActiveWindowSnapshot window)
    {
        var key = BuildAppKey(window);
        _perAppPolicies[key] = ScreenshotVisionAppPolicy.Allowed;
        _ = PersistAsync(key, "Allowed");
    }

    public void Deny(ActiveWindowSnapshot window)
    {
        var key = BuildAppKey(window);
        _perAppPolicies[key] = ScreenshotVisionAppPolicy.Denied;
        _ = PersistAsync(key, "Denied");
    }

    public void ResetToPromptEachTime(ActiveWindowSnapshot window)
    {
        var key = BuildAppKey(window);
        _perAppPolicies.Remove(key);
        _ = RemovePersistedAsync(key);
    }

    private async Task PersistAsync(string appKey, string policy)
    {
        try { if (_persistPolicy is not null) await _persistPolicy(appKey, policy); }
        catch { /* persistence is best-effort; in-memory state is authoritative during the session */ }
    }

    private async Task RemovePersistedAsync(string appKey)
    {
        try { if (_removePolicy is not null) await _removePolicy(appKey); }
        catch { /* persistence is best-effort */ }
    }

    public string DescribePolicy(ActiveWindowSnapshot window)
    {
        var policy = GetPolicy(window);
        var display = BuildAppDisplayName(window);
        return policy switch
        {
            ScreenshotVisionAppPolicy.Allowed => $"{display}: allowed app, but each screenshot/OCR capture still requires visible one-time approval.",
            ScreenshotVisionAppPolicy.Denied => $"{display}: denied. Screenshot/OCR will not run even with one-time approval.",
            _ => $"{display}: prompt each time. Screenshot/OCR needs visible one-time approval."
        };
    }

    private static string BuildAppKey(ActiveWindowSnapshot window)
    {
        if (!string.IsNullOrWhiteSpace(window.ExecutablePath))
        {
            return window.ExecutablePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(window.ProcessName))
        {
            return window.ProcessName.Trim();
        }

        return "unknown-process";
    }

    private static string BuildAppDisplayName(ActiveWindowSnapshot window)
    {
        if (!string.IsNullOrWhiteSpace(window.ProcessName))
        {
            return window.ProcessName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(window.WindowTitle))
        {
            return window.WindowTitle.Trim();
        }

        return "Unknown app";
    }
}
