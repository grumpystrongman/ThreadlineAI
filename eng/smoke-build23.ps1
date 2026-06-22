param(
  [string] $BaseUrl = 'http://localhost:5057',
  [string] $LocalAccess = ''
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($LocalAccess)) {
  $headers['X-Threadline-Token'] = $LocalAccess
}

function Assert-Condition {
  param(
    [Parameter(Mandatory = $true)] [bool] $Condition,
    [Parameter(Mandatory = $true)] [string] $Message
  )

  if (-not $Condition) {
    throw "Build 23 smoke assertion failed: $Message"
  }
}

function Invoke-ThreadlineJson {
  param(
    [Parameter(Mandatory = $true)] [ValidateSet('Get', 'Post', 'Delete')] [string] $Method,
    [Parameter(Mandatory = $true)] [string] $Path,
    [object] $Body = $null
  )

  $uri = "$BaseUrl$Path"
  if ($null -eq $Body) {
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
  }

  return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 10)
}

Write-Host "Build 23 smoke: checking Threadline service at $BaseUrl..."
$health = Invoke-ThreadlineJson -Method Get -Path '/health'
Assert-Condition ($health.status -eq 'ok') 'GET /health should return status ok.'
Assert-Condition ($health.service -eq 'Threadline.Service') 'GET /health should identify Threadline.Service.'

Write-Host 'Build 23 smoke: checking reliability contracts...'
$actions = Invoke-ThreadlineJson -Method Get -Path '/actions'
Assert-Condition (($actions | Where-Object { $_.id -eq 'provider.test' }).Count -eq 1) 'GET /actions should include provider.test.'
Assert-Condition (($actions | Where-Object { $_.id -eq 'artifact.summary' }).Count -eq 1) 'GET /actions should include artifact.summary.'

$capabilities = Invoke-ThreadlineJson -Method Get -Path '/capabilities'
Assert-Condition (($capabilities | Where-Object { $_.id -eq 'browser-extension.bridge' }).Count -eq 1) 'GET /capabilities should include browser-extension.bridge.'

$doctorBefore = Invoke-ThreadlineJson -Method Get -Path '/doctor'
Assert-Condition (($doctorBefore.checks | Where-Object { $_.id -eq 'sqlite.writable' }).Count -eq 1) 'GET /doctor should include sqlite.writable.'
Assert-Condition (($doctorBefore.checks | Where-Object { $_.id -eq 'browser-extension.reachable' }).Count -eq 1) 'GET /doctor should include browser-extension.reachable.'

Write-Host 'Build 23 smoke: bootstrapping a session and approved context...'
$session = Invoke-ThreadlineJson -Method Post -Path '/sessions' -Body @{ name = 'Build 23 smoke session'; provider = 'Local' }
Assert-Condition ($session.id -like 'ses_*') 'POST /sessions should return a Threadline session id.'

$context = Invoke-ThreadlineJson -Method Post -Path "/sessions/$($session.id)/events" -Body @{
  source = 'Manual'
  contextType = 'release-smoke-note'
  content = 'Build 23 smoke verifies service, reliability, session, context, and registered action contracts.'
  userApproved = $true
}
Assert-Condition ($context.id -like 'evt_*') 'POST /sessions/{id}/events should return a Threadline context event id.'

$prompt = Invoke-ThreadlineJson -Method Post -Path "/sessions/$($session.id)/prompt" -Body @{ question = 'What does Build 23 smoke verify?' }
Assert-Condition ($prompt.Count -eq 2) 'POST /sessions/{id}/prompt should return system and user messages.'

Write-Host 'Build 23 smoke: registering browser extension and heartbeat...'
$adapter = Invoke-ThreadlineJson -Method Post -Path '/adapters' -Body @{
  kind = 'BrowserExtension'
  displayName = 'Build 23 Smoke Browser Extension'
  permissions = 'WriteContext'
  version = '17.0.0'
  metadata = @{ extensionVersion = '17.0.0'; source = 'build-23-smoke' }
}
Assert-Condition ($adapter.id -like 'adp_*') 'POST /adapters should return an adapter id.'

$heartbeat = Invoke-ThreadlineJson -Method Post -Path "/adapters/$($adapter.id)/heartbeat" -Body @{ version = '17.0.0'; metadata = @{ source = 'build-23-smoke' } }
Assert-Condition ($heartbeat.id -eq $adapter.id) 'POST /adapters/{id}/heartbeat should update the same adapter.'

$doctorAfter = Invoke-ThreadlineJson -Method Get -Path '/doctor'
$browserReachable = $doctorAfter.checks | Where-Object { $_.id -eq 'browser-extension.reachable' } | Select-Object -First 1
Assert-Condition ($browserReachable.status -eq 'Pass') 'Browser extension reachability should pass after heartbeat.'

Write-Host 'Build 23 smoke: running registered artifact action...'
$artifactResult = Invoke-ThreadlineJson -Method Post -Path '/actions/artifact.summary/run' -Body @{
  transcript = 'Build 23 smoke created a release confidence summary artifact.'
  contextSummary = 'Smoke test is exercising registered action execution.'
}
Assert-Condition ($artifactResult.status -eq 'Succeeded') 'POST /actions/artifact.summary/run should succeed.'
Assert-Condition ($artifactResult.artifact.id -like 'art_*') 'Artifact action should return an artifact id.'

Write-Host ''
Write-Host 'Build 23 smoke test passed.' -ForegroundColor Green
Write-Host 'Covered: health, doctor, capabilities, actions, session bootstrap, context storage, prompt composition, browser extension heartbeat, artifact action.'
