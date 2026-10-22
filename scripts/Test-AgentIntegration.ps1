param([string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $DotnetPath) { $DotnetPath = Join-Path $repo '.tools\dotnet\dotnet.exe' }
$appDll = Join-Path $repo 'src\TokenArtTagger.App\bin\Debug\net10.0-windows\TokenArtTagger.App.dll'
$bridgeDll = Join-Path $repo 'src\TokenArtTagger.Agent\bin\Debug\net10.0\TokenArtTagger.Agent.dll'
$testRoot = Join-Path $repo ('.cache\agent-smoke\' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot ('images-' + [char]0x56FE)
$sorted = Join-Path $fixture 'Sorted'
$state = Join-Path $testRoot 'state'
New-Item -ItemType Directory -Force -Path $fixture,$sorted,$state | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 8,6
try {
    $bitmap.SetPixel(2,2,[System.Drawing.Color]::Red)
    foreach ($name in @('a.png','b.png','Sorted\female-caster-bard-human.png')) { $bitmap.Save((Join-Path $fixture $name), [System.Drawing.Imaging.ImageFormat]::Png) }
} finally { $bitmap.Dispose() }
$originalHashes = @(Get-ChildItem -LiteralPath $fixture -File -Recurse | Get-FileHash | Select-Object Path,Hash)
$savedLocal = $env:LOCALAPPDATA
$savedRoaming = $env:APPDATA
$savedData = $env:TOKENARTTAGGER_DATA_DIR
$app = $null
$bridge = $null
try {
    $env:LOCALAPPDATA = $state
    $env:APPDATA = $state
    $env:TOKENARTTAGGER_DATA_DIR = $state
    $app = Start-Process -FilePath $DotnetPath -ArgumentList @(('"' + $appDll + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'app-out.txt') -RedirectStandardError (Join-Path $testRoot 'app-error.txt')
    $registration = Join-Path $state ('Agent\instances\' + $app.Id + '.json')
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $registration)) {
        if ($app.HasExited) { throw ('App exited: ' + (Get-Content (Join-Path $testRoot 'app-error.txt') -Raw)) }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Timed out waiting for app registration.' }
        Start-Sleep -Milliseconds 100
    }
    $bridge = New-Object System.Diagnostics.Process
    $bridge.StartInfo.FileName = $DotnetPath
    $bridge.StartInfo.Arguments = '"' + $bridgeDll + '" --instance ' + $app.Id
    $bridge.StartInfo.UseShellExecute = $false
    $bridge.StartInfo.CreateNoWindow = $true
    $bridge.StartInfo.RedirectStandardInput = $true
    $bridge.StartInfo.RedirectStandardOutput = $true
    $bridge.StartInfo.RedirectStandardError = $true
    [void]$bridge.Start()
    $script:rpcId = 0
    function SendLine([string]$line) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($line + "`n")
        $bridge.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $bridge.StandardInput.BaseStream.Flush()
    }
    function Rpc($method, $parameters) {
        $script:rpcId++
        SendLine (@{ jsonrpc = '2.0'; id = $script:rpcId; method = $method; params = $parameters } | ConvertTo-Json -Depth 30 -Compress)
        $read = $bridge.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(20000)) { throw "Timed out: $method" }
        if (-not $read.Result) { throw 'Bridge stopped responding.' }
        $response = $read.Result | ConvertFrom-Json
        if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
        return $response.result
    }
    function Call($name, $arguments, $expectedError) {
        $result = Rpc 'tools/call' @{ name = $name; arguments = $arguments }
        if ($expectedError) {
            if (-not $result.isError -or $result.structuredContent.code -ne $expectedError) { throw "Expected $expectedError from $name" }
        } elseif ($result.isError) { throw ($result | ConvertTo-Json -Depth 10 -Compress) }
        return $result
    }
    function Check($condition, $message) { if (-not $condition) { throw $message } }
    $init = Rpc 'initialize' @{ protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'integration-test'; version = '1' } }
    Check ($init.protocolVersion -eq '2025-11-25') 'Protocol negotiation failed.'
    SendLine '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    $tools = Rpc 'tools/list' @{}
    Check ($tools.tools.Count -eq 11) 'Unexpected tool count.'
    [void](Call 'tag_schema' @{})
    [void](Call 'agent_instances' @{})
    $status = (Call 'app_status' @{}).structuredContent
    Check ($status.count -eq 0) 'Test app did not start empty.'
    $launcher = Join-Path $PSScriptRoot 'Start-Agent.ps1'
    $cliStatus = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher -Instance $app.Id -Call app_status | ConvertFrom-Json
    Check (-not $cliStatus.isError -and $cliStatus.structuredContent.count -eq 0) 'One-shot CLI without argument file failed.'
    $scanned = (Call 'scan_images' @{ root = $fixture; excludeDirectories = @($sorted); expectedWorkspaceRevision = $status.workspaceRevision }).structuredContent
    Check ($scanned.count -eq 2) 'Nested-folder exclusion failed.'
    $list = (Call 'list_images' @{ limit = 100 }).structuredContent
    $argumentFile = Join-Path $testRoot 'list-request.json'
    @{ folder = $fixture; recursive = $false } | ConvertTo-Json | Set-Content -LiteralPath $argumentFile -Encoding UTF8
    $cliList = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher -Instance $app.Id -Call list_images -ArgumentFile $argumentFile | ConvertFrom-Json
    Check (-not $cliList.isError -and $cliList.structuredContent.total -eq 2) 'One-shot CLI did not preserve Unicode folder paths.'
    $a = $list.images[0]; $b = $list.images[1]
    $tags = @{ gender = 'female'; role = 'range'; weaponOrStyle = 'bow'; race = 'elf' }
    $badTags = @{ gender = 'male'; role = 'range'; weaponOrStyle = 'wizard'; race = 'human' }
    [void](Call 'update_tags' @{ changes = @(@{ id = $a.id; expectedRevision = $a.revision; tags = $tags }, @{ id = $b.id; expectedRevision = $b.revision; tags = $badTags }) } 'invalid_tags')
    Check (((Call 'app_status' @{}).structuredContent.changed) -eq 0) 'Invalid batch partly applied.'
    $changed = (Call 'update_tags' @{ changes = @(@{ id = $a.id; expectedRevision = $a.revision; tags = $tags }) }).structuredContent.images[0]
    [void](Call 'update_tags' @{ changes = @(@{ id = $b.id; expectedRevision = $b.revision; tags = $tags }, @{ id = $a.id; expectedRevision = $a.revision; tags = $tags }) } 'stale_image')
    Check (((Call 'app_status' @{}).structuredContent.changed) -eq 1) 'Stale batch partly applied.'
    [void](Call 'list_images' @{ expectedWorkspaceRevision = $list.workspaceRevision } 'stale_workspace')
    $picture = Call 'view_image' @{ id = $a.id; expectedRevision = $changed.revision; maxDimension = 256 }
    Check ($picture.content[1].type -eq 'image' -and $picture.content[1].mimeType -eq 'image/png') 'Native MCP image missing.'
    Check ($picture.structuredContent.width -eq 8 -and $picture.structuredContent.height -eq 6) 'Image dimensions changed unexpectedly.'
    $detail = (Call 'image_details' @{ id = $a.id; includeHash = $true }).structuredContent
    Check ($detail.sha256 -eq (Get-FileHash -LiteralPath $a.path -Algorithm SHA256).Hash.ToLowerInvariant()) 'Content hash mismatch.'
    $target = @{ id = $a.id; expectedRevision = $changed.revision }
    [void](Call 'select_images' @{ targets = @($target) })
    Check (((Call 'app_status' @{}).structuredContent.selectedIds[0]) -eq $a.id) 'Desktop selection was not updated.'
    $preview = (Call 'preview_rename' @{ targets = @($target) }).structuredContent
    Check ($preview.images[0].proposedFilename -like 'female-range-bow-elf__*.png') 'Rename preview was not populated.'
    $status = (Call 'app_status' @{}).structuredContent
    [void](Call 'scan_images' @{ root = $sorted; expectedWorkspaceRevision = $status.workspaceRevision })
    $status = (Call 'app_status' @{}).structuredContent
    [void](Call 'scan_images' @{ root = $fixture; excludeDirectories = @($sorted); expectedWorkspaceRevision = $status.workspaceRevision })
    $returned = (Call 'list_images' @{ state = 'changed' }).structuredContent
    Check ($returned.total -eq 1 -and $returned.images[0].currentTags.weaponOrStyle -eq 'bow') 'Tags did not survive scanning another folder.'
    $target = @{ id = $returned.images[0].id; expectedRevision = $returned.images[0].revision }
    [void](Call 'reset_tags' @{ targets = @($target) })
    Check (((Call 'app_status' @{}).structuredContent.changed) -eq 0) 'Reset did not restore filename tags.'
    $newHashes = @(Get-ChildItem -LiteralPath $fixture -File -Recurse | Get-FileHash | Select-Object Path,Hash)
    Check (-not (Compare-Object $originalHashes $newHashes -Property Path,Hash)) 'Source images were renamed or changed.'
    'PASS: MCP handshake/discovery; all 11 tools; real desktop IPC; CLI and Unicode paths; nested exclusion; atomic validation; stale revisions; PNG pixels; SHA-256; selection; rename preview; rescan persistence; reset; original files unchanged.'
    "Test artifacts: $testRoot"
} finally {
    if ($bridge) { if (-not $bridge.HasExited) { $bridge.StandardInput.Close(); if (-not $bridge.WaitForExit(3000)) { $bridge.Kill() } }; $bridge.Dispose() }
    if ($app) { if (-not $app.HasExited) { [void]$app.CloseMainWindow(); if (-not $app.WaitForExit(3000)) { $app.Kill() } }; $app.Dispose() }
    $env:LOCALAPPDATA = $savedLocal
    $env:APPDATA = $savedRoaming
    $env:TOKENARTTAGGER_DATA_DIR = $savedData
}
