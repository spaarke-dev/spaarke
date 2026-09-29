<#
.SYNOPSIS
    Creates the sprk_accessevent append-only access event log table in Dataverse (FR-32).

.DESCRIPTION
    Idempotent schema deployment for `sprk_accessevent` — the append-only log of GRANT and DENY
    STATE CHANGES that backs the unified-access-control-r2 attestation story (spec FR-32,
    design.md §7). Creates:

      * the entity (Organization-owned, audit ENABLED on the log itself for tamper-evidence),
      * three local option sets (event type · subject kind · access level),
      * seventeen custom attributes (plus the primary name field created with the entity),

    all under the `MSCRM.SolutionUniqueName` header so the components land in the Spaarke
    publisher (prefix `sprk`) and the target unmanaged solution — never the environment's
    default publisher.

    DERIVED ACCESS IS NEVER STORED HERE. There is deliberately no "effective level" /
    "computed access" column: point-in-time derived access is reconstructed by evaluator replay
    (task 088), not materialized (spec FR-32 MUST; design.md §7).

    Re-running is a no-op: every create is guarded by an existence probe. Safe to run against an
    environment that already has the table.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionName
    Unmanaged solution unique name that will own the components. Default: SpaarkeCore
    (the solution that already owns sprk_externalrecordaccess and sprk_noaccessentry).

.PARAMETER DryRun
    Print the full plan (what exists, what would be created) and exit WITHOUT issuing a single
    write. Use this first — it is a read-only reconnaissance pass.

.EXAMPLE
    .\Deploy-AccessEventEntity.ps1 -DryRun
    .\Deploy-AccessEventEntity.ps1
    .\Deploy-AccessEventEntity.ps1 -EnvironmentUrl "https://contoso.crm.dynamics.com" -SolutionName "SpaarkeCore"

.NOTES
    Project : unified-access-control-r2
    Task    : 086 — FR-32 access event log schema
    Created : 2026-09-09
    Docs    : docs/data-model/sprk_accessevent.md

    ⚠️  PREFIX SAFETY. Do NOT create this table with `mcp__dataverse__create_table` — that tool
        has no publisher/solution parameter and silently uses the environment's DEFAULT publisher
        (this repo already lost a table to that trap: .claude/FAILURE-MODES.md AP-13, the stray
        `cr140_noaccessentry`). Dataverse prefixes are IMMUTABLE, so a wrong prefix cannot be
        fixed in place — the table has to be dropped and rebuilt. This script therefore:
          1. asserts the target solution's publisher carries customizationprefix 'sprk' BEFORE
             creating anything, and
          2. re-reads the created entity and FAILS if its LogicalName is not `sprk_accessevent`.

    Requires: Azure CLI logged in (`az login`) with a principal that can customize the target
    environment. PowerShell 7+ (uses Invoke-RestMethod).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$SolutionName = "SpaarkeCore",

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$EntityLogicalName = "sprk_accessevent"
$EntitySchemaName  = "sprk_AccessEvent"
$ExpectedPrefix    = "sprk"

# ============================================================================
# Helpers (same shape as scripts/Deploy-PrecedentEntity.ps1 — the repo's
# canonical raw-Web-API schema-deployment pattern)
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)
    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'."
    }
    return $tokenResult.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{}
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) { $errorDetails = $errorJson.error.message }
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

function New-LocalOptionSet {
    param([string]$Description, [hashtable[]]$Options)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "IsGlobal"      = $false
        "OptionSetType" = "Picklist"
        "Description"   = (New-Label $Description)
        "Options"       = $Options
    }
}

function New-Option {
    param([int]$Value, [string]$Label, [string]$Description)
    return @{
        "Value"       = $Value
        "Label"       = (New-Label $Label)
        "Description" = (New-Label $Description)
    }
}

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -Method "GET" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|Could not find|404|9870979|NotFound") { return $false }
        throw
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" `
            -Method "GET" | Out-Null
        return $true
    }
    catch { return $false }
}

function Add-SolutionComponent {
    param(
        [string]$Token, [string]$BaseUrl,
        [string]$ComponentId, [int]$ComponentType,
        [string]$SolutionUniqueName, [bool]$AddRequiredComponents = $false
    )
    try {
        $body = @{
            "ComponentId"           = $ComponentId
            "ComponentType"         = $ComponentType
            "SolutionUniqueName"    = $SolutionUniqueName
            "AddRequiredComponents" = $AddRequiredComponents
        }
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "AddSolutionComponent" -Method "POST" -Body $body | Out-Null
        Write-Host "    Added component $ComponentId (type $ComponentType) to $SolutionUniqueName" -ForegroundColor Green
    }
    catch {
        Write-Host "    Note: AddSolutionComponent for $ComponentId (type $ComponentType): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ============================================================================
# Attribute definitions (the FR-32 column contract)
# ============================================================================
#
# Design notes that a reader will otherwise ask about:
#
#  * Subject and target are (kind/entity-name + id-as-STRING + name SNAPSHOT), not lookups.
#    Two reasons, both load-bearing for an attestation log:
#      1. Polymorphism — a subject is a contact, an organization, a systemuser or a team; a
#         target is any record type. Modelling that as lookups means one nullable lookup per
#         possible type (the ADR-024 dual-field strategy is for SMALL CLOSED parent sets; this
#         is open-ended).
#      2. Immutability — a lookup with RemoveLink-on-delete NULLS ITSELF OUT when the subject or
#         target record is deleted, silently destroying the very history the log exists to hold.
#         A string id plus a name snapshot survives the delete. That is the whole point of the
#         name-snapshot columns: attestation must survive later renames AND deletions.
#
#  * ADR-044 (GUID canonicalization) is BINDING on the appender for the three id columns
#    (sprk_subjectid, sprk_targetrecordid, sprk_actinguserid). They are plain Edm.String columns —
#    there is no platform-side normalization — and replay finds rows by string `eq` on them. A
#    brace-wrapped or upper-cased id written once yields a row later queries silently miss. Write
#    them BARE and LOWERCASE.
#
#  * NO derived/effective-access column exists, by rule (spec FR-32 MUST; design.md §7).
#    sprk_accesslevel is the LITERAL level on the grant row being created/revoked, and
#    sprk_sharedrights is the LITERAL AccessRights mask handed to the POA Grant/ModifyAccess
#    call. Neither is an evaluator output. Derived access is REPLAYED (task 088), never stored.
#
#  * sprk_previousvalue / sprk_newvalue carry the before/after for the three flag-flip event
#    kinds (secure · restricted · standing-grant), so no event kind depends on the free-form
#    JSON column for a core field.

$EventTypeOptions = @(
    (New-Option 100000000 "Grant Created"           "A sprk_externalrecordaccess row was created (contact or org grant)."),
    (New-Option 100000001 "Grant Revoked"           "A sprk_externalrecordaccess row was deactivated (revocation, incl. closure cascade)."),
    (New-Option 100000002 "Share Granted"           "A Dataverse POA share was granted to a principal (GrantAccess/ModifyAccess)."),
    (New-Option 100000003 "Share Revoked"           "A Dataverse POA share was removed from a principal (RevokeAccess)."),
    (New-Option 100000004 "Deny Added"              "A sprk_noaccessentry deny-list veto was created."),
    (New-Option 100000005 "Deny Removed"            "A sprk_noaccessentry deny-list veto was deactivated."),
    (New-Option 100000006 "Secure Flag Changed"     "A root record's sprk_issecure flag was set or cleared through a BFF writer."),
    (New-Option 100000007 "Restricted Flag Changed" "A root record's sprk_accesspermission choice changed through a BFF writer."),
    (New-Option 100000008 "Standing Grant Changed"  "A contact's sprk_standinggrant policy flag changed through a BFF writer.")
)

$SubjectKindOptions = @(
    (New-Option 100000000 "Contact"      "External contact (CIAM principal) — contact.contactid."),
    (New-Option 100000001 "Organization" "sprk_organization — an org-wide grant or an org-scoped deny."),
    (New-Option 100000002 "System User"  "Internal Dataverse user — systemuser.systemuserid (POA shares)."),
    (New-Option 100000003 "Team"         "Dataverse team — team.teamid (POA shares to a team).")
)

# Mirrors sprk_externalrecordaccess.sprk_accesslevel exactly (verified live 2026-09-09).
$AccessLevelOptions = @(
    (New-Option 100000000 "View Only"   "Mirrors sprk_externalrecordaccess.sprk_accesslevel View Only."),
    (New-Option 100000001 "Collaborate" "Mirrors sprk_externalrecordaccess.sprk_accesslevel Collaborate."),
    (New-Option 100000002 "Full Access" "Mirrors sprk_externalrecordaccess.sprk_accesslevel Full Access.")
)

$Attributes = @(
    @{
        name = "sprk_eventtype"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_EventType"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "DisplayName"   = (New-Label "Event Type")
            "Description"   = (New-Label "Which grant/deny state change this row records. Closed vocabulary — the appender (task 087) may not invent values.")
            "IsAuditEnabled" = @{ "Value" = $true }
            "OptionSet"     = (New-LocalOptionSet "FR-32 access event vocabulary." $EventTypeOptions)
        }
    },
    @{
        name = "sprk_occurredon"
        def  = @{
            "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            "SchemaName"       = "sprk_OccurredOn"
            "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
            "Format"           = "DateAndTime"
            "DateTimeBehavior" = @{ "Value" = "UTC" }
            "DisplayName"      = (New-Label "Occurred On")
            "Description"      = (New-Label "UTC instant the STATE CHANGE happened (domain time). Distinct from createdon, which is when the log row was inserted — they differ for retried or batched cascade writes. Replay (task 088) orders on this column.")
        }
    },
    @{
        name = "sprk_subjectkind"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_SubjectKind"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "DisplayName"   = (New-Label "Subject Kind")
            "Description"   = (New-Label "What kind of principal sprk_subjectid identifies.")
            "OptionSet"     = (New-LocalOptionSet "Principal kinds an access event can name." $SubjectKindOptions)
        }
    },
    @{
        name = "sprk_subjectid"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_SubjectId"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 50
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Subject Id")
            "Description"   = (New-Label "GUID of the principal whose access changed, as BARE LOWERCASE text (ADR-044 — replay matches by string eq). String, not a lookup, so the row survives deletion of the principal; see the header note in Deploy-AccessEventEntity.ps1.")
        }
    },
    @{
        name = "sprk_subjectname"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_SubjectName"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 200
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Subject Name")
            "Description"   = (New-Label "Display name of the principal AT EVENT TIME (snapshot). Deliberate denormalization: attestation must survive a later rename or deletion.")
        }
    },
    @{
        name = "sprk_targetentityname"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_TargetEntityName"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 100
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Target Entity Name")
            "Description"   = (New-Label "Logical name of the record whose access changed (e.g. sprk_project, sprk_matter, sprk_workassignment, sprk_communication, contact for standing-grant events).")
        }
    },
    @{
        name = "sprk_targetrecordid"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_TargetRecordId"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 50
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Target Record Id")
            "Description"   = (New-Label "GUID of the record whose access changed, as BARE LOWERCASE text (ADR-044). String, not a lookup — same survives-deletion reasoning as sprk_subjectid.")
        }
    },
    @{
        name = "sprk_targetrecordname"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_TargetRecordName"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 400
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Target Record Name")
            "Description"   = (New-Label "Display name of the target record AT EVENT TIME (snapshot). Survives renames; 400 chars because matter/project names are long.")
        }
    },
    @{
        name = "sprk_accesslevel"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_AccessLevel"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = (New-Label "Access Level")
            "Description"   = (New-Label "The LITERAL sprk_externalrecordaccess.sprk_accesslevel value on the grant row being created or revoked. NOT an effective/derived access value — derived access is replayed (task 088), never stored. Empty for share, deny and flag-change events.")
            "OptionSet"     = (New-LocalOptionSet "Mirrors sprk_externalrecordaccess.sprk_accesslevel." $AccessLevelOptions)
        }
    },
    @{
        name = "sprk_sharedrights"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_SharedRights"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Shared Rights")
            "Description"   = (New-Label "The LITERAL Dataverse AccessRights mask passed to the POA call, e.g. 'ReadAccess,WriteAccess'. A record of what the share operation requested — NOT a computed effective-rights value. Empty for non-share events.")
        }
    },
    @{
        name = "sprk_previousvalue"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_PreviousValue"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Previous Value")
            "Description"   = (New-Label "Before-value for the flag-change event kinds (secure / restricted / standing grant), as text: 'true'/'false' for booleans, the option value + label for sprk_accesspermission. Empty for non-flag events.")
        }
    },
    @{
        name = "sprk_newvalue"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_NewValue"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "New Value")
            "Description"   = (New-Label "After-value for the flag-change event kinds. Same encoding as sprk_previousvalue.")
        }
    },
    @{
        name = "sprk_actinguserid"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_ActingUserId"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 50
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Acting User Id")
            "Description"   = (New-Label "GUID of the systemuser who caused the change, as BARE LOWERCASE text (ADR-044). Empty when the change was made by the app-only identity with no resolvable calling user (background/cascade paths) — sprk_actingusername then names the app registration.")
        }
    },
    @{
        name = "sprk_actingusername"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_ActingUserName"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 200
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Acting User Name")
            "Description"   = (New-Label "Display name of the acting user AT EVENT TIME (snapshot), or the app-only identity's name for system-initiated changes.")
        }
    },
    @{
        name = "sprk_evaluatorversion"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_EvaluatorVersion"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 50
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Evaluator Version")
            "Description"   = (New-Label "The access evaluator's version string current when the event was written (task 088 owns the constant + bump policy). Replay uses it to disclose when it answers a historical date with a newer evaluator than the one live on that date.")
        }
    },
    @{
        name = "sprk_correlationid"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_CorrelationId"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 100
            "FormatName"    = @{ "Value" = "Text" }
            "DisplayName"   = (New-Label "Correlation Id")
            "Description"   = (New-Label "Request correlation id from the BFF (same source as AuditEnrichmentMiddleware). Ties the N events of one cascade — e.g. FR-09 revoke-all — into a single operation.")
        }
    },
    @{
        name = "sprk_details"
        def  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_Details"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 4000
            "Format"        = "TextArea"
            "DisplayName"   = (New-Label "Details")
            "Description"   = (New-Label "Free-form JSON supplement (endpoint route, admin-supplied reason, grant row id, cascade parent). SUPPLEMENT ONLY — no event kind may depend on this column for a core field; every kind's core facts have dedicated columns.")
        }
    }
)

# ============================================================================
# Main
# ============================================================================

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Deploy sprk_accessevent (UAC-r2 task 086 / FR-32)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Environment : $EnvironmentUrl" -ForegroundColor Yellow
Write-Host "Solution    : $SolutionName" -ForegroundColor Yellow
Write-Host "Mode        : $(if ($DryRun) { 'DRY RUN (no writes)' } else { 'APPLY' })" -ForegroundColor Yellow
Write-Host ""

$token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
Write-Host "Authentication successful" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1 — PREFIX SAFETY GATE (see the AP-13 note in the header)
# ---------------------------------------------------------------------------
Write-Host "Step 1: Verify solution '$SolutionName' and its publisher prefix..." -ForegroundColor Cyan
$sol = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "solutions?`$filter=uniquename eq '$SolutionName'&`$select=solutionid,uniquename,ismanaged&`$expand=publisherid(`$select=uniquename,customizationprefix)" `
    -Method "GET"

if ($sol.value.Count -eq 0) {
    throw "Solution '$SolutionName' does not exist in $EnvironmentUrl. Create it (with the Spaarke publisher) before running this script — this script will NOT create a solution, because a solution created with the wrong publisher bakes in the wrong prefix permanently."
}
$solution = $sol.value[0]
$prefix = $solution.publisherid.customizationprefix
Write-Host "  Solution  : $($solution.uniquename) (managed=$($solution.ismanaged))" -ForegroundColor Gray
Write-Host "  Publisher : $($solution.publisherid.uniquename) (prefix='$prefix')" -ForegroundColor Gray

if ($prefix -ne $ExpectedPrefix) {
    throw "ABORT: solution '$SolutionName' belongs to publisher '$($solution.publisherid.uniquename)' with customizationprefix '$prefix', expected '$ExpectedPrefix'. Creating the table here would produce '${prefix}_accessevent', and Dataverse prefixes are IMMUTABLE. Fix the solution/publisher first."
}
if ($solution.ismanaged) {
    throw "ABORT: solution '$SolutionName' is MANAGED. Schema cannot be added to a managed solution."
}
Write-Host "  Prefix gate passed." -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Step 2 — Reconnaissance: what already exists?
# ---------------------------------------------------------------------------
Write-Host "Step 2: Reconnaissance..." -ForegroundColor Cyan
$entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName $EntityLogicalName
Write-Host "  Entity $EntityLogicalName : $(if ($entityExists) { 'EXISTS' } else { 'MISSING (will be created)' })" -ForegroundColor Gray

$missingAttributes = @()
if ($entityExists) {
    foreach ($attr in $Attributes) {
        if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeLogicalName $attr.name) {
            Write-Host "    $($attr.name) : exists" -ForegroundColor DarkGray
        }
        else {
            Write-Host "    $($attr.name) : MISSING (will be created)" -ForegroundColor Yellow
            $missingAttributes += $attr
        }
    }
}
else {
    $missingAttributes = $Attributes
    Write-Host "    (all $($Attributes.Count) custom attributes will be created)" -ForegroundColor Gray
}
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN complete — nothing was written." -ForegroundColor Cyan
    Write-Host "  Would create entity      : $(if ($entityExists) { 'no (already present)' } else { "yes ($EntitySchemaName)" })" -ForegroundColor Gray
    Write-Host "  Would create attributes  : $($missingAttributes.Count)" -ForegroundColor Gray
    Write-Host ""
    return
}

# ---------------------------------------------------------------------------
# Step 3 — Create the entity (primary name attribute only)
# ---------------------------------------------------------------------------
Write-Host "Step 3: Create entity $EntityLogicalName..." -ForegroundColor Cyan
if ($entityExists) {
    Write-Host "  Entity already exists — skipping." -ForegroundColor Green
}
else {
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = $EntitySchemaName
        "DisplayName"           = (New-Label "Access Event")
        "DisplayCollectionName" = (New-Label "Access Events")
        "Description"           = (New-Label "APPEND-ONLY log of access GRANT/DENY state changes (FR-32). Application code only ever CREATES rows here. Derived/effective access is NEVER stored — it is reconstructed by evaluator replay.")
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        # Audit ON for the log ITSELF: the rows are never legitimately updated or deleted, so any
        # audit entry on this table is evidence of tampering. Cheap tamper-evidence for a table
        # whose whole value is being legally defensible.
        "IsAuditEnabled"        = @{ "Value" = $true }
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_Name"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 300
                "FormatName"    = @{ "Value" = "Text" }
                "IsPrimaryName" = $true
                "DisplayName"   = (New-Label "Name")
                "Description"   = (New-Label "Human-readable one-line summary composed by the appender, e.g. 'Grant Created - Jane Doe -> Acme Litigation (Collaborate)'. Written by task 087's appender; there is no auto-naming plugin.")
            }
        )
    }

    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Entity created." -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 4 — Add attributes (idempotent, one at a time)
# ---------------------------------------------------------------------------
Write-Host "Step 4: Add attributes..." -ForegroundColor Cyan
$created = 0
foreach ($attr in $Attributes) {
    if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeLogicalName $attr.name) {
        Write-Host "  $($attr.name) already exists — skipping." -ForegroundColor DarkGray
        continue
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $attr.def `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Created $($attr.name)" -ForegroundColor Green
    $created++
}
Write-Host "  $created attribute(s) created; $($Attributes.Count - $created) already present." -ForegroundColor Gray
Write-Host ""

# ---------------------------------------------------------------------------
# Step 5 — Publish customizations
# ---------------------------------------------------------------------------
Write-Host "Step 5: Publish customizations..." -ForegroundColor Cyan
try {
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl -Endpoint "PublishXml" -Method "POST" `
        -Body @{ "ParameterXml" = "<importexportxml><entities><entity>$EntityLogicalName</entity></entities></importexportxml>" } | Out-Null
    Write-Host "  Published." -ForegroundColor Green
}
catch {
    Write-Host "  Publish warning (may have timed out; re-run is safe): $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 6 — Ensure the entity is a component of the target solution
# ---------------------------------------------------------------------------
Write-Host "Step 6: Add entity to solution '$SolutionName'..." -ForegroundColor Cyan
$entityMeta = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=MetadataId" -Method "GET"
Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
    -ComponentId $entityMeta.MetadataId -ComponentType 1 -SolutionUniqueName $SolutionName -AddRequiredComponents $true
Write-Host ""

# ---------------------------------------------------------------------------
# Step 7 — READ-BACK VERIFICATION + prefix assertion (never trust the request)
# ---------------------------------------------------------------------------
Write-Host "Step 7: Verify (read-back)..." -ForegroundColor Cyan
$verify = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=LogicalName,SchemaName,EntitySetName,OwnershipType,IsAuditEnabled,PrimaryNameAttribute&`$expand=Attributes(`$select=LogicalName,AttributeType,RequiredLevel)" `
    -Method "GET"

if ($verify.LogicalName -ne $EntityLogicalName) {
    throw "ABORT: read-back returned LogicalName '$($verify.LogicalName)', expected '$EntityLogicalName'. The table landed under the WRONG PUBLISHER PREFIX. Dataverse prefixes are immutable — delete the stray table and re-run after fixing the solution/publisher."
}
if (-not $verify.LogicalName.StartsWith("$ExpectedPrefix`_")) {
    throw "ABORT: entity logical name '$($verify.LogicalName)' does not carry the '$ExpectedPrefix' prefix."
}

Write-Host "  LogicalName        : $($verify.LogicalName)" -ForegroundColor Green
Write-Host "  SchemaName         : $($verify.SchemaName)" -ForegroundColor Green
Write-Host "  EntitySetName      : $($verify.EntitySetName)" -ForegroundColor Green
Write-Host "  OwnershipType      : $($verify.OwnershipType)" -ForegroundColor Green
Write-Host "  IsAuditEnabled     : $($verify.IsAuditEnabled.Value)" -ForegroundColor Green
Write-Host "  PrimaryNameAttr    : $($verify.PrimaryNameAttribute)" -ForegroundColor Green

$custom = $verify.Attributes | Where-Object { $_.LogicalName -like 'sprk_*' } | Sort-Object LogicalName
Write-Host "  Custom attributes ($($custom.Count)):" -ForegroundColor Green
foreach ($a in $custom) {
    Write-Host ("    - {0,-26} {1,-12} required={2}" -f $a.LogicalName, $a.AttributeType, $a.RequiredLevel.Value) -ForegroundColor Gray
}

# Negative assertion: the spec FR-32 MUST — no derived/effective-access column may exist.
$banned = $custom | Where-Object {
    $_.LogicalName -match 'effective|computed|derived|resolvedaccess|effectiverights'
}
if ($banned) {
    throw "ABORT: found column(s) that look like materialized derived access: $($banned.LogicalName -join ', '). Spec FR-32 forbids storing computed access in this table."
}
Write-Host "  Derived-access column check: PASS (none present)." -ForegroundColor Green

# Sanity: the entity set is queryable.
try {
    $q = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "$($verify.EntitySetName)?`$top=1&`$select=sprk_name" -Method "GET"
    Write-Host "  Web API query OK (returned $($q.value.Count) row(s))." -ForegroundColor Green
}
catch {
    Write-Host "  Web API query failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " DEPLOYMENT COMPLETE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Operator follow-ups (deliberately NOT automated by this script):" -ForegroundColor Yellow
Write-Host "  1. Security roles — apply the CREATE-ONLY posture documented in" -ForegroundColor Gray
Write-Host "     src/solutions/SpaarkeCore/entities/sprk_accessevent/entity-schema.md (Privileges)." -ForegroundColor Gray
Write-Host "     The BFF application user needs Create + Read (Organization depth) and NOTHING else." -ForegroundColor Gray
Write-Host "  2. Views/forms — none are required for task 087; add an admin view when someone" -ForegroundColor Gray
Write-Host "     needs to browse the log in the MDA." -ForegroundColor Gray
Write-Host "  3. Audit prerequisites for replay (task 088) — see the 'Replay coverage' section of" -ForegroundColor Gray
Write-Host "     the schema doc and projects/unified-access-control-r2/notes/task-086-access-event-schema.md." -ForegroundColor Gray
Write-Host ""
