<#
.SYNOPSIS
    Task 002 (FR-06) — Additive schema for sprk_communicationthread: the all-11 typed
    regarding lookups (mirror RegardingFieldMap.All), a NEW Lookup discriminator
    (sprk_regardingrecordtype_ref -> sprk_recordtype_ref, RegardingResolver-bindable),
    a naming-edited marker (sprk_nameisautoderived), and a default-thread marker
    (sprk_isdefaultthread).

.DESCRIPTION
    ADDITIVE + NON-BREAKING (spec NFR-04). This script:
      - Creates 11 typed sprk_regarding{...} lookups on sprk_communicationthread, one per
        RegardingFieldMap.All target entity, using the SAME field-name convention that
        sprk_communication already uses (verified verbatim from
        src/server/api/Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs).
      - Creates ONE new Lookup discriminator sprk_regardingrecordtype_ref -> sprk_recordtype_ref
        so the entity-agnostic RegardingResolver PCF can bind its `regardingRecordType`
        bound property (placement is task 071).
      - Creates two Boolean markers: sprk_nameisautoderived (default Yes/auto, task 071
        BuildTopic re-derive gate) and sprk_isdefaultthread (default No, task 070 default
        catch-all thread).

    MUST-NOT (enforced by construction + idempotent guards):
      - Does NOT touch/retype the existing Text sprk_regardingrecordtype (breaking — read by
        ThreadResolver, membership derivation, timeline filters). It stays the denormalized copy.
      - Does NOT add category / tags / description (spec Q2).
      - Does NOT recreate any of the 8 existing thread columns (task 001 audit §3).

    Idempotent: each attribute/relationship is skipped if it already exists (describe-before-write).

.NOTES
    Pattern source: .claude/skills/dataverse-create-schema/SKILL.md
    Task 001 audit  : projects/messaging-communication-app-r2/notes/001-phase0-schema-audit.md
    Discriminator   : projects/messaging-communication-app-r2/notes/002-thread-regarding-schema.md

    ADR-022: schema authored via Web API is unmanaged (correct for dev). Supply -SolutionUniqueName
    to land the components in the project's (unmanaged) solution per ADR-027; export as managed for
    higher environments.

    LIVE APPLY DEFERRED (describe-before-write gate): authored, NOT applied this session (Dataverse
    MCP unavailable). Owner / next session runs this against spaarkedev1 and MCP-verifies — exactly the
    R1 owner-created + agent-verified pattern.

.PARAMETER Environment
    Dataverse host, e.g. spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    Unique name of the project's (unmanaged) solution to add the components to. If omitted the
    components land in the Default solution — set this to the messaging/communication solution before
    a real apply (ADR-027).

.PARAMETER WhatIf
    Print the create plan without calling the Web API.

.EXAMPLE
    az login
    ./Deploy-ThreadRegardingSchema.ps1 -Environment spaarkedev1.crm.dynamics.com -SolutionUniqueName sprk_Communication
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$SolutionUniqueName
)

$ErrorActionPreference = 'Stop'

$ThreadEntity = 'sprk_communicationthread'
$BaseUrl = "https://$Environment/api/data/v9.2"

Write-Host "Acquiring Dataverse token for $Environment ..." -ForegroundColor Cyan
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
if (-not $token) { throw "Failed to acquire access token. Run 'az login' first." }

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = $Text; "LanguageCode" = 1033 }
        )
    }
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $headers = @{
        "Authorization"    = "Bearer $token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Content-Type"     = "application/json"
        "Accept"           = "application/json"
    }
    if ($SolutionUniqueName) { $headers["MSCRM.SolutionUniqueName"] = $SolutionUniqueName }

    $params = @{ Uri = "$BaseUrl/$Endpoint"; Headers = $headers; Method = $Method }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    return Invoke-RestMethod @params
}

function Test-AttributeExists {
    param([string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" -Method GET | Out-Null
        return $true
    } catch { return $false }
}

function New-BooleanAttributeDef {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [bool]$DefaultValue,
          [string]$TrueLabel = "Yes", [string]$FalseLabel = "No")
    return @{
        "@odata.type"  = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"   = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"  = (New-Label -Text $DisplayName)
        "Description"  = (New-Label -Text $Description)
        "DefaultValue" = $DefaultValue
        "OptionSet"    = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
            "TrueOption"  = @{ "Value" = 1; "Label" = (New-Label -Text $TrueLabel) }
            "FalseOption" = @{ "Value" = 0; "Label" = (New-Label -Text $FalseLabel) }
        }
    }
}

function Add-EntityAttribute {
    param([string]$EntityLogicalName, [hashtable]$AttributeDef, [string]$LogicalName)
    if (Test-AttributeExists -EntityLogicalName $EntityLogicalName -AttributeLogicalName $LogicalName) {
        Write-Host "    SKIP (exists): $LogicalName" -ForegroundColor DarkGray
        return
    }
    if ($PSCmdlet.ShouldProcess("$EntityLogicalName.$LogicalName", "Create attribute")) {
        Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method POST -Body $AttributeDef | Out-Null
        Write-Host "    CREATED: $LogicalName" -ForegroundColor Green
    }
}

function New-RegardingLookup {
    param(
        [string]$ReferencedEntity,     # 1-side (target, e.g. sprk_matter / account / contact / sprk_recordtype_ref)
        [string]$LookupSchemaName,     # e.g. sprk_RegardingMatter (logical -> sprk_regardingmatter)
        [string]$LookupLogicalName,    # e.g. sprk_regardingmatter
        [string]$DisplayName
    )
    if (Test-AttributeExists -EntityLogicalName $ThreadEntity -AttributeLogicalName $LookupLogicalName) {
        Write-Host "    SKIP (exists): $LookupLogicalName -> $ReferencedEntity" -ForegroundColor DarkGray
        return
    }
    $relSchema = "sprk_${ReferencedEntity}_commthread_$($LookupLogicalName -replace '^sprk_','')"
    $relDef = @{
        "@odata.type"        = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"         = $relSchema
        "ReferencedEntity"   = $ReferencedEntity
        "ReferencingEntity"  = $ThreadEntity
        "CascadeConfiguration" = @{
            "Assign" = "NoCascade"; "Delete" = "RemoveLink"; "Merge" = "NoCascade"
            "Reparent" = "NoCascade"; "Share" = "NoCascade"; "Unshare" = "NoCascade"
        }
        "Lookup" = @{
            "@odata.type"  = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"   = $LookupSchemaName
            "DisplayName"  = (New-Label -Text $DisplayName)
            "Description"  = (New-Label -Text "Typed regarding lookup mirroring RegardingFieldMap.All (FR-06).")
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }
    if ($PSCmdlet.ShouldProcess("$ThreadEntity.$LookupLogicalName -> $ReferencedEntity", "Create lookup relationship")) {
        Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method POST -Body $relDef | Out-Null
        Write-Host "    CREATED lookup: $LookupLogicalName -> $ReferencedEntity  (rel $relSchema)" -ForegroundColor Green
    }
}

function Publish-Customizations {
    if ($PSCmdlet.ShouldProcess($ThreadEntity, "Publish customizations")) {
        $publishXml = @{ "ParameterXml" = "<importexportxml><entities><entity>$ThreadEntity</entity></entities></importexportxml>" }
        Invoke-DataverseApi -Endpoint "PublishXml" -Method POST -Body $publishXml | Out-Null
        Write-Host "Customizations published for $ThreadEntity" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# 0. Guard: the existing Text sprk_regardingrecordtype MUST stay Text (NFR-04)
# ---------------------------------------------------------------------------
Write-Host "`n[0] Non-breaking guard: verifying existing Text sprk_regardingrecordtype ..." -ForegroundColor Cyan
try {
    $existing = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$ThreadEntity')/Attributes(LogicalName='sprk_regardingrecordtype')?`$select=LogicalName,AttributeType" -Method GET
    if ($existing.AttributeType -ne 'String') {
        throw "ABORT: sprk_regardingrecordtype is '$($existing.AttributeType)', expected 'String' (Text). NFR-04 non-breaking invariant violated — do NOT proceed."
    }
    Write-Host "    OK: sprk_regardingrecordtype is Text (String) — will remain untouched." -ForegroundColor Green
} catch {
    Write-Warning "    Could not confirm existing Text field (may be pre-apply dry run). $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# 1. The 11 typed regarding lookups (RegardingFieldMap.All, verbatim order)
# ---------------------------------------------------------------------------
Write-Host "`n[1] Creating 11 typed sprk_regarding{...} lookups ..." -ForegroundColor Cyan
$RegardingLookups = @(
    @{ Target = 'sprk_matter';          Logical = 'sprk_regardingmatter';         Schema = 'sprk_RegardingMatter';         Display = 'Regarding Matter' }
    @{ Target = 'sprk_project';         Logical = 'sprk_regardingproject';        Schema = 'sprk_RegardingProject';        Display = 'Regarding Project' }
    @{ Target = 'sprk_invoice';         Logical = 'sprk_regardinginvoice';        Schema = 'sprk_RegardingInvoice';        Display = 'Regarding Invoice' }
    @{ Target = 'sprk_servicerequest';  Logical = 'sprk_regardingservicerequest'; Schema = 'sprk_RegardingServiceRequest'; Display = 'Regarding Service Request' }
    @{ Target = 'sprk_workassignment';  Logical = 'sprk_regardingworkassignment'; Schema = 'sprk_RegardingWorkAssignment'; Display = 'Regarding Work Assignment' }
    @{ Target = 'sprk_event';           Logical = 'sprk_regardingevent';          Schema = 'sprk_RegardingEvent';          Display = 'Regarding Event' }
    @{ Target = 'sprk_budget';          Logical = 'sprk_regardingbudget';         Schema = 'sprk_RegardingBudget';         Display = 'Regarding Budget' }
    @{ Target = 'sprk_analysis';        Logical = 'sprk_regardinganalysis';       Schema = 'sprk_RegardingAnalysis';       Display = 'Regarding Analysis' }
    @{ Target = 'sprk_organization';    Logical = 'sprk_regardingorganization';   Schema = 'sprk_RegardingOrganization';   Display = 'Regarding Organization' }
    @{ Target = 'account';              Logical = 'sprk_regardingaccount';        Schema = 'sprk_RegardingAccount';        Display = 'Regarding Account' }
    @{ Target = 'contact';              Logical = 'sprk_regardingperson';         Schema = 'sprk_RegardingPerson';         Display = 'Regarding Person' }  # contact -> sprk_regardingperson (NOT sprk_regardingcontact)
)
foreach ($l in $RegardingLookups) {
    New-RegardingLookup -ReferencedEntity $l.Target -LookupSchemaName $l.Schema -LookupLogicalName $l.Logical -DisplayName $l.Display
}

# ---------------------------------------------------------------------------
# 2. NEW Lookup discriminator -> sprk_recordtype_ref (RegardingResolver-bindable)
#    Named *_ref because sprk_regardingrecordtype (no suffix) is the in-use Text field.
#    The write handler discovers it by: referencedEntity == 'sprk_recordtype_ref'
#    AND columnName.toLowerCase().Contains('regardingrecordtype') — this name satisfies both.
# ---------------------------------------------------------------------------
Write-Host "`n[2] Creating NEW Lookup discriminator sprk_regardingrecordtype_ref -> sprk_recordtype_ref ..." -ForegroundColor Cyan
New-RegardingLookup -ReferencedEntity 'sprk_recordtype_ref' `
    -LookupSchemaName 'sprk_RegardingRecordType_Ref' `
    -LookupLogicalName 'sprk_regardingrecordtype_ref' `
    -DisplayName 'Regarding Record Type (Ref)'

# ---------------------------------------------------------------------------
# 3. Markers
# ---------------------------------------------------------------------------
Write-Host "`n[3] Creating markers ..." -ForegroundColor Cyan

# Naming-edited marker (task 071): default Yes = auto-derived; user edit flips to No = preserved.
Add-EntityAttribute -EntityLogicalName $ThreadEntity -LogicalName 'sprk_nameisautoderived' -AttributeDef (
    New-BooleanAttributeDef -SchemaName 'sprk_NameIsAutoDerived' `
        -DisplayName 'Name Is Auto-Derived' `
        -Description 'Yes = sprk_name was auto-derived by ThreadResolver.BuildTopic and may be re-derived on regarding change (FR-07). No = user-edited, preserve. Default Yes.' `
        -DefaultValue $true -TrueLabel 'Auto' -FalseLabel 'Edited'
)

# Default-thread marker (task 070): Yes = this is the record's default catch-all thread.
Add-EntityAttribute -EntityLogicalName $ThreadEntity -LogicalName 'sprk_isdefaultthread' -AttributeDef (
    New-BooleanAttributeDef -SchemaName 'sprk_IsDefaultThread' `
        -DisplayName 'Is Default Thread' `
        -Description 'Yes = the default catch-all thread for its regarding record so messages never orphan (FR-09). Default No.' `
        -DefaultValue $false -TrueLabel 'Yes' -FalseLabel 'No'
)

# ---------------------------------------------------------------------------
# 4. Publish
# ---------------------------------------------------------------------------
Write-Host "`n[4] Publishing ..." -ForegroundColor Cyan
Publish-Customizations

Write-Host "`nDone. Verify with MCP describe_table('$ThreadEntity') — expect 14 new columns:" -ForegroundColor Cyan
Write-Host "  11 typed regarding lookups + sprk_regardingrecordtype_ref + sprk_nameisautoderived + sprk_isdefaultthread" -ForegroundColor Cyan
Write-Host "  and the existing Text sprk_regardingrecordtype UNCHANGED." -ForegroundColor Cyan
