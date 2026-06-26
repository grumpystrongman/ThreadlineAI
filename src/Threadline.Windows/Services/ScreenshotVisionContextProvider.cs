using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Threadline.Windows.Services;

public sealed record ScreenshotVisionCaptureResult(
    bool Success,
    string VisionSummary,
    string OcrText,
    string RedactedOcrText,
    int RedactionCount,
    IReadOnlyList<string> RedactionCategories,
    ScreenshotRegion Region,
    bool RawScreenshotStored,
    string? RawScreenshotPath,
    ContextConfidence Confidence,
    IReadOnlyList<string> Warnings)
{
    public static ScreenshotVisionCaptureResult Failed(string warning) => new(
        false,
        "Screenshot/OCR/vision capture did not run.",
        string.Empty,
        string.Empty,
        0,
        [],
        ScreenshotRegion.Empty,
        false,
        null,
        ContextConfidence.Low,
        [warning]);
}

public readonly record struct ScreenshotRegion(int Left, int Top, int Width, int Height)
{
    public static ScreenshotRegion Empty { get; } = new(0, 0, 0, 0);

    public string ToDisplayText() => Width <= 0 || Height <= 0
        ? "No capture region."
        : $"left={Left}, top={Top}, width={Width}, height={Height}";
}

public sealed class ScreenshotVisionContextProvider
{
    private const int MaxOcrCharacters = 12000;
    private const int DwmExtendedFrameBounds = 9;

    public async Task<ScreenshotVisionCaptureResult> CaptureAsync(ThreadlineTarget target, ContextCaptureConsent consent, CancellationToken cancellationToken = default)
    {
        if (!consent.CanUseScreenshotVision)
        {
            return ScreenshotVisionCaptureResult.Failed(consent.ScreenshotVisionConsentReason);
        }

        var warnings = new List<string>
        {
            "Screenshot/OCR/vision ran only because one-time user approval and app policy allowed this capture."
        };

        if (!TryGetWindowRegion(target.Window.Handle, out var region, out var regionWarning))
        {
            return ScreenshotVisionCaptureResult.Failed(regionWarning ?? "Threadline could not resolve a safe window capture region.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        byte[] screenshotPngBytes;
        try
        {
            screenshotPngBytes = CaptureWindowRegionAsPng(region);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
        {
            return ScreenshotVisionCaptureResult.Failed($"Screenshot capture failed: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var ocrText = await ExtractOcrTextAsync(screenshotPngBytes, warnings, cancellationToken);
        var redacted = SensitiveContentRedactor.Redact(ocrText);
        if (redacted.RedactionCount > 0)
        {
            warnings.Add($"Redaction before provider handoff replaced {redacted.RedactionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} sensitive-looking value(s): {string.Join(", ", redacted.Categories)}.");
        }

        bool rawStored = false;
        string? rawPath = null;

        if (!consent.RawScreenshotStorageAllowed)
        {
            Array.Clear(screenshotPngBytes, 0, screenshotPngBytes.Length);
        }
        else
        {
            try
            {
                rawPath = SaveRawScreenshot(screenshotPngBytes, target);
                rawStored = true;
                warnings.Add($"Raw screenshot saved to local storage: {Path.GetFileName(rawPath)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Raw screenshot storage failed: {ex.Message}. Image bytes remain in memory only for this request.");
            }
        }

        var visionSummary = BuildVisionSummary(target, redacted.Text, region, warnings, rawStored);
        var confidence = string.IsNullOrWhiteSpace(redacted.Text)
            ? ContextConfidence.Low
            : ContextConfidence.Medium;

        return new ScreenshotVisionCaptureResult(
            true,
            visionSummary,
            Truncate(ocrText),
            Truncate(redacted.Text),
            redacted.RedactionCount,
            redacted.Categories,
            region,
            rawStored,
            rawPath,
            confidence,
            warnings);
    }

    private static byte[] CaptureWindowRegionAsPng(ScreenshotRegion region)
    {
        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.Left, region.Top, 0, 0, new Size(region.Width, region.Height), CopyPixelOperation.SourceCopy);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static async Task<string> ExtractOcrTextAsync(byte[] screenshotPngBytes, List<string> warnings, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            warnings.Add("Windows OCR engine was not available for the current user languages.");
            return string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccessStream))
        {
            writer.WriteBytes(screenshotPngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        randomAccessStream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var ocrResult = await engine.RecognizeAsync(softwareBitmap);

        var text = string.Join(
            Environment.NewLine,
            ocrResult.Lines
                .Select(line => line.Text?.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(text))
        {
            warnings.Add("OCR completed, but no readable text was detected in the approved window region.");
        }

        return Truncate(text);
    }

    private static string SaveRawScreenshot(byte[] pngBytes, ThreadlineTarget target)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThreadlineAI",
            "screenshots");
        Directory.CreateDirectory(root);

        var processName = string.IsNullOrWhiteSpace(target.Window.ProcessName)
            ? "unknown"
            : SanitizeFileName(target.Window.ProcessName);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var fileName = $"{timestamp}_{processName}.png";
        var filePath = Path.Combine(root, fileName);

        File.WriteAllBytes(filePath, pngBytes);
        return filePath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string BuildVisionSummary(ThreadlineTarget target, string redactedOcrText, ScreenshotRegion region, IReadOnlyList<string> warnings, bool rawScreenshotStored = false)
    {
        var targetTitle = string.IsNullOrWhiteSpace(target.Title) ? "the attached window" : target.Title;
        if (string.IsNullOrWhiteSpace(redactedOcrText))
        {
            var storageNote = rawScreenshotStored
                ? "The raw screenshot was saved locally and can be sent to vision-capable models."
                : "The raw image was not stored or sent as an image provider payload.";
            return $"Threadline captured an approved screenshot region for '{targetTitle}' ({region.ToDisplayText()}) and attempted OCR, but no readable text was detected. {storageNote}";
        }

        var cleanedLines = redactedOcrText
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(10)
            .ToList();

        var joined = string.Join(" ", cleanedLines);
        if (joined.Length > 900)
        {
            joined = joined[..900].TrimEnd() + "...";
        }

        var warningHint = warnings.Count == 0 ? string.Empty : $" Warnings: {warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";
        var rawNote = rawScreenshotStored
            ? "The raw screenshot was saved locally and can be sent to vision-capable models."
            : "The raw screenshot was not stored.";
        return $"Threadline captured an approved screenshot region for '{targetTitle}' ({region.ToDisplayText()}), extracted visible OCR text, and redacted sensitive-looking values before prompt handoff. {rawNote} The visible content appears to focus on: {joined}{warningHint}";
    }

    private static bool TryGetWindowRegion(nint handle, out ScreenshotRegion region, out string? warning)
    {
        region = ScreenshotRegion.Empty;
        warning = null;

        if (handle == nint.Zero)
        {
            warning = "The target window handle was empty.";
            return false;
        }

        if (IsIconic(handle))
        {
            warning = "The target window is minimized. Threadline will not capture the desktop behind it.";
            return false;
        }

        var rect = default(RECT);
        var dwmResult = DwmGetWindowAttribute(handle, DwmExtendedFrameBounds, out rect, Marshal.SizeOf<RECT>());
        if (dwmResult != 0 && !GetWindowRect(handle, out rect))
        {
            warning = "Threadline could not read the target window bounds.";
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            warning = "The target window bounds were empty.";
            return false;
        }

        region = new ScreenshotRegion(rect.Left, rect.Top, width, height);
        return true;
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.ReplaceLineEndings(Environment.NewLine).Trim();
        return normalized.Length <= MaxOcrCharacters ? normalized : normalized[..MaxOcrCharacters].TrimEnd() + "\n...[truncated by Threadline OCR]";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
