$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiCache.psm1') -Force
$root=Join-Path (Split-Path $PSScriptRoot -Parent) ('.cache\benchmark-cache-tests\'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root,(Join-Path $root 'items') | Out-Null
[IO.File]::WriteAllText((Join-Path $root 'worker.lock'),'')
$script:passed=0
function Check($Value,$Message){if(-not $Value){throw $Message};$script:passed++}
$settings=@{prompt='Classify';schema=@{type='object'};model='cached';maxDimension=768;sourceManifestFingerprint='source-a';targetSelectionHash='targets-a'}
$other=$settings | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$other.sourceManifestFingerprint='source-b';$other.targetSelectionHash='targets-b'
Check ((Get-AiInferenceSettingsHash $settings) -eq (Get-AiInferenceSettingsHash $other)) 'Sample changes invalidate identical inference settings.'
$other.prompt='Different question'
Check ((Get-AiInferenceSettingsHash $settings) -ne (Get-AiInferenceSettingsHash $other)) 'Different prompt could reuse results.'
$other=$settings | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$other | Add-Member -NotePropertyName newPreprocessing -NotePropertyValue 'changed'
Check ((Get-AiInferenceSettingsHash $settings) -ne (Get-AiInferenceSettingsHash $other)) 'Unknown inference setting was silently ignored.'
$entry=@{path='private-fixture.png';sha256=('a'*64);hasGroundTruth=$true;expected=@{gender='female';role='caster';weaponOrStyle='bard';race='human'}}
$identity=Get-AiSettingsHash $settings
$manifest=@{root='private-fixture-library';settings=$settings;fingerprint=$identity;items=@($entry);references=@()}
$state=@{status='complete';cleanupComplete=$true;fingerprint=$identity}
$row=@{path=$entry.path;sha256=$entry.sha256;hasGroundTruth=$true;expected=$entry.expected;fingerprint=$identity;outcome='refusal';prediction=$null;response=@{done=$true;done_reason='stop';message=@{content='I cannot help with this image.'}}}
Write-AiCheckpoint $manifest (Join-Path $root 'manifest.json')
Write-AiCheckpoint $state (Join-Path $root 'status.json')
$itemPath=Join-Path $root 'items/one.json'
Write-AiCheckpoint $row $itemPath
$sourceHash=(Get-FileHash $itemPath).Hash
$cache=Read-AiBenchmarkCache $root
$copy=Copy-AiCachedResult $cache.records[0] $entry $entry.sha256 ('b'*64) $cache
Check ($copy.outcome -eq 'refusal' -and $copy.response.message.content -eq $row.response.message.content) 'A failed answer was lost during reuse.'
Check ($copy.fingerprint -eq ('b'*64) -and $copy.reusedFrom.recordSha256 -eq $sourceHash.ToLowerInvariant()) 'Copied result lacks source provenance.'
Check ((Get-FileHash $itemPath).Hash -eq $sourceHash) 'Reading/reusing a cache changed its original record.'
$changed=$entry | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$changed.expected.race='elf'
$blocked=$false
try{Copy-AiCachedResult $cache.records[0] $changed $entry.sha256 ('b'*64) $cache}catch{$blocked=$_.Exception.Message -like 'Reusable result labels*'}
Check $blocked 'Changed authoritative labels allowed reuse.'
$hold=[IO.File]::Open((Join-Path $root 'worker.lock'),'Open','ReadWrite','None')
$blocked=$false
try{Read-AiBenchmarkCache $root}catch{$blocked=$true}finally{$hold.Dispose()}
Check $blocked 'Active cache writer was not excluded.'
$row.response=$null;$row.outcome='decode_error';Write-AiCheckpoint $row $itemPath
Check ((Read-AiBenchmarkCache $root).records[0].outcome -eq 'decode_error') 'Decode failure disappeared from cache.'
$row.outcome='pending';Write-AiCheckpoint $row $itemPath
$blocked=$false
try{Read-AiBenchmarkCache $root}catch{$blocked=$_.Exception.Message -like 'Cached result has neither*'}
Check $blocked 'Incomplete result allowed reuse.'
$row.outcome='decode_error';Write-AiCheckpoint $row $itemPath
$state.status='running';Write-AiCheckpoint $state (Join-Path $root 'status.json')
$blocked=$false
try{Read-AiBenchmarkCache $root}catch{$blocked=$_.Exception.Message -like 'Finish the cached benchmark*'}
Check $blocked 'Unfinished run allowed reuse.'
$parentStop=Join-Path $root 'shared-STOP'
[IO.File]::WriteAllText($parentStop,'Fixture stop signal')
$blocked=$false
try{Invoke-LocalAiVision $null '' '' @{} (Join-Path $root 'absent-STOP') -AdditionalStopFile $parentStop}catch{$blocked=$_.Exception.Message -eq 'paused: Stop requested before inference.'}
Check $blocked 'Shared stop signal failed before model admission.'
Write-AiCheckpoint @{passed=$script:passed;inference=$false} (Join-Path $root 'result.json')
Write-Output "PASS: $script:passed cache checks; no inference. Evidence: $root"
