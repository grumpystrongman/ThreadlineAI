param(
  [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

if (-not $SkipBuild) {
  Write-Host 'Building Threadline local service and Windows companion with Visual Studio MSBuild...'
  & "$PSScriptRoot\build-windows.ps1"
}
else {
  Write-Host 'Skipping build because -SkipBuild was supplied.'
}

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$releaseRoot = Join-Path $repoRoot 'src\Threadline.Windows\bin\Release'
$serviceReleaseRoot = Join-Path $repoRoot 'src\Threadline.Service\bin\Release'
$preferredExe = Join-Path $releaseRoot 'net8.0-windows10.0.19041.0\win-x64\Threadline.Windows.exe'
$preferredServiceExe = Join-Path $serviceReleaseRoot 'net8.0\Threadline.Service.exe'
$preferredServiceDll = Join-Path $serviceReleaseRoot 'net8.0\Threadline.Service.dll'
$logDirectory = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\logs'
$logPath = Join-Path $logDirectory 'Threadline.Windows.log'
$serviceLogPath = Join-Path $logDirectory 'Threadline.Service.out.log'
$serviceErrPath = Join-Path $logDirectory 'Threadline.Service.err.log'
$serviceUrl = 'http://localhost:5057'

if (-not (Test-Path $logDirectory)) {
  New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}

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

function Stop-ProcessByName {
  param([Parameter(Mandatory = $true)] [string] $Name)

  $existing = @(Get-Process $Name -ErrorAction SilentlyContinue)
  if ($existing.Count -gt 0) {
    Write-Host "Stopping existing $Name process(es): $($existing.Id -join ', ')" -ForegroundColor Yellow
    $existing | Stop-Process -Force
    Start-Sleep -Seconds 1
  }
}

function Start-ThreadlineService {
  if (Test-Path $serviceLogPath) { Remove-Item $serviceLogPath -Force -ErrorAction SilentlyContinue }
  if (Test-Path $serviceErrPath) { Remove-Item $serviceErrPath -Force -ErrorAction SilentlyContinue }

  $serviceFile = $null
  $serviceArguments = $null
  if (Test-Path $preferredServiceExe) {
    $serviceFile = $preferredServiceExe
    $serviceArguments = $null
  }
  elseif (Test-Path $preferredServiceDll) {
    $serviceFile = 'dotnet'
    $serviceArguments = "`"$preferredServiceDll`""
  }
  else {
    Write-Host "Threadline service executable was not found. Searching under: $serviceReleaseRoot" -ForegroundColor Yellow
    $fallbackExe = Get-ChildItem $serviceReleaseRoot -Recurse -Filter 'Threadline.Service.exe' -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -notmatch '\\ref\\|\\refint\\|\\obj\\' } |
      Sort-Object LastWriteTime -Descending |
      Select-Object -First 1

    if ($null -ne $fallbackExe) {
      $serviceFile = $fallbackExe.FullName
      $serviceArguments = $null
    }
    else {
      $fallbackDll = Get-ChildItem $serviceReleaseRoot -Recurse -Filter 'Threadline.Service.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\ref\\|\\refint\\|\\obj\\' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

      if ($null -eq $fallbackDll) {
        throw "Threadline service was not found under: $serviceReleaseRoot. Run ./eng/build-windows.ps1 first."
      }

      $serviceFile = 'dotnet'
      $serviceArguments = "`"$($fallbackDll.FullName)`""
    }
  }

  $serviceWorkingDirectory = if ($serviceFile -eq 'dotnet') {
    Split-Path -Parent $preferredServiceDll
  }
  else {
    Split-Path -Parent $serviceFile
  }

  Write-Host "Starting Threadline local service on $serviceUrl"
  Write-Host "Service log: $serviceLogPath"

  $previousUrls = $env:ASPNETCORE_URLS
  $env:ASPNETCORE_URLS = $serviceUrl
  try {
    if ([string]::IsNullOrWhiteSpace($serviceArguments)) {
      $process = Start-Process -FilePath $serviceFile -WorkingDirectory $serviceWorkingDirectory -RedirectStandardOutput $serviceLogPath -RedirectStandardError $serviceErrPath -PassThru
    }
    else {
      $process = Start-Process -FilePath $serviceFile -ArgumentList $serviceArguments -WorkingDirectory $serviceWorkingDirectory -RedirectStandardOutput $serviceLogPath -RedirectStandardError $serviceErrPath -PassThru
    }
  }
  finally {
    $env:ASPNETCORE_URLS = $previousUrls
  }

  for ($attempt = 1; $attempt -le 30; $attempt++) {
    Start-Sleep -Milliseconds 350

    $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($null -eq $running -or $running.HasExited) {
      Write-Host 'Threadline.Service exited before becoming healthy.' -ForegroundColor Yellow
      Show-ServiceLogs
      throw 'Threadline local service did not stay running.'
    }

    try {
      $response = Invoke-WebRequest -Uri "$serviceUrl/health" -UseBasicParsing -TimeoutSec 1
      if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
        Write-Host "Threadline local service is running. Process ID: $($process.Id)" -ForegroundColor Green
        return $process
      }
    }
    catch {
      # Service may still be starting.
    }
  }

  Write-Host 'Threadline.Service did not answer /health in time.' -ForegroundColor Yellow
  Show-ServiceLogs
  throw 'Threadline local service did not become healthy.'
}

function Show-ServiceLogs {
  if (Test-Path $serviceLogPath) {
    Write-Host 'Last 120 lines from service stdout:' -ForegroundColor Yellow
    Get-Content $serviceLogPath -Tail 120
  }
  if (Test-Path $serviceErrPath) {
    Write-Host 'Last 120 lines from service stderr:' -ForegroundColor Yellow
    Get-Content $serviceErrPath -Tail 120
  }
}

Write-Host "Launching Threadline Windows companion: $preferredExe"
Write-Host "Startup log: $logPath"

Stop-ProcessByName Threadline.Windows
Stop-ProcessByName Threadline.Service

if (Test-Path $logPath) {
  Remove-Item $logPath -Force -ErrorAction SilentlyContinue
}

$serviceProcess = Start-ThreadlineService

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
Write-Host "Threadline.Service is running. Process ID: $($serviceProcess.Id)" -ForegroundColor Green
Write-Host 'If you do not see the window, hover near the right edge of a non-Threadline window for the floating AI tab.' -ForegroundColor Yellow
