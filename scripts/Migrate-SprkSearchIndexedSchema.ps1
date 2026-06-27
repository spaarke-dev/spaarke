<#
.SYNOPSIS
    Deploys the H3 search-index dual-field migration to sprk_document
    (R3 Part 3 / Workstream H3 — FR-3H3.2).

.DESCRIPTION
    Adds two new DateTime (UTC) attributes to the `sprk_document` entity:

      - sprk_searchindexqueuedon    : datetime, set when an indexing job ENQUEUES
      - sprk_searchindexcompletedon : datetime, set when AI Search confirms COMPLETION

    Preserves the legacy `sprk_searchindexed` (Two options / Yes-No) and
    `sprk_searchindexedon` (DateTime) columns for DUAL-WRITE during the
    transition window (R3 + one sprint per spec assumption, line 366).
    Removal of `sprk_searchindexed` is OUT OF SCOPE for R3 and will be a
    future task once consumer migration is confirmed in prod
    (per spec FR-3H3.4 and inventory §5A step 3, deferred beyond R3).

    HOST ENTITY: sprk_document
      - Confirmed via inventory (`projects/spaarke-platform-foundations-r3/notes/
        sprk-searchindexed-consumer-inventory.md`).
      - Cross-checked: `DataverseWebApiService._entitySetName = "sprk_documents"`
        and `Models.cs` defines `DocumentEntity` with the `SearchIndexed` property
        mapping to `sprk_searchindexed` (lines 175-177, 286).

    The script is IDEMPOTENT — safe to re-run. For each new attribute:
      - If absent → POST CreateAttribute (logs "Added")
      - If present → skip (logs "already exists")
    The legacy `sprk_searchindexed` is NEVER modified by this script.

    SCHEMA SHAPE PARITY:
      Both new fields mirror the existing `sprk_searchindexedon` shape from the
      financial-related-entities data-model doc (Format=DateAndTime,
      DateTimeBehavior=UserLocal). Audit settings + searchability are left at
      Dataverse defaults — the legacy field's exact audit setting is not
      asserted from metadata here; downstream task 062 verifies parity via
      the DataverseEntitySchemaTests integration test.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (default: spaarkedev1, via DATAVERSE_URL env var or hardcoded).

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Migrate-SprkSearchIndexedSchema.ps1 -DryRun

.EXAMPLE
    # Deploy to spaarkedev1 (idempotent — safe to re-run)
    .\Migrate-SprkSearchIndexedSchema.ps1

.EXAMPLE
    # Deploy to a different environment
    .\Migrate-SprkSearchIndexedSchema.ps1 -EnvironmentUrl "https://spaarketest.crm.dynamics.com"

.NOTES
    Project: spaarke-platform-foundations-r3 (Part 3 / Workstream H3)
    Task:    R3-061 — `sprk_searchindexed` schema migration (dual-field)
    Spec FR: FR-3H3.2 (schema migration), AC-H3.2 (acceptance gate)
    Depends: 060 (consumer inventory — confirms host entity = sprk_document)
    Blocks:  062 (DeliverToIndexNodeExecutor / mapping-layer dual-write — depends on
             these attributes being deployed); 063/064 (consumer migration — verify-empty
             per inventory §3A/§3B); future R4 task (drop sprk_searchindexed).
    Created: 2026-06-21
    Pattern source: scripts/Create-BackgroundJobEntity.ps1 (R3-015 — idempotent canonical)
    ADR Compliance:
      - ADR-027 (unmanaged solution; sprk_ prefix)
      - ADR-029 (BFF size N/A — 0 MB delta; no BFF code change in this task)
      - ADR-002 (late-bound; no early-bound code generation needed downstream)
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
# Constants
# -----------------------------------------------------------------------------

$HostEntity        = "sprk_document"
$LegacyBoolField   = "sprk_searchindexed"
$LegacyDateField   = "sprk_searchindexedon"
$NewFieldQueued    = "sprk_searchindexqueuedon"
$NewFieldCompleted = "sprk_searchindexcompletedon"

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

# -----------------------------------------------------------------------------
# Attribute Creation
# -----------------------------------------------------------------------------

function New-SearchIndexDateTimeAttribute {
    param(
        [string]$SchemaName,
        [string]$DisplayName,
        [string]$Description
    )

    return @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = $SchemaName
        "RequiredLevel"    = @{ "Value" = "None" }
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        "DisplayName"      = New-Label -Text $DisplayName
        "Description"      = New-Label -Text $Description
    }
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

function Publish-DocumentCustomizations {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>$HostEntity</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but attributes should be available shortly" -ForegroundColor Yellow
    }
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " Migrate sprk_searchindexed Schema — Dual-Field (R3-061)" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment:  $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Host entity:  $HostEntity" -ForegroundColor Yellow
    Write-Host "New fields:   $NewFieldQueued, $NewFieldCompleted (datetime)" -ForegroundColor Yellow
    Write-Host "Preserved:    $LegacyBoolField (legacy bool — dual-write transition)" -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "MODE:         DRY RUN (no Dataverse modifications)" -ForegroundColor Yellow
    }
    Write-Host ""

    # ---- Step 0: Get auth token ----
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # ---- Step 1: Verify host entity exists ----
    Write-Host "Step 1: Verifying host entity ($HostEntity)..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName $HostEntity

    if (-not $entityExists) {
        throw "Host entity '$HostEntity' does not exist in $EnvironmentUrl. Cannot proceed with attribute migration."
    }
    Write-Host "  $HostEntity exists" -ForegroundColor Green
    Write-Host ""

    # ---- Step 2: Verify legacy fields still intact (dual-write transition guarantee) ----
    Write-Host "Step 2: Verifying legacy fields are intact (dual-write requirement)..." -ForegroundColor Cyan

    $legacyBoolExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName $HostEntity -AttributeLogicalName $LegacyBoolField
    if (-not $legacyBoolExists) {
        Write-Host "  WARNING: $LegacyBoolField NOT found on $HostEntity — dual-write contract cannot be honoured." -ForegroundColor Red
        Write-Host "  This is unexpected per inventory (task 060). Aborting to prevent silent state loss." -ForegroundColor Red
        throw "Legacy field $LegacyBoolField is missing; refusing to add new fields without dual-write target."
    }
    Write-Host "  $LegacyBoolField : PRESENT (will be preserved)" -ForegroundColor Green

    $legacyDateExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName $HostEntity -AttributeLogicalName $LegacyDateField
    if ($legacyDateExists) {
        Write-Host "  $LegacyDateField : PRESENT (will be preserved — sibling legacy field)" -ForegroundColor Green
    }
    else {
        Write-Host "  $LegacyDateField : NOT FOUND (informational — sibling, not required by this task)" -ForegroundColor Gray
    }
    Write-Host ""

    # ---- Step 3: Add new attributes (idempotent) ----
    Write-Host "Step 3: Adding/verifying new datetime attributes..." -ForegroundColor Cyan

    $newAttributes = @(
        @{
            SchemaName  = $NewFieldQueued
            DisplayName = "Search Index Queued On"
            Description = "Datetime (UTC, UserLocal) set when an AI Search indexing job is ENQUEUED for this document (e.g., when RagEndpoints.SendToIndex publishes the Service Bus message, before the handler runs). Pairs with sprk_searchindexcompletedon to give true 'enqueued vs done' visibility. Introduced R3 FR-3H3.2 to replace the misleading sprk_searchindexed bool which was set after completion despite its name suggesting enqueue."
        },
        @{
            SchemaName  = $NewFieldCompleted
            DisplayName = "Search Index Completed On"
            Description = "Datetime (UTC, UserLocal) set when the AI Search indexer CONFIRMS successful indexing of this document (e.g., when RagIndexingJobHandler completes the AI Search write). Pairs with sprk_searchindexqueuedon. Introduced R3 FR-3H3.2 alongside the queued-on field."
        }
    )

    $createdAttrs = @()
    $skippedAttrs = @()

    foreach ($attrDef in $newAttributes) {
        $name = $attrDef.SchemaName
        $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
            -EntityLogicalName $HostEntity -AttributeLogicalName $name

        if ($attrExists) {
            Write-Host "  $name already exists, skipping" -ForegroundColor Gray
            $skippedAttrs += $name
        }
        else {
            if ($DryRun) {
                Write-Host "  [DRY RUN] Would add attribute: $name (DateTime / UserLocal)" -ForegroundColor Yellow
            }
            else {
                $payload = New-SearchIndexDateTimeAttribute `
                    -SchemaName $attrDef.SchemaName `
                    -DisplayName $attrDef.DisplayName `
                    -Description $attrDef.Description
                Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                    -EntityLogicalName $HostEntity -AttributeDef $payload
                $createdAttrs += $name
            }
        }
    }
    Write-Host ""

    # ---- Step 4: Publish customizations ----
    Write-Host "Step 4: Publishing customizations..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would publish $HostEntity customizations" -ForegroundColor Yellow
    }
    else {
        Publish-DocumentCustomizations -Token $token -BaseUrl $EnvironmentUrl
    }
    Write-Host ""

    # ---- Step 5: Verify both new attributes + legacy preservation ----
    Write-Host "Step 5: Final verification..." -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "  [DRY RUN] Skipping verification" -ForegroundColor Yellow
    }
    else {
        $allPresent = $true
        foreach ($name in @($NewFieldQueued, $NewFieldCompleted)) {
            if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                    -EntityLogicalName $HostEntity -AttributeLogicalName $name) {
                Write-Host "  $name : PRESENT" -ForegroundColor Green
            }
            else {
                Write-Host "  $name : MISSING" -ForegroundColor Red
                $allPresent = $false
            }
        }

        # Re-verify legacy still intact
        if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName $HostEntity -AttributeLogicalName $LegacyBoolField) {
            Write-Host "  $LegacyBoolField : STILL PRESENT (dual-write OK)" -ForegroundColor Green
        }
        else {
            Write-Host "  $LegacyBoolField : MISSING AFTER MIGRATION (UNEXPECTED!)" -ForegroundColor Red
            $allPresent = $false
        }

        if (-not $allPresent) {
            throw "Post-migration verification failed. See errors above."
        }
    }

    # ---- Summary ----
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " Migration Complete" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Summary:" -ForegroundColor Yellow
    Write-Host "  Created: $(if ($createdAttrs.Count -gt 0) { $createdAttrs -join ', ' } else { '(none)' })" -ForegroundColor Gray
    Write-Host "  Skipped: $(if ($skippedAttrs.Count -gt 0) { $skippedAttrs -join ', ' } else { '(none)' })" -ForegroundColor Gray
    Write-Host "  Preserved (dual-write): $LegacyBoolField" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify in Power Apps maker portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Task 062: wire dual-write in Spaarke.Dataverse mapping layer" -ForegroundColor Gray
    Write-Host "     (DataverseWebApiService + DataverseServiceClientImpl, per inventory §2A)" -ForegroundColor Gray
    Write-Host "  3. Tasks 063/064: verify-empty consumer migration (per inventory §3A/§3B)" -ForegroundColor Gray
    Write-Host "  4. Future R4 task: drop $LegacyBoolField after consumer migration confirmed in prod" -ForegroundColor Gray
    Write-Host ""
}

Main
