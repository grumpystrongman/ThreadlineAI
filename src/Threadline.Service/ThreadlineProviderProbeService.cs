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
    private readonly IAuditRepository _audit;
    private readonly IClock _clock;

    public ThreadlineProviderProbeService(
        ProviderConnectionService providers,
        SecretService secrets,
        IHttpClientFactory httpClientFactory,
        IAuditRepository audit,
        IClock clock)
    {
        _providers = providers;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ProviderTestResult> TestAsync(string? providerName = null, CancellationToken cancellationToken = default)
    {
        var result = await RunTestAsync(providerName, cancellationToken);
        await AppendProviderTestAuditAsync(result, cancellationToken);
        return result;
    }

    private async Task<ProviderTestResult> RunTestAsync(string? providerName, CancellationToken cancellationToken)
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
            var llmProvider = LlmProviderFactory.Create(
                _httpClientFactory.CreateClient(nameof(ThreadlineProviderProbeService)),
                provider.ProviderName, provider.BaseUrl!, apiKey, provider.DefaultModel!);

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

    private async Task AppendProviderTestAuditAsync(ProviderTestResult result, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "ThreadlineProviderTest",
            ["provider"] = result.ProviderName,
            ["success"] = result.Success.ToString(),
            ["status"] = result.Status.ToString(),
            ["detail"] = result.Detail,
            ["durationMs"] = result.DurationMs.ToString()
        };

        if (!string.IsNullOrWhiteSpace(result.Model))
        {
            metadata["model"] = result.Model;
        }

        if (result.Metadata is not null)
        {
            foreach (var pair in result.Metadata)
            {
                metadata[$"provider.{pair.Key}"] = pair.Value;
            }
        }

        await _audit.AppendAuditEventAsync(
            AuditEvent.Create(
                null,
                result.Success ? AuditEventType.ProviderCallCompleted : AuditEventType.ProviderCallFailed,
                _clock.UtcNow,
                result.Success ? $"Provider test succeeded: {result.ProviderName}" : $"Provider test failed: {result.ProviderName}",
                metadata),
            cancellationToken);
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
