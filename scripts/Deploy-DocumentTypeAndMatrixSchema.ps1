#requires -Version 7

<#
.SYNOPSIS
    Wave D1 (task 030) — Create sprk_documenttype_ref entity + sprk_practicearea_documenttype N:N matrix.

.DESCRIPTION
    Per `projects/ai-spaarke-insights-engine-r2/design-a3-2d-taxonomy.md`:
      - §1.2 — sprk_documenttype_ref entity (code, name, description, sortorder; statecode/statuscode for active flag)
      - §2.2 — sprk_practicearea_documenttype intersect (manual N:N) entity with per-pair columns
      - §6.1 — Wave D1 hand-off contract

    Per ADR-027 amendment 2026-06-02: Spaarke uses unmanaged solutions everywhere. Schema is created
    directly via Dataverse Web API (no managed-solution promotion).

    Idempotent — safe to re-run. Each step checks current state before acting.

.PARAMETER DataverseUrl
    Target Dataverse environment URL. Defaults to https://spaarkedev1.crm.dynamics.com.

.PARAMETER DryRun
    Show what would change without making any modifications.

.NOTES
    Authored 2026-06-03 for Insights Engine r2 task 030 (Wave D1).
#>

[CmdletBinding()]
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ===========================================================================
# Auth
# ===========================================================================
Write-Host '=== Task 030: Document Type + Matrix Schema ===' -ForegroundColor Cyan
Write-Host "Environment : $DataverseUrl" -ForegroundColor White
Write-Host "Mode        : $(if ($DryRun) { 'DRY-RUN' } else { 'LIVE' })" -ForegroundColor White
Write-Host ''

Write-Host '[Auth] Acquiring access token via Azure CLI...' -ForegroundColor Yellow
$token = az account get-access-token --resource $DataverseUrl --query 'accessToken' -o tsv 2>&1
if (-not $token -or $LASTEXITCODE -ne 0) {
    throw "Failed to acquire access token. Run 'az login' first."
}

$apiBase = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json'
}

# Helper for localized labels
function New-Label([string]$Text) {
    return @{
        'LocalizedLabels' = @(
            @{ 'Label' = $Text; 'LanguageCode' = 1033 }
        )
    }
}

function Test-EntityExists([string]$LogicalName) {
    try {
        Invoke-RestMethod -Uri "$apiBase/EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -Headers $headers -Method GET -ErrorAction Stop | Out-Null
        return $true
    } catch {
        if ($_.Exception.Response.StatusCode -eq 'NotFound') { return $false }
        throw
    }
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    try {
        Invoke-RestMethod -Uri "$apiBase/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" -Headers $headers -Method GET -ErrorAction Stop | Out-Null
        return $true
    } catch {
        if ($_.Exception.Response.StatusCode -eq 'NotFound') { return $false }
        throw
    }
}

function Test-RelationshipExists([string]$SchemaName) {
    try {
        Invoke-RestMethod -Uri "$apiBase/RelationshipDefinitions(SchemaName='$SchemaName')?`$select=SchemaName" -Headers $headers -Method GET -ErrorAction Stop | Out-Null
        return $true
    } catch {
        if ($_.Exception.Response.StatusCode -eq 'NotFound') { return $false }
        throw
    }
}

# ===========================================================================
# Step 1: Create sprk_documenttype_ref entity
# ===========================================================================
Write-Host ''
Write-Host '[1/8] sprk_documenttype_ref entity...' -ForegroundColor Yellow

if (Test-EntityExists 'sprk_documenttype_ref') {
    Write-Host '  Entity already exists.' -ForegroundColor Green
} else {
    Write-Host '  Entity MISSING — will create.' -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host '  [DRY-RUN] Would create sprk_documenttype_ref with primary-name sprk_documenttypename.' -ForegroundColor Gray
    } else {
        $entityBody = @{
            '@odata.type'           = 'Microsoft.Dynamics.CRM.EntityMetadata'
            'SchemaName'            = 'sprk_documenttype_ref'
            'DisplayName'           = New-Label 'Document Type (Ref)'
            'DisplayCollectionName' = New-Label 'Document Types (Ref)'
            'Description'           = New-Label 'Reference data — 2D classification taxonomy dimension 2 (per Insights Engine r2 design-a3 §1). Practice-area-prefixed UPPER_SNAKE codes (e.g. CTRNS_CLOSING_STATEMENT) for routing Layer 2 extraction via the sprk_practicearea_documenttype matrix.'
            'OwnershipType'         = 'OrganizationOwned'
            'IsActivity'            = $false
            'HasNotes'              = $false
            'HasActivities'         = $false
            'PrimaryNameAttribute'  = 'sprk_documenttypename'
            'Attributes'            = @(
                @{
                    '@odata.type'   = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
                    'SchemaName'    = 'sprk_documenttypename'
                    'RequiredLevel' = @{ 'Value' = 'ApplicationRequired' }
                    'MaxLength'     = 200
                    'DisplayName'   = New-Label 'Document Type Name'
                    'Description'   = New-Label 'Human-readable display name (e.g. "Closing Statement").'
                    'IsPrimaryName' = $true
                }
            )
        } | ConvertTo-Json -Depth 20 -Compress
        Invoke-RestMethod -Uri "$apiBase/EntityDefinitions" -Headers $headers -Method POST -Body $entityBody | Out-Null
        Write-Host '  Entity created.' -ForegroundColor Green
    }
}

# ===========================================================================
# Step 2: Add columns to sprk_documenttype_ref
# ===========================================================================
Write-Host ''
Write-Host '[2/8] sprk_documenttype_ref columns...' -ForegroundColor Yellow

$docTypeAttrs = @(
    @{
        Logical = 'sprk_documenttypecode'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
            'SchemaName'    = 'sprk_documenttypecode'
            'RequiredLevel' = @{ 'Value' = 'ApplicationRequired' }
            'MaxLength'     = 100
            'DisplayName'   = New-Label 'Document Type Code'
            'Description'   = New-Label 'Practice-area-prefixed UPPER_SNAKE code (e.g. CTRNS_CLOSING_STATEMENT, IPPAT_PATENT_APPLICATION). Pan-area types use GEN_ prefix. Used as the routing key by universal-ingest Layer 1 → Layer 2 matrix lookup.'
        }
    },
    @{
        Logical = 'sprk_documenttypedescription'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
            'SchemaName'    = 'sprk_documenttypedescription'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'MaxLength'     = 2000
            'DisplayName'   = New-Label 'Description'
            'Description'   = New-Label 'SME-authored disambiguation text — optional; helps Layer 1 prompts and human reviewers distinguish similar doc types.'
            'Format'        = 'TextArea'
        }
    },
    @{
        Logical = 'sprk_sortorder'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
            'SchemaName'    = 'sprk_sortorder'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'DisplayName'   = New-Label 'Sort Order'
            'Description'   = New-Label 'Optional UX sort order for dropdowns and pickers.'
            'MinValue'      = 0
            'MaxValue'      = 100000
            'Format'        = 'None'
        }
    }
)

foreach ($attr in $docTypeAttrs) {
    if (Test-AttributeExists 'sprk_documenttype_ref' $attr.Logical) {
        Write-Host "  $($attr.Logical): exists." -ForegroundColor Green
    } else {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would create $($attr.Logical)." -ForegroundColor Gray
        } else {
            $body = $attr.Body | ConvertTo-Json -Depth 20 -Compress
            Invoke-RestMethod -Uri "$apiBase/EntityDefinitions(LogicalName='sprk_documenttype_ref')/Attributes" -Headers $headers -Method POST -Body $body | Out-Null
            Write-Host "  $($attr.Logical): CREATED." -ForegroundColor Yellow
        }
    }
}

# ===========================================================================
# Step 3: Create sprk_practicearea_documenttype intersect entity
# ===========================================================================
Write-Host ''
Write-Host '[3/8] sprk_practicearea_documenttype intersect entity...' -ForegroundColor Yellow

if (Test-EntityExists 'sprk_practicearea_documenttype') {
    Write-Host '  Entity already exists.' -ForegroundColor Green
} else {
    Write-Host '  Entity MISSING — will create.' -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host '  [DRY-RUN] Would create sprk_practicearea_documenttype with primary-name sprk_name.' -ForegroundColor Gray
    } else {
        $intersectBody = @{
            '@odata.type'           = 'Microsoft.Dynamics.CRM.EntityMetadata'
            'SchemaName'            = 'sprk_practicearea_documenttype'
            'DisplayName'           = New-Label 'Practice Area / Document Type'
            'DisplayCollectionName' = New-Label 'Practice Area / Document Types'
            'Description'           = New-Label 'Manual N:N intersect between sprk_practicearea_ref and sprk_documenttype_ref (per Insights Engine r2 design-a3 §2). Carries per-pair Layer 2 routing columns (layer2actioncode, layer2required, gatesignal) that auto-generated N:N intersect cannot. NULL sprk_layer2actioncode = structured gate-fail (Phase 1.5 replacement for outcomeBearing=false).'
            'OwnershipType'         = 'OrganizationOwned'
            'IsActivity'            = $false
            'HasNotes'              = $false
            'HasActivities'         = $false
            'PrimaryNameAttribute'  = 'sprk_name'
            'Attributes'            = @(
                @{
                    '@odata.type'   = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
                    'SchemaName'    = 'sprk_name'
                    'RequiredLevel' = @{ 'Value' = 'ApplicationRequired' }
                    'MaxLength'     = 200
                    'DisplayName'   = New-Label 'Pair Display Name'
                    'Description'   = New-Label 'Display label for the pair (e.g. "CTRNS × Closing Statement").'
                    'IsPrimaryName' = $true
                }
            )
        } | ConvertTo-Json -Depth 20 -Compress
        Invoke-RestMethod -Uri "$apiBase/EntityDefinitions" -Headers $headers -Method POST -Body $intersectBody | Out-Null
        Write-Host '  Entity created.' -ForegroundColor Green
    }
}

# ===========================================================================
# Step 4: Add scalar columns to sprk_practicearea_documenttype
# ===========================================================================
Write-Host ''
Write-Host '[4/8] sprk_practicearea_documenttype scalar columns...' -ForegroundColor Yellow

$matrixAttrs = @(
    @{
        Logical = 'sprk_layer2actioncode'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
            'SchemaName'    = 'sprk_layer2actioncode'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'MaxLength'     = 100
            'DisplayName'   = New-Label 'Layer 2 Action Code'
            'Description'   = New-Label 'sprk_analysisaction.sprk_actioncode of the Layer 2 extraction action for this (area × type) pair (e.g. INSIGHTS.LAYER2_EXTRACT.CTRNS.CLOSING_STATEMENT). NULL = structured gate-fail (no Layer 2 — Layer 1 signal only emitted as Observation).'
        }
    },
    @{
        Logical = 'sprk_layer2required'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
            'SchemaName'    = 'sprk_layer2required'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'DisplayName'   = New-Label 'Layer 2 Required'
            'Description'   = New-Label 'When true, the universal-ingest confidence gate is bypassed and Layer 2 runs regardless of Layer 1 confidence. Use for always-extract pairs (e.g. patent applications).'
            'DefaultValue'  = $false
            'OptionSet'     = @{
                'TrueOption'  = @{ 'Value' = 1; 'Label' = New-Label 'Yes' }
                'FalseOption' = @{ 'Value' = 0; 'Label' = New-Label 'No' }
            }
        }
    },
    @{
        Logical = 'sprk_gatesignal'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
            'SchemaName'    = 'sprk_gatesignal'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'MaxLength'     = 100
            'DisplayName'   = New-Label 'Gate Signal'
            'Description'   = New-Label 'The Layer 1 boolean signal name that activates this row (e.g. is_closing_statement). Defensive double-check that Layer 1 output agrees with the routing decision.'
        }
    },
    @{
        Logical = 'sprk_sortorder'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
            'SchemaName'    = 'sprk_sortorder'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'DisplayName'   = New-Label 'Sort Order'
            'Description'   = New-Label 'Optional UX sort order.'
            'MinValue'      = 0
            'MaxValue'      = 100000
            'Format'        = 'None'
        }
    },
    @{
        Logical = 'sprk_notes'
        Body    = @{
            '@odata.type'   = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
            'SchemaName'    = 'sprk_notes'
            'RequiredLevel' = @{ 'Value' = 'None' }
            'MaxLength'     = 2000
            'DisplayName'   = New-Label 'Notes'
            'Description'   = New-Label 'Per-pair SME context — extraction priorities, gate-signal exceptions, deferral rationale.'
            'Format'        = 'TextArea'
        }
    }
)

foreach ($attr in $matrixAttrs) {
    if (Test-AttributeExists 'sprk_practicearea_documenttype' $attr.Logical) {
        Write-Host "  $($attr.Logical): exists." -ForegroundColor Green
    } else {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would create $($attr.Logical)." -ForegroundColor Gray
        } else {
            $body = $attr.Body | ConvertTo-Json -Depth 20 -Compress
            Invoke-RestMethod -Uri "$apiBase/EntityDefinitions(LogicalName='sprk_practicearea_documenttype')/Attributes" -Headers $headers -Method POST -Body $body | Out-Null
            Write-Host "  $($attr.Logical): CREATED." -ForegroundColor Yellow
        }
    }
}

# ===========================================================================
# Step 5: Create lookup relationships (intersect → ref entities)
# ===========================================================================
Write-Host ''
Write-Host '[5/8] Lookup relationships...' -ForegroundColor Yellow

$lookups = @(
    @{
        SchemaName        = 'sprk_practicearea_documenttype_practicearea'
        LookupLogical     = 'sprk_practiceareaid'
        LookupSchema      = 'sprk_practiceareaid'
        LookupDisplay     = 'Practice Area'
        LookupDescription = 'Lookup to sprk_practicearea_ref — one half of the (area × type) pair.'
        Referenced        = 'sprk_practicearea_ref'
        Referencing       = 'sprk_practicearea_documenttype'
    },
    @{
        SchemaName        = 'sprk_practicearea_documenttype_documenttype'
        LookupLogical     = 'sprk_documenttypeid'
        LookupSchema      = 'sprk_documenttypeid'
        LookupDisplay     = 'Document Type'
        LookupDescription = 'Lookup to sprk_documenttype_ref — other half of the (area × type) pair.'
        Referenced        = 'sprk_documenttype_ref'
        Referencing       = 'sprk_practicearea_documenttype'
    }
)

foreach ($lk in $lookups) {
    if (Test-RelationshipExists $lk.SchemaName) {
        Write-Host "  $($lk.SchemaName): exists." -ForegroundColor Green
    } else {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would create lookup $($lk.LookupLogical) on $($lk.Referencing) → $($lk.Referenced)." -ForegroundColor Gray
        } else {
            $relBody = @{
                '@odata.type'         = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
                'SchemaName'          = $lk.SchemaName
                'ReferencedEntity'    = $lk.Referenced
                'ReferencingEntity'   = $lk.Referencing
                'CascadeConfiguration' = @{
                    'Assign'   = 'NoCascade'
                    'Delete'   = 'Restrict'
                    'Merge'    = 'NoCascade'
                    'Reparent' = 'NoCascade'
                    'Share'    = 'NoCascade'
                    'Unshare'  = 'NoCascade'
                }
                'Lookup'              = @{
                    '@odata.type'   = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
                    'SchemaName'    = $lk.LookupSchema
                    'DisplayName'   = New-Label $lk.LookupDisplay
                    'Description'   = New-Label $lk.LookupDescription
                    'RequiredLevel' = @{ 'Value' = 'ApplicationRequired' }
                }
            } | ConvertTo-Json -Depth 20 -Compress
            Invoke-RestMethod -Uri "$apiBase/RelationshipDefinitions" -Headers $headers -Method POST -Body $relBody | Out-Null
            Write-Host "  $($lk.SchemaName): CREATED." -ForegroundColor Yellow
        }
    }
}

# ===========================================================================
# Step 6: Publish customizations
# ===========================================================================
Write-Host ''
Write-Host '[6/8] Publishing customizations...' -ForegroundColor Yellow

if ($DryRun) {
    Write-Host '  [DRY-RUN] Would publish sprk_documenttype_ref + sprk_practicearea_documenttype.' -ForegroundColor Gray
} else {
    $publishBody = @{
        'ParameterXml' = '<importexportxml><entities><entity>sprk_documenttype_ref</entity><entity>sprk_practicearea_documenttype</entity></entities></importexportxml>'
    } | ConvertTo-Json -Compress
    Invoke-RestMethod -Uri "$apiBase/PublishXml" -Headers $headers -Method POST -Body $publishBody | Out-Null
    Write-Host '  Published.' -ForegroundColor Green
}

# ===========================================================================
# Summary
# ===========================================================================
Write-Host ''
Write-Host '=== Schema Deployment Complete ===' -ForegroundColor Cyan
Write-Host 'Next: seed sprk_documenttype_ref rows + sprk_practicearea_documenttype intersect rows via MCP create_record.' -ForegroundColor White
Write-Host ''
