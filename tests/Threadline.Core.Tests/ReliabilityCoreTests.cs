using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ReliabilityCoreTests
{
    [Fact]
    public void ActionCatalogRegistersBuild15Actions()
    {
        var catalog = new ThreadlineActionCatalog();
        var actions = catalog.List();

        Assert.Contains(actions, action => action.Id == "artifact.summary" && action.Kind == ThreadlineActionKind.Summary);
        Assert.Contains(actions, action => action.Id == "artifact.handoff" && action.Kind == ThreadlineActionKind.Handoff);
        Assert.Contains(actions, action => action.Id == "artifact.decisions" && action.Kind == ThreadlineActionKind.Decisions);
        Assert.Contains(actions, action => action.Id == "artifact.risks" && action.Kind == ThreadlineActionKind.Risks);
        Assert.Contains(actions, action => action.Id == "artifact.next-actions" && action.Kind == ThreadlineActionKind.NextActions);
        Assert.Contains(actions, action => action.Id == "provider.test" && action.Kind == ThreadlineActionKind.ProviderTest);
        Assert.Contains(actions, action => action.Id == "work.resume" && action.Kind == ThreadlineActionKind.ResumeWork);
        Assert.Contains(actions, action => action.Id == "context.clear" && action.Kind == ThreadlineActionKind.ClearContext);
    }

    [Fact]
    public void CapabilityRegistrySupportsTypedCapabilities()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new ProviderCapability("OpenAI", ThreadlineCapabilityStatus.Ready, "Configured and ready.").ToCapability());
        registry.Register(new BrowserExtensionCapability(ThreadlineCapabilityStatus.NeedsSetup, "Extension needs setup.").ToCapability());

        Assert.Equal(ThreadlineCapabilityStatus.Ready, registry.Get("provider.openai")?.Status);
        Assert.Equal(ThreadlineCapabilityStatus.NeedsSetup, registry.Get("browser-extension.bridge")?.Status);
        Assert.Contains(registry.List(), capability => capability.Category == "MemoryCapability");
        Assert.Contains(registry.List(), capability => capability.Category == "ArtifactCapability");
    }

    [Fact]
    public void DoctorReportMarksReadyOnlyWhenReadinessIsReady()
    {
        var report = new ThreadlineDoctorReport(
            ThreadlineReadinessState.Ready,
            DateTimeOffset.UtcNow,
            [ThreadlineDoctorCheck.Pass("service.running", "Service running", "Running.")],
            [],
            []);

        Assert.True(report.IsReady);
        Assert.False(report with { Readiness = ThreadlineReadinessState.Degraded } is { IsReady: true });
    }
}
