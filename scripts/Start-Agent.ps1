param([int]$Instance = 0, [string]$Call, [string]$ArgumentFile, [ValidateSet('Debug','Release')][string]$Configuration='Debug')
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $OutputEncoding
[Console]::OutputEncoding = $OutputEncoding
$repo = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repo ('src\TokenArtTagger.Agent\bin\'+$Configuration+'\net10.0\TokenArtTagger.Agent.dll')
$sdk = Join-Path $repo '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $dll)) { [Console]::Error.WriteLine('Build first with scripts/Build-Agent.ps1.'); exit 2 }
$bridgeArgs = @($dll)
if ($Instance -ne 0) { $bridgeArgs += @('--instance', $Instance.ToString()) }
if ($Call) { $bridgeArgs += @('--call', $Call) }
if ($ArgumentFile) {
    if (-not $Call) { [Console]::Error.WriteLine('-ArgumentFile requires -Call.'); exit 2 }
    $json = Get-Content -LiteralPath $ArgumentFile -Raw -Encoding UTF8 | ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress
    $json | & $sdk @bridgeArgs
} elseif ($Call) {
    '{}' | & $sdk @bridgeArgs
} else { & $sdk @bridgeArgs }
exit $LASTEXITCODE
