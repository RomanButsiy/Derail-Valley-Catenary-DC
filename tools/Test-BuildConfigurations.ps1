# Runs the real Build.ps1 twice on the user's machine; no installation.
[CmdletBinding()]
param([string]$GameDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) { $GameDirectory = Read-Host 'Derail Valley GAME ROOT' }
    foreach ($configuration in @('Release','Debug')) {
        Write-Host "Verifying $configuration core build (separate artifacts directory) ..."
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build.ps1') -GameDirectory $GameDirectory -Configuration $configuration
        if ($LASTEXITCODE -ne 0) { throw "$configuration compilation failed. Do not install that result." }
    }
    Write-Host 'BOTH CONFIGURATIONS BUILT. Neither was installed. Keep only one core DLL active.'
} catch { Write-Host ('STOPPED: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
