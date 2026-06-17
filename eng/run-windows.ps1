$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

Write-Host 'Building Windows companion with Visual Studio MSBuild...'
& "$PSScriptRoot\build-windows.ps1"

$releaseRoot = Join-Path (Resolve-Path "$PSScriptRoot\..") 'src\Threadline.Windows\bin\Release'
$exe = Get-ChildItem $releaseRoot -Recurse -Filter 'Threadline.Windows.exe' |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if ($null -eq $exe) {
  throw "Windows companion executable was not found under: $releaseRoot"
}

$logPath = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\logs\Threadline.Windows.log'
Write-Host "Launching Threadline Windows companion: $($exe.FullName)"
Write-Host "Startup log: $logPath"

$process = Start-Process -FilePath $exe.FullName -PassThru
Start-Sleep -Seconds 2

if ($process.HasExited) {
  Write-Host "Threadline.Windows exited immediately with code $($process.ExitCode)." -ForegroundColor Yellow
  if (Test-Path $logPath) {
    Write-Host 'Last 80 lines from startup log:' -ForegroundColor Yellow
    Get-Content $logPath -Tail 80
  }
  else {
    Write-Host 'No startup log was found.' -ForegroundColor Yellow
  }
}
else {
  Write-Host "Threadline.Windows is running. Process ID: $($process.Id)" -ForegroundColor Green
}
