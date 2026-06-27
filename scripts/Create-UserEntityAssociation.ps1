<#
.SYNOPSIS
    Deploys the sprk_userentityassociation Dataverse junction entity (R3 Part 1 Phase 2, FR-2P2.1).

.DESCRIPTION
    Creates the sprk_userentityassociation junction entity used by Phase 2 of the
    user-record membership architecture (ADR-034). This is the materialized junction
    that backs `GET /api/users/me/memberships/{entityType}` once per-request FetchXML
    (Phase 1A) hits performance limits (target trigger: p95 > 500 ms).

    Schema (7 columns per spec.md FR-2P2.1 + design.md Phase 2 table):
      - sprk_personid          : Text(36) REQUIRED — canonical GUID string of the person
                                 (NOT a Lookup: the "person" can be one of 6 different
                                 table types — see sprk_personidtype disambiguator below.
                                 Polymorphic Lookup not used because Dataverse polymorphic
                                 lookups don't span the {SystemUser, Contact, Team, BU,
                                 Account, Organization} set we need; instead we encode
                                 the type via a sibling OptionSet — pattern aligned with
                                 ADR-024 polymorphic-resolver guidance.

                                 IMPLEMENTATION NOTE: spec FR-2P2.1 lists this field as
                                 "Uniqueidentifier" but Dataverse Web API rejects creating
                                 custom UniqueIdentifierAttributeMetadata columns
                                 ("Attribute of type UniqueIdentifierAttributeMetadata
                                 cannot be created through the SDK"). The native type is
                                 reserved for system primary IDs + lookup internals.
                                 Custom GUID-typed columns are stored as Text(36) and
                                 treated as Guid strings at the application layer.
                                 No semantic difference for our access patterns.)
      - sprk_personidtype      : OptionSet — disambiguates which table sprk_personid
                                 references. Values: SystemUser=1, Contact=2, Team=3,
                                 BusinessUnit=4, Account=5, Organization=6.
      - sprk_entitylogicalname : Text(100) REQUIRED — target entity logical name
                                 (e.g., "sprk_matter")
      - sprk_entityrecordid    : Text(36) REQUIRED — canonical GUID string of the target
                                 record (same SDK rationale as sprk_personid above)
      - sprk_role              : Text(100) — discovered role name (e.g., "assignedAttorney")
      - sprk_sourcefield       : Text(100) — provenance: which field on the target entity
                                 provided this association (e.g., "sprk_assignedattorneyid")
      - sprk_lastsyncedon      : DateTime REQUIRED — staleness audit (when junction row
                                 was last reconciled by MembershipJunctionUpdater or
                                 MembershipReconciliationJob)

    Indexing approach (design nuance — DOCUMENTED in docs/data-model/sprk_userentityassociation.md):
      Dataverse does NOT expose "indexes" as first-class entities like SQL — instead
      it auto-creates indexes for:
        - Unique alternate keys (EntityKey metadata)
        - Lookup attributes (sprk_*id)
      To force the equivalent of a composite index for our bidirectional query workload
      we define ONE alternate key as a 5-column composite covering the natural idempotency
      key for MembershipJunctionUpdater upsert + the primary "find all entities a person
      belongs to" query path:

        AlternateKey #1: (sprk_personid, sprk_personidtype, sprk_entitylogicalname,
                          sprk_entityrecordid, sprk_sourcefield)

      The 2nd composite index ({sprk_entitylogicalname, sprk_entityrecordid} — "find all
      people associated with a record") is NOT created as a unique alternate key because
      multiple persons may share a (entityLogicalName, entityRecordId) pair — uniqueness
      doesn't apply. For that query path we rely on:
        (a) Dataverse's Cosmos-DB-backed storage + query optimizer; or
        (b) Adding a non-unique composite index via Dataverse-managed solutions if
            observed perf is insufficient at scale (deferred to Phase 2 implementation
            tuning — see ADR-034).

    The script is idempotent: safe to re-run. It checks for existing entity / attributes /
    alternate keys before creating, and reports skip vs create per step.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (defaults to $env:DATAVERSE_URL or Spaarke Dev).

.PARAMETER DryRun
    Preview-only mode — no Dataverse modifications.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Create-UserEntityAssociation.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Create-UserEntityAssociation.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project   : spaarke-platform-foundations-r3
    Task      : 070 — Create sprk_userentityassociation entity + indexes
    Spec      : FR-2P2.1, AC-1P2.1
    Design    : design.md Part 1 §"Phase 2 — Junction table sprk_userentityassociation"
    ADRs      : ADR-034 (membership pattern, task 037), ADR-024 (polymorphic-resolver pattern)
    Naming    : Distinct from existing sprk_fieldmappingprofile/sprk_fieldmappingrule (those
                are field-value-copy config, NOT membership) and distinct from AssociationResolver
                PCF (different scope — see naming-collision register in design.md).
    Verified  : Entity does NOT exist in spaarkedev1 prior to first run
                (mcp__dataverse__describe('tables/sprk_userentityassociation') returned 11620785).
    Created   : 2026-06-21
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Authentication
# -----------------------------------------------------------------------------

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Run 'az login' first."
    }

    return $tokenResult.Trim()
}

# -----------------------------------------------------------------------------
# Web API Helpers
# -----------------------------------------------------------------------------

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

# -----------------------------------------------------------------------------
# Idempotency Checks
# -----------------------------------------------------------------------------

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Test-AttributeExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$AttributeLogicalName
    )
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" `
            -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Test-EntityKeyExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$KeySchemaName
    )
    try {
        $keys = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Keys" -Method "GET"
        foreach ($k in $keys.value) {
            if ($k.SchemaName -eq $KeySchemaName) { return $true }
        }
        return $false
    }
    catch {
        return $false
    }
}

# -----------------------------------------------------------------------------
# Entity Creation (primary name auto-generated via AutoNumberFormat)
# -----------------------------------------------------------------------------

function New-UserEntityAssociationEntity {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Creating sprk_userentityassociation entity..." -ForegroundColor Yellow

    # Primary Name: auto-numbered so makers don't have to populate it.
    # Junction rows are machine-managed; the meaningful "identity" is the composite
    # alternate key, not the primary name. Format: UEA-{000000}.
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_userentityassociation"
        "DisplayName"           = New-Label -Text "User-Entity Association"
        "DisplayCollectionName" = New-Label -Text "User-Entity Associations"
        "Description"           = New-Label -Text "Materialized junction row linking a person (one of 6 identity table types) to a target Dataverse record. Backs GET /api/users/me/memberships/{entityType} once Phase 2 is implemented (R3 task 070). See ADR-034."
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"       = "sprk_name"
                "RequiredLevel"    = @{ "Value" = "None" }
                "MaxLength"        = 100
                "DisplayName"      = New-Label -Text "Name"
                "Description"      = New-Label -Text "Auto-generated identifier (UEA-NNNNNN). Junction rows are machine-managed; meaningful identity is the composite alternate key sprk_uea_natural_key."
                "IsPrimaryName"    = $true
                "AutoNumberFormat" = "UEA-{SEQNUM:6}"
            }
        )
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef

    Write-Host "  Entity created successfully" -ForegroundColor Green
}

function Add-EntityAttribute {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [object]$AttributeDef
    )

    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Adding attribute: $schemaName..." -ForegroundColor Gray

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" `
        -Method "POST" -Body $AttributeDef

    Write-Host "    Added: $schemaName" -ForegroundColor Green
}

function Add-CompositeAlternateKey {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$KeySchemaName,
        [string]$KeyDisplayName,
        [string[]]$KeyAttributes
    )

    Write-Host "  Adding alternate key: $KeySchemaName ($($KeyAttributes -join ', '))..." -ForegroundColor Gray

    $keyDef = @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.EntityKeyMetadata"
        "SchemaName"      = $KeySchemaName
        "DisplayName"     = New-Label -Text $KeyDisplayName
        "KeyAttributes"   = $KeyAttributes
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Keys" `
        -Method "POST" -Body $keyDef

    Write-Host "    Created alternate key: $KeySchemaName" -ForegroundColor Green
}

function Publish-UserEntityAssociationCustomizations {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_userentityassociation</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but entity should be available shortly" -ForegroundColor Yellow
    }
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "===============================================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_userentityassociation Entity (R3 task 070, FR-2P2.1)" -ForegroundColor Cyan
    Write-Host "===============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "MODE: DRY RUN (no Dataverse modifications)" -ForegroundColor Yellow
    }
    Write-Host ""

    # ---- Step 0: Auth ----
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # ---- Step 1: Check if entity exists ----
    Write-Host "Step 1: Checking if entity exists..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_userentityassociation"

    if ($entityExists) {
        Write-Host "  sprk_userentityassociation already exists — will verify / add missing fields only" -ForegroundColor Yellow
    }
    else {
        Write-Host "  sprk_userentityassociation does NOT exist — will create" -ForegroundColor Gray
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would create entity sprk_userentityassociation (primary attribute sprk_name auto-numbered UEA-NNNNNN)" -ForegroundColor Yellow
        }
        else {
            New-UserEntityAssociationEntity -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 2: Add the 7 columns (sprk_name primary already covered by entity create) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying the 7 functional attributes..." -ForegroundColor Cyan

    $attributes = @(
        # 1. sprk_personid — Text(36) holding canonical GUID string. REQUIRED.
        #    NOTE: Dataverse Web API forbids creating UniqueIdentifierAttributeMetadata
        #    via the SDK ("Attribute of type UniqueIdentifierAttributeMetadata cannot be
        #    created through the SDK"). The native Uniqueidentifier type is reserved for
        #    system primary IDs + lookup-pair internals. Custom GUID-typed columns must
        #    be stored as Text(36) and treated as Guid strings at the application layer.
        #    This is documented in the data-model doc + ADR-034.
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_personid"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 36
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = New-Label -Text "Person Id"
            "Description"   = New-Label -Text "Resolved identity GUID of the person (stored as canonical 36-char string — Dataverse SDK forbids creating native Uniqueidentifier columns). Disambiguated by sprk_personidtype (which of 6 identity tables it belongs to). NOT a Lookup because polymorphic lookups can't span the {SystemUser, Contact, Team, BusinessUnit, Account, Organization} set."
        },

        # 2. sprk_personidtype — Local OptionSet (6 values)
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_personidtype"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "DisplayName"   = New-Label -Text "Person Id Type"
            "Description"   = New-Label -Text "Disambiguates which identity table sprk_personid references. Values match the 6 identity types in ADR-034: SystemUser=1, Contact=2, Team=3, BusinessUnit=4, Account=5, Organization=6."
            "OptionSet"     = @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                "OptionSetType" = "Picklist"
                "IsGlobal"      = $false
                "Options"       = @(
                    @{ "Value" = 1; "Label" = New-Label -Text "SystemUser" },
                    @{ "Value" = 2; "Label" = New-Label -Text "Contact" },
                    @{ "Value" = 3; "Label" = New-Label -Text "Team" },
                    @{ "Value" = 4; "Label" = New-Label -Text "BusinessUnit" },
                    @{ "Value" = 5; "Label" = New-Label -Text "Account" },
                    @{ "Value" = 6; "Label" = New-Label -Text "Organization" }
                )
            }
        },

        # 3. sprk_entitylogicalname — Text(100) REQUIRED
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_entitylogicalname"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 100
            "DisplayName"   = New-Label -Text "Entity Logical Name"
            "Description"   = New-Label -Text "Logical name of the target Dataverse entity (e.g., 'sprk_matter'). Combined with sprk_entityrecordid identifies the target record."
        },

        # 4. sprk_entityrecordid — Text(36) holding canonical GUID string. REQUIRED.
        #    See sprk_personid note above re: Dataverse SDK Uniqueidentifier restriction.
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_entityrecordid"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 36
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = New-Label -Text "Entity Record Id"
            "Description"   = New-Label -Text "GUID of the target record on the entity named in sprk_entitylogicalname (stored as canonical 36-char string — see sprk_personid for SDK rationale)."
        },

        # 5. sprk_role — Text(100)
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_role"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100
            "DisplayName"   = New-Label -Text "Role"
            "Description"   = New-Label -Text "Discovered role name for the person on the target record (e.g., 'assignedAttorney', 'paralegal'). Derived by MembershipFieldDiscoveryService / per-entity overrides."
        },

        # 6. sprk_sourcefield — Text(100)
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_sourcefield"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100
            "DisplayName"   = New-Label -Text "Source Field"
            "Description"   = New-Label -Text "Provenance: which lookup field on the target entity provided this association (e.g., 'sprk_assignedattorneyid'). Used as part of the composite idempotency key for upsert."
        },

        # 7. sprk_lastsyncedon — DateTime REQUIRED
        @{
            "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            "SchemaName"       = "sprk_lastsyncedon"
            "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
            "DisplayName"      = New-Label -Text "Last Synced On"
            "Description"      = New-Label -Text "Timestamp when this junction row was last reconciled by MembershipJunctionUpdater (event handler) or MembershipReconciliationJob (nightly recon). Used for staleness audit."
            "Format"           = "DateAndTime"
            "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        }
    )

    foreach ($attr in $attributes) {
        $name = $attr.SchemaName
        if (-not $entityExists) {
            if ($DryRun) {
                Write-Host "  [DRY RUN] Would add attribute: $name" -ForegroundColor Yellow
            }
            else {
                Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                    -EntityLogicalName "sprk_userentityassociation" -AttributeDef $attr
            }
        }
        else {
            $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName "sprk_userentityassociation" -AttributeLogicalName $name
            if ($attrExists) {
                Write-Host "  $name already exists, skipping" -ForegroundColor Gray
            }
            else {
                if ($DryRun) {
                    Write-Host "  [DRY RUN] Would add attribute: $name" -ForegroundColor Yellow
                }
                else {
                    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                        -EntityLogicalName "sprk_userentityassociation" -AttributeDef $attr
                }
            }
        }
    }

    # ---- Step 3: Composite alternate key (natural idempotency key) ----
    # This is the closest Dataverse-native equivalent to a SQL composite index.
    # It also forces the unique-upsert contract for MembershipJunctionUpdater.
    Write-Host ""
    Write-Host "Step 3: Adding/verifying composite alternate key..." -ForegroundColor Cyan

    $keySchemaName = "sprk_uea_natural_key"
    $keyAttrs = @("sprk_personid", "sprk_personidtype", "sprk_entitylogicalname", "sprk_entityrecordid", "sprk_sourcefield")

    $keyExists = Test-EntityKeyExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_userentityassociation" -KeySchemaName $keySchemaName

    if ($keyExists) {
        Write-Host "  Alternate key $keySchemaName already exists, skipping" -ForegroundColor Gray
    }
    else {
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would add alternate key $keySchemaName ($($keyAttrs -join ', '))" -ForegroundColor Yellow
        }
        else {
            Add-CompositeAlternateKey -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName "sprk_userentityassociation" `
                -KeySchemaName $keySchemaName `
                -KeyDisplayName "UEA Natural Key" `
                -KeyAttributes $keyAttrs
        }
    }

    # ---- Step 4: Publish customizations ----
    Write-Host ""
    Write-Host "Step 4: Publishing customizations..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would publish sprk_userentityassociation customizations" -ForegroundColor Yellow
    }
    else {
        Publish-UserEntityAssociationCustomizations -Token $token -BaseUrl $EnvironmentUrl
    }

    # ---- Step 5: Verify ----
    Write-Host ""
    Write-Host "Step 5: Verifying deployment..." -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "  [DRY RUN] Skipping verification" -ForegroundColor Yellow
    }
    else {
        $verifyResp = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_userentityassociation')?`$expand=Attributes,Keys" `
            -Method "GET"
        $sprkAttrs = $verifyResp.Attributes | Where-Object { $_.LogicalName -like "sprk_*" -and $_.LogicalName -ne "sprk_userentityassociationid" }
        Write-Host ("  Entity: {0}" -f $verifyResp.LogicalName) -ForegroundColor Green
        Write-Host ("  sprk_* attribute count: {0}" -f $sprkAttrs.Count) -ForegroundColor Green
        foreach ($a in ($sprkAttrs | Sort-Object LogicalName)) {
            Write-Host ("    - {0} ({1})" -f $a.LogicalName, $a.AttributeType) -ForegroundColor Gray
        }
        Write-Host ("  Alternate keys: {0}" -f $verifyResp.Keys.Count) -ForegroundColor Green
        foreach ($k in $verifyResp.Keys) {
            Write-Host ("    - {0} ({1})" -f $k.SchemaName, ($k.KeyAttributes -join ', ')) -ForegroundColor Gray
        }
    }

    Write-Host ""
    Write-Host "===============================================================" -ForegroundColor Cyan
    Write-Host " Done." -ForegroundColor Cyan
    Write-Host "===============================================================" -ForegroundColor Cyan
}

Main
