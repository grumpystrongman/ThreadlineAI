param(
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [string]$StageRoot = (Join-Path $PSScriptRoot '..\artifacts\commercial'),
  [string]$Version = '21.0.0',
  [switch]$RequireSigning
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $StageRoot))
$appStage = Join-Path $stageRoot 'app'
$serviceStage = Join-Path $stageRoot 'service'
$extensionStage = Join-Path $stageRoot 'browser-extension'
$installerStage = Join-Path $stageRoot 'installer'

Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $appStage,$serviceStage,$extensionStage,$installerStage | Out-Null

Write-Host 'Publishing local service...'
dotnet publish (Join-Path $repoRoot 'src\Threadline.Service\Threadline.Service.csproj') -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o $serviceStage

Write-Host 'Publishing Windows sidecar...'
dotnet publish (Join-Path $repoRoot 'src\Threadline.Windows\Threadline.Windows.csproj') -c $Configuration -r $Runtime --self-contained true -p:Version=$Version -o $appStage

Write-Host 'Building browser extension...'
Push-Location (Join-Path $repoRoot 'adapters\browser-extension')
try {
  if (Test-Path package-lock.json) { npm ci } else { npm install }
  npm run build
}
finally {
  Pop-Location
}
Copy-Item -Path (Join-Path $repoRoot 'adapters\browser-extension\*') -Destination $extensionStage -Recurse -Force

$manifest = [ordered]@{
  product = 'ThreadlineAI'
  version = $Version
  build = '21-commercial-installer-service-lifecycle'
  createdAt = (Get-Date).ToUniversalTime().ToString('O')
  runtime = $Runtime
  serviceName = 'ThreadlineAIService'
  serviceUrl = 'http://127.0.0.1:5057'
  includes = @('WinUI sidecar','ASP.NET Core local service','browser extension assets','service lifecycle scripts','diagnostics scripts')
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $stageRoot 'commercial-manifest.json') -Encoding UTF8

$wix = Get-Command wix.exe -ErrorAction SilentlyContinue
if ($wix) {
  Write-Host 'WiX detected. Building MSI from installer\wix\ThreadlineAI.wxs...'
  & $wix.Source build (Join-Path $repoRoot 'installer\wix\ThreadlineAI.wxs') -d SourceDir=$stageRoot -d ProductVersion=$Version -out (Join-Path $installerStage "ThreadlineAI-$Version.msi")
}
else {
  Write-Warning 'WiX v4 CLI was not found. Stage output is ready, but MSI generation was skipped.'
}

$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
$msi = Get-ChildItem -Path $installerStage -Filter '*.msi' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($msi -and $signTool) {
  if ($env:THREADLINE_SIGN_CERT_SHA1) {
    & $signTool.Source sign /sha1 $env:THREADLINE_SIGN_CERT_SHA1 /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $msi.FullName
  }
  elseif ($env:THREADLINE_SIGN_PFX -and $env:THREADLINE_SIGN_PFX_PASSWORD) {
    & $signTool.Source sign /f $env:THREADLINE_SIGN_PFX /p $env:THREADLINE_SIGN_PFX_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $msi.FullName
  }
  elseif ($RequireSigning) {
    throw 'MSI was created but signing credentials were not provided. Set THREADLINE_SIGN_CERT_SHA1 or THREADLINE_SIGN_PFX/THREADLINE_SIGN_PFX_PASSWORD.'
  }
  else {
    Write-Warning 'MSI was created but not signed because signing credentials were not provided.'
  }
}
elseif ($RequireSigning) {
  throw 'Signing was required, but either no MSI was created or signtool.exe was not found.'
}

Write-Host "Commercial package staged at $stageRoot"
