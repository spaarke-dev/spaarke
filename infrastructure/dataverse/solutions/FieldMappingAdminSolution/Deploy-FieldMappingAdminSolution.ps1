<#
.SYNOPSIS
    Deploy FieldMappingAdminSolution (Field Mapping Profile MDA form + subgrid) to a Dataverse environment.

.DESCRIPTION
    Repacks the unpacked solution folder and imports it via `pac solution import`.
    Use for Wave 8 SRFR-083 UAT deploy. For spaarkedev1, the current v1.0.5 zip is
    already in this folder.

.PARAMETER Environment
    Target environment URL (e.g., https://spaarkedev1.crm.dynamics.com/).

.PARAMETER Version
    Solution version to build (e.g., "1.0.6"). Update <Version> in Other/Solution.xml before running.

.EXAMPLE
    .\Deploy-FieldMappingAdminSolution.ps1 -Environment "https://spaarkedev1.crm.dynamics.com/" -Version "1.0.6"

.NOTES
    Created: 2026-07-02
    Task: SRFR-060 (form authoring); SRFR-083 (Wave 8 UAT deploy)
    Solution: FieldMappingAdminSolution
    Publisher: Spaarke (sprk_)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Environment,

    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

Write-Host "== FieldMappingAdminSolution deploy ==" -ForegroundColor Cyan
Write-Host "Environment: $Environment"
Write-Host "Version:     $Version"

# Sanity: verify Solution.xml Version matches
$solutionXmlPath = Join-Path $here 'Other/Solution.xml'
if (-not (Test-Path $solutionXmlPath)) { throw "Solution.xml not found at $solutionXmlPath" }
$xmlContent = Get-Content $solutionXmlPath -Raw
if ($xmlContent -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
    throw "Other/Solution.xml <Version> does not match requested $Version. Bump it manually first."
}

# 1. Select PAC auth
Write-Host "`n-- Selecting PAC auth --"
pac org who
$connectedUrl = (pac org who) -match 'Org URL' | Out-String
if ($connectedUrl -notmatch [regex]::Escape($Environment)) {
    Write-Warning "Currently connected to a different environment. Run 'pac auth select --index <n>' to switch."
    throw "Not connected to $Environment"
}

# 2. Pack solution
$zipPath = Join-Path $here "FieldMappingAdminSolution-v$Version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "`n-- Packing solution --"
pac solution pack --zipfile $zipPath --folder $here --packagetype Unmanaged
if ($LASTEXITCODE -ne 0) { throw "Pack failed" }

# 3. Import
Write-Host "`n-- Importing solution --"
pac solution import --path $zipPath --publish-changes --async
if ($LASTEXITCODE -ne 0) { throw "Import failed" }

Write-Host "`nDeploy complete: $zipPath" -ForegroundColor Green
