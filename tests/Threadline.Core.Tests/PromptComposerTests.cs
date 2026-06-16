using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class PromptComposerTests
{
    [Fact]
    public void Compose_IncludesQuestionWindowSummaryAndEvents()
    {
        var composer = new PromptComposer();
        var contextEvent = ContextEvent.Create(
            "ses_test",
            ContextSource.PowerShell,
            "command-output",
            "npm run build failed because vite was missing",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"));

        var messages = composer.Compose(new ThreadlinePromptContext(
            "Why did the build fail?",
            "PowerShell - pwsh.exe",
            "The user is debugging a Node build.",
            [contextEvent]));

        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Contains("Why did the build fail?", messages[1].Content);
        Assert.Contains("PowerShell", messages[1].Content);
        Assert.Contains("vite was missing", messages[1].Content);
    }
}
