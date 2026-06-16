using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ThreadlineSessionTests
{
    [Fact]
    public void Start_CreatesActiveSessionWithNameAndProvider()
    {
        var now = DateTimeOffset.UtcNow;

        var session = ThreadlineSession.Start("Debug build", now, "openai");

        Assert.StartsWith("ses_", session.Id);
        Assert.Equal("Debug build", session.Name);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal("openai", session.ActiveProvider);
    }

    [Fact]
    public void PauseResumeEnd_TransitionsStatus()
    {
        var session = ThreadlineSession.Start("Research", DateTimeOffset.UtcNow);
        var paused = session.Pause();
        var resumed = paused.Resume();
        var ended = resumed.End(DateTimeOffset.UtcNow);

        Assert.Equal(SessionStatus.Paused, paused.Status);
        Assert.Equal(SessionStatus.Active, resumed.Status);
        Assert.Equal(SessionStatus.Ended, ended.Status);
        Assert.NotNull(ended.EndedAt);
    }
}
