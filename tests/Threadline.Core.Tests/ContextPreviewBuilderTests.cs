using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ContextPreviewBuilderTests
{
    [Fact]
    public void Build_RedactsSecretsAndAllowsNormalContext()
    {
        var builder = new ContextPreviewBuilder(new CapturePolicy([]), new SecretRedactor());
        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.Manual,
            "note",
            "api_key=abc123456789",
            DateTimeOffset.UtcNow);

        var preview = builder.Build(contextEvent);

        Assert.True(preview.Decision.IsAllowed);
        Assert.True(preview.WillBeStored);
        Assert.Equal("api_key=[REDACTED]", preview.RedactedContent);
        Assert.Contains(preview.Warnings, warning => warning.Contains("redacted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_MarksScreenshotsAsApprovalRequired()
    {
        var builder = new ContextPreviewBuilder(new CapturePolicy([]), new SecretRedactor());
        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.Screenshot,
            "screenshot",
            "screen-reference",
            DateTimeOffset.UtcNow,
            userApproved: false);

        var preview = builder.Build(contextEvent);

        Assert.True(preview.Decision.IsAllowed);
        Assert.True(preview.RequiresExplicitApproval);
        Assert.False(preview.WillBeStored);
    }

    [Fact]
    public void Build_MarksBlockedContextAsNotStored()
    {
        var builder = new ContextPreviewBuilder(
            new CapturePolicy([CaptureRule.Create(CaptureRuleType.ProcessName, "blocked.exe", CaptureRuleAction.Block, DateTimeOffset.UtcNow)]),
            new SecretRedactor());
        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.ActiveWindow,
            "window",
            "blocked",
            DateTimeOffset.UtcNow,
            processName: "blocked.exe");

        var preview = builder.Build(contextEvent);

        Assert.False(preview.Decision.IsAllowed);
        Assert.False(preview.WillBeStored);
    }
}
