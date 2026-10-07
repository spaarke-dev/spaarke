#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks the report catalog's Power BI pointer — sprk_report.sprk_pbi_reportid, sprk_workspaceid, sprk_datasetid and
    sprk_iscustom — with field-level security, so ONLY the BFF can set or change which Power BI report, workspace and
    dataset a catalog row names, while every user still reads them (unified-access-control-r2 task 166 f1; owner round 25
    item 6). Dry run by default; -Apply writes; -Verify checks.

.DESCRIPTION
    The catalog row is the authority (owner round 21 item 2): the BFF derives every embed token, export and delete from
    the four columns. Before this lock any Author (Org-scope Write on sprk_report, scripts/Deploy-ReportingSchema.ps1)
    could edit them in the MDA or through Xrm.WebApi and redirect every viewer's embed and export of that row to any
    report the service principal can reach — or, as an Admin, point a custom row at an uncatalogued report and DELETE
    it. Round 25 item 6 decided BOTH controls: this lock, and the BFF's allowed-workspace check before embed, export,
    delete and clone (PowerBi:AllowedWorkspaces), which also catches rows written before the lock.

    This is the named entry point; the ONE mechanism is scripts/Set-DocumentPointerFieldSecurity.ps1 -Target
    ReportCatalog (same two profiles, same preconditions p1-p6, same secure-then-grant-at-once steps and revert).

    PRECONDITION (p4) for -Apply: -BffWritesCatalogPointers — the BFF build (task 166 f1) that registers a catalog row
    WITHOUT the four columns and stamps them app-only is deployed. scripts/Deploy-ReportingReports.ps1 seeds catalog rows
    as the operator (System Administrator — full field access by platform rule), so it keeps working.

    LIVE ORDER: deploy the BFF → set PowerBi__AllowedWorkspaces__N__WorkspaceId (and, for a multi-customer environment,
    PowerBi__AllowedWorkspaces__N__CustomerBusinessUnitId) → THIS SCRIPT (dry run) → -BffWritesCatalogPointers -Apply →
    -Verify.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users. Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles and sprk_report. Default SpaarkeCore.

.PARAMETER BffWritesCatalogPointers
    The operator's confirmation for (p4). Required with -Apply.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-ReportCatalogFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c

.EXAMPLE
    .\Set-ReportCatalogFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -BffWritesCatalogPointers -Apply

.NOTES
    unified-access-control-r2 task 166 f1 (#1105). Live run = a main-session manual gate (task note §20).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$BffWritesCatalogPointers,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'Set-DocumentPointerFieldSecurity.ps1'
$arguments = @{
    EnvironmentUrl     = $EnvironmentUrl
    BffApplicationIds  = $BffApplicationIds
    Target             = 'ReportCatalog'
    SolutionUniqueName = $SolutionUniqueName
}
if ($BffWritesCatalogPointers) { $arguments.BffWritesCatalogPointers = $true }
if ($Apply) { $arguments.Apply = $true }
if ($Verify) { $arguments.Verify = $true }

& $engine @arguments
exit $LASTEXITCODE
