param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$Offline)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $repo '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget\packages'
$env:NUGET_SCRATCH = Join-Path $repo '.cache\nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:NUGET_SCRATCH | Out-Null
$config = Join-Path $repo 'NuGet.Config'
if ($Offline) {
    $config = Join-Path $repo '.cache\nuget-offline.config'
    '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath $config -Encoding UTF8
}
& $sdk restore (Join-Path $repo 'TokenArtTagger.slnx') --configfile $config -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $sdk build (Join-Path $repo 'TokenArtTagger.slnx') --no-restore -c $Configuration
exit $LASTEXITCODE
