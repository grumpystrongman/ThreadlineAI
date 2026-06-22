namespace Threadline.Windows.Services;

public sealed class ActiveWindowContentResolver
{
    private readonly BrowserExtensionContextProvider _browserProvider = new();
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly ContextSummarizer _contextSummarizer = new();
    private readonly FileBackedTextResolver _fileResolver = new();
    private readonly ProcessIntelligenceService _processIntelligence = new();
    private readonly ScreenshotVisionContextProvider _screenshotVisionProvider = new();

    public async Task<SummarizedContext> ResolveAsync(string sessionId, ThreadlineTarget target, CancellationToken cancellationToken = default) =>
        await ResolveAsync(sessionId, target, ContextCaptureConsent.None, cancellationToken);

    public async Task<SummarizedContext> ResolveAsync(string sessionId, ThreadlineTarget target, ContextCaptureConsent consent, CancellationToken cancellationToken = default)
    {
        var attempts = new List<ContextProviderAttempt>();
        var process = _processIntelligence.Inspect(target);

        if (ShouldAttemptBrowserProvider(target, process))
        {
            try
            {
                var browser = await _browserProvider.TryGetLatestAsync(sessionId, target, cancellationToken);
                if (browser is not null)
                {
                    attempts.Add(new ContextProviderAttempt("Browser extension provider", ContextProviderAttemptStatus.Captured, "Used latest browser extension page/selection context event for this session."));
                    return FinalizeContext(AddRoute(browser, target, process, "browser-extension", "browser extension provider", browser.Confidence), target, process, "browser-extension", attempts);
                }

                attempts.Add(new ContextProviderAttempt("Browser extension provider", ContextProviderAttemptStatus.Missed, "No browser page or selected-text event is available in this session yet."));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                attempts.Add(new ContextProviderAttempt("Browser extension provider", ContextProviderAttemptStatus.Failed, $"Browser provider could not be queried: {ex.Message}"));
            }
        }
        else
        {
            attempts.Add(new ContextProviderAttempt("Browser extension provider", ContextProviderAttemptStatus.Skipped, "Target is not a browser tab/window."));
        }

        var fileBacked = TryResolveFileBacked(target, process, attempts);
        if (fileBacked is not null)
        {
            return FinalizeContext(fileBacked, target, process, "file-document-provider", attempts);
        }

        if (CanUseUiAutomation(target, process))
        {
            var nativeResult = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
            if (nativeResult.Success && !string.IsNullOrWhiteSpace(nativeResult.Content))
            {
                attempts.Add(new ContextProviderAttempt("UI Automation provider", ContextProviderAttemptStatus.Captured, $"Captured {nativeResult.Content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} characters through Windows accessibility."));
                var nativeSummary = _contextSummarizer.SummarizeNativeUi(nativeResult);
                var receipt = BuildUiAutomationReceipt(target, process, nativeResult, nativeSummary);
                var nativeContext = AddRoute(
                    nativeSummary with { Receipt = receipt },
                    target,
                    process,
                    "ui-automation",
                    "ui automation provider",
                    nativeSummary.Confidence);

                return FinalizeContext(nativeContext, target, process, "ui-automation", attempts);
            }

            attempts.Add(new ContextProviderAttempt("UI Automation provider", ContextProviderAttemptStatus.Missed, string.Join(" ", nativeResult.Warnings)));
        }
        else
        {
            attempts.Add(new ContextProviderAttempt("UI Automation provider", ContextProviderAttemptStatus.Skipped, UiAutomationSkipReason(target, process)));
        }

        if (consent.ClipboardSelectionAllowed)
        {
            attempts.Add(new ContextProviderAttempt("Clipboard/selection provider", ContextProviderAttemptStatus.Missed, "Explicit consent was supplied, but no safe clipboard/selection adapter is wired in this build."));
        }
        else
        {
            attempts.Add(new ContextProviderAttempt("Clipboard/selection provider", ContextProviderAttemptStatus.Blocked, "Not attempted. Clipboard and selected text require explicit user approval for this capture."));
        }

        if (consent.CanUseScreenshotVision)
        {
            try
            {
                var screenshotVision = await _screenshotVisionProvider.CaptureAsync(target, consent, cancellationToken);
                if (screenshotVision.Success)
                {
                    attempts.Add(new ContextProviderAttempt("Screenshot/OCR/vision provider", ContextProviderAttemptStatus.Captured, $"Captured approved window-region screenshot, extracted {screenshotVision.RedactedOcrText.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} redacted OCR characters, and discarded raw image bytes."));
                    return FinalizeContext(BuildScreenshotVisionContext(target, process, screenshotVision), target, process, "screenshot-ocr-vision", attempts);
                }

                attempts.Add(new ContextProviderAttempt("Screenshot/OCR/vision provider", ContextProviderAttemptStatus.Failed, string.Join(" ", screenshotVision.Warnings)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException or UnauthorizedAccessException or TaskCanceledException)
            {
                attempts.Add(new ContextProviderAttempt("Screenshot/OCR/vision provider", ContextProviderAttemptStatus.Failed, $"Approved screenshot/OCR/vision capture failed safely: {ex.Message}"));
            }
        }
        else
        {
            attempts.Add(new ContextProviderAttempt("Screenshot/OCR/vision provider", ContextProviderAttemptStatus.Blocked, $"Not attempted. {consent.ScreenshotVisionConsentReason}"));
        }

        attempts.Add(new ContextProviderAttempt("Title/process fallback", ContextProviderAttemptStatus.Captured, "Only window title and process metadata are available."));
        return FinalizeContext(TitleProcessFallbackContext(target, process), target, process, "title-process-fallback", attempts);
    }

    private SummarizedContext? TryResolveFileBacked(ThreadlineTarget target, ProcessIntelligence process, IList<ContextProviderAttempt> attempts)
    {
        var result = _fileResolver.TryResolve(target.Title);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
        {
            var detail = result.Warnings.Count == 0
                ? "No unique readable file was resolved from the target title."
                : string.Join(" ", result.Warnings);
            attempts.Add(new ContextProviderAttempt("File/document provider", ContextProviderAttemptStatus.Missed, detail));
            return null;
        }

        attempts.Add(new ContextProviderAttempt("File/document provider", ContextProviderAttemptStatus.Captured, $"Resolved exact local file match: {result.Path}"));

        var source = target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase)
            ? "notepad-file-backed"
            : "file-backed-document";
        var summary = _contextSummarizer.SummarizePlainText(target.Title, source, result.Text);
        var warnings = result.Warnings.ToList();
        var details = new List<string>
        {
            "Resolver route: file/document provider",
            $"Provider selected: {source}",
            "Capture kind: FileBacked",
            "Confidence: High",
            $"Target: {target}",
            $"File path: {result.Path}"
        };
        details.AddRange(summary.KeyDetails);

        var receipt = new ContextReceipt(
            "file/document provider",
            ContextConfidence.High,
            ContextCaptureKind.FileBacked,
            [
                $"File path: {result.Path}",
                $"File-backed text characters: {result.Text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "Document text was read from a unique local file match."
            ],
            [
                "Live editor selection was not captured.",
                "Unsaved in-memory changes were not captured unless they already exist in the saved file."
            ],
            false,
            "Threadline captured file-backed document text from a unique local file match.",
            []);

        return new SummarizedContext(
            summary.Title,
            source,
            summary.Summary,
            details,
            warnings,
            summary.RawPreview,
            ContextConfidence.High,
            process,
            null,
            receipt);
    }

    private static SummarizedContext BuildScreenshotVisionContext(ThreadlineTarget target, ProcessIntelligence process, ScreenshotVisionCaptureResult result)
    {
        var redactedLines = result.RedactedOcrText
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var details = new List<string>
        {
            "Resolver route: screenshot/OCR/vision provider",
            "Provider selected: screenshot-ocr-vision",
            "Capture kind: ScreenshotVision",
            $"Confidence: {result.Confidence}",
            $"Target: {target}",
            $"Window region: {result.Region.ToDisplayText()}",
            $"Redacted OCR characters: {result.RedactedOcrText.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"Redaction count: {result.RedactionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "Raw screenshot stored: No"
        };
        details.AddRange(redactedLines);

        var captured = new List<string>
        {
            $"User-approved screenshot region tied to the attached window: {result.Region.ToDisplayText()}",
            $"OCR text characters after redaction: {result.RedactedOcrText.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"Vision summary characters: {result.VisionSummary.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "Raw screenshot bytes were discarded after OCR.",
            "Only redacted OCR text and a text vision summary are eligible for prompt/provider handoff in this build."
        };

        if (result.RedactionCount > 0)
        {
            captured.Add($"Redaction applied before provider handoff: {result.RedactionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} replacement(s) across {string.Join(", ", result.RedactionCategories)}.");
        }

        var notCaptured = new List<string>
        {
            "No silent screenshot capture was performed.",
            "Raw screenshots were not stored.",
            "Raw screenshots were not sent to the provider as image payloads in this build.",
            "Offscreen document/page content outside the visible window region was not captured.",
            "Clipboard and selected text were not captured by this fallback."
        };

        foreach (var warning in result.Warnings)
        {
            notCaptured.Add($"Screenshot/OCR warning: {warning}");
        }

        var receipt = new ContextReceipt(
            "screenshot/ocr/vision provider",
            result.Confidence,
            ContextCaptureKind.ScreenshotVision,
            captured,
            notCaptured,
            false,
            "Threadline used an explicitly approved screenshot/OCR fallback for the attached window. Raw screenshots were not stored; redacted OCR/summary text may be used.",
            []);

        var rawPreview = string.IsNullOrWhiteSpace(result.RedactedOcrText)
            ? result.VisionSummary
            : result.RedactedOcrText;

        return new SummarizedContext(
            target.Title,
            "screenshot-ocr-vision",
            result.VisionSummary,
            details,
            result.Warnings,
            rawPreview,
            result.Confidence,
            process,
            null,
            receipt);
    }

    private static ContextReceipt BuildUiAutomationReceipt(ThreadlineTarget target, ProcessIntelligence process, NativeUiAutomationResult nativeResult, SummarizedContext nativeSummary)
    {
        var missing = IsMissingRealWorkingContent(target, process, ContextCaptureKind.UiAutomation);
        var notCaptured = new List<string>
        {
            "File-backed source was not captured.",
            "Clipboard or explicit selected text was not captured.",
            "Screenshot, OCR, and visual layout were not captured."
        };

        if (process.AppType == ActiveAppType.Browser)
        {
            notCaptured.Add("Browser page/body content was not captured. UI Automation is not treated as page text.");
        }

        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase))
        {
            notCaptured.Add("Active Notepad tab body was not captured through a tab-safe provider.");
        }

        foreach (var warning in nativeResult.Warnings)
        {
            notCaptured.Add($"UI Automation warning: {warning}");
        }

        return new ContextReceipt(
            "ui-automation",
            nativeSummary.Confidence,
            ContextCaptureKind.UiAutomation,
            [
                "Windows UI Automation/accessibility text.",
                $"Captured characters: {nativeResult.Content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"Process: {nativeResult.ProcessName}",
                $"Window title: {nativeResult.WindowTitle}"
            ],
            notCaptured,
            missing,
            missing
                ? "Threadline has UI Automation text, but it may not be the real working page/document body."
                : "Threadline captured readable text through Windows UI Automation.",
            []);
    }

    private static SummarizedContext TitleProcessFallbackContext(ThreadlineTarget target, ProcessIntelligence process)
    {
        var title = target.Window.WindowTitle ?? target.Title;
        var processName = target.Window.ProcessName ?? process.ProcessName;
        var receipt = new ContextReceipt(
            "title/process fallback",
            ContextConfidence.Low,
            ContextCaptureKind.TitleOnly,
            [
                $"Window title: {title}",
                $"Process: {processName}",
                $"Target kind: {target.Kind}",
                $"Provider key: {target.ProviderKey}"
            ],
            [
                "Page/body content was not captured.",
                "Selected text was not captured.",
                "File-backed document text was not captured.",
                "UI Automation body text was not captured.",
                "Screenshot, OCR, and visual layout were not captured."
            ],
            true,
            "I only have the window title. I do not have the page/body content.",
            []);

        return new SummarizedContext(
            target.Title,
            "title-process-fallback",
            "I only have the window title. I do not have the page/body content.",
            [
                target.ToString(),
                "Resolver route: title/process fallback",
                "Capture kind: TitleOnly",
                "Confidence: Low"
            ],
            ["Threadline is missing the real working content for this target."],
            target.Window.ToDisplayText(),
            ContextConfidence.Low,
            process,
            null,
            receipt);
    }

    private static SummarizedContext AddRoute(SummarizedContext context, ThreadlineTarget target, ProcessIntelligence process, string provider, string route, ContextConfidence confidence)
    {
        var details = new List<string> { $"Resolver route: {route}", $"Provider selected: {provider}", $"Confidence: {confidence}", $"Target: {target}" };
        details.AddRange(context.KeyDetails);
        return new SummarizedContext(context.Title, context.Source, context.Summary, details, context.Warnings, context.RawPreview, confidence, process, null, context.Receipt);
    }

    private static SummarizedContext FinalizeContext(SummarizedContext context, ThreadlineTarget target, ProcessIntelligence process, string method, IReadOnlyList<ContextProviderAttempt> attempts)
    {
        var receipt = context.Receipt ?? BuildImplicitReceipt(context);
        if (IsMissingRealWorkingContent(target, process, receipt.CaptureKind))
        {
            receipt = receipt with
            {
                MissingRealWorkingContent = true,
                NotCaptured = EnsureMissingRealContent(receipt.NotCaptured),
                UserMessage = receipt.IsTitleOnly
                    ? "I only have the window title. I do not have the page/body content."
                    : receipt.UserMessage
            };
        }

        receipt = receipt with { ProviderAttempts = attempts.ToList() };
        return WithDiagnostics(context with { Receipt = receipt, Confidence = receipt.Confidence }, target, process, method);
    }

    private static ContextReceipt BuildImplicitReceipt(SummarizedContext context) =>
        new(
            context.Source,
            context.Confidence,
            ContextCaptureKind.None,
            string.IsNullOrWhiteSpace(context.RawPreview) ? [] : [$"Raw preview characters: {context.RawPreview.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}"],
            ["No explicit context receipt was supplied by this provider."],
            context.Confidence is ContextConfidence.None or ContextConfidence.Low,
            "Threadline has a legacy context result without a full receipt.",
            []);

    private static SummarizedContext WithDiagnostics(SummarizedContext context, ThreadlineTarget target, ProcessIntelligence process, string method)
    {
        var details = new List<string>(context.KeyDetails);
        if (context.Receipt is not null)
        {
            details.Add("Context receipt:");
            details.AddRange(context.Receipt.ToDisplayText().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var diagnostics = new CaptureDiagnostics(
            target.Window.WindowTitle ?? target.Title,
            target.Window.ProcessName ?? "Unknown",
            target.Window.ProcessId,
            method,
            string.IsNullOrWhiteSpace(context.RawPreview) ? 0 : context.RawPreview.Length,
            context.Receipt?.IsScreenshotVision == true ? 1 : 0,
            context.Summary.Length,
            context.Confidence,
            details);

        return context with { Process = process, Diagnostics = diagnostics };
    }

    private static bool ShouldAttemptBrowserProvider(ThreadlineTarget target, ProcessIntelligence process) =>
        target.Kind == ThreadlineTargetKind.BrowserTab ||
        target.ProviderKey.Equals("browser-extension", StringComparison.OrdinalIgnoreCase) ||
        process.AppType == ActiveAppType.Browser;

    private static bool CanUseUiAutomation(ThreadlineTarget target, ProcessIntelligence process)
    {
        if (!target.CanReadBody) return false;
        if (process.AppType == ActiveAppType.Browser) return false;
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string UiAutomationSkipReason(ThreadlineTarget target, ProcessIntelligence process)
    {
        if (!target.CanReadBody) return "Target metadata says body text is not readable.";
        if (process.AppType == ActiveAppType.Browser) return "Skipped for browser targets because UI Automation usually exposes browser chrome, not page/body content.";
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase) || string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase)) return "Skipped for Notepad because modern tabbed Notepad can expose the wrong tab body through UI Automation.";
        return "Skipped because no safe UI Automation route was available.";
    }

    private static bool IsMissingRealWorkingContent(ThreadlineTarget target, ProcessIntelligence process, ContextCaptureKind captureKind)
    {
        if (captureKind == ContextCaptureKind.TitleOnly || captureKind == ContextCaptureKind.None) return true;
        if (process.AppType == ActiveAppType.Browser && captureKind is not (ContextCaptureKind.PageText or ContextCaptureKind.SelectedText or ContextCaptureKind.ScreenshotVision or ContextCaptureKind.Ocr)) return true;
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase) && captureKind is not (ContextCaptureKind.FileBacked or ContextCaptureKind.ScreenshotVision or ContextCaptureKind.Ocr)) return true;
        return false;
    }

    private static IReadOnlyList<string> EnsureMissingRealContent(IReadOnlyList<string> existing)
    {
        var values = existing.ToList();
        AddIfMissing(values, "Real working page/document body content is missing.");
        return values;
    }

    private static void AddIfMissing(ICollection<string> values, string item)
    {
        if (!values.Any(value => value.Equals(item, StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(item);
        }
    }
}
