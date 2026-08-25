[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$deviceMcpDll = Join-Path $repoRoot 'src\Threadline.DeviceMcp\bin\Release\net8.0-windows10.0.19041.0\Threadline.DeviceMcp.dll'
$marker = "Threadline Device Agent smoke $([DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))"

function Invoke-DeviceMcpTool {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [hashtable]$Arguments = @{}
    )

    if (-not (Test-Path $deviceMcpDll)) {
        throw "Device MCP build output was not found: $deviceMcpDll"
    }

    $initialize = @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{ name = 'threadline-device-smoke'; version = '1.0' }
        }
    } | ConvertTo-Json -Compress -Depth 20

    $initialized = @{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
        params = @{}
    } | ConvertTo-Json -Compress -Depth 20

    $call = @{
        jsonrpc = '2.0'
        id = 2
        method = 'tools/call'
        params = @{
            name = $Name
            arguments = $Arguments
        }
    } | ConvertTo-Json -Compress -Depth 20

    $output = @(@($initialize, $initialized, $call) | & dotnet $deviceMcpDll)
    if ($LASTEXITCODE -ne 0) {
        throw "Device MCP exited with code $LASTEXITCODE while calling '$Name'."
    }

    $response = $output |
        ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.id -eq 2 } |
        Select-Object -Last 1

    if ($null -eq $response) {
        throw "Device MCP returned no tools/call response for '$Name'."
    }

    if ($response.error) {
        throw "Device MCP JSON-RPC error for '$Name': $($response.error.message)"
    }

    $content = @($response.result.content) | Where-Object { $_.type -eq 'text' } | Select-Object -First 1
    if ($null -eq $content -or [string]::IsNullOrWhiteSpace($content.text)) {
        throw "Device MCP returned no text result for '$Name'."
    }

    $inner = $content.text | ConvertFrom-Json
    if ($response.result.isError -eq $true -or $inner.ok -ne $true) {
        $reason = if ($inner.error) { $inner.error } else { $content.text }
        throw "Device tool '$Name' failed: $reason"
    }

    return $inner.result
}

function Wait-ForDeviceHost {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $observation = Invoke-DeviceMcpTool -Name 'observe_desktop'
            if ($null -ne $observation) {
                return
            }
        }
        catch {
            if ($attempt -eq 30) { throw }
        }
        Start-Sleep -Milliseconds 350
    }
}

Write-Host 'AIKA / JARVIS Windows Device Agent smoke test' -ForegroundColor Cyan
Write-Host 'This test opens Notepad, inspects native controls, types a temporary marker, verifies it, and attempts to close without saving.'

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-windows.ps1')
}

if (-not $SkipLaunch) {
    & (Join-Path $PSScriptRoot 'run-windows.ps1') -SkipBuild
}

Wait-ForDeviceHost
Write-Host 'Device Host: ready' -ForegroundColor Green

$capabilities = Invoke-DeviceMcpTool -Name 'list_device_capabilities'
$capabilityIds = @($capabilities | ForEach-Object { $_.id })
foreach ($requiredCapability in @('windows.process-window','windows.uia','windows.uia-inspection','windows.screen-ocr','windows.input')) {
    if ($capabilityIds -notcontains $requiredCapability) {
        throw "Device Host did not advertise required capability '$requiredCapability'."
    }
}
Write-Host "Capabilities: $($capabilityIds -join ', ')" -ForegroundColor Green

$launch = Invoke-DeviceMcpTool -Name 'open_application' -Arguments @{
    application = 'notepad'
    expected_window_title = 'Notepad'
}
if ($launch.status -ne 'completed' -or $launch.verification.satisfied -ne $true) {
    throw "Notepad launch was not verified. Status: $($launch.status); $($launch.verification.detail)"
}
Write-Host 'Launch: Notepad verified' -ForegroundColor Green

$focus = Invoke-DeviceMcpTool -Name 'focus_window' -Arguments @{ application = 'notepad' }
if ($focus.status -ne 'completed' -or $focus.verification.satisfied -ne $true) {
    throw "Notepad focus was not verified. Status: $($focus.status); $($focus.verification.detail)"
}
Write-Host 'Focus: verified' -ForegroundColor Green

$inspection = Invoke-DeviceMcpTool -Name 'inspect_controls' -Arguments @{ application = 'notepad' }
if ($inspection.success -ne $true -or @($inspection.controls).Count -eq 0) {
    throw "Notepad UI Automation inspection returned no controls: $($inspection.error)"
}
Write-Host "UIA inspection: $(@($inspection.controls).Count) controls" -ForegroundColor Green

$typed = Invoke-DeviceMcpTool -Name 'send_keys' -Arguments @{
    application = 'notepad'
    text = $marker
    expected_text = $marker
}

$verified = $typed.status -eq 'completed' -and $typed.verification.satisfied -eq $true
if (-not $verified) {
    Write-Host 'Native accessibility did not verify the typed marker; checking screenshot/OCR fallback...' -ForegroundColor Yellow
    $capture = Invoke-DeviceMcpTool -Name 'capture_window' -Arguments @{ application = 'notepad' }
    if ($capture.success -ne $true -or -not ($capture.ocrText -like "*$marker*")) {
        throw "Typed text could not be verified by accessibility or OCR. Accessibility: $($typed.verification.detail); OCR error: $($capture.error)"
    }
    Write-Host 'Typed marker verified by OCR fallback.' -ForegroundColor Green
}
else {
    Write-Host 'Typed marker verified by native accessibility.' -ForegroundColor Green
}

# Cleanup is best-effort. Closing a dirty Notepad may open a Save Changes dialog;
# inspect the foreground dialog and invoke an exact native button when one is exposed.
try {
    Invoke-DeviceMcpTool -Name 'send_keys' -Arguments @{ application = 'notepad'; hotkey = 'alt+f4' } | Out-Null
    Start-Sleep -Milliseconds 500
    $dialog = Invoke-DeviceMcpTool -Name 'inspect_controls'
    $discardButton = @($dialog.controls) |
        Where-Object {
            $_.controlType -eq 'Button' -and
            ($_.name -match "(?i)don't save|don.t save|discard|no")
        } |
        Select-Object -First 1

    if ($null -ne $discardButton -and -not [string]::IsNullOrWhiteSpace($discardButton.name)) {
        $invokeArgs = @{
            process_id = [int]$dialog.processId
            control_name = [string]$discardButton.name
        }
        if (-not [string]::IsNullOrWhiteSpace($dialog.windowTitle)) {
            $invokeArgs.window_title = [string]$dialog.windowTitle
        }
        Invoke-DeviceMcpTool -Name 'invoke_control' -Arguments $invokeArgs | Out-Null
        Write-Host "Cleanup: invoked '$($discardButton.name)' on the Notepad close dialog." -ForegroundColor Green
    }
    else {
        Write-Host 'Cleanup: Notepad may remain open because no unambiguous discard button was exposed. No file was saved by the smoke test.' -ForegroundColor Yellow
    }
}
catch {
    Write-Host "Cleanup warning: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Windows Device Agent smoke test PASSED.' -ForegroundColor Green
Write-Host "Verified marker: $marker"
