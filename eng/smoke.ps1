param(
  [string] $BaseUrl = 'http://localhost:5057',
  [string] $LocalAccess = ''
)

$ErrorActionPreference = 'Stop'

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($LocalAccess)) {
  $headers['X-Threadline-Token'] = $LocalAccess
}

Write-Host "Checking Threadline service at $BaseUrl..."
$health = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health"
$health | Format-List

Write-Host 'Creating smoke-test session...'
$sessionBody = @{ name = 'Smoke test session'; provider = 'Local' } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions" -Headers $headers -ContentType 'application/json' -Body $sessionBody
$session | Format-List

Write-Host 'Previewing context...'
$previewBody = @{
  source = 'Manual'
  contextType = 'note'
  content = 'api_key=abc123456789 should be redacted'
  userApproved = $false
} | ConvertTo-Json
$preview = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.id)/events/preview" -Headers $headers -ContentType 'application/json' -Body $previewBody
$preview | Format-List

Write-Host 'Storing approved context...'
$eventBody = @{
  source = 'Manual'
  contextType = 'note'
  content = 'This is a safe smoke-test note.'
  userApproved = $true
} | ConvertTo-Json
$event = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.id)/events" -Headers $headers -ContentType 'application/json' -Body $eventBody
$event | Format-List

Write-Host 'Registering adapter...'
$adapterBody = @{
  kind = 'DevelopmentTool'
  displayName = 'Smoke Test Adapter'
  permissions = 'All'
  version = '0.1.0'
} | ConvertTo-Json
$adapter = Invoke-RestMethod -Method Post -Uri "$BaseUrl/adapters" -Headers $headers -ContentType 'application/json' -Body $adapterBody
$adapter | Format-List

Write-Host 'Sending adapter heartbeat...'
$heartbeat = Invoke-RestMethod -Method Post -Uri "$BaseUrl/adapters/$($adapter.id)/heartbeat" -Headers $headers
$heartbeat | Format-List

Write-Host 'Reading recent audit events...'
Invoke-RestMethod -Method Get -Uri "$BaseUrl/audit/recent?take=10" -Headers $headers | Format-Table

Write-Host 'Smoke test complete.'
