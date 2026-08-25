namespace Threadline.Service;

public static class AgentEndpointMappings
{
    public static IEndpointRouteBuilder MapThreadlineAgentApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty)
            .RequireThreadlineLocalAccess();

        group.MapPost("/sessions/{sessionId}/agent", async (
            string sessionId,
            AgentAskRequest request,
            ThreadlineAgentRouterService router,
            CancellationToken cancellationToken) =>
        {
            var invalidSession = RequestValidator.ValidateSessionId(sessionId);
            if (invalidSession is not null) return invalidSession;

            var invalidQuestion = RequestValidator.ValidateQuestion(request.Question);
            if (invalidQuestion is not null) return invalidQuestion;

            try
            {
                var result = await router.ExecuteAsync(sessionId, request, cancellationToken);
                return Results.Json(result.Response, statusCode: result.StatusCode);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(
                    $"Agent communication failed: {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    $"An unexpected agent-routing error occurred ({ex.GetType().Name}): {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return endpoints;
    }
}
