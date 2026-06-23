using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private Button? _ambientCaptureIndicatorButton;
    private Ellipse? _ambientCaptureIndicatorGlow;
    private Ellipse? _ambientCaptureIndicatorDot;
    private ScaleTransform? _ambientCaptureIndicatorScale;
    private Storyboard? _ambientCapturePulseStoryboard;
    private bool _ambientCaptureIsProcessing;
    private string? _ambientCaptureLastError;

    private void EnsureAmbientCaptureIndicator()
    {
        if (_ambientCaptureIndicatorButton is not null) return;

        _ambientCaptureIndicatorScale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        _ambientCaptureIndicatorGlow = new Ellipse
        {
            Width = 26,
            Height = 26,
            Fill = new SolidColorBrush(Colors.Red),
            Opacity = 0.30,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _ambientCaptureIndicatorDot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = new SolidColorBrush(Colors.Red),
            Stroke = new SolidColorBrush(Colors.MistyRose),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var indicatorRoot = new Grid
        {
            Width = 32,
            Height = 32,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = _ambientCaptureIndicatorScale
        };
        indicatorRoot.Children.Add(_ambientCaptureIndicatorGlow);
        indicatorRoot.Children.Add(_ambientCaptureIndicatorDot);

        _ambientCaptureIndicatorButton = new Button
        {
            Width = 36,
            Height = 36,
            MinWidth = 36,
            MinHeight = 36,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            Visibility = Visibility.Collapsed,
            Content = indicatorRoot
        };
        _ambientCaptureIndicatorButton.Click += AmbientCaptureIndicator_Click;
        ToolTipService.SetToolTip(_ambientCaptureIndicatorButton, "Ambient Capture status");
        AutomationProperties.SetName(_ambientCaptureIndicatorButton, "Ambient Capture status");
        Canvas.SetZIndex(_ambientCaptureIndicatorButton, 1000);

        _ambientCapturePulseStoryboard = BuildAmbientCapturePulseStoryboard();
        ChatShellPanel.Children.Add(_ambientCaptureIndicatorButton);
    }

    private Storyboard BuildAmbientCapturePulseStoryboard()
    {
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        if (_ambientCaptureIndicatorGlow is not null)
        {
            var glowAnimation = new DoubleAnimation
            {
                From = 0.22,
                To = 0.76,
                Duration = new Duration(TimeSpan.FromMilliseconds(1450)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(glowAnimation, _ambientCaptureIndicatorGlow);
            Storyboard.SetTargetProperty(glowAnimation, "Opacity");
            storyboard.Children.Add(glowAnimation);
        }
        if (_ambientCaptureIndicatorDot is not null)
        {
            var dotAnimation = new DoubleAnimation
            {
                From = 0.56,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(1450)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(dotAnimation, _ambientCaptureIndicatorDot);
            Storyboard.SetTargetProperty(dotAnimation, "Opacity");
            storyboard.Children.Add(dotAnimation);
        }
        if (_ambientCaptureIndicatorScale is not null)
        {
            var scaleXAnimation = new DoubleAnimation
            {
                From = 0.94,
                To = 1.08,
                Duration = new Duration(TimeSpan.FromMilliseconds(1450)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(scaleXAnimation, _ambientCaptureIndicatorScale);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");
            storyboard.Children.Add(scaleXAnimation);

            var scaleYAnimation = new DoubleAnimation
            {
                From = 0.94,
                To = 1.08,
                Duration = new Duration(TimeSpan.FromMilliseconds(1450)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(scaleYAnimation, _ambientCaptureIndicatorScale);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");
            storyboard.Children.Add(scaleYAnimation);
        }
        return storyboard;
    }

    private void RefreshAmbientCaptureIndicator()
    {
        EnsureAmbientCaptureIndicator();
        if (_ambientCaptureIndicatorButton is null || _ambientCaptureIndicatorGlow is null || _ambientCaptureIndicatorDot is null) return;

        _ambientCapturePulseStoryboard?.Stop();
        if (!string.IsNullOrWhiteSpace(_ambientCaptureLastError))
        {
            _ambientCaptureIndicatorButton.Visibility = Visibility.Visible;
            _ambientCaptureIndicatorGlow.Fill = new SolidColorBrush(Colors.Firebrick);
            _ambientCaptureIndicatorDot.Fill = new SolidColorBrush(Colors.Firebrick);
            _ambientCaptureIndicatorGlow.Opacity = 0.42;
            ToolTipService.SetToolTip(_ambientCaptureIndicatorButton, "Ambient Capture needs attention. Click to open the tool.");
            AutomationProperties.SetName(_ambientCaptureIndicatorButton, "Ambient Capture needs attention");
            return;
        }

        if (_ambientCaptureIsProcessing)
        {
            _ambientCaptureIndicatorButton.Visibility = Visibility.Visible;
            _ambientCaptureIndicatorGlow.Fill = new SolidColorBrush(Colors.DodgerBlue);
            _ambientCaptureIndicatorDot.Fill = new SolidColorBrush(Colors.DodgerBlue);
            ToolTipService.SetToolTip(_ambientCaptureIndicatorButton, "Ambient Capture is processing. Click to open the tool.");
            AutomationProperties.SetName(_ambientCaptureIndicatorButton, "Ambient Capture is processing");
            _ambientCapturePulseStoryboard?.Begin();
            return;
        }

        if (_ambientCapture.IsRecording)
        {
            _ambientCaptureIndicatorButton.Visibility = Visibility.Visible;
            _ambientCaptureIndicatorGlow.Fill = new SolidColorBrush(Colors.Red);
            _ambientCaptureIndicatorDot.Fill = new SolidColorBrush(Colors.Red);
            ToolTipService.SetToolTip(_ambientCaptureIndicatorButton, "Ambient Capture is recording. Click to open the tool.");
            AutomationProperties.SetName(_ambientCaptureIndicatorButton, "Ambient Capture is recording");
            _ambientCapturePulseStoryboard?.Begin();
            return;
        }

        _ambientCaptureIndicatorButton.Visibility = Visibility.Collapsed;
    }

    private void AmbientCaptureIndicator_Click(object sender, RoutedEventArgs e)
    {
        OpenDiagnosticsTargetPickerPanel();
        AddTimeline("Opened Ambient Capture from the recording indicator.");
    }
}
