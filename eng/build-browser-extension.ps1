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

$extensionRoot = Join-Path (Resolve-Path "$PSScriptRoot\..") 'adapters\browser-extension'
Push-Location $extensionRoot
try {
  Write-Host 'Installing browser extension dependencies...'
  if (Test-Path 'package-lock.json') {
    Invoke-CheckedCommand npm ci
  }
  else {
    Invoke-CheckedCommand npm install
  }

  Write-Host 'Cleaning browser extension dist...'
  if (Test-Path 'dist') {
    Remove-Item -Recurse -Force 'dist'
  }

  Write-Host 'Building browser extension...'
  Invoke-CheckedCommand npm run build
  Write-Host "Browser extension build complete: $extensionRoot"
}
finally {
  Pop-Location
}
