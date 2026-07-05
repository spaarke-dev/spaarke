# Deploy-AiCatalogSchemaExtensions.ps1
# ============================================================================
# Project: spaarke-ai-architecture-redesign-r1 — Task 003 (FR-P0-03 schema half)
# Purpose: Extend the three AI catalog tables with the closed-catalog contract
#          columns (ADR-039, canonical doc SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md v0.4 §6):
#            - sprk_analysisaction   +4  (sprk_kind, sprk_workflowclass, sprk_inputschema, sprk_modeltier)
#            - sprk_playbookconsumer +9  (sprk_ucid, sprk_tooldescription, sprk_disposition,
#                                         sprk_chiptransitions, sprk_risk, sprk_capturemode,
#                                         sprk_oneventbindings, sprk_surfaces, sprk_modeltieroverride)
#            - sprk_analysistool     +5  (sprk_namespace, sprk_outputschema, sprk_sideeffectclass,
#                                         sprk_permissionscope, sprk_budgetclass)
#                                         (sprk_toolid already exists — NVARCHAR(100))
# Pattern:  .claude/skills/dataverse-create-schema/SKILL.md (Web API; idempotent; additive-only)
# Usage:    pwsh ./scripts/Deploy-AiCatalogSchemaExtensions.ps1
# ============================================================================

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"
$BaseUrl = "https://$Environment/api/data/v9.2"

Write-Host "=== AI Catalog Schema Extensions (FR-P0-03) ===" -ForegroundColor Cyan
Write-Host "Target: $Environment"

# --- Auth -------------------------------------------------------------------
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
if (-not $token) { throw "Failed to obtain access token. Run 'az login' first." }

# --- Helpers ----------------------------------------------------------------
function Invoke-DataverseApi {
    param([string]$Endpoint, [string]$Method = "GET", [object]$Body = $null)
    $headers = @{
        "Authorization"    = "Bearer $token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Content-Type"     = "application/json"
        "Accept"           = "application/json"
    }
    $params = @{ Uri = "$BaseUrl/$Endpoint"; Headers = $headers; Method = $Method }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    return Invoke-RestMethod @params
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

function Test-AttributeExists {
    param([string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" | Out-Null
        return $true
    } catch { return $false }
}

function Test-GlobalOptionSetExists {
    param([string]$Name)
    try {
        Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions(Name='$Name')" | Out-Null
        return $true
    } catch { return $false }
}

function Add-EntityAttribute {
    param([string]$EntityLogicalName, [hashtable]$AttributeDef)
    $name = $AttributeDef.SchemaName
    if (Test-AttributeExists -EntityLogicalName $EntityLogicalName -AttributeLogicalName $name.ToLower()) {
        Write-Host "    SKIP (exists): $EntityLogicalName.$name" -ForegroundColor Yellow
        return
    }
    Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef | Out-Null
    Write-Host "    CREATED: $EntityLogicalName.$name" -ForegroundColor Green
}

# --- Attribute factories ----------------------------------------------------
function New-StringAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [int]$MaxLength = 200)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = $MaxLength
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
    }
}

function New-MemoAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [int]$MaxLength = 100000)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = $MaxLength
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
    }
}

function New-LocalPicklistAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [hashtable[]]$Options)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "IsGlobal"      = $false
            "OptionSetType" = "Picklist"
            "Options"       = @($Options | ForEach-Object {
                @{ "Value" = $_.Value; "Label" = New-Label -Text $_.Label }
            })
        }
    }
}

function New-GlobalPicklistAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [string]$GlobalOptionSetName)
    # @odata.bind requires the MetadataId GUID key — Name-key binding is rejected
    # ("Guid should contain 32 digits with 4 dashes").
    $os = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions(Name='$GlobalOptionSetName')?`$select=MetadataId"
    return @{
        "@odata.type"                 = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                  = $SchemaName
        "RequiredLevel"               = @{ "Value" = "None" }
        "DisplayName"                 = New-Label -Text $DisplayName
        "Description"                 = New-Label -Text $Description
        "GlobalOptionSet@odata.bind"  = "/GlobalOptionSetDefinitions($($os.MetadataId))"
    }
}

# ============================================================================
# Phase 1: Global option set — sprk_aimodeltier (shared by Action default +
#          Binding override so the two columns can never drift)
#          Tier vocabulary grounded in ModelSelector.cs doc comment:
#          fast/cheap (gpt-4o-mini) | capable (gpt-4o) | reasoning (o1-mini)
# ============================================================================
Write-Host "`nPhase 1: Global option sets" -ForegroundColor Cyan
if (Test-GlobalOptionSetExists -Name "sprk_aimodeltier") {
    Write-Host "    SKIP (exists): sprk_aimodeltier" -ForegroundColor Yellow
} else {
    Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = "sprk_aimodeltier"
        "DisplayName"   = New-Label -Text "AI Model Tier"
        "Description"   = New-Label -Text "Model tier for AI execution: Fast (cheap, e.g. gpt-4o-mini), Standard (capable, e.g. gpt-4o), Reasoning (e.g. o-series). Action sets the default; Binding may override."
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = @(
            @{ "Value" = 100000000; "Label" = New-Label -Text "Fast" },
            @{ "Value" = 100000001; "Label" = New-Label -Text "Standard" },
            @{ "Value" = 100000002; "Label" = New-Label -Text "Reasoning" }
        )
    } | Out-Null
    Write-Host "    CREATED: global option set sprk_aimodeltier" -ForegroundColor Green
}

# ============================================================================
# Phase 3: Extend existing entities (additive-only)
# ============================================================================

# --- sprk_analysisaction (Action — the execution unit) ----------------------
Write-Host "`nPhase 3a: sprk_analysisaction (+4)" -ForegroundColor Cyan

Add-EntityAttribute -EntityLogicalName "sprk_analysisaction" -AttributeDef (
    New-LocalPicklistAttribute -SchemaName "sprk_kind" -DisplayName "Kind" `
        -Description "Execution kind of this Action: Prompted (default; JPS prompt via ActionRunner) or Coded (registered ICodedWorkflow)." `
        -Options @(
            @{ Value = 100000000; Label = "Prompted" },
            @{ Value = 100000001; Label = "Coded" }
        ))

Add-EntityAttribute -EntityLogicalName "sprk_analysisaction" -AttributeDef (
    New-StringAttribute -SchemaName "sprk_workflowclass" -DisplayName "Workflow Class" `
        -Description "For kind=Coded: registered ICodedWorkflow class reference resolved by assembly-scan discovery (canonical doc E-1)." `
        -MaxLength 200)

Add-EntityAttribute -EntityLogicalName "sprk_analysisaction" -AttributeDef (
    New-MemoAttribute -SchemaName "sprk_inputschema" -DisplayName "Input Schema" `
        -Description "JSON typed-argument schema: per arg name, type, required, ledger_resolution, elicitation prompt (canonical doc 6.1)." `
        -MaxLength 100000)

Add-EntityAttribute -EntityLogicalName "sprk_analysisaction" -AttributeDef (
    New-GlobalPicklistAttribute -SchemaName "sprk_modeltier" -DisplayName "Model Tier" `
        -Description "Default model tier for this Action; overridable per Binding via sprk_modeltieroverride." `
        -GlobalOptionSetName "sprk_aimodeltier")

# --- sprk_playbookconsumer (Binding — the invocation unit) -------------------
Write-Host "`nPhase 3b: sprk_playbookconsumer (+9)" -ForegroundColor Cyan

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-StringAttribute -SchemaName "sprk_ucid" -DisplayName "UC ID" `
        -Description "Use-case vocabulary ID from canonical doc section 3 (e.g. UC-A-1) tying this Binding to the platform use-case taxonomy." `
        -MaxLength 100)

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-MemoAttribute -SchemaName "sprk_tooldescription" -DisplayName "Tool Description" `
        -Description "Maker-editable intent surface the agent loop sees when this Binding is projected as a capability tool." `
        -MaxLength 100000)

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-LocalPicklistAttribute -SchemaName "sprk_disposition" -DisplayName "Disposition" `
        -Description "Output routing disposition consumed by the Output Router." `
        -Options @(
            @{ Value = 100000000; Label = "Informational" },
            @{ Value = 100000001; Label = "Work Product" },
            @{ Value = 100000002; Label = "Overlay" },
            @{ Value = 100000003; Label = "Email" },
            @{ Value = 100000004; Label = "Record" },
            @{ Value = 100000005; Label = "Notification" }
        ))

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-MemoAttribute -SchemaName "sprk_chiptransitions" -DisplayName "Chip Transitions" `
        -Description "JSON array of next-step chips: [{target_binding_id, chip_label}] (canonical doc D4)." `
        -MaxLength 100000)

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-LocalPicklistAttribute -SchemaName "sprk_risk" -DisplayName "Risk" `
        -Description "Confirmation-gate risk posture for this Binding." `
        -Options @(
            @{ Value = 100000000; Label = "None" },
            @{ Value = 100000001; Label = "Confirm When Uncertain" },
            @{ Value = 100000002; Label = "Always Confirm" }
        ))

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-LocalPicklistAttribute -SchemaName "sprk_capturemode" -DisplayName "Capture Mode" `
        -Description "How missing required args are captured: Loop Elicitation (default; clarifying chat turns) or Modal (form modal escape hatch)." `
        -Options @(
            @{ Value = 100000000; Label = "Loop Elicitation" },
            @{ Value = 100000001; Label = "Modal" }
        ))

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-MemoAttribute -SchemaName "sprk_oneventbindings" -DisplayName "On-Event Bindings" `
        -Description "JSON array declaring Event-path membership: [{event, order}] (e.g. document_uploaded)." `
        -MaxLength 100000)

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-StringAttribute -SchemaName "sprk_surfaces" -DisplayName "Surfaces" `
        -Description "Comma-separated surface tokens where this Binding is offered: assistant, record-form, wizard, office, external-spa, scheduler, inbound-email (canonical doc 4.1)." `
        -MaxLength 400)

Add-EntityAttribute -EntityLogicalName "sprk_playbookconsumer" -AttributeDef (
    New-GlobalPicklistAttribute -SchemaName "sprk_modeltieroverride" -DisplayName "Model Tier Override" `
        -Description "Per-Binding override of the Action's default sprk_modeltier; empty = use Action default." `
        -GlobalOptionSetName "sprk_aimodeltier")

# --- sprk_analysistool (Tool manifest) ---------------------------------------
Write-Host "`nPhase 3c: sprk_analysistool (+5; sprk_toolid already exists)" -ForegroundColor Cyan

Add-EntityAttribute -EntityLogicalName "sprk_analysistool" -AttributeDef (
    New-StringAttribute -SchemaName "sprk_namespace" -DisplayName "Namespace" `
        -Description "Tool namespace segment (e.g. dataverse, document); sprk_toolid carries the full namespaced id (e.g. dataverse.read_query)." `
        -MaxLength 100)

Add-EntityAttribute -EntityLogicalName "sprk_analysistool" -AttributeDef (
    New-MemoAttribute -SchemaName "sprk_outputschema" -DisplayName "Output Schema" `
        -Description "JSON schema of the tool's output contract, used for grounding/citation enforcement." `
        -MaxLength 100000)

Add-EntityAttribute -EntityLogicalName "sprk_analysistool" -AttributeDef (
    New-LocalPicklistAttribute -SchemaName "sprk_sideeffectclass" -DisplayName "Side Effect Class" `
        -Description "Declared side-effect class driving the ONE confirmation gate: Read, Write, Communicate, Pure." `
        -Options @(
            @{ Value = 100000000; Label = "Read" },
            @{ Value = 100000001; Label = "Write" },
            @{ Value = 100000002; Label = "Communicate" },
            @{ Value = 100000003; Label = "Pure" }
        ))

Add-EntityAttribute -EntityLogicalName "sprk_analysistool" -AttributeDef (
    New-StringAttribute -SchemaName "sprk_permissionscope" -DisplayName "Permission Scope" `
        -Description "Permission scope required to project this tool into a loop turn (user-OBO enforced; e.g. dataverse-user-context)." `
        -MaxLength 200)

Add-EntityAttribute -EntityLogicalName "sprk_analysistool" -AttributeDef (
    New-StringAttribute -SchemaName "sprk_budgetclass" -DisplayName "Budget Class" `
        -Description "Named budget profile applied per ADR-016 (max tokens/docs/duration are configured in code against this class name)." `
        -MaxLength 100)

# ============================================================================
# Phase 6: Publish customizations
# ============================================================================
Write-Host "`nPhase 6: Publish customizations" -ForegroundColor Cyan
$entityXml = @("sprk_analysisaction", "sprk_playbookconsumer", "sprk_analysistool") |
    ForEach-Object { "<entity>$_</entity>" }
Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body @{
    "ParameterXml" = "<importexportxml><entities>$($entityXml -join '')</entities><optionsets><optionset>sprk_aimodeltier</optionset></optionsets></importexportxml>"
} | Out-Null
Write-Host "    Customizations published" -ForegroundColor Green

# ============================================================================
# Verification
# ============================================================================
Write-Host "`nVerification:" -ForegroundColor Cyan
$expected = @{
    "sprk_analysisaction"  = @("sprk_kind", "sprk_workflowclass", "sprk_inputschema", "sprk_modeltier")
    "sprk_playbookconsumer" = @("sprk_ucid", "sprk_tooldescription", "sprk_disposition", "sprk_chiptransitions",
                                "sprk_risk", "sprk_capturemode", "sprk_oneventbindings", "sprk_surfaces", "sprk_modeltieroverride")
    "sprk_analysistool"    = @("sprk_toolid", "sprk_namespace", "sprk_outputschema", "sprk_sideeffectclass",
                               "sprk_permissionscope", "sprk_budgetclass")
}
$fail = $false
foreach ($entity in $expected.Keys) {
    $attrs = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$entity')?`$expand=Attributes(`$select=LogicalName,AttributeType)"
    $logical = $attrs.Attributes | ForEach-Object { $_.LogicalName }
    foreach ($col in $expected[$entity]) {
        $found = $attrs.Attributes | Where-Object { $_.LogicalName -eq $col }
        if ($found) {
            Write-Host ("    OK  {0}.{1}  ({2})" -f $entity, $col, $found.AttributeType) -ForegroundColor Green
        } else {
            Write-Host ("    MISSING  {0}.{1}" -f $entity, $col) -ForegroundColor Red
            $fail = $true
        }
    }
}
if ($fail) { throw "Verification failed — one or more columns missing." }
Write-Host "`n=== FR-P0-03 schema deployment complete ===" -ForegroundColor Cyan
