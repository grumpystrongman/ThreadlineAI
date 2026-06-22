using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ReliabilityCoreReadinessTests
{
    [Fact]
    public void DoctorReportIsReadyOnlyForReadyState()
    {
        var report = new ThreadlineDoctorReport(
            ThreadlineReadinessState.Ready,
            DateTimeOffset.UtcNow,
            [ThreadlineDoctorCheck.Pass("service.running", "Service running", "Running.")],
            [],
            []);

        var degraded = report with { Readiness = ThreadlineReadinessState.Degraded };

        Assert.True(report.IsReady);
        Assert.False(degraded.IsReady);
    }
}
