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

Write-Host 'Attaching simulated active window...'
$windowBody = @{
  applicationName = 'PowerShell'
  processName = 'pwsh'
  windowTitle = 'Threadline Phase 6 Smoke Test'
  processId = $PID
  isForeground = $true
  metadata = @{ source = 'smoke-test' }
} | ConvertTo-Json -Depth 4
$windowAttachment = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.id)/windows/attach" -Headers $headers -ContentType 'application/json' -Body $windowBody
$windowAttachment | Format-List

Write-Host 'Previewing attached-window context...'
$windowPreviewBody = @{ userApproved = $true } | ConvertTo-Json
$windowPreview = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.id)/windows/current/preview" -Headers $headers -ContentType 'application/json' -Body $windowPreviewBody
$windowPreview | Format-List

Write-Host 'Storing attached-window context...'
$storedWindowContext = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.id)/windows/current/store" -Headers $headers -ContentType 'application/json' -Body $windowPreviewBody
$storedWindowContext | Format-List

Write-Host 'Proposing approved insert action...'
$actionBody = @{
  kind = 'InsertText'
  description = 'Insert generated smoke-test text into attached window'
  payload = 'Threadline smoke-test insertion payload'
  userApproved = $true
  risk = 'Medium'
} | ConvertTo-Json
$action = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.id)/actions" -Headers $headers -ContentType 'application/json' -Body $actionBody
$action | Format-List

Write-Host 'Completing insert action...'
$completeBody = @{ resultMessage = 'Smoke test marked action complete.'; failed = $false } | ConvertTo-Json
$completedAction = Invoke-RestMethod -Method Post -Uri "$BaseUrl/actions/$($action.id)/complete" -Headers $headers -ContentType 'application/json' -Body $completeBody
$completedAction | Format-List

Write-Host 'Saving provider credential through protected store...'
$credentialBody = @{
  secretValue = 'smoke-test-provider-secret-12345'
  authType = 'ApiKey'
  defaultModel = 'local-smoke-model'
  metadata = @{ purpose = 'smoke-test' }
} | ConvertTo-Json -Depth 4
$credentialResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/providers/SmokeProvider/credential" -Headers $headers -ContentType 'application/json' -Body $credentialBody
$credentialResponse.provider | Format-List
$credentialResponse.credential | Format-List

Write-Host 'Reading provider credential descriptor without exposing value...'
$encodedReference = [System.Uri]::EscapeDataString($credentialResponse.credential.reference)
$descriptor = Invoke-RestMethod -Method Get -Uri "$BaseUrl/secrets/$encodedReference" -Headers $headers
$descriptor | Format-List

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
Invoke-RestMethod -Method Get -Uri "$BaseUrl/audit/recent?take=20" -Headers $headers | Format-Table

Write-Host 'Smoke test complete.'
