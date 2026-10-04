<#
.SYNOPSIS
    Runs `npm run build:prod` for one PCF control and FAILS if the build failed, even though
    pcf-scripts itself exits 0.

.DESCRIPTION
    Use this instead of a bare `npm run build:prod` anywhere a PCF is built before it is packed or
    imported (the pcf-deploy and dataverse-deploy skills, release scripts). pcf-scripts exits 0 when
    webpack fails, so a bare build followed by "copy bundle.js and pack" silently ships the PREVIOUS
    bundle still sitting in out/. The decision rule lives in PcfBuildResult.psm1.

    Exit codes: 0 = build succeeded; 1 = build failed or its result could not be confirmed.

.PARAMETER PcfPath
    The PCF control folder (the one containing package.json with a build:prod script), e.g.
    src/client/pcf/VisualHost.

.PARAMETER Install
    Run `npm install --legacy-peer-deps --no-audit --no-fund` first (root CLAUDE.md section 12).

.EXAMPLE
    pwsh -File scripts/Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/VisualHost

.NOTES
    Added 2026-10-04 by spaarke-ontology-platform-r1 task 094.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PcfPath,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PcfBuildResult.psm1') -Force

# Report setup problems with Write-Host + exit 1, not Write-Error: under ErrorActionPreference=Stop
# Write-Error throws, so an in-process caller would get an exception instead of LASTEXITCODE=1.
function Stop-WithFailure([string] $Message) { Write-Host "FAIL  $Message" -ForegroundColor Red; exit 1 }

$resolved = Resolve-Path -LiteralPath $PcfPath -ErrorAction SilentlyContinue
if (-not $resolved) { Stop-WithFailure "PCF folder not found: $PcfPath" }
$pkg = Join-Path $resolved.ProviderPath 'package.json'
if (-not (Test-Path -LiteralPath $pkg)) { Stop-WithFailure "No package.json in $resolved" }
$scripts = (Get-Content -LiteralPath $pkg -Raw | ConvertFrom-Json).scripts
if (-not $scripts -or -not $scripts.'build:prod') {
    Stop-WithFailure "$resolved has no build:prod script. Production PCF builds must use build:prod (root CLAUDE.md section 12)."
}

Push-Location -LiteralPath $resolved.ProviderPath
try {
    if ($Install) {
        $installOut = & { $ErrorActionPreference = 'Continue'; npm install --legacy-peer-deps --no-audit --no-fund 2>&1 }
        if ($LASTEXITCODE -ne 0) {
            $installOut | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
            Write-Host "FAIL  npm install (exit code $LASTEXITCODE)" -ForegroundColor Red
            exit 1
        }
    }

    # Stream the build output as it arrives AND keep it for the verdict.
    $output = & { $ErrorActionPreference = 'Continue'; npm run build:prod 2>&1 | Tee-Object -Variable teeOut | Out-Host; $teeOut }
    $exit = $LASTEXITCODE

    $result = Get-PcfBuildResult -Output $output -ExitCode $exit
    Write-Host ''
    if ($result.Succeeded) {
        Write-Host "PASS  $(Split-Path $resolved -Leaf): $($result.Reason)" -ForegroundColor Green
        exit 0
    }

    Write-Host "FAIL  $(Split-Path $resolved -Leaf): $($result.Status) ($($result.Reason))" -ForegroundColor Red
    if ($exit -eq 0) {
        Write-Host '      Note: npm exited 0. pcf-scripts does not propagate webpack failures; out/ may hold a STALE bundle. Do not pack or deploy it.' -ForegroundColor Yellow
    }
    $result.Excerpt | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
    exit 1
}
finally {
    Pop-Location
}
