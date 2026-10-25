param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$privateRoot=[IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $OutputDirectory.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Grounding checks must remain inside .cache.'}
New-Item -ItemType Directory -Force -Path $OutputDirectory,(Join-Path $OutputDirectory 'items') | Out-Null
$lock=[IO.File]::Open((Join-Path $OutputDirectory 'worker.lock'),'OpenOrCreate','ReadWrite','None')
$runtime=$null
$report=@{status='running';workerProcessId=$PID;workerStartedUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o');completedThisRun=0;cleanupComplete=$false}
try{
    $stop=Join-Path $OutputDirectory 'STOP'
    if(Test-Path -LiteralPath $stop){throw 'paused: STOP marker exists.'}
    Add-Type -AssemblyName System.Drawing
    $fixtures=@(@{color='red';shape='square'},@{color='blue';shape='circle'})
    $images=@{}
    foreach($case in $fixtures){
        $bitmap=New-Object Drawing.Bitmap 256,256
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $stream=New-Object IO.MemoryStream
        $brush=New-Object Drawing.SolidBrush([Drawing.Color]::FromName($case.color))
        try{
            $graphics.Clear([Drawing.Color]::White)
            if($case.shape -eq 'square'){$graphics.FillRectangle($brush,48,48,160,160)}else{$graphics.FillEllipse($brush,48,48,160,160)}
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $bytes=$stream.ToArray();$images[$case.color]=[Convert]::ToBase64String($bytes)
            [IO.File]::WriteAllBytes((Join-Path $OutputDirectory ($case.color+'.png')),$bytes)
        }finally{$brush.Dispose();$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
    }
    $schema=@{type='object';properties=@{color=@{type='string';enum=@('red','blue','green','yellow','unknown')};shape=@{type='string';enum=@('square','circle','triangle','unknown')}};required=@('color','shape');additionalProperties=$false}
    $prompt='Classify only the final TARGET image in this last message. Do not describe the preceding reference image. Identify the single large colored shape on the white background. Return only its color and shape as JSON using the supplied schema.'
    $settings=@{version='reference-grounding-v1';prompt=$prompt;schema=$schema;modelDigest='afc393e3572c07db0bc84aa460d928a16c75f3fbb9ca9e0dccc4fbd2dafb573e';runtime='0.40.8';cpuOnly=$true;maxTokens=64;fixtures=$fixtures;imageHashes=@(foreach($case in $fixtures){(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutputDirectory ($case.color+'.png'))).Hash.ToLowerInvariant()})}
    $fingerprint=Get-AiSettingsHash $settings
    $settingsPath=Join-Path $OutputDirectory 'settings.json'
    if(Test-Path -LiteralPath $settingsPath){
        $saved=Get-Content -Raw -Encoding UTF8 $settingsPath | ConvertFrom-Json
        if($saved.fingerprint -ne $fingerprint){throw 'Grounding settings changed; use a new output directory.'}
    }else{Write-AiCheckpoint @{fingerprint=$fingerprint;settings=$settings} $settingsPath}
    $report.fingerprint=$fingerprint;$report.cases=@()
    Write-AiCheckpoint $report (Join-Path $OutputDirectory 'status.json')
    for($i=0;$i -lt 2;$i++){
        if(Test-Path -LiteralPath $stop){throw 'paused: Stop requested.'}
        $target=$fixtures[$i];$reference=$fixtures[1-$i]
        $path=Join-Path $OutputDirectory ('items/'+$target.color+'.json')
        if(Test-Path -LiteralPath $path){
            $saved=Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json
            if($saved.fingerprint -ne $fingerprint){throw 'Grounding result fingerprint changed.'}
            $report.cases+=@($saved);continue
        }
        if(-not $runtime){$runtime=Start-LocalAiRuntime $repo $OutputDirectory}
        Write-Output ('Checking final target '+$target.color+' after reference '+$reference.color+'...')
        $examples=@(@{imageBase64=$images[$reference.color];tags=$reference})
        $answer=Invoke-LocalAiVision $runtime $images[$target.color] $prompt $schema $stop 600 64 $examples
        $prediction=$answer.response.message.content | ConvertFrom-Json
        $correct=$answer.response.done -and $answer.response.done_reason -eq 'stop' -and $prediction.color -ceq $target.color -and $prediction.shape -ceq $target.shape
        Clear-LocalAiModel $runtime
        $record=@{fingerprint=$fingerprint;target=$target;reference=$reference;prediction=$prediction;correct=$correct;response=$answer.response;elapsedSeconds=$answer.elapsedSeconds;unloadVerified=$true;sizeVram=$answer.loadedModel.size_vram}
        Write-AiCheckpoint $record $path
        $report.cases+=@($record);$report.completedThisRun++
        Write-AiCheckpoint $report (Join-Path $OutputDirectory 'status.json')
    }
    $report.correct=@($report.cases | Where-Object correct).Count;$report.total=2;$report.allPassed=$report.correct -eq 2;$report.status='complete'
}catch{$report.status=if($_.Exception.Message -match '^(paused|memory_pressure):'){'paused'}else{'failed'};$report.error=$_.Exception.Message;throw}
finally{
    try{
        if($runtime){
            $report.peakWorkingSetBytes=$runtime.PeakWorkingSet;$report.peakPrivateBytes=$runtime.PeakPrivateBytes
            try{Write-AiCheckpoint ($runtime.Samples.ToArray()) (Join-Path $OutputDirectory 'memory.json')}
            finally{Stop-LocalAiRuntime $runtime}
        }
        $report.cleanupComplete=$true
    }finally{
        try{
            Write-AiCheckpoint $report (Join-Path $OutputDirectory 'status.json')
            Write-AiCheckpoint $report (Join-Path $OutputDirectory ('session-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json'))
        }finally{$lock.Dispose()}
    }
    Write-Output ('Grounding checkpoint: '+(Join-Path $OutputDirectory 'status.json'))
}
