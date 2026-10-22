$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
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
$summary=Get-AiEvaluationSummary @(
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='complete';prediction=$good.prediction;elapsedSeconds=10},
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='refusal';prediction=$null},
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='decode_error';prediction=$null},
    @{hasGroundTruth=$true;expected=$good.prediction.tags;outcome='partial';prediction=(Result $partial).prediction}
)
Check ($summary.attempted -eq 4 -and $summary.exactFullMatchRate -eq 0.25) 'Failures disappeared from accuracy denominator.'
Check ($summary.fields.race.precision -eq 1 -and $summary.fields.race.coverage -eq 0.25) 'Precision and abstention coverage confused.'
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
Write-Output "PASS: $script:passed local AI checks (schema, refusal, abstention, truncation, failure denominators, read-only boundary, fingerprint)."
