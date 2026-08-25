using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Threadline.Core;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private Button? _ownerAuthorityButton;
    private DeviceAuthorityGrant _ownerAuthority = DeviceAuthorityGrant.OwnerDefault;
    private Action<DeviceAuthorityGrant>? _saveOwnerAuthority;

    public void ConfigureOwnerAuthority(DeviceAuthorityGrant grant, Action<DeviceAuthorityGrant> save)
    {
        _ownerAuthority = grant;
        _saveOwnerAuthority = save;
        EnsureOwnerAuthorityButton();
        RefreshOwnerAuthorityButton();
        AddTimeline($"Owner authority loaded: {(grant.Enabled ? "enabled" : "disabled")}; protected operations require confirmation.");
    }

    private void EnsureOwnerAuthorityButton()
    {
        if (_ownerAuthorityButton is not null) return;
        if (AttachSidecarButton.Parent is not StackPanel headerButtons) return;

        var button = new Button
        {
            MinWidth = 82,
            Padding = new Thickness(9, 4, 9, 4)
        };
        ToolTipService.SetToolTip(button, "Owner Authority controls what AIKA/JARVIS may do on this computer without asking each step.");
        if (Resources.TryGetValue("SidecarButtonStyle", out var style) && style is Style buttonStyle) button.Style = buttonStyle;
        button.Click += OwnerAuthorityButton_Click;
        var insertAt = Math.Max(0, headerButtons.Children.IndexOf(AttachSidecarButton));
        headerButtons.Children.Insert(insertAt, button);
        _ownerAuthorityButton = button;
    }

    private void RefreshOwnerAuthorityButton()
    {
        if (_ownerAuthorityButton is null) return;
        _ownerAuthorityButton.Content = _ownerAuthority.Enabled ? "Authority: On" : "Authority: Off";
    }

    private async void OwnerAuthorityButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = Toggle("Allow AIKA/JARVIS to operate this computer on my behalf", _ownerAuthority.Enabled);
        var routine = Toggle("Routine operations", _ownerAuthority.AllowRoutineOperations);
        var consequential = Toggle("Consequential operations", _ownerAuthority.AllowConsequentialOperations);
        var launch = Toggle("Open applications", _ownerAuthority.AllowApplicationLaunch);
        var input = Toggle("Keyboard and mouse fallback", _ownerAuthority.AllowKeyboardAndMouse);
        var shell = Toggle("PowerShell / command execution", _ownerAuthority.AllowShellCommands);
        var files = Toggle("Write files", _ownerAuthority.AllowFileWrites);
        var appSpecific = Toggle("App-specific automation (browser/Office/etc.)", _ownerAuthority.AllowAppSpecificAutomation);

        var panel = new StackPanel { Spacing = 10, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "This is your persistent agency grant. Jarvis can read it but cannot expand it. Protected/elevated operations always remain on the separate confirmation/broker path.",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var control in new[] { enabled, routine, consequential, launch, input, shell, files, appSpecific }) panel.Children.Add(control);
        panel.Children.Add(new TextBlock
        {
            Text = "Always protected: UAC/secure desktop, credential export, security-control changes, and other privileged broker operations.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Owner Authority",
            Content = panel,
            PrimaryButtonText = "Save authority",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var updated = new DeviceAuthorityGrant(
            Enabled: enabled.IsOn,
            AllowRoutineOperations: routine.IsOn,
            AllowConsequentialOperations: consequential.IsOn,
            AllowKeyboardAndMouse: input.IsOn,
            AllowShellCommands: shell.IsOn,
            AllowFileWrites: files.IsOn,
            AllowApplicationLaunch: launch.IsOn,
            AllowAppSpecificAutomation: appSpecific.IsOn,
            RequireConfirmationForProtectedOperations: true);

        try
        {
            _saveOwnerAuthority?.Invoke(updated);
            _ownerAuthority = updated;
            RefreshOwnerAuthorityButton();
            AppendTranscript("AIKA / JARVIS", updated.Enabled
                ? "Owner Authority is enabled. I can carry out the allowed routine and consequential work on your behalf without stopping for every step. Protected operations remain confirmation-gated."
                : "Owner Authority is disabled. I can still observe and answer, but device mutations are blocked until you re-enable authority.");
            AddTimeline("Owner Authority settings saved locally.");
        }
        catch (Exception ex)
        {
            AppendTranscript("AIKA / JARVIS", $"I couldn't save Owner Authority: {ex.Message}");
        }
    }

    private static ToggleSwitch Toggle(string header, bool isOn) => new() { Header = header, IsOn = isOn };
}
