using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class CapturePolicyTests
{
    [Fact]
    public void Evaluate_Blocks_When_ProcessRuleMatches()
    {
        var policy = new CapturePolicy([
            CaptureRule.Create(CaptureRuleType.ProcessName, "1Password", CaptureRuleAction.Block, DateTimeOffset.UtcNow)
        ]);

        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.ActiveWindow,
            "window-focus",
            "Password vault",
            DateTimeOffset.UtcNow,
            processName: "1Password.exe");

        var decision = policy.Evaluate(contextEvent);

        Assert.False(decision.IsAllowed);
        Assert.Contains("Blocked", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_RequiresApproval_ForScreenshotsByDefault()
    {
        var policy = new CapturePolicy([]);
        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.Screenshot,
            "screenshot",
            "screenshot-reference",
            DateTimeOffset.UtcNow);

        var decision = policy.Evaluate(contextEvent);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresExplicitApproval);
    }

    [Fact]
    public void Evaluate_Blocks_SecretSensitivity()
    {
        var policy = new CapturePolicy([]);
        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.Manual,
            "note",
            "token: secret",
            DateTimeOffset.UtcNow,
            sensitivity: SensitivityLevel.ContainsSecret);

        var decision = policy.Evaluate(contextEvent);

        Assert.False(decision.IsAllowed);
    }
}
