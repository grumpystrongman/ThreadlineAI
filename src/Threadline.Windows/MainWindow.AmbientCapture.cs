using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Threadline.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly AmbientCaptureCoordinator _ambientCapture = new();
    private TextBlock? _ambientCaptureStatusText;
    private TextBlock? _ambientCaptureDeviceText;
    private CheckBox? _ambientCaptureMicrophoneToggle;
    private CheckBox? _ambientCaptureSystemAudioToggle;
    private CheckBox? _ambientCaptureSaveAudioToggle;
    private CheckBox? _ambientCaptureTranslateToggle;
    private TextBox? _ambientCaptureTargetLanguageBox;
    private Button? _ambientCaptureStartButton;
    private Button? _ambientCaptureStopButton;
    private Button? _ambientCaptureOpenFolderButton;
    private Button? _ambientCaptureCopyHandoffButton;

    private void EnsureAmbientCaptureToolsPanel()
    {
        if (_ambientCaptureStatusText is not null) return;
        if (DiagnosticsTargetPickerPanel.Child is not ScrollViewer scrollViewer) return;
        if (scrollViewer.Content is not StackPanel toolsStack) return;

        var section = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.LightSteelBlue),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Colors.White)
        };

        var panel = new StackPanel { Spacing = 7 };
        section.Child = panel;

        panel.Children.Add(new TextBlock
        {
            Text = "Ambient Capture",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Black)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Records microphone plus current Windows output loopback, so speakers, wired headphones, Bluetooth headphones, and headsets are handled as active audio endpoints.",
            TextWrapping = TextWrapping.WrapWholeWords,
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.DimGray)
        });

        _ambientCaptureStatusText = new TextBlock
        {
            Text = "Status: ready",
            TextWrapping = TextWrapping.WrapWholeWords,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.DimGray)
        };
        panel.Children.Add(_ambientCaptureStatusText);

        _ambientCaptureDeviceText = new TextBlock
        {
            Text = "Devices: not checked yet",
            TextWrapping = TextWrapping.WrapWholeWords,
            FontSize = 10,
            IsTextSelectionEnabled = true,
            Foreground = new SolidColorBrush(Colors.DimGray)
        };
        panel.Children.Add(_ambientCaptureDeviceText);

        var toggleGrid = new Grid { ColumnSpacing = 6, RowSpacing = 3 };
        toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toggleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _ambientCaptureMicrophoneToggle = CreateAmbientToggle("Mic", true);
        _ambientCaptureSystemAudioToggle = CreateAmbientToggle("System", true);
        _ambientCaptureSaveAudioToggle = CreateAmbientToggle("Save audio", true);
        _ambientCaptureTranslateToggle = CreateAmbientToggle("Translate", true);

        toggleGrid.Children.Add(_ambientCaptureMicrophoneToggle);
        Grid.SetColumn(_ambientCaptureSystemAudioToggle, 1);
        toggleGrid.Children.Add(_ambientCaptureSystemAudioToggle);
        Grid.SetRow(_ambientCaptureSaveAudioToggle, 1);
        toggleGrid.Children.Add(_ambientCaptureSaveAudioToggle);
        Grid.SetRow(_ambientCaptureTranslateToggle, 1);
        Grid.SetColumn(_ambientCaptureTranslateToggle, 1);
        toggleGrid.Children.Add(_ambientCaptureTranslateToggle);
        panel.Children.Add(toggleGrid);

        _ambientCaptureTargetLanguageBox = new TextBox
        {
            Header = "Translation target",
            Text = "en",
            PlaceholderText = "en, es, fr...",
            FontSize = 11
        };
        panel.Children.Add(_ambientCaptureTargetLanguageBox);

        var actionGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _ambientCaptureStartButton = CreateAmbientButton("Start", StartAmbientCapture_Click, primary: true);
        _ambientCaptureStopButton = CreateAmbientButton("Stop", StopAmbientCapture_Click);
        _ambientCaptureOpenFolderButton = CreateAmbientButton("Open Folder", OpenAmbientCaptureFolder_Click);
        _ambientCaptureCopyHandoffButton = CreateAmbientButton("Copy Handoff", CopyAmbientCaptureHandoff_Click);

        actionGrid.Children.Add(_ambientCaptureStartButton);
        Grid.SetColumn(_ambientCaptureStopButton, 1);
        actionGrid.Children.Add(_ambientCaptureStopButton);
        Grid.SetRow(_ambientCaptureOpenFolderButton, 1);
        actionGrid.Children.Add(_ambientCaptureOpenFolderButton);
        Grid.SetRow(_ambientCaptureCopyHandoffButton, 1);
        Grid.SetColumn(_ambientCaptureCopyHandoffButton, 1);
        actionGrid.Children.Add(_ambientCaptureCopyHandoffButton);
        panel.Children.Add(actionGrid);

        var refreshButton = CreateAmbientButton("Refresh Devices", RefreshAmbientCaptureDevices_Click);
        panel.Children.Add(refreshButton);

        toolsStack.Children.Insert(Math.Min(4, toolsStack.Children.Count), section);
        RefreshAmbientCaptureUi();
    }

    private CheckBox CreateAmbientToggle(string text, bool isChecked) => new()
    {
        Content = text,
        IsChecked = isChecked,
        FontSize = 10,
        Foreground = new SolidColorBrush(Colors.DimGray)
    };

    private Button CreateAmbientButton(string content, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 64,
            Style = Resources[primary ? "PrimarySidecarButtonStyle" : "SidecarButtonStyle"] as Style
        };
        button.Click += handler;
        return button;
    }

    private async void StartAmbientCapture_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(StartAmbientCaptureAsync);
    private async void StopAmbientCapture_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(StopAmbientCaptureAsync);
    private void RefreshAmbientCaptureDevices_Click(object sender, RoutedEventArgs e) => RefreshAmbientCaptureUi();
    private void OpenAmbientCaptureFolder_Click(object sender, RoutedEventArgs e) => OpenAmbientCaptureFolder();
    private void CopyAmbientCaptureHandoff_Click(object sender, RoutedEventArgs e) => CopyAmbientCaptureHandoff();

    private Task StartAmbientCaptureAsync()
    {
        EnsureAmbientCaptureToolsPanel();
        _ambientCaptureLastError = null;
        _ambientCaptureIsProcessing = false;
        RefreshAmbientCaptureIndicator();

        var options = new AmbientCaptureOptions(
            CaptureMicrophone: _ambientCaptureMicrophoneToggle?.IsChecked == true,
            CaptureSystemAudio: _ambientCaptureSystemAudioToggle?.IsChecked == true,
            SaveOriginalAudio: _ambientCaptureSaveAudioToggle?.IsChecked == true,
            TranslateTranscript: _ambientCaptureTranslateToggle?.IsChecked == true,
            TargetLanguage: string.IsNullOrWhiteSpace(_ambientCaptureTargetLanguageBox?.Text) ? "en" : _ambientCaptureTargetLanguageBox!.Text.Trim());

        try
        {
            var session = _ambientCapture.Start(options);
            AppendTranscript("Ambient Capture", $"Recording started.\n\n{session.DeviceSnapshot.ToDisplayText()}\n\nFolder:\n{session.OutputFolder}");
            AddTimeline("Ambient capture started with microphone/system loopback settings.");
            RefreshAmbientCaptureUi();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _ambientCaptureLastError = ex.Message;
            RefreshAmbientCaptureUi();
            RefreshAmbientCaptureIndicator();
            throw;
        }
    }

    private async Task StopAmbientCaptureAsync()
    {
        _ambientCaptureLastError = null;
        _ambientCaptureIsProcessing = true;
        RefreshAmbientCaptureIndicator();

        AmbientCaptureSession? session = null;
        try
        {
            session = _ambientCapture.Stop();
            AppendTranscript("Ambient Capture", $"Recording stopped. Audio, manifest, and handoff saved.\n\nFolder:\n{session.OutputFolder}");
            AddTimeline("Ambient capture stopped and handoff files were generated.");
        }
        catch (Exception ex)
        {
            _ambientCaptureLastError = ex.Message;
            _ambientCaptureIsProcessing = false;
            RefreshAmbientCaptureUi();
            RefreshAmbientCaptureIndicator();
            throw;
        }

        // Attempt transcription if audio files exist
        try
        {
            var audioPath = session.MicrophoneAudioPath ?? session.SystemAudioPath;
            if (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath))
            {
                AppendTranscript("Ambient Capture", "Attempting transcription via configured provider...");

                var translate = _ambientCaptureTranslateToggle?.IsChecked == true;
                var language = string.IsNullOrWhiteSpace(_ambientCaptureTargetLanguageBox?.Text)
                    ? null
                    : _ambientCaptureTargetLanguageBox!.Text.Trim();

                var result = await _threadlineClient.TranscribeAudioAsync(
                    audioPath, provider: null, language: language, translate: translate);

                _ambientCapture.UpdateTranscript(session, result.Transcript, result.Provider);

                AppendTranscript("Ambient Capture",
                    $"Transcription complete ({result.Provider}, {result.DurationMs}ms).\n\n{(result.Transcript.Length > 300 ? result.Transcript[..300] + "..." : result.Transcript)}");
                AddTimeline($"Ambient capture transcribed by {result.Provider}.");
            }
            else
            {
                AppendTranscript("Ambient Capture", "No audio file available for transcription.");
            }
        }
        catch (Exception ex)
        {
            AppendTranscript("Ambient Capture", $"Transcription not available: {ex.Message}\nAudio and metadata are still stored — you can transcribe manually later.");
        }
        finally
        {
            _ambientCaptureIsProcessing = false;
            RefreshAmbientCaptureUi();
            RefreshAmbientCaptureIndicator();
        }
    }

    private void RefreshAmbientCaptureUi()
    {
        if (_ambientCaptureStatusText is null || _ambientCaptureDeviceText is null)
        {
            RefreshAmbientCaptureIndicator();
            return;
        }

        var snapshot = _ambientCapture.DetectDevices();
        _ambientCaptureStatusText.Text = _ambientCapture.IsRecording
            ? $"Status: recording since {_ambientCapture.CurrentSession?.StartedAt:t}. Visible recording indicator is active."
            : _ambientCapture.LastCompletedSession is null
                ? "Status: ready"
                : $"Status: stopped. Last capture: {_ambientCapture.LastCompletedSession.Title}";
        _ambientCaptureDeviceText.Text = snapshot.ToDisplayText();

        if (_ambientCaptureStartButton is not null) _ambientCaptureStartButton.IsEnabled = !_ambientCapture.IsRecording;
        if (_ambientCaptureStopButton is not null) _ambientCaptureStopButton.IsEnabled = _ambientCapture.IsRecording;
        if (_ambientCaptureOpenFolderButton is not null) _ambientCaptureOpenFolderButton.IsEnabled = _ambientCapture.CurrentSession is not null || _ambientCapture.LastCompletedSession is not null;
        if (_ambientCaptureCopyHandoffButton is not null) _ambientCaptureCopyHandoffButton.IsEnabled = _ambientCapture.LastCompletedSession is not null;

        RefreshAmbientCaptureIndicator();
    }

    private void OpenAmbientCaptureFolder()
    {
        var session = _ambientCapture.LastCompletedSession ?? _ambientCapture.CurrentSession;
        if (session is null || !Directory.Exists(session.OutputFolder))
        {
            AppendTranscript("Ambient Capture", "No ambient capture folder is available yet.");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = session.OutputFolder,
            UseShellExecute = true
        });
    }

    private void CopyAmbientCaptureHandoff()
    {
        var session = _ambientCapture.LastCompletedSession;
        if (session is null || !File.Exists(session.HandoffPath))
        {
            AppendTranscript("Ambient Capture", "No completed handoff file is available yet.");
            return;
        }

        var package = new DataPackage();
        package.SetText(File.ReadAllText(session.HandoffPath));
        Clipboard.SetContent(package);
        AppendTranscript("Ambient Capture", "Ambient capture handoff copied to clipboard.");
        AddTimeline("Copied ambient capture handoff to clipboard.");
    }
}
