namespace Threadline.Service;

public sealed record JarvisDispatchRequest(string Prompt, string? Language = "en", bool Confirmed = false);
public sealed record JarvisDenyRequest(string? Reason = null);

public static class JarvisEndpointMappings
{
    public static IEndpointRouteBuilder MapThreadlineJarvisApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/jarvis")
            .RequireThreadlineLocalAccess();

        group.MapGet("/status", async (PersonalJarvisClient client, CancellationToken cancellationToken) =>
        {
            var probe = await client.ProbeAsync(cancellationToken);
            return Results.Json(new
            {
                enabled = client.Options.Enabled,
                baseAddress = client.Options.BaseAddress.ToString(),
                upstreamRepository = JarvisRuntimeOptions.UpstreamRepository,
                pinnedUpstreamCommit = JarvisRuntimeOptions.PinnedUpstreamCommit,
                upstreamStatusCode = (int)probe.StatusCode,
                upstream = probe.Payload
            }, statusCode: probe.IsSuccessStatusCode ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        group.MapPost("/missions", async (
            JarvisDispatchRequest request,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { error = "Mission prompt is required." });
            }

            if (request.Prompt.Length > 100_000)
            {
                return Results.BadRequest(new { error = "Mission prompt must be 100000 characters or fewer." });
            }

            var language = string.Equals(request.Language, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            var response = await client.DispatchMissionAsync(
                request.Prompt.Trim(),
                language,
                request.Confirmed,
                cancellationToken);
            return Proxy(response);
        });

        group.MapGet("/missions/{missionId}", async (
            string missionId,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.GetMissionAsync(missionId, cancellationToken)));

        group.MapGet("/missions/{missionId}/result", async (
            string missionId,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.GetMissionResultAsync(missionId, cancellationToken)));

        group.MapGet("/missions/{missionId}/changes", async (
            string missionId,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.GetMissionChangesAsync(missionId, cancellationToken)));

        group.MapPost("/missions/{missionId}/cancel", async (
            string missionId,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.CancelMissionAsync(missionId, cancellationToken)));

        group.MapGet("/missions/{missionId}/tool-approvals", async (
            string missionId,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.GetToolApprovalsAsync(missionId, cancellationToken)));

        group.MapPost("/missions/{missionId}/tool-approvals/{traceId}/approve", async (
            string missionId,
            string traceId,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.ApproveToolCallAsync(missionId, traceId, cancellationToken)));

        group.MapPost("/missions/{missionId}/tool-approvals/{traceId}/deny", async (
            string missionId,
            string traceId,
            JarvisDenyRequest request,
            PersonalJarvisClient client,
            CancellationToken cancellationToken) =>
            Proxy(await client.DenyToolCallAsync(missionId, traceId, request.Reason, cancellationToken)));

        return endpoints;
    }

    private static IResult Proxy(JarvisApiResponse response) =>
        Results.Json(response.Payload, statusCode: (int)response.StatusCode);
}
