param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repo ('.cache\vision-smoke\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$runtime=$null
$report=[ordered]@{status='running';startedUtc=[DateTime]::UtcNow.ToString('o');cases=@();cpuOnly=$true;threads=2;context=4096;cleanupComplete=$false}
$reportPath=Join-Path $OutputDirectory 'result.json'
Write-AiCheckpoint $report $reportPath
try {
    $runtime=Start-LocalAiRuntime $repo $OutputDirectory
    Add-Type -AssemblyName System.Drawing
    $schema=@{type='object';properties=@{color=@{type='string';enum=@('red','blue','green','yellow','unknown')};shape=@{type='string';enum=@('square','circle','triangle','unknown')}};required=@('color','shape');additionalProperties=$false}
    foreach($case in @(@{color='red';shape='square'},@{color='blue';shape='circle'})){
        $bitmap=New-Object Drawing.Bitmap 256,256
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $stream=New-Object IO.MemoryStream
        $brush=New-Object Drawing.SolidBrush([Drawing.Color]::FromName($case.color))
        try {
            $graphics.Clear([Drawing.Color]::White)
            if($case.shape -eq 'square'){$graphics.FillRectangle($brush,48,48,160,160)}else{$graphics.FillEllipse($brush,48,48,160,160)}
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $base64=[Convert]::ToBase64String($stream.ToArray())
            [IO.File]::WriteAllBytes((Join-Path $OutputDirectory ($case.color+'.png')),$stream.ToArray())
        } finally {$brush.Dispose();$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
        Write-Output "Testing synthetic $($case.color) $($case.shape) on CPU..."
        $result=Invoke-LocalAiVision $runtime $base64 'Identify the single large colored shape on the white background. Return only its color and shape as JSON using the supplied schema.' $schema (Join-Path $OutputDirectory 'STOP') 600 64
        $prediction=$result.response.message.content | ConvertFrom-Json
        $passed=$prediction.color -eq $case.color -and $prediction.shape -eq $case.shape -and $result.response.done -eq $true -and $result.response.done_reason -eq 'stop'
        $report.cases+=@{expected=$case;prediction=$prediction;passed=$passed;response=$result.response;elapsedSeconds=$result.elapsedSeconds;sizeVram=$result.loadedModel.size_vram}
        Write-AiCheckpoint $report $reportPath
        if(-not $passed){throw 'Synthetic vision result did not match the fixture.'}
    }
    $null=Invoke-LocalAiMetadata $runtime '/api/generate' @{model=$runtime.Model;keep_alive=0}
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    do{$loaded=Invoke-LocalAiMetadata $runtime '/api/ps';if(@($loaded.models).Count -eq 0){break};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
    if(@($loaded.models).Count -ne 0){throw 'Model did not unload.'}
    $report.unloadVerified=$true
    $report.status='passed'
} catch {$report.status='failed';$report.error=$_.Exception.Message;throw}
finally {
    if($runtime){
        $report.peakWorkingSetBytes=$runtime.PeakWorkingSet;$report.peakPrivateBytes=$runtime.PeakPrivateBytes;$report.minimumAvailableBytes=$runtime.MinimumAvailable
        try { Write-AiCheckpoint ($runtime.Samples.ToArray()) (Join-Path $OutputDirectory 'memory-samples.json') }
        finally { Stop-LocalAiRuntime $runtime; $report.cleanupComplete=$true }
    }
    $report.completedUtc=[DateTime]::UtcNow.ToString('o');Write-AiCheckpoint $report $reportPath
    Write-Output "Checkpoint: $reportPath"
}
