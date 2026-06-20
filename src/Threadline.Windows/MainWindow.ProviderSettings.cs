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

    private void ToggleProviderSettingsPanel_Click(object sender, RoutedEventArgs e)
    {
        ProviderSettingsPanel.Visibility = ProviderSettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ProviderSettingsPanel.Visibility == Visibility.Visible)
        {
            ProviderSettingsStatusText.Text = "Provider settings panel open. It will stay open while you copy and paste values.";
            ApplyProviderDefaults(GetSelectedSettingsProvider(), overwriteExisting: false);
        }
    }

    private void CloseProviderSettingsPanel_Click(object sender, RoutedEventArgs e)
    {
        ProviderSettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void UseProviderDefaults_Click(object sender, RoutedEventArgs e)
    {
        ApplyProviderDefaults(GetSelectedSettingsProvider(), overwriteExisting: true);
        ProviderSettingsStatusText.Text = "Defaults applied. Add or confirm the credential, then click Save Provider.";
    }

    private async void SaveProviderSettings_Click(object sender, RoutedEventArgs e)
    {
        var provider = GetSelectedSettingsProvider();
        ProviderSettingsPanel.Visibility = Visibility.Visible;
        ProviderSettingsStatusText.Text = $"Saving {provider} provider settings...";
        ServiceStatusText.Text = $"Saving provider: {provider}";

        try
        {
            await SaveProviderSettingsAsync();
        }
        catch (Exception ex)
        {
            ProviderSettingsStatusText.Text = $"Could not save {provider}: {ex.Message}";
            ServiceStatusText.Text = "Provider save failed";
            AddTimeline($"Provider save failed for {provider}: {ex.Message}");
            AppendTranscript("Threadline Settings", $"Provider settings were not saved for {provider}.\n\nReason: {ex.Message}");
        }
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
        SelectComboBoxItem(SettingsProviderBox, provider);
        ProviderSettingsPanel.Visibility = Visibility.Visible;
        ProviderSettingsStatusText.Text = $"Saved {provider}. Start or restart a session to use this provider.";
        ServiceStatusText.Text = $"Provider saved: {provider}";
        AppendTranscript("Threadline Settings", $"Saved provider settings for {provider}. Start or restart a session with Provider set to {provider}.");
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
        "OpenAI" => new("https://api.openai.com/v1/", "gpt-4.1-mini", "Paste your OpenAI API key once, then click Save Provider. OpenAI calls use the Responses API while the key stays in local Threadline secret storage."),
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
