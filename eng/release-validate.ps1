param(
  [switch] $SkipBrowserExtension,
  [switch] $SkipWindows,
  [string] $ArtifactsRoot = 'artifacts/release-validation'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifactPath = Join-Path $repoRoot $ArtifactsRoot

function Invoke-Step {
  param(
    [Parameter(Mandatory = $true)] [string] $Name,
    [Parameter(Mandatory = $true)] [scriptblock] $Script
  )

  Write-Host ''
  Write-Host "==> $Name" -ForegroundColor Cyan
  $started = Get-Date
  & $Script
  $elapsed = (Get-Date) - $started
  Write-Host "Completed: $Name ($([int]$elapsed.TotalSeconds)s)" -ForegroundColor Green
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

Push-Location $repoRoot
try {
  if (Test-Path $artifactPath) {
    Remove-Item -Recurse -Force $artifactPath
  }
  New-Item -ItemType Directory -Force -Path $artifactPath | Out-Null

  Invoke-Step 'Build core/service release configuration' {
    & "$PSScriptRoot/build.ps1"
  }

  Invoke-Step 'Run all automated tests' {
    & "$PSScriptRoot/test.ps1"
  }

  Invoke-Step 'Publish local service release output' {
    $serviceOut = Join-Path $artifactPath 'service'
    Invoke-CheckedCommand dotnet publish src/Threadline.Service/Threadline.Service.csproj --configuration Release --output $serviceOut
    if (-not (Test-Path (Join-Path $serviceOut 'Threadline.Service.dll'))) {
      throw 'Release validation failed: Threadline.Service.dll was not produced.'
    }
  }

  if (-not $SkipBrowserExtension) {
    Invoke-Step 'Build browser extension' {
      & "$PSScriptRoot/build-browser-extension.ps1"
      $dist = Join-Path $repoRoot 'adapters/browser-extension/dist'
      if (-not (Test-Path $dist)) {
        throw 'Release validation failed: browser extension dist folder was not produced.'
      }
    }
  }
  else {
    Write-Host 'Skipping browser extension build because -SkipBrowserExtension was supplied.' -ForegroundColor Yellow
  }

  if (-not $SkipWindows) {
    Invoke-Step 'Build Windows companion' {
      & "$PSScriptRoot/build-windows.ps1"
    }
  }
  else {
    Write-Host 'Skipping Windows companion build because -SkipWindows was supplied.' -ForegroundColor Yellow
  }

  $manifest = [ordered]@{
    validatedAt = (Get-Date).ToUniversalTime().ToString('O')
    artifactsRoot = $artifactPath
    coreBuild = 'passed'
    automatedTests = 'passed'
    servicePublish = 'passed'
    browserExtension = if ($SkipBrowserExtension) { 'skipped' } else { 'passed' }
    windowsCompanion = if ($SkipWindows) { 'skipped' } else { 'passed' }
  }

  $manifestPath = Join-Path $artifactPath 'release-validation.json'
  $manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $manifestPath

  Write-Host ''
  Write-Host 'Release validation passed.' -ForegroundColor Green
  Write-Host "Manifest: $manifestPath"
}
finally {
  Pop-Location
}
