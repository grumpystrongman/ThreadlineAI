using System.Diagnostics;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;

namespace Threadline.Service;

public sealed class ThreadlineProviderProbeService
{
    private readonly ProviderConnectionService _providers;
    private readonly SecretService _secrets;
    private readonly IHttpClientFactory _httpClientFactory;

    public ThreadlineProviderProbeService(
        ProviderConnectionService providers,
        SecretService secrets,
        IHttpClientFactory httpClientFactory)
    {
        _providers = providers;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ProviderTestResult> TestAsync(string? providerName = null, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(providerName, cancellationToken);
        if (provider is null)
        {
            return new ProviderTestResult(
                providerName ?? "None",
                false,
                ThreadlineDoctorCheckStatus.Fail,
                "No configured provider was found. Save provider settings before running a provider test.");
        }

        var validation = ValidateProvider(provider);
        if (validation is not null)
        {
            return new ProviderTestResult(provider.ProviderName, false, ThreadlineDoctorCheckStatus.Fail, validation, Model: provider.DefaultModel);
        }

        string apiKey;
        try
        {
            apiKey = await ResolveApiKeyAsync(provider, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ProviderTestResult(provider.ProviderName, false, ThreadlineDoctorCheckStatus.Fail, ex.Message, Model: provider.DefaultModel);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var llmProvider = new OpenAiCompatibleProvider(
                _httpClientFactory.CreateClient(nameof(ThreadlineProviderProbeService)),
                new OpenAiCompatibleProviderOptions(provider.ProviderName, provider.BaseUrl!, apiKey, provider.DefaultModel!));

            var response = await llmProvider.CompleteAsync(
                new LlmRequest(
                    provider.DefaultModel!,
                    [LlmMessage.User("Threadline provider health check. Reply with OK.")],
                    Temperature: 0,
                    MaxOutputTokens: 16),
                cancellationToken);

            stopwatch.Stop();
            var detail = string.IsNullOrWhiteSpace(response.Content)
                ? "Provider responded but returned an empty health-check response."
                : "Provider test succeeded.";
            return new ProviderTestResult(
                provider.ProviderName,
                !string.IsNullOrWhiteSpace(response.Content),
                string.IsNullOrWhiteSpace(response.Content) ? ThreadlineDoctorCheckStatus.Warning : ThreadlineDoctorCheckStatus.Pass,
                detail,
                stopwatch.ElapsedMilliseconds,
                response.Model,
                response.Metadata);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ProviderTestResult(
                provider.ProviderName,
                false,
                ThreadlineDoctorCheckStatus.Fail,
                $"Provider test failed: {ex.GetType().Name}: {ex.Message}",
                stopwatch.ElapsedMilliseconds,
                provider.DefaultModel);
        }
    }

    private async Task<ProviderConnection?> ResolveProviderAsync(string? providerName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return await _providers.GetAsync(providerName.Trim(), cancellationToken);
        }

        var providers = await _providers.ListAsync(cancellationToken);
        return providers.FirstOrDefault(p => p.Status == ProviderConnectionStatus.Ready)
            ?? providers.FirstOrDefault();
    }

    private static string? ValidateProvider(ProviderConnection provider)
    {
        if (provider.Status != ProviderConnectionStatus.Ready)
        {
            return $"Provider '{provider.ProviderName}' is not ready. Current status: {provider.Status}.";
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return $"Provider '{provider.ProviderName}' is missing a base URL.";
        }

        if (string.IsNullOrWhiteSpace(provider.DefaultModel))
        {
            return $"Provider '{provider.ProviderName}' is missing a default model.";
        }

        return null;
    }

    private async Task<string> ResolveApiKeyAsync(ProviderConnection provider, CancellationToken cancellationToken)
    {
        if (provider.AuthType is ProviderAuthType.None or ProviderAuthType.LocalEndpoint)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(provider.CredentialReference))
        {
            throw new InvalidOperationException($"Provider '{provider.ProviderName}' is missing a credential reference.");
        }

        return await _secrets.GetValueAsync(provider.CredentialReference, cancellationToken)
            ?? throw new InvalidOperationException($"Provider '{provider.ProviderName}' credential could not be resolved.");
    }
}
