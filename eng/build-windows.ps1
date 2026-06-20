param(
  [switch] $Run
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)] [string] $Command,
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments
  )

  & $Command @Arguments
  if ($LASTEXITCODE -ne 0) {
    $argumentText = $Arguments -join ' '
    throw "Command failed with exit code $($LASTEXITCODE): $Command $argumentText"
  }
}

function Stop-ThreadlineWindowsCompanion {
  $processes = Get-Process Threadline.Windows -ErrorAction SilentlyContinue
  if ($processes) {
    Write-Host 'Stopping running Threadline.Windows instances before build...'
    $processes | Stop-Process -Force
    Start-Sleep -Milliseconds 500
  }
}

function Stop-ThreadlineService {
  $processes = Get-Process Threadline.Service -ErrorAction SilentlyContinue
  if ($processes) {
    Write-Host 'Stopping running Threadline.Service instances before build...'
    $processes | Stop-Process -Force
    Start-Sleep -Milliseconds 500
  }
}

function Get-VisualStudioInstallPath {
  $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
  if (-not (Test-Path $vswhere)) {
    return $null
  }

  $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
  if ([string]::IsNullOrWhiteSpace($installPath)) {
    return $null
  }

  return $installPath.Trim()
}

function Get-VisualStudioMsBuildPath {
  $installPath = Get-VisualStudioInstallPath
  if ([string]::IsNullOrWhiteSpace($installPath)) {
    return $null
  }

  $candidates = @(
    (Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path $installPath 'MSBuild\Current\Bin\amd64\MSBuild.exe')
  )

  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) {
      return $candidate
    }
  }

  return $null
}

function Test-WindowsAppSdkBuildTasks {
  $installPath = Get-VisualStudioInstallPath
  $vsPriTaskPath = $null
  if (-not [string]::IsNullOrWhiteSpace($installPath)) {
    $vsPriTaskPath = Join-Path $installPath 'MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll'
  }

  if ($vsPriTaskPath -and (Test-Path $vsPriTaskPath)) {
    return
  }

  Write-Host ''
  Write-Host 'Threadline.Windows uses WinUI 3 / Windows App SDK.' -ForegroundColor Yellow
  Write-Host 'This machine is missing the Visual Studio Windows app packaging PRI build task.' -ForegroundColor Yellow
  if ($vsPriTaskPath) {
    Write-Host 'Expected Visual Studio task path:' -ForegroundColor Yellow
    Write-Host "  $vsPriTaskPath" -ForegroundColor Yellow
  }
  else {
    Write-Host 'No Visual Studio / Build Tools MSBuild installation was found by vswhere.' -ForegroundColor Yellow
  }
  Write-Host ''
  Write-Host 'Install or modify Visual Studio 2022 / Build Tools with these workloads/components:' -ForegroundColor Yellow
  Write-Host '  - .NET desktop development' -ForegroundColor Yellow
  Write-Host '  - Universal Windows Platform development / Windows application development tools' -ForegroundColor Yellow
  Write-Host '  - Windows 10/11 SDK and MSIX packaging tools' -ForegroundColor Yellow
  Write-Host ''
  Write-Host 'VS Code is fine as the editor, but WinUI still needs these Visual Studio build tools.' -ForegroundColor Yellow
  Write-Host 'After install, reopen PowerShell and rerun ./eng/build-windows.ps1.' -ForegroundColor Yellow
  throw 'Missing Visual Studio Windows App SDK packaging build tasks required for WinUI.'
}

Stop-ThreadlineWindowsCompanion
Stop-ThreadlineService

Write-Host 'Checking .NET SDK availability...'
Invoke-CheckedCommand dotnet --list-sdks

Write-Host 'Checking WinUI / Windows App SDK build prerequisites...'
Test-WindowsAppSdkBuildTasks

$msbuild = Get-VisualStudioMsBuildPath
if ([string]::IsNullOrWhiteSpace($msbuild)) {
  throw 'Visual Studio MSBuild.exe was not found. Install Visual Studio 2022 Build Tools or Visual Studio Community with Windows app development tools.'
}

Write-Host "Using Visual Studio MSBuild: $msbuild"

Write-Host 'Restoring Threadline local service...'
Invoke-CheckedCommand $msbuild src/Threadline.Service/Threadline.Service.csproj /t:Restore /p:Configuration=Release

Write-Host 'Building Threadline local service...'
Invoke-CheckedCommand $msbuild src/Threadline.Service/Threadline.Service.csproj /p:Configuration=Release /p:Restore=false

Write-Host 'Restoring Threadline Windows companion...'
Invoke-CheckedCommand $msbuild src/Threadline.Windows/Threadline.Windows.csproj /t:Restore /p:Configuration=Release

Write-Host 'Building Threadline Windows companion...'
Invoke-CheckedCommand $msbuild src/Threadline.Windows/Threadline.Windows.csproj /p:Configuration=Release /p:Restore=false

Write-Host 'Threadline service and Windows companion build complete.' -ForegroundColor Green

if ($Run) {
  Write-Host 'Launching local service and Windows companion because -Run was supplied...'
  & "$PSScriptRoot\run-windows.ps1" -SkipBuild
  exit $LASTEXITCODE
}

Write-Host ''
Write-Host 'Build only: the Windows app and local service have not been launched.' -ForegroundColor Yellow
Write-Host 'To launch them now, run:' -ForegroundColor Yellow
Write-Host '  ./eng/run-windows.ps1' -ForegroundColor Yellow
Write-Host 'Or build and launch in one step:' -ForegroundColor Yellow
Write-Host '  ./eng/build-windows.ps1 -Run' -ForegroundColor Yellow
