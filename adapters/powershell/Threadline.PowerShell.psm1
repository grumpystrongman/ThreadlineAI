Set-StrictMode -Version Latest

$script:ThreadlineBaseUrl = 'http://localhost:5057'
$script:ThreadlineLocalAccessToken = $null
$script:ThreadlineTranscriptPath = $null
$script:ThreadlineLastTranscriptLength = 0
$script:ThreadlineLastActionId = $null

function Set-ThreadlineService {
    [CmdletBinding()]
    param(
        [string] $BaseUrl = 'http://localhost:5057',
        [string] $LocalAccessToken
    )

    $script:ThreadlineBaseUrl = $BaseUrl.TrimEnd('/')
    $script:ThreadlineLocalAccessToken = if ([string]::IsNullOrWhiteSpace($LocalAccessToken)) { $null } else { $LocalAccessToken }
}

function Get-ThreadlineHeaders {
    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($script:ThreadlineLocalAccessToken)) {
        $headers['X-Threadline-Token'] = $script:ThreadlineLocalAccessToken
    }
    return $headers
}

function Invoke-ThreadlineJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateSet('Get','Post','Delete')] [string] $Method,
        [Parameter(Mandatory)] [string] $Path,
        [object] $Body
    )

    $uri = "$($script:ThreadlineBaseUrl)$Path"
    $headers = Get-ThreadlineHeaders
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }

    $json = $Body | ConvertTo-Json -Depth 8
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' -Body $json
}

function Get-ThreadlinePowerShellExecutable {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -ne $pwsh) {
        return $pwsh.Source
    }

    $windowsPowerShell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($null -ne $windowsPowerShell) {
        return $windowsPowerShell.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($PSHOME)) {
        foreach ($candidateName in @('pwsh.exe', 'powershell.exe')) {
            $candidate = Join-Path $PSHOME $candidateName
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    throw 'No PowerShell executable was found. Install PowerShell 7 or ensure Windows PowerShell is available on PATH.'
}

function Get-ThreadlineActiveSession {
    [CmdletBinding()]
    param()

    Invoke-ThreadlineJson -Method Get -Path '/sessions/active'
}

function Start-ThreadlineSession {
    [CmdletBinding()]
    param(
        [string] $Name = "PowerShell session $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
        [string] $Provider = 'Local'
    )

    Invoke-ThreadlineJson -Method Post -Path '/sessions' -Body @{ name = $Name; provider = $Provider }
}

function Register-ThreadlinePowerShellAdapter {
    [CmdletBinding()]
    param()

    Invoke-ThreadlineJson -Method Post -Path '/adapters' -Body @{
        kind = 'PowerShell'
        displayName = 'Threadline PowerShell Adapter'
        permissions = 'All'
        version = '0.1.0'
        metadata = @{ shell = $PSVersionTable.PSEdition; version = $PSVersionTable.PSVersion.ToString() }
    }
}

function Start-ThreadlineTranscriptCapture {
    [CmdletBinding()]
    param(
        [string] $Path = (Join-Path $env:TEMP "threadline-transcript-$([Guid]::NewGuid().ToString('N')).txt")
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Start-Transcript -Path $Path -Append | Out-Null
    $script:ThreadlineTranscriptPath = $Path
    $script:ThreadlineLastTranscriptLength = 0
    Write-Host "Threadline transcript capture started: $Path"
}

function Stop-ThreadlineTranscriptCapture {
    [CmdletBinding()]
    param()

    Stop-Transcript | Out-Null
    Write-Host 'Threadline transcript capture stopped.'
}

function Send-ThreadlineTerminalContext {
    [CmdletBinding()]
    param(
        [string] $SessionId,
        [string] $Content,
        [string] $ContextType = 'terminal-note',
        [switch] $PreviewOnly
    )

    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        $SessionId = (Get-ThreadlineActiveSession).id
    }

    if ([string]::IsNullOrWhiteSpace($Content)) {
        throw 'Content is required.'
    }

    $body = @{
        source = 'PowerShell'
        contextType = $ContextType
        content = $Content
        applicationName = 'PowerShell'
        processName = 'pwsh'
        windowTitle = $Host.UI.RawUI.WindowTitle
        userApproved = $true
        metadata = @{ adapter = 'Threadline PowerShell Adapter'; capturedAt = (Get-Date).ToString('o') }
    }

    $preview = Invoke-ThreadlineJson -Method Post -Path "/sessions/$SessionId/events/preview" -Body $body
    if ($PreviewOnly) { return $preview }
    Invoke-ThreadlineJson -Method Post -Path "/sessions/$SessionId/events" -Body $body
}

function Sync-ThreadlineTranscriptCapture {
    [CmdletBinding()]
    param(
        [string] $SessionId,
        [int] $MaxCharacters = 12000,
        [switch] $PreviewOnly
    )

    if ([string]::IsNullOrWhiteSpace($script:ThreadlineTranscriptPath) -or -not (Test-Path $script:ThreadlineTranscriptPath)) {
        throw 'No active Threadline transcript path is available. Run Start-ThreadlineTranscriptCapture first.'
    }

    $content = Get-Content -Path $script:ThreadlineTranscriptPath -Raw
    if ($content.Length -le $script:ThreadlineLastTranscriptLength) {
        Write-Host 'No new transcript content to send.'
        return $null
    }

    $newContent = $content.Substring($script:ThreadlineLastTranscriptLength)
    $script:ThreadlineLastTranscriptLength = $content.Length
    if ($newContent.Length -gt $MaxCharacters) {
        $newContent = $newContent.Substring($newContent.Length - $MaxCharacters)
    }

    Send-ThreadlineTerminalContext -SessionId $SessionId -Content $newContent -ContextType 'terminal-transcript' -PreviewOnly:$PreviewOnly
}

function Invoke-ThreadlineCommandCapture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Command,
        [string] $SessionId,
        [switch] $PreviewOnly
    )

    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        $SessionId = (Get-ThreadlineActiveSession).id
    }

    $started = Get-Date
    $shellExecutable = Get-ThreadlinePowerShellExecutable
    try {
        $output = & $shellExecutable -NoLogo -NoProfile -Command $Command 2>&1 | Out-String
        $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    }
    catch {
        $output = $_ | Out-String
        $exitCode = 1
    }
    $finished = Get-Date

    $content = @"
Command:
$Command

Shell:
$shellExecutable

ExitCode:
$exitCode

Started:
$($started.ToString('o'))

Finished:
$($finished.ToString('o'))

Output:
$output
"@

    Send-ThreadlineTerminalContext -SessionId $SessionId -Content $content -ContextType 'terminal-command-output' -PreviewOnly:$PreviewOnly
}

function New-ThreadlineCommandAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Command,
        [string] $Description = 'Run suggested PowerShell command',
        [string] $SessionId,
        [ValidateSet('Low','Medium','High')] [string] $Risk = 'Medium',
        [switch] $UserApproved
    )

    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        $SessionId = (Get-ThreadlineActiveSession).id
    }

    $action = Invoke-ThreadlineJson -Method Post -Path "/sessions/$SessionId/actions" -Body @{
        kind = 'RunCommand'
        description = $Description
        payload = $Command
        userApproved = [bool] $UserApproved
        risk = $Risk
    }

    $script:ThreadlineLastActionId = $action.id
    $action
}

function Complete-ThreadlineCommandAction {
    [CmdletBinding()]
    param(
        [string] $ActionId = $script:ThreadlineLastActionId,
        [string] $ResultMessage = 'PowerShell command action completed.',
        [switch] $Failed
    )

    if ([string]::IsNullOrWhiteSpace($ActionId)) {
        throw 'ActionId is required or no previous Threadline action is available.'
    }

    Invoke-ThreadlineJson -Method Post -Path "/actions/$ActionId/complete" -Body @{ resultMessage = $ResultMessage; failed = [bool] $Failed }
}

Export-ModuleMember -Function Set-ThreadlineService, Get-ThreadlineActiveSession, Start-ThreadlineSession, Register-ThreadlinePowerShellAdapter, Start-ThreadlineTranscriptCapture, Stop-ThreadlineTranscriptCapture, Send-ThreadlineTerminalContext, Sync-ThreadlineTranscriptCapture, Invoke-ThreadlineCommandCapture, New-ThreadlineCommandAction, Complete-ThreadlineCommandAction