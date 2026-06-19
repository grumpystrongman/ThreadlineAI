using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsProviderBox is null) return;

        var provider = GetSelectedProvider();
        SelectComboBoxItem(SettingsProviderBox, provider);
        ApplyProviderDefaults(provider, overwriteExisting: false);
    }

    private void SettingsProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderBaseUrlBox is null || ProviderModelBox is null || ProviderApiKeyBox is null) return;

        var provider = GetSelectedSettingsProvider();
        SelectComboBoxItem(ProviderBox, provider);
        ApplyProviderDefaults(provider, overwriteExisting: false);
    }

    private void UseProviderDefaults_Click(object sender, RoutedEventArgs e)
    {
        ApplyProviderDefaults(GetSelectedSettingsProvider(), overwriteExisting: true);
        ProviderSettingsStatusText.Text = "Defaults applied. Add or confirm the credential, then save the provider.";
    }

    private async void SaveProviderSettings_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(SaveProviderSettingsAsync);
    }

    private async Task SaveProviderSettingsAsync()
    {
        var provider = GetSelectedSettingsProvider();
        var baseUrl = ProviderBaseUrlBox.Text?.Trim() ?? string.Empty;
        var model = ProviderModelBox.Text?.Trim() ?? string.Empty;
        var apiKey = ProviderApiKeyBox.Password?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Provider base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Provider default model is required.");
        }

        if (IsLocalProvider(provider))
        {
            await _client.SaveLocalProviderAsync(provider, baseUrl, model);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException($"{provider} requires an API key before it can be saved.");
            }

            await _client.SaveProviderCredentialAsync(provider, apiKey, baseUrl, model);
            ProviderApiKeyBox.Password = string.Empty;
        }

        SelectComboBoxItem(ProviderBox, provider);
        ProviderSettingsStatusText.Text = $"Saved {provider}. Start a new session to use this provider.";
        ServiceStatusText.Text = $"Provider saved: {provider}";
        AppendTranscript("Threadline Settings", $"Saved provider settings for {provider}. Start a new session with Provider set to {provider}.");
        AddTimeline($"Saved provider settings for {provider}.");
    }

    private void ApplyProviderDefaults(string provider, bool overwriteExisting)
    {
        var defaults = GetProviderDefaults(provider);

        if (overwriteExisting || string.IsNullOrWhiteSpace(ProviderBaseUrlBox.Text))
        {
            ProviderBaseUrlBox.Text = defaults.BaseUrl;
        }

        if (overwriteExisting || string.IsNullOrWhiteSpace(ProviderModelBox.Text))
        {
            ProviderModelBox.Text = defaults.DefaultModel;
        }

        ProviderSettingsHintText.Text = defaults.Hint;
    }

    private string GetSelectedSettingsProvider()
    {
        if (SettingsProviderBox.SelectedItem is ComboBoxItem item && item.Content is not null)
        {
            return item.Content.ToString() ?? "OpenAI";
        }

        return GetSelectedProvider();
    }

    private static ProviderDefaults GetProviderDefaults(string provider) => provider switch
    {
        "OpenAI" => new("https://api.openai.com/v1/", "gpt-4.1-mini", "Paste your OpenAI API key once, then click Save Provider. The key is stored by the local Threadline service."),
        "Claude" => new("https://api.anthropic.com/v1/", "claude-3-5-sonnet-latest", "Claude may need an Anthropic-compatible adapter before provider execution is complete. Save the setting here so the sidecar has one place for provider setup."),
        "Gemini" => new("https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-1.5-flash", "Use Gemini's OpenAI-compatible endpoint when available for your key/project."),
        "DeepSeek" => new("https://api.deepseek.com/v1/", "deepseek-chat", "DeepSeek is OpenAI-compatible. Paste the API key once, then save."),
        "OpenRouter" => new("https://openrouter.ai/api/v1/", "openai/gpt-4o-mini", "OpenRouter is OpenAI-compatible. Pick a model your OpenRouter account can access."),
        "Local" => new("http://localhost:1234/v1/", "local-model", "Local uses an OpenAI-compatible local endpoint such as LM Studio. API key is not required for Local."),
        _ => new(string.Empty, string.Empty, "Select a provider, confirm the base URL/model, then save.")
    };

    private static bool IsLocalProvider(string provider) =>
        provider.Equals("Local", StringComparison.OrdinalIgnoreCase);

    private static void SelectComboBoxItem(ComboBox comboBox, string provider)
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item && string.Equals(item.Content?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private sealed record ProviderDefaults(string BaseUrl, string DefaultModel, string Hint);
}
