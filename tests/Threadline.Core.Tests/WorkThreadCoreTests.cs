using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class WorkThreadCoreTests
{
    [Fact]
    public void Create_AssignsIdTitleAndOpenStatus()
    {
        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("Build feature", now);

        Assert.StartsWith("thr_", thread.Id);
        Assert.Equal("Build feature", thread.Title);
        Assert.Null(thread.Description);
        Assert.Equal(WorkThreadStatus.Open, thread.Status);
        Assert.Equal(now, thread.CreatedAt);
        Assert.Equal(now, thread.UpdatedAt);
        Assert.Equal(now, thread.LastResumedAt);
        Assert.Null(thread.ClosedAt);
    }

    [Fact]
    public void Create_TrimsTitle()
    {
        var thread = WorkThread.Create("  padded title  ", DateTimeOffset.UtcNow);
        Assert.Equal("padded title", thread.Title);
    }

    [Fact]
    public void Create_FallsBackWhenTitleIsBlank()
    {
        var thread = WorkThread.Create("   ", DateTimeOffset.UtcNow);
        Assert.StartsWith("Work Thread ", thread.Title);
    }

    [Fact]
    public void Create_NormalizesDescriptionWhitespace()
    {
        var thread = WorkThread.Create("T", DateTimeOffset.UtcNow, description: "  desc  ");
        Assert.Equal("desc", thread.Description);
    }

    [Fact]
    public void Create_NullDescriptionWhenBlank()
    {
        var thread = WorkThread.Create("T", DateTimeOffset.UtcNow, description: "   ");
        Assert.Null(thread.Description);
    }

    [Fact]
    public void Rename_UpdatesTitleAndDescription()
    {
        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("Original", now);
        var later = now.AddMinutes(5);

        var renamed = thread.Rename("Renamed", later, description: "New desc");

        Assert.Equal("Renamed", renamed.Title);
        Assert.Equal("New desc", renamed.Description);
        Assert.Equal(later, renamed.UpdatedAt);
        Assert.Equal(thread.Id, renamed.Id);
    }

    [Fact]
    public void Close_SetsClosedStatusAndTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("Task", now);
        var closeTime = now.AddHours(1);

        var closed = thread.Close(closeTime);

        Assert.Equal(WorkThreadStatus.Closed, closed.Status);
        Assert.Equal(closeTime, closed.ClosedAt);
        Assert.Equal(closeTime, closed.UpdatedAt);
    }

    [Fact]
    public void Resume_ReopensClosedThread()
    {
        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("Task", now).Close(now.AddHours(1));
        var resumeTime = now.AddHours(2);

        var resumed = thread.Resume(resumeTime);

        Assert.Equal(WorkThreadStatus.Open, resumed.Status);
        Assert.Equal(resumeTime, resumed.LastResumedAt);
        Assert.Null(resumed.ClosedAt);
        Assert.Equal(resumeTime, resumed.UpdatedAt);
    }

    [Fact]
    public void WorkContextEvent_Create_NormalizesFields()
    {
        var now = DateTimeOffset.UtcNow;
        var evt = WorkContextEvent.Create(
            "thr_1",
            "  browser  ",
            "  page-view  ",
            WorkCaptureMode.Followed,
            now,
            appName: "  Chrome  ",
            windowTitle: "  Google  ",
            url: "  https://example.com  ",
            contentSummary: "  summary  ");

        Assert.StartsWith("wce_", evt.Id);
        Assert.Equal("thr_1", evt.WorkThreadId);
        Assert.Equal("browser", evt.SourceType);
        Assert.Equal("page-view", evt.SourceName);
        Assert.Equal("Chrome", evt.AppName);
        Assert.Equal("Google", evt.WindowTitle);
        Assert.Equal("https://example.com", evt.Url);
        Assert.Equal("summary", evt.ContentSummary);
        Assert.Equal(WorkCaptureMode.Followed, evt.CaptureMode);
    }

    [Fact]
    public void WorkContextEvent_Create_UseFallbacksForBlankValues()
    {
        var evt = WorkContextEvent.Create("thr_1", "", "  ", WorkCaptureMode.Manual, DateTimeOffset.UtcNow);

        Assert.Equal("Unknown", evt.SourceType);
        Assert.Equal("Unknown context", evt.SourceName);
        Assert.Null(evt.AppName);
        Assert.Null(evt.WindowTitle);
        Assert.Null(evt.Url);
        Assert.Null(evt.ContentSummary);
    }

    [Fact]
    public void ConversationMessage_Create_NormalizesRole()
    {
        var msg = ConversationMessage.Create("thr_1", "  USER  ", "Hello", DateTimeOffset.UtcNow);

        Assert.StartsWith("msg_", msg.Id);
        Assert.Equal("user", msg.Role);
        Assert.Equal("Hello", msg.Content);
    }

    [Fact]
    public void ConversationMessage_Create_FallsBackToNoteWhenRoleIsBlank()
    {
        var msg = ConversationMessage.Create("thr_1", "  ", "content", DateTimeOffset.UtcNow);
        Assert.Equal("note", msg.Role);
    }

    [Fact]
    public void ContextReceiptRecord_Create_TrimsAndNormalizesFields()
    {
        var now = DateTimeOffset.UtcNow;
        var receipt = ContextReceiptRecord.Create("thr_1", "  [\"src1\"]  ", now, notUsedSourcesJson: "  [\"src2\"]  ", limitations: "  limit  ");

        Assert.StartsWith("crp_", receipt.Id);
        Assert.Equal("[\"src1\"]", receipt.UsedSourcesJson);
        Assert.Equal("[\"src2\"]", receipt.NotUsedSourcesJson);
        Assert.Equal("limit", receipt.Limitations);
    }

    [Fact]
    public void ContextReceiptRecord_Create_NullsBlankOptionalFields()
    {
        var receipt = ContextReceiptRecord.Create("thr_1", "[]", DateTimeOffset.UtcNow, notUsedSourcesJson: "  ", limitations: "  ");

        Assert.Null(receipt.NotUsedSourcesJson);
        Assert.Null(receipt.Limitations);
    }

    [Fact]
    public void WorkArtifact_Create_TrimsAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var artifact = WorkArtifact.Create("thr_1", "  code  ", "  Helper  ", "  content  ", now, "crp_1");

        Assert.StartsWith("art_", artifact.Id);
        Assert.Equal("code", artifact.ArtifactType);
        Assert.Equal("Helper", artifact.Title);
        Assert.Equal("content", artifact.Content);
        Assert.Equal(now, artifact.CreatedAt);
        Assert.Equal(now, artifact.UpdatedAt);
        Assert.Equal("crp_1", artifact.ContextReceiptId);
    }
}
