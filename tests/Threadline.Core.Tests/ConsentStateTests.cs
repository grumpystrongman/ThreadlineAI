using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ConsentStateTests
{
    [Fact]
    public void ScreenshotPreview_RequiresApprovalUntilApproved()
    {
        var builder = new ContextPreviewBuilder(new CapturePolicy([]), new SecretRedactor());
        var pending = ContextEvent.Create("ses_test", ContextSource.Screenshot, "screenshot", "screen", DateTimeOffset.UtcNow, userApproved: false);
        var approved = pending with { UserApproved = true };

        var pendingPreview = builder.Build(pending);
        var approvedPreview = builder.Build(approved);

        Assert.Equal(ConsentState.Required, pendingPreview.ConsentState);
        Assert.False(pendingPreview.WillBeStored);
        Assert.Equal(ConsentState.Approved, approvedPreview.ConsentState);
        Assert.True(approvedPreview.WillBeStored);
    }

    [Fact]
    public void Preview_ReportsRuleSourceMetadata()
    {
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "sample-app", CaptureRuleAction.Block, DateTimeOffset.UtcNow, CaptureRuleSource.Organization);
        var builder = new ContextPreviewBuilder(new CapturePolicy([rule]), new SecretRedactor());
        var contextEvent = ContextEvent.Create("ses_test", ContextSource.ActiveWindow, "window", "sample", DateTimeOffset.UtcNow, processName: "sample-app");

        var preview = builder.Build(contextEvent);

        Assert.Equal(ConsentState.Blocked, preview.ConsentState);
        Assert.Equal("Organization", preview.PrivacyMetadata!["matchedRuleSource"]);
        Assert.Equal("Block", preview.PrivacyMetadata!["matchedRuleAction"]);
    }
}
