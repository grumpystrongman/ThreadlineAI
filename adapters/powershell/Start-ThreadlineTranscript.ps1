param(
    [Parameter(Mandatory = $true)] [string] $SessionId,
    [string] $ThreadlineRoot = "$env:LOCALAPPDATA\ThreadlineAI"
)
$sessionPath = Join-Path $ThreadlineRoot "sessions\$SessionId"
New-Item -Path $sessionPath -ItemType Directory -Force | Out-Null
$transcriptPath = Join-Path $sessionPath "powershell-transcript.txt"
Start-Transcript -Path $transcriptPath -Append
Write-Host "ThreadlineAI transcript started: $transcriptPath"
