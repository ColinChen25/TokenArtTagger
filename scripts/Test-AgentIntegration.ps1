param([string]$DotnetPath, [string]$AppDll, [string]$BridgeDll, [ValidateSet('Debug','Release')][string]$Configuration='Debug')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $DotnetPath) { $DotnetPath = Join-Path $repo '.tools\dotnet\dotnet.exe' }
if (-not $AppDll) { $AppDll = Join-Path $repo ('src\TokenArtTagger.App\bin\'+$Configuration+'\net10.0-windows\TokenArtTagger.App.dll') }
if (-not $BridgeDll) { $BridgeDll = Join-Path $repo ('src\TokenArtTagger.Agent\bin\'+$Configuration+'\net10.0\TokenArtTagger.Agent.dll') }
$testRoot = Join-Path $repo ('.cache\agent-smoke\' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot ('images-' + [char]0x56FE)
$sorted = Join-Path $fixture 'Sorted'
$state = Join-Path $testRoot 'state'
New-Item -ItemType Directory -Force -Path $fixture,$sorted,$state | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 8,6
try {
    $bitmap.SetPixel(2,2,[System.Drawing.Color]::Red)
    foreach ($name in @('a.png','b.png','Sorted\female-caster-bard-human.png','Sorted\male-range-bow-elf.png','Sorted\female-melee-blade-human.png')) { $bitmap.Save((Join-Path $fixture $name), [System.Drawing.Imaging.ImageFormat]::Png) }
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
    Check ($tools.tools.Count -eq 13) 'Unexpected tool count.'
    [void](Call 'tag_schema' @{})
    [void](Call 'agent_instances' @{})
    $status = (Call 'app_status' @{}).structuredContent
    Check ($status.count -eq 0) 'Test app did not start empty.'
    $launcher = Join-Path $PSScriptRoot 'Start-Agent.ps1'
    $cliStatus = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher -Configuration $Configuration -Instance $app.Id -Call app_status | ConvertFrom-Json
    Check (-not $cliStatus.isError -and $cliStatus.structuredContent.count -eq 0) 'One-shot CLI without argument file failed.'
    $scanned = (Call 'scan_images' @{ root = $fixture; excludeDirectories = @($sorted); expectedWorkspaceRevision = $status.workspaceRevision }).structuredContent
    Check ($scanned.count -eq 2) 'Nested-folder exclusion failed.'
    $list = (Call 'list_images' @{ limit = 100 }).structuredContent
    $argumentFile = Join-Path $testRoot 'list-request.json'
    @{ folder = $fixture; recursive = $false } | ConvertTo-Json | Set-Content -LiteralPath $argumentFile -Encoding UTF8
    $cliList = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher -Configuration $Configuration -Instance $app.Id -Call list_images -ArgumentFile $argumentFile | ConvertFrom-Json
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
    # Proposals persist separately from manual work in progress and authoritative filenames.
    $queueBefore=(Call 'list_ai_suggestions' @{}).structuredContent
    Check ($queueBefore.total -eq 0) 'Isolated proposal queue did not start empty.'
    $proposalA=@{id=$a.id;expectedRevision=$changed.revision;sha256=$detail.sha256;tags=@{gender='male';role='generic';weaponOrStyle=$null;race='human'};provider='fixture';model='local-fixture';runFingerprint=('a'*64);evidence='Fixture observation'}
    $proposalB=@{id=$b.id;expectedRevision=$b.revision;sha256=('0'*64);tags=$tags;provider='fixture';model='local-fixture';runFingerprint=('a'*64)}
    [void](Call 'queue_ai_suggestions' @{suggestions=@($proposalA,$proposalB)} 'source_changed')
    Check (((Call 'list_ai_suggestions' @{}).structuredContent.total) -eq 0) 'Invalid source hash partly imported proposals.'
    $proposalB.sha256=$detail.sha256
    $queueWorkspace=(Call 'app_status' @{}).structuredContent.workspaceRevision
    $queued=(Call 'queue_ai_suggestions' @{suggestions=@($proposalA,$proposalB)}).structuredContent
    Check ($queued.imported -eq 2 -and $queued.reviewRequired -and -not $queued.automaticAcceptance -and $queued.workspaceRevision -eq $queueWorkspace) 'Queue import changed tags or acceptance policy.'
    $retried=(Call 'queue_ai_suggestions' @{suggestions=@($proposalA,$proposalB)}).structuredContent
    Check ($retried.imported -eq 0 -and $retried.alreadyPresent -eq 2) 'Repeated proposal import duplicated records.'
    $cliQueue = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher -Configuration $Configuration -Instance $app.Id -Call list_ai_suggestions | ConvertFrom-Json
    Check (-not $cliQueue.isError -and $cliQueue.structuredContent.total -eq 2) 'Stable CLI did not expose the proposal queue.'
    [void](Call 'list_ai_suggestions' @{expectedQueueRevision=$queueBefore.queueRevision} 'stale_queue')
    $queueFirst=(Call 'list_ai_suggestions' @{limit=1;expectedWorkspaceRevision=$queueWorkspace}).structuredContent
    $queueSecond=(Call 'list_ai_suggestions' @{offset=1;limit=1;expectedWorkspaceRevision=$queueWorkspace;expectedQueueRevision=$queueFirst.queueRevision}).structuredContent
    Check ($queueFirst.total -eq 2 -and $queueFirst.nextOffset -eq 1 -and $null -eq $queueSecond.nextOffset -and $queueFirst.suggestions[0].suggestion.id -ne $queueSecond.suggestions[0].suggestion.id) 'Proposal pagination duplicated or omitted records.'
    $queueA=(Call 'list_ai_suggestions' @{imageId=$a.id}).structuredContent
    Check ($queueA.total -eq 1 -and $queueA.suggestions[0].suggestion.tags.role -eq 'generic' -and $queueA.suggestions[0].image.currentTags.role -eq 'range' -and $queueA.suggestions[0].tagsUnchanged -and -not $queueA.sourceContentRechecked) 'Saved proposal was confused with live tags or rehashed content.'
    $proposalA.tags.race='elf'
    [void](Call 'queue_ai_suggestions' @{suggestions=@($proposalA)} 'invalid_suggestion')
    $proposalA.tags.race='human'
    $proposalA.expectedRevision=$a.revision
    [void](Call 'queue_ai_suggestions' @{suggestions=@($proposalA)} 'stale_image')
    Check (((Call 'app_status' @{}).structuredContent.workspaceRevision) -eq $queueWorkspace) 'Proposal validation changed manual work in progress.'
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
    $restoredQueue=(Call 'list_ai_suggestions' @{imageId=$target.id}).structuredContent
    Check ($restoredQueue.total -eq 1 -and -not $restoredQueue.suggestions[0].tagsUnchanged) 'Proposal queue did not survive rescan or detect later manual edits.'
    $status = (Call 'app_status' @{}).structuredContent
    [void](Call 'scan_images' @{ root = $fixture; expectedWorkspaceRevision = $status.workspaceRevision })
    $filtered = (Call 'list_images' @{ folder = $sorted; query = 'bard'; state = 'tagged'; limit = 2 }).structuredContent
    Check ($filtered.total -eq 1 -and $null -eq $filtered.nextOffset -and $filtered.images[0].filenameTags.weaponOrStyle -eq 'bard') 'Filtered final page did not return.'
    Check (((Call 'list_images' @{ query = 'no-such-image'; limit = 2 }).structuredContent.total) -eq 0) 'Empty query result failed.'
    $dataset = Join-Path $testRoot 'dataset'
    $datasetScript = Join-Path $PSScriptRoot 'Export-LocalAiDataset.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $datasetScript -Instance $app.Id -OutputDirectory $dataset -MaxItems 1
    Check ($LASTEXITCODE -eq 0) 'Dataset checkpoint export failed.'
    $firstPass = Get-Content -Raw -LiteralPath (Join-Path $dataset 'status.json') | ConvertFrom-Json
    Check ($firstPass.status -eq 'checkpointed' -and $firstPass.completedThisRun -eq 1) 'Dataset checkpoint did not stop at one item.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $datasetScript -Instance $app.Id -OutputDirectory $dataset
    Check ($LASTEXITCODE -eq 0) 'Dataset resume failed.'
    $datasetResult = Get-Content -Raw -LiteralPath (Join-Path $dataset 'status.json') | ConvertFrom-Json
    Check ($datasetResult.status -eq 'complete' -and $datasetResult.completedThisRun -eq 2 -and $datasetResult.summary.files -eq 3) 'Dataset resume repeated or missed a file.'
    Check ($datasetResult.summary.uniqueContent -eq 1 -and $datasetResult.summary.exactDuplicateGroups -eq 1 -and $datasetResult.summary.conflictingLabelGroups -eq 1) 'Duplicate content with conflicting filename labels was not flagged.'
    $previews=Join-Path $testRoot 'encoder-previews'
    $previewScript=Join-Path $PSScriptRoot 'Export-LocalAiPreviews.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $previewScript -Instance $app.Id -DatasetDirectory $dataset -OutputDirectory $previews -MaxItems 1
    Check ($LASTEXITCODE -eq 0) 'MCP preview export failed.'
    $previewInput=Get-Content -Raw -LiteralPath (Join-Path $previews 'inputs.json') | ConvertFrom-Json
    Check ($previewInput.items.Count -eq 1 -and -not ($previewInput.items[0].PSObject.Properties.Name -contains 'tags') -and -not ($previewInput.items[0].PSObject.Properties.Name -contains 'path')) 'Feature input leaked labels/source names or duplicated pixels.'
    $previewFile=Join-Path $previews $previewInput.items[0].preview
    Check ((Get-FileHash -LiteralPath $previewFile -Algorithm SHA256).Hash.ToLowerInvariant() -eq $previewInput.items[0].previewSha256) 'Preview hash receipt differs from PNG.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $previewScript -Instance $app.Id -DatasetDirectory $dataset -OutputDirectory $previews
    Check ($LASTEXITCODE -eq 0) 'MCP preview resume failed.'
    $previewStatus=Get-Content -Raw -LiteralPath (Join-Path $previews 'status.json') | ConvertFrom-Json
    Check ($previewStatus.status -eq 'complete' -and $previewStatus.completedThisRun -eq 0) 'Preview resume repeated decoding.'
    $savedErrorPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $null=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $previewScript -Instance $app.Id -DatasetDirectory $dataset -OutputDirectory $previews -MaxDimension 512 2>&1
        $previewExit=$LASTEXITCODE
    } finally {$ErrorActionPreference=$savedErrorPreference}
    Check ($previewExit -ne 0) 'Preview cache reused changed preprocessing.'
    $backlogAudit=Join-Path $testRoot 'backlog-audit'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Find-LocalAiKnownImages.ps1') -Instance $app.Id -DatasetDirectory $dataset -OutputDirectory $backlogAudit
    Check ($LASTEXITCODE -eq 0) 'Backlog exact-match audit failed.'
    $auditStatus=Get-Content -Raw -LiteralPath (Join-Path $backlogAudit 'status.json') | ConvertFrom-Json
    Check ($auditStatus.status -eq 'complete' -and $auditStatus.summary.checked -eq 2 -and $auditStatus.summary.conflictingLabels -eq 2 -and $auditStatus.summary.knownLabels -eq 0) 'Conflicting trusted copies became automatic backlog labels.'
    # The same pixels under different filenames must never leak a target answer through a reference.
    $referencePlan=Join-Path $testRoot 'reference-plan.json'
    @{references=@((Join-Path $sorted 'female-caster-bard-human.png'));targets=@((Join-Path $sorted 'male-range-bow-elf.png'))} | ConvertTo-Json | Set-Content -LiteralPath $referencePlan -Encoding UTF8
    $referenceRun=Join-Path $testRoot 'reference-leak-check'
    $savedErrorPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $null=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-LocalAiBenchmark.ps1') -Instance $app.Id -OutputDirectory $referenceRun -TaggedCount 1 -UntaggedCount 0 -ReferencePlanFile $referencePlan 2>&1
        $referenceExit=$LASTEXITCODE
    } finally {$ErrorActionPreference=$savedErrorPreference}
    $referenceStatus=Get-Content -Raw -LiteralPath (Join-Path $referenceRun 'status.json') | ConvertFrom-Json
    Check ($referenceExit -ne 0 -and $referenceStatus.status -eq 'failed' -and $referenceStatus.error -like 'Target content duplicates*') 'Reference content leakage was not rejected.'
    Check (-not (Test-Path -LiteralPath (Join-Path $referenceRun 'runtime.json'))) 'Reference leakage rejection started model inference.'
    # A retrieval plan must remain bound to the bytes/labels used for selection.
    $snapshotItems=@((Call 'list_images' @{folder=$sorted;state='tagged';limit=100}).structuredContent.images | Select-Object -First 2)
    $snapshots=@(foreach($item in $snapshotItems){
        $detail=(Call 'image_details' @{id=$item.id;includeHash=$true}).structuredContent
        @{path=$item.path;sha256=$detail.sha256;tags=$item.filenameTags}
    })
    foreach($kind in @('bytes','labels','incomplete')){
        $snapshotPlan=Join-Path $testRoot ('reference-snapshot-'+$kind+'.json')
        $planData=@{references=@($snapshotItems[0].path);targets=@($snapshotItems[1].path);sources=$snapshots} | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        if($kind -eq 'bytes'){$planData.sources[0].sha256=('0'*64)}
        elseif($kind -eq 'labels'){$planData.sources[0].tags.race='other'}
        else{$planData.sources=@($planData.sources[0])}
        $planData | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $snapshotPlan -Encoding UTF8
        $snapshotRun=Join-Path $testRoot ('snapshot-reject-'+$kind)
        $savedErrorPreference=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $null=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-LocalAiBenchmark.ps1') -Instance $app.Id -OutputDirectory $snapshotRun -TaggedCount 1 -UntaggedCount 0 -ReferencePlanFile $snapshotPlan 2>&1
            $snapshotExit=$LASTEXITCODE
        }finally{$ErrorActionPreference=$savedErrorPreference}
        $snapshotStatus=Get-Content -Raw -LiteralPath (Join-Path $snapshotRun 'status.json') | ConvertFrom-Json
        Check ($snapshotExit -ne 0 -and $snapshotStatus.error -like 'Reference plan*') 'Stale or incomplete retrieval snapshot was not rejected.'
        Check (-not (Test-Path -LiteralPath (Join-Path $snapshotRun 'runtime.json'))) 'Snapshot rejection started inference.'
    }
    # Focused experiments reuse completed development targets, with live byte/label checks.
    foreach($kind in @('incomplete-run','bytes','labels','unhashed','duplicate')){
        $sourceRoot=Join-Path $testRoot ('target-source-'+$kind)
        New-Item -ItemType Directory -Path $sourceRoot | Out-Null
        $sourceItem=@{path=$snapshots[0].path;sha256=$snapshots[0].sha256;expected=$snapshots[0].tags;hasGroundTruth=$true} | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        if($kind -eq 'bytes'){$sourceItem.sha256=('0'*64)}
        if($kind -eq 'labels'){$sourceItem.expected.race='other'}
        if($kind -eq 'unhashed'){$sourceItem.sha256=$null}
        $sourceItems=@(if($kind -eq 'duplicate'){$sourceItem;$sourceItem}else{$sourceItem})
        @{root=$fixture;fingerprint=('a'*64);items=$sourceItems} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $sourceRoot 'manifest.json') -Encoding UTF8
        @{status=if($kind -eq 'incomplete-run'){'running'}else{'complete'};cleanupComplete=$true;fingerprint=('a'*64)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sourceRoot 'status.json') -Encoding UTF8
        $targetRun=Join-Path $testRoot ('target-reject-'+$kind)
        $savedErrorPreference=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $null=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-LocalAiBenchmark.ps1') -Instance $app.Id -OutputDirectory $targetRun -TaggedCount $sourceItems.Count -UntaggedCount 0 -TargetManifestFile (Join-Path $sourceRoot 'manifest.json') -Focus role-style 2>&1
            $targetExit=$LASTEXITCODE
        }finally{$ErrorActionPreference=$savedErrorPreference}
        $targetStatus=Get-Content -Raw -LiteralPath (Join-Path $targetRun 'status.json') | ConvertFrom-Json
        $expectedError=switch($kind){'incomplete-run'{'Target manifest must belong*'} 'unhashed'{'Target manifest requires*'} 'duplicate'{'Reference and target paths must be distinct*'} default{'Reference plan*'}}
        Check ($targetExit -ne 0 -and $targetStatus.error -like $expectedError) ('Invalid target manifest was not rejected: '+$kind)
        Check (-not (Test-Path -LiteralPath (Join-Path $targetRun 'runtime.json'))) 'Target manifest rejection started inference.'
    }
    $unlabeled=(Call 'list_images' @{state='untagged';limit=1}).structuredContent.images[0]
    $unlabeledHash=(Call 'image_details' @{id=$unlabeled.id;includeHash=$true}).structuredContent.sha256
    $unlabeledSource=Join-Path $testRoot 'untagged-benchmark-source'
    New-Item -ItemType Directory -Path $unlabeledSource | Out-Null
    $unlabeledManifest=@{root=$fixture;fingerprint=('b'*64);items=@(@{path=$unlabeled.path;sha256=$unlabeledHash;expected=$unlabeled.filenameTags;hasGroundTruth=$false})}
    $unlabeledManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $unlabeledSource 'manifest.json') -Encoding UTF8
    @{status='complete';cleanupComplete=$true;fingerprint=('b'*64)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $unlabeledSource 'status.json') -Encoding UTF8
    $unlabeledPreviews=Join-Path $testRoot 'untagged-previews'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $previewScript -Instance $app.Id -UntaggedBenchmarkDirectory $unlabeledSource -OutputDirectory $unlabeledPreviews
    Check ($LASTEXITCODE -eq 0) 'Untagged benchmark preview failed.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $previewScript -Instance $app.Id -UntaggedBenchmarkDirectory $unlabeledSource -OutputDirectory $unlabeledPreviews
    $unlabeledStatus=Get-Content -Raw -LiteralPath (Join-Path $unlabeledPreviews 'status.json') | ConvertFrom-Json
    Check ($LASTEXITCODE -eq 0 -and $unlabeledStatus.completedThisRun -eq 0 -and $unlabeledStatus.cleanupComplete) 'Untagged preview resume did not reuse its verified cache.'
    $inputText=Get-Content -Raw -LiteralPath (Join-Path $unlabeledPreviews 'inputs.json')
    Check ($inputText -notmatch 'filename|expected|gender|race' -and $inputText -notmatch [regex]::Escape($fixture)) 'Source labels or filenames leaked into encoder inputs.'
    $suggestionFixture=Join-Path $testRoot 'classifier-suggestions'
    New-Item -ItemType Directory -Path $suggestionFixture | Out-Null
    $suggestionFingerprint=('c'*64)
    $suggestionData=@{fingerprint=$suggestionFingerprint;automaticAcceptance=$false;scoresCalibrated=$false;finalTestEvaluated=$false;items=@(@{sha256=$unlabeledHash;prediction=@{gender='female';role=$null;weaponOrStyle=$null;race=$null};reviewRequired=$true;automaticAcceptance=$false;scoresCalibrated=$false})}
    $suggestionData | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $suggestionFixture 'suggestions.json') -Encoding UTF8
    @{fingerprint=$suggestionFingerprint;settings=@{}} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $suggestionFixture 'settings.json') -Encoding UTF8
    @{fingerprint=$suggestionFingerprint;status='complete';cleanupComplete=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $suggestionFixture 'status.json') -Encoding UTF8
    $importScript=Join-Path $PSScriptRoot 'Import-LocalAiSuggestions.ps1'
    foreach($importPass in 1..2){
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $importScript -Instance $app.Id -Configuration $Configuration -SuggestionsDirectory $suggestionFixture -SourceBenchmarkDirectory $unlabeledSource -Model 'fixture-model'
        Check ($LASTEXITCODE -eq 0) 'Saved suggestion adapter failed.'
        $importReceipt=Get-Content -Raw -LiteralPath (Join-Path $suggestionFixture 'queue-import.json') | ConvertFrom-Json
        Check ($importReceipt.result.imported -eq (2-$importPass) -and -not $importReceipt.inference) 'Saved suggestion adapter duplicated or computed predictions.'
    }
    $suggestionData.items[0].automaticAcceptance=$true
    $suggestionData | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $suggestionFixture 'suggestions.json') -Encoding UTF8
    $savedErrorPreference=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        $null=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $importScript -Instance $app.Id -Configuration $Configuration -SuggestionsDirectory $suggestionFixture -SourceBenchmarkDirectory $unlabeledSource -Model 'fixture-model' 2>&1
        $unsafeImportExit=$LASTEXITCODE
    }finally{$ErrorActionPreference=$savedErrorPreference}
    Check ($unsafeImportExit -ne 0) 'Automatic-acceptance artifact was imported.'
    $unlabeledManifest.items[0].sha256=('0'*64)
    $unlabeledManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $unlabeledSource 'manifest.json') -Encoding UTF8
    $savedErrorPreference=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        $null=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $previewScript -Instance $app.Id -UntaggedBenchmarkDirectory $unlabeledSource -OutputDirectory (Join-Path $testRoot 'stale-untagged-previews') 2>&1
        $staleExit=$LASTEXITCODE
    }finally{$ErrorActionPreference=$savedErrorPreference}
    $stalePreviewStatus=Get-Content -Raw -LiteralPath (Join-Path $testRoot 'stale-untagged-previews/status.json') | ConvertFrom-Json
    Check ($staleExit -ne 0 -and $stalePreviewStatus.error -like 'Benchmark source changed*') 'Changed untagged bytes accepted for encoder input.'
    $newHashes = @(Get-ChildItem -LiteralPath $fixture -File -Recurse | Get-FileHash | Select-Object Path,Hash)
    Check (-not (Compare-Object $originalHashes $newHashes -Property Path,Hash)) 'Source images were renamed or changed.'
    'PASS: MCP handshake/discovery; all 13 tools; real desktop IPC; CLI and Unicode paths; nested exclusion; atomic validation; stale revisions; PNG pixels; SHA-256; selection; rename preview; rescan persistence; reset; review-only suggestion import/retry/pagination and separate tag state; filtered final pages; dataset checkpoint/resume and conflicting duplicates; reference leakage blocked before inference; original files unchanged.'
    "Test artifacts: $testRoot"
} finally {
    if ($bridge) { if (-not $bridge.HasExited) { $bridge.StandardInput.Close(); if (-not $bridge.WaitForExit(3000)) { $bridge.Kill() } }; $bridge.Dispose() }
    if ($app) { if (-not $app.HasExited) { [void]$app.CloseMainWindow(); if (-not $app.WaitForExit(3000)) { $app.Kill() } }; $app.Dispose() }
    $env:LOCALAPPDATA = $savedLocal
    $env:APPDATA = $savedRoaming
    $env:TOKENARTTAGGER_DATA_DIR = $savedData
}
