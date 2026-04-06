<#
.SYNOPSIS
    Deploy all Spaarke web resources to a single Dataverse environment.

.DESCRIPTION
    Orchestrator script that calls individual deploy scripts in sequence:
      1. Deploy-CorporateWorkspace.ps1    — sprk_corporateworkspace (HTML)
      2. Deploy-ExternalWorkspaceSpa.ps1  — sprk_externalworkspace (HTML + inline JS)
      3. Deploy-SpeAdminApp.ps1           — sprk_speadmin (HTML)
      4. Deploy-WizardCodePages.ps1       — 12 wizard/code page web resources
      5. Deploy-EventsPage.ps1            — sprk_eventspage.html
      6. Deploy-PCFWebResources.ps1       — PCF bundle.js + CSS
      7. Deploy-RibbonIcons.ps1           — 3 SVG ribbon icons

    Each sub-script handles its own authentication, encoding, and error handling.
    Failures in individual components do not stop the overall run.

.PARAMETER DataverseUrl
    Target Dataverse environment URL (e.g. https://spaarkedev1.crm.dynamics.com).
    Falls back to DATAVERSE_URL environment variable.

.PARAMETER SkipComponent
    Array of component names to skip. Valid values:
      CorporateWorkspace, ExternalWorkspaceSpa, SpeAdminApp,
      WizardCodePages, EventsPage, PCFWebResources, RibbonIcons

.PARAMETER WhatIf
    Pass -WhatIf to preview which components would be deployed without executing.

.EXAMPLE
    .\scripts\Deploy-AllWebResources.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\scripts\Deploy-AllWebResources.ps1 -SkipComponent RibbonIcons,PCFWebResources

.EXAMPLE
    .\scripts\Deploy-AllWebResources.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [ValidateSet(
        'CorporateWorkspace', 'ExternalWorkspaceSpa', 'SpeAdminApp',
        'WizardCodePages', 'EventsPage', 'PCFWebResources', 'RibbonIcons'
    )]
    [string[]]$SkipComponent = @()
)

$ErrorActionPreference = 'Continue'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---------------------------------------------------------------------------
# Component definitions — order matters (deploy sequence)
# ---------------------------------------------------------------------------
$components = @(
    @{
        Name       = 'CorporateWorkspace'
        Script     = 'Deploy-CorporateWorkspace.ps1'
        Desc       = 'sprk_corporateworkspace (HTML)'
        Args       = @{ DataverseUrl = $DataverseUrl }
    },
    @{
        Name       = 'ExternalWorkspaceSpa'
        Script     = 'Deploy-ExternalWorkspaceSpa.ps1'
        Desc       = 'sprk_externalworkspace (HTML + inline JS)'
        Args       = @{ DataverseUrl = $DataverseUrl }
    },
    @{
        Name       = 'SpeAdminApp'
        Script     = 'Deploy-SpeAdminApp.ps1'
        Desc       = 'sprk_speadmin (HTML)'
        Args       = @{ DataverseUrl = $DataverseUrl }
    },
    @{
        Name       = 'WizardCodePages'
        Script     = 'Deploy-WizardCodePages.ps1'
        Desc       = '12 wizard/code page web resources'
        Args       = @{ DataverseUrl = $DataverseUrl }
    },
    @{
        Name       = 'EventsPage'
        Script     = 'Deploy-EventsPage.ps1'
        Desc       = 'sprk_eventspage.html'
        Args       = @{ DataverseUrl = $DataverseUrl }
    },
    @{
        Name       = 'PCFWebResources'
        Script     = 'Deploy-PCFWebResources.ps1'
        Desc       = 'PCF bundle.js + CSS'
        Args       = @{ DataverseUrl = $DataverseUrl }
    },
    @{
        Name       = 'RibbonIcons'
        Script     = 'Deploy-RibbonIcons.ps1'
        Desc       = '3 SVG ribbon icons'
        # RibbonIcons uses PAC CLI auth (pac org who) — takes -SolutionName, not -DataverseUrl
        Args       = @{}
    }
)

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=============================================' -ForegroundColor Cyan
Write-Host '  Spaarke — Deploy All Web Resources'         -ForegroundColor Cyan
Write-Host '=============================================' -ForegroundColor Cyan
Write-Host "  Target:     $DataverseUrl"
Write-Host "  Components: $($components.Count)"
if ($SkipComponent.Count -gt 0) {
    Write-Host "  Skipping:   $($SkipComponent -join ', ')" -ForegroundColor Yellow
}
Write-Host ''

# ---------------------------------------------------------------------------
# Execute each component
# ---------------------------------------------------------------------------
$results = @()
$overallStart = Get-Date

foreach ($comp in $components) {
    $name   = $comp.Name
    $script = Join-Path $scriptDir $comp.Script
    $desc   = $comp.Desc

    # Skip?
    if ($SkipComponent -contains $name) {
        Write-Host "[$name] SKIPPED (excluded via -SkipComponent)" -ForegroundColor Yellow
        $results += @{ Name = $name; Desc = $desc; Status = 'Skipped'; Duration = [TimeSpan]::Zero; Error = $null }
        continue
    }

    # WhatIf?
    if (-not $PSCmdlet.ShouldProcess($name, "Deploy $desc")) {
        $results += @{ Name = $name; Desc = $desc; Status = 'Skipped'; Duration = [TimeSpan]::Zero; Error = $null }
        continue
    }

    # Verify script exists
    if (-not (Test-Path $script)) {
        Write-Host "[$name] FAILED — script not found: $script" -ForegroundColor Red
        $results += @{ Name = $name; Desc = $desc; Status = 'Failed'; Duration = [TimeSpan]::Zero; Error = "Script not found: $($comp.Script)" }
        continue
    }

    Write-Host ''
    Write-Host "---------------------------------------------" -ForegroundColor DarkGray
    Write-Host "[$name] Deploying $desc ..." -ForegroundColor Cyan
    Write-Host "---------------------------------------------" -ForegroundColor DarkGray

    $compStart = Get-Date
    try {
        # Build splatted arguments
        $splatArgs = $comp.Args.Clone()

        & $script @splatArgs
        $exitCode = $LASTEXITCODE

        $compDuration = (Get-Date) - $compStart

        if ($exitCode -and $exitCode -ne 0) {
            Write-Host "[$name] FAILED (exit code $exitCode)" -ForegroundColor Red
            $results += @{ Name = $name; Desc = $desc; Status = 'Failed'; Duration = $compDuration; Error = "Exit code $exitCode" }
        } else {
            Write-Host "[$name] SUCCESS ($([math]::Round($compDuration.TotalSeconds, 1))s)" -ForegroundColor Green
            $results += @{ Name = $name; Desc = $desc; Status = 'Success'; Duration = $compDuration; Error = $null }
        }
    }
    catch {
        $compDuration = (Get-Date) - $compStart
        Write-Host "[$name] FAILED — $($_.Exception.Message)" -ForegroundColor Red
        $results += @{ Name = $name; Desc = $desc; Status = 'Failed'; Duration = $compDuration; Error = $_.Exception.Message }
    }
}

$overallDuration = (Get-Date) - $overallStart

# ---------------------------------------------------------------------------
# Summary report
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=============================================' -ForegroundColor Cyan
Write-Host '  Deployment Summary'                          -ForegroundColor Cyan
Write-Host '=============================================' -ForegroundColor Cyan
Write-Host ''

$succeeded = 0
$failed    = 0
$skipped   = 0

foreach ($r in $results) {
    $statusColor = switch ($r.Status) {
        'Success' { 'Green'  }
        'Failed'  { 'Red'    }
        'Skipped' { 'Yellow' }
    }
    $durationStr = if ($r.Duration.TotalSeconds -gt 0) { "$([math]::Round($r.Duration.TotalSeconds, 1))s" } else { '-' }
    $errorStr    = if ($r.Error) { " ($($r.Error))" } else { '' }

    Write-Host ("  {0,-25} {1,-10} {2,8}{3}" -f $r.Name, $r.Status, $durationStr, $errorStr) -ForegroundColor $statusColor

    switch ($r.Status) {
        'Success' { $succeeded++ }
        'Failed'  { $failed++    }
        'Skipped' { $skipped++   }
    }
}

Write-Host ''
Write-Host "  Total: $($results.Count) components | " -NoNewline
Write-Host "$succeeded succeeded" -ForegroundColor Green -NoNewline
Write-Host " | " -NoNewline
if ($failed -gt 0) {
    Write-Host "$failed failed" -ForegroundColor Red -NoNewline
} else {
    Write-Host "0 failed" -NoNewline
}
Write-Host " | $skipped skipped"
Write-Host "  Duration: $([math]::Round($overallDuration.TotalSeconds, 1))s"
Write-Host ''

# ---------------------------------------------------------------------------
# Exit code: non-zero if any component failed
# ---------------------------------------------------------------------------
if ($failed -gt 0) {
    Write-Host "Deployment completed with $failed failure(s)." -ForegroundColor Red
    exit 1
}

Write-Host 'All web resources deployed successfully.' -ForegroundColor Green
exit 0
