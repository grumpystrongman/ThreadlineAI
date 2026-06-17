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

Write-Host 'Checking .NET SDK availability...'
Invoke-CheckedCommand dotnet --list-sdks

Write-Host 'Restoring Threadline Windows companion...'
Invoke-CheckedCommand dotnet restore src/Threadline.Windows/Threadline.Windows.csproj

Write-Host 'Building Threadline Windows companion...'
Invoke-CheckedCommand dotnet build src/Threadline.Windows/Threadline.Windows.csproj --configuration Release --no-restore

Write-Host 'Windows companion build complete.'
