param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,32)][int]$MaxItems=32
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $OutputDirectory.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Experiment must remain inside .cache.'}
$lock=[IO.File]::Open((Join-Path $OutputDirectory 'driver.lock'),'OpenOrCreate','ReadWrite','None')
$report=@{status='running';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');completedThisRun=0;cleanupComplete=$true}
try{
    $plan=Get-Content -Raw -Encoding UTF8 (Join-Path $OutputDirectory 'plan.json') | ConvertFrom-Json
    $audit=Get-Content -Raw -Encoding UTF8 (Join-Path $OutputDirectory 'visual-audit.json') | ConvertFrom-Json
    if($audit.planFingerprint -ne $plan.fingerprint -or @($audit.items).Count -ne @($plan.items).Count){throw 'A current visual audit of all reference pairs is required.'}
    $report.fingerprint=$plan.fingerprint;$report.total=@($plan.items).Count;$report.completed=0
    foreach($item in $plan.items){
        if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'STOP')){throw 'paused: Experiment STOP exists.'}
        $decision=@($audit.items | Where-Object {$_.sha256 -eq $item.sha256 -and $_.referenceSha256 -eq $item.referenceSha256})
        if($decision.Count -ne 1 -or $decision[0].related -isnot [bool] -or $decision[0].related){throw 'Every selected pair must be visually reviewed as unrelated before inference.'}
        $child=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $item.childDirectory))
        $childPlan=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $item.planFile))
        if(-not $child.StartsWith($OutputDirectory+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $childPlan.StartsWith($OutputDirectory+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Child experiment path escapes output directory.'}
        $childSelection=Get-Content -Raw -Encoding UTF8 $childPlan | ConvertFrom-Json
        if(@($childSelection.targets).Count -ne 1 -or @($childSelection.references).Count -ne 1 -or $childSelection.targets[0] -cne $item.targetPath -or $childSelection.references[0] -cne $item.referencePath){throw 'Child plan differs from the visually audited selection.'}
        foreach($expected in @(@{path=$item.targetPath;sha256=$item.sha256},@{path=$item.referencePath;sha256=$item.referenceSha256})){
            $snapshot=@($childSelection.sources | Where-Object {$_.path -ceq $expected.path -and $_.sha256 -ceq $expected.sha256})
            if($snapshot.Count -ne 1){throw 'Child plan source snapshot differs from the audited selection.'}
        }
        $prior=$null;$statusPath=Join-Path $child 'status.json'
        if(Test-Path -LiteralPath $statusPath){$prior=Get-Content -Raw -Encoding UTF8 $statusPath | ConvertFrom-Json}
        if(-not ($prior -and $prior.status -eq 'complete' -and $prior.cleanupComplete) -and $report.completedThisRun -ge $MaxItems){$report.status='checkpointed';break}
        $report.currentTarget=$item.sha256;$report.currentDirectory=$child
        $report.cleanupComplete=$false
        Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
        # The existing runner owns MCP access, hashes, memory guards, STOP, inference and cleanup.
        try{
            & (Join-Path $PSScriptRoot 'Invoke-LocalAiBenchmark.ps1') -Instance $Instance -OutputDirectory $child -TaggedCount 1 -UntaggedCount 0 -MaxItems 1 -ReferencePlanFile $childPlan
        }finally{
            if(Test-Path -LiteralPath $statusPath){$report.cleanupComplete=[bool](Get-Content -Raw -Encoding UTF8 $statusPath | ConvertFrom-Json).cleanupComplete}
        }
        $after=Get-Content -Raw -Encoding UTF8 $statusPath | ConvertFrom-Json
        if($after.status -ne 'complete' -or -not $after.cleanupComplete){throw 'Child benchmark did not finish cleanly.'}
        $report.completed++;$report.completedThisRun+=$after.completedThisRun
        Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
    }
    if($report.completed -eq $report.total){$report.status='complete'}
    $report.cleanupComplete=$true
}catch{$report.status=if($_.Exception.Message -like 'paused:*'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{try{Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')}finally{$lock.Dispose()}}
