$ErrorActionPreference = 'Stop'

Write-Host 'Restoring ThreadlineAI core projects...'
dotnet restore src/Threadline.Core/Threadline.Core.csproj
dotnet restore src/Threadline.Infrastructure/Threadline.Infrastructure.csproj
dotnet restore src/Threadline.Service/Threadline.Service.csproj

Write-Host 'Building ThreadlineAI core projects...'
dotnet build src/Threadline.Core/Threadline.Core.csproj --configuration Release --no-restore
dotnet build src/Threadline.Infrastructure/Threadline.Infrastructure.csproj --configuration Release --no-restore
dotnet build src/Threadline.Service/Threadline.Service.csproj --configuration Release --no-restore

Write-Host 'Build complete.'
