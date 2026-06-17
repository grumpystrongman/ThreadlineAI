param(
  [string] $BaseUrl = 'http://localhost:5057',
  [string] $LocalAccess = ''
)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path (Resolve-Path "$PSScriptRoot\..") 'adapters\powershell\Threadline.PowerShell.psm1'
Import-Module $modulePath -Force

if ([string]::IsNullOrWhiteSpace($LocalAccess)) {
  Set-ThreadlineService -BaseUrl $BaseUrl
}
else {
  Set-ThreadlineService -BaseUrl $BaseUrl -LocalAccessToken $LocalAccess
}

Write-Host 'Checking active session or creating one...'
try {
  $session = Get-ThreadlineActiveSession
}
catch {
  $session = Start-ThreadlineSession -Name 'PowerShell adapter smoke session' -Provider 'Local'
}
$session | Format-List

Write-Host 'Registering PowerShell adapter...'
$adapter = Register-ThreadlinePowerShellAdapter
$adapter | Format-List

Write-Host 'Sending manual terminal context...'
$manual = Send-ThreadlineTerminalContext -SessionId $session.id -Content 'PowerShell adapter smoke context.' -ContextType 'terminal-note'
$manual | Format-List

Write-Host 'Capturing command output...'
$commandResult = Invoke-ThreadlineCommandCapture -SessionId $session.id -Command 'Write-Output "Threadline PowerShell smoke command"'
$commandResult | Format-List

Write-Host 'Proposing approved command action...'
$action = New-ThreadlineCommandAction -SessionId $session.id -Command 'Get-ChildItem' -Description 'List current directory' -Risk Low -UserApproved
$action | Format-List

Write-Host 'Completing command action...'
$completed = Complete-ThreadlineCommandAction -ActionId $action.id -ResultMessage 'Smoke action completed.'
$completed | Format-List

Write-Host 'PowerShell adapter smoke test complete.'
