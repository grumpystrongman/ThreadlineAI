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

Write-Host 'Restoring ThreadlineAI core projects...'
Invoke-CheckedCommand dotnet restore src/Threadline.Core/Threadline.Core.csproj
Invoke-CheckedCommand dotnet restore src/Threadline.Infrastructure/Threadline.Infrastructure.csproj
Invoke-CheckedCommand dotnet restore src/Threadline.Service/Threadline.Service.csproj

Write-Host 'Building ThreadlineAI core projects...'
Invoke-CheckedCommand dotnet build src/Threadline.Core/Threadline.Core.csproj --configuration Release --no-restore
Invoke-CheckedCommand dotnet build src/Threadline.Infrastructure/Threadline.Infrastructure.csproj --configuration Release --no-restore
Invoke-CheckedCommand dotnet build src/Threadline.Service/Threadline.Service.csproj --configuration Release --no-restore

Write-Host 'Build complete.'
