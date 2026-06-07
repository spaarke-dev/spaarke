<#
.SYNOPSIS
    Deploys the sprk_aipersona Dataverse entity (R6 Pillar 1 — 5th scope library entity).

.DESCRIPTION
    Creates the sprk_aipersona entity (a Dataverse-driven persona scope for the chat-agent)
    mirroring the canonical 4-scope schema pattern (sprk_analysisaction / sprk_analysisskill /
    sprk_analysisknowledge / sprk_analysistool). Persona makes chat-agent persona data-driven
    instead of hardcoded in SprkChatAgentFactory.BuildDefaultSystemPrompt().

    Schema fields (all mirror the existing scope-entity convention):
      - sprk_name             : NVARCHAR(200) NOT NULL, primary name (SYS-/CUST- prefix
                                enforced API-side via OwnershipValidator per scope-architecture.md)
      - sprk_personacode      : NVARCHAR(10), short code (mirrors sprk_skillcode/sprk_toolcode)
      - sprk_description      : MULTILINE TEXT, persona description
      - sprk_systemprompt     : MULTILINE TEXT, system prompt body (8KB persona budget per NFR-10)
      - sprk_scopetype        : Local picklist (Global=100000000 / Tenant=100000001 /
                                PlaybookAttached=100000002) — drives most-specific-wins inheritance
                                resolution per R6 Q1
      - sprk_tags             : NVARCHAR(1000), categorization tags
      - sprk_availableadhoc   : BIT, mirrors all 4 existing scopes
      - sprk_parentpersonaid  : LOOKUP -> sprk_aipersona (self-relationship) for inheritance chain
                                per R6 Q1 (most-specific-wins)
      - Standard audit fields (createdon/createdby/modifiedon/modifiedby/ownerid/statecode/
        statuscode/versionnumber) are auto-added by Dataverse — no explicit declaration.

    The script is idempotent: safe to re-run. It checks for existing entity/attributes/lookup
    before creating, and reports skip vs create for each step.

    Prefix enforcement: SYS-/CUST- prefix is enforced API-side via the existing OwnershipValidator
    (BFF Services/Scopes/OwnershipValidator.cs), NOT at the Dataverse layer. This matches the
    canonical pattern across all 4 existing scope entities — none have a Dataverse-side plugin
    or business rule enforcing the prefix. Enforcement lives in the scope CRUD service layer
    (ScopeManagementService.cs). Task 002 (GET /api/ai/scopes/personas endpoint) will reuse
    OwnershipValidator for persona CRUD.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Create-AiPersonaEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Create-AiPersonaEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-ai-platform-unification-r6 (Pillar 1)
    Task: 001 (D-A-01) — Create sprk_aipersona Dataverse Entity
    Blocks: 002 (persona endpoint), 003 (resolver), 004 (SYS- seed), 005 (factory wiring)
    Created: 2026-06-07
    ADR Compliance: ADR-027 (unmanaged solution; sprk_ prefix), ADR-029 (BFF size N/A — 0 MB
                    delta; no BFF code change), ADR-018 (no new feature flags)
    Pattern Source: scripts/Deploy-ChartDefinitionEntity.ps1 (canonical exemplar) +
                    docs/architecture/scope-architecture.md (scope schema canonical reference)
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

function Test-RelationshipExists {
    param([string]$Token, [string]$BaseUrl, [string]$SchemaName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "RelationshipDefinitions(SchemaName='$SchemaName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

# -----------------------------------------------------------------------------
# Entity Creation
# -----------------------------------------------------------------------------

function New-AiPersonaEntity {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Creating sprk_aipersona entity..." -ForegroundColor Yellow

    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_aipersona"
        "DisplayName"           = New-Label -Text "AI Persona"
        "DisplayCollectionName" = New-Label -Text "AI Personas"
        "Description"           = New-Label -Text "Defines a persona (system prompt + voice + tone) for the AI chat-agent. Resolved via most-specific-wins inheritance: global SYS- < tenant CUST- < playbook-attached. R6 Pillar 1 — 5th scope library entity."
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_name"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 200
                "DisplayName"   = New-Label -Text "Name"
                "Description"   = New-Label -Text "Persona name. SYS-/CUST- prefix enforced API-side via OwnershipValidator (system personas immutable; customer personas editable)."
                "IsPrimaryName" = $true
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

function Add-SelfLookupRelationship {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Adding self-lookup relationship: sprk_aipersona_parentpersona..." -ForegroundColor Gray

    $relationshipDef = @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = "sprk_aipersona_parentpersona"
        "ReferencedEntity"     = "sprk_aipersona"
        "ReferencingEntity"    = "sprk_aipersona"
        "CascadeConfiguration" = @{
            "Assign"   = "NoCascade"
            "Delete"   = "RemoveLink"
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup"               = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"    = "sprk_parentpersonaid"
            "DisplayName"   = New-Label -Text "Parent Persona"
            "Description"   = New-Label -Text "Self-relationship for inheritance chain (most-specific-wins per R6 Q1). When set, this persona inherits fields from the parent; overridden fields use child values."
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relationshipDef

    Write-Host "    Created: sprk_parentpersonaid (self-lookup)" -ForegroundColor Green
}

function Publish-PersonaCustomizations {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_aipersona</entity></entities></importexportxml>"
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
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_aipersona Entity (R6 D-A-01)" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "MODE: DRY RUN (no Dataverse modifications)" -ForegroundColor Yellow
    }
    Write-Host ""

    # ---- Step 0: Get auth token ----
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # ---- Step 1: Check if entity exists ----
    Write-Host "Step 1: Checking if entity exists..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_aipersona"

    if ($entityExists) {
        Write-Host "  sprk_aipersona already exists — will verify/add missing fields only" -ForegroundColor Yellow
    }
    else {
        Write-Host "  sprk_aipersona does NOT exist — will create" -ForegroundColor Gray
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would create entity sprk_aipersona with primary attribute sprk_name" -ForegroundColor Yellow
        }
        else {
            New-AiPersonaEntity -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 2: Add attributes (idempotent) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying attributes..." -ForegroundColor Cyan

    # Define all non-primary attributes
    $attributes = @(
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_personacode"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 10
            "DisplayName"   = New-Label -Text "Persona Code"
            "Description"   = New-Label -Text "Short code for the persona (mirrors sprk_skillcode/sprk_toolcode pattern). Used for human-readable references."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_description"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 2000
            "DisplayName"   = New-Label -Text "Description"
            "Description"   = New-Label -Text "Human-readable description of the persona's purpose, intended use cases, and voice/tone characteristics."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_systemprompt"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100000
            "DisplayName"   = New-Label -Text "System Prompt"
            "Description"   = New-Label -Text "The system prompt body for the chat-agent persona. Subject to ~8KB persona budget per NFR-10. Resolved at chat-agent build time via IScopeResolverService."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_scopetype"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "DisplayName"   = New-Label -Text "Scope Type"
            "Description"   = New-Label -Text "Persona scope level for most-specific-wins inheritance per R6 Q1: Global (system-wide default) / Tenant (customer-specific override) / PlaybookAttached (playbook-specific persona)."
            "OptionSet"     = @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                "OptionSetType" = "Picklist"
                "IsGlobal"      = $false
                "Options"       = @(
                    @{
                        "Value" = 100000000
                        "Label" = New-Label -Text "Global"
                    },
                    @{
                        "Value" = 100000001
                        "Label" = New-Label -Text "Tenant"
                    },
                    @{
                        "Value" = 100000002
                        "Label" = New-Label -Text "PlaybookAttached"
                    }
                )
            }
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_tags"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 1000
            "DisplayName"   = New-Label -Text "Tags"
            "Description"   = New-Label -Text "Comma-separated tags for categorization, search, and filtering. Mirrors tag fields on existing 4 scope entities."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
            "SchemaName"    = "sprk_availableadhoc"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = New-Label -Text "Available Ad Hoc"
            "Description"   = New-Label -Text "When true, persona may be invoked outside of playbook context (e.g., direct chat-agent attachment). Mirrors sprk_availableadhoc on all 4 existing scopes."
            "DefaultValue"  = $false
            "OptionSet"     = @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
                "OptionSetType" = "Boolean"
                "TrueOption"    = @{
                    "Value" = 1
                    "Label" = New-Label -Text "Yes"
                }
                "FalseOption"   = @{
                    "Value" = 0
                    "Label" = New-Label -Text "No"
                }
            }
        }
    )

    foreach ($attr in $attributes) {
        $name = $attr.SchemaName
        if (-not $entityExists) {
            # Entity is newly created; need to add this attribute
            if ($DryRun) {
                Write-Host "  [DRY RUN] Would add attribute: $name" -ForegroundColor Yellow
            }
            else {
                Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                    -EntityLogicalName "sprk_aipersona" -AttributeDef $attr
            }
        }
        else {
            $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName "sprk_aipersona" -AttributeLogicalName $name
            if ($attrExists) {
                Write-Host "  $name already exists, skipping" -ForegroundColor Gray
            }
            else {
                if ($DryRun) {
                    Write-Host "  [DRY RUN] Would add attribute: $name" -ForegroundColor Yellow
                }
                else {
                    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                        -EntityLogicalName "sprk_aipersona" -AttributeDef $attr
                }
            }
        }
    }

    # ---- Step 3: Add self-lookup relationship (idempotent) ----
    Write-Host ""
    Write-Host "Step 3: Adding/verifying self-lookup relationship..." -ForegroundColor Cyan

    $relExists = Test-RelationshipExists -Token $token -BaseUrl $EnvironmentUrl `
        -SchemaName "sprk_aipersona_parentpersona"
    if ($relExists) {
        Write-Host "  sprk_aipersona_parentpersona already exists, skipping" -ForegroundColor Gray
    }
    else {
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would create self-lookup relationship sprk_aipersona_parentpersona" -ForegroundColor Yellow
            Write-Host "  [DRY RUN]   → adds sprk_parentpersonaid lookup attribute (self → sprk_aipersona)" -ForegroundColor Yellow
        }
        else {
            Add-SelfLookupRelationship -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 4: Publish customizations ----
    Write-Host ""
    Write-Host "Step 4: Publishing customizations..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would publish sprk_aipersona customizations" -ForegroundColor Yellow
    }
    else {
        Publish-PersonaCustomizations -Token $token -BaseUrl $EnvironmentUrl
    }

    # ---- Step 5: Verify entity ----
    Write-Host ""
    Write-Host "Step 5: Verifying entity..." -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "  [DRY RUN] Skipping verification" -ForegroundColor Yellow
    }
    else {
        if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_aipersona") {
            Write-Host "  sprk_aipersona exists and is accessible" -ForegroundColor Green
        }
        else {
            Write-Host "  Warning: Entity verification failed" -ForegroundColor Yellow
        }

        # Smoke-test the Web API collection endpoint
        Write-Host ""
        Write-Host "Step 6: Smoke-testing Web API endpoint..." -ForegroundColor Cyan
        try {
            $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                -Endpoint "sprk_aipersonas?`$top=1" -Method "GET"
            Write-Host "  Web API query successful — collection endpoint reachable" -ForegroundColor Green
            Write-Host "  Current record count: $($result.value.Count)" -ForegroundColor Gray
        }
        catch {
            Write-Host "  Warning: Web API query failed (entity may still be publishing)" -ForegroundColor Yellow
            Write-Host "  Error: $_" -ForegroundColor Gray
        }
    }

    # ---- Done ----
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Deployment Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify in Power Apps maker portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Task 002: Add GET /api/ai/scopes/personas endpoint" -ForegroundColor Gray
    Write-Host "  3. Task 003: Add persona resolver methods in IScopeResolverService" -ForegroundColor Gray
    Write-Host "  4. Task 004: Seed default SYS- persona row with current BuildDefaultSystemPrompt() text" -ForegroundColor Gray
    Write-Host "  5. Task 005: Wire SprkChatAgentFactory.CreateAgentAsync to scope persona" -ForegroundColor Gray
    Write-Host ""
}

Main
