param(
    [string]$RuntimePath,
    [string]$ModelDirectory,
    [ValidateRange(1024,65535)][int]$Port = 11435
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $RuntimePath) { $RuntimePath = Join-Path $repo '.tools\ollama\0.40.8\ollama.exe' }
if (-not $ModelDirectory) { $ModelDirectory = Join-Path $repo '.cache\models\ollama' }
$RuntimePath = [IO.Path]::GetFullPath($RuntimePath)
$ModelDirectory = [IO.Path]::GetFullPath($ModelDirectory)
$model = 'qwen3-vl:2b-instruct-q4_K_M'
$expectedManifest = 'afc393e3572c07db0bc84aa460d928a16c75f3fbb9ca9e0dccc4fbd2dafb573e'
$manifestPath = Join-Path $ModelDirectory 'manifests\registry.ollama.ai\library\qwen3-vl\2b-instruct-q4_K_M'
if (-not (Test-Path -LiteralPath $RuntimePath -PathType Leaf)) { throw 'Runtime is missing. Follow docs/local-ai-runtime-setup.md; this check never downloads files.' }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'The pinned local model manifest is missing.' }
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedManifest) { throw 'Cached model manifest differs from the verified revision.' }
$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
$blobCount = 0
foreach ($blob in (@($manifest.config) + @($manifest.layers))) {
    if ($blob.digest -notmatch '^sha256:[0-9a-f]{64}$' -or [long]$blob.size -lt 1) { throw 'Invalid manifest blob descriptor.' }
    $digest = $blob.digest.Substring(7)
    $blobPath = Join-Path $ModelDirectory ('blobs\sha256-' + $digest)
    if (-not (Test-Path -LiteralPath $blobPath -PathType Leaf)) { throw 'A required model blob is missing; no automatic pull will be attempted.' }
    if ((Get-Item -LiteralPath $blobPath).Length -ne [long]$blob.size -or (Get-FileHash -LiteralPath $blobPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) { throw 'A cached model blob failed size or SHA-256 verification.' }
    $blobCount++
}
Write-Output "Verified $blobCount cached model blobs. No model download required."

$listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if (@($listeners | Where-Object Port -eq $Port).Count) { throw "Port $Port is in use. Choose another port; existing services will not be touched." }
$runRoot = Join-Path $repo ('.cache\runtime-checks\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$profileRoot = Join-Path $runRoot 'profile'
$roaming = Join-Path $profileRoot 'AppData\Roaming'
$local = Join-Path $profileRoot 'AppData\Local'
New-Item -ItemType Directory -Force -Path $roaming,$local | Out-Null
$reportPath = Join-Path $runRoot 'result.json'
$report = [ordered]@{
    status = 'starting'
    startedUtc = [DateTime]::UtcNow.ToString('o')
    runtimePath = $RuntimePath
    model = $model
    manifestSha256 = $expectedManifest
    blobsVerified = $blobCount
    endpoint = "http://127.0.0.1:$Port"
    modelLoaded = $false
    inferenceTested = $false
    cleanupComplete = $false
}
function Save-Report { $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8 }
Save-Report
$environment = @{
    OLLAMA_HOST = "127.0.0.1:$Port"
    OLLAMA_MODELS = $ModelDirectory
    OLLAMA_NO_CLOUD = '1'
    OLLAMA_NUM_PARALLEL = '1'
    OLLAMA_MAX_LOADED_MODELS = '1'
    OLLAMA_MAX_QUEUE = '1'
    OLLAMA_CONTEXT_LENGTH = '4096'
    OLLAMA_KEEP_ALIVE = '0'
    USERPROFILE = $profileRoot
    APPDATA = $roaming
    LOCALAPPDATA = $local
}
$savedEnvironment = @{}
$server = $null
$client = $null
$handler = $null
try {
    # These variables exist only in this script process and its child, not in user settings.
    try {
        foreach ($name in $environment.Keys) {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
        }
        $server = Start-Process -FilePath $RuntimePath -ArgumentList 'serve' -WorkingDirectory (Split-Path $RuntimePath -Parent) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'server-out.log') -RedirectStandardError (Join-Path $runRoot 'server-error.log')
        $null = $server.Handle
    } finally {
        foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    }
    $server.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
    $report.serverProcessId = $server.Id
    Save-Report
    Add-Type -AssemblyName System.Net.Http
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.UseProxy = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    function Call-LocalApi([string]$Path, $Body = $null) {
        $uri = "http://127.0.0.1:$Port$Path"
        $content = $null
        $response = $null
        try {
            if ($null -eq $Body) {
                $response = $client.GetAsync($uri).GetAwaiter().GetResult()
            } else {
                $content = New-Object Net.Http.StringContent(($Body | ConvertTo-Json -Depth 8 -Compress), [Text.Encoding]::UTF8, 'application/json')
                $response = $client.PostAsync($uri,$content).GetAwaiter().GetResult()
            }
            if (-not $response.IsSuccessStatusCode) { throw "Local API $Path returned HTTP $([int]$response.StatusCode)." }
            return ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json)
        } finally {
            if ($response) { $response.Dispose() }
            if ($content) { $content.Dispose() }
        }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $version = $null
    while (-not $version) {
        if ($server.HasExited) { throw "Runtime exited before becoming ready; inspect private logs in $runRoot." }
        try { $version = Call-LocalApi '/api/version' } catch {
            if ([DateTime]::UtcNow -ge $deadline) { throw 'Local runtime did not become ready within 30 seconds.' }
            Start-Sleep -Milliseconds 250
        }
    }
    $ownedListeners = @(Get-NetTCPConnection -OwningProcess $server.Id -State Listen -ErrorAction Stop)
    if ($ownedListeners.Count -ne 1 -or $ownedListeners[0].LocalAddress -ne '127.0.0.1' -or $ownedListeners[0].LocalPort -ne $Port) { throw 'Runtime is not bound exclusively to the requested loopback endpoint.' }
    $tags = Call-LocalApi '/api/tags'
    $cached = @($tags.models | Where-Object name -eq $model)
    if ($cached.Count -ne 1 -or $cached[0].digest -ne $expectedManifest) { throw 'Runtime did not discover the expected cached model revision.' }
    $details = Call-LocalApi '/api/show' @{model=$model}
    if ('vision' -notin $details.capabilities) { throw 'Runtime does not report vision support for this model.' }
    $loaded = Call-LocalApi '/api/ps'
    if (@($loaded.models).Count -ne 0) { throw 'Discovery unexpectedly loaded a model.' }
    $report.runtimeVersion = $version.version
    $report.capabilities = @($details.capabilities)
    $report.loopbackOnly = $true
    $report.cloudDisabledConfigured = $true
    $report.loadedModelCount = 0
    $report.serverWorkingSetMiB = [math]::Round($server.WorkingSet64/1MB,1)
    $report.status = 'passed'
    Save-Report
    Write-Output "PASS: Ollama $($version.version), loopback only, pinned cached model found with vision capability. No inference performed."
} catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
    Save-Report
    throw
} finally {
    if ($client) { $client.Dispose() }
    if ($handler) { $handler.Dispose() }
    if ($server) {
        if (-not $server.HasExited) { $server.Kill(); [void]$server.WaitForExit(5000) }
        $report.cleanupComplete = $server.HasExited
        $server.Dispose()
    } else { $report.cleanupComplete = $true }
    $report.completedUtc = [DateTime]::UtcNow.ToString('o')
    Save-Report
    Write-Output "Checkpoint: $reportPath"
}
