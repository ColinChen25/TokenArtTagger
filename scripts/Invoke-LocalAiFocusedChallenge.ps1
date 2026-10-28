param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$TargetManifestFile,
    [ValidateRange(1,200)][int]$TaggedCount=32,
    [ValidateRange(0,200)][int]$MaxItemsPerArm=200,
    [string]$ControlCache,
    [string]$RoleStyleCache,
    [string]$RaceCache
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$TargetManifestFile=[IO.Path]::GetFullPath($TargetManifestFile)
foreach($path in @($OutputDirectory,$TargetManifestFile)){
    if(-not $path.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Challenge artifacts must remain inside .cache.'}
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$lock=[IO.File]::Open((Join-Path $OutputDirectory 'driver.lock'),'OpenOrCreate','ReadWrite','None')
$report=@{status='running';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');completedArms=0;newInference=0;reusedResults=0;cleanupComplete=$true;arms=@()}
$stop=Join-Path $OutputDirectory 'STOP'
try{
    $settings=@{version=1;targetManifestSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $TargetManifestFile).Hash.ToLowerInvariant();taggedCount=$TaggedCount;arms=@('control','role-style','race')}
    $identity=Get-AiSettingsHash $settings
    $planPath=Join-Path $OutputDirectory 'challenge-settings.json'
    if(Test-Path -LiteralPath $planPath){
        if((Get-Content -Raw -Encoding UTF8 -LiteralPath $planPath | ConvertFrom-Json).fingerprint -ne $identity){throw 'Challenge target selection changed; use a new output directory.'}
    }else{Write-AiCheckpoint @{fingerprint=$identity;settings=$settings} $planPath}
    $report.fingerprint=$identity
    $arms=@(@{name='control';focus='all';reuse=$ControlCache},@{name='role-style';focus='role-style';reuse=$RoleStyleCache},@{name='race';focus='race';reuse=$RaceCache})
    foreach($arm in $arms){
        if(Test-Path -LiteralPath $stop){throw 'paused: Challenge STOP exists.'}
        $child=Join-Path $OutputDirectory $arm.name
        $childStatus=Join-Path $child 'status.json'
        $report.currentArm=$arm.name;$report.currentDirectory=$child;$report.cleanupComplete=$false
        Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
        $arguments=@{Instance=$Instance;OutputDirectory=$child;TargetManifestFile=$TargetManifestFile;LabeledTargetsOnly=$true;TaggedCount=$TaggedCount;UntaggedCount=0;MaxItems=$MaxItemsPerArm;Focus=$arm.focus;AdditionalStopFile=$stop}
        if($arm.name -eq 'control'){$arguments.ConstrainRoleStyle=$true;$arguments.BoundEvidence=$true}
        if($arm.reuse){$arguments.ReuseDirectory=$arm.reuse}
        try{& (Join-Path $PSScriptRoot 'Invoke-LocalAiBenchmark.ps1') @arguments}
        finally{if(Test-Path -LiteralPath $childStatus){$report.cleanupComplete=[bool](Get-Content -Raw -Encoding UTF8 -LiteralPath $childStatus | ConvertFrom-Json).cleanupComplete}}
        $after=Get-Content -Raw -Encoding UTF8 -LiteralPath $childStatus | ConvertFrom-Json
        if($after.status -notin @('complete','checkpointed') -or -not $after.cleanupComplete){throw 'Challenge arm did not reach a clean checkpoint.'}
        $report.newInference+=$after.completedThisRun;$report.reusedResults+=$after.reusedThisRun
        $report.arms+=@(@{name=$arm.name;status=$after.status;completed=$after.summary.attempted;total=$after.total;fingerprint=$after.fingerprint})
        if($after.status -eq 'complete'){$report.completedArms++}
        Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
    }
    $report.status=if($report.completedArms -eq 3){'complete'}else{'checkpointed'}
}catch{$report.status=if($_.Exception.Message -match '^(paused|memory_pressure):'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{
    $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
    try{Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')}finally{$lock.Dispose()}
    Write-Output ('Challenge checkpoint: '+(Join-Path $OutputDirectory 'driver-status.json'))
}
