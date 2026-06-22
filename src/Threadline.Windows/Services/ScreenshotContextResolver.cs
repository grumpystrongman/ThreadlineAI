namespace Threadline.Windows.Services;

public sealed class ScreenshotContextResolver
{
    public SummarizedContext ResolveFallback(ThreadlineTarget target, ProcessIntelligence processIntelligence, IReadOnlyList<string> priorAttempts)
    {
        var details = new List<string>
        {
            "Resolver route: screenshot/OCR fallback",
            "Provider selected: screenshot-resolver",
            "Capture method used: Screenshot/OCR fallback",
            "Chars extracted: 0",
            "Images found: 0",
            "Summary size: 0",
            "Confidence: Low",
            $"Target: {target}",
            processIntelligence.ToDisplayText()
        };
        details.AddRange(priorAttempts.Select(attempt => "Prior attempt: " + attempt));

        var receipt = new ContextReceipt(
            "screenshot/OCR/vision fallback",
            ContextConfidence.Low,
            ContextCaptureKind.TitleOnly,
            [
                $"Window title: {target.Window.WindowTitle ?? target.Title}",
                $"Process: {target.Window.ProcessName ?? processIntelligence.ProcessName}",
                "No OCR text was extracted."
            ],
            [
                "Screenshot pixels were not captured by this resolver result.",
                "OCR text was not captured.",
                "Image extraction and layout analysis were not captured.",
                "Page/body content was not captured."
            ],
            true,
            "I only have the window title. I do not have the page/body content.",
            priorAttempts.Select(attempt => new ContextProviderAttempt("Prior capture attempt", ContextProviderAttemptStatus.Missed, attempt)).ToList());

        return new SummarizedContext(
            target.Title,
            "screenshot-resolver",
            "Threadline reached the screenshot fallback, but OCR and image extraction are not configured in this build. Based on visible window metadata only, Threadline cannot safely claim document body text yet.",
            details,
            ["Screenshot capture is the fallback path only. OCR, image extraction, and layout analysis still need the vision/OCR engine integration."],
            target.Window.ToDisplayText(),
            ContextConfidence.Low,
            processIntelligence,
            null,
            receipt);
    }
}
