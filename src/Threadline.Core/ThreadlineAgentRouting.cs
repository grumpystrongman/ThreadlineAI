namespace Threadline.Core;

public enum ThreadlineAgentRoute
{
    Direct,
    Mission
}

public enum ThreadlineAgentRoutePreference
{
    Auto,
    Direct,
    Mission
}

public sealed record AgentIntentDecision(
    ThreadlineAgentRoute Route,
    string Reason,
    string Confidence);

/// <summary>
/// Small, deterministic routing layer that keeps ordinary conversation fast while
/// escalating operational, discovery, current-information, and multi-step work to Jarvis.
/// This remains inspectable and available even when a provider is degraded.
/// </summary>
public sealed class AgentIntentClassifier
{
    private static readonly string[] MissionPhrases =
    [
        "run the tests", "run tests", "fix this", "fix the", "debug this", "debug the",
        "implement this", "implement the", "build this", "build the", "refactor this",
        "refactor the", "edit this", "edit the", "modify this", "modify the",
        "change this", "change the", "create a file", "create the file", "write a file",
        "install this", "install the", "configure this", "configure the", "set up the",
        "setup the", "download this", "download the", "open the", "launch the",
        "click the", "send the", "email the", "commit this", "commit the", "push the",
        "merge the", "delete the", "remove the", "move the", "rename the", "organize the",
        "clean up", "research this", "research the", "investigate this", "investigate the",
        "look this up", "look up the", "make the changes", "apply the changes",
        "update the repo", "update this repo", "work on the repo", "work on this repo"
    ];

    private static readonly string[] DiscoveryPhrases =
    [
        "find me", "find a", "find the", "find some", "search for", "look for",
        "give me a list", "make me a list", "build me a list", "compile a list",
        "who are the", "which agents", "which companies", "which vendors",
        "contact information", "contact details", "submission guidelines",
        "submission requirements", "currently accepting", "open to submissions"
    ];

    private static readonly string[] FreshInformationMarkers =
    [
        "current", "currently", "latest", "recent", "today", "this week", "this month",
        "up-to-date", "up to date", "online", "website", "availability", "price", "prices",
        "schedule", "hours", "contact info", "submission guidelines"
    ];

    private static readonly string[] StrongActionVerbs =
    [
        "build", "implement", "fix", "debug", "refactor", "edit", "modify", "install",
        "configure", "download", "launch", "click", "send", "email", "commit", "push",
        "merge", "delete", "remove", "move", "rename", "organize", "research", "investigate",
        "find", "search"
    ];

    private static readonly string[] MultiStepMarkers =
    [
        " and then ", " then ", " after that ", " once that is done ", " finally ", "; then "
    ];

    public AgentIntentDecision Decide(
        string? request,
        ThreadlineAgentRoutePreference preference = ThreadlineAgentRoutePreference.Auto)
    {
        var text = (request ?? string.Empty).Trim();

        if (preference == ThreadlineAgentRoutePreference.Direct)
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Direct, "Direct route explicitly requested.", "High");
        }

        if (preference == ThreadlineAgentRoutePreference.Mission)
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Mission, "Mission route explicitly requested.", "High");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Direct, "Empty requests stay on the direct path for validation.", "High");
        }

        var normalized = $" {text.ToLowerInvariant()} ";

        if (normalized.TrimStart().StartsWith("mission:", StringComparison.Ordinal)
            || normalized.TrimStart().StartsWith("agent:", StringComparison.Ordinal)
            || normalized.Contains(" do this for me ", StringComparison.Ordinal)
            || normalized.Contains(" take care of this ", StringComparison.Ordinal))
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Mission, "The request explicitly asks for agentic execution.", "High");
        }

        var matchedPhrase = MissionPhrases.FirstOrDefault(phrase => ContainsPhrase(normalized, phrase));
        if (matchedPhrase is not null)
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Mission, $"Operational phrase detected: '{matchedPhrase}'.", "High");
        }

        var discoveryPhrase = DiscoveryPhrases.FirstOrDefault(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
        if (discoveryPhrase is not null)
        {
            return new AgentIntentDecision(
                ThreadlineAgentRoute.Mission,
                $"Discovery/research intent detected: '{discoveryPhrase}'.",
                "High");
        }

        var freshMarker = FreshInformationMarkers.FirstOrDefault(marker => normalized.Contains($" {marker} ", StringComparison.Ordinal)
            || normalized.Contains($" {marker}?", StringComparison.Ordinal)
            || normalized.Contains($" {marker}.", StringComparison.Ordinal)
            || normalized.Contains($" {marker},", StringComparison.Ordinal));
        if (freshMarker is not null)
        {
            return new AgentIntentDecision(
                ThreadlineAgentRoute.Mission,
                $"Current/external information signal detected: '{freshMarker}'.",
                "Medium");
        }

        var actionVerbCount = StrongActionVerbs.Count(verb => ContainsWord(normalized, verb));
        var hasMultiStepMarker = MultiStepMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
        if (hasMultiStepMarker && actionVerbCount > 0)
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Mission, "The request combines an action with a multi-step sequence.", "High");
        }

        if (actionVerbCount >= 2)
        {
            return new AgentIntentDecision(ThreadlineAgentRoute.Mission, "The request contains multiple operational actions.", "Medium");
        }

        return new AgentIntentDecision(ThreadlineAgentRoute.Direct, "The request looks conversational or analytical and does not require autonomous execution or fresh external information.", "Medium");
    }

    private static bool ContainsPhrase(string normalizedText, string phrase) =>
        normalizedText.Contains($" {phrase} ", StringComparison.Ordinal)
        || normalizedText.Contains($" {phrase}.", StringComparison.Ordinal)
        || normalizedText.Contains($" {phrase},", StringComparison.Ordinal)
        || normalizedText.Contains($" {phrase}?", StringComparison.Ordinal);

    private static bool ContainsWord(string normalizedText, string word) =>
        normalizedText.Contains($" {word} ", StringComparison.Ordinal)
        || normalizedText.Contains($" {word}.", StringComparison.Ordinal)
        || normalizedText.Contains($" {word},", StringComparison.Ordinal)
        || normalizedText.Contains($" {word}?", StringComparison.Ordinal)
        || normalizedText.Contains($" {word}!", StringComparison.Ordinal);
}
