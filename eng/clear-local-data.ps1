param(
  [string]$ServiceName = 'ThreadlineAIService',
  [string]$LocalDataRoot = (Join-Path $env:LOCALAPPDATA 'ThreadlineAI'),
  [switch]$PreserveDiagnostics,
  [switch]$Force
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

if (-not $Force) {
  $confirmation = Read-Host 'Type CLEAR THREADLINE LOCAL DATA to remove local database, tokens, settings, logs, and cached context'
  if ($confirmation -ne 'CLEAR THREADLINE LOCAL DATA') {
    Write-Host 'Confirmation did not match. No data was removed.'
    exit 1
  }
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
  Write-Host "Stopping $ServiceName before clearing local data..."
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
}

$targets = @(
  (Join-Path $LocalDataRoot 'threadline.db'),
  (Join-Path $LocalDataRoot 'threadline.db-wal'),
  (Join-Path $LocalDataRoot 'threadline.db-shm'),
  (Join-Path $LocalDataRoot 'service-token.txt'),
  (Join-Path $LocalDataRoot 'secrets'),
  (Join-Path $LocalDataRoot 'logs'),
  (Join-Path $LocalDataRoot 'settings.json'),
  (Join-Path $LocalDataRoot 'first-run-complete.json')
)

if (-not $PreserveDiagnostics) {
  $targets += (Join-Path $LocalDataRoot 'diagnostics')
}

$failed = New-Object System.Collections.Generic.List[string]
foreach ($target in $targets) {
  if (-not (Test-Path $target)) { continue }
  Write-Host "Removing $target"
  Remove-Item -Path $target -Recurse -Force -ErrorAction SilentlyContinue
  if (Test-Path $target) {
    $failed.Add($target)
  }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
try {
  Remove-ItemProperty -Path $runKey -Name 'ThreadlineAI' -ErrorAction SilentlyContinue
} catch {
  $failed.Add('HKCU Run entry: ThreadlineAI')
}

if ($failed.Count -gt 0) {
  Write-Error ("Clear local data finished with failures:`n" + ($failed -join "`n"))
}

Write-Host 'ThreadlineAI local data clear verified: targeted files, settings, tokens, logs, and startup entry are absent.'
