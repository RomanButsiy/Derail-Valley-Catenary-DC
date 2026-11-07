# Optional local migration tool. Does not copy the original catenary engine or install anything.
# Pinned upstream controller. The original OCS and scenery are not imported.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OriginalSourceDirectory,
      [Parameter(Mandatory=$true)][string]$CoreReadyDirectory,
      [Parameter(Mandatory=$true)][string]$GameDirectory,
      [ValidateSet('Release','Debug')][string]$Configuration='Release',
      [ValidateSet('e0b78cb','4d1a6a6')][string]$UpstreamRevision='e0b78cb')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Root=Split-Path -Parent $PSScriptRoot
$Revision='legacy2we3-upstream.2'
$log=$null
function RequireFile([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing file: $Path" } }
function WriteBuildLine([string]$Message, [string]$LogPath) {
    Write-Host $Message
    $Message | Out-File -LiteralPath $LogPath -Append -Encoding UTF8
}
function RunDotnet([string[]]$Arguments,[string]$Log) {
    $saved=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try { & dotnet @Arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $Log -Append | Out-Host; $code=$LASTEXITCODE }
    finally { $ErrorActionPreference=$saved }
    if ($code -ne 0) { throw "dotnet failed ($code). See $Log" }
}
try {
    Write-Host "Legacy bridge build revision: $Revision"
    $Source=(Resolve-Path -LiteralPath $OriginalSourceDirectory).ProviderPath
    $Core=(Resolve-Path -LiteralPath $CoreReadyDirectory).ProviderPath
    $Game=(Resolve-Path -LiteralPath $GameDirectory).ProviderPath
    $dll=Join-Path $Core 'CatenaryDC.dll'; RequireFile $dll
    RequireFile (Join-Path $Core 'info.json')
    $coreMod=Get-Content -LiteralPath (Join-Path $Core 'info.json') -Raw | ConvertFrom-Json
    if ($coreMod.Id -ne 'DerailValleyCatenaryDC' -or [version]$coreMod.Version -lt [version]'0.2.0') {
        throw 'The new bridge requires Catenary DC 0.2.0 or newer.'
    }
    RequireFile (Join-Path $Core 'build-info.json')
    $coreInfo=Get-Content -LiteralPath (Join-Path $Core 'build-info.json') -Raw | ConvertFrom-Json
    $hash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($coreInfo.project -ne 'Derail-Valley-Catenary-DC' -or $hash -ne $coreInfo.core_sha256) { throw 'Choose the unchanged ready-to-install directory produced by Build.ps1.' }
    RequireFile (Join-Path $Game 'DerailValley_Data\Managed\Assembly-CSharp.dll')
    $null=Get-Command dotnet -ErrorAction Stop
    $null=Get-Command python -ErrorAction Stop
    $build=Join-Path (Join-Path $Root 'artifacts') ('legacy2we3-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    $work=Join-Path $build 'work'
    New-Item -ItemType Directory -Path $build -Force | Out-Null
    $log=Join-Path $build 'build.log'
    WriteBuildLine "Legacy bridge build revision: $Revision" $log
    WriteBuildLine "Original source (read-only): $Source" $log
    WriteBuildLine "Core (read-only): $Core" $log
    WriteBuildLine "Working copy: $work" $log
    WriteBuildLine "Configuration: $Configuration; language: C# 14.0; target framework: net48" $log
    $saved=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try {
        & python (Join-Path $PSScriptRoot 'prepare_legacy_bridge.py') --source $Source --output $work --core-dll $dll --upstream-revision $UpstreamRevision 2>&1 |
            ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append | Out-Host
        $prepareCode=$LASTEXITCODE
    } finally { $ErrorActionPreference=$saved }
    if ($prepareCode -ne 0) { throw 'Adapter preparation failed. Original source and game were not changed.' }
    $prepPath=Join-Path $work 'preparation.json'; RequireFile $prepPath
    $prep=Get-Content -LiteralPath $prepPath -Raw | ConvertFrom-Json
    $expectedCommit = if ($UpstreamRevision -eq 'e0b78cb') { 'e0b78cb7943ca4d7cde112065b863139dae0c7b0' } else { '4d1a6a691415b6f08f9a76fbafdc8357485e277a' }
    $expectedFixes = if ($UpstreamRevision -eq 'e0b78cb') { 0 } else { 2 }
    if ($prep.build_fix_revision -ne $Revision -or $prep.language_version -ne '14.0' -or
        $prep.upstream_commit -ne $expectedCommit -or $prep.simulate_named_arguments_fixed -ne $expectedFixes) {
        throw 'Generator and builder revisions do not match. Apply the complete bridge patch.'
    }
    $project=Join-Path $work 'Legacy2WE3.csproj'
    # Resolve the SDK and run both build commands from the SAME working directory,
    # so ancestor global.json files cannot change SDK selection between checks.
    Push-Location -LiteralPath $work
    try {
        $saved=$ErrorActionPreference; $ErrorActionPreference='Continue'
        try { $sdkLines=@(& dotnet --version 2>&1 | ForEach-Object { "$_" }); $sdkCode=$LASTEXITCODE }
        finally { $ErrorActionPreference=$saved }
        foreach ($line in $sdkLines) { WriteBuildLine $line $log }
        if ($sdkCode -ne 0) { throw 'Cannot resolve the .NET SDK. See dotnet --version output above.' }
        $sdkMatches=@($sdkLines | Where-Object { $_ -match '^\s*\d+\.\d+\.\d+[^\s]*\s*$' })
        if ($sdkMatches.Count -eq 0) { throw 'Could not read the SDK version. Run dotnet --info from this working directory.' }
        $sdk=$sdkMatches[-1].Trim()
        $major=[int]($sdk.Split('.')[0])
        if ($major -lt 10) {
            throw "Selected SDK $sdk cannot build this C# 14 source. Use .NET SDK 10 or newer; check dotnet --list-sdks and any ancestor global.json. Keep the target framework net48."
        }
        WriteBuildLine "SDK preflight passed: $sdk; controller target remains net48." $log
        RunDotnet -Arguments @('restore',$project,"-p:GameDirectory=$Game","-p:CatenaryCoreDll=$dll") -Log $log
        RunDotnet -Arguments @('build',$project,'-c',$Configuration,'--no-restore',"-p:GameDirectory=$Game","-p:CatenaryCoreDll=$dll") -Log $log
    } finally { Pop-Location }
    $result=Join-Path $work "bin\$Configuration\net48\CatenaryDC.Legacy2WE3.dll"; RequireFile $result
    $stage=Join-Path (Join-Path $build 'ready-to-install') 'Derail-Valley-Catenary-DC-2WE3-Bridge'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -LiteralPath $result -Destination $stage
    Copy-Item -LiteralPath (Join-Path $work 'info.json') -Destination $stage
    foreach ($file in @('LICENSE_CC0.txt','LICENSE_MIT.txt','NOTICE.md')) { Copy-Item -LiteralPath (Join-Path $Root $file) -Destination $stage }
    $prep.compiled=$true
    # Local paths remain in the private work log, not in public release metadata.
    $prep.PSObject.Properties.Remove('source')
    $prep.PSObject.Properties.Remove('core_dll_requested')
    $prep | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stage 'preparation.json') -Encoding UTF8
    $resultHash=(Get-FileHash -LiteralPath $result -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{project='Legacy2WE3 bridge';version=$prep.bridge_version;build_fix_revision=$Revision;
        upstream_commit=$prep.upstream_commit;ccl_pack_version=$prep.ccl_pack_version;core_sha256=$hash;controller_sha256=$resultHash;
        configuration=$Configuration;dotnet_sdk=$sdk;language_version='14.0';target_framework='net48';runtime_tested_here=$false} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'build-info.json') -Encoding UTF8
    $zip=Join-Path $build ("Derail-Valley-Catenary-DC-2WE3-Bridge-"+$prep.bridge_version+"-$Configuration.zip")
    Compress-Archive -LiteralPath $stage -DestinationPath $zip
    WriteBuildLine "OPTIONAL BRIDGE BUILT: $stage" $log
    WriteBuildLine "UMM ZIP: $zip" $log
    WriteBuildLine 'Nothing installed. Do not enable original Catenary-DC/electric_sim alongside this core or bridge.' $log
    WriteBuildLine 'Keep the separately installed CCL + 2WE3CCL locomotive models for vehicle tests.' $log
} catch {
    $message='STOPPED: '+$_.Exception.Message
    Write-Host $message -ForegroundColor Red
    if ($null -ne $log -and (Test-Path -LiteralPath (Split-Path -Parent $log))) {
        $message | Out-File -LiteralPath $log -Append -Encoding UTF8
        Write-Host "Log: $log"
    }
    exit 1
}
