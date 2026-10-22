$ErrorActionPreference = 'Stop'
if (-not ('TokenArtTagger.MemoryProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TokenArtTagger {
 public static class MemoryProbe {
  [StructLayout(LayoutKind.Sequential)] public struct Status { public uint Length,Load; public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual; }
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref Status value);
  public static Status Read() { var s=new Status(); s.Length=(uint)Marshal.SizeOf(s); if(!GlobalMemoryStatusEx(ref s)) throw new System.ComponentModel.Win32Exception(); return s; }
 }
}
'@
}

function Write-AiCheckpoint($Value, [string]$Path) {
    $temporary = $Path + '.tmp'
    [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $Value -Depth 40), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary,$Path,($Path + '.previous')) }
    else { [IO.File]::Move($temporary,$Path) }
}

function Get-AiMemory { [TokenArtTagger.MemoryProbe]::Read() }

function Start-LocalAiRuntime([string]$Repository, [string]$RunDirectory, [int]$Port = 11435) {
    $runtime = Join-Path $Repository '.tools\ollama\0.40.8\ollama.exe'
    $models = Join-Path $Repository '.cache\models\ollama'
    $manifest = Join-Path $models 'manifests\registry.ollama.ai\library\qwen3-vl\2b-instruct-q4_K_M'
    $digest = 'afc393e3572c07db0bc84aa460d928a16c75f3fbb9ca9e0dccc4fbd2dafb573e'
    if (-not (Test-Path -LiteralPath $runtime) -or -not (Test-Path -LiteralPath $manifest)) { throw 'Provision the pinned local runtime and model first.' }
    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) { throw 'Model manifest changed.' }
    $manifestData = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifest | ConvertFrom-Json
    foreach ($blob in @($manifestData.config) + @($manifestData.layers)) {
        if ($blob.digest -notmatch '^sha256:[0-9a-f]{64}$') { throw 'Invalid cached blob digest.' }
        $file = Join-Path $models ('blobs\sha256-' + $blob.digest.Substring(7))
        if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -ne $blob.size -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $blob.digest.Substring(7)) { throw 'Cached model blob failed verification.' }
    }
    if (@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port).Count) { throw "Port $Port is occupied; existing services are never reused or stopped." }
    $profile = Join-Path $RunDirectory ('runtime-profile-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $profile | Out-Null
    $settings = @{ OLLAMA_HOST="127.0.0.1:$Port"; OLLAMA_MODELS=$models; OLLAMA_NO_CLOUD='1'; OLLAMA_NUM_PARALLEL='1'; OLLAMA_MAX_LOADED_MODELS='1'; OLLAMA_MAX_QUEUE='1'; OLLAMA_CONTEXT_LENGTH='4096'; OLLAMA_KEEP_ALIVE='0'; CUDA_VISIBLE_DEVICES='-1'; ROCR_VISIBLE_DEVICES='-1'; OLLAMA_VULKAN='0'; USERPROFILE=$profile; APPDATA=$profile; LOCALAPPDATA=$profile }
    $saved = @{}
    $server = $null
    try {
        try {
            foreach ($name in $settings.Keys) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process'); [Environment]::SetEnvironmentVariable($name,$settings[$name],'Process') }
            $server = Start-Process -FilePath $runtime -ArgumentList 'serve' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $profile 'server-out.log') -RedirectStandardError (Join-Path $profile 'server-error.log')
            $null = $server.Handle
        } finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
        $server.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
        Add-Type -AssemblyName System.Net.Http
        $handler = New-Object Net.Http.HttpClientHandler
        $handler.UseProxy = $false
        $client = New-Object Net.Http.HttpClient($handler)
        $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
        $context = @{ Server=$server; ServerPath=$server.Path; Started=$server.StartTime; Client=$client; Handler=$handler; BaseUri="http://127.0.0.1:$Port"; Model='qwen3-vl:2b-instruct-q4_K_M'; Digest=$digest; Profile=$profile; Owned=@{}; Samples=(New-Object Collections.Generic.List[object]); PeakWorkingSet=0L; PeakPrivateBytes=0L; PeakCombinedWorkingSet=0L; MinimumAvailable=[long]::MaxValue }
        $context.Owned[$server.Id]=$server.StartTime.Ticks
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ($true) {
            if ($server.HasExited) { throw "Local runtime exited. Logs: $profile" }
            try { $version = Invoke-LocalAiMetadata $context '/api/version'; break } catch { if ([DateTime]::UtcNow -ge $deadline) { throw }; Start-Sleep -Milliseconds 200 }
        }
        $listeners = @(Get-NetTCPConnection -OwningProcess $server.Id -State Listen -ErrorAction Stop)
        if ($listeners.Count -ne 1 -or $listeners[0].LocalAddress -ne '127.0.0.1' -or $listeners[0].LocalPort -ne $Port) { throw 'Unexpected server listener.' }
        $context.Version = $version.version
        if($version.version -ne '0.40.8'){throw 'Runtime version differs from the pinned setup.'}
        Write-AiCheckpoint @{serverProcessId=$server.Id;startedUtc=$server.StartTime.ToUniversalTime().ToString('o');profile=$profile;model=$context.Model;digest=$digest;version=$version.version} (Join-Path $RunDirectory 'runtime.json')
        return $context
    } catch {
        if ($server -and -not $server.HasExited) { $server.Kill(); [void]$server.WaitForExit(5000) }
        if($client){$client.Dispose()};if($handler){$handler.Dispose()}
        throw
    }
}

function Invoke-LocalAiMetadata($Runtime, [string]$Path, $Body = $null) {
    $cts = New-Object Threading.CancellationTokenSource
    $cts.CancelAfter(10000)
    $content = $null
    $response = $null
    try {
        if ($null -eq $Body) { $response=$Runtime.Client.GetAsync(($Runtime.BaseUri+$Path),$cts.Token).GetAwaiter().GetResult() }
        else {
            $content=New-Object Net.Http.StringContent(($Body | ConvertTo-Json -Depth 40 -Compress),[Text.Encoding]::UTF8,'application/json')
            $response=$Runtime.Client.PostAsync(($Runtime.BaseUri+$Path),$content,$cts.Token).GetAwaiter().GetResult()
        }
        $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "Local runtime HTTP $([int]$response.StatusCode): $text" }
        return ($text | ConvertFrom-Json)
    } finally { if($content){$content.Dispose()}; if($response){$response.Dispose()}; $cts.Dispose() }
}

function Get-LocalAiSample($Runtime) {
    # Only descendants of this server belong to this workload. Never stop unrelated Ollama processes.
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'ollama.exe' OR Name = 'llama-server.exe'")
    $ids = @($Runtime.Server.Id)
    do {
        $previous = $ids.Count
        $ids = @($ids + @($processes | Where-Object { $_.ParentProcessId -in $ids } | ForEach-Object ProcessId) | Select-Object -Unique)
    } while ($ids.Count -gt $previous)
    $working = 0L; $private = 0L;$baselineWorking=0L;$baselinePrivate=0L
    foreach ($id in @($ids + $PID + @($Runtime.AdditionalProcessIds) | Where-Object {$_} | Select-Object -Unique)) {
        try {
            $p=Get-Process -Id $id -ErrorAction Stop
            $working+=$p.WorkingSet64;$private+=$p.PrivateMemorySize64
            if($id -in $ids){$Runtime.Owned[$id]=$p.StartTime.Ticks;$p.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal}
            if($Runtime.AppBaseline -and $id -eq $Runtime.AppBaseline.processId){
                $baselineWorking=[math]::Min($p.WorkingSet64,$Runtime.AppBaseline.workingSetBytes)
                $baselinePrivate=[math]::Min($p.PrivateMemorySize64,$Runtime.AppBaseline.privateBytes)
            }
            $p.Dispose()
        } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
    }
    $memory=Get-AiMemory
    $sample=[pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');workingSetBytes=($working-$baselineWorking);privateBytes=($private-$baselinePrivate);combinedWorkingSetBytes=$working;existingAppWorkingSetBytes=$baselineWorking;availableBytes=$memory.AvailablePhysical;commitAvailableBytes=$memory.AvailablePageFile}
    $Runtime.PeakWorkingSet=[math]::Max($Runtime.PeakWorkingSet,$sample.workingSetBytes)
    $Runtime.PeakPrivateBytes=[math]::Max($Runtime.PeakPrivateBytes,$sample.privateBytes)
    $Runtime.PeakCombinedWorkingSet=[math]::Max($Runtime.PeakCombinedWorkingSet,$working)
    $Runtime.MinimumAvailable=[math]::Min($Runtime.MinimumAvailable,$memory.AvailablePhysical)
    if($Runtime.Samples.Count -lt 10000){$Runtime.Samples.Add($sample)}
    return $sample
}

function Invoke-LocalAiVision($Runtime, [string]$ImageBase64, [string]$Prompt, $Schema, [string]$StopFile, [int]$TimeoutSeconds = 600, [int]$MaxTokens = 256) {
    $memory=Get-AiMemory
    if ($memory.AvailablePhysical -lt 8GB) { throw 'memory_pressure: Need 8 GiB available before admitting an image (4 GiB workload plus 4 GiB reserve).' }
    $body=@{model=$Runtime.Model;stream=$false;keep_alive='2m';messages=@(@{role='user';content=$Prompt;images=@($ImageBase64)});format=$Schema;options=@{temperature=0;seed=42;num_ctx=4096;num_predict=$MaxTokens;num_thread=2;num_gpu=0}}
    $cts=New-Object Threading.CancellationTokenSource
    $cts.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
    $content=New-Object Net.Http.StringContent(($body | ConvertTo-Json -Depth 40 -Compress),[Text.Encoding]::UTF8,'application/json')
    $response=$null
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try {
        $pending=$Runtime.Client.PostAsync(($Runtime.BaseUri+'/api/chat'),$content,$cts.Token)
        while (-not $pending.IsCompleted) {
            $sample=Get-LocalAiSample $Runtime
            $reason=if(Test-Path -LiteralPath $StopFile){'Stop requested'}elseif($sample.availableBytes -lt 4GB){'Available RAM below 4 GiB reserve'}elseif($sample.workingSetBytes -gt 4GB){'Incremental workload above 4 GiB'}elseif($sample.commitAvailableBytes -lt 4GB){'Commit headroom below 4 GiB'}else{$null}
            if($reason){$cts.Cancel();throw "paused: $reason; result was not accepted."}
            Start-Sleep -Milliseconds 1000
        }
        $response=$pending.GetAwaiter().GetResult()
        $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "Local runtime HTTP $([int]$response.StatusCode): $text" }
        $value=$text | ConvertFrom-Json
        $loaded=Invoke-LocalAiMetadata $Runtime '/api/ps'
        $running=@($loaded.models | Where-Object { $_.name -eq $Runtime.Model -or $_.model -eq $Runtime.Model })
        if ($running.Count -ne 1 -or $running[0].size_vram -ne 0 -or $running[0].digest -ne $Runtime.Digest) { throw 'CPU-only pinned model execution could not be verified.' }
        return [pscustomobject]@{response=$value;elapsedSeconds=$watch.Elapsed.TotalSeconds;loadedModel=$running[0]}
    } finally {
        $cts.Cancel()
        if($response){$response.Dispose()}; $content.Dispose(); $cts.Dispose()
    }
}

function Clear-LocalAiModel($Runtime) {
    $null=Invoke-LocalAiMetadata $Runtime '/api/generate' @{model=$Runtime.Model;keep_alive=0}
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        $loaded=Invoke-LocalAiMetadata $Runtime '/api/ps'
        if(@($loaded.models).Count -eq 0){return}
        Start-Sleep -Milliseconds 200
    } while([DateTime]::UtcNow -lt $deadline)
    throw 'Local model did not unload.'
}

function Stop-LocalAiRuntime($Runtime) {
    if(-not $Runtime){return}
    try { $null=Invoke-LocalAiMetadata $Runtime '/api/generate' @{model=$Runtime.Model;keep_alive=0} } catch { }
    # Capture descendants before stopping the server; track their creation times to avoid PID reuse.
    $owned=@()
    $runtimePath=$Runtime.ServerPath
    $runtimeDirectory=(Split-Path $runtimePath -Parent)+'\'
    $cleanupErrors=New-Object Collections.Generic.List[string]
    try {
        foreach($id in $Runtime.Owned.Keys) {
            $p=$null
            try {
                $p=Get-Process -Id $id -ErrorAction Stop
                # Old workers may have exited or their PIDs may have been reused after unloading.
                if(-not $p.HasExited -and $p.StartTime.Ticks -eq $Runtime.Owned[$id]){
                    $path=$p.Path
                    if($path -and $path.StartsWith($runtimeDirectory,[StringComparison]::OrdinalIgnoreCase)){$owned+=$p;$p=$null}
                    elseif(-not $p.HasExited){$cleanupErrors.Add("Could not verify runtime process $id.")}
                }
            } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
            catch {if(-not $p -or -not $p.HasExited){$cleanupErrors.Add($_.Exception.Message)}}
            finally {if($p){$p.Dispose()}}
        }
        foreach($p in $owned | Sort-Object Id -Descending) {
            try {if(-not $p.HasExited){$p.Kill();if(-not $p.WaitForExit(5000)){$cleanupErrors.Add('Runtime process did not stop.')}}}
            catch {$cleanupErrors.Add($_.Exception.Message)}
            finally {$p.Dispose()}
        }
    } finally {$Runtime.Client.Dispose();$Runtime.Handler.Dispose();$Runtime.Server.Dispose()}
    if($cleanupErrors.Count){throw ('Runtime cleanup failed: '+($cleanupErrors -join '; '))}
}

Export-ModuleMember -Function Write-AiCheckpoint,Get-AiMemory,Start-LocalAiRuntime,Invoke-LocalAiMetadata,Invoke-LocalAiVision,Get-LocalAiSample,Clear-LocalAiModel,Stop-LocalAiRuntime
