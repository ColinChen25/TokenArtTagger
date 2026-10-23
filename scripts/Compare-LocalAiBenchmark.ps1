param(
    [Parameter(Mandatory=$true)][string]$BaselineDirectory,
    [Parameter(Mandatory=$true)][string]$CandidateDirectory
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
function Read-Run([string]$Directory){
    $Directory=[IO.Path]::GetFullPath($Directory)
    if(-not $Directory.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Benchmark directories must remain inside .cache.'}
    $manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Directory 'manifest.json') | ConvertFrom-Json
    $status=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Directory 'status.json') | ConvertFrom-Json
    if($status.status -ne 'complete' -or -not $status.cleanupComplete){throw 'Finish each benchmark and verify cleanup before comparing.'}
    $paths=@{};foreach($entry in $manifest.items){$paths[$entry.path]=$entry}
    $seen=@{}
    $records=@(foreach($file in Get-ChildItem -LiteralPath (Join-Path $Directory 'items') -Filter '*.json'){
        $row=Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
        if($row.fingerprint -ne $manifest.fingerprint){continue}
        if(-not $paths.ContainsKey($row.path)){throw 'Result does not belong to the saved sample.'}
        if($seen.ContainsKey($row.path)){throw 'Multiple results exist for one sample path.'}
        $seen[$row.path]=$true
        $entry=$paths[$row.path]
        if($row.sha256 -ne $entry.sha256 -or $row.hasGroundTruth -ne $entry.hasGroundTruth){throw 'Result identity differs from the manifest.'}
        foreach($field in @('gender','role','weaponOrStyle','race')){
            if($row.expected.$field -cne $entry.expected.$field){throw 'Result source labels differ from the manifest.'}
        }
        $row
    })
    if($records.Count -ne $paths.Count){throw 'Completed run does not contain exactly one result per sample.'}
    return @{directory=$Directory;manifest=$manifest;records=$records}
}
$baseline=Read-Run $BaselineDirectory
$candidate=Read-Run $CandidateDirectory
if($baseline.manifest.root -ne $candidate.manifest.root){throw 'Comparison libraries differ.'}
$comparison=Compare-AiEvaluation $baseline.records $candidate.records
$comparison.baselineFingerprint=$baseline.manifest.fingerprint
$comparison.candidateFingerprint=$candidate.manifest.fingerprint
$output=Join-Path $candidate.directory 'comparison.json'
Write-AiCheckpoint $comparison $output
Write-Output "Paired comparison: $output"
