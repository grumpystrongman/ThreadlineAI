using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class WindowAttachmentTests
{
    [Fact]
    public void WindowSnapshot_ToContextEvent_CarriesWindowMetadata()
    {
        var snapshot = WindowSnapshot.Create(DateTimeOffset.UtcNow, "PowerShell", "pwsh", "Threadline build", 1234, uri: "file://repo");

        var contextEvent = snapshot.ToContextEvent("ses_test", userApproved: true);

        Assert.Equal(ContextSource.ActiveWindow, contextEvent.Source);
        Assert.Equal("window-snapshot", contextEvent.ContextType);
        Assert.Equal("PowerShell", contextEvent.ApplicationName);
        Assert.Equal("pwsh", contextEvent.ProcessName);
        Assert.Equal("Threadline build", contextEvent.WindowTitle);
        Assert.Contains("Application: PowerShell", contextEvent.Content);
        Assert.Equal("1234", contextEvent.Metadata!["processId"]);
    }

    [Fact]
    public void WindowActionRequest_TransitionsFromProposedToApprovedToCompleted()
    {
        var action = WindowActionRequest.Propose("ses_test", WindowActionKind.InsertText, "Insert generated text", "hello", DateTimeOffset.UtcNow);

        var approved = action.Approve(DateTimeOffset.UtcNow);
        var completed = approved.Complete(DateTimeOffset.UtcNow, "Inserted text.");

        Assert.Equal(WindowActionStatus.Proposed, action.Status);
        Assert.Equal(WindowActionStatus.Approved, approved.Status);
        Assert.Equal(WindowActionStatus.Completed, completed.Status);
        Assert.Equal("Inserted text.", completed.ResultMessage);
    }
}
