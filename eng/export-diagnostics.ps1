param(
  [string]$BaseUrl = 'http://127.0.0.1:5057',
  [string]$TokenPath = (Join-Path $env:LOCALAPPDATA 'ThreadlineAI\service-token.txt')
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

$headers = @{}
if (Test-Path $TokenPath) {
  $token = (Get-Content -Raw -Path $TokenPath).Trim()
  if ($token.Length -ge 32) {
    $headers['X-Threadline-Token'] = $token
  }
}

try {
  $result = Invoke-RestMethod -Method Post -Uri "$($BaseUrl.TrimEnd('/'))/diagnostics/export" -Headers $headers
  Write-Host "Diagnostics package created: $($result.exportPath)"
  $result | ConvertTo-Json -Depth 10
}
catch {
  Write-Warning "Could not export diagnostics through the service: $($_.Exception.Message)"
  $fallbackRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\diagnostics'
  New-Item -ItemType Directory -Force -Path $fallbackRoot | Out-Null
  $fallback = Join-Path $fallbackRoot ("threadline-diagnostics-fallback-{0:yyyyMMdd-HHmmss}.zip" -f (Get-Date))
  $sourceRoot = Join-Path $env:LOCALAPPDATA 'ThreadlineAI'
  if (Test-Path $sourceRoot) {
    Compress-Archive -Path (Join-Path $sourceRoot '*') -DestinationPath $fallback -Force
    Write-Host "Fallback diagnostics package created: $fallback"
  }
  else {
    throw 'ThreadlineAI local data root was not found, and the service diagnostics endpoint was unavailable.'
  }
}
