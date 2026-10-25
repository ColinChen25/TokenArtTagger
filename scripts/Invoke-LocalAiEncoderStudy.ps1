param(
    [Parameter(Mandatory=$true)][int]$Instance,
    [Parameter(Mandatory=$true)][string]$DatasetDirectory,
    [Parameter(Mandatory=$true)][string]$PreviewDirectory,
    [Parameter(Mandatory=$true)][string]$FeatureDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string[]]$DevelopmentManifest,
    [string]$PreserveTestSplits,
    [string]$VariantAudit,
    [string]$VisionModelDirectory
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$privatePaths=@($DatasetDirectory,$PreviewDirectory,$FeatureDirectory,$OutputDirectory)+$DevelopmentManifest
if($PreserveTestSplits){$privatePaths+=$PreserveTestSplits}
if($VariantAudit){$privatePaths+=$VariantAudit}
if($VisionModelDirectory){$privatePaths+=$VisionModelDirectory}
foreach($path in $privatePaths){
    if(-not ([IO.Path]::GetFullPath($path)).StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'All study inputs/outputs must remain in .cache.'}
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$lock=[IO.File]::Open((Join-Path $OutputDirectory 'driver.lock'),'OpenOrCreate','ReadWrite','None')
$report=@{status='running';phase='previews';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o')}
function CheckStop{
    if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'STOP')){throw 'paused: Study STOP exists.'}
}
try{
    Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
    do{
        CheckStop
        $statusPath=Join-Path $PreviewDirectory 'status.json'
        if(-not (Test-Path -LiteralPath $statusPath)){break}
        $preview=Get-Content -Raw -Encoding UTF8 -LiteralPath $statusPath | ConvertFrom-Json
        if($preview.status -ne 'running'){break}
        $process=Get-Process -Id $preview.workerProcessId -ErrorAction Stop
        if($process.StartTime.ToUniversalTime().ToString('o') -ne $preview.workerStartedUtc){throw 'Preview worker identity changed.'}
        # Attach only to the verified existing exporter. Never start a second writer.
        Start-Sleep -Seconds 5
    }while($true)
    if($preview -and $preview.status -in @('failed','paused')){throw ('Inspect the preview checkpoint before resuming: '+$preview.status)}
    if(-not ($preview -and $preview.status -eq 'complete' -and $preview.cleanupComplete)){
        & (Join-Path $PSScriptRoot 'Export-LocalAiPreviews.ps1') -Instance $Instance -DatasetDirectory $DatasetDirectory -OutputDirectory $PreviewDirectory
    }
    CheckStop
    $report.phase='cpu-features';Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
    $python=Join-Path $repo '.tools\encoder-venv\Scripts\python.exe'
    $featureArguments=@((Join-Path $PSScriptRoot 'extract_encoder_features.py'),'--inputs',(Join-Path $PreviewDirectory 'inputs.json'),'--output',$FeatureDirectory,'--app-pid',[string]$Instance)
    if($VisionModelDirectory){$featureArguments+=@('--vision-model-directory',$VisionModelDirectory)}
    & $python @featureArguments
    if($LASTEXITCODE){throw 'Feature worker did not finish; inspect its status.'}
    CheckStop
    $report.phase='grouped-development-probe';Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')
    $arguments=@((Join-Path $PSScriptRoot 'train_encoder_probe.py'),'--dataset',$DatasetDirectory,'--features',$FeatureDirectory,'--inputs',$PreviewDirectory,'--output',$OutputDirectory,'--app-pid',[string]$Instance)
    foreach($manifest in $DevelopmentManifest){$arguments+=@('--development-manifest',$manifest)}
    if($PreserveTestSplits){$arguments+=@('--preserve-test-splits',$PreserveTestSplits)}
    if($VariantAudit){$arguments+=@('--variant-audit',$VariantAudit)}
    & $python @arguments
    if($LASTEXITCODE){throw 'Classifier study did not finish; inspect its status.'}
    $report.status='complete';$report.phase='development-results-ready'
}catch{$report.status=if($_.Exception.Message -like 'paused:*'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{try{Write-AiCheckpoint $report (Join-Path $OutputDirectory 'driver-status.json')}finally{$lock.Dispose()}}
