param(
  [switch] $Build
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$windowsProject = Join-Path $repoRoot 'src/Threadline.Windows'
$requiredFiles = @(
  'ShuttleTabWindow.cs',
  'MainWindow.ShuttleTabs.cs',
  'MainWindow.EdgeHandleStartup.cs'
)

foreach ($file in $requiredFiles) {
  $path = Join-Path $windowsProject $file
  if (-not (Test-Path $path)) {
    throw "Missing required Shuttle file: $path"
  }
}

$shuttleManager = Get-Content (Join-Path $windowsProject 'MainWindow.ShuttleTabs.cs') -Raw
$edgeStartup = Get-Content (Join-Path $windowsProject 'MainWindow.EdgeHandleStartup.cs') -Raw
$shuttleWindow = Get-Content (Join-Path $windowsProject 'ShuttleTabWindow.cs') -Raw

if ($shuttleManager -notmatch 'StartShuttleTabs') {
  throw 'Smoke failed: StartShuttleTabs is missing.'
}

if ($shuttleManager -notmatch 'EnumWindows') {
  throw 'Smoke failed: Shuttle manager is not discovering eligible windows.'
}

if ($shuttleManager -notmatch 'RestoreSidecarFromFloatingTrigger') {
  throw 'Smoke failed: Shuttle click path does not restore/open the sidecar.'
}

if ($shuttleManager -notmatch 'DirectWindowHoverEnabled\s*=\s*false') {
  throw 'Smoke failed: old direct hover affordance is not explicitly disabled.'
}

if ($edgeStartup -notmatch 'StartShuttleTabs\(\)') {
  throw 'Smoke failed: startup is not arming Shuttle tabs.'
}

if ($edgeStartup -match 'StartFallbackFloatingTriggerTimer\(\)') {
  throw 'Smoke failed: startup still arms the old fallback hover trigger.'
}

if ($shuttleWindow -notmatch 'Threadline Shuttle') {
  throw 'Smoke failed: native Shuttle tab window identity is missing.'
}

if ($shuttleWindow -notmatch 'Clicked') {
  throw 'Smoke failed: Shuttle tab click event is missing.'
}

Write-Host 'Shuttle tabs smoke checks passed.' -ForegroundColor Green

if ($Build) {
  & (Join-Path $PSScriptRoot 'build-windows.ps1')
}
