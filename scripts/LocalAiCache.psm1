$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1')

function Get-AiInferenceSettingsHash($Settings){
    $inference=[ordered]@{}
    $names=if($Settings -is [Collections.IDictionary]){@($Settings.Keys)}else{@($Settings.PSObject.Properties.Name)}
    foreach($name in $names){
        if($name -notin @('sourceManifestFingerprint','targetSelectionHash')){$inference[$name]=$Settings.$name}
    }
    Get-AiSettingsHash $inference
}

function Read-AiBenchmarkCache([string]$Directory){
    $repo=Split-Path $PSScriptRoot -Parent
    $Directory=[IO.Path]::GetFullPath($Directory)
    if(-not $Directory.StartsWith(([IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Benchmark directories must remain inside .cache.'}
    $lock=[IO.File]::Open((Join-Path $Directory 'worker.lock'),'Open','Read','None')
    try{
        $manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Directory 'manifest.json') | ConvertFrom-Json
        $status=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Directory 'status.json') | ConvertFrom-Json
        if($status.status -ne 'complete' -or -not $status.cleanupComplete){throw 'Finish the cached benchmark and verify cleanup before reuse.'}
        if($status.fingerprint -ne $manifest.fingerprint -or (Get-AiSettingsHash $manifest.settings) -ne $manifest.fingerprint){throw 'Cached benchmark settings identity is inconsistent.'}
        $paths=@{};$hashes=@{}
        foreach($entry in $manifest.items){
            if(-not $entry.path -or $entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $paths.ContainsKey($entry.path) -or $hashes.ContainsKey($entry.sha256)){throw 'Cached sample has invalid or duplicate identities.'}
            $paths[$entry.path]=$entry;$hashes[$entry.sha256]=$true
        }
        $seen=@{};$digests=@{}
        $records=@(foreach($file in Get-ChildItem -LiteralPath (Join-Path $Directory 'items') -Filter '*.json'){
            $row=Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
            if($row.fingerprint -ne $manifest.fingerprint){continue}
            if(-not $paths.ContainsKey($row.path) -or $seen.ContainsKey($row.path)){throw 'Cached result does not uniquely belong to the sample.'}
            $entry=$paths[$row.path];$seen[$row.path]=$true
            if($row.sha256 -ne $entry.sha256 -or $row.hasGroundTruth -ne $entry.hasGroundTruth){throw 'Cached result identity differs from its manifest.'}
            foreach($field in @('gender','role','weaponOrStyle','race')){
                if($row.expected.$field -cne $entry.expected.$field){throw 'Cached result labels differ from its manifest.'}
            }
            if(-not $row.response -and $row.outcome -ne 'decode_error'){throw 'Cached result has neither raw model output nor a decoding failure.'}
            $digests[$row.sha256]=(Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
            $row
        })
        if(-not $paths.Count -or $records.Count -ne $paths.Count){throw 'Completed cache lacks exactly one result per sample.'}
        return @{directory=$Directory;manifest=$manifest;records=$records;recordDigests=$digests}
    }finally{$lock.Dispose()}
}

function Copy-AiCachedResult($Row,$Entry,[string]$Hash,[string]$Fingerprint,$Source){
    if($Row.sha256 -cne $Hash -or $Row.path -cne $Entry.path -or $Row.hasGroundTruth -ne $Entry.hasGroundTruth){throw 'Reusable result identity differs from the current target.'}
    foreach($field in @('gender','role','weaponOrStyle','race')){
        if($Row.expected.$field -cne $Entry.expected.$field){throw 'Reusable result labels differ from the current target.'}
    }
    $copy=$Row | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $copy.fingerprint=$Fingerprint
    $copy | Add-Member -NotePropertyName reusedFrom -NotePropertyValue @{
        directory=$Source.directory;fingerprint=$Source.manifest.fingerprint;recordSha256=$Source.recordDigests[$Hash]
    } -Force
    return $copy
}

Export-ModuleMember -Function Get-AiInferenceSettingsHash,Read-AiBenchmarkCache,Copy-AiCachedResult
