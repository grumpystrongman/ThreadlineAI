namespace Threadline.Windows.Services;

public sealed class ScreenshotContextResolver
{
    public SummarizedContext ResolveFallback(ThreadlineTarget target, ProcessIntelligenceSnapshot processIntelligence, IReadOnlyList<string> priorAttempts)
    {
        var details = new List<string>
        {
            "Resolver route: screenshot/OCR fallback",
            "Provider selected: screenshot-resolver",
            "Capture method used: Screenshot/OCR fallback",
            "Chars extracted: 0",
            "Images found: 0",
            "Summary size: 0",
            "Confidence: none",
            $"Target: {target}",
            processIntelligence.ToDisplayText()
        };
        details.AddRange(priorAttempts.Select(attempt => "Prior attempt: " + attempt));

        return new SummarizedContext(
            target.Title,
            "screenshot-resolver",
            "Threadline reached the screenshot fallback, but OCR/image extraction is not configured in this build. Based on visible content, Threadline cannot safely claim document body text yet.",
            details,
            ["Screenshot capture is the fallback path only. OCR, image extraction, and layout analysis still need the vision/OCR engine integration."],
            target.Window.ToDisplayText(),
            ContextConfidence.None,
            "screenshot-fallback",
            processIntelligence);
    }
}
