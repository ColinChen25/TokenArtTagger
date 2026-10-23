param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$DatasetDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,100000)][int]$MaxItems=100000
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory);$DatasetDirectory=[IO.Path]::GetFullPath($DatasetDirectory)
foreach($directory in @($OutputDirectory,$DatasetDirectory)){
    if(-not $directory.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Dataset and output directories must remain in .cache.'}
}
if($OutputDirectory -eq $DatasetDirectory){throw 'Use a separate output directory for the backlog audit.'}
$datasetStatus=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $DatasetDirectory 'status.json') | ConvertFrom-Json
if($datasetStatus.status -ne 'complete' -or -not $datasetStatus.cleanupComplete){throw 'Complete the trusted dataset export before matching.'}
$dataset=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $DatasetDirectory 'dataset.json') | ConvertFrom-Json
$index=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $DatasetDirectory 'index.json') | ConvertFrom-Json
$known=@{};foreach($row in $index){if(-not $known.ContainsKey($row.sha256)){$known[$row.sha256]=New-Object Collections.Generic.List[object]};$known[$row.sha256].Add($row)}
New-Item -ItemType Directory -Force -Path $OutputDirectory,(Join-Path $OutputDirectory 'items') | Out-Null
$statusPath=Join-Path $OutputDirectory 'status.json'
$report=[ordered]@{status='starting';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');completedThisRun=0;cleanupComplete=$false;automaticApplication=$false}
$client=$null;$lock=$null;$records=New-Object Collections.Generic.List[object]
try{
    $lock=[IO.File]::Open((Join-Path $OutputDirectory 'worker.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $client=Start-LocalAiMcp $repo $Instance
    $status=(Invoke-LocalAiTool $client 'app_status').structuredContent
    $schema=(Invoke-LocalAiTool $client 'tag_schema').structuredContent
    if($status.root -ne $dataset.root -or (Get-AiSettingsHash $schema) -ne (Get-AiSettingsHash $dataset.schema)){throw 'Scanned library or schema differs from the trusted dataset.'}
    $catalog=New-Object Collections.Generic.List[object];$lookup=@{};$offset=0
    do{
        $page=(Invoke-LocalAiTool $client 'list_images' @{limit=100;offset=$offset;expectedWorkspaceRevision=$status.workspaceRevision}).structuredContent
        foreach($item in $page.images){$lookup[$item.path]=$item;if(-not $item.recognizedFilename){$catalog.Add($item)}};$offset=$page.nextOffset
    }while($null -ne $offset)
    # Revisions tie the source hashes/labels to this live scan. Re-export after a rescan or edit.
    foreach($row in $index){
        $item=$lookup[$row.path]
        if(-not $item -or $item.revision -ne $row.revision){throw 'Trusted dataset is stale; resume its export before matching.'}
        foreach($field in @('gender','role','weaponOrStyle','race')){if($item.filenameTags.$field -cne $row.tags.$field){throw 'Trusted filename labels changed; refresh the dataset.'}}
    }
    $fingerprint=Get-AiSettingsHash @{dataset=$dataset.fingerprint;index=$index;version=1}
    $report.status='running';$report.total=$catalog.Count;$report.fingerprint=$fingerprint
    Write-AiCheckpoint $report $statusPath
    foreach($item in $catalog){
        if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'STOP')){throw 'paused: Backlog audit stop requested.'}
        $file=Join-Path $OutputDirectory ('items\'+(Get-AiTextHash $item.path)+'.json')
        if(Test-Path -LiteralPath $file){
            $cached=Get-Content -Raw -Encoding UTF8 -LiteralPath $file | ConvertFrom-Json
            if($cached.fingerprint -eq $fingerprint -and $cached.revision -eq $item.revision -and $cached.path -eq $item.path){$records.Add($cached);continue}
        }
        if($report.completedThisRun -ge $MaxItems){$report.status='checkpointed';break}
        $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        if($detail.image.revision -ne $item.revision){throw 'Backlog image changed; rescan and refresh the dataset.'}
        $matches=@();$outcome='no_exact_match';$tags=$null
        if($known.ContainsKey($detail.sha256)){
            $matches=$known[$detail.sha256].ToArray()
            # Rehash matched trusted sources rather than relying only on their cached metadata.
            foreach($match in $matches){
                $source=$lookup[$match.path]
                $verified=(Invoke-LocalAiTool $client 'image_details' @{id=$source.id;includeHash=$true}).structuredContent
                if($verified.sha256 -ne $detail.sha256 -or $verified.image.revision -ne $source.revision){throw 'Matched trusted source changed; refresh the dataset.'}
            }
            $labels=@($matches | ForEach-Object {Get-AiSettingsHash $_.tags} | Select-Object -Unique)
            if($labels.Count -eq 1){$outcome='known_labels';$tags=$matches[0].tags}else{$outcome='conflicting_labels'}
        }
        $record=@{path=$item.path;revision=$item.revision;sha256=$detail.sha256;fingerprint=$fingerprint;outcome=$outcome;tags=$tags;matches=@($matches | Select-Object path,tags);automaticApplication=$false}
        Write-AiCheckpoint $record $file;$records.Add($record);$report.completedThisRun++
        $report.completed=$records.Count;Write-AiCheckpoint $report $statusPath
        if($records.Count % 100 -eq 0){Write-Output ('Checked {0}/{1} backlog hashes.' -f $records.Count,$catalog.Count)}
    }
    $after=(Invoke-LocalAiTool $client 'app_status').structuredContent
    if($after.workspaceRevision -ne $status.workspaceRevision){throw 'Workspace changed during matching; inspect edits and resume.'}
    $report.workspaceUnchanged=$true
    $report.summary=@{checked=$records.Count;knownLabels=@($records | Where-Object outcome -eq 'known_labels').Count;conflictingLabels=@($records | Where-Object outcome -eq 'conflicting_labels').Count;noExactMatch=@($records | Where-Object outcome -eq 'no_exact_match').Count}
    Write-AiCheckpoint ($records.ToArray()) (Join-Path $OutputDirectory 'index.json')
    if($records.Count -eq $catalog.Count){$report.status='complete'}
}catch{$report.status=if($_.Exception.Message -like 'paused:*'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{
    try{if($client){Stop-LocalAiMcp $client};$report.cleanupComplete=$true}
    finally{if($lock){$report.finishedUtc=[DateTime]::UtcNow.ToString('o');try{Write-AiCheckpoint $report $statusPath}finally{$lock.Dispose()}}}
    Write-Output "Backlog audit: $statusPath"
}
