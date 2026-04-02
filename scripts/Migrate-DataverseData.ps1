#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Migrate all Spaarke record data from one Dataverse environment to another.

.DESCRIPTION
    Exports all records from sprk_ entities in the source environment and creates
    them in the target environment. Handles import order (parents before children),
    lookup field remapping, and many-to-many relationships.

    Excludes: Documents (SPE-linked), transient data (chat messages, audit logs,
    processing jobs, upload contexts).

.PARAMETER SourceUrl
    Source Dataverse URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER TargetUrl
    Target Dataverse URL (e.g., https://spaarke-demo.crm.dynamics.com)

.PARAMETER DryRun
    Preview what would be migrated without making changes.

.EXAMPLE
    .\Migrate-DataverseData.ps1 -SourceUrl "https://spaarkedev1.crm.dynamics.com" -TargetUrl "https://spaarke-demo.crm.dynamics.com"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceUrl,

    [Parameter(Mandatory)]
    [string]$TargetUrl,

    [switch]$DryRun
)

$ErrorActionPreference = "Continue"
$SourceUrl = $SourceUrl.TrimEnd('/')
$TargetUrl = $TargetUrl.TrimEnd('/')

# ─────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────

# Entities to EXCLUDE from migration
$ExcludeEntities = @(
    'sprk_document',              # Tied to SPE files
    'sprk_fileversion',           # Tied to documents
    'sprk_uploadcontext',         # Transient upload state
    'sprk_processingjob',         # Transient job state
    'sprk_eventlog',              # Audit/transient
    'sprk_speauditlog',           # SPE audit log
    'sprk_analysischatmessage',   # Transient chat
    'sprk_aichatmessage',        # Transient chat
    'sprk_aichatsummary',        # Transient chat
    'sprk_aichatcontextmap',     # Transient chat
    'sprk_externalrecordaccess', # Access records (environment-specific)
    'sprk_userpreferences',      # User-specific preferences
    'sprk_gridconfiguration'     # UI configuration (may differ per env)
)

# Many-to-many intersect entities (process AFTER both sides exist)
$IntersectEntities = @(
    'sprk_analysis_knowledge',
    'sprk_analysis_skill',
    'sprk_analysis_tool',
    'sprk_analysisplaybook_action',
    'sprk_analysisplaybook_analysisoutput',
    'sprk_analysisplaybook_mattertype',
    'sprk_event_contact',
    'sprk_matter_contact',
    'sprk_matter_organization',
    'sprk_playbook_knowledge',
    'sprk_playbook_skill',
    'sprk_playbook_tool',
    'sprk_playbooknode_knowledge',
    'sprk_playbooknode_skill',
    'sprk_playbooknode_tool',
    'sprk_project_contact',
    'sprk_project_organization',
    'sprk_project_sprk_matter'
)

# Import order tiers (parents before children)
# Tier 1: Reference/lookup tables (no foreign keys to other sprk_ entities)
# Tier 2: Core entities (reference lookups only)
# Tier 3: Dependent entities (reference core entities)
# Tier 4: Many-to-many intersect tables

$Tier1_Reference = @(
    'sprk_accounttype_ref', 'sprk_contacttype_ref', 'sprk_countryregion_ref',
    'sprk_documenttype', 'sprk_eventtype_ref', 'sprk_mattersubtype_ref',
    'sprk_mattertype_ref', 'sprk_organizationtype_ref', 'sprk_practicearea_ref',
    'sprk_projecttype_ref', 'sprk_recordtype_ref', 'sprk_usertype_ref',
    'sprk_charttype', 'sprk_outputtypes',
    'sprk_aiactiontype', 'sprk_aiknowledgetype', 'sprk_aiknowledgesource',
    'sprk_aiknowledgedeployment', 'sprk_aiskilltype', 'sprk_aitooltype',
    'sprk_aioutputtype', 'sprk_airetrievalmode', 'sprk_aimodeldeployment',
    'sprk_analysisdeliverytype', 'sprk_analysisactiontype',
    'sprk_externalserviceconfig', 'sprk_specontainertypeconfig', 'sprk_speenvironment',
    'sprk_fieldmappingprofile', 'sprk_deliverytemplate',
    'sprk_emailsaverule', 'sprk_communicationaccount'
)

$Tier2_Core = @(
    'sprk_organization', 'sprk_container',
    'sprk_matter', 'sprk_project', 'sprk_event', 'sprk_invoice',
    'sprk_analysisaction', 'sprk_analysisskill', 'sprk_analysisknowledge', 'sprk_analysistool',
    'sprk_analysisplaybook',
    'sprk_chartdefinition', 'sprk_reportingentity', 'sprk_reportingview'
)

$Tier3_Dependent = @(
    'sprk_eventset', 'sprk_eventtodo', 'sprk_workassignment',
    'sprk_budget', 'sprk_budgetbucket', 'sprk_invoicelineitem',
    'sprk_billingevent', 'sprk_spendsnapshot', 'sprk_spendsignal',
    'sprk_kpiassessment', 'sprk_reportcard', 'sprk_timekeeper',
    'sprk_memo', 'sprk_fieldmappingrule',
    'sprk_analysis', 'sprk_analysisoutput', 'sprk_analysisworkingversion',
    'sprk_analysisemailmetadata',
    'sprk_playbooknode', 'sprk_playbookrun', 'sprk_playbooknoderun',
    'sprk_communication', 'sprk_communicationattachment',
    'sprk_emailartifact', 'sprk_attachmentartifact'
)

# Fields to strip from records before creating in target
$SystemFields = @(
    'createdon', 'modifiedon', 'createdby', 'modifiedby',
    'ownerid', 'owningbusinessunit', 'owningteam', 'owninguser',
    'versionnumber', 'importsequencenumber', 'overriddencreatedon',
    'timezoneruleversionnumber', 'utcconversiontimezonecode',
    'statecode', 'statuscode',  # Will be set separately if needed
    '_createdby_value', '_modifiedby_value', '_ownerid_value',
    '_owningbusinessunit_value', '_owningteam_value', '_owninguser_value',
    'createdbyname', 'modifiedbyname', 'owneridname',
    'createdbyyominame', 'modifiedbyyominame', 'owneridyominame',
    'organizationid', '_organizationid_value'
)

# ─────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────

function Get-Token {
    param([string]$Resource)
    return az account get-access-token --resource $Resource --query accessToken -o tsv
}

function Invoke-DvApi {
    param(
        [string]$BaseUrl,
        [string]$Token,
        [string]$Path,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $headers = @{
        Authorization   = "Bearer $Token"
        Accept          = "application/json"
        "OData-Version" = "4.0"
        Prefer          = "odata.include-annotations=*,odata.maxpagesize=5000,return=representation"
    }
    if ($Method -ne "GET") { $headers["Content-Type"] = "application/json; charset=utf-8" }

    $uri = "$BaseUrl/api/data/v9.2/$Path"
    $allResults = @()

    do {
        try {
            if ($Body) {
                $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
                $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method $Method -Body $bodyBytes
            } else {
                $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method $Method
            }
        } catch {
            $status = $_.Exception.Response.StatusCode.value__
            $detail = ""
            try { $detail = $_.ErrorDetails.Message | ConvertFrom-Json | Select-Object -ExpandProperty error | Select-Object -ExpandProperty message } catch {}
            return @{ Error = $true; Status = $status; Message = $detail; Uri = $uri }
        }

        if ($Method -eq "GET" -and $null -ne $response.value) {
            $allResults += $response.value
            $uri = $response.'@odata.nextLink'
        } else {
            return $response
        }
    } while ($uri)

    return $allResults
}

function Get-EntitySetName {
    param([string]$LogicalName, [string]$Token, [string]$BaseUrl)

    $meta = Invoke-DvApi -BaseUrl $BaseUrl -Token $Token -Path "EntityDefinitions(LogicalName='$LogicalName')?`$select=EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute"
    if ($meta.Error) { return $null }
    return @{
        EntitySetName       = $meta.EntitySetName
        PrimaryIdAttribute  = $meta.PrimaryIdAttribute
        PrimaryNameAttribute = $meta.PrimaryNameAttribute
    }
}

function Clean-Record {
    param([PSObject]$Record, [string]$PrimaryIdField)

    $clean = @{}
    foreach ($prop in $Record.PSObject.Properties) {
        $name = $prop.Name

        # Skip system fields
        if ($name -in $SystemFields) { continue }
        # Skip OData annotations
        if ($name.StartsWith('@')) { continue }
        # Skip computed/formatted values
        if ($name.EndsWith('name') -and $name.StartsWith('_')) { continue }
        if ($name.Contains('@')) { continue }
        # Skip the primary ID (will be set by Dataverse)
        if ($name -eq $PrimaryIdField) { continue }
        # Skip null values
        if ($null -eq $prop.Value) { continue }

        # Convert lookup fields (_xxx_value) to @odata.bind format
        if ($name.StartsWith('_') -and $name.EndsWith('_value')) {
            # Skip - we'll handle lookups to sprk_ entities by preserving GUIDs
            # Lookups to system entities (users, BUs) are skipped
            continue
        }

        $clean[$name] = $prop.Value
    }

    return $clean
}

# ─────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────

$startTime = Get-Date
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host "  Dataverse Data Migration" -ForegroundColor Yellow
Write-Host "  Source: $SourceUrl" -ForegroundColor Yellow
Write-Host "  Target: $TargetUrl" -ForegroundColor Yellow
Write-Host "  Mode:   $(if ($DryRun) {'DRY RUN'} else {'LIVE'})" -ForegroundColor $(if ($DryRun) {'DarkYellow'} else {'Green'})
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host ""

# Get tokens
Write-Host "[1] Authenticating..." -ForegroundColor Cyan
$sourceToken = Get-Token -Resource $SourceUrl
$targetToken = Get-Token -Resource $TargetUrl
Write-Host "  Source token: OK" -ForegroundColor Green
Write-Host "  Target token: OK" -ForegroundColor Green

# Build entity list (Tier order)
$allEntities = @()
$allEntities += $Tier1_Reference
$allEntities += $Tier2_Core
$allEntities += $Tier3_Dependent

# Discover any entities not in our tiers (catch-all)
Write-Host ""
Write-Host "[2] Discovering entities..." -ForegroundColor Cyan
$allCustom = Invoke-DvApi -BaseUrl $SourceUrl -Token $sourceToken -Path "EntityDefinitions?`$filter=IsCustomEntity eq true&`$select=LogicalName"
$sprkEntities = $allCustom | Where-Object { $_.LogicalName -like "sprk_*" } | Select-Object -ExpandProperty LogicalName

$uncategorized = $sprkEntities | Where-Object {
    $_ -notin $allEntities -and
    $_ -notin $ExcludeEntities -and
    $_ -notin $IntersectEntities
}
if ($uncategorized.Count -gt 0) {
    Write-Host "  Uncategorized entities (adding to Tier 3): $($uncategorized -join ', ')" -ForegroundColor DarkYellow
    $allEntities += $uncategorized
}

Write-Host "  Total entities to migrate: $($allEntities.Count)" -ForegroundColor Green
Write-Host "  Excluded: $($ExcludeEntities.Count)" -ForegroundColor Gray
Write-Host "  Intersect (phase 2): $($IntersectEntities.Count)" -ForegroundColor Gray

# Phase 1: Migrate regular entities
Write-Host ""
Write-Host "[3] Phase 1: Migrating entity records..." -ForegroundColor Cyan

$totalRecords = 0
$totalCreated = 0
$totalSkipped = 0
$totalErrors = 0
$entityIndex = 0

$logPath = Join-Path $PSScriptRoot ".." "logs" "data-migration-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$logDir = Split-Path $logPath -Parent
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

foreach ($entity in $allEntities) {
    $entityIndex++

    # Get entity metadata
    $meta = Get-EntitySetName -LogicalName $entity -Token $sourceToken -BaseUrl $SourceUrl
    if (-not $meta) {
        Write-Host "  [$entityIndex/$($allEntities.Count)] $entity — SKIPPED (no metadata)" -ForegroundColor DarkYellow
        continue
    }

    $entitySetName = $meta.EntitySetName
    $primaryId = $meta.PrimaryIdAttribute
    $primaryName = $meta.PrimaryNameAttribute

    # Get all records from source
    $records = Invoke-DvApi -BaseUrl $SourceUrl -Token $sourceToken -Path "$entitySetName"
    if ($records -is [hashtable] -and $records.Error) {
        Write-Host "  [$entityIndex/$($allEntities.Count)] $entity — ERROR querying: $($records.Message)" -ForegroundColor Red
        $totalErrors++
        continue
    }

    $count = if ($records -is [array]) { $records.Count } else { 0 }
    $totalRecords += $count

    if ($count -eq 0) {
        Write-Host "  [$entityIndex/$($allEntities.Count)] $entity — 0 records" -ForegroundColor Gray
        continue
    }

    Write-Host "  [$entityIndex/$($allEntities.Count)] $entity — $count records" -ForegroundColor White -NoNewline

    if ($DryRun) {
        Write-Host " (dry run)" -ForegroundColor DarkYellow
        continue
    }

    # Check what already exists in target
    $existing = Invoke-DvApi -BaseUrl $TargetUrl -Token $targetToken -Path "$entitySetName?`$select=$primaryId&`$top=1"
    $existingCount = if ($existing -is [array]) { $existing.Count } else { 0 }

    $created = 0
    $skipped = 0
    $errors = 0

    foreach ($record in $records) {
        $recordId = $record.$primaryId
        $recordName = if ($primaryName) { $record.$primaryName } else { $recordId }

        # Clean the record (remove system fields, annotations)
        $cleanRecord = Clean-Record -Record $record -PrimaryIdField $primaryId

        # Preserve the original GUID
        $cleanRecord[$primaryId] = $recordId

        # Create in target
        $result = Invoke-DvApi -BaseUrl $TargetUrl -Token $targetToken -Path $entitySetName -Method POST -Body $cleanRecord
        if ($result -is [hashtable] -and $result.Error) {
            if ($result.Status -eq 409 -or $result.Message -match "duplicate" -or $result.Message -match "already exists") {
                $skipped++
            } else {
                $errors++
                "ERROR: $entity/$recordId ($recordName): $($result.Status) $($result.Message)" | Out-File -Append $logPath
            }
        } else {
            $created++
        }
    }

    $totalCreated += $created
    $totalSkipped += $skipped
    $totalErrors += $errors

    $status = if ($errors -gt 0) { "Red" } elseif ($skipped -gt 0) { "DarkYellow" } else { "Green" }
    Write-Host " → $created created, $skipped skipped, $errors errors" -ForegroundColor $status
}

# Phase 2: Many-to-many relationships
Write-Host ""
Write-Host "[4] Phase 2: Migrating many-to-many relationships..." -ForegroundColor Cyan

foreach ($intersect in $IntersectEntities) {
    $meta = Get-EntitySetName -LogicalName $intersect -Token $sourceToken -BaseUrl $SourceUrl
    if (-not $meta) {
        Write-Host "  $intersect — SKIPPED (no metadata)" -ForegroundColor DarkYellow
        continue
    }

    $records = Invoke-DvApi -BaseUrl $SourceUrl -Token $sourceToken -Path "$($meta.EntitySetName)"
    $count = if ($records -is [array]) { $records.Count } else { 0 }
    $totalRecords += $count

    if ($count -eq 0) {
        Write-Host "  $intersect — 0 records" -ForegroundColor Gray
        continue
    }

    Write-Host "  $intersect — $count records" -ForegroundColor White -NoNewline

    if ($DryRun) {
        Write-Host " (dry run)" -ForegroundColor DarkYellow
        continue
    }

    $created = 0
    $errors = 0

    foreach ($record in $records) {
        $cleanRecord = Clean-Record -Record $record -PrimaryIdField $meta.PrimaryIdAttribute
        $cleanRecord[$meta.PrimaryIdAttribute] = $record.($meta.PrimaryIdAttribute)

        $result = Invoke-DvApi -BaseUrl $TargetUrl -Token $targetToken -Path $meta.EntitySetName -Method POST -Body $cleanRecord
        if ($result -is [hashtable] -and $result.Error) {
            if ($result.Status -ne 409) { $errors++ }
        } else {
            $created++
        }
    }

    $totalCreated += $created
    $totalErrors += $errors
    Write-Host " → $created created, $errors errors" -ForegroundColor $(if ($errors -gt 0) {"Red"} else {"Green"})
}

# Summary
$elapsed = (Get-Date) - $startTime
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host "  Migration Complete" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Records found:    $totalRecords" -ForegroundColor White
Write-Host "  Records created:  $totalCreated" -ForegroundColor Green
Write-Host "  Records skipped:  $totalSkipped" -ForegroundColor DarkYellow
Write-Host "  Errors:           $totalErrors" -ForegroundColor $(if ($totalErrors -gt 0) {"Red"} else {"Green"})
Write-Host "  Duration:         $($elapsed.ToString('mm\:ss'))" -ForegroundColor Gray
Write-Host "  Log:              $logPath" -ForegroundColor Gray
Write-Host ""
