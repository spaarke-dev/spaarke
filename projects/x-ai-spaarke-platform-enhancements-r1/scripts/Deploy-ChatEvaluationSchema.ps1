<#
.SYNOPSIS
    Creates Dataverse schema for SprkChat and Evaluation entities.

.DESCRIPTION
    Deploys 4 new Dataverse entities to spaarkedev1.crm.dynamics.com:
    - sprk_aichatmessage      (chat message persistence)
    - sprk_aichatsummary      (conversation session metadata)
    - sprk_aievaluationrun    (evaluation run records)
    - sprk_aievaluationresult (per-query evaluation results)

    Prerequisites:
    - Azure CLI installed and logged in: az login
    - Account must have System Customizer or System Administrator role
    - PowerShell 7+

    This script is IDEMPOTENT — safe to run multiple times.

.NOTES
    Task:    AIPL-001
    Schema:  projects/ai-spaarke-platform-enhancements-r1/notes/design/dataverse-chat-schema.md
    Created: 2026-02-23
    Guide:   docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md
#>

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Deploy-ChatEvaluationSchema.ps1" -ForegroundColor Cyan
Write-Host "  Target: $Environment" -ForegroundColor Cyan
Write-Host "  Task:   AIPL-001" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Authentication
# ---------------------------------------------------------------------------

Write-Host "Step 0: Obtaining Azure access token..." -ForegroundColor Yellow

$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv 2>&1)
if (-not $token -or $token -like "*ERROR*" -or $token -like "*error*") {
    Write-Error "Failed to get access token. Run 'az login' first and ensure your account has Dataverse access."
    exit 1
}
Write-Host "  Token obtained." -ForegroundColor Green

$BaseUrl = "https://$Environment/api/data/v9.2"

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "Prefer"           = "return=representation"
}

# ---------------------------------------------------------------------------
# Helper Functions
# ---------------------------------------------------------------------------

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels"  = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $uri = "$BaseUrl/$Endpoint"
    $params = @{
        Uri     = $uri
        Headers = $headers
        Method  = $Method
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }

    try {
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response) {
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $errorMessage = $reader.ReadToEnd()
            } catch {}
        }
        return @{ Success = $false; Error = $errorMessage }
    }
}

function Test-GlobalOptionSetExists {
    param([string]$Name)
    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions(Name='$Name')"
    return $result.Success
}

function Test-EntityExists {
    param([string]$LogicalName)
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$LogicalName')"
    return $result.Success
}

function Test-AttributeExists {
    param([string]$EntityLogicalName, [string]$AttributeLogicalName)
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')"
    return $result.Success
}

function Test-RelationshipExists {
    param([string]$SchemaName)
    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions(SchemaName='$SchemaName')"
    return $result.Success
}

# ---------------------------------------------------------------------------
# Phase 1: Global Option Sets
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 1: Creating Global Option Sets..." -ForegroundColor Yellow

# --- sprk_aichatrole ---
if (-not (Test-GlobalOptionSetExists -Name "sprk_aichatrole")) {
    $optionSet = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = "sprk_aichatrole"
        "DisplayName"   = New-Label -Text "AI Chat Role"
        "Description"   = New-Label -Text "The role of the message author in a chat conversation"
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = @(
            @{ "Value" = 726490000; "Label" = New-Label -Text "User" }
            @{ "Value" = 726490001; "Label" = New-Label -Text "Assistant" }
            @{ "Value" = 726490002; "Label" = New-Label -Text "System" }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSet
    if ($result.Success) {
        Write-Host "  [OK] Created option set: sprk_aichatrole" -ForegroundColor Green
    } else {
        Write-Warning "  [WARN] sprk_aichatrole: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aichatrole already exists" -ForegroundColor Yellow
}

# --- sprk_aichatcontextmode ---
if (-not (Test-GlobalOptionSetExists -Name "sprk_aichatcontextmode")) {
    $optionSet = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = "sprk_aichatcontextmode"
        "DisplayName"   = New-Label -Text "AI Chat Context Mode"
        "Description"   = New-Label -Text "The context mode for a SprkChat session"
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = @(
            @{ "Value" = 726490000; "Label" = New-Label -Text "Document" }
            @{ "Value" = 726490001; "Label" = New-Label -Text "Analysis" }
            @{ "Value" = 726490002; "Label" = New-Label -Text "Hybrid" }
            @{ "Value" = 726490003; "Label" = New-Label -Text "Knowledge" }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSet
    if ($result.Success) {
        Write-Host "  [OK] Created option set: sprk_aichatcontextmode" -ForegroundColor Green
    } else {
        Write-Warning "  [WARN] sprk_aichatcontextmode: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aichatcontextmode already exists" -ForegroundColor Yellow
}

# --- sprk_aievaluationrunstatus ---
if (-not (Test-GlobalOptionSetExists -Name "sprk_aievaluationrunstatus")) {
    $optionSet = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = "sprk_aievaluationrunstatus"
        "DisplayName"   = New-Label -Text "AI Evaluation Run Status"
        "Description"   = New-Label -Text "Status of an AI evaluation run"
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = @(
            @{ "Value" = 726490000; "Label" = New-Label -Text "Pending" }
            @{ "Value" = 726490001; "Label" = New-Label -Text "Running" }
            @{ "Value" = 726490002; "Label" = New-Label -Text "Complete" }
            @{ "Value" = 726490003; "Label" = New-Label -Text "Failed" }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSet
    if ($result.Success) {
        Write-Host "  [OK] Created option set: sprk_aievaluationrunstatus" -ForegroundColor Green
    } else {
        Write-Warning "  [WARN] sprk_aievaluationrunstatus: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aievaluationrunstatus already exists" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Phase 2: Create sprk_aichatsummary (parent — must exist before child)
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 2: Creating sprk_aichatsummary entity..." -ForegroundColor Yellow

if (-not (Test-EntityExists -LogicalName "sprk_aichatsummary")) {
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_aichatsummary"
        "DisplayName"           = New-Label -Text "AI Chat Summary"
        "DisplayCollectionName" = New-Label -Text "AI Chat Summaries"
        "Description"           = New-Label -Text "Session-level metadata and rolling summary for SprkChat conversations"
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"       = "sprk_name"
                "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
                "MaxLength"        = 200
                "DisplayName"      = New-Label -Text "Session Name"
                "Description"      = New-Label -Text "Auto-generated session identifier"
                "IsPrimaryName"    = $true
                "AutoNumberFormat" = "SES-{SEQNUM:6}"
            }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    if ($result.Success) {
        Write-Host "  [OK] Created entity: sprk_aichatsummary" -ForegroundColor Green
    } else {
        Write-Error "  [FAIL] sprk_aichatsummary: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aichatsummary already exists" -ForegroundColor Yellow
}

# Add attributes to sprk_aichatsummary
$summaryAttrs = @(
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_sessionid"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Session ID"
        "Description"   = New-Label -Text "Unique session GUID from the BFF. Redis cache key suffix. Indexed."
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_tenantid"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Tenant ID"
        "Description"   = New-Label -Text "Power Platform tenant ID for multi-tenant isolation"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_summary"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 20000
        "DisplayName"   = New-Label -Text "Conversation Summary"
        "Description"   = New-Label -Text "Rolling summary of archived messages. Populated after 15 messages."
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_messagecount"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 999999
        "DisplayName"   = New-Label -Text "Message Count"
        "Description"   = New-Label -Text "Total messages in session. Archive trigger at 50 (NFR-12)."
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_playbookid"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Playbook ID"
        "Description"   = New-Label -Text "sprk_aiplaybook logical ID for the playbook this session uses"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_documentid"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Document ID"
        "Description"   = New-Label -Text "SPE document ID for document-context sessions"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_analysisid"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Analysis ID"
        "Description"   = New-Label -Text "Analysis record ID for analysis-context sessions"
    }
    @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                 = "sprk_contextmode"
        "RequiredLevel"              = @{ "Value" = "None" }
        "DisplayName"                = New-Label -Text "Context Mode"
        "Description"                = New-Label -Text "Current context mode for the chat session"
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions(Name='sprk_aichatcontextmode')"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"    = "sprk_isarchived"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text "Is Archived"
        "Description"   = New-Label -Text "True when session is archived (past retention or manually archived)"
        "OptionSet"     = @{
            "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Archived" }
            "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "Active" }
        }
    }
)

foreach ($attr in $summaryAttrs) {
    if (-not (Test-AttributeExists -EntityLogicalName "sprk_aichatsummary" -AttributeLogicalName $attr.SchemaName.ToLower())) {
        $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_aichatsummary')/Attributes" -Method "POST" -Body $attr
        if ($result.Success) {
            Write-Host "  [OK] Added attribute: sprk_aichatsummary.$($attr.SchemaName)" -ForegroundColor Green
        } else {
            Write-Warning "  [WARN] $($attr.SchemaName): $($result.Error)"
        }
    } else {
        Write-Host "  [SKIP] sprk_aichatsummary.$($attr.SchemaName) already exists" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Phase 3: Create sprk_aichatmessage
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 3: Creating sprk_aichatmessage entity..." -ForegroundColor Yellow

if (-not (Test-EntityExists -LogicalName "sprk_aichatmessage")) {
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_aichatmessage"
        "DisplayName"           = New-Label -Text "AI Chat Message"
        "DisplayCollectionName" = New-Label -Text "AI Chat Messages"
        "Description"           = New-Label -Text "Persistent cold storage for individual SprkChat messages"
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"       = "sprk_name"
                "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
                "MaxLength"        = 200
                "DisplayName"      = New-Label -Text "Message ID"
                "Description"      = New-Label -Text "Auto-generated message identifier"
                "IsPrimaryName"    = $true
                "AutoNumberFormat" = "MSG-{SEQNUM:6}"
            }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    if ($result.Success) {
        Write-Host "  [OK] Created entity: sprk_aichatmessage" -ForegroundColor Green
    } else {
        Write-Error "  [FAIL] sprk_aichatmessage: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aichatmessage already exists" -ForegroundColor Yellow
}

# Add attributes to sprk_aichatmessage
$messageAttrs = @(
    @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                 = "sprk_role"
        "RequiredLevel"              = @{ "Value" = "ApplicationRequired" }
        "DisplayName"                = New-Label -Text "Role"
        "Description"                = New-Label -Text "Who authored this message: User, Assistant, or System"
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions(Name='sprk_aichatrole')"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_content"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 10000
        "DisplayName"   = New-Label -Text "Content"
        "Description"   = New-Label -Text "The message text. For assistant messages, the full streamed response."
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_tokencount"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 100000
        "DisplayName"   = New-Label -Text "Token Count"
        "Description"   = New-Label -Text "Number of tokens in this message (for cost tracking)"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_sessionid"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Session ID"
        "Description"   = New-Label -Text "Foreign key to the chat session. Indexed for query. Matches sprk_aichatsummary.sprk_sessionid."
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_sequencenumber"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 999999
        "DisplayName"   = New-Label -Text "Sequence Number"
        "Description"   = New-Label -Text "Message order within the session. Used to reconstruct ordered history."
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_toolcallsjson"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 5000
        "DisplayName"   = New-Label -Text "Tool Calls JSON"
        "Description"   = New-Label -Text "JSON array of tool calls made during this message turn (assistant messages only)"
    }
)

foreach ($attr in $messageAttrs) {
    if (-not (Test-AttributeExists -EntityLogicalName "sprk_aichatmessage" -AttributeLogicalName $attr.SchemaName.ToLower())) {
        $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_aichatmessage')/Attributes" -Method "POST" -Body $attr
        if ($result.Success) {
            Write-Host "  [OK] Added attribute: sprk_aichatmessage.$($attr.SchemaName)" -ForegroundColor Green
        } else {
            Write-Warning "  [WARN] $($attr.SchemaName): $($result.Error)"
        }
    } else {
        Write-Host "  [SKIP] sprk_aichatmessage.$($attr.SchemaName) already exists" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Phase 4: Create sprk_aievaluationrun (parent — must exist before child)
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 4: Creating sprk_aievaluationrun entity..." -ForegroundColor Yellow

if (-not (Test-EntityExists -LogicalName "sprk_aievaluationrun")) {
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_aievaluationrun"
        "DisplayName"           = New-Label -Text "AI Evaluation Run"
        "DisplayCollectionName" = New-Label -Text "AI Evaluation Runs"
        "Description"           = New-Label -Text "Aggregate record for a complete AI evaluation run with Recall@K and nDCG@K metrics"
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"       = "sprk_name"
                "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
                "MaxLength"        = 200
                "DisplayName"      = New-Label -Text "Evaluation Run ID"
                "Description"      = New-Label -Text "Auto-generated evaluation run identifier"
                "IsPrimaryName"    = $true
                "AutoNumberFormat" = "EVAL-{SEQNUM:6}"
            }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    if ($result.Success) {
        Write-Host "  [OK] Created entity: sprk_aievaluationrun" -ForegroundColor Green
    } else {
        Write-Error "  [FAIL] sprk_aievaluationrun: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aievaluationrun already exists" -ForegroundColor Yellow
}

# Add attributes to sprk_aievaluationrun
$evalRunAttrs = @(
    @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = "sprk_rundate"
        "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
        "DisplayName"      = New-Label -Text "Run Date"
        "Description"      = New-Label -Text "When the evaluation started"
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_environment"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 50
        "DisplayName"   = New-Label -Text "Environment"
        "Description"   = New-Label -Text "Target environment name (e.g., dev, staging, prod)"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_modelversion"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Model Version"
        "Description"   = New-Label -Text "Azure OpenAI model deployment name used (e.g., gpt-4o-mini-2024-07-18)"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_corpusversion"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 50
        "DisplayName"   = New-Label -Text "Corpus Version"
        "Description"   = New-Label -Text "Test corpus version identifier (e.g., v1.0, 2026-02)"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_indexstate"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Index State"
        "Description"   = New-Label -Text "AI Search index state at run time (e.g., knowledge-v3, discovery-v2)"
    }
    @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"     = "sprk_recallatk"
        "RequiredLevel"  = @{ "Value" = "None" }
        "MinValue"       = 0.0
        "MaxValue"       = 1.0
        "Precision"      = 4
        "DisplayName"    = New-Label -Text "Recall@K"
        "Description"    = New-Label -Text "Aggregate Recall@K score for this run (0.0-1.0)"
    }
    @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"     = "sprk_ndcgatk"
        "RequiredLevel"  = @{ "Value" = "None" }
        "MinValue"       = 0.0
        "MaxValue"       = 1.0
        "Precision"      = 4
        "DisplayName"    = New-Label -Text "nDCG@K"
        "Description"    = New-Label -Text "Aggregate normalized Discounted Cumulative Gain@K score (0.0-1.0)"
    }
    @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                 = "sprk_status"
        "RequiredLevel"              = @{ "Value" = "ApplicationRequired" }
        "DisplayName"                = New-Label -Text "Status"
        "Description"                = New-Label -Text "Evaluation run status: Pending, Running, Complete, or Failed"
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions(Name='sprk_aievaluationrunstatus')"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_evaluationtype"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 50
        "DisplayName"   = New-Label -Text "Evaluation Type"
        "Description"   = New-Label -Text "Category of evaluation: rag_eval, chat_eval, playbook_eval"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_resultcount"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 999999
        "DisplayName"   = New-Label -Text "Result Count"
        "Description"   = New-Label -Text "Total number of sprk_aievaluationresult records in this run"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_passedcount"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 999999
        "DisplayName"   = New-Label -Text "Passed Count"
        "Description"   = New-Label -Text "Number of results where sprk_passed = true"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_reportjson"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 1048576
        "DisplayName"   = New-Label -Text "Report JSON"
        "Description"   = New-Label -Text "Full evaluation report in JSON format. Generated at completion."
    }
)

foreach ($attr in $evalRunAttrs) {
    if (-not (Test-AttributeExists -EntityLogicalName "sprk_aievaluationrun" -AttributeLogicalName $attr.SchemaName.ToLower())) {
        $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_aievaluationrun')/Attributes" -Method "POST" -Body $attr
        if ($result.Success) {
            Write-Host "  [OK] Added attribute: sprk_aievaluationrun.$($attr.SchemaName)" -ForegroundColor Green
        } else {
            Write-Warning "  [WARN] $($attr.SchemaName): $($result.Error)"
        }
    } else {
        Write-Host "  [SKIP] sprk_aievaluationrun.$($attr.SchemaName) already exists" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Phase 5: Create sprk_aievaluationresult
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 5: Creating sprk_aievaluationresult entity..." -ForegroundColor Yellow

if (-not (Test-EntityExists -LogicalName "sprk_aievaluationresult")) {
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_aievaluationresult"
        "DisplayName"           = New-Label -Text "AI Evaluation Result"
        "DisplayCollectionName" = New-Label -Text "AI Evaluation Results"
        "Description"           = New-Label -Text "Per-query evaluation result within an evaluation run"
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"       = "sprk_name"
                "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
                "MaxLength"        = 200
                "DisplayName"      = New-Label -Text "Result ID"
                "Description"      = New-Label -Text "Auto-generated result identifier"
                "IsPrimaryName"    = $true
                "AutoNumberFormat" = "RES-{SEQNUM:6}"
            }
        )
    }
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    if ($result.Success) {
        Write-Host "  [OK] Created entity: sprk_aievaluationresult" -ForegroundColor Green
    } else {
        Write-Error "  [FAIL] sprk_aievaluationresult: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] sprk_aievaluationresult already exists" -ForegroundColor Yellow
}

# Add attributes to sprk_aievaluationresult
$evalResultAttrs = @(
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_query"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 1000
        "DisplayName"   = New-Label -Text "Query"
        "Description"   = New-Label -Text "The evaluation question posed to the system"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_expectedkeywords"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 2000
        "DisplayName"   = New-Label -Text "Expected Keywords"
        "Description"   = New-Label -Text "JSON array of expected keyword strings (e.g., [""indemnification"", ""limitation of liability""])"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_actualresponse"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 10000
        "DisplayName"   = New-Label -Text "Actual Response"
        "Description"   = New-Label -Text "The full response returned by the system for this query"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_retrievedchunksjson"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 20000
        "DisplayName"   = New-Label -Text "Retrieved Chunks JSON"
        "Description"   = New-Label -Text "JSON array of retrieved RAG chunks that informed the response (for Recall@K)"
    }
    @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"     = "sprk_recallatk"
        "RequiredLevel"  = @{ "Value" = "None" }
        "MinValue"       = 0.0
        "MaxValue"       = 1.0
        "Precision"      = 4
        "DisplayName"    = New-Label -Text "Recall@K"
        "Description"    = New-Label -Text "Per-query Recall@K score (0.0-1.0)"
    }
    @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"     = "sprk_ndcgatk"
        "RequiredLevel"  = @{ "Value" = "None" }
        "MinValue"       = 0.0
        "MaxValue"       = 1.0
        "Precision"      = 4
        "DisplayName"    = New-Label -Text "nDCG@K"
        "Description"    = New-Label -Text "Per-query nDCG@K score (0.0-1.0)"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"    = "sprk_passed"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text "Passed"
        "Description"   = New-Label -Text "True if this query passed the evaluation threshold"
        "OptionSet"     = @{
            "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Passed" }
            "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "Failed" }
        }
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_failurereason"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 500
        "DisplayName"   = New-Label -Text "Failure Reason"
        "Description"   = New-Label -Text "If sprk_passed = false, brief description of why the query failed"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_playbookid"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Playbook ID"
        "Description"   = New-Label -Text "sprk_aiplaybook logical ID this query targets (e.g., PB-002)"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_latencyms"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 999999
        "DisplayName"   = New-Label -Text "Latency (ms)"
        "Description"   = New-Label -Text "Response latency in milliseconds"
    }
    @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_citationcount"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 999
        "DisplayName"   = New-Label -Text "Citation Count"
        "Description"   = New-Label -Text "Number of citations in the actual response"
    }
)

foreach ($attr in $evalResultAttrs) {
    if (-not (Test-AttributeExists -EntityLogicalName "sprk_aievaluationresult" -AttributeLogicalName $attr.SchemaName.ToLower())) {
        $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_aievaluationresult')/Attributes" -Method "POST" -Body $attr
        if ($result.Success) {
            Write-Host "  [OK] Added attribute: sprk_aievaluationresult.$($attr.SchemaName)" -ForegroundColor Green
        } else {
            Write-Warning "  [WARN] $($attr.SchemaName): $($result.Error)"
        }
    } else {
        Write-Host "  [SKIP] sprk_aievaluationresult.$($attr.SchemaName) already exists" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Phase 6: Create Lookup Relationships
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 6: Creating lookup relationships..." -ForegroundColor Yellow

# Relationship 1: sprk_aichatmessage -> sprk_aichatsummary (optional N:1)
$rel1SchemaName = "sprk_aichatsummary_aichatmessage_summaryid"
if (-not (Test-RelationshipExists -SchemaName $rel1SchemaName)) {
    $rel1 = @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = $rel1SchemaName
        "ReferencedEntity"     = "sprk_aichatsummary"
        "ReferencingEntity"    = "sprk_aichatmessage"
        "CascadeConfiguration" = @{
            "Assign"   = "NoCascade"
            "Delete"   = "Cascade"
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName"    = "sprk_summaryid"
            "DisplayName"   = New-Label -Text "Chat Summary"
            "Description"   = New-Label -Text "The chat session summary this message belongs to"
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }
    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $rel1
    if ($result.Success) {
        Write-Host "  [OK] Created relationship: sprk_aichatmessage.sprk_summaryid -> sprk_aichatsummary" -ForegroundColor Green
    } else {
        Write-Warning "  [WARN] Relationship ${rel1SchemaName}: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] Relationship $rel1SchemaName already exists" -ForegroundColor Yellow
}

# Relationship 2: sprk_aievaluationresult -> sprk_aievaluationrun (required N:1)
$rel2SchemaName = "sprk_aievaluationrun_aievaluationresult_runid"
if (-not (Test-RelationshipExists -SchemaName $rel2SchemaName)) {
    $rel2 = @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = $rel2SchemaName
        "ReferencedEntity"     = "sprk_aievaluationrun"
        "ReferencingEntity"    = "sprk_aievaluationresult"
        "CascadeConfiguration" = @{
            "Assign"   = "NoCascade"
            "Delete"   = "Cascade"
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName"    = "sprk_runid"
            "DisplayName"   = New-Label -Text "Evaluation Run"
            "Description"   = New-Label -Text "The evaluation run this result belongs to"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        }
    }
    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $rel2
    if ($result.Success) {
        Write-Host "  [OK] Created relationship: sprk_aievaluationresult.sprk_runid -> sprk_aievaluationrun" -ForegroundColor Green
    } else {
        Write-Warning "  [WARN] Relationship ${rel2SchemaName}: $($result.Error)"
    }
} else {
    Write-Host "  [SKIP] Relationship $rel2SchemaName already exists" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Phase 7: Publish Customizations
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 7: Publishing customizations..." -ForegroundColor Yellow

$entities = @("sprk_aichatmessage", "sprk_aichatsummary", "sprk_aievaluationrun", "sprk_aievaluationresult")
$entityXml = ($entities | ForEach-Object { "<entity>$_</entity>" }) -join ""
$publishRequest = @{
    "ParameterXml" = "<importexportxml><entities>$entityXml</entities></importexportxml>"
}

$result = Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishRequest
if ($result.Success) {
    Write-Host "  [OK] Customizations published" -ForegroundColor Green
} else {
    Write-Warning "  [WARN] Publish may have partially failed: $($result.Error)"
}

# ---------------------------------------------------------------------------
# Phase 8: Verification
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 8: Verifying deployment..." -ForegroundColor Yellow

$allEntities = @("sprk_aichatmessage", "sprk_aichatsummary", "sprk_aievaluationrun", "sprk_aievaluationresult")
$allVerified = $true

foreach ($entityName in $allEntities) {
    if (Test-EntityExists -LogicalName $entityName) {
        Write-Host "  [OK] $entityName exists in Dataverse" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $entityName NOT FOUND" -ForegroundColor Red
        $allVerified = $false
    }
}

Write-Host ""
if ($allVerified) {
    Write-Host "================================================" -ForegroundColor Green
    Write-Host "  Deployment complete. All 4 entities verified." -ForegroundColor Green
    Write-Host "================================================" -ForegroundColor Green
} else {
    Write-Host "================================================" -ForegroundColor Red
    Write-Host "  Deployment completed with errors." -ForegroundColor Red
    Write-Host "  Check output above for details." -ForegroundColor Red
    Write-Host "================================================" -ForegroundColor Red
}

Write-Host ""
Write-Host "Schema documentation: projects/ai-spaarke-platform-enhancements-r1/notes/design/dataverse-chat-schema.md"
Write-Host "AIPL-001 complete."
