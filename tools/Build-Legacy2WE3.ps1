# Optional local migration tool. Does not copy the original catenary engine or install anything.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OriginalSourceDirectory,
      [Parameter(Mandatory=$true)][string]$CoreReadyDirectory,
      [Parameter(Mandatory=$true)][string]$GameDirectory,
      [ValidateSet('Release','Debug')][string]$Configuration='Release')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Root=Split-Path -Parent $PSScriptRoot
function RequireFile([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing file: $Path" } }
function RunDotnet([string[]]$Arguments,[string]$Log) {
    $saved=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try { & dotnet @Arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $Log -Append | Out-Host; $code=$LASTEXITCODE }
    finally { $ErrorActionPreference=$saved }
    if ($code -ne 0) { throw "dotnet failed ($code). See $Log" }
}
try {
    $Source=(Resolve-Path -LiteralPath $OriginalSourceDirectory).ProviderPath
    $Core=(Resolve-Path -LiteralPath $CoreReadyDirectory).ProviderPath
    $Game=(Resolve-Path -LiteralPath $GameDirectory).ProviderPath
    $dll=Join-Path $Core 'CatenaryDC.dll'; RequireFile $dll
    RequireFile (Join-Path $Core 'build-info.json')
    $coreInfo=Get-Content -LiteralPath (Join-Path $Core 'build-info.json') -Raw | ConvertFrom-Json
    $hash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($coreInfo.project -ne 'Derail-Valley-Catenary-DC' -or $hash -ne $coreInfo.core_sha256) { throw 'Choose the unchanged ready-to-install directory produced by Build.ps1.' }
    RequireFile (Join-Path $Game 'DerailValley_Data\Managed\Assembly-CSharp.dll')
    $null=Get-Command dotnet -ErrorAction Stop
    $null=Get-Command python -ErrorAction Stop
    $build=Join-Path (Join-Path $Root 'artifacts') ('legacy2we3-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    $work=Join-Path $build 'work'
    & python (Join-Path $PSScriptRoot 'prepare_legacy_bridge.py') --source $Source --output $work --core-dll $dll
    if ($LASTEXITCODE -ne 0) { throw 'Adapter preparation failed. Original source and game were not changed.' }
    $project=Join-Path $work 'Legacy2WE3.csproj'; $log=Join-Path $build 'build.log'
    RunDotnet -Arguments @('restore',$project,"-p:GameDirectory=$Game","-p:CatenaryCoreDll=$dll") -Log $log
    RunDotnet -Arguments @('build',$project,'-c',$Configuration,'--no-restore',"-p:GameDirectory=$Game","-p:CatenaryCoreDll=$dll") -Log $log
    $result=Join-Path $work "bin\$Configuration\net48\CatenaryDC.Legacy2WE3.dll"; RequireFile $result
    $stage=Join-Path (Join-Path $build 'ready-to-install') 'Derail-Valley-Catenary-DC-2WE3-Bridge'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -LiteralPath $result -Destination $stage
    Copy-Item -LiteralPath (Join-Path $work 'info.json') -Destination $stage
    foreach ($file in @('LICENSE_CC0.txt','LICENSE_MIT.txt','NOTICE.md')) { Copy-Item -LiteralPath (Join-Path $Root $file) -Destination $stage }
    Copy-Item -LiteralPath (Join-Path $work 'preparation.json') -Destination $stage
    [ordered]@{project='Legacy2WE3 bridge';core_sha256=$hash;configuration=$Configuration;runtime_tested_here=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'build-info.json') -Encoding UTF8
    Write-Host "OPTIONAL BRIDGE BUILT: $stage"
    Write-Host 'Nothing installed. Do not enable original Catenary-DC/electric_sim alongside this core or bridge.'
    Write-Host 'Keep the separately installed CCL + 2WE3CCL locomotive models for vehicle tests.'
} catch { Write-Host ('STOPPED: '+$_.Exception.Message) -ForegroundColor Red; exit 1 }
