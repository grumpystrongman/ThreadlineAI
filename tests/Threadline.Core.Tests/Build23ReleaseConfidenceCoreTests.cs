using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class Build23ReleaseConfidenceCoreTests
{
    [Fact]
    public void CapabilityRegistry_ReplacesCapabilityByIdCaseInsensitively()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new ProviderCapability("OpenAI", ThreadlineCapabilityStatus.NeedsSetup, "Not ready.").ToCapability());
        registry.Register(new ProviderCapability("openai", ThreadlineCapabilityStatus.Ready, "Ready.").ToCapability());

        var capability = registry.Get("PROVIDER.OPENAI");

        Assert.NotNull(capability);
        Assert.Equal(ThreadlineCapabilityStatus.Ready, capability.Status);
        Assert.Equal("Ready.", capability.Description);
    }

    [Fact]
    public void ActionCatalog_HasUniqueIdsAndEveryRequiredCapabilityIsKnown()
    {
        var catalog = new ThreadlineActionCatalog();
        var registry = new CapabilityRegistry();
        var knownCapabilityIds = registry.List().Select(capability => capability.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        knownCapabilityIds.Add("provider.configured"); // Doctor projects this dynamic capability from provider state.

        var actions = catalog.List();
        var duplicateIds = actions.GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray();

        Assert.Empty(duplicateIds);
        Assert.All(actions.Where(action => action.RequiredCapabilityId is not null), action =>
            Assert.Contains(action.RequiredCapabilityId!, knownCapabilityIds));
    }

    [Fact]
    public void ActionCatalog_DestructiveActionsRequireActiveWorkThread()
    {
        var destructiveActions = new ThreadlineActionCatalog().List().Where(action => action.IsDestructive).ToArray();

        Assert.NotEmpty(destructiveActions);
        Assert.All(destructiveActions, action => Assert.True(action.RequiresActiveWorkThread, action.Id));
    }

    [Theory]
    [InlineData("chrome.exe", "https://example.com/path", "page", ContextSource.Browser)]
    [InlineData("pwsh.exe", null, "command-output", ContextSource.PowerShell)]
    [InlineData("WindowsTerminal.exe", null, "shell", ContextSource.Terminal)]
    [InlineData("Threadline.exe", null, "screenshot", ContextSource.Screenshot)]
    [InlineData("Threadline.exe", null, "selected-text", ContextSource.UserSelection)]
    public void ContextSourceClassifier_ClassifiesCommonSources(string processName, string? uri, string contextType, ContextSource expected)
    {
        var classifier = new ContextSourceClassifier();

        var classification = classifier.Classify(
            applicationName: processName,
            processName: processName,
            windowTitle: "Build 23 test window",
            uri: uri,
            contextType: contextType);

        Assert.Equal(expected, classification.Source);
        Assert.False(string.IsNullOrWhiteSpace(classification.Reason));
    }

    [Fact]
    public async Task JsonSidecarGeometryStore_SavesAndRestoresUsableGeometry()
    {
        var path = Path.Combine(Path.GetTempPath(), "threadline-build23", Guid.NewGuid().ToString("N"), "geometry.json");
        var store = new JsonSidecarGeometryStore(path);
        var geometry = SidecarGeometryState.Create(100, 200, 900, 700, isAttached: true, DateTimeOffset.Parse("2026-06-22T12:00:00Z"));

        await store.SaveAsync(geometry);
        var restored = await store.RestoreAsync();

        Assert.Equal(geometry, restored);
    }

    [Fact]
    public void SidecarGeometryState_ClampsTooSmallBoundsBeforeSave()
    {
        var geometry = SidecarGeometryState.Create(10, 20, 1, 2, isAttached: false, DateTimeOffset.UtcNow);

        Assert.True(geometry.IsUsable);
        Assert.Equal(SidecarGeometryState.MinimumWidth, geometry.Width);
        Assert.Equal(SidecarGeometryState.MinimumHeight, geometry.Height);
    }

    [Fact]
    public void UiAutomationFakeWindow_ProducesClassifiableWindowSnapshotAndContextContent()
    {
        var fake = new UiAutomationFakeWindow(
            "Notepad",
            "notepad.exe",
            "release-notes.txt - Notepad",
            "Build 23 release notes visible in fake UI Automation text.",
            DateTimeOffset.Parse("2026-06-22T12:00:00Z"));

        var snapshot = fake.ToWindowSnapshot();
        var contextEvent = snapshot.ToContextEvent("ses_build23", userApproved: true);
        var classification = new ContextSourceClassifier().Classify(snapshot);

        Assert.Equal(ContextSource.UiAutomation, classification.Source);
        Assert.Contains("Build 23 release notes", contextEvent.Content);
        Assert.Equal(ContextSource.ActiveWindow, contextEvent.Source);
        Assert.Equal("Fake UI Automation", snapshot.Metadata!["nativeContext.providerName"]);
    }
}
