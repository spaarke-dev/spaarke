# Deploy External Workspace SPA to Power Pages Code Site
# Runs: npm run build → pac pages upload-code-site
#
# Prerequisites:
#   PAC CLI authenticated: pac auth create --url https://spaarkedev1.crm.dynamics.com
#   Node.js / npm installed
#
# Usage (from repo root):
#   .\scripts\Deploy-PowerPages.ps1
#   .\scripts\Deploy-PowerPages.ps1 -SkipBuild   # skip npm run build if already built

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot        = Split-Path -Parent $PSScriptRoot
$spaDir          = Join-Path $repoRoot 'src\client\external-spa'
$siteDownloadDir = Join-Path $spaDir '.site-download\spaarke-external-workspace'
$distDir         = Join-Path $spaDir 'dist'
$siteName        = 'Spaarke External Workspace'
$webSiteId       = 'a79315b5-d91e-4e27-b016-439ad439babe'
$portalUrl       = 'https://sprk-external-workspace.powerappsportals.com'

Write-Host '=====================================' -ForegroundColor Cyan
Write-Host 'Power Pages Code Site Deployment'     -ForegroundColor Cyan
Write-Host '=====================================' -ForegroundColor Cyan

# ── Step 1: Build ─────────────────────────────────────────────────────────────
if ($SkipBuild) {
    Write-Host '[1/3] Skipping build (-SkipBuild)' -ForegroundColor Gray
} else {
    Write-Host '[1/3] Building SPA...' -ForegroundColor Yellow
    Push-Location $spaDir
    npm run build
    $buildExit = $LASTEXITCODE
    Pop-Location
    if ($buildExit -ne 0) {
        Write-Host '      Build FAILED' -ForegroundColor Red
        exit 1
    }
    Write-Host '      Build complete' -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $distDir 'index.html'))) {
    Write-Host "ERROR: dist/index.html not found. Run 'cd src/client/external-spa && npm run build'" -ForegroundColor Red
    exit 1
}

# ── Step 2: Ensure new-format site folder exists ──────────────────────────────
# PAC CLI 2.4.x requires the new .powerpages-site/ format.
# The .site-download/ folder was created once via:
#   pac pages download-code-site --webSiteId $webSiteId --path ".site-download" --overwrite
# If it's missing (e.g., fresh clone), run the download automatically.
if (-not (Test-Path $siteDownloadDir)) {
    Write-Host '[2/3] Site download folder not found — running first-time download...' -ForegroundColor Yellow
    Write-Host '      (This is a one-time migration step; folder is gitignored but committed in this repo.)' -ForegroundColor Gray

    pac pages download-code-site `
        --webSiteId $webSiteId `
        --path (Join-Path $spaDir '.site-download') `
        --overwrite

    if ($LASTEXITCODE -ne 0) {
        Write-Host '      Download FAILED. Ensure PAC CLI auth is valid:' -ForegroundColor Red
        Write-Host '      pac auth create --url https://spaarkedev1.crm.dynamics.com' -ForegroundColor Red
        exit 1
    }
    Write-Host '      Download complete' -ForegroundColor Green
} else {
    Write-Host '[2/3] Site folder ready (new format)' -ForegroundColor Green
}

# ── Step 3: Upload ────────────────────────────────────────────────────────────
Write-Host '[3/3] Uploading to Power Pages Code Site...' -ForegroundColor Yellow
Write-Host "      rootPath     : $siteDownloadDir" -ForegroundColor Gray
Write-Host "      compiledPath : $distDir" -ForegroundColor Gray

pac pages upload-code-site `
    --rootPath $siteDownloadDir `
    --compiledPath $distDir `
    --siteName $siteName

if ($LASTEXITCODE -ne 0) {
    Write-Host '      Upload FAILED. If you see auth errors:' -ForegroundColor Red
    Write-Host '      pac auth create --url https://spaarkedev1.crm.dynamics.com' -ForegroundColor Red
    Write-Host '      If upload succeeds but portal is unchanged, token expired mid-run — re-auth and retry.' -ForegroundColor Red
    exit 1
}
Write-Host '      Upload complete' -ForegroundColor Green

Write-Host ''
Write-Host '=====================================' -ForegroundColor Cyan
Write-Host 'Deployment Complete!'                  -ForegroundColor Green
Write-Host '=====================================' -ForegroundColor Cyan
Write-Host "Portal : $portalUrl"
Write-Host 'Portal updates within ~30-60 seconds.'
Write-Host ''
Write-Host 'Verify: Open in a private browser window and hard-refresh (Ctrl+Shift+R).'
