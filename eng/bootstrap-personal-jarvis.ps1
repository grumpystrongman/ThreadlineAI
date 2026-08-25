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

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
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

    Write-Host ''
    Write-Host 'PersonalJarvis runtime is installed and pinned.'
    Write-Host "Runtime: $RuntimeRoot"
    Write-Host "Commit : $PinnedCommit"
    Write-Host 'Threadline expects the Jarvis server at http://127.0.0.1:47821/ by default.'

    if ($Start) {
        Write-Host 'Starting PersonalJarvis in server mode...'
        Start-Process -FilePath $jarvisExe -ArgumentList 'serve' -WorkingDirectory $RuntimeRoot
    }
}
finally {
    Pop-Location
}
