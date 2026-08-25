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
$DeviceMcpProject = Join-Path $ThreadlineRoot 'src\Threadline.DeviceMcp\Threadline.DeviceMcp.csproj'
$DeviceMcpRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\DeviceMcp'
$JarvisConfigRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\jarvis'
$JarvisMcpConfig = Join-Path $JarvisConfigRoot 'mcp.json'

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Install-ThreadlineDeviceMcp {
    Assert-Command 'dotnet'
    if (-not (Test-Path $DeviceMcpProject)) {
        throw "Threadline Device MCP project was not found at $DeviceMcpProject"
    }

    New-Item -ItemType Directory -Force -Path $DeviceMcpRoot | Out-Null
    Write-Host "Publishing Threadline Windows Device MCP into $DeviceMcpRoot"
    dotnet publish $DeviceMcpProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        --output $DeviceMcpRoot

    $deviceMcpExe = Join-Path $DeviceMcpRoot 'Threadline.DeviceMcp.exe'
    if (-not (Test-Path $deviceMcpExe)) {
        throw "Device MCP publish completed but executable was not found at $deviceMcpExe"
    }

    New-Item -ItemType Directory -Force -Path $JarvisConfigRoot | Out-Null
    if (Test-Path $JarvisMcpConfig) {
        try {
            $config = Get-Content -Raw -Path $JarvisMcpConfig | ConvertFrom-Json
        }
        catch {
            throw "Existing Jarvis MCP config is not valid JSON: $JarvisMcpConfig"
        }
    }
    else {
        $config = [pscustomobject]@{ mcpServers = [pscustomobject]@{} }
    }

    if (-not $config.PSObject.Properties['mcpServers']) {
        $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([pscustomobject]@{})
    }
    if ($null -eq $config.mcpServers) {
        $config.mcpServers = [pscustomobject]@{}
    }

    $serverSpec = [pscustomobject]@{
        command = $deviceMcpExe
        args = @()
        enabled = $true
        description = 'Threadline interactive Windows Device Agent. Native UI/app control with closed-loop verification.'
    }
    $config.mcpServers | Add-Member -NotePropertyName 'threadline-windows-device' -NotePropertyValue $serverSpec -Force
    $config | ConvertTo-Json -Depth 20 | Set-Content -Path $JarvisMcpConfig -Encoding UTF8

    Write-Host "Registered Jarvis MCP server: threadline-windows-device"
    Write-Host "MCP config: $JarvisMcpConfig"
    Write-Host "Device MCP: $deviceMcpExe"
}

Assert-Command 'git'
Assert-Command $Python

$runtimeParent = Split-Path -Parent $RuntimeRoot
New-Item -ItemType Directory -Force -Path $runtimeParent | Out-Null

if (-not (Test-Path (Join-Path $RuntimeRoot '.git'))) {
    Write-Host "Cloning PersonalJarvis into $RuntimeRoot"
    git clone $Repository $RuntimeRoot
}

Push-Location $RuntimeRoot
try {
    Write-Host "Pinning PersonalJarvis to $PinnedCommit"
    git fetch --tags --prune origin
    git fetch origin $PinnedCommit
    git checkout --detach $PinnedCommit

    $venvPython = Join-Path $RuntimeRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path $venvPython)) {
        Write-Host 'Creating isolated Python environment'
        & $Python -m venv .venv
    }

    & $venvPython -m pip install --upgrade pip
    if ($Headless) {
        & $venvPython -m pip install -e '.'
    }
    else {
        & $venvPython -m pip install -e '.[full]'
    }

    $jarvisExe = Join-Path $RuntimeRoot '.venv\Scripts\jarvis.exe'
    if (-not (Test-Path $jarvisExe)) {
        throw "PersonalJarvis installed but jarvis.exe was not created at $jarvisExe"
    }
}
finally {
    Pop-Location
}

Install-ThreadlineDeviceMcp

Write-Host ''
Write-Host 'PersonalJarvis runtime and Threadline Windows Device MCP are installed.'
Write-Host "Runtime: $RuntimeRoot"
Write-Host "Commit : $PinnedCommit"
Write-Host 'Threadline expects the Jarvis server at http://127.0.0.1:47821/ by default.'
Write-Host 'AIKA/JARVIS must be running for the interactive Windows Device Host named pipe to accept device commands.'

if ($Start) {
    $jarvisExe = Join-Path $RuntimeRoot '.venv\Scripts\jarvis.exe'
    Write-Host 'Starting PersonalJarvis in server mode with Threadline MCP configuration...'
    $previousConfig = $env:JARVIS_MCP_CONFIG
    try {
        $env:JARVIS_MCP_CONFIG = $JarvisMcpConfig
        Start-Process -FilePath $jarvisExe -ArgumentList 'serve' -WorkingDirectory $RuntimeRoot
    }
    finally {
        if ($null -eq $previousConfig) {
            Remove-Item Env:JARVIS_MCP_CONFIG -ErrorAction SilentlyContinue
        }
        else {
            $env:JARVIS_MCP_CONFIG = $previousConfig
        }
    }
}
