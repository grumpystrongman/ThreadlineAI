using Microsoft.AspNetCore.Http;
using Threadline.Core;

namespace Threadline.Service;

public static class RequestValidator
{
    public static IResult? ValidateSessionName(string? name, ThreadlineServiceOptions options)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest(new { error = "Session name is required." });
        }

        if (name.Trim().Length > options.MaxSessionNameCharacters)
        {
            return Results.BadRequest(new { error = $"Session name must be {options.MaxSessionNameCharacters} characters or fewer." });
        }

        return null;
    }

    public static IResult? ValidateSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !sessionId.StartsWith("ses_", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "A valid Threadline session id is required." });
        }

        return null;
    }

    public static IResult? ValidateContext(AppendContextEventRequest request, ThreadlineServiceOptions options)
    {
        if (string.IsNullOrWhiteSpace(request.ContextType))
        {
            return Results.BadRequest(new { error = "Context type is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Context content is required." });
        }

        if (request.Content.Length > options.MaxContextCharacters)
        {
            return Results.BadRequest(new { error = $"Context content exceeds the {options.MaxContextCharacters} character limit." });
        }

        return null;
    }

    public static IResult? ValidateQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Results.BadRequest(new { error = "Question is required." });
        }

        return null;
    }

    public static IResult? ValidateSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return Results.BadRequest(new { error = "Summary is required." });
        }

        return null;
    }

    public static IResult? ValidateProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Results.BadRequest(new { error = "Provider name is required." });
        }

        return null;
    }

    public static IResult? ValidateAdapter(RegisterAdapterRequest request)
    {
        if (request.Kind == AdapterKind.Unknown)
        {
            return Results.BadRequest(new { error = "Adapter kind is required." });
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.BadRequest(new { error = "Adapter display name is required." });
        }

        if (request.Permissions == AdapterPermission.None)
        {
            return Results.BadRequest(new { error = "Adapter permissions are required." });
        }

        return null;
    }
}
