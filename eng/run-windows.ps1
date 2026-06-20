param(
  [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

if (-not $SkipBuild) {
  Write-Host 'Building Windows companion with Visual Studio MSBuild...'
  & "$PSScriptRoot\build-windows.ps1"
}
else {
  Write-Host 'Skipping build because -SkipBuild was supplied.'
}

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$releaseRoot = Join-Path $repoRoot 'src\Threadline.Windows\bin\Release'
$preferredExe = Join-Path $releaseRoot 'net8.0-windows10.0.19041.0\win-x64\Threadline.Windows.exe'
$logPath = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\logs\Threadline.Windows.log'

Write-Host "Preferred Windows companion executable: $preferredExe"
if (-not (Test-Path $preferredExe)) {
  Write-Host "Preferred executable was not found. Searching under: $releaseRoot" -ForegroundColor Yellow
  $fallback = Get-ChildItem $releaseRoot -Recurse -Filter 'Threadline.Windows.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\ref\\|\\refint\\|\\obj\\' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

  if ($null -eq $fallback) {
    throw "Windows companion executable was not found under: $releaseRoot"
  }

  $preferredExe = $fallback.FullName
}

Write-Host "Launching Threadline Windows companion: $preferredExe"
Write-Host "Startup log: $logPath"

$existing = @(Get-Process Threadline.Windows -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
  Write-Host "Stopping existing Threadline.Windows process(es): $($existing.Id -join ', ')" -ForegroundColor Yellow
  $existing | Stop-Process -Force
  Start-Sleep -Seconds 1
}

if (Test-Path $logPath) {
  Remove-Item $logPath -Force -ErrorAction SilentlyContinue
}

$workingDirectory = Split-Path -Parent $preferredExe
$process = Start-Process -FilePath $preferredExe -WorkingDirectory $workingDirectory -PassThru
Start-Sleep -Seconds 4

$running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
if ($null -eq $running -or $running.HasExited) {
  $exitCode = if ($null -ne $running) { $running.ExitCode } else { $process.ExitCode }
  Write-Host "Threadline.Windows exited immediately with code $exitCode." -ForegroundColor Yellow
  if (Test-Path $logPath) {
    Write-Host 'Last 160 lines from startup log:' -ForegroundColor Yellow
    Get-Content $logPath -Tail 160
  }
  else {
    Write-Host 'No startup log was found.' -ForegroundColor Yellow
  }
  throw 'Threadline.Windows did not stay running.'
}

Write-Host "Threadline.Windows is running. Process ID: $($process.Id)" -ForegroundColor Green
Write-Host 'If you do not see the window, check the right edge of your active display for the sidecar or edge handle.' -ForegroundColor Yellow
