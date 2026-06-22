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

        api.MapPost("/providers/{providerName}/test", async (string providerName, ThreadlineProviderProbeService providerTests, CancellationToken ct) =>
        {
            var invalidProvider = RequestValidator.ValidateProviderName(providerName);
            if (invalidProvider is not null) return invalidProvider;

            return Results.Ok(await providerTests.TestAsync(providerName.Trim(), ct));
        });

        return app;
    }
}
