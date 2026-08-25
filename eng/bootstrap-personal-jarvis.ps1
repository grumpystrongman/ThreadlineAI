[CmdletBinding()]
param(
    [string]$RuntimeRoot = (Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\PersonalJarvis'),
    [string]$Python = 'python',
    [switch]$Start,
    [switch]$Headless
)

$ErrorActionPreference = 'Stop'

$Repository = 'https://github.com/PersonalJarvis/PersonalJarvis.git'
$PinnedCommit = 'b85535a50a3cece8cd269325b6b3ea2cb900e43f'
$ThreadlineRoot = Split-Path -Parent $PSScriptRoot
$JarvisRiskPatch = Join-Path $ThreadlineRoot 'eng\patches\personal-jarvis-mcp-risk.patch'

$DeviceMcpProject = Join-Path $ThreadlineRoot 'src\Threadline.DeviceMcp\Threadline.DeviceMcp.csproj'
$DeviceMcpRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\DeviceMcp'
$BrowserMcpProject = Join-Path $ThreadlineRoot 'src\Threadline.BrowserMcp\Threadline.BrowserMcp.csproj'
$BrowserMcpRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\BrowserMcp'
$PrivilegedBrokerProject = Join-Path $ThreadlineRoot 'src\Threadline.PrivilegedBroker\Threadline.PrivilegedBroker.csproj'
$PrivilegedBrokerRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\PrivilegedBroker'
$PrivilegedMcpProject = Join-Path $ThreadlineRoot 'src\Threadline.PrivilegedMcp\Threadline.PrivilegedMcp.csproj'
$PrivilegedMcpRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\PrivilegedMcp'

$JarvisConfigRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\jarvis'
$JarvisMcpConfig = Join-Path $JarvisConfigRoot 'mcp.json'

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Invoke-CheckedNative([string]$Label, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Publish-ThreadlineRuntime([string]$Project, [string]$OutputRoot, [string]$ExeName, [string]$Label) {
    Assert-Command 'dotnet'
    if (-not (Test-Path $Project)) { throw "$Label project was not found at $Project" }
    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    Write-Host "Publishing $Label into $OutputRoot"
    Invoke-CheckedNative "Publishing $Label" {
        dotnet publish $Project `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=false `
            --output $OutputRoot
    }
    $exe = Join-Path $OutputRoot $ExeName
    if (-not (Test-Path $exe)) { throw "$Label publish completed but executable was not found at $exe" }
    return $exe
}

function Read-JarvisMcpConfig {
    New-Item -ItemType Directory -Force -Path $JarvisConfigRoot | Out-Null
    if (Test-Path $JarvisMcpConfig) {
        try { $config = Get-Content -Raw -Path $JarvisMcpConfig | ConvertFrom-Json }
        catch { throw "Existing Jarvis MCP config is not valid JSON: $JarvisMcpConfig" }
    }
    else { $config = [pscustomobject]@{ mcpServers = [pscustomobject]@{} } }
    if (-not $config.PSObject.Properties['mcpServers']) {
        $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([pscustomobject]@{})
    }
    if ($null -eq $config.mcpServers) { $config.mcpServers = [pscustomobject]@{} }
    return $config
}

function Write-JarvisMcpConfig($config) {
    $json = $config | ConvertTo-Json -Depth 20
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($JarvisMcpConfig, $json, $utf8NoBom)
}

function Install-ThreadlineMcpAgents {
    $deviceExe = Publish-ThreadlineRuntime $DeviceMcpProject $DeviceMcpRoot 'Threadline.DeviceMcp.exe' 'Threadline Windows Device MCP'
    $browserExe = Publish-ThreadlineRuntime $BrowserMcpProject $BrowserMcpRoot 'Threadline.BrowserMcp.exe' 'Threadline Browser MCP'
    $brokerExe = Publish-ThreadlineRuntime $PrivilegedBrokerProject $PrivilegedBrokerRoot 'Threadline.PrivilegedBroker.exe' 'Threadline Privileged Broker'
    $privilegedMcpExe = Publish-ThreadlineRuntime $PrivilegedMcpProject $PrivilegedMcpRoot 'Threadline.PrivilegedMcp.exe' 'Threadline Privileged MCP'

    $config = Read-JarvisMcpConfig
    $config.mcpServers | Add-Member -NotePropertyName 'threadline-windows-device' -NotePropertyValue ([pscustomobject]@{
        command = $deviceExe
        args = @()
        enabled = $true
        description = 'Threadline interactive Windows Device Agent. Native file/shell/UI/app control with closed-loop verification.'
    }) -Force
    $config.mcpServers | Add-Member -NotePropertyName 'threadline-browser' -NotePropertyValue ([pscustomobject]@{
        command = $browserExe
        args = @()
        enabled = $true
        description = 'Threadline browser agent. Semantic tab and DOM control through the user browser extension; no arbitrary JavaScript execution.'
    }) -Force
    $config.mcpServers | Add-Member -NotePropertyName 'threadline-privileged' -NotePropertyValue ([pscustomobject]@{
        command = $privilegedMcpExe
        args = @()
        enabled = $true
        description = 'Threadline protected Windows agent. Every advertised tool is destructiveHint=true, pauses in Jarvis for owner approval, and then crosses normal Windows UAC into an allowlisted elevated broker.'
    }) -Force
    Write-JarvisMcpConfig $config

    Write-Host 'Registered Jarvis MCP servers: threadline-windows-device, threadline-browser, threadline-privileged'
    Write-Host "MCP config: $JarvisMcpConfig"
    Write-Host "Device MCP: $deviceExe"
    Write-Host "Browser MCP: $browserExe"
    Write-Host "Privileged MCP: $privilegedMcpExe"
    Write-Host "Privileged broker: $brokerExe"
}

Assert-Command 'git'
Assert-Command $Python
if (-not (Test-Path $JarvisRiskPatch)) { throw "Jarvis MCP risk patch was not found at $JarvisRiskPatch" }

$runtimeParent = Split-Path -Parent $RuntimeRoot
New-Item -ItemType Directory -Force -Path $runtimeParent | Out-Null
if (-not (Test-Path (Join-Path $RuntimeRoot '.git'))) {
    Write-Host "Cloning PersonalJarvis into $RuntimeRoot"
    Invoke-CheckedNative 'Cloning PersonalJarvis' { git clone $Repository $RuntimeRoot }
}

Push-Location $RuntimeRoot
try {
    Write-Host "Pinning managed PersonalJarvis runtime to $PinnedCommit"
    Invoke-CheckedNative 'Fetching PersonalJarvis refs' { git fetch --tags --prune origin }
    Invoke-CheckedNative 'Fetching pinned PersonalJarvis commit' { git fetch origin $PinnedCommit }
    Invoke-CheckedNative 'Checking out pinned PersonalJarvis commit' { git checkout --detach --force $PinnedCommit }
    Invoke-CheckedNative 'Resetting managed PersonalJarvis checkout' { git reset --hard $PinnedCommit }

    Write-Host 'Applying Threadline MCP risk-tier patch to the pinned Jarvis runtime...'
    Invoke-CheckedNative 'Checking Jarvis MCP risk patch' { git apply --check $JarvisRiskPatch }
    Invoke-CheckedNative 'Applying Jarvis MCP risk patch' { git apply --whitespace=error-all $JarvisRiskPatch }
    Invoke-CheckedNative 'Validating patched Jarvis working tree' { git diff --check }

    $patchedFiles = @(git diff --name-only)
    $expectedPatchedFiles = @('jarvis/mcp/client.py', 'jarvis/mcp/loader.py')
    foreach ($expected in $expectedPatchedFiles) {
        if ($patchedFiles -notcontains $expected) {
            throw "Jarvis risk patch did not modify expected file '$expected'."
        }
    }

    $venvPython = Join-Path $RuntimeRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path $venvPython)) {
        Write-Host 'Creating isolated Python environment'
        Invoke-CheckedNative 'Creating PersonalJarvis virtual environment' { & $Python -m venv .venv }
    }

    Invoke-CheckedNative 'Upgrading pip' { & $venvPython -m pip install --upgrade pip }
    if ($Headless) { Invoke-CheckedNative 'Installing PersonalJarvis' { & $venvPython -m pip install -e '.' } }
    else { Invoke-CheckedNative 'Installing PersonalJarvis full runtime' { & $venvPython -m pip install -e '.[full]' } }

    Invoke-CheckedNative 'Compiling patched PersonalJarvis MCP modules' {
        & $venvPython -m compileall -q jarvis/mcp/client.py jarvis/mcp/loader.py
    }

    $jarvisExe = Join-Path $RuntimeRoot '.venv\Scripts\jarvis.exe'
    if (-not (Test-Path $jarvisExe)) { throw "PersonalJarvis installed but jarvis.exe was not created at $jarvisExe" }
}
finally { Pop-Location }

Install-ThreadlineMcpAgents

Write-Host ''
Write-Host 'PersonalJarvis plus Threadline Device, Browser, and Privileged agents are installed.'
Write-Host "Runtime: $RuntimeRoot"
Write-Host "Commit : $PinnedCommit + Threadline MCP risk patch"
Write-Host 'Threadline expects Jarvis at http://127.0.0.1:47821/ and Threadline Service at http://127.0.0.1:5057/.'
Write-Host 'The Windows Device Host lives in the signed-in AIKA/JARVIS WinUI process; browser semantic control requires the Threadline browser extension.'
Write-Host 'Protected operations require a Jarvis per-tool approval and then normal Windows UAC; the elevated broker has no arbitrary-shell capability.'

if ($Start) {
    $jarvisExe = Join-Path $RuntimeRoot '.venv\Scripts\jarvis.exe'
    Write-Host 'Starting PersonalJarvis in server mode with Threadline MCP configuration...'
    $previousConfig = $env:JARVIS_MCP_CONFIG
    try {
        $env:JARVIS_MCP_CONFIG = $JarvisMcpConfig
        Start-Process -FilePath $jarvisExe -ArgumentList 'serve' -WorkingDirectory $RuntimeRoot
    }
    finally {
        if ($null -eq $previousConfig) { Remove-Item Env:JARVIS_MCP_CONFIG -ErrorAction SilentlyContinue }
        else { $env:JARVIS_MCP_CONFIG = $previousConfig }
    }
}
