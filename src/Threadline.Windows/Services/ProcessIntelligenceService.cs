using System.Diagnostics;

namespace Threadline.Windows.Services;

public sealed class ProcessIntelligenceService
{
    public ProcessIntelligence Inspect(ThreadlineTarget target)
    {
        var window = target.Window;
        var processName = window.ProcessName ?? "Unknown";
        var process = TryGetProcess(window.ProcessId);
        var parent = TryGetParentProcess(process);
        var appType = Classify(processName, window.WindowTitle ?? target.Title);
        var methods = BuildCaptureMethods(target, appType);

        return new ProcessIntelligence(
            window.WindowTitle ?? target.Title,
            window.Handle,
            window.ProcessId,
            processName,
            window.ExecutablePath,
            parent?.ProcessName,
            parent?.Id,
            appType,
            methods);
    }

    private static IReadOnlyList<CaptureMethodAvailability> BuildCaptureMethods(ThreadlineTarget target, ActiveAppType appType)
    {
        var methods = new List<CaptureMethodAvailability>();

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.Provider,
            ProviderDisplayName(target.ProviderKey),
            target.Kind == ThreadlineTargetKind.BrowserTab || target.ProviderKey.Equals("browser-extension", StringComparison.OrdinalIgnoreCase),
            ProviderNotes(target)));

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.UiAutomation,
            "UI Automation",
            target.CanReadBody || appType is ActiveAppType.OfficeDocument or ActiveAppType.MailClient or ActiveAppType.PdfViewer or ActiveAppType.AnalyticsTool or ActiveAppType.DesktopApp,
            "Windows accessibility text can be attempted, but confidence depends on what the app exposes."));

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.FileResolver,
            "Document/File Resolver",
            target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase) || appType == ActiveAppType.TextEditor,
            "Available when the visible document title can be mapped to a unique local file."));

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.Screenshot,
            "Screenshot",
            true,
            "Fallback only. Used when provider, UI Automation, and file resolution do not produce reliable text."));

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.Ocr,
            "OCR",
            false,
            "Planned fallback after screenshot capture; not treated as implemented in this build."));

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.ImageExtraction,
            "Image Extraction",
            false,
            "Planned for visual documents and dashboards."));

        methods.Add(new CaptureMethodAvailability(
            CaptureMethodKind.LayoutAnalysis,
            "Layout Analysis",
            false,
            "Planned for screenshots, PDFs, and dashboard-like apps."));

        return methods;
    }

    private static string ProviderDisplayName(string providerKey) => providerKey switch
    {
        "browser-extension" => "Browser Provider",
        "notepad-tabs" => "Notepad Provider",
        "native-ui" => "Native Window Provider",
        _ => providerKey
    };

    private static string ProviderNotes(ThreadlineTarget target)
    {
        if (target.Kind == ThreadlineTargetKind.BrowserTab) return "Preferred for browser page/tab context.";
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase)) return "Can identify tabs; body text still needs file or active-document resolution.";
        return "No app-specific provider is registered for this target yet.";
    }

    private static ActiveAppType Classify(string processName, string title)
    {
        if (IsAny(processName, "chrome", "msedge", "firefox")) return ActiveAppType.Browser;
        if (IsAny(processName, "notepad", "code")) return ActiveAppType.TextEditor;
        if (IsAny(processName, "winword", "excel", "powerpnt") || title.Contains("Word", StringComparison.OrdinalIgnoreCase)) return ActiveAppType.OfficeDocument;
        if (IsAny(processName, "outlook", "olk")) return ActiveAppType.MailClient;
        if (title.Contains(".pdf", StringComparison.OrdinalIgnoreCase) || processName.Contains("acrobat", StringComparison.OrdinalIgnoreCase)) return ActiveAppType.PdfViewer;
        if (processName.Contains("powerbi", StringComparison.OrdinalIgnoreCase) || title.Contains("Power BI", StringComparison.OrdinalIgnoreCase)) return ActiveAppType.AnalyticsTool;
        if (IsAny(processName, "windowsterminal", "powershell", "pwsh", "cmd")) return ActiveAppType.Terminal;
        return ActiveAppType.DesktopApp;
    }

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private static Process? TryGetProcess(int? processId)
    {
        if (processId is null or <= 0) return null;
        try { return Process.GetProcessById(processId.Value); } catch { return null; }
    }

    private static Process? TryGetParentProcess(Process? process)
    {
        if (process is null) return null;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {process.Id}");
            foreach (var item in searcher.Get())
            {
                var parentId = Convert.ToInt32(item["ParentProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                return TryGetProcess(parentId);
            }
        }
        catch
        {
        }

        return null;
    }
}
