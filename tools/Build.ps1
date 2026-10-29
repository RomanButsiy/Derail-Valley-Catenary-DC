# Builds in an isolated working copy. NEVER installs to the game.
[CmdletBinding()]
param(
    [string]$GameDirectory,
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
    [string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
function Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function RequireFile([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing file: $Path" } }
function RunDotnet([string[]]$Arguments, [string]$Log) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & dotnet @Arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $Log -Append | Out-Host; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $saved }
    if ($code -ne 0) { throw "dotnet exited with $code. See $Log" }
}
try {
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) { $GameDirectory = Read-Host 'Derail Valley GAME ROOT (contains DerailValley_Data)' }
    $GameDirectory = (Resolve-Path -LiteralPath $GameDirectory.Trim().Trim('"')).ProviderPath
    $Managed = Join-Path $GameDirectory 'DerailValley_Data\Managed'
    RequireFile (Join-Path $Managed 'Assembly-CSharp.dll')
    $null = Get-Command dotnet -ErrorAction Stop
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or [int]($sdk.Split('.')[0]) -lt 8) { throw 'Install .NET SDK 8 or newer (C# 12 compiler required).' }
    [xml]$Project = Get-Content -LiteralPath (Join-Path $Root 'src\CatenaryDC\CatenaryDC.csproj') -Raw
    foreach ($reference in @($Project.SelectNodes('/Project/ItemGroup/Reference'))) {
        if ($null -eq $reference) { continue }
        RequireFile (Join-Path $Managed ($reference.GetAttribute('Include') + '.dll'))
    }
    $Unity = Join-Path $Root 'unity_project'
    $BundleDir = Join-Path $Unity 'Assets\AssetBundles'
    $ReceiptPath = Join-Path $BundleDir 'catenarydc-export.json'
    RequireFile $ReceiptPath
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
    if ($receipt.project -ne 'Derail-Valley-Catenary-DC' -or $receipt.unity_version -ne '2019.4.41f2') {
        throw 'Use Unity 2019.4.41f2 and Export OCS in this NEW project. Do not copy old bundles.'
    }
    foreach ($entry in @($receipt.source_files) + @($receipt.bundles)) {
        $full = [IO.Path]::GetFullPath((Join-Path $Unity $entry.path))
        $prefix = [IO.Path]::GetFullPath($Unity).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe path in export receipt.' }
        RequireFile $full
        if ((Sha $full) -ne $entry.sha256) { throw "Unity source or bundle changed after Export OCS: $($entry.path). Export again." }
    }
    $bundleNames = @($receipt.bundles | ForEach-Object { [IO.Path]::GetFileName($_.path) } | Sort-Object)
    if (@(Compare-Object @('catenary','catenary_parts') $bundleNames).Count -ne 0) { throw 'The receipt must contain exactly the two catenary bundles.' }
    # Detect additions/deletions, not just changed files already listed in the receipt.
    $actualSources = @(Get-ChildItem -LiteralPath (Join-Path $Unity 'Assets\Catenary') -File -Recurse | ForEach-Object {
        $_.FullName.Substring($Unity.Length + 1).Replace('\','/')
    }) + @('Assets/Catenary.meta','Assets/Editor/CatenaryBundleBuild.cs')
    if (@(Compare-Object @($receipt.source_files.path | Sort-Object) @($actualSources | Sort-Object)).Count -ne 0) {
        throw 'Asset set changed after export. Export OCS again.'
    }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $Root 'artifacts' }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $gamePrefix = $GameDirectory.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($OutputDirectory -eq $GameDirectory -or $OutputDirectory.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must be OUTSIDE the game folder.'
    }
    $build = Join-Path $OutputDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    $work = Join-Path $build 'work'
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $sourceRoot = Join-Path $Root 'src\CatenaryDC'
    $workProject = Join-Path $work 'CatenaryDC'
    New-Item -ItemType Directory -Path $workProject -Force | Out-Null
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
        $relative = $sourceFile.FullName.Substring($sourceRoot.Length + 1)
        if (@($relative -split '[\\/]' | Where-Object { @('bin','obj','.vs') -contains $_ }).Count -gt 0) { continue }
        $destination = Join-Path $workProject $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destination
    }
    $projectPath = Join-Path $work 'CatenaryDC\CatenaryDC.csproj'
    $Log = Join-Path $build 'build.log'
    Write-Host "Build directory: $build"
    Write-Host 'Game and original author source are read-only. No automatic installation.'
    RunDotnet -Arguments @('restore',$projectPath,"-p:GameDirectory=$GameDirectory") -Log $Log
    RunDotnet -Arguments @('build',$projectPath,'-c',$Configuration,'--no-restore',"-p:GameDirectory=$GameDirectory") -Log $Log
    $dll = Join-Path $work "CatenaryDC\bin\$Configuration\net48\CatenaryDC.dll"
    RequireFile $dll
    $stageRoot = Join-Path $build 'ready-to-install'
    $stage = Join-Path $stageRoot 'Derail-Valley-Catenary-DC'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($name in @('info.json','LICENSE_CC0.txt','LICENSE_MIT.txt','NOTICE.md')) {
        Copy-Item -LiteralPath (Join-Path $Root $name) -Destination $stage
    }
    Copy-Item -LiteralPath $dll -Destination $stage
    foreach ($name in @('catenary','catenary_parts')) { Copy-Item -LiteralPath (Join-Path $BundleDir $name) -Destination $stage }
    Copy-Item -LiteralPath (Join-Path $Root 'config\network.json') -Destination $stage
    New-Item -ItemType Directory -Path (Join-Path $stage 'Scenery') | Out-Null
    Copy-Item -LiteralPath (Join-Path $Root 'Scenery\README.md') -Destination (Join-Path $stage 'Scenery')
    $info = [ordered]@{
        project='Derail-Valley-Catenary-DC'; version='0.1.0'; configuration=$Configuration;
        source_base='4d1a6a691415b6f08f9a76fbafdc8357485e277a'; sdk=$sdk;
        built_utc=(Get-Date).ToUniversalTime().ToString('o');
        core_sha256=(Sha (Join-Path $stage 'CatenaryDC.dll'));
        catenary_sha256=(Sha (Join-Path $stage 'catenary'));
        parts_sha256=(Sha (Join-Path $stage 'catenary_parts'));
        locomotive_controller_included=$false;
        hb_cs_cms_route_implemented=$false
    }
    $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'build-info.json') -Encoding UTF8
    $zip = Join-Path $build "Derail-Valley-Catenary-DC-0.1.0-$Configuration.zip"
    Compress-Archive -LiteralPath $stage -DestinationPath $zip
    Write-Host 'BUILD SUCCEEDED. Nothing was installed.'
    Write-Host "Ready folder: $stage"
    Write-Host "UMM ZIP: $zip"
    Write-Host 'Core only: use a diesel to inspect, or separately build the optional 2WE3 bridge for powered tests.'
} catch {
    Write-Host ('STOPPED: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
