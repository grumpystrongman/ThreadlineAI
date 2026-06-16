$ErrorActionPreference = 'Stop'

Write-Host 'Running ThreadlineAI tests...'
dotnet test tests/Threadline.Core.Tests/Threadline.Core.Tests.csproj --configuration Release
dotnet test tests/Threadline.Infrastructure.Tests/Threadline.Infrastructure.Tests.csproj --configuration Release

Write-Host 'Tests complete.'
