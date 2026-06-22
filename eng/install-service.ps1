param(
  [string]$InstallRoot = "$env:ProgramFiles\ThreadlineAI",
  [string]$ServiceName = 'ThreadlineAIService',
  [string]$DisplayName = 'ThreadlineAI Service',
  [string]$ServiceUrl = 'http://127.0.0.1:5057',
  [switch]$Start
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Assert-Administrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]::new($identity)
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Install-service must be run from an elevated PowerShell session.'
  }
}

Assert-Administrator

$serviceExe = Join-Path $InstallRoot 'service\Threadline.Service.exe'
if (-not (Test-Path $serviceExe)) {
  throw "Threadline service executable was not found at $serviceExe. Run eng/package-commercial.ps1 first or pass the installed root."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  Write-Host "Stopping existing $ServiceName service..."
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Host
  Start-Sleep -Seconds 2
}

$binPath = "`"$serviceExe`" --urls `"$ServiceUrl`""
Write-Host "Creating $DisplayName at $ServiceUrl..."
sc.exe create $ServiceName binPath= $binPath start= delayed-auto DisplayName= $DisplayName | Out-Host
sc.exe description $ServiceName 'ThreadlineAI local context broker and provider bridge.' | Out-Host

# Restart after crashes: 1 minute, 5 minutes, then 15 minutes.
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/900000 | Out-Host
sc.exe failureflag $ServiceName 1 | Out-Host

if ($Start) {
  Write-Host "Starting $ServiceName..."
  Start-Service -Name $ServiceName
  Start-Sleep -Seconds 2
  Get-Service -Name $ServiceName | Format-List Name,DisplayName,Status,StartType
}
else {
  Write-Host "$ServiceName installed. Start it with: Start-Service $ServiceName"
}
