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

    [Fact]
    public void Analyze_ReturnsFindingsByKind()
    {
        var redactor = new SecretRedactor();
        var input = "Contact jeff@example.com with MRN: A123456 and token=supersecret123";

        var result = redactor.Analyze(input);

        Assert.True(result.WasRedacted);
        Assert.Contains(result.Findings, finding => finding.Kind == RedactionKind.EmailAddress);
        Assert.Contains(result.Findings, finding => finding.Kind == RedactionKind.PhiMarker);
        Assert.Contains(result.Findings, finding => finding.Kind == RedactionKind.GenericSecret);
        Assert.DoesNotContain("jeff@example.com", result.RedactedText);
        Assert.DoesNotContain("A123456", result.RedactedText);
        Assert.DoesNotContain("supersecret123", result.RedactedText);
    }

    [Theory]
    [InlineData("Server=myserver;User Id=threadline;Password=secret;")]
    [InlineData("https://example.com/callback?access_token=secret-token-value&next=/home")]
    [InlineData("Call me at 919-555-1212")]
    public void Redact_RemovesAdditionalPrivacyRisks(string input)
    {
        var redactor = new SecretRedactor();

        var result = redactor.Analyze(input);

        Assert.True(result.WasRedacted);
        Assert.Contains("[REDACTED]", result.RedactedText);
    }
}
