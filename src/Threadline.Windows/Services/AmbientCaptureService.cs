using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Threadline.Windows.Services;

public sealed record AmbientCaptureOptions(
    bool CaptureMicrophone = true,
    bool CaptureSystemAudio = true,
    bool SaveOriginalAudio = true,
    bool TranslateTranscript = true,
    string TargetLanguage = "en");

public sealed record AmbientAudioDeviceSnapshot(
    DateTimeOffset CapturedAt,
    string? InputDeviceId,
    string? InputDeviceName,
    string? OutputDeviceId,
    string? OutputDeviceName,
    string OutputDeviceKind,
    bool IsBluetooth,
    bool IsHeadset,
    bool IsDefaultCommunicationDevice)
{
    public string ToDisplayText() =>
        $"Mic: {InputDeviceName ?? "not available"}\nOutput: {OutputDeviceName ?? "not available"}\nKind: {OutputDeviceKind}";
}

public sealed record AmbientCaptureSession(
    string Id,
    string Title,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Status,
    AmbientCaptureOptions Options,
    AmbientAudioDeviceSnapshot DeviceSnapshot,
    string OutputFolder,
    string? MicrophoneAudioPath,
    string? SystemAudioPath,
    string ManifestPath,
    string TranscriptPath,
    string HandoffPath)
{
    public AmbientCaptureSession Complete(DateTimeOffset endedAt) => this with { EndedAt = endedAt, Status = "Completed" };
}

public sealed class AmbientCaptureCoordinator : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly string _captureRoot;
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private WasapiCapture? _microphoneCapture;
    private WasapiLoopbackCapture? _systemCapture;
    private WaveFileWriter? _microphoneWriter;
    private WaveFileWriter? _systemWriter;
    private AmbientCaptureSession? _currentSession;
    private AmbientCaptureSession? _lastCompletedSession;

    public AmbientCaptureCoordinator()
    {
        _captureRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThreadlineAI",
            "AmbientCapture");
    }

    public bool IsRecording => _currentSession is not null;
    public AmbientCaptureSession? CurrentSession => _currentSession;
    public AmbientCaptureSession? LastCompletedSession => _lastCompletedSession;

    public AmbientAudioDeviceSnapshot DetectDevices()
    {
        var input = TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            ?? TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        var output = TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            ?? TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);

        var outputName = output?.FriendlyName;
        var inputName = input?.FriendlyName;
        var combinedName = string.Join(" ", new[] { inputName, outputName }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new AmbientAudioDeviceSnapshot(
            DateTimeOffset.Now,
            input?.ID,
            inputName,
            output?.ID,
            outputName,
            InferOutputKind(outputName),
            ContainsAny(combinedName, "bluetooth", "bt", "hands-free", "hands free"),
            ContainsAny(combinedName, "headset", "headphone", "headphones", "earbud", "earbuds", "jabra", "sony", "bose", "airpods", "poly", "plantronics"),
            input is not null && string.Equals(input.ID, TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)?.ID, StringComparison.OrdinalIgnoreCase));
    }

    public AmbientCaptureSession Start(AmbientCaptureOptions options)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("Ambient capture is already recording.");
        }

        if (!options.CaptureMicrophone && !options.CaptureSystemAudio)
        {
            throw new InvalidOperationException("Select microphone capture, system audio capture, or both before starting ambient capture.");
        }

        Directory.CreateDirectory(_captureRoot);
        var now = DateTimeOffset.Now;
        var id = $"amb_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var folder = Path.Combine(_captureRoot, id);
        Directory.CreateDirectory(folder);

        var snapshot = DetectDevices();
        var microphonePath = options.CaptureMicrophone ? Path.Combine(folder, "microphone.wav") : null;
        var systemPath = options.CaptureSystemAudio ? Path.Combine(folder, "system-loopback.wav") : null;
        var session = new AmbientCaptureSession(
            id,
            $"Ambient Capture {now:yyyy-MM-dd HH:mm}",
            now,
            null,
            "Recording",
            options,
            snapshot,
            folder,
            microphonePath,
            systemPath,
            Path.Combine(folder, "manifest.json"),
            Path.Combine(folder, "transcript.md"),
            Path.Combine(folder, "handoff.md"));

        WriteSessionFiles(session, "Recording started. Transcript is created when recording stops.");

        try
        {
            StartMicrophoneIfRequested(session);
            StartSystemLoopbackIfRequested(session);
            _currentSession = session;
            return session;
        }
        catch
        {
            SafeStopCapture();
            _currentSession = null;
            throw;
        }
    }

    public AmbientCaptureSession Stop()
    {
        var session = _currentSession ?? throw new InvalidOperationException("Ambient capture is not recording.");
        SafeStopCapture();
        var completed = session.Complete(DateTimeOffset.Now);
        WriteSessionFiles(completed, BuildPendingTranscriptNotice(completed));
        _lastCompletedSession = completed;
        _currentSession = null;
        return completed;
    }

    public void Dispose()
    {
        SafeStopCapture();
        _deviceEnumerator.Dispose();
    }

    private void StartMicrophoneIfRequested(AmbientCaptureSession session)
    {
        if (!session.Options.CaptureMicrophone || string.IsNullOrWhiteSpace(session.MicrophoneAudioPath)) return;

        var input = TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            ?? TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            ?? throw new InvalidOperationException("No microphone or headset input device is available.");

        _microphoneCapture = new WasapiCapture(input);
        _microphoneWriter = new WaveFileWriter(session.MicrophoneAudioPath, _microphoneCapture.WaveFormat);
        _microphoneCapture.DataAvailable += (_, args) =>
        {
            lock (_gate)
            {
                _microphoneWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                _microphoneWriter?.Flush();
            }
        };
        _microphoneCapture.StartRecording();
    }

    private void StartSystemLoopbackIfRequested(AmbientCaptureSession session)
    {
        if (!session.Options.CaptureSystemAudio || string.IsNullOrWhiteSpace(session.SystemAudioPath)) return;

        var output = TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            ?? TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Communications)
            ?? throw new InvalidOperationException("No speaker, headphone, or Bluetooth output device is available for loopback capture.");

        _systemCapture = new WasapiLoopbackCapture(output);
        _systemWriter = new WaveFileWriter(session.SystemAudioPath, _systemCapture.WaveFormat);
        _systemCapture.DataAvailable += (_, args) =>
        {
            lock (_gate)
            {
                _systemWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                _systemWriter?.Flush();
            }
        };
        _systemCapture.StartRecording();
    }

    private void SafeStopCapture()
    {
        var microphoneCapture = _microphoneCapture;
        var systemCapture = _systemCapture;

        _microphoneCapture = null;
        _systemCapture = null;

        try { microphoneCapture?.StopRecording(); } catch { }
        try { systemCapture?.StopRecording(); } catch { }

        lock (_gate)
        {
            _microphoneWriter?.Dispose();
            _systemWriter?.Dispose();
            _microphoneWriter = null;
            _systemWriter = null;
        }

        microphoneCapture?.Dispose();
        systemCapture?.Dispose();
    }

    private MMDevice? TryGetDefaultAudioEndpoint(DataFlow flow, Role role)
    {
        try
        {
            return _deviceEnumerator.GetDefaultAudioEndpoint(flow, role);
        }
        catch
        {
            return null;
        }
    }

    private static string InferOutputKind(string? outputName)
    {
        if (string.IsNullOrWhiteSpace(outputName)) return "Unavailable";
        if (ContainsAny(outputName, "bluetooth", "hands-free", "hands free")) return "Bluetooth headphones/headset";
        if (ContainsAny(outputName, "headset")) return "Headset";
        if (ContainsAny(outputName, "headphone", "headphones", "earbud", "earbuds")) return "Headphones";
        if (ContainsAny(outputName, "speaker", "speakers", "realtek")) return "Speakers / wired jack endpoint";
        return "Windows render endpoint";
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildPendingTranscriptNotice(AmbientCaptureSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Ambient Capture Transcript");
        builder.AppendLine();
        builder.AppendLine($"Session: {session.Title}");
        builder.AppendLine($"Started: {session.StartedAt:O}");
        builder.AppendLine($"Ended: {session.EndedAt:O}");
        builder.AppendLine();
        builder.AppendLine("## Transcription status");
        builder.AppendLine();
        builder.AppendLine("Audio was recorded and stored locally. The pluggable transcription/translation provider is not configured in this build yet, so this transcript is intentionally marked pending rather than pretending speech-to-text has completed.");
        builder.AppendLine();
        builder.AppendLine("## Recorded tracks");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(session.MicrophoneAudioPath)) builder.AppendLine($"- Microphone: `{Path.GetFileName(session.MicrophoneAudioPath)}`");
        if (!string.IsNullOrWhiteSpace(session.SystemAudioPath)) builder.AppendLine($"- System audio loopback: `{Path.GetFileName(session.SystemAudioPath)}`");
        return builder.ToString();
    }

    private static void WriteSessionFiles(AmbientCaptureSession session, string transcriptContent)
    {
        File.WriteAllText(session.ManifestPath, JsonSerializer.Serialize(session, JsonOptions));
        File.WriteAllText(session.TranscriptPath, transcriptContent);
        File.WriteAllText(session.HandoffPath, BuildHandoff(session));
    }

    private static string BuildHandoff(AmbientCaptureSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Handoff: {session.Title}");
        builder.AppendLine();
        builder.AppendLine($"Status: {session.Status}");
        builder.AppendLine($"Started: {session.StartedAt:g}");
        if (session.EndedAt is not null) builder.AppendLine($"Ended: {session.EndedAt:g}");
        builder.AppendLine();
        builder.AppendLine("## Capture sources");
        builder.AppendLine();
        builder.AppendLine($"- Microphone/input: {session.DeviceSnapshot.InputDeviceName ?? "not available"}");
        builder.AppendLine($"- System output: {session.DeviceSnapshot.OutputDeviceName ?? "not available"}");
        builder.AppendLine($"- Output kind: {session.DeviceSnapshot.OutputDeviceKind}");
        builder.AppendLine($"- Bluetooth detected: {session.DeviceSnapshot.IsBluetooth}");
        builder.AppendLine($"- Headset/headphones detected: {session.DeviceSnapshot.IsHeadset}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine(session.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            ? "Recording completed and source audio is stored locally. Transcription and translation are pending provider integration."
            : "Recording is in progress.");
        builder.AppendLine();
        builder.AppendLine("## Share checklist");
        builder.AppendLine();
        builder.AppendLine("- Confirm consent and sharing rules before sending audio or transcript content.");
        builder.AppendLine("- Review and redact transcript/handoff content before sharing externally.");
        builder.AppendLine("- Delete source audio if retention policy requires transcript-only storage.");
        return builder.ToString();
    }
}
