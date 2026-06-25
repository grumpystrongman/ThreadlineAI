using Threadline.Core;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class PrivacyRuntimeStateTests
{
    [Fact]
    public void Rules_IsEmptyByDefault()
    {
        var state = new PrivacyRuntimeState();
        Assert.Empty(state.Rules);
    }

    [Fact]
    public void Replace_SetsRulesFilteringBlankPatterns()
    {
        var state = new PrivacyRuntimeState();
        var rules = new[]
        {
            CaptureRule.Create(CaptureRuleType.ProcessName, "chrome", CaptureRuleAction.Block, DateTimeOffset.UtcNow),
            CaptureRule.Create(CaptureRuleType.ProcessName, "   ", CaptureRuleAction.Block, DateTimeOffset.UtcNow),
            CaptureRule.Create(CaptureRuleType.DomainContains, "example.com", CaptureRuleAction.Block, DateTimeOffset.UtcNow),
        };

        state.Replace(rules);

        Assert.Equal(2, state.Rules.Count);
    }

    [Fact]
    public void Replace_DeduplicatesById()
    {
        var state = new PrivacyRuntimeState();
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "chrome", CaptureRuleAction.Block, DateTimeOffset.UtcNow);
        var duplicate = rule with { Pattern = "chrome-updated" };

        state.Replace([rule, duplicate]);

        Assert.Single(state.Rules);
    }

    [Fact]
    public void Replace_PrioritizesUserRulesFirst()
    {
        var state = new PrivacyRuntimeState();
        var now = DateTimeOffset.UtcNow;
        var systemRule = CaptureRule.Create(CaptureRuleType.ProcessName, "sys", CaptureRuleAction.Block, now, CaptureRuleSource.Organization);
        var userRule = CaptureRule.Create(CaptureRuleType.ProcessName, "usr", CaptureRuleAction.Block, now, CaptureRuleSource.User);

        state.Replace([systemRule, userRule]);

        Assert.Equal(2, state.Rules.Count);
        Assert.Equal(CaptureRuleSource.User, state.Rules[0].Source);
        Assert.Equal(CaptureRuleSource.Organization, state.Rules[1].Source);
    }

    [Fact]
    public void Upsert_AddsNewRule()
    {
        var state = new PrivacyRuntimeState();
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "notepad", CaptureRuleAction.Block, DateTimeOffset.UtcNow);

        state.Upsert(rule);

        Assert.Single(state.Rules);
        Assert.Equal("notepad", state.Rules[0].Pattern);
    }

    [Fact]
    public void Upsert_ReplacesExistingRuleById()
    {
        var state = new PrivacyRuntimeState();
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "notepad", CaptureRuleAction.Block, DateTimeOffset.UtcNow);
        state.Upsert(rule);

        var updated = rule with { Pattern = "notepad.exe" };
        state.Upsert(updated);

        Assert.Single(state.Rules);
        Assert.Equal("notepad.exe", state.Rules[0].Pattern);
    }

    [Fact]
    public void Remove_DeletesRuleById()
    {
        var state = new PrivacyRuntimeState();
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "notepad", CaptureRuleAction.Block, DateTimeOffset.UtcNow);
        state.Upsert(rule);

        state.Remove(rule.Id);

        Assert.Empty(state.Rules);
    }

    [Fact]
    public void Remove_IsCaseInsensitive()
    {
        var state = new PrivacyRuntimeState();
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "notepad", CaptureRuleAction.Block, DateTimeOffset.UtcNow);
        state.Upsert(rule);

        state.Remove(rule.Id.ToUpperInvariant());

        Assert.Empty(state.Rules);
    }

    [Fact]
    public void Remove_NoOpForUnknownId()
    {
        var state = new PrivacyRuntimeState();
        var rule = CaptureRule.Create(CaptureRuleType.ProcessName, "notepad", CaptureRuleAction.Block, DateTimeOffset.UtcNow);
        state.Upsert(rule);

        state.Remove("unknown_id");

        Assert.Single(state.Rules);
    }
}
