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
$BrowserMcpProject = Join-Path $ThreadlineRoot 'src\Threadline.BrowserMcp\Threadline.BrowserMcp.csproj'
$BrowserMcpRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\runtimes\BrowserMcp'
$JarvisConfigRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\jarvis'
$JarvisMcpConfig = Join-Path $JarvisConfigRoot 'mcp.json'

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Publish-ThreadlineMcp([string]$Project, [string]$OutputRoot, [string]$ExeName, [string]$Label) {
    Assert-Command 'dotnet'
    if (-not (Test-Path $Project)) { throw "$Label project was not found at $Project" }
    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    Write-Host "Publishing $Label into $OutputRoot"
    dotnet publish $Project `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        --output $OutputRoot
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
    $deviceExe = Publish-ThreadlineMcp $DeviceMcpProject $DeviceMcpRoot 'Threadline.DeviceMcp.exe' 'Threadline Windows Device MCP'
    $browserExe = Publish-ThreadlineMcp $BrowserMcpProject $BrowserMcpRoot 'Threadline.BrowserMcp.exe' 'Threadline Browser MCP'

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
    Write-JarvisMcpConfig $config

    Write-Host 'Registered Jarvis MCP servers: threadline-windows-device, threadline-browser'
    Write-Host "MCP config: $JarvisMcpConfig"
    Write-Host "Device MCP: $deviceExe"
    Write-Host "Browser MCP: $browserExe"
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
    if ($Headless) { & $venvPython -m pip install -e '.' }
    else { & $venvPython -m pip install -e '.[full]' }

    $jarvisExe = Join-Path $RuntimeRoot '.venv\Scripts\jarvis.exe'
    if (-not (Test-Path $jarvisExe)) { throw "PersonalJarvis installed but jarvis.exe was not created at $jarvisExe" }
}
finally { Pop-Location }

Install-ThreadlineMcpAgents

Write-Host ''
Write-Host 'PersonalJarvis runtime plus Threadline Windows and Browser MCP agents are installed.'
Write-Host "Runtime: $RuntimeRoot"
Write-Host "Commit : $PinnedCommit"
Write-Host 'Threadline expects Jarvis at http://127.0.0.1:47821/ and Threadline Service at http://127.0.0.1:5057/.'
Write-Host 'The Windows Device Host lives in the signed-in AIKA/JARVIS WinUI process; browser semantic control requires the Threadline browser extension.'

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
