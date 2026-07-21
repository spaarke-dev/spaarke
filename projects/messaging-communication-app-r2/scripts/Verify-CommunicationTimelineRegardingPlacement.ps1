<#
.SYNOPSIS
    Task 022 (FR-04) — READ-ONLY verification that the CommunicationTimelineRegarding PCF
    (task 021) is registered and placed on each of the 11 ADR-024/RegardingFieldMap.All
    regarding-family entity main forms.

.DESCRIPTION
    messaging-communication-app-r2 task 022. This script does NOT write anything — it is
    a diagnostic/progress-tracker for the 11-row placement checklist in
    notes/022-pcf-form-placement.md. It:

      1. Confirms the sprk_Spaarke.Controls.CommunicationTimelineRegarding custom control
         is registered in the target org (i.e. the task-021 solution ZIP was imported).
      2. For each of the 11 RegardingFieldMap.All entities, fetches the entity's active
         main form and checks whether the control's schema name appears in the form's
         <controlDescriptions> block (placed) and whether a cell bound to that entity's
         confirmed primary-name attribute exists (anchor field present on the form).
      3. Prints an 11-row summary table (Entity | Control Registered | Placed on Form |
         Anchor Field Present | Form Name).

    WHY READ-ONLY: unlike the sibling subgrid-placement precedent
    (scripts/Deploy-TodoSubgridsToElevenParentForms.ps1, which PATCHes FormXml for a
    subgrid using a stable, widely-documented classid), a PCF field-bound custom control
    uses the <controlDescriptions>/<customControl> FormXml schema (per-form-factor
    entries, StyleTemplate, forId linkage) which is NOT fully verified/tested in this
    repo. Task 022's own rigor-reason flags this as a "host-affecting, hard-to-reverse
    deploy" — so the WRITE side is done via the standard, tested Form Designer UI
    procedure (PCF-DEPLOYMENT-GUIDE.md Step 9 "Post-Import: Field-Based PCF"), documented
    as the maker checklist in notes/022-pcf-form-placement.md Section 4. This script only
    verifies the *result* of that manual placement, safely and repeatably.

.PARAMETER EnvironmentUrl
    Dataverse environment URL.

.PARAMETER ControlSchemaName
    Schema name of the custom control to check for (default: the task-021 control).

.NOTES
    Project: messaging-communication-app-r2
    Task:    022 — W2 / FR-04 — 11-form placement verification
    Created: 2026-07-19
    Rigor:   FULL (deploy/e2e-test tags) — this script is the read-only verification leg;
             the write (form placement) is a maker checklist per notes/022-pcf-form-placement.md.

.EXAMPLE
    az login
    ./Verify-CommunicationTimelineRegardingPlacement.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$ControlSchemaName = "sprk_Spaarke.Controls.CommunicationTimelineRegarding"
)

$ErrorActionPreference = "Stop"

# =============================================================================
# The 11 RegardingFieldMap.All entities + their CODE-GROUNDED primary-name
# attribute (source: src/server/api/Sprk.Bff.Api/Services/Communication/
# IncomingAssociationResolver.cs GetPrimaryNameField(), lines 469-483 — the
# server's own authoritative per-entity primary-name lookup, used for the
# same cross-entity "regarding" purpose this PCF placement serves).
#
# NOTE — CORRECTS an assumption in the task-022 POML notes ("sprk_name for
# the sprk_ entities"): 3 of the 9 sprk_ entities use an entity-specific
# primary-name field, not sprk_name. Verified by grep hits in production
# code/tests (IncomingCommunicationProcessor.cs, CreateProjectWizard
# handoffSeedMapping.ts, sprk_gridconfiguration entity-schema.md fetchxml
# examples) for sprk_mattername / sprk_projectname / sprk_eventname.
# =============================================================================
$entityConfig = @(
    @{ entity = 'sprk_matter';         primaryNameField = 'sprk_mattername' }
    @{ entity = 'sprk_project';        primaryNameField = 'sprk_projectname' }
    @{ entity = 'sprk_invoice';        primaryNameField = 'sprk_name' }
    @{ entity = 'sprk_servicerequest'; primaryNameField = 'sprk_name' }
    @{ entity = 'sprk_workassignment'; primaryNameField = 'sprk_name' }
    @{ entity = 'sprk_event';          primaryNameField = 'sprk_eventname' }
    @{ entity = 'sprk_budget';         primaryNameField = 'sprk_name' }
    @{ entity = 'sprk_analysis';       primaryNameField = 'sprk_name' }
    @{ entity = 'sprk_organization';   primaryNameField = 'sprk_name' }
    @{ entity = 'account';             primaryNameField = 'name' }
    @{ entity = 'contact';             primaryNameField = 'fullname' }
)

# =============================================================================
# Helpers (mirrors scripts/Deploy-TodoSubgridsToElevenParentForms.ps1 style)
# =============================================================================
function Get-DataverseToken {
    param([string]$Url)
    $t = az account get-access-token --resource $Url --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Token failed: $t" }
    return $t.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token, [string]$BaseUrl, [string]$Endpoint
    )
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
    }
    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    try { return Invoke-RestMethod -Uri $uri -Method GET -Headers $headers }
    catch {
        $msg = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            try {
                $j = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($j.error.message) { $msg = $j.error.message }
            } catch {}
        }
        throw "API Error (GET $Endpoint): $msg"
    }
}

function Get-ActiveMainForm {
    param([string]$Token, [string]$BaseUrl, [string]$Entity)

    $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "systemforms?`$filter=objecttypecode eq '$Entity' and type eq 2 and formactivationstate eq 1&`$select=formid,name,formxml"
    if ($r.value.Count -eq 0) { return $null }
    return $r.value[0]
}

# =============================================================================
# Main
# =============================================================================
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Task 022: CommunicationTimelineRegarding 11-form placement check" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Environment:   $EnvironmentUrl"
Write-Host "Control name:  $ControlSchemaName"
Write-Host ""

$token = Get-DataverseToken -Url $EnvironmentUrl
Write-Host "[Auth] Token acquired" -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 1: Confirm the custom control is registered (solution ZIP imported).
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 1: Confirm custom control is registered..." -ForegroundColor Cyan
$controlRegistered = $false
try {
    $cc = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "customcontrols?`$filter=name eq '$ControlSchemaName'&`$select=customcontrolid,name"
    if ($cc.value.Count -gt 0) {
        $controlRegistered = $true
        Write-Host "  FOUND: $ControlSchemaName ($($cc.value[0].customcontrolid))" -ForegroundColor Green
    } else {
        Write-Host "  NOT FOUND — solution ZIP not yet imported. Run 'pac solution import' first (see notes/022-pcf-form-placement.md Section 3)." -ForegroundColor Red
    }
} catch {
    Write-Host "  ERROR querying customcontrols: $($_.Exception.Message)" -ForegroundColor Red
}

# -----------------------------------------------------------------------------
# Step 2: Per-entity form check.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 2: Check each of the 11 entity main forms..." -ForegroundColor Cyan

$results = @()
foreach ($cfg in $entityConfig) {
    $entity = $cfg.entity
    $primaryField = $cfg.primaryNameField
    Write-Host "  [$entity]" -ForegroundColor Cyan
    try {
        $form = Get-ActiveMainForm -Token $token -BaseUrl $EnvironmentUrl -Entity $entity
        if (-not $form) {
            Write-Host "    ERROR: no active main form found" -ForegroundColor Red
            $results += [pscustomobject]@{
                Entity = $entity; PrimaryNameField = $primaryField; Form = ''
                ControlPlaced = 'N/A'; AnchorFieldPresent = 'N/A'; Status = 'Error: no form'
            }
            continue
        }

        $placed = $form.formxml -like "*$ControlSchemaName*"
        $anchorPresent = $form.formxml -like "*datafieldname=`"$primaryField`"*"

        $status = if ($placed -and $anchorPresent) { 'OK' }
                  elseif (-not $anchorPresent) { 'WARN: primary-name field not on this form' }
                  else { 'NOT PLACED' }

        $color = if ($status -eq 'OK') { 'Green' } elseif ($status -eq 'NOT PLACED') { 'Yellow' } else { 'Yellow' }
        Write-Host "    Form: $($form.name) | Control placed: $placed | Anchor field present: $anchorPresent" -ForegroundColor $color

        $results += [pscustomobject]@{
            Entity = $entity; PrimaryNameField = $primaryField; Form = $form.name
            ControlPlaced = $placed; AnchorFieldPresent = $anchorPresent; Status = $status
        }
    }
    catch {
        Write-Host "    ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $results += [pscustomobject]@{
            Entity = $entity; PrimaryNameField = $primaryField; Form = ''
            ControlPlaced = 'N/A'; AnchorFieldPresent = 'N/A'; Status = "Error: $($_.Exception.Message)"
        }
    }
}

# -----------------------------------------------------------------------------
# Step 3: Summary
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " RESULTS SUMMARY (11-row placement matrix)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize Entity, PrimaryNameField, Form, ControlPlaced, AnchorFieldPresent, Status

$placedCount = ($results | Where-Object { $_.Status -eq 'OK' }).Count
Write-Host ""
Write-Host "Control registered: $controlRegistered" -ForegroundColor $(if ($controlRegistered) { 'Green' } else { 'Red' })
Write-Host "Placed + verified:  $placedCount / 11" -ForegroundColor $(if ($placedCount -eq 11) { 'Green' } else { 'Yellow' })
Write-Host ""
Write-Host "NOTE: 'ControlPlaced' is a string-containment check against the form's" -ForegroundColor Gray
Write-Host "controlDescriptions block — a reliable presence signal, not a full binding" -ForegroundColor Gray
Write-Host "validation. Always confirm with a live smoke check (notes/022 Section 6)." -ForegroundColor Gray

if (-not $controlRegistered -or $placedCount -lt 11) { exit 1 }
exit 0
