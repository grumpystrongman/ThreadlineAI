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

if ($shuttleManager -match 'Win32Interop' -and $shuttleManager -notmatch 'using Microsoft\.UI;') {
  throw 'Smoke failed: Shuttle manager uses Win32Interop without importing Microsoft.UI.'
}

if ($shuttleManager -notmatch 'occludingRects') {
  throw 'Smoke failed: Shuttle placement is not tracking higher-window occlusion.'
}

if ($shuttleManager -notmatch 'TryFindUnoccludedShuttleLocation') {
  throw 'Smoke failed: Shuttle placement does not require an unoccluded edge location.'
}

if ($shuttleManager -notmatch 'GetRightEdgeAnchoredShuttleX') {
  throw 'Smoke failed: Shuttle placement is not edge-anchoring the tab X coordinate.'
}

if ($shuttleManager -notmatch 'ShuttleTabEdgeOverlap') {
  throw 'Smoke failed: Shuttle placement has no explicit right-edge overlap.'
}

if ($shuttleManager -notmatch 'targetRect\.Right\s*-\s*ShuttleTabEdgeOverlap') {
  throw 'Smoke failed: Shuttle preferred placement is not anchored just outside the right edge.'
}

if ($shuttleManager -notmatch 'targetRect\.Right\s*-\s*ShuttleTabWidth') {
  throw 'Smoke failed: Shuttle screen-edge fallback is not flush to the right edge.'
}

if ($shuttleManager -notmatch 'IsAnchoredToRightEdge') {
  throw 'Smoke failed: Shuttle placement does not verify right-edge anchoring.'
}

if ($shuttleManager -match 'targetRect\.Right\s*-\s*ShuttleTabWidth\s*-\s*ShuttleTabInset') {
  throw 'Smoke failed: Shuttle placement regressed to the old inset-inside-window formula.'
}

if ($shuttleManager -notmatch 'IsShuttleTargetWindow') {
  throw 'Smoke failed: Shuttle placement is not filtering real target windows.'
}

if ($shuttleManager -notmatch 'ShuttleGetWindowLongPtr') {
  throw 'Smoke failed: Shuttle placement is not checking tool/owned window styles.'
}

if ($shuttleManager -notmatch 'ShuttleDwmGetWindowAttribute') {
  throw 'Smoke failed: Shuttle placement is not filtering cloaked windows.'
}

if ($edgeStartup -notmatch 'StartShuttleTabs\(\)') {
  throw 'Smoke failed: startup is not arming Shuttle tabs.'
}

if ($edgeStartup -notmatch 'OpenSidecarAtStartup\(\)') {
  throw 'Smoke failed: startup does not force the sidecar visible first.'
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
