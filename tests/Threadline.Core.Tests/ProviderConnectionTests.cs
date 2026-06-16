using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ProviderConnectionTests
{
    [Fact]
    public void Create_DoesNotStoreRawCredentialByDesign()
    {
        var now = DateTimeOffset.UtcNow;

        var connection = ProviderConnection.Create(
            "OpenAI",
            ProviderAuthType.ApiKey,
            now,
            credentialReference: "credref://windows-credential-manager/openai",
            defaultModel: "gpt-4.1",
            status: ProviderConnectionStatus.Ready);

        Assert.StartsWith("prv_", connection.Id);
        Assert.Equal("OpenAI", connection.ProviderName);
        Assert.Equal("credref://windows-credential-manager/openai", connection.CredentialReference);
        Assert.Equal(ProviderConnectionStatus.Ready, connection.Status);
    }

    [Fact]
    public void MarkReadyAndDisable_UpdateStatusAndTimestamp()
    {
        var connection = ProviderConnection.Create("Local", ProviderAuthType.LocalEndpoint, DateTimeOffset.UtcNow);
        var readyAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var disabledAt = readyAt.AddMinutes(1);

        var ready = connection.MarkReady(readyAt);
        var disabled = ready.Disable(disabledAt);

        Assert.Equal(ProviderConnectionStatus.Ready, ready.Status);
        Assert.Equal(readyAt, ready.UpdatedAt);
        Assert.Equal(ProviderConnectionStatus.Disabled, disabled.Status);
        Assert.Equal(disabledAt, disabled.UpdatedAt);
    }
}
