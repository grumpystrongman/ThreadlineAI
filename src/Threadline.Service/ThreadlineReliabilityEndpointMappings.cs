using Threadline.Core;

namespace Threadline.Service;

public static class ThreadlineReliabilityEndpointMappings
{
    public static WebApplication MapThreadlineReliabilityApi(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty).RequireThreadlineLocalAccess();

        api.MapGet("/doctor", async (ThreadlineDoctorService doctor, CancellationToken ct) =>
            Results.Ok(await doctor.BuildReportAsync(ct)));

        api.MapGet("/capabilities", async (ThreadlineDoctorService doctor, CancellationToken ct) =>
        {
            var report = await doctor.BuildReportAsync(ct);
            return Results.Ok(report.Capabilities);
        });

        api.MapGet("/actions", (ThreadlineActionCatalog actions) =>
            Results.Ok(actions.List()));

        api.MapGet("/actions/catalog", (ThreadlineActionCatalog actions) =>
            Results.Ok(actions.List()));

        api.MapPost("/actions/{actionId}/run", async (string actionId, ThreadlineActionRunRequest request, ThreadlineActionExecutionService actionRunner, CancellationToken ct) =>
        {
            var result = await actionRunner.ExecuteAsync(actionId, request, ct);
            return result.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                ? Results.Json(result, statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(result);
        });

        api.MapPost("/providers/{providerName}/test", async (string providerName, ThreadlineProviderProbeService providerTests, CancellationToken ct) =>
        {
            var invalidProvider = RequestValidator.ValidateProviderName(providerName);
            if (invalidProvider is not null) return invalidProvider;

            return Results.Ok(await providerTests.TestAsync(providerName.Trim(), ct));
        });

        return app;
    }
}
