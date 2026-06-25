using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private ThreadlineTarget? _pendingConnectionTarget;

    private async void ConnectPendingTargetToCurrentSession_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(ConnectPendingTargetToCurrentSessionAsync);

    private async void StartNewSessionForPendingTarget_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(StartNewSessionForPendingTargetAsync);

    private async void DetachPendingTarget_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(DetachPendingOrCurrentTargetAsync);

    private async Task ConnectPendingTargetToCurrentSessionAsync()
    {
        if (_pendingConnectionTarget is null)
        {
            AppendTranscript("Threadline", "No pending window connection. Click the AI icon on another app/window first.");
            UpdateSessionBindingStatus("Window session: no pending connection. Click AI on another window to choose how to connect it.");
            return;
        }

        EnsureSession();
        AttachSidecarToWindowTarget(_pendingConnectionTarget, clearDraft: true, "Connected pending window to current session.");
        await PersistTargetContextEventAsync(_selectedThreadlineTarget, "Manual");
        _sidecarCollapsedToHandle = false;
        _sidecarWindowHiddenForTrigger = false;
        ShowMainSidecarWindow();
        SetSidecarVisualState();
        PlaceSidecarForTarget(_selectedThreadlineTarget, "Connected pending window to current session.");
        AppendTranscript("Threadline Session", $"Connected this window to the current session:\n{FormatTargetForBinding(_selectedThreadlineTarget)}");
    }

    private async Task StartNewSessionForPendingTargetAsync()
    {
        var target = _pendingConnectionTarget ?? _floatingTriggerTarget ?? _selectedThreadlineTarget;
        if (target is null)
        {
            AppendTranscript("Threadline", "No target is available for a new window session. Click the AI icon on a window first.");
            UpdateSessionBindingStatus("Window session: no target selected for a new session.");
            return;
        }

        var provider = GetSelectedProvider();
        var sessionName = $"Window session: {target.Window.ApplicationName} — {target.Title} — {DateTimeOffset.Now:g}";
        _session = await _client.StartSessionAsync(sessionName, provider);
        SessionText.Text = $"Session: {_session.Status} / {_session.ActiveProvider ?? "None"}";
        AppendTranscript("Threadline Session", "Started a new window-attached session.\n" + FormatSession(_session));

        AttachSidecarToWindowTarget(target, clearDraft: true, "Started a new session for this window.");
        await PersistTargetContextEventAsync(target, "Manual");
        _sidecarCollapsedToHandle = false;
        _sidecarWindowHiddenForTrigger = false;
        ShowMainSidecarWindow();
        SetSidecarVisualState();
        PlaceSidecarForTarget(target, "Started a new session for this window.");
    }

    private Task DetachPendingOrCurrentTargetAsync()
    {
        if (_pendingConnectionTarget is not null)
        {
            var detached = FormatTargetForBinding(_pendingConnectionTarget);
            _pendingConnectionTarget = null;
            UpdateSessionBindingStatus("Window session: pending connection cancelled. Current sidecar target was not changed.");
            AddTimeline("Cancelled pending window connection.");
            AppendTranscript("Threadline Session", $"Cancelled pending window connection:\n{detached}");
            return Task.CompletedTask;
        }

        var previousTarget = _selectedThreadlineTarget;
        _selectedThreadlineTarget = null;
        _selectedTargetWindow = null;
        _attachedSidecarTargetId = null;
        _lastFollowTarget = null;
        _attachment = null;
        UpdateSessionBindingStatus("Window session: detached from the current window. Click AI on a window to attach or start a new session.");
        DockSidecarToScreen("Sidecar: Detached from window; using screen dock fallback.");
        AppendTranscript("Threadline Session", previousTarget is null
            ? "No current window target was attached."
            : $"Detached sidecar from window:\n{FormatTargetForBinding(previousTarget)}");
        return Task.CompletedTask;
    }

    private bool IsSidecarOpenAgainstDifferentTarget(ThreadlineTarget target)
    {
        if (_sidecarWindowHiddenForTrigger) return false;
        if (_selectedThreadlineTarget is null && string.IsNullOrWhiteSpace(_attachedSidecarTargetId)) return false;
        return !string.Equals(_attachedSidecarTargetId, target.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowPendingConnection(ThreadlineTarget target)
    {
        _pendingConnectionTarget = target;
        _sidecarCollapsedToHandle = false;
        _sidecarWindowHiddenForTrigger = false;
        ShowMainSidecarWindow();
        SetSidecarVisualState();
        PlaceSidecarForTarget(target, "Pending window connection; choose how to bind this window.");
        UpdateSessionBindingStatus($"Pending window connection:\n{FormatTargetForBinding(target)}\nChoose Connect Current, New Window Session, or Detach.");
        CurrentWindowText.Text = BuildTargetStatus(target, "Pending connection");
        AddTimeline($"Pending connection: {target.Window.ApplicationName} — {target.Title}");
        AppendTranscript("Threadline Session", $"Pending window connection. Choose whether to connect this window to the current session or start a new session.\n{FormatTargetForBinding(target)}");
    }

    private void AttachSidecarToWindowTarget(ThreadlineTarget target, bool clearDraft, string reason)
    {
        _pendingConnectionTarget = null;
        _lastFollowTarget = target;
        _lastForegroundWindow = target.Window;
        _selectedThreadlineTarget = target;
        _selectedTargetWindow = target.Window;
        _attachedSidecarTargetId = target.Id;

        if (clearDraft && !string.IsNullOrWhiteSpace(QuestionBox.Text))
        {
            QuestionBox.Text = string.Empty;
            AddTimeline("Cleared draft question for newly attached target.");
        }

        CurrentWindowText.Text = BuildTargetStatus(target, "Attached target");
        UpdateSessionBindingStatus($"Connected window:\n{FormatTargetForBinding(target)}\n{reason}");
    }

    private void UpdateSessionBindingStatus(string message)
    {
        try
        {
            SessionBindingStatusText.Text = message;
            SessionBindingPanel.Visibility = _pendingConnectionTarget is null
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Session binding status update skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string FormatTargetForBinding(ThreadlineTarget? target)
    {
        if (target is null) return "No target.";
        return $"{target.Window.ApplicationName} — {target.Title}\nSource: {target.ProviderKey}\nConfidence: {target.Confidence}";
    }
}
