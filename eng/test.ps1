$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

Write-Host 'Checking .NET SDK availability...'
dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Running ThreadlineAI tests...'
dotnet test tests/Threadline.Core.Tests/Threadline.Core.Tests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test tests/Threadline.Infrastructure.Tests/Threadline.Infrastructure.Tests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Tests complete.'
