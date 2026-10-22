$ErrorActionPreference = 'Stop'

# The benchmark is an ordinary MCP client. It never parses image names or decodes files.
function Start-LocalAiMcp([string]$Repository, [int]$Instance) {
    $bridge = New-Object Diagnostics.Process
    $bridge.StartInfo.FileName = Join-Path $Repository '.tools\dotnet\dotnet.exe'
    $bridge.StartInfo.Arguments = '"' + (Join-Path $Repository 'src\TokenArtTagger.Agent\bin\Debug\net10.0\TokenArtTagger.Agent.dll') + '" --instance ' + $Instance
    $bridge.StartInfo.UseShellExecute = $false
    $bridge.StartInfo.CreateNoWindow = $true
    $bridge.StartInfo.RedirectStandardInput = $true
    $bridge.StartInfo.RedirectStandardOutput = $true
    $bridge.StartInfo.RedirectStandardError = $true
    [void]$bridge.Start()
    $client = @{ Process=$bridge; NextId=0; ErrorTask=$bridge.StandardError.ReadToEndAsync(); Faulted=$false }
    try {
        $null = Invoke-AiRpc $client 'initialize' @{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='local-ai-benchmark';version='1'}}
        Send-AiMcpLine $client '{"jsonrpc":"2.0","method":"notifications/initialized"}'
        return $client
    } catch { Stop-LocalAiMcp $client; throw }
}

function Send-AiMcpLine($Client, [string]$Line) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Line + "`n")
    $Client.Process.StandardInput.BaseStream.Write($bytes,0,$bytes.Length)
    $Client.Process.StandardInput.BaseStream.Flush()
}

function Invoke-AiRpc($Client, [string]$Method, $Parameters) {
    if($Client.Faulted){throw 'MCP connection faulted; start a new client before retrying.'}
    $Client.NextId++
    Send-AiMcpLine $Client (@{jsonrpc='2.0';id=$Client.NextId;method=$Method;params=$Parameters} | ConvertTo-Json -Depth 30 -Compress)
    $read=$Client.Process.StandardOutput.ReadLineAsync()
    if(-not $read.Wait(135000)){ $Client.Faulted=$true; throw "MCP timeout: $Method" }
    if(-not $read.Result){ $Client.Faulted=$true; throw 'MCP bridge closed its output.' }
    $response=$read.Result | ConvertFrom-Json
    if($response.id -ne $Client.NextId){$Client.Faulted=$true;throw 'MCP response ID mismatch.'}
    if($response.error){throw ($response.error | ConvertTo-Json -Compress)}
    return $response.result
}

function Invoke-LocalAiTool($Client, [string]$Name, $Arguments = @{}) {
    if($Name -notin @('app_status','tag_schema','list_images','image_details','view_image')) { throw "Read-only benchmark cannot call $Name." }
    $result=Invoke-AiRpc $Client 'tools/call' @{name=$Name;arguments=$Arguments}
    if($result.isError){throw ($result.structuredContent | ConvertTo-Json -Compress)}
    return $result
}

function Stop-LocalAiMcp($Client) {
    if(-not $Client){return}
    $p=$Client.Process
    try { if(-not $p.HasExited){$p.StandardInput.Close();if(-not $p.WaitForExit(3000)){$p.Kill();[void]$p.WaitForExit(5000)}} }
    finally {$p.Dispose()}
}

Export-ModuleMember -Function Start-LocalAiMcp,Invoke-LocalAiTool,Stop-LocalAiMcp
