param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$SuggestionsDirectory,
    [Parameter(Mandatory=$true)][string]$SourceBenchmarkDirectory,
    [Parameter(Mandatory=$true)][string]$Model,
    [ValidateSet('Debug','Release')][string]$Configuration='Debug'
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$SuggestionsDirectory=[IO.Path]::GetFullPath($SuggestionsDirectory)
$SourceBenchmarkDirectory=[IO.Path]::GetFullPath($SourceBenchmarkDirectory)
foreach($path in @($SuggestionsDirectory,$SourceBenchmarkDirectory)){
    if(-not $path.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Suggestion imports require private .cache artifacts.'}
}
$client=$null
$lock=[IO.File]::Open((Join-Path $SuggestionsDirectory 'import.lock'),'OpenOrCreate','ReadWrite','None')
try{
    $saved=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $SuggestionsDirectory 'suggestions.json') | ConvertFrom-Json
    $settings=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $SuggestionsDirectory 'settings.json') | ConvertFrom-Json
    $state=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $SuggestionsDirectory 'status.json') | ConvertFrom-Json
    $source=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $SourceBenchmarkDirectory 'manifest.json') | ConvertFrom-Json
    $sourceState=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $SourceBenchmarkDirectory 'status.json') | ConvertFrom-Json
    if($state.status -ne 'complete' -or -not $state.cleanupComplete -or $sourceState.status -ne 'complete' -or -not $sourceState.cleanupComplete -or
        $sourceState.fingerprint -ne $source.fingerprint -or $saved.fingerprint -cnotmatch '^[a-f0-9]{64}$' -or
        $saved.fingerprint -ne $settings.fingerprint -or $saved.fingerprint -ne $state.fingerprint -or
        $saved.automaticAcceptance -ne $false -or $saved.scoresCalibrated -ne $false -or $saved.finalTestEvaluated -ne $false){throw 'Expected completed review-only suggestions and source benchmark.'}
    if(@($saved.items).Count -lt 1 -or @($saved.items).Count -gt 100){throw 'Import between one and 100 suggestions at a time.'}
    $sources=@{};$seen=@{}
    foreach($entry in $source.items | Where-Object {-not $_.hasGroundTruth}){
        if($entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $sources.ContainsKey($entry.sha256)){throw 'Unlabeled source identities are invalid or ambiguous.'}
        $sources[$entry.sha256]=$entry
    }
    $client=Start-LocalAiMcp $repo $Instance
    $status=(Invoke-LocalAiTool $client 'app_status').structuredContent
    $null=Invoke-LocalAiTool $client 'tag_schema'
    if($status.root -ne $source.root){throw 'Scan the source library before importing.'}
    $catalog=@{};$offset=0
    do{
        $page=(Invoke-LocalAiTool $client 'list_images' @{state='untagged';offset=$offset;limit=100;expectedWorkspaceRevision=$status.workspaceRevision}).structuredContent
        foreach($item in $page.images){$catalog[$item.path]=$item}
        $offset=$page.nextOffset
    }while($null -ne $offset)
    $requests=@(foreach($row in $saved.items){
        if($seen.ContainsKey($row.sha256) -or -not $sources.ContainsKey($row.sha256) -or $row.reviewRequired -ne $true -or
            $row.automaticAcceptance -ne $false -or $row.scoresCalibrated -ne $false){throw 'Suggestion targets must be unique, previously untagged, review-only sources.'}
        $seen[$row.sha256]=$true
        $entry=$sources[$row.sha256];$item=$catalog[$entry.path]
        if(-not $item){throw 'Source disappeared or now has authoritative filename tags; inspect it before importing.'}
        $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        if($detail.sha256 -ne $row.sha256 -or $detail.image.revision -ne $item.revision){throw 'Source changed since prediction; do not import stale suggestions.'}
        @{id=$item.id;expectedRevision=$item.revision;sha256=$row.sha256;tags=$row.prediction;provider='local-encoder-probe';model=$Model;runFingerprint=$saved.fingerprint;evidence='Saved classifier suggestion; scores are uncalibrated and review is required.'}
    })
    Stop-LocalAiMcp $client;$client=$null
    $requestPath=Join-Path $SuggestionsDirectory 'queue-request.json'
    Write-AiCheckpoint @{suggestions=$requests} $requestPath
    # Keep the benchmark client's read-only allowlist intact; use the normal application command interface for this explicit write.
    $result=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Start-Agent.ps1') -Configuration $Configuration -Instance $Instance -Call queue_ai_suggestions -ArgumentFile $requestPath
    if($LASTEXITCODE -ne 0){throw 'Queue import failed. Inspect live queue state before retrying.'}
    $result=($result -join "`n") | ConvertFrom-Json
    if($result.isError -or -not $result.structuredContent.persisted -or -not $result.structuredContent.reviewRequired -or $result.structuredContent.automaticAcceptance){throw 'Queue did not confirm review-only persistence.'}
    Write-AiCheckpoint @{importedUtc=[DateTime]::UtcNow.ToString('o');fingerprint=$saved.fingerprint;result=$result.structuredContent;inference=$false} (Join-Path $SuggestionsDirectory 'queue-import.json')
    Write-Output ('Queued '+$result.structuredContent.imported+' proposals; '+$result.structuredContent.alreadyPresent+' already present. No tags were applied.')
}finally{
    try{if($client){Stop-LocalAiMcp $client}}finally{$lock.Dispose()}
}
