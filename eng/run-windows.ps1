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

Write-Host 'Building Windows companion with Visual Studio MSBuild...'
& "$PSScriptRoot\build-windows.ps1"

$exe = Join-Path (Resolve-Path "$PSScriptRoot\..") 'src\Threadline.Windows\bin\Release\net8.0-windows10.0.19041.0\Threadline.Windows.exe'
if (-not (Test-Path $exe)) {
  throw "Windows companion executable was not found: $exe"
}

Write-Host "Launching Threadline Windows companion: $exe"
Start-Process -FilePath $exe
