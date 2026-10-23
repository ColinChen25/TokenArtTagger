param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,100000)][int]$MaxItems=100000
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $OutputDirectory.StartsWith(([IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Dataset output must remain in the ignored .cache directory.'}
New-Item -ItemType Directory -Force -Path $OutputDirectory,(Join-Path $OutputDirectory 'items') | Out-Null
$lock=$null;$client=$null
$report=[ordered]@{status='starting';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');startedUtc=[DateTime]::UtcNow.ToString('o');completedThisRun=0;cleanupComplete=$false}
$statusPath=Join-Path $OutputDirectory 'status.json'
$records=New-Object Collections.Generic.List[object]
try {
    $lock=[IO.File]::Open((Join-Path $OutputDirectory 'worker.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    [Diagnostics.Process]::GetCurrentProcess().PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal
    $client=Start-LocalAiMcp $repo $Instance
    $status=(Invoke-LocalAiTool $client 'app_status').structuredContent
    $schema=(Invoke-LocalAiTool $client 'tag_schema').structuredContent
    if(-not $status.root){throw 'Scan a library before exporting its filename labels.'}
    $catalog=New-Object Collections.Generic.List[object];$offset=0
    do {
        $page=(Invoke-LocalAiTool $client 'list_images' @{state='tagged';limit=100;offset=$offset;expectedWorkspaceRevision=$status.workspaceRevision}).structuredContent
        foreach($item in $page.images){$catalog.Add($item)};$offset=$page.nextOffset
    }while($null -ne $offset)
    $fingerprint=Get-AiSettingsHash @{root=$status.root;schema=$schema;labelSource='filename';version=1}
    $datasetPath=Join-Path $OutputDirectory 'dataset.json'
    if(Test-Path -LiteralPath $datasetPath){
        $previous=Get-Content -Raw -Encoding UTF8 -LiteralPath $datasetPath | ConvertFrom-Json
        if($previous.fingerprint -ne $fingerprint){throw 'Dataset root or schema changed; use a new output directory.'}
    }
    $report.fingerprint=$fingerprint;$report.total=$catalog.Count;$report.status='running'
    Write-AiCheckpoint @{root=$status.root;schema=$schema;fingerprint=$fingerprint;labelSource='filename';createdUtc=[DateTime]::UtcNow.ToString('o')} $datasetPath
    Write-AiCheckpoint $report $statusPath
    foreach($item in $catalog){
        if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'STOP')){throw 'paused: Dataset stop requested.'}
        $key=Get-AiTextHash $item.path
        $path=Join-Path $OutputDirectory ('items\'+$key+'.json')
        if(Test-Path -LiteralPath $path){
            $cached=Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
            if($cached.fingerprint -eq $fingerprint -and $cached.revision -eq $item.revision -and $cached.path -eq $item.path){$records.Add($cached);continue}
        }
        if($report.completedThisRun -ge $MaxItems){$report.status='checkpointed';break}
        $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        if($detail.image.revision -ne $item.revision){throw 'Source changed while exporting; refresh the app scan and resume.'}
        $record=[pscustomobject]@{path=$item.path;revision=$item.revision;sha256=$detail.sha256;tags=$detail.image.filenameTags;fingerprint=$fingerprint;labelSource='filename';hashedUtc=[DateTime]::UtcNow.ToString('o')}
        Write-AiCheckpoint $record $path
        $records.Add($record);$report.completedThisRun++
        $report.completed=$records.Count;$report.updatedUtc=[DateTime]::UtcNow.ToString('o')
        Write-AiCheckpoint $report $statusPath
        if($records.Count % 100 -eq 0){Write-Output ('Hashed {0}/{1} authoritative images.' -f $records.Count,$catalog.Count)}
    }
    $duplicates=@(foreach($group in @($records | Group-Object sha256 | Where-Object Count -gt 1)){
        $labels=@($group.Group | ForEach-Object {Get-AiSettingsHash $_.tags} | Select-Object -Unique)
        @{sha256=$group.Name;count=$group.Count;conflictingFilenameLabels=($labels.Count -gt 1);items=@($group.Group | Select-Object path,tags)}
    })
    $frequencies=[ordered]@{}
    foreach($field in @('gender','role','weaponOrStyle','race')){
        $frequencies[$field]=@(foreach($group in @($records | Group-Object {$_.tags.$field} | Sort-Object Count,Name)){
            @{tag=if($group.Name){$group.Name}else{$null};files=$group.Count;uniqueContent=@($group.Group.sha256 | Select-Object -Unique).Count}
        })
    }
    $report.summary=@{files=$records.Count;uniqueContent=@($records | ForEach-Object sha256 | Select-Object -Unique).Count;exactDuplicateGroups=$duplicates.Count;conflictingLabelGroups=@($duplicates | Where-Object conflictingFilenameLabels).Count;frequencies=$frequencies;nearDuplicatesGrouped=$false;trainingPerformed=$false}
    Write-AiCheckpoint ($records.ToArray()) (Join-Path $OutputDirectory 'index.json')
    Write-AiCheckpoint $duplicates (Join-Path $OutputDirectory 'exact-duplicates.json')
    Write-AiCheckpoint $report.summary (Join-Path $OutputDirectory 'summary.json')
    $after=(Invoke-LocalAiTool $client 'app_status').structuredContent
    if($after.workspaceRevision -ne $status.workspaceRevision){throw 'Workspace changed during export; inspect concurrent edits and resume.'}
    $report.workspaceUnchanged=$true
    if($records.Count -eq $catalog.Count){$report.status='complete'}
}catch{$report.status=if($_.Exception.Message -like 'paused:*'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{
    try{if($client){Stop-LocalAiMcp $client};$report.cleanupComplete=$true}
    finally{if($lock){$report.finishedUtc=[DateTime]::UtcNow.ToString('o');try{Write-AiCheckpoint $report $statusPath}finally{$lock.Dispose()}}}
    Write-Output "Dataset checkpoint: $statusPath"
}
