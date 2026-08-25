using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class DeviceAgentCoreTests
{
    [Fact]
    public void OwnerDefault_AllowsRoutineApplicationLaunch()
    {
        var evaluator = new DeviceAuthorityEvaluator();
        var command = new DeviceCommand(
            "launch-excel",
            DeviceOperationKind.LaunchApplication,
            new DeviceTarget(Application: "Excel"));

        var decision = evaluator.Evaluate(DeviceAuthorityGrant.OwnerDefault, command);

        Assert.True(decision.Allowed);
        Assert.False(decision.RequiresConfirmation);
    }

    [Fact]
    public void ProtectedOperation_StillRequiresExplicitConfirmation()
    {
        var evaluator = new DeviceAuthorityEvaluator();
        var command = new DeviceCommand(
            "protected",
            DeviceOperationKind.PrivilegedOperation,
            new DeviceTarget(),
            RiskTier: DeviceRiskTier.Protected);

        var first = evaluator.Evaluate(DeviceAuthorityGrant.OwnerDefault, command);
        var confirmed = evaluator.Evaluate(DeviceAuthorityGrant.OwnerDefault, command, confirmed: true);

        Assert.False(first.Allowed);
        Assert.True(first.RequiresConfirmation);
        Assert.True(confirmed.Allowed);
    }

    [Fact]
    public void KeyboardInput_CanBeDisabledIndependently()
    {
        var evaluator = new DeviceAuthorityEvaluator();
        var grant = DeviceAuthorityGrant.OwnerDefault with { AllowKeyboardAndMouse = false };
        var command = new DeviceCommand(
            "type",
            DeviceOperationKind.KeyboardInput,
            new DeviceTarget(Application: "Notepad"));

        var decision = evaluator.Evaluate(grant, command);

        Assert.False(decision.Allowed);
        Assert.True(decision.RequiresConfirmation);
    }

    [Fact]
    public void CapabilitySelector_PrefersNativeOrAppSpecificCapability()
    {
        var selector = new DeviceCapabilitySelector();
        var operation = DeviceOperationKind.SetText;
        var capabilities = new[]
        {
            new DeviceCapability(
                "vision-input",
                "Vision + input fallback",
                new HashSet<DeviceOperationKind> { operation },
                Priority: 10,
                RequiresInteractiveDesktop: true,
                "Last-resort coordinate automation."),
            new DeviceCapability(
                "native-uia",
                "Windows UI Automation",
                new HashSet<DeviceOperationKind> { operation },
                Priority: 50,
                RequiresInteractiveDesktop: true,
                "Native accessibility control."),
            new DeviceCapability(
                "excel-com",
                "Excel COM",
                new HashSet<DeviceOperationKind> { operation },
                Priority: 100,
                RequiresInteractiveDesktop: false,
                "Excel object-model control.")
        };

        var selected = selector.Select(capabilities, operation);

        Assert.NotNull(selected);
        Assert.Equal("excel-com", selected!.Id);
    }
}
