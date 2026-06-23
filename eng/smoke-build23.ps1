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

function Get-ThreadlineItems {
  param(
    [AllowNull()] [object] $Response,
    [string] $CollectionPropertyName = ''
  )

  if ($null -eq $Response) {
    return @()
  }

  if (-not [string]::IsNullOrWhiteSpace($CollectionPropertyName) -and
      $Response.PSObject.Properties.Name -contains $CollectionPropertyName) {
    return @($Response.$CollectionPropertyName)
  }

  return @($Response)
}

function Test-ThreadlineItemId {
  param(
    [AllowNull()] [object] $Items,
    [Parameter(Mandatory = $true)] [string] $Id
  )

  foreach ($item in @($Items)) {
    if ($null -eq $item) {
      continue
    }

    $value = $null
    if ($item.PSObject.Properties.Name -contains 'id') {
      $value = $item.id
    } elseif ($item.PSObject.Properties.Name -contains 'Id') {
      $value = $item.Id
    }

    if ($value -eq $Id) {
      return $true
    }
  }

  return $false
}

function Format-ThreadlineItemIds {
  param(
    [AllowNull()] [object] $Items
  )

  $ids = foreach ($item in @($Items)) {
    if ($null -eq $item) {
      continue
    }

    if ($item.PSObject.Properties.Name -contains 'id') {
      $item.id
    } elseif ($item.PSObject.Properties.Name -contains 'Id') {
      $item.Id
    }
  }

  if ($null -eq $ids -or @($ids).Count -eq 0) {
    return '<none>'
  }

  return (@($ids) -join ', ')
}

Write-Host "Build 23 smoke: checking Threadline service at $BaseUrl..."
$health = Invoke-ThreadlineJson -Method Get -Path '/health'
Assert-Condition ($health.status -eq 'ok') 'GET /health should return status ok.'
Assert-Condition ($health.service -eq 'Threadline.Service') 'GET /health should identify Threadline.Service.'

Write-Host 'Build 23 smoke: checking reliability contracts...'
$actionsResponse = Invoke-ThreadlineJson -Method Get -Path '/actions'
$actions = Get-ThreadlineItems -Response $actionsResponse -CollectionPropertyName 'actions'
$actionIds = Format-ThreadlineItemIds -Items $actions
Assert-Condition (Test-ThreadlineItemId -Items $actions -Id 'provider.test') "GET /actions should include provider.test. Saw: $actionIds"
Assert-Condition (Test-ThreadlineItemId -Items $actions -Id 'artifact.summary') "GET /actions should include artifact.summary. Saw: $actionIds"

$capabilitiesResponse = Invoke-ThreadlineJson -Method Get -Path '/capabilities'
$capabilities = Get-ThreadlineItems -Response $capabilitiesResponse -CollectionPropertyName 'capabilities'
$capabilityIds = Format-ThreadlineItemIds -Items $capabilities
Assert-Condition (Test-ThreadlineItemId -Items $capabilities -Id 'browser-extension.bridge') "GET /capabilities should include browser-extension.bridge. Saw: $capabilityIds"

$doctorBefore = Invoke-ThreadlineJson -Method Get -Path '/doctor'
$checks = Get-ThreadlineItems -Response $doctorBefore.checks
$checkIds = Format-ThreadlineItemIds -Items $checks
Assert-Condition (Test-ThreadlineItemId -Items $checks -Id 'sqlite.writable') "GET /doctor should include sqlite.writable. Saw: $checkIds"
Assert-Condition (Test-ThreadlineItemId -Items $checks -Id 'browser-extension.reachable') "GET /doctor should include browser-extension.reachable. Saw: $checkIds"

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
$browserReachable = Get-ThreadlineItems -Response $doctorAfter.checks | Where-Object { $_.id -eq 'browser-extension.reachable' -or $_.Id -eq 'browser-extension.reachable' } | Select-Object -First 1
Assert-Condition ($browserReachable.status -eq 'Pass' -or $browserReachable.Status -eq 'Pass') 'Browser extension reachability should pass after heartbeat.'

Write-Host 'Build 23 smoke: running registered artifact action...'
$artifactResult = Invoke-ThreadlineJson -Method Post -Path '/actions/artifact.summary/run' -Body @{
  transcript = 'Build 23 smoke created a release confidence summary artifact.'
  contextSummary = 'Smoke test is exercising registered action execution.'
}
Assert-Condition ($artifactResult.status -eq 'Succeeded' -or $artifactResult.Status -eq 'Succeeded') 'POST /actions/artifact.summary/run should succeed.'
Assert-Condition ($artifactResult.artifact.id -like 'art_*' -or $artifactResult.Artifact.Id -like 'art_*') 'Artifact action should return an artifact id.'

Write-Host ''
Write-Host 'Build 23 smoke test passed.' -ForegroundColor Green
Write-Host 'Covered: health, doctor, capabilities, actions, session bootstrap, context storage, prompt composition, browser extension heartbeat, artifact action.'
