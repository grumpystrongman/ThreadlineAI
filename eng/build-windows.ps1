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

function Test-WindowsAppSdkBuildTasks {
  $dotnetRoot = Split-Path -Parent (Split-Path -Parent (Get-Command dotnet).Source)
  $sdkVersion = (dotnet --version).Trim()
  $priTaskPath = Join-Path $dotnetRoot "sdk\$sdkVersion\Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll"

  if (-not (Test-Path $priTaskPath)) {
    Write-Host ''
    Write-Host 'Threadline.Windows uses WinUI 3 / Windows App SDK.' -ForegroundColor Yellow
    Write-Host 'This machine is missing the Visual Studio Windows app packaging PRI build task:' -ForegroundColor Yellow
    Write-Host "  $priTaskPath" -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Install or modify Visual Studio 2022 / Build Tools with these workloads/components:' -ForegroundColor Yellow
    Write-Host '  - .NET desktop development' -ForegroundColor Yellow
    Write-Host '  - Universal Windows Platform development / Windows application development tools' -ForegroundColor Yellow
    Write-Host '  - Windows 10/11 SDK and MSIX packaging tools' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'After install, reopen PowerShell and rerun ./eng/build-windows.ps1.' -ForegroundColor Yellow
    throw 'Missing Visual Studio Windows App SDK packaging build tasks required for WinUI.'
  }
}

Write-Host 'Checking .NET SDK availability...'
Invoke-CheckedCommand dotnet --list-sdks

Write-Host 'Checking WinUI / Windows App SDK build prerequisites...'
Test-WindowsAppSdkBuildTasks

Write-Host 'Restoring Threadline Windows companion...'
Invoke-CheckedCommand dotnet restore src/Threadline.Windows/Threadline.Windows.csproj

Write-Host 'Building Threadline Windows companion...'
Invoke-CheckedCommand dotnet build src/Threadline.Windows/Threadline.Windows.csproj --configuration Release --no-restore

Write-Host 'Windows companion build complete.'
