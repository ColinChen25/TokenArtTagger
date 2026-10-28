$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
# The schema comes from the actual bridge, not a duplicate tag vocabulary.
$output='{}' | & (Join-Path $repo '.tools\dotnet\dotnet.exe') (Join-Path $repo 'src\TokenArtTagger.Agent\bin\Debug\net10.0\TokenArtTagger.Agent.dll') --call tag_schema
if($LASTEXITCODE -ne 0){throw 'Schema request failed.'}
$schema=($output | ConvertFrom-Json).structuredContent
$script:passed=0
function Check($Condition,[string]$Message){if(-not $Condition){throw $Message};$script:passed++}
function Result([string]$Text,[string]$Reason='stop') {Test-AiPrediction ([pscustomobject]@{done=$true;done_reason=$Reason;message=@{content=$Text}}) $schema}
$complete='{"tags":{"gender":"female","role":"caster","weaponOrStyle":"wizard","race":"human"},"evidence":"Robe and spellbook.","uncertainty":""}'
$partial='{"tags":{"gender":null,"role":null,"weaponOrStyle":null,"race":null},"evidence":"The character is obscured.","uncertainty":"Cannot determine anatomy or equipment."}'
$generic='{"tags":{"gender":"male","role":"generic","weaponOrStyle":null,"race":"elf"},"evidence":"Pointed ears; no role-specific equipment.","uncertainty":""}'
Check ((Result $complete).outcome -eq 'complete') 'Valid complete output rejected.'
Check ((Result $generic).outcome -eq 'complete') 'Generic null style rejected.'
Check ((Result $partial).outcome -eq 'partial') 'Ambiguous all-null output confused with refusal.'
Check ((Result ($partial.Replace('Cannot determine anatomy or equipment.','I cannot classify its race from the crop.'))).outcome -eq 'partial') 'Field-specific uncertainty confused with refusal.'
Check ((Result 'I cannot help with this image.').outcome -eq 'refusal') 'Plain-text refusal not detected.'
Check ((Result ($complete.Replace('Robe and spellbook.','I cannot help with this content.'))).outcome -eq 'refusal') 'JSON-contained refusal not detected.'
Check ((Result '{"status":"refused"}').outcome -eq 'refusal') 'Explicit refusal not detected.'
Check ((Result $complete 'length').outcome -eq 'invalid_output') 'Truncated output accepted.'
Check ((Result ($complete.Replace('wizard','bow'))).outcome -eq 'invalid_output') 'Invalid role/style pair accepted.'
Check ((Result ($complete.Replace('human','unknown'))).outcome -eq 'invalid_output') 'Unsupported race accepted.'
Check ((Result ($generic.Replace('"weaponOrStyle":null','"weaponOrStyle":"bow"'))).outcome -eq 'invalid_output') 'Generic with weapon accepted.'
Check ((Result ($complete.Replace('"race":"human"','"race":"human","extra":1'))).outcome -eq 'invalid_output') 'Extra tag fields accepted.'
Check ((Result ($complete.Replace('"gender":"female",',''))).outcome -eq 'invalid_output') 'Missing field accepted.'
$good=Result $complete
$messages=New-AiVisionMessages 'TARGET_PIXELS' 'Classify target.' @(@{imageBase64='REFERENCE_PIXELS';tags=$good.prediction.tags;path='PRIVATE_REFERENCE_PATH'})
Check ($messages.Count -eq 3 -and $messages[0].images[0] -eq 'REFERENCE_PIXELS' -and $messages[1].role -eq 'assistant' -and $messages[2].images[0] -eq 'TARGET_PIXELS') 'References and target are not distinct ordered visual messages.'
Check (($messages | ConvertTo-Json -Depth 10 -Compress) -notmatch 'PRIVATE_REFERENCE_PATH') 'Reference path leaked into model messages.'
Check ((New-AiVisionMessages 'TARGET' 'Prompt').Count -eq 1) 'No-reference request changed its message count.'
$blocked=$false
try {New-AiVisionMessages 'TARGET' 'Prompt' @(@{},@{},@{})}catch{$blocked=$_.Exception.Message -like 'At most two*'}
Check $blocked 'Low-memory reference-count bound was not enforced.'
Assert-AiPromptBudget 3840 256
foreach($tokens in @($null,0,3841)){
    $blocked=$false
    try {Assert-AiPromptBudget $tokens 256}catch{$blocked=$_.Exception.Message -like 'context_budget:*'}
    Check $blocked 'Missing or over-budget prompt token count was accepted.'
}
$summary=Get-AiEvaluationSummary @(
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='complete';prediction=$good.prediction;elapsedSeconds=10},
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='refusal';prediction=$null},
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='decode_error';prediction=$null},
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='partial';prediction=(Result $partial).prediction}
)
Check ($summary.attempted -eq 4 -and $summary.exactFullMatchRate -eq 0.25) 'Failures disappeared from accuracy denominator.'
Check ($summary.fields.race.precision -eq 1 -and $summary.fields.race.coverage -eq 0.25) 'Precision and abstention coverage confused.'
$median=Get-AiEvaluationSummary @(@{elapsedSeconds=10},@{elapsedSeconds=30})
Check ($median.medianSeconds -eq 20) 'Even-sized latency sample has an incorrect median.'
$left=@(
    @{sha256=('a'*64);hasGroundTruth=$true;expected=$good.prediction.tags;outcome='refusal'},
    @{sha256=('b'*64);hasGroundTruth=$true;expected=$good.prediction.tags;outcome='complete';prediction=$good.prediction}
)
$right=@(@{sha256=('a'*64);hasGroundTruth=$true;expected=$good.prediction.tags;outcome='complete';prediction=$good.prediction})
$comparison=Compare-AiEvaluation $left $right
Check ($comparison.paired -eq 1 -and $comparison.baselineOnly -eq 1 -and $comparison.baseline.exactFullMatches -eq 0 -and $comparison.candidate.exactFullMatches -eq 1 -and $comparison.improvedFullMatches -eq 1) 'Comparison used unequal samples or dropped a failure.'
$blocked=$false
try {Compare-AiEvaluation $left @($right[0],$right[0])}catch{$blocked=$_.Exception.Message -like 'Duplicate content*'}
Check $blocked 'Ambiguous duplicate pairing accepted.'
$conflict=@{sha256=('a'*64);hasGroundTruth=$true;expected=@{gender='male';role='caster';weaponOrStyle='wizard';race='human'};outcome='refusal'}
$blocked=$false
try {Compare-AiEvaluation $left @($conflict)}catch{$blocked=$_.Exception.Message -like 'Authoritative labels differ*'}
Check $blocked 'Conflicting source labels compared as equivalent ground truth.'
$blocked=$false
try {Invoke-LocalAiTool $null 'update_tags' @{}} catch {$blocked=$_.Exception.Message -like 'Read-only benchmark*'}
Check $blocked 'Benchmark MCP client allowed a mutating tool.'
$contract=Get-AiTagContract $schema
Check ((Get-AiTextHash $contract.Prompt) -ne (Get-AiTextHash ($contract.Prompt+'changed'))) 'Prompt cache key did not change.'
Check ((Get-AiSettingsHash @{b=@(1,$null);a=@{d=2;c=1}}) -eq (Get-AiSettingsHash @{a=@{c=1;d=2};b=@(1,$null)})) 'Settings key depends on dictionary enumeration order.'
Check ((Get-AiSettingsHash @{a=@(1,$null)}) -ne (Get-AiSettingsHash @{a=@(1)})) 'Null array entry disappeared from settings identity.'
$coupled=Get-AiTagContract $schema -ConstrainRoleStyle
Check ($coupled.Schema.properties.tags.anyOf.Count -eq ($schema.roles.Count+1)) 'Role grammar omits an allowed role or uncertainty.'
foreach($branch in $coupled.Schema.properties.tags.anyOf){
    $role=$branch.properties.role.const
    $styles=@($branch.properties.weaponOrStyle.enum | Where-Object {$null -ne $_})
    if($null -eq $role -or $role -eq 'generic'){Check ($styles.Count -eq 0) 'Unknown or generic role permits a style.'}
    else {Check ((Compare-Object $styles @($schema.stylesByRole.$role)).Count -eq 0) 'Role grammar differs from the app schema.'}
}
$focused=Get-AiTagContract $schema -Focus role-style
Check ($focused.Schema.properties.evidence.maxLength -eq 180 -and $focused.Schema.properties.uncertainty.maxLength -eq 96) 'Focused explanation length is unbounded.'
$bounded=Get-AiTagContract $schema -ConstrainRoleStyle -BoundEvidence
Check ($bounded.Schema.properties.evidence.maxLength -eq 180 -and $bounded.Version -ne $coupled.Version) 'Bounded control reused an unbounded cache identity.'
Check ((Test-AiPrediction @{done=$true;done_reason='stop';message=@{content=$complete.Replace('Robe and spellbook.',('x'*181))}} $schema -BoundEvidence).outcome -eq 'invalid_output') 'Overlong bounded explanation accepted.'
$astral=[char]::ConvertFromUtf32(0x1F9D9)
foreach($evidence in @(('x'*179+$astral),($astral*180))){
    Check ((Test-AiPrediction @{done=$true;done_reason='stop';message=@{content=$complete.Replace('Robe and spellbook.',$evidence)}} $schema -BoundEvidence).outcome -eq 'complete') 'Valid supplementary Unicode characters counted twice.'
}
foreach($evidence in @(($astral*181),('x'*179+'a'+[char]0x0301))){
    Check ((Test-AiPrediction @{done=$true;done_reason='stop';message=@{content=$complete.Replace('Robe and spellbook.',$evidence)}} $schema -BoundEvidence).outcome -eq 'invalid_output') 'Overlong Unicode code-point sequence accepted.'
}
foreach($length in 96..97){
    $unicodeText=$complete.Replace('"uncertainty":""',('"uncertainty":"'+('x'*($length-1))+$astral+'"'))
    $expected=if($length -eq 96){'complete'}else{'invalid_output'}
    Check ((Test-AiPrediction @{done=$true;done_reason='stop';message=@{content=$unicodeText}} $schema -BoundEvidence).outcome -eq $expected) 'Unicode uncertainty bound differs from schema.'
}
Check ($focused.Version -ne $coupled.Version -and $focused.Prompt -notmatch 'Allowed tags and role/style combinations:') 'Focused prompt reused the baseline contract.'
foreach($branch in $focused.Schema.properties.tags.anyOf){
    Check ($branch.properties.gender.type -eq 'null' -and $branch.properties.race.type -eq 'null') 'Role focus permits unrelated field predictions.'
}
$raceContract=Get-AiTagContract $schema -Focus race
Check ($raceContract.Schema.properties.tags.properties.role.type -eq 'null' -and $raceContract.Schema.properties.tags.properties.weaponOrStyle.type -eq 'null') 'Race focus permits a role/style.'
$focusedText=$complete.Replace('"gender":"female"','"gender":null').Replace('"race":"human"','"race":null')
$focusedPrediction=Test-AiPrediction @{done=$true;done_reason='stop';message=@{content=$focusedText}} $schema -Focus role-style
Check ($focusedPrediction.outcome -eq 'partial') 'Focused output incorrectly rejected or promoted to a complete tag set.'
Check ((Test-AiPrediction @{done=$true;done_reason='stop';message=@{content=$complete}} $schema -Focus role-style).outcome -eq 'invalid_output') 'Out-of-scope generated fields accepted.'
$focusRows=@(
    @{sha256=('a'*64);hasGroundTruth=$true;expected=$good.prediction.tags;outcome='partial';prediction=$focusedPrediction.prediction},
    @{sha256=('b'*64);hasGroundTruth=$true;expected=$good.prediction.tags;outcome='decode_error';prediction=$null},
    @{sha256=('c'*64);hasGroundTruth=$true;expected=$good.prediction.tags;outcome='partial';prediction=(Result $partial).prediction}
)
$focusedSummary=Get-AiEvaluationSummary $focusRows -Focus role-style
Check ($focusedSummary.exactFocusedMatches -eq 1 -and $focusedSummary.exactFocusedMatchRate -eq (1/3.0) -and $null -eq $focusedSummary.exactFullMatches -and $null -eq $focusedSummary.exactFullMatchRate) 'Focused matches count failures incorrectly or imply full-tag accuracy.'
Check ($focusedSummary.fields.Count -eq 2 -and $focusedSummary.fields.role.coverage -eq (1/3.0)) 'Focused report includes unrelated fields or loses abstention coverage.'
$focusComparison=Compare-AiEvaluation $left @($focusRows[0]) -Focus role-style
Check ($focusComparison.paired -eq 1 -and $focusComparison.improvedFocusedMatches -eq 1 -and $null -eq $focusComparison.improvedFullMatches) 'Focused pairing inflated full-match improvement.'
Write-Output "PASS: $script:passed local AI checks (schema, refusal, abstention, focus boundaries, truncation, failure denominators, read-only boundary, fingerprint)."
