[CmdletBinding(DefaultParameterSetName='Dataset')]
param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true,ParameterSetName='Dataset')][string]$DatasetDirectory,
    [Parameter(Mandatory=$true,ParameterSetName='Benchmark')][string]$UntaggedBenchmarkDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,100000)][int]$MaxItems=100000,
    [ValidateRange(256,1536)][int]$MaxDimension=768
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$sourceDirectory=if($UntaggedBenchmarkDirectory){$UntaggedBenchmarkDirectory}else{$DatasetDirectory}
foreach($directory in @($sourceDirectory,$OutputDirectory)){
    if(-not ([IO.Path]::GetFullPath($directory)).StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Private data must stay in .cache.'}
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory,(Join-Path $OutputDirectory 'images'),(Join-Path $OutputDirectory 'items') | Out-Null
$report=@{status='starting';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');completedThisRun=0;cleanupComplete=$false}
$lock=$null;$client=$null
try{
    $lock=[IO.File]::Open((Join-Path $OutputDirectory 'worker.lock'),'OpenOrCreate','ReadWrite','None')
    if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'STOP')){throw 'paused: STOP marker exists.'}
    $datasetStatus=Get-Content -Raw -Encoding UTF8 (Join-Path $sourceDirectory 'status.json') | ConvertFrom-Json
    if($datasetStatus.status -ne 'complete' -or -not $datasetStatus.cleanupComplete){throw 'Complete the authoritative dataset export first.'}
    if($UntaggedBenchmarkDirectory){
        $manifest=Get-Content -Raw -Encoding UTF8 (Join-Path $sourceDirectory 'manifest.json') | ConvertFrom-Json
        if(-not $manifest.fingerprint -or $manifest.fingerprint -ne $datasetStatus.fingerprint){throw 'Benchmark source identity is inconsistent.'}
        $index=@($manifest.items | Where-Object {-not $_.hasGroundTruth} | ForEach-Object {@{path=$_.path;sha256=$_.sha256;tags=$_.expected}})
        if(-not $index.Count -or @($index | Where-Object {$_.sha256 -cnotmatch '^[a-f0-9]{64}$'}).Count){throw 'Benchmark must contain hashed untagged targets.'}
        $dataset=@{root=$manifest.root;fingerprint=(Get-AiSettingsHash @{source='untagged-benchmark-v1';fingerprint=$manifest.fingerprint;items=$index})}
    }else{
        $dataset=Get-Content -Raw -Encoding UTF8 (Join-Path $DatasetDirectory 'dataset.json') | ConvertFrom-Json
        $index=Get-Content -Raw -Encoding UTF8 (Join-Path $DatasetDirectory 'index.json') | ConvertFrom-Json
    }
    $client=Start-LocalAiMcp $repo $Instance
    $status=(Invoke-LocalAiTool $client 'app_status').structuredContent
    $schema=(Invoke-LocalAiTool $client 'tag_schema').structuredContent
    if($status.root -ne $dataset.root -or (-not $UntaggedBenchmarkDirectory -and (Get-AiSettingsHash $schema) -ne (Get-AiSettingsHash $dataset.schema))){throw 'Dataset differs from the current library/schema.'}
    $lookup=@{};$offset=0
    do{
        $page=(Invoke-LocalAiTool $client 'list_images' @{state=if($UntaggedBenchmarkDirectory){'untagged'}else{'tagged'};limit=100;offset=$offset;expectedWorkspaceRevision=$status.workspaceRevision}).structuredContent
        foreach($item in $page.images){$lookup[$item.path]=$item};$offset=$page.nextOffset
    }while($null -ne $offset)
    $settings=@{version=1;source='MCP view_image PNG';maxDimension=$MaxDimension;datasetFingerprint=$dataset.fingerprint}
    $fingerprint=Get-AiSettingsHash $settings
    $inputs=New-Object Collections.Generic.List[object]
    $seen=@{};$report.total=@($index).Count;$report.status='running';$report.fingerprint=$fingerprint
    Write-AiCheckpoint $report (Join-Path $OutputDirectory 'status.json')
    foreach($record in @($index | Sort-Object sha256)){
        if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'STOP')){throw 'paused: Stop requested.'}
        $item=$lookup[$record.path]
        if($UntaggedBenchmarkDirectory -and $item){
            # Prior benchmark IDs/revisions belong to an earlier scan. Rebind only after a full content check.
            $live=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
            if($live.sha256 -ne $record.sha256 -or $live.image.revision -ne $item.revision){throw 'Benchmark source changed before preview.'}
            $record.revision=$item.revision
        }
        if(-not $item -or $item.revision -ne $record.revision -or (Get-AiSettingsHash $item.filenameTags) -ne (Get-AiSettingsHash $record.tags)){throw 'Dataset source changed; refresh its export before continuing.'}
        if($seen.ContainsKey($record.sha256)){continue};$seen[$record.sha256]=$true
        $png=Join-Path $OutputDirectory ('images\'+$record.sha256+'.png')
        $receipt=Join-Path $OutputDirectory ('items\'+$record.sha256+'.json')
        if((Test-Path -LiteralPath $png) -and (Test-Path -LiteralPath $receipt)){
            $cached=Get-Content -Raw -Encoding UTF8 $receipt | ConvertFrom-Json
            if($cached.fingerprint -ne $fingerprint -or $cached.sha256 -ne $record.sha256){throw 'Preview settings changed; use a new output directory.'}
            if((Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash.ToLowerInvariant() -ne $cached.previewSha256){throw 'Preview cache changed; inspect before resuming.'}
            $inputs.Add($cached);continue
        }
        if($report.completedThisRun -ge $MaxItems){$report.status='checkpointed';break}
        $memory=Get-AiMemory
        if($memory.AvailablePhysical -lt 4GB -or $memory.AvailablePageFile -lt 4GB){throw 'paused: Insufficient available memory.'}
        $before=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        if($before.sha256 -ne $record.sha256 -or $before.image.revision -ne $record.revision){throw 'Source changed before preview.'}
        $preview=Invoke-LocalAiTool $client 'view_image' @{id=$item.id;expectedRevision=$item.revision;maxDimension=$MaxDimension}
        $picture=@($preview.content | Where-Object type -eq 'image')
        if($picture.Count -ne 1 -or $picture[0].mimeType -ne 'image/png'){throw 'Expected one MCP PNG.'}
        $after=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        if($after.sha256 -ne $record.sha256 -or $after.image.revision -ne $record.revision){throw 'Source changed during preview.'}
        [IO.File]::WriteAllBytes(($png+'.tmp'),[Convert]::FromBase64String($picture[0].data))
        Move-Item -LiteralPath ($png+'.tmp') -Destination $png -Force
        # The inference manifest intentionally contains neither filename labels nor source paths.
        $inputRecord=@{sha256=$record.sha256;preview=('images/'+$record.sha256+'.png');previewSha256=(Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash.ToLowerInvariant();fingerprint=$fingerprint}
        Write-AiCheckpoint $inputRecord $receipt
        $inputs.Add($inputRecord);$report.completedThisRun++;$report.completed=$inputs.Count
        Write-AiCheckpoint $report (Join-Path $OutputDirectory 'status.json')
        if($inputs.Count % 100 -eq 0){Write-Output ('Exported {0} previews.' -f $inputs.Count)}
    }
    $after=(Invoke-LocalAiTool $client 'app_status').structuredContent
    if($after.workspaceRevision -ne $status.workspaceRevision){throw 'Workspace changed during export.'}
    $report.workspaceUnchanged=$true
    if($report.status -ne 'checkpointed'){$report.status='complete'}
    Write-AiCheckpoint @{settings=$settings;fingerprint=$fingerprint;items=$inputs.ToArray()} (Join-Path $OutputDirectory 'inputs.json')
}catch{$report.status=if($_.Exception.Message -like 'paused:*'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{
    try{if($client){Stop-LocalAiMcp $client};$report.cleanupComplete=$true}
    finally{if($lock){$report.finishedUtc=[DateTime]::UtcNow.ToString('o');try{Write-AiCheckpoint $report (Join-Path $OutputDirectory 'status.json')}finally{$lock.Dispose()}}}
    Write-Output ('Preview checkpoint: '+(Join-Path $OutputDirectory 'status.json'))
}
