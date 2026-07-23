<#
.SYNOPSIS
    Create the NEW `sprk_communicationparticipant` junction (message-grain participant index)
    in Dataverse — the queryable person->communications structure FR-08 locks.

.DESCRIPTION
    Task 003 of messaging-communication-app-r2 (Communication Workspace). Creates ONE net-new
    entity + its 6 locked fields + primary name, via the Dataverse Metadata Web API (Approach B,
    same pattern as scripts/Deploy-ReportingSchema.ps1 / Deploy-ChartDefinitionEntity.ps1).

    ENTITY: sprk_communicationparticipant  (Organization-owned — mirrors the sibling message-child
            intersection sprk_communicationattachment; access inherits from the parent
            sprk_communication via the required lookup + Cascade delete).

    LOCKED FIELD SET (spec FR-08, 2026-07-18 — do NOT add / drop / rename):
      1. sprk_name          Text (400)   primary name  "{personDisplay|address} - {role}"
      2. sprk_communication Lookup -> sprk_communication   REQUIRED   Cascade delete (parent, message grain)
      3. sprk_systemuser    Lookup -> systemuser           nullable   RemoveLink
      4. sprk_contact       Lookup -> contact              nullable   RemoveLink
      5. sprk_role          Choice (LOCAL) From=100000000 / To=100000001 / Cc=100000002 / Bcc=100000003
      6. sprk_addresstext   Text (400)   raw email (unresolved parties + provenance for resolved)
      7. sprk_isresolved    Boolean (default No) — false when no person lookup is set

    INVARIANTS (enforced by the WRITE at task 050, NOT by schema):
      - Exactly one of sprk_systemuser / sprk_contact is set for a RESOLVED person.
      - BOTH null + sprk_addresstext set + sprk_isresolved=false for an UNRESOLVED external address.
    The schema deliberately allows both lookups null so unresolved external rows are creatable (Q-D).

    ADR-034 (path C, spec/CLAUDE.md ADR Tensions): two TYPED nullable person lookups
    (systemuser/contact) — NOT the (personId, personIdType) tuple, NOT a polymorphic lookup.
    Only 2 person targets, so typed lookups honor ADR-034 intent (typed identity, no text-name
    matching) + add FK integrity + DataGrid person-chip auto-derivation. Documented by ADR task 004.

    IDEMPOTENT: skips any component that already exists (describe-before-write). Safe to re-run.

    -------------------------------------------------------------------------------------------
    >>> LIVE APPLY DEFERRED (describe-before-write gate) <<<
    Dataverse MCP was UNAVAILABLE in the authoring session (2026-07-19). This script is the
    authored deliverable; it has NOT been run against a live environment. Owner / next session:
      1. `az login`  (Dataverse audience token via az account get-access-token)
      2. MCP `describe` (or the -WhatIf run) to CONFIRM no `sprk_communicationparticipant` exists.
      3. Run this script; then verify the 6 fields + relationships via the -Verify pass / MCP.
    The sprk_role integers are a LOCAL option set on a NET-NEW entity, so no cross-entity collision
    is possible (100000000-3 are always free within this entity's own option set). The describe
    gate exists to confirm the ENTITY itself is absent before create.
    -------------------------------------------------------------------------------------------

.PARAMETER DataverseOrg
    Target environment URL (e.g. https://spaarkedev1.crm.dynamics.com). Defaults to $env:DATAVERSE_URL.

.PARAMETER SolutionUniqueName
    Unique name of the project's UNMANAGED solution to add the components to (ADR-022 / ADR-027).
    When supplied, components are created inside the solution via the MSCRM.SolutionUniqueName header.
    Leave blank to create in the Default solution (owner then adds to the messaging solution manually).

.PARAMETER WhatIf
    Show the deployment plan (and run the describe-before-write existence checks) without creating anything.

.EXAMPLE
    .\Deploy-CommunicationParticipantSchema.ps1 -DataverseOrg "https://spaarkedev1.crm.dynamics.com" -WhatIf
    # describe-before-write dry run — confirms absence, prints the plan, creates nothing

.EXAMPLE
    .\Deploy-CommunicationParticipantSchema.ps1 -DataverseOrg "https://spaarkedev1.crm.dynamics.com" -SolutionUniqueName "MessagingCommunication"

.NOTES
    Project: messaging-communication-app-r2   Task: 003 (FR-08)
    Skill:   .claude/skills/dataverse-create-schema/SKILL.md (Web API + PowerShell)
    Mirrors: sprk_communicationattachment (Org-owned, N:1 to sprk_communication, Cascade delete)
    Author:  task-execute 003, 2026-07-19  (STANDARD rigor, sonnet@high)
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseOrg = $env:DATAVERSE_URL,

    [Parameter(Mandatory = $false)]
    [string]$SolutionUniqueName = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DataverseOrg)) {
    throw "DataverseOrg not supplied and DATAVERSE_URL env var is empty. Pass -DataverseOrg https://<org>.crm.dynamics.com"
}

$envHost = ([Uri]$DataverseOrg).Host
$BaseUrl = "https://$envHost/api/data/v9.2"

Write-Host "=== Deploy sprk_communicationparticipant junction ===" -ForegroundColor Cyan
Write-Host "  Target : $DataverseOrg" -ForegroundColor Gray
Write-Host "  Solution: $(if ($SolutionUniqueName) { $SolutionUniqueName } else { '(Default)' })" -ForegroundColor Gray
Write-Host ""

# --- Auth -------------------------------------------------------------------
$token = (az account get-access-token --resource "https://$envHost" --query accessToken -o tsv)
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Failed to acquire a Dataverse token. Run 'az login' first (audience https://$envHost)."
}

# --- Helpers ----------------------------------------------------------------
function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label"       = $Text
            "LanguageCode" = 1033
        })
    }
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $headers = @{
        "Authorization"     = "Bearer $token"
        "OData-MaxVersion"  = "4.0"
        "OData-Version"     = "4.0"
        "Content-Type"      = "application/json"
        "Accept"            = "application/json"
    }
    # Add net-new metadata components to the project solution (ADR-022 / ADR-027)
    if ($SolutionUniqueName -and $Method -eq "POST") {
        $headers["MSCRM.SolutionUniqueName"] = $SolutionUniqueName
    }
    $params = @{ Uri = "$BaseUrl/$Endpoint"; Headers = $headers; Method = $Method }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }
    return Invoke-RestMethod @params
}

function Test-EntityExists {
    param([string]$LogicalName)
    try {
        Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" | Out-Null
        return $true
    } catch { return $false }
}

function Test-AttributeExists {
    param([string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" | Out-Null
        return $true
    } catch { return $false }
}

$Entity = "sprk_communicationparticipant"

# ============================================================================
# STEP 1 (describe-before-write): confirm the junction does NOT already exist
# ============================================================================
Write-Host "[1] describe-before-write: does $Entity already exist?" -ForegroundColor Yellow
if (Test-EntityExists -LogicalName $Entity) {
    Write-Host "    $Entity ALREADY EXISTS — task 001 audit said it should be net-new." -ForegroundColor Red
    Write-Host "    STOP. Confirm this is the intended junction before proceeding. Exiting." -ForegroundColor Red
    return
}
Write-Host "    Confirmed absent — proceeding with net-new create." -ForegroundColor Green

if ($WhatIfPreference) {
    Write-Host ""
    Write-Host "WHATIF PLAN (nothing created):" -ForegroundColor Cyan
    Write-Host "  CREATE entity  $Entity (Organization-owned, primary sprk_name Text 400)"
    Write-Host "  CREATE N:1     sprk_communicationparticipant_communication  (sprk_communication -> $Entity, REQUIRED, Cascade)"
    Write-Host "  CREATE N:1     sprk_communicationparticipant_systemuser     (sprk_systemuser    -> $Entity, nullable, RemoveLink)"
    Write-Host "  CREATE N:1     sprk_communicationparticipant_contact        (sprk_contact       -> $Entity, nullable, RemoveLink)"
    Write-Host "  ADD attr       sprk_role        (LOCAL Choice From=100000000/To=100000001/Cc=100000002/Bcc=100000003)"
    Write-Host "  ADD attr       sprk_addresstext (Text 400)"
    Write-Host "  ADD attr       sprk_isresolved  (Boolean, default No)"
    Write-Host "  PUBLISH        $Entity"
    return
}

# ============================================================================
# STEP 2: create the entity with the primary sprk_name
# ============================================================================
Write-Host "[2] Creating entity $Entity ..." -ForegroundColor Yellow
$entityDef = @{
    "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
    "SchemaName"            = "sprk_CommunicationParticipant"
    "DisplayName"           = New-Label "Communication Participant"
    "DisplayCollectionName" = New-Label "Communication Participants"
    "Description"           = New-Label "Message-grain participant index (junction). One row per person/address per message with a role (From/To/Cc/Bcc). Makes participants queryable and supports unresolved external rows. FR-08."
    "OwnershipType"         = "OrganizationOwned"
    "IsActivity"            = $false
    "HasNotes"              = $false
    "HasActivities"         = $false
    "PrimaryNameAttribute"  = "sprk_name"
    "Attributes"            = @(@{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_Name"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 400
        "FormatName"    = @{ "Value" = "Text" }
        "DisplayName"   = New-Label "Name"
        "Description"   = New-Label "Primary name, e.g. '{personDisplay|address} - {role}'. Set by the participant-index write (050)."
        "IsPrimaryName" = $true
    })
}
Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef | Out-Null
Write-Host "    Created $Entity (primary sprk_name)." -ForegroundColor Green

# ============================================================================
# STEP 3: lookups (created via RelationshipDefinitions — lookups cannot be POSTed as attributes)
# ============================================================================
function New-Lookup {
    param(
        [string]$ReferencedEntity,   # parent (1 side)
        [string]$LookupSchemaName,   # e.g. sprk_Communication
        [string]$LookupDisplay,
        [string]$LookupDescription,
        [bool]$Required,
        [string]$DeleteBehavior,     # Cascade | RemoveLink | Restrict
        [string]$RelationshipSchemaName
    )
    $def = @{
        "@odata.type"        = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"         = $RelationshipSchemaName
        "ReferencedEntity"   = $ReferencedEntity
        "ReferencingEntity"  = $Entity
        "CascadeConfiguration" = @{
            "Assign" = "NoCascade"; "Delete" = $DeleteBehavior; "Merge" = "NoCascade"
            "Reparent" = "NoCascade"; "Share" = "NoCascade"; "Unshare" = "NoCascade"
        }
        "Lookup" = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"    = $LookupSchemaName
            "DisplayName"   = New-Label $LookupDisplay
            "Description"   = New-Label $LookupDescription
            "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        }
    }
    Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $def | Out-Null
}

Write-Host "[3] Creating lookups ..." -ForegroundColor Yellow

# 3a. sprk_communication — REQUIRED parent, Cascade delete (mirror sprk_communicationattachment)
if (Test-AttributeExists $Entity "sprk_communication") {
    Write-Host "    sprk_communication exists — skip." -ForegroundColor DarkGray
} else {
    New-Lookup -ReferencedEntity "sprk_communication" -LookupSchemaName "sprk_Communication" `
        -LookupDisplay "Communication" `
        -LookupDescription "The parent message (message grain). Required. Cascade delete." `
        -Required $true -DeleteBehavior "Cascade" `
        -RelationshipSchemaName "sprk_communicationparticipant_communication"
    Write-Host "    Created sprk_communication (required, Cascade)." -ForegroundColor Green
}

# 3b. sprk_systemuser — nullable, RemoveLink
if (Test-AttributeExists $Entity "sprk_systemuser") {
    Write-Host "    sprk_systemuser exists — skip." -ForegroundColor DarkGray
} else {
    New-Lookup -ReferencedEntity "systemuser" -LookupSchemaName "sprk_SystemUser" `
        -LookupDisplay "System User" `
        -LookupDescription "Resolved internal (Entra) participant. Nullable — set for a resolved systemuser only (ADR-034 typed identity)." `
        -Required $false -DeleteBehavior "RemoveLink" `
        -RelationshipSchemaName "sprk_communicationparticipant_systemuser"
    Write-Host "    Created sprk_systemuser (nullable, RemoveLink)." -ForegroundColor Green
}

# 3c. sprk_contact — nullable, RemoveLink
if (Test-AttributeExists $Entity "sprk_contact") {
    Write-Host "    sprk_contact exists — skip." -ForegroundColor DarkGray
} else {
    New-Lookup -ReferencedEntity "contact" -LookupSchemaName "sprk_Contact" `
        -LookupDisplay "Contact" `
        -LookupDescription "Resolved external participant. Nullable — set for a resolved contact only. Enables back-fill when a contact is later created." `
        -Required $false -DeleteBehavior "RemoveLink" `
        -RelationshipSchemaName "sprk_communicationparticipant_contact"
    Write-Host "    Created sprk_contact (nullable, RemoveLink)." -ForegroundColor Green
}

# ============================================================================
# STEP 4: value attributes — sprk_role (local Choice), sprk_addresstext, sprk_isresolved
# ============================================================================
function Add-Attribute {
    param([hashtable]$AttrDef)
    Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$Entity')/Attributes" -Method "POST" -Body $AttrDef | Out-Null
}

Write-Host "[4] Creating value attributes ..." -ForegroundColor Yellow

# 4a. sprk_role — LOCAL option set (entity-scoped; no cross-entity collision possible)
if (Test-AttributeExists $Entity "sprk_role") {
    Write-Host "    sprk_role exists — skip." -ForegroundColor DarkGray
} else {
    Add-Attribute @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = "sprk_Role"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label "Role"
        "Description"   = New-Label "The participant's role on the message header."
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "IsGlobal"      = $false
            "OptionSetType" = "Picklist"
            "Options"       = @(
                @{ "Value" = 100000000; "Label" = New-Label "From" },
                @{ "Value" = 100000001; "Label" = New-Label "To" },
                @{ "Value" = 100000002; "Label" = New-Label "Cc" },
                @{ "Value" = 100000003; "Label" = New-Label "Bcc" }
            )
        }
    }
    Write-Host "    Created sprk_role (From=100000000/To=100000001/Cc=100000002/Bcc=100000003)." -ForegroundColor Green
}

# 4b. sprk_addresstext — raw email address
if (Test-AttributeExists $Entity "sprk_addresstext") {
    Write-Host "    sprk_addresstext exists — skip." -ForegroundColor DarkGray
} else {
    Add-Attribute @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_AddressText"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 400
        "FormatName"    = @{ "Value" = "Email" }
        "DisplayName"   = New-Label "Address Text"
        "Description"   = New-Label "Raw email address. Populated for unresolved external parties and retained for resolved ones as provenance."
    }
    Write-Host "    Created sprk_addresstext (Text 400)." -ForegroundColor Green
}

# 4c. sprk_isresolved — Boolean, default No
if (Test-AttributeExists $Entity "sprk_isresolved") {
    Write-Host "    sprk_isresolved exists — skip." -ForegroundColor DarkGray
} else {
    Add-Attribute @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"    = "sprk_IsResolved"
        "RequiredLevel" = @{ "Value" = "None" }
        "DefaultValue"  = $false
        "DisplayName"   = New-Label "Is Resolved"
        "Description"   = New-Label "True when a person lookup (systemuser/contact) is set; false for an unresolved external address. Enables back-fill."
        "OptionSet"     = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
            "TrueOption"  = @{ "Value" = 1; "Label" = New-Label "Yes" }
            "FalseOption" = @{ "Value" = 0; "Label" = New-Label "No" }
        }
    }
    Write-Host "    Created sprk_isresolved (Boolean, default No)." -ForegroundColor Green
}

# ============================================================================
# STEP 5: publish
# ============================================================================
Write-Host "[5] Publishing customizations ..." -ForegroundColor Yellow
$publishXml = @{ "ParameterXml" = "<importexportxml><entities><entity>$Entity</entity></entities></importexportxml>" }
Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
Write-Host "    Published." -ForegroundColor Green

# ============================================================================
# STEP 6: verify
# ============================================================================
Write-Host "[6] Verifying ..." -ForegroundColor Yellow
$attrs = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$Entity')?`$expand=Attributes(`$select=LogicalName,AttributeType)"
$expected = @("sprk_name","sprk_communication","sprk_systemuser","sprk_contact","sprk_role","sprk_addresstext","sprk_isresolved")
foreach ($e in $expected) {
    $a = $attrs.Attributes | Where-Object { $_.LogicalName -eq $e }
    if ($a) { Write-Host "    OK  $e ($($a.AttributeType))" -ForegroundColor Green }
    else    { Write-Host "    MISSING  $e" -ForegroundColor Red }
}
Write-Host ""
Write-Host "Done. Remember: exactly-one-of systemuser/contact is a WRITE invariant (task 050), not a schema rule." -ForegroundColor Cyan
