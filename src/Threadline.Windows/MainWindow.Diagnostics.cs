using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    /// <summary>
    /// Asynchronously gathers product diagnostics and displays them in the Diagnostics panel.
    /// This includes service health, provider configuration, provider test result,
    /// session/work-thread status, browser extension connection, current context source,
    /// and the last provider error. The diagnostics string is written to DiagnosticsText.Text.
    /// </summary>
    private async Task ShowProductDiagnosticsAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Product Diagnostics ===");

        // Service running?
        try
        {
            var health = await _client.GetHealthAsync();
            sb.AppendLine($"Service: {health}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Service: Unreachable - {ex.Message}");
        }

        // Provider configured?
        var provider = GetSelectedProvider();
        var baseUrl = ProviderBaseUrlBox?.Text?.Trim() ?? string.Empty;
        var model = ProviderModelBox?.Text?.Trim() ?? string.Empty;
        var apiKeyNeeded = !IsLocalProvider(provider);
        var hasApiKey = !string.IsNullOrWhiteSpace(ProviderApiKeyBox?.Password);
        var providerConfigured = !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(model) && (!apiKeyNeeded || hasApiKey);
        sb.AppendLine($"Provider configured: {(providerConfigured ? "Yes" : "No")} ({provider})");

        // Provider connectivity test
        var testResult = await TestProviderSettingsAsync();
        sb.AppendLine($"Provider test: {testResult}");

        // Active session?
        sb.AppendLine($"Active session: {(_session != null ? "Yes" : "No")}");

        // Work thread active?
        try
        {
            var workThread = await _workThreadClient.GetActiveWorkThreadAsync();
            sb.AppendLine($"Work thread active: {(workThread != null ? "Yes" : "No")}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Work thread access failed: {ex.Message}");
        }

        // Browser extension connected?
        var contextSource = _lastContextSummary?.Source ?? "None";
        var browserConnected = contextSource.Contains("browser", StringComparison.OrdinalIgnoreCase);
        sb.AppendLine($"Browser extension connected: {(browserConnected ? "Yes" : "No")}");

        // Current context source
        sb.AppendLine($"Current context source: {contextSource}");

        // Last provider error (derived from ServiceStatusText)
        var lastProviderError = ServiceStatusText?.Text?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true
            ? ServiceStatusText.Text
            : "None";
        sb.AppendLine($"Last provider error: {lastProviderError}");

        DiagnosticsText.Text = sb.ToString();
    }
}
