using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("api_key=abc123456789", "api_key=[REDACTED]")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456", "Authorization: [REDACTED]")]
    [InlineData("User SSN is 123-45-6789", "User SSN is [REDACTED]")]
    public void Redact_RemovesCommonSecrets(string input, string expected)
    {
        var redactor = new SecretRedactor();

        var result = redactor.Redact(input);

        Assert.Equal(expected, result);
    }
}
