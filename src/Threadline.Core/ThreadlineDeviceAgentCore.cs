namespace Threadline.Core;

public enum DeviceOperationKind
{
    ObserveDesktop,
    CaptureScreen,
    InspectControls,
    ListApplications,
    LaunchApplication,
    FocusWindow,
    InvokeControl,
    SetText,
    KeyboardInput,
    MouseInput,
    ReadFile,
    WriteFile,
    RunCommand,
    AppSpecific,
    PrivilegedOperation
}

public enum DeviceRiskTier
{
    Routine,
    Consequential,
    Protected
}

public enum DeviceExecutionStatus
{
    Completed,
    Partial,
    Blocked,
    Failed
}

public enum DeviceVerificationKind
{
    None,
    ProcessRunning,
    WindowExists,
    ForegroundWindowMatches,
    AccessibleTextContains,
    FileExists
}

public sealed record DeviceTarget(
    string? Application = null,
    string? WindowTitle = null,
    string? ExecutablePath = null,
    string? AutomationId = null,
    string? ControlName = null,
    int? ProcessId = null,
    long? WindowHandle = null);

public sealed record DeviceExpectedState(
    DeviceVerificationKind Kind,
    string? Value = null,
    int TimeoutMilliseconds = 8000)
{
    public static DeviceExpectedState None { get; } = new(DeviceVerificationKind.None);
}

public sealed record DeviceCommand(
    string Id,
    DeviceOperationKind Operation,
    DeviceTarget Target,
    IReadOnlyDictionary<string, string>? Arguments = null,
    DeviceExpectedState? ExpectedState = null,
    DeviceRiskTier RiskTier = DeviceRiskTier.Routine,
    string? MissionId = null,
    string? Rationale = null)
{
    public DeviceExpectedState Verification => ExpectedState ?? DeviceExpectedState.None;
}

public sealed record DeviceWindowObservation(
    long Handle,
    string Title,
    string ProcessName,
    int ProcessId,
    string? ExecutablePath,
    bool IsForeground);

public sealed record DeviceObservation(
    DateTimeOffset CapturedAt,
    DeviceWindowObservation? ForegroundWindow,
    IReadOnlyList<DeviceWindowObservation> Windows,
    string? AccessibleText = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record DeviceScreenCaptureResult(
    bool Success,
    string? ImagePath,
    string OcrText,
    int Left,
    int Top,
    int Width,
    int Height,
    DateTimeOffset CapturedAt,
    string? Error = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record DeviceUiControlObservation(
    string Name,
    string AutomationId,
    string ControlType,
    bool IsEnabled,
    bool IsKeyboardFocusable);

public sealed record DeviceUiInspectionResult(
    bool Success,
    string WindowTitle,
    string ProcessName,
    int ProcessId,
    IReadOnlyList<DeviceUiControlObservation> Controls,
    DateTimeOffset CapturedAt,
    string? Error = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record DeviceVerificationResult(
    bool Satisfied,
    DeviceVerificationKind Kind,
    string Detail,
    DateTimeOffset CheckedAt);

public sealed record DeviceExecutionResult(
    string CommandId,
    DeviceExecutionStatus Status,
    string Message,
    DeviceObservation? Before,
    DeviceObservation? After,
    DeviceVerificationResult Verification,
    string? CapabilityUsed = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public bool Succeeded => Status == DeviceExecutionStatus.Completed && Verification.Satisfied;
}

public sealed record DeviceAuthorityGrant(
    bool Enabled,
    bool AllowRoutineOperations,
    bool AllowConsequentialOperations,
    bool AllowKeyboardAndMouse,
    bool AllowShellCommands,
    bool AllowFileWrites,
    bool AllowApplicationLaunch,
    bool AllowAppSpecificAutomation,
    bool RequireConfirmationForProtectedOperations = true)
{
    public static DeviceAuthorityGrant OwnerDefault { get; } = new(
        Enabled: true,
        AllowRoutineOperations: true,
        AllowConsequentialOperations: true,
        AllowKeyboardAndMouse: true,
        AllowShellCommands: true,
        AllowFileWrites: true,
        AllowApplicationLaunch: true,
        AllowAppSpecificAutomation: true,
        RequireConfirmationForProtectedOperations: true);
}

public sealed record DeviceAuthorityDecision(
    bool Allowed,
    bool RequiresConfirmation,
    string Reason);

public sealed class DeviceAuthorityEvaluator
{
    public DeviceAuthorityDecision Evaluate(DeviceAuthorityGrant grant, DeviceCommand command, bool confirmed = false)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(command);

        if (!grant.Enabled)
        {
            return new DeviceAuthorityDecision(false, false, "Owner authority is disabled.");
        }

        if (command.RiskTier == DeviceRiskTier.Protected)
        {
            if (grant.RequireConfirmationForProtectedOperations && !confirmed)
            {
                return new DeviceAuthorityDecision(false, true, "Protected operations require explicit confirmation even under owner authority.");
            }
        }
        else if (command.RiskTier == DeviceRiskTier.Consequential && !grant.AllowConsequentialOperations)
        {
            return new DeviceAuthorityDecision(false, true, "Consequential operations are not pre-authorized by the owner grant.");
        }
        else if (command.RiskTier == DeviceRiskTier.Routine && !grant.AllowRoutineOperations)
        {
            return new DeviceAuthorityDecision(false, false, "Routine device operations are disabled by the owner grant.");
        }

        return command.Operation switch
        {
            DeviceOperationKind.LaunchApplication when !grant.AllowApplicationLaunch =>
                new DeviceAuthorityDecision(false, true, "Application launch is not pre-authorized."),
            DeviceOperationKind.KeyboardInput or DeviceOperationKind.MouseInput when !grant.AllowKeyboardAndMouse =>
                new DeviceAuthorityDecision(false, true, "Keyboard and mouse control are not pre-authorized."),
            DeviceOperationKind.RunCommand when !grant.AllowShellCommands =>
                new DeviceAuthorityDecision(false, true, "Shell commands are not pre-authorized."),
            DeviceOperationKind.WriteFile when !grant.AllowFileWrites =>
                new DeviceAuthorityDecision(false, true, "File writes are not pre-authorized."),
            DeviceOperationKind.AppSpecific when !grant.AllowAppSpecificAutomation =>
                new DeviceAuthorityDecision(false, true, "App-specific automation is not pre-authorized."),
            _ => new DeviceAuthorityDecision(true, false, "Authorized by the active owner grant.")
        };
    }
}

public sealed record DeviceCapability(
    string Id,
    string DisplayName,
    IReadOnlySet<DeviceOperationKind> Operations,
    int Priority,
    bool RequiresInteractiveDesktop,
    string Description);

public sealed class DeviceCapabilitySelector
{
    public DeviceCapability? Select(IReadOnlyList<DeviceCapability> capabilities, DeviceOperationKind operation)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities
            .Where(capability => capability.Operations.Contains(operation))
            .OrderByDescending(capability => capability.Priority)
            .FirstOrDefault();
    }
}
