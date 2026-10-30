# Copies completed user-triggered exports only. Does not install or modify the game.
[CmdletBinding()]
param([string]$ModDirectory, [string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    if ([string]::IsNullOrWhiteSpace($ModDirectory)) { $ModDirectory = Read-Host 'Installed Derail-Valley-Catenary-DC MOD folder' }
    $ModDirectory = (Resolve-Path -LiteralPath $ModDirectory.Trim().Trim('"')).ProviderPath
    $exports = Join-Path $ModDirectory 'UserData\Authoring\Exports'
    if (-not (Test-Path -LiteralPath $exports -PathType Container)) { throw "No exports directory: $exports. Use the in-game authoring tools first." }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\surveys' }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $modPrefix = $ModDirectory.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($OutputDirectory -eq $ModDirectory -or $OutputDirectory.StartsWith($modPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose an output directory OUTSIDE the installed mod.' }
    $files = @(Get-ChildItem -LiteralPath $exports -File | Where-Object { @('.json','.jsonl') -contains $_.Extension.ToLowerInvariant() })
    if ($files.Count -eq 0) { throw 'No completed JSON/JSONL files to pack. .partial exports are deliberately excluded.' }
    $stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $staging = Join-Path $OutputDirectory ('Authoring-' + $stamp)
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $manifest = @()
    foreach ($file in $files) {
        $destination = Join-Path $staging $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $manifest += [ordered]@{name=$file.Name;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    [ordered]@{schema_version=1;complete_exports_only=$true;files=$manifest;note='JSON completion does not certify world/mesh coverage; inspect export footer warnings.'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $staging 'package-manifest.json') -Encoding UTF8
    $zip = $staging + '.zip'
    Compress-Archive -LiteralPath $staging -DestinationPath $zip
    Write-Host "PACKED: $zip"
    Write-Host 'Original exports remain in place. No mod, DLL, layout or save was modified.'
} catch { Write-Host ('STOPPED: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
