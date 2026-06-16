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
    throw "Command failed with exit code $LASTEXITCODE: $Command $($Arguments -join ' ')"
  }
}

Write-Host 'Checking .NET SDK availability...'
Invoke-CheckedCommand dotnet --list-sdks

Write-Host 'Running ThreadlineAI tests...'
Invoke-CheckedCommand dotnet test tests/Threadline.Core.Tests/Threadline.Core.Tests.csproj --configuration Release
Invoke-CheckedCommand dotnet test tests/Threadline.Infrastructure.Tests/Threadline.Infrastructure.Tests.csproj --configuration Release

Write-Host 'Tests complete.'
