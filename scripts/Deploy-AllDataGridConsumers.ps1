<#
.SYNOPSIS
    Build + deploy every Custom Page that consumes the Spaarke DataGrid framework.

.DESCRIPTION
    The DataGrid framework lives in @spaarke/ui-components (shared library). Each
    Custom Page that mounts <DataGrid> bakes a copy of the framework into its
    Vite single-file bundle at build time. When the framework changes, EVERY
    consumer must be rebuilt and redeployed — otherwise the user sees stale
    framework code in some surfaces and new code in others.

    This script enumerates all known DataGrid consumers, builds each, PATCHes
    the matching web resource, and runs a SINGLE PublishXml at the end so the
    Dataverse runtime cache is flushed atomically.

    The consumer registry below is the source of truth — add new entries here
    when a new Custom Page starts consuming <DataGrid>.

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL.

.PARAMETER Only
    Optional comma-separated list of consumer names to build + deploy.
    Defaults to ALL consumers.

.PARAMETER SkipBuild
    Skip the npm run build step (use when you've already built locally and just
    want to PATCH + Publish).

.PARAMETER SkipInstall
    Skip the npm install step (use when you know node_modules is fresh).

.PARAMETER WhatIf
    List what would be built + deployed without actually doing it.

.EXAMPLE
    .\Deploy-AllDataGridConsumers.ps1
    # Build + deploy every consumer to https://spaarkedev1.crm.dynamics.com
    # (or wherever $env:DATAVERSE_URL points).

.EXAMPLE
    .\Deploy-AllDataGridConsumers.ps1 -Only EventsPage,LegalWorkspace
    # Only those two.

.EXAMPLE
    .\Deploy-AllDataGridConsumers.ps1 -SkipBuild
    # PATCH + publish whatever's in each consumer's dist/ folder right now.

.NOTES
    Why this exists: per Spaarke DataGrid framework r1 task 035 UAT iteration 5
    (2026-06-04). Operator noted "we do not want to go through this same UI
    review and back/forth every time we deploy the dataset grid — this is
    always going to be part of the grid". This script kills the
    "I forgot to redeploy InvoicesPage" class of bug permanently — every
    framework change is now a single command.
#>
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string[]]$Only,
    [switch]$SkipBuild,
    [switch]$SkipInstall,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

# ─────────────────────────────────────────────────────────────────────────────
# Consumer registry — source of truth.
#
# WhenYouAddANewConsumer:
#   - SolutionDir: relative to repo root, where `npm run build` lives
#   - DistFile: relative to SolutionDir/dist/, the artifact to upload
#               (Vite default is index.html; LegalWorkspace renames to corporateworkspace.html)
#   - WebResource: the Dataverse web resource `name` value to PATCH
# ─────────────────────────────────────────────────────────────────────────────

$Consumers = @(
    [PSCustomObject]@{
        Name        = 'EventsPage'
        SolutionDir = 'src\solutions\EventsPage'
        DistFile    = 'index.html'
        WebResource = 'sprk_eventspage.html'
    }
    [PSCustomObject]@{
        Name        = 'sprk_invoicespage'
        SolutionDir = 'src\solutions\sprk_invoicespage'
        DistFile    = 'index.html'
        WebResource = 'sprk_invoicespage.html'
    }
    [PSCustomObject]@{
        Name        = 'sprk_kpiassessmentspage'
        SolutionDir = 'src\solutions\sprk_kpiassessmentspage'
        DistFile    = 'index.html'
        WebResource = 'sprk_kpiassessmentspage.html'
    }
    [PSCustomObject]@{
        Name        = 'LegalWorkspace'
        SolutionDir = 'src\solutions\LegalWorkspace'
        DistFile    = 'corporateworkspace.html'
        # LegalWorkspace's web resource name has NO ".html" suffix — preserved
        # for parity with the existing Deploy-CorporateWorkspace.ps1 contract.
        WebResource = 'sprk_corporateworkspace'
    }
)

# ─────────────────────────────────────────────────────────────────────────────
# Optional filter
# ─────────────────────────────────────────────────────────────────────────────

if ($Only) {
    $Consumers = $Consumers | Where-Object { $Only -contains $_.Name }
    if ($Consumers.Count -eq 0) {
        Write-Error "No consumers matched -Only filter: $($Only -join ', ')"
        exit 1
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Resolve repo root
# ─────────────────────────────────────────────────────────────────────────────

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

Write-Host "============================================="
Write-Host "Deploy-AllDataGridConsumers"
Write-Host "============================================="
Write-Host "Repo root:     $RepoRoot"
Write-Host "Dataverse:     $DataverseUrl"
Write-Host "Consumers:     $($Consumers.Count) ($($Consumers.Name -join ', '))"
Write-Host "SkipBuild:     $SkipBuild"
Write-Host "SkipInstall:   $SkipInstall"
Write-Host "WhatIfOnly:    $WhatIfOnly"
Write-Host ""

if ($WhatIfOnly) {
    foreach ($c in $Consumers) {
        Write-Host "[WhatIf] Would build $($c.SolutionDir) and PATCH webresource '$($c.WebResource)' with $($c.SolutionDir)\dist\$($c.DistFile)"
    }
    exit 0
}

# ─────────────────────────────────────────────────────────────────────────────
# Phase 1 — Build each consumer
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Host "[Phase 1/3] Building $($Consumers.Count) consumer(s)..."
    foreach ($c in $Consumers) {
        $dir = Join-Path $RepoRoot $c.SolutionDir
        if (-not (Test-Path $dir)) {
            Write-Error "Solution directory not found: $dir"
            exit 1
        }

        # Install if needed (lazy)
        if (-not $SkipInstall) {
            $nodeModules = Join-Path $dir 'node_modules'
            if (-not (Test-Path $nodeModules)) {
                Write-Host "  [$($c.Name)] node_modules missing - installing..."
                Push-Location $dir
                try {
                    # Per CLAUDE.md §11: many src/solutions/* Vite solutions have stale package-lock files;
                    # npm ci fails on most. Use install --legacy-peer-deps.
                    npm install --legacy-peer-deps --no-audit --no-fund 2>&1 | Out-Null
                    if ($LASTEXITCODE -ne 0) {
                        Write-Error "npm install failed for $($c.Name)"
                        exit 1
                    }
                } finally {
                    Pop-Location
                }
            }
        }

        Write-Host "  [$($c.Name)] npm run build..."
        Push-Location $dir
        try {
            $buildOutput = npm run build 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host $buildOutput
                Write-Error "npm run build failed for $($c.Name)"
                exit 1
            }
        } finally {
            Pop-Location
        }

        $distPath = Join-Path $dir "dist\$($c.DistFile)"
        if (-not (Test-Path $distPath)) {
            Write-Error "Expected dist artifact missing after build: $distPath"
            exit 1
        }
        $sizeKb = [math]::Round((Get-Item $distPath).Length / 1KB)
        Write-Host "    -> $($c.DistFile) ($sizeKb KB)" -ForegroundColor Green
    }
    Write-Host ""
} else {
    Write-Host "[Phase 1/3] SKIPPED (build)"
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Phase 2 — PATCH each web resource (no per-resource PublishXml yet)
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "[Phase 2/3] Acquiring Dataverse access token..."
$accessToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Error "Failed to get access token via az. Run 'az login' and ensure you have access to $DataverseUrl."
    exit 1
}
Write-Host "  Token acquired" -ForegroundColor Green
Write-Host ""

$apiUrl  = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

$webResourceIds = @()

Write-Host "[Phase 2/3] PATCHing $($Consumers.Count) web resource(s)..."
foreach ($c in $Consumers) {
    $dir      = Join-Path $RepoRoot $c.SolutionDir
    $distPath = Join-Path $dir "dist\$($c.DistFile)"

    if (-not (Test-Path $distPath)) {
        Write-Error "dist artifact missing for $($c.Name): $distPath (run without -SkipBuild?)"
        exit 1
    }

    Write-Host "  [$($c.Name)] resolving web resource '$($c.WebResource)'..."
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$($c.WebResource)'&`$select=webresourceid"
    $lookup    = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
    if ($lookup.value.Count -eq 0) {
        Write-Error "Web resource '$($c.WebResource)' not found in $DataverseUrl. Create it in the maker portal first."
        exit 1
    }
    $wrId = $lookup.value[0].webresourceid
    Write-Host "    id: $wrId" -ForegroundColor Gray

    $bytes  = [System.IO.File]::ReadAllBytes($distPath)
    $b64    = [Convert]::ToBase64String($bytes)
    $sizeKb = [math]::Round($bytes.Length / 1KB)

    $patchBody = @{ content = $b64 } | ConvertTo-Json
    Invoke-RestMethod -Uri "$apiUrl/webresourceset($wrId)" -Headers $headers -Method Patch -Body $patchBody | Out-Null
    Write-Host "    PATCHed ($sizeKb KB)" -ForegroundColor Green

    $webResourceIds += $wrId
}
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 3 — Single PublishXml flushes the runtime cache atomically
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "[Phase 3/3] PublishXml..."
$webResourceXml = ($webResourceIds | ForEach-Object { "<webresource>{$_}</webresource>" }) -join ''
$publishXml     = "<importexportxml><webresources>$webResourceXml</webresources></importexportxml>"
$publishBody    = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host "  All web resources published" -ForegroundColor Green
Write-Host ""

Write-Host "============================================="
Write-Host "Deploy complete: $($Consumers.Count) consumer(s) at $DataverseUrl"
Write-Host "============================================="
foreach ($c in $Consumers) {
    Write-Host "  ✓ $($c.Name) -> $($c.WebResource)"
}
