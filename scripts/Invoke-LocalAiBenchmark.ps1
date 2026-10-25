param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,200)][int]$TaggedCount=32,
    [ValidateRange(0,50)][int]$UntaggedCount=4,
    [ValidateRange(1,250)][int]$MaxItems=250,
    [ValidateRange(256,1536)][int]$MaxDimension=768,
    [switch]$ConstrainRoleStyle,
    [string]$ReferencePlanFile
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
if(-not $OutputDirectory.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Benchmark output must be inside the ignored repository .cache directory.'}
New-Item -ItemType Directory -Force -Path $OutputDirectory,(Join-Path $OutputDirectory 'items') | Out-Null
$lock=$null;$client=$null;$runtime=$null
$report=[ordered]@{status='starting';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');startedUtc=[DateTime]::UtcNow.ToString('o');cleanupComplete=$false;completedThisRun=0}
$reportPath=Join-Path $OutputDirectory 'status.json'
$manifestPath=Join-Path $OutputDirectory 'manifest.json'
$stopFile=Join-Path $OutputDirectory 'STOP'
$results=New-Object Collections.Generic.List[object]
try {
    $lock=[IO.File]::Open((Join-Path $OutputDirectory 'worker.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    if(Test-Path -LiteralPath $stopFile){throw 'paused: STOP marker exists. Remove it only when resumption is intended.'}
    Write-AiCheckpoint $report $reportPath
    $client=Start-LocalAiMcp $repo $Instance
    $status=(Invoke-LocalAiTool $client 'app_status').structuredContent
    $schema=(Invoke-LocalAiTool $client 'tag_schema').structuredContent
    if(-not $status.root){throw 'Scan the library with MCP before starting the benchmark.'}
    $contract=Get-AiTagContract $schema -ConstrainRoleStyle:$ConstrainRoleStyle
    $settings=[ordered]@{model='qwen3-vl:2b-instruct-q4_K_M';digest='afc393e3572c07db0bc84aa460d928a16c75f3fbb9ca9e0dccc4fbd2dafb573e';runtime='0.40.8';promptVersion=$contract.Version;prompt=$contract.Prompt;schema=$contract.Schema;maxDimension=$MaxDimension;threads=2;context=4096;maxTokens=256;seed=42;temperature=0;cpuOnly=$true;unloadAfterEachImage=$true}
    $catalog=New-Object Collections.Generic.List[object]
    $offset=0
    do {
        $page=(Invoke-LocalAiTool $client 'list_images' @{limit=100;offset=$offset;expectedWorkspaceRevision=$status.workspaceRevision}).structuredContent
        foreach($item in $page.images){$catalog.Add($item)}
        $offset=$page.nextOffset
    } while($null -ne $offset)
    $lookup=@{};foreach($item in $catalog){$lookup[$item.path]=$item}
    $referenceSources=New-Object Collections.Generic.List[object]
    $examples=New-Object Collections.Generic.List[object]
    $plan=$null
    if($ReferencePlanFile){
        $ReferencePlanFile=[IO.Path]::GetFullPath($ReferencePlanFile)
        if(-not $ReferencePlanFile.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Reference plans must remain in .cache.'}
        $plan=Get-Content -Raw -Encoding UTF8 -LiteralPath $ReferencePlanFile | ConvertFrom-Json
        if(@($plan.references).Count -lt 1 -or @($plan.references).Count -gt 2){throw 'Reference pilot requires one or two fixed reference paths.'}
        if(@($plan.targets).Count -ne $TaggedCount -or $UntaggedCount -ne 0){throw 'Reference pilot requires TaggedCount matching its targets and UntaggedCount zero.'}
        $chosenPaths=@{}
        foreach($path in @($plan.references)+@($plan.targets)){
            if($chosenPaths.ContainsKey($path)){throw 'Reference and target paths must be distinct.'}
            $chosenPaths[$path]=$true
            $item=$lookup[$path]
            if(-not $item -or -not $item.recognizedFilename -or $item.tagIssue -ne 'None'){throw 'Reference pilot paths must have authoritative filename tags in the current scan.'}
        }
        if($plan.sources){
            $snapshotPaths=@{}
            foreach($snapshot in $plan.sources){
                if(-not $chosenPaths.ContainsKey($snapshot.path) -or $snapshotPaths.ContainsKey($snapshot.path)){throw 'Reference plan source snapshots are inconsistent.'}
                $snapshotPaths[$snapshot.path]=$true
                $item=$lookup[$snapshot.path]
                $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
                if($detail.sha256 -ne $snapshot.sha256 -or $detail.image.revision -ne $item.revision){throw 'Reference plan source bytes changed; prepare a new plan.'}
                foreach($field in @('gender','role','weaponOrStyle','race')){
                    if($snapshot.tags.$field -cne $item.filenameTags.$field){throw 'Reference plan filename labels changed; prepare a new plan.'}
                }
            }
            if($snapshotPaths.Count -ne $chosenPaths.Count){throw 'Reference plan source snapshots are incomplete.'}
        }
        foreach($path in $plan.references){
            $item=$lookup[$path]
            $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
            if($detail.image.revision -ne $item.revision){throw 'Reference changed during selection; rescan and resume.'}
            if($detail.sha256 -in @($referenceSources | ForEach-Object sha256)){throw 'Reference images have duplicate content.'}
            $tags=[ordered]@{};foreach($field in @('gender','role','weaponOrStyle','race')){$tags[$field]=$item.filenameTags.$field}
            $referenceSources.Add(@{path=$path;sha256=$detail.sha256;tags=$tags;revision=$item.revision;id=$item.id})
        }
        $contract.Prompt='Use the preceding trusted visual references as examples of this collection. Classify only the final target image below. References do not imply that the target shares their class or race. '+$contract.Prompt
        $contract.Version+='-references-v1'
        $settings.prompt=$contract.Prompt;$settings.promptVersion=$contract.Version
        $settings.references=@($referenceSources | ForEach-Object {@{sha256=$_.sha256;tags=$_.tags}})
        $settings.referenceMaxDimension=384
        $settings.targetSelectionHash=Get-AiSettingsHash @($plan.targets)
    }
    $fingerprint=Get-AiSettingsHash $settings
    if(Test-Path -LiteralPath $manifestPath){
        $manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
        if($manifest.fingerprint -ne $fingerprint){throw 'Settings changed; use a new output directory so old results cannot be reused.'}
        if($manifest.root -ne $status.root){throw 'Scanned library differs from the saved manifest.'}
        if($manifest.taggedRequested -ne $TaggedCount -or $manifest.untaggedRequested -ne $UntaggedCount){throw 'Sample counts changed; use the original counts or a new output directory.'}
    } else {
        $tagged=@($catalog | Where-Object {$_.recognizedFilename -and $_.tagIssue -eq 'None'} | Sort-Object @{Expression={Get-AiTextHash ('sample-v1|'+$_.path)}})
        $selected=New-Object Collections.Generic.List[object]
        $chosen=@{}
        if($plan){foreach($path in $plan.targets){$selected.Add($lookup[$path]);$chosen[$path]=$true}}
        # Cover rare races, then missing weapon/caster styles, then fill deterministically.
        foreach($group in @($tagged | Group-Object {$_.filenameTags.race} | Sort-Object Count,Name)){
            if($selected.Count -ge $TaggedCount){break}
            $item=$group.Group[0];$selected.Add($item);$chosen[$item.path]=$true
        }
        foreach($group in @($tagged | Where-Object {$null -ne $_.filenameTags.weaponOrStyle} | Group-Object {$_.filenameTags.weaponOrStyle} | Sort-Object Count,Name)){
            if($selected.Count -ge $TaggedCount){break}
            if(@($selected | Where-Object {$_.filenameTags.weaponOrStyle -eq $group.Name}).Count){continue}
            $item=@($group.Group | Where-Object {-not $chosen.ContainsKey($_.path)})[0]
            if($item){$selected.Add($item);$chosen[$item.path]=$true}
        }
        foreach($item in $tagged){if($selected.Count -ge $TaggedCount){break};if(-not $chosen.ContainsKey($item.path)){$selected.Add($item);$chosen[$item.path]=$true}}
        foreach($item in @($catalog | Where-Object {-not $_.recognizedFilename} | Sort-Object @{Expression={Get-AiTextHash ('sample-v1|'+$_.path)}} | Select-Object -First $UntaggedCount)){$selected.Add($item)}
        $selection=if($plan){'Fixed private development plan with one or two references shared by every target; not automatic retrieval.'}else{'Deterministic coverage of rare races and styles, followed by hash-ordered fill; untagged hash-ordered sample.'}
        $manifest=[ordered]@{createdUtc=[DateTime]::UtcNow.ToString('o');root=$status.root;fingerprint=$fingerprint;settings=$settings;taggedRequested=$TaggedCount;untaggedRequested=$UntaggedCount;selection=$selection;references=@($referenceSources | ForEach-Object {@{path=$_.path;sha256=$_.sha256;tags=$_.tags}});items=@($selected | ForEach-Object {@{path=$_.path;hasGroundTruth=$_.recognizedFilename;expected=$_.filenameTags}})}
        Write-AiCheckpoint $manifest $manifestPath
    }
    $report.total=@($manifest.items).Count;$report.fingerprint=$fingerprint;$report.status='running'
    $appProcess=Get-Process -Id $Instance
    $appBaseline=@{processId=$Instance;workingSetBytes=$appProcess.WorkingSet64;privateBytes=$appProcess.PrivateMemorySize64}
    $appProcess.Dispose();$report.existingAppBaseline=$appBaseline
    Write-AiCheckpoint $report $reportPath
    $seenHashes=@{}
    foreach($entry in $manifest.items){
        if(Test-Path -LiteralPath $stopFile){throw 'paused: Stop requested.'}
        $item=$lookup[$entry.path]
        if(-not $item){throw 'source_changed: A sampled file is absent from this scan. Use a new run directory.'}
        foreach($field in @('gender','role','weaponOrStyle','race')){if($entry.expected.$field -cne $item.filenameTags.$field){throw 'source_changed: Authoritative tags changed since selection. Use a new run directory.'}}
        $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        if($detail.image.revision -ne $item.revision){throw 'source_changed: File or tags changed during listing. Rescan and resume.'}
        $hash=$detail.sha256
        if($hash -in @($referenceSources | ForEach-Object sha256)){throw 'Target content duplicates a trusted reference; refuse answer leakage.'}
        if($entry.sha256 -and $entry.sha256 -ne $hash){throw 'source_changed: Sample content changed since its first attempt. Use a new run directory.'}
        if(-not $entry.sha256){
            if($entry -is [Collections.IDictionary]){$entry.sha256=$hash}else{$entry | Add-Member -NotePropertyName sha256 -NotePropertyValue $hash}
            Write-AiCheckpoint $manifest $manifestPath
        }
        $key=Get-AiTextHash ($entry.path+'|'+$hash+'|'+$fingerprint)
        $resultPath=Join-Path $OutputDirectory ('items\'+$key+'.json')
        if(Test-Path -LiteralPath $resultPath){
            $saved=Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
            if($saved.sha256 -ne $hash -or $saved.fingerprint -ne $fingerprint -or $saved.path -ne $entry.path){throw 'Invalid cached result identity.'}
            # Revalidate cached model text with current rules; never trust an old acceptance decision.
            if($saved.response){
                Assert-AiPromptBudget $saved.response.prompt_eval_count $settings.maxTokens
                $checked=Test-AiPrediction $saved.response $schema
                $saved.outcome=$checked.outcome;$saved.prediction=$checked.prediction;$saved.reason=$checked.reason
                $saved.expected=$entry.expected;$saved.hasGroundTruth=$entry.hasGroundTruth
                $saved | Add-Member -NotePropertyName validationVersion -NotePropertyValue 'strict-tags-v2' -Force
                Write-AiCheckpoint $saved $resultPath
            }
            $results.Add($saved);$seenHashes[$hash]=$true;continue
        }
        if($report.completedThisRun -ge $MaxItems){$report.status='checkpointed';break}
        if($seenHashes.ContainsKey($hash)){throw 'Exact duplicate content in diagnostic sample; select a new deduplicated sample before scoring.'}
        $seenHashes[$hash]=$true
        $record=[ordered]@{path=$entry.path;sha256=$hash;fingerprint=$fingerprint;validationVersion='strict-tags-v2';hasGroundTruth=$entry.hasGroundTruth;expected=$entry.expected;startedUtc=[DateTime]::UtcNow.ToString('o');outcome='pending';prediction=$null;reason=$null;elapsedSeconds=$null;automaticAcceptance=$false}
        Write-Output ('Analyzing image {0}/{1} (checkpoint {2})...' -f ($results.Count+1),$report.total,$key.Substring(0,12))
        # Transport failures stop the run; unsupported image decoding is an item result.
        try {$pixels=Invoke-LocalAiTool $client 'view_image' @{id=$item.id;expectedRevision=$item.revision;maxDimension=$MaxDimension}}
        catch {
            if($_.Exception.Message -notmatch 'image_decode|unsupported_image|image_too_large|decode_failed'){throw}
            $record.outcome='decode_error';$record.reason=$_.Exception.Message
        }
        if($record.outcome -eq 'pending'){
            if($referenceSources.Count -and -not $examples.Count){
                foreach($reference in $referenceSources){
                    $preview=Invoke-LocalAiTool $client 'view_image' @{id=$reference.id;expectedRevision=$reference.revision;maxDimension=384}
                    $referenceImage=@($preview.content | Where-Object type -eq 'image')
                    if($referenceImage.Count -ne 1 -or $referenceImage[0].mimeType -ne 'image/png'){throw 'Expected one PNG per reference.'}
                    $examples.Add(@{imageBase64=$referenceImage[0].data;tags=$reference.tags})
                }
                $preview=$null;$referenceImage=$null
            }
            if(-not $runtime){$runtime=Start-LocalAiRuntime $repo $OutputDirectory;$runtime.AdditionalProcessIds=@($client.Process.Id,$Instance);$runtime.AppBaseline=$appBaseline}
            $picture=@($pixels.content | Where-Object type -eq 'image')
            if($picture.Count -ne 1 -or $picture[0].mimeType -ne 'image/png'){throw 'Expected exactly one MCP PNG preview.'}
            $answer=Invoke-LocalAiVision $runtime $picture[0].data $contract.Prompt $contract.Schema $stopFile 600 256 ($examples.ToArray())
            $checked=Test-AiPrediction $answer.response $schema
            $record.outcome=$checked.outcome;$record.prediction=$checked.prediction;$record.reason=$checked.reason
            $record.response=$answer.response;$record.elapsedSeconds=$answer.elapsedSeconds;$record.sizeVram=$answer.loadedModel.size_vram
            Clear-LocalAiModel $runtime
            $record.unloadVerified=$true
            $record.width=$pixels.structuredContent.width;$record.height=$pixels.structuredContent.height
            $after=(Invoke-LocalAiTool $client 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
            if($after.sha256 -ne $hash -or $after.image.revision -ne $item.revision){throw 'source_changed: Image changed during inference; result was not accepted.'}
            foreach($reference in $referenceSources){
                $verified=(Invoke-LocalAiTool $client 'image_details' @{id=$reference.id;includeHash=$true}).structuredContent
                if($verified.sha256 -ne $reference.sha256 -or $verified.image.revision -ne $reference.revision){throw 'source_changed: Reference changed during inference; result was not accepted.'}
            }
            $pixels=$null;$picture=$null;$answer=$null
            [GC]::Collect();[GC]::WaitForPendingFinalizers()
        }
        $record.completedUtc=[DateTime]::UtcNow.ToString('o')
        Write-AiCheckpoint $record $resultPath
        $results.Add([pscustomobject]$record);$report.completedThisRun++
        $report.summary=Get-AiEvaluationSummary ($results.ToArray())
        $report.updatedUtc=[DateTime]::UtcNow.ToString('o')
        Write-AiCheckpoint $report $reportPath
        Write-AiCheckpoint $report.summary (Join-Path $OutputDirectory 'summary.json')
        Write-Output ('Saved {0}; {1:N1} seconds.' -f $record.outcome,$record.elapsedSeconds)
    }
    $afterStatus=(Invoke-LocalAiTool $client 'app_status').structuredContent
    if($afterStatus.workspaceRevision -ne $status.workspaceRevision){throw 'workspace_changed: Review concurrent edits before interpreting this run.'}
    $report.workspaceUnchanged=$true
    $report.summary=Get-AiEvaluationSummary ($results.ToArray())
    if($results.Count -eq $report.total){$report.status='complete'}
    Write-AiCheckpoint $report.summary (Join-Path $OutputDirectory 'summary.json')
} catch {
    $report.status=if($_.Exception.Message -match '^(paused|memory_pressure):'){'paused'}else{'failed'}
    $report.error=$_.Exception.Message
    throw
} finally {
    try {
        if($runtime){
            $report.peakWorkingSetBytes=$runtime.PeakWorkingSet;$report.peakPrivateBytes=$runtime.PeakPrivateBytes;$report.minimumAvailableBytes=$runtime.MinimumAvailable;$report.peakCombinedWorkingSetBytes=$runtime.PeakCombinedWorkingSet
            try {Write-AiCheckpoint ($runtime.Samples.ToArray()) (Join-Path $OutputDirectory ('memory-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json'))}
            finally {Stop-LocalAiRuntime $runtime}
        }
        if($client){Stop-LocalAiMcp $client}
        $report.cleanupComplete=$true
    } finally {
        if($lock){
            $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
            try {
                Write-AiCheckpoint $report $reportPath
                Write-AiCheckpoint $report (Join-Path $OutputDirectory ('session-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json'))
            } finally {$lock.Dispose()}
        }
        Write-Output "Checkpoint: $reportPath"
    }
}
