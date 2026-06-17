$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

Write-Host 'Building Windows companion with Visual Studio MSBuild...'
& "$PSScriptRoot\build-windows.ps1"

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$releaseRoot = Join-Path $repoRoot 'src\Threadline.Windows\bin\Release'
$logPath = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\logs\Threadline.Windows.log'

Write-Host "Searching for Threadline.Windows.exe under: $releaseRoot"
$candidates = @(Get-ChildItem $releaseRoot -Recurse -Filter 'Threadline.Windows.exe' -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\ref\\|\\refint\\|\\obj\\' } |
  Select-Object *, @{ Name = 'IsWinX64'; Expression = { $_.FullName -match '\\win-x64\\' } } |
  Sort-Object @{ Expression = 'IsWinX64'; Descending = $true }, @{ Expression = 'LastWriteTime'; Descending = $true })

if ($candidates.Count -eq 0) {
  throw "Windows companion executable was not found under: $releaseRoot"
}

Write-Host 'Found executable candidates:'
foreach ($candidate in $candidates) {
  $label = if ($candidate.IsWinX64) { 'preferred' } else { 'fallback' }
  Write-Host " - [$label] $($candidate.FullName) [$($candidate.LastWriteTime)]"
}

$exe = $candidates | Select-Object -First 1
Write-Host "Launching Threadline Windows companion: $($exe.FullName)"
Write-Host "Startup log: $logPath"

$existing = @(Get-Process Threadline.Windows -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
  Write-Host "Existing Threadline.Windows process(es) detected: $($existing.Id -join ', ')"
}

$process = Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName -PassThru
Start-Sleep -Seconds 3

$running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
if ($null -eq $running -or $running.HasExited) {
  $exitCode = if ($null -ne $running) { $running.ExitCode } else { $process.ExitCode }
  Write-Host "Threadline.Windows exited immediately with code $exitCode." -ForegroundColor Yellow
  if (Test-Path $logPath) {
    Write-Host 'Last 120 lines from startup log:' -ForegroundColor Yellow
    Get-Content $logPath -Tail 120
  }
  else {
    Write-Host 'No startup log was found.' -ForegroundColor Yellow
  }
  throw 'Threadline.Windows did not stay running.'
}

Write-Host "Threadline.Windows is running. Process ID: $($process.Id)" -ForegroundColor Green
