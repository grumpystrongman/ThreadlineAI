using Microsoft.AspNetCore.Http;
using Threadline.Core;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class RequestValidatorTests
{
    private static readonly ThreadlineServiceOptions DefaultOptions = new(
        RequireApiToken: false,
        ApiToken: null,
        ApiTokenPath: "/tmp/token",
        MaxContextCharacters: 200_000,
        MaxSessionNameCharacters: 120,
        RetentionDays: 30,
        LocalOnlyMode: false,
        CorsAllowedOrigins: new HashSet<string>());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSessionName_RejectsBlankNames(string? name)
    {
        var result = RequestValidator.ValidateSessionName(name, DefaultOptions);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSessionName_RejectsTooLongNames()
    {
        var longName = new string('x', 121);
        var result = RequestValidator.ValidateSessionName(longName, DefaultOptions);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSessionName_AcceptsValidName()
    {
        var result = RequestValidator.ValidateSessionName("Build debug", DefaultOptions);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_id")]
    public void ValidateSessionId_RejectsInvalidIds(string? sessionId)
    {
        var result = RequestValidator.ValidateSessionId(sessionId);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSessionId_AcceptsValidId()
    {
        var result = RequestValidator.ValidateSessionId("ses_abc123");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateContext_RejectsBlankContextType()
    {
        var request = new AppendContextEventRequest(ContextSource.Manual, "", "content");
        var result = RequestValidator.ValidateContext(request, DefaultOptions);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateContext_RejectsBlankContent()
    {
        var request = new AppendContextEventRequest(ContextSource.Manual, "note", "");
        var result = RequestValidator.ValidateContext(request, DefaultOptions);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateContext_RejectsExcessiveContent()
    {
        var request = new AppendContextEventRequest(ContextSource.Manual, "note", new string('x', 200_001));
        var result = RequestValidator.ValidateContext(request, DefaultOptions);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateContext_AcceptsValidRequest()
    {
        var request = new AppendContextEventRequest(ContextSource.Manual, "note", "some content");
        var result = RequestValidator.ValidateContext(request, DefaultOptions);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateWindow_RejectsBlankApplicationName()
    {
        var request = new AttachWindowRequest("", "proc", "title");
        var result = RequestValidator.ValidateWindow(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateWindow_RejectsBlankProcessName()
    {
        var request = new AttachWindowRequest("App", "", "title");
        var result = RequestValidator.ValidateWindow(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateWindow_RejectsBlankWindowTitle()
    {
        var request = new AttachWindowRequest("App", "proc", "");
        var result = RequestValidator.ValidateWindow(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateWindow_AcceptsValidRequest()
    {
        var request = new AttachWindowRequest("Chrome", "chrome.exe", "Google");
        var result = RequestValidator.ValidateWindow(request);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWindowAction_RejectsBlankDescription(string? description)
    {
        var request = new ProposeWindowActionRequest(WindowActionKind.ClickElement, description!, "payload");
        var result = RequestValidator.ValidateWindowAction(request);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWindowAction_RejectsBlankPayload(string? payload)
    {
        var request = new ProposeWindowActionRequest(WindowActionKind.ClickElement, "do something", payload!);
        var result = RequestValidator.ValidateWindowAction(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateWindowAction_AcceptsValidRequest()
    {
        var request = new ProposeWindowActionRequest(WindowActionKind.ClickElement, "click button", "button-id");
        var result = RequestValidator.ValidateWindowAction(request);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void ValidateActionId_RejectsInvalidIds(string? actionId)
    {
        var result = RequestValidator.ValidateActionId(actionId);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateActionId_AcceptsValidId()
    {
        var result = RequestValidator.ValidateActionId("act_abc123");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateQuestion_RejectsBlankQuestion(string? question)
    {
        var result = RequestValidator.ValidateQuestion(question);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateQuestion_AcceptsValidQuestion()
    {
        var result = RequestValidator.ValidateQuestion("What does this code do?");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSummary_RejectsBlank(string? summary)
    {
        var result = RequestValidator.ValidateSummary(summary);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSummary_AcceptsValidSummary()
    {
        var result = RequestValidator.ValidateSummary("A summary");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateProviderName_RejectsBlank(string? providerName)
    {
        var result = RequestValidator.ValidateProviderName(providerName);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateProviderName_AcceptsValidName()
    {
        var result = RequestValidator.ValidateProviderName("openai");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateProviderCredential_RejectsBlankSecretValue()
    {
        var request = new SaveProviderCredentialRequest("");
        var result = RequestValidator.ValidateProviderCredential(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateProviderCredential_RejectsTooShortSecretValue()
    {
        var request = new SaveProviderCredentialRequest("short");
        var result = RequestValidator.ValidateProviderCredential(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateProviderCredential_AcceptsValidCredential()
    {
        var request = new SaveProviderCredentialRequest("a-valid-secret-key-here");
        var result = RequestValidator.ValidateProviderCredential(request);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-reference")]
    public void ValidateSecretReference_RejectsInvalidReferences(string? reference)
    {
        var result = RequestValidator.ValidateSecretReference(reference);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSecretReference_AcceptsValidReference()
    {
        var result = RequestValidator.ValidateSecretReference("secret://memory/my-key");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateAdapter_RejectsUnknownKind()
    {
        var request = new RegisterAdapterRequest(AdapterKind.Unknown, "Name", AdapterPermission.ReadSessions);
        var result = RequestValidator.ValidateAdapter(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAdapter_RejectsBlankDisplayName()
    {
        var request = new RegisterAdapterRequest(AdapterKind.BrowserExtension, "", AdapterPermission.ReadSessions);
        var result = RequestValidator.ValidateAdapter(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAdapter_RejectsNoPermissions()
    {
        var request = new RegisterAdapterRequest(AdapterKind.BrowserExtension, "Name", AdapterPermission.None);
        var result = RequestValidator.ValidateAdapter(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAdapter_AcceptsValidRequest()
    {
        var request = new RegisterAdapterRequest(AdapterKind.BrowserExtension, "My Extension", AdapterPermission.ReadSessions);
        var result = RequestValidator.ValidateAdapter(request);
        Assert.Null(result);
    }
}
