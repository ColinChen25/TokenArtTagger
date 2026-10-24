param(
    [Parameter(Mandatory=$true)][string]$BootstrapPython,
    [Parameter(Mandatory=$true)][string]$Proxy
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
$state=Join-Path $repo '.cache\encoder-setup'
New-Item -ItemType Directory -Force -Path $state | Out-Null
$lock=$null
$report=@{status='starting';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');inferenceTested=$false}
function Step([string]$Name){
    if(Test-Path -LiteralPath (Join-Path $state 'STOP')){throw 'paused: Setup stop requested.'}
    $report.phase=$Name;$report.status='running'
    Write-AiCheckpoint $report (Join-Path $state 'status.json')
}
try{
    $lock=[IO.File]::Open((Join-Path $state 'setup.lock'),'OpenOrCreate','ReadWrite','None')
    $venv=Join-Path $repo '.tools\encoder-venv'
    $python=Join-Path $venv 'Scripts\python.exe'
    Step 'isolated-environment'
    if(-not (Test-Path -LiteralPath $python)){
        & $BootstrapPython -m venv $venv
        if($LASTEXITCODE){throw 'Unable to create isolated Python environment.'}
    }
    Step 'pinned-cpu-dependencies'
    $installReport=Join-Path $state ('install-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json')
    & $python -m pip --isolated --disable-pip-version-check --cache-dir (Join-Path $state 'pip-cache') --proxy $Proxy install --only-binary=:all: --require-hashes -r (Join-Path $PSScriptRoot 'encoder-windows-py312.lock.txt') --report $installReport
    if($LASTEXITCODE){throw 'Encoder dependency installation failed; pip cache retained.'}
    & $python -m pip check
    if($LASTEXITCODE){throw 'Dependency compatibility check failed.'}
    & $python -m pip freeze | Set-Content -Encoding UTF8 (Join-Path $state 'installed-versions.txt')
    Step 'pinned-model-download'
    & $python (Join-Path $PSScriptRoot 'download_encoder.py') --proxy $Proxy
    if($LASTEXITCODE){throw 'Encoder download failed; partial files retained.'}
    $report.status='complete';$report.phase='installed-not-inference-tested'
}catch{
    $report.status=if($_.Exception.Message -like 'paused:*'){'paused'}else{'failed'}
    $report.error=$_.Exception.Message
    throw
}finally{
    if($lock){
        $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
        try{Write-AiCheckpoint $report (Join-Path $state 'status.json')}finally{$lock.Dispose()}
    }
}
