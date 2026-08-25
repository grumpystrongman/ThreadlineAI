[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipLaunch,
    [switch]$RequireBrowserAgent,
    [switch]$RequireJarvis
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$browserMcpDll = Join-Path $repoRoot 'src\Threadline.BrowserMcp\bin\Release\net8.0\Threadline.BrowserMcp.dll'
$privilegedMcpDll = Join-Path $repoRoot 'src\Threadline.PrivilegedMcp\bin\Release\net8.0-windows10.0.19041.0\Threadline.PrivilegedMcp.dll'
$jarvisConfig = Join-Path $env:LOCALAPPDATA 'ThreadlineAI\jarvis\mcp.json'

function Invoke-McpExchange {
    param(
        [Parameter(Mandatory = $true)] [string]$Dll,
        [Parameter(Mandatory = $true)] [hashtable]$Request
    )

    if (-not (Test-Path $Dll)) {
        throw "MCP build output was not found: $Dll"
    }

    $initialize = @{
        jsonrpc = '2.0'; id = 1; method = 'initialize';
        params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'threadline-stack-smoke'; version = '1.0' } }
    } | ConvertTo-Json -Compress -Depth 30
    $initialized = @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } | ConvertTo-Json -Compress -Depth 30
    $requestJson = $Request | ConvertTo-Json -Compress -Depth 30

    $output = @(@($initialize, $initialized, $requestJson) | & dotnet $Dll)
    if ($LASTEXITCODE -ne 0) {
        throw "MCP process exited with code $LASTEXITCODE: $Dll"
    }

    $expectedId = $Request.id
    $response = $output |
        ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.id -eq $expectedId } |
        Select-Object -Last 1

    if ($null -eq $response) {
        throw "MCP server returned no response for id $expectedId: $Dll"
    }
    if ($response.error) {
        throw "MCP JSON-RPC error: $($response.error.message)"
    }
    return $response.result
}

function Get-McpTools {
    param([Parameter(Mandatory = $true)] [string]$Dll)
    $result = Invoke-McpExchange -Dll $Dll -Request @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} }
    return @($result.tools)
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory = $true)] [string]$Dll,
        [Parameter(Mandatory = $true)] [string]$Name,
        [hashtable]$Arguments = @{}
    )
    return Invoke-McpExchange -Dll $Dll -Request @{
        jsonrpc = '2.0'; id = 3; method = 'tools/call';
        params = @{ name = $Name; arguments = $Arguments }
    }
}

Write-Host 'AIKA / JARVIS full local stack smoke' -ForegroundColor Cyan
Write-Host 'Protected tools are inventoried only; this smoke never auto-approves or invokes an elevated mutation.'

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-windows.ps1')
}

if (-not $SkipLaunch) {
    & (Join-Path $PSScriptRoot 'run-windows.ps1') -SkipBuild
}

# Real signed-in desktop test: native window discovery/UIA/input/verification.
& (Join-Path $PSScriptRoot 'test-device-agent.ps1') -SkipBuild -SkipLaunch

$browserTools = Get-McpTools -Dll $browserMcpDll
$browserNames = @($browserTools | ForEach-Object { $_.name })
$requiredBrowser = @(
    'browser_state','browser_navigate','browser_new_tab','browser_activate_tab','browser_close_tab',
    'browser_inspect_dom','browser_click','browser_fill','browser_read_text','browser_scroll'
)
foreach ($name in $requiredBrowser) {
    if ($browserNames -notcontains $name) { throw "Browser MCP is missing required tool '$name'." }
}
Write-Host "Browser MCP surface: $($browserNames -join ', ')" -ForegroundColor Green

$privilegedTools = Get-McpTools -Dll $privilegedMcpDll
$privilegedNames = @($privilegedTools | ForEach-Object { $_.name })
$requiredPrivileged = @(
    'protected_install_package','protected_service_start','protected_service_stop',
    'protected_service_restart','protected_registry_set_hklm_software'
)
foreach ($tool in $privilegedTools) {
    if ($tool.annotations.destructiveHint -ne $true) {
        throw "Protected MCP tool '$($tool.name)' does not advertise destructiveHint=true."
    }
}
foreach ($name in $requiredPrivileged) {
    if ($privilegedNames -notcontains $name) { throw "Privileged MCP is missing required tool '$name'." }
}
Write-Host "Privileged MCP surface and destructive annotations: verified ($($privilegedNames -join ', '))" -ForegroundColor Green

if (Test-Path $jarvisConfig) {
    $config = Get-Content -Raw $jarvisConfig | ConvertFrom-Json
    $servers = @($config.mcpServers.PSObject.Properties.Name)
    foreach ($required in @('threadline-windows-device','threadline-browser','threadline-privileged')) {
        if ($servers -notcontains $required) {
            throw "Jarvis MCP config is missing '$required'. Re-run eng/bootstrap-personal-jarvis.ps1."
        }
    }
    Write-Host 'Installed Jarvis MCP config contains Device, Browser, and Privileged agents.' -ForegroundColor Green
}
else {
    Write-Host 'Jarvis MCP config is not installed yet; bootstrap installation check skipped.' -ForegroundColor Yellow
}

try {
    $browserState = Invoke-McpTool -Dll $browserMcpDll -Name 'browser_state'
    if ($browserState.isError -eq $true) {
        throw (@($browserState.content | ForEach-Object { $_.text }) -join ' ')
    }
    Write-Host 'Live browser agent: connected.' -ForegroundColor Green
}
catch {
    if ($RequireBrowserAgent) { throw }
    Write-Host "Live browser agent not required for this run: $($_.Exception.Message)" -ForegroundColor Yellow
}

try {
    $jarvisResponse = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:47821/api/missions?limit=1' -TimeoutSec 3
    if (-not $jarvisResponse.StatusCode -or $jarvisResponse.StatusCode -lt 200 -or $jarvisResponse.StatusCode -ge 300) {
        throw "Jarvis returned HTTP $($jarvisResponse.StatusCode)."
    }
    Write-Host 'PersonalJarvis Mission API: ready.' -ForegroundColor Green
}
catch {
    if ($RequireJarvis) { throw }
    Write-Host "PersonalJarvis readiness not required for this run: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'AIKA / JARVIS local stack smoke PASSED.' -ForegroundColor Green
Write-Host 'For a strict installed-runtime check, rerun with -RequireBrowserAgent -RequireJarvis.'
