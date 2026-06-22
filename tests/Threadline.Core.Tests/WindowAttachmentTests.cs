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
    public void WindowSnapshot_ToContextEvent_UsesNativeWindowContextWhenPresent()
    {
        var metadata = new Dictionary<string, string>
        {
            ["nativeContext.providerName"] = "Notepad / file-backed text",
            ["nativeContext.level"] = "FullDocument",
            ["nativeContext.levelDisplay"] = "Full document",
            ["nativeContext.guidance"] = "A file path was resolved from the Notepad window title.",
            ["nativeContext.content"] = "hello from the attached document"
        };
        var snapshot = WindowSnapshot.Create(DateTimeOffset.UtcNow, "notepad", "notepad", "notes.txt - Notepad", metadata: metadata);

        var contextEvent = snapshot.ToContextEvent("ses_test", userApproved: true);

        Assert.Contains("Native provider: Notepad / file-backed text", contextEvent.Content);
        Assert.Contains("Context level: Full document", contextEvent.Content);
        Assert.Contains("hello from the attached document", contextEvent.Content);
        Assert.Equal("FullDocument", contextEvent.Metadata!["window.nativeContext.level"]);
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
