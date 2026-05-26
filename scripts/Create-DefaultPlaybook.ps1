<#
.SYNOPSIS
    Creates or updates the "Spaarke AI General" default analysis playbook in Dataverse.

.DESCRIPTION
    Idempotent script that ensures the "Spaarke AI General" sprk_analysisplaybook record
    exists in Dataverse. This playbook auto-loads in the SpaarkeAi control when no context
    playbook is supplied by the host form (standalone mode).

    Fields written:
      sprk_name                 = "Spaarke AI General"
      sprk_description          = (see below)
      sprk_ispublic             = true
      sprk_playbookcapabilities = 100000000,100000001,100000004,100000006
                                  (search, analyze, selection_revise, summarize)
      sprk_systemprompt         = (comprehensive standalone-mode system prompt)
      sprk_playbookcode         = "PB-DEFAULT-GENERAL"
      sprk_isdefault            = true

    Idempotency: checks for an existing record by sprk_name before creating.
    If found, updates in-place. Running twice does NOT create duplicates.

    Record ID is logged to stdout on completion for traceability.

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to DATAVERSE_URL environment variable.
    Example: https://spaarkedev1.crm.dynamics.com

.PARAMETER WhatIf
    Preview the payload without making changes to Dataverse.

.EXAMPLE
    .\Create-DefaultPlaybook.ps1
    .\Create-DefaultPlaybook.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
    .\Create-DefaultPlaybook.ps1 -WhatIf

.NOTES
    Author:       Spaarke AI Platform Team
    Created:      2026-05-17
    Task:         AIPU2-043
    Entity:       sprk_analysisplaybook
    Entity set:   sprk_analysisplaybooks
    Field ref:    sprk_playbookcapabilities option values:
                    100000000 = search
                    100000001 = analyze
                    100000004 = selection_revise
                    100000006 = summarize
    Auth:         Azure CLI (az account get-access-token). Run 'az login' first.
    Environment:  Dev = https://spaarkedev1.crm.dynamics.com
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    $DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
    Write-Host "DataverseUrl not supplied — using default: $DataverseUrl" -ForegroundColor Yellow
}

$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase      = "$DataverseUrl/api/data/v9.2"
$EntitySet    = "sprk_analysisplaybooks"

# ---------------------------------------------------------------------------
# Playbook definition
# ---------------------------------------------------------------------------
$PlaybookName        = "Spaarke AI General"
$PlaybookCode        = "PB-DEFAULT-GENERAL"
$PlaybookDescription = "General-purpose AI assistant with document search, knowledge retrieval, entity queries, and text refinement capabilities. Auto-loaded in standalone mode."

# sprk_playbookcapabilities multi-select (comma-separated option integers):
#   100000000 = search          (SearchDocuments, SearchDiscovery)
#   100000001 = analyze         (QueryEntities, GetKnowledgeSource)
#   100000004 = selection_revise (RefineText)
#   100000006 = summarize        (GenerateSummary)
$PlaybookCapabilities = "100000000,100000001,100000004,100000006"

$PlaybookSystemPrompt = @"
You are Spaarke AI, an intelligent legal and business document assistant. You help legal professionals search documents, retrieve knowledge, query entity records, refine text, and generate summaries.

## Role
You are a knowledgeable, precise, and professional AI assistant embedded in the Spaarke platform. You have access to the organisation's document library, knowledge base, and structured data.

## Core Capabilities
- **Document Search** (SearchDocuments): Search the document library using semantic and keyword queries. Always cite document names and sources when referencing content.
- **Discovery Search** (SearchDiscovery): Explore related documents and topics across the knowledge index.
- **Knowledge Retrieval** (GetKnowledgeSource): Retrieve structured knowledge articles, policies, and reference materials.
- **Entity Queries** (QueryEntities): Query matters, contacts, accounts, and other Dataverse entities to provide contextual information.
- **Text Refinement** (RefineText): Help improve, rephrase, or reformat text provided by the user.
- **Summary Generation** (GenerateSummary): Generate concise, accurate summaries of documents or topics.

## Behaviour Guidelines
1. **Cite your sources**: Always reference document names, record names, or knowledge article titles when drawing on retrieved content. Use the format: *Source: [Document Name]* or *(Source: [Knowledge Article])*.
2. **Be precise and professional**: Use plain language. Avoid unnecessary jargon. Structure responses clearly with headings or bullet points where appropriate.
3. **Ask for clarification when needed**: If a request is ambiguous, ask one focused clarifying question before proceeding.
4. **Acknowledge uncertainty**: If you cannot find relevant information in the available sources, say so clearly and suggest next steps.
5. **Respect data scope**: Only reference documents and data within the user's authorised scope. Do not fabricate citations or invent document content.
6. **Standalone mode**: When no specific matter or document context has been provided, assist the user in finding relevant information across their organisation's content.

## Response Format
- Use Markdown formatting for structure (headings, bullet lists, bold for emphasis).
- Keep responses concise — prefer structured summaries over lengthy prose.
- For document excerpts, use block quotes.
- For multi-step answers, use numbered lists.
"@

# ---------------------------------------------------------------------------
# Auth
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Create-DefaultPlaybook.ps1 ===" -ForegroundColor Cyan
Write-Host "Target:   $DataverseUrl"
Write-Host "Playbook: $PlaybookName"
if ($WhatIf) { Write-Host "Mode:     WHAT-IF (no changes will be made)" -ForegroundColor Yellow }
else          { Write-Host "Mode:     LIVE" -ForegroundColor Green }
Write-Host ""

Write-Host "[1/4] Authenticating with Azure CLI..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $DataverseUrl --query accessToken -o tsv 2>&1)
if (-not $token -or $token -match 'ERROR') {
    Write-Error "Failed to obtain access token. Run 'az login' and ensure you have access to $DataverseUrl."
    exit 1
}
Write-Host "  Token acquired." -ForegroundColor Green

$headers = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Prefer'           = 'return=representation'
}

# ---------------------------------------------------------------------------
# Idempotency check: does a record with this name already exist?
# ---------------------------------------------------------------------------
Write-Host "[2/4] Checking for existing '$PlaybookName' record..." -ForegroundColor Yellow

$encodedFilter = [Uri]::EscapeDataString("sprk_name eq '$PlaybookName'")
$checkUrl      = "$ApiBase/$EntitySet" +
                 "?`$select=sprk_analysisplaybookid,sprk_name,sprk_playbookcode,sprk_ispublic" +
                 "&`$filter=$encodedFilter" +
                 "&`$top=1"

try {
    $checkResponse = Invoke-RestMethod -Uri $checkUrl -Headers $headers -Method Get
}
catch {
    Write-Error "Failed to query Dataverse for existing record: $($_.Exception.Message)"
    exit 1
}

$existingRecord = $null
if ($checkResponse.value -and $checkResponse.value.Count -gt 0) {
    $existingRecord = $checkResponse.value[0]
    $existingId     = $existingRecord.sprk_analysisplaybookid
    Write-Host "  Found existing record: ID = $existingId" -ForegroundColor Cyan
}
else {
    Write-Host "  No existing record found — will create." -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# Build the payload
# ---------------------------------------------------------------------------
$payload = @{
    sprk_name                 = $PlaybookName
    sprk_description          = $PlaybookDescription
    sprk_ispublic             = $true
    sprk_playbookcode         = $PlaybookCode
    sprk_playbookcapabilities = $PlaybookCapabilities
    sprk_systemprompt         = $PlaybookSystemPrompt
    sprk_isdefault            = $true
}

$body = $payload | ConvertTo-Json -Depth 10

if ($WhatIf) {
    Write-Host ""
    Write-Host "[WHAT-IF] Would send the following payload to Dataverse:" -ForegroundColor Yellow
    Write-Host $body
    Write-Host ""
    Write-Host "[WHAT-IF] No changes made." -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------------------
# Create or Update
# ---------------------------------------------------------------------------
Write-Host "[3/4] Writing record to Dataverse..." -ForegroundColor Yellow

try {
    if ($existingRecord) {
        # UPDATE existing record (PATCH)
        $updateUrl = "$ApiBase/$EntitySet($existingId)"
        $patchHeaders = $headers.Clone()
        $patchHeaders['If-Match'] = '*'

        Invoke-RestMethod -Uri $updateUrl -Headers $patchHeaders -Method Patch -Body $body | Out-Null

        $recordId = $existingId
        Write-Host "  Updated existing record." -ForegroundColor Green
    }
    else {
        # CREATE new record (POST)
        $createUrl    = "$ApiBase/$EntitySet"
        $createHeaders = $headers.Clone()
        $createHeaders['Prefer'] = 'return=representation'

        $createResponse = Invoke-WebRequest -Uri $createUrl -Headers $createHeaders -Method Post -Body $body -UseBasicParsing
        $locationHeader = $createResponse.Headers['OData-EntityId']

        if ($locationHeader) {
            $recordId = [regex]::Match($locationHeader, '\(([^)]+)\)').Groups[1].Value
        }
        else {
            # Fall back: re-query by name
            $recheck = Invoke-RestMethod -Uri $checkUrl -Headers $headers -Method Get
            $recordId = $recheck.value[0].sprk_analysisplaybookid
        }

        Write-Host "  Created new record." -ForegroundColor Green
    }
}
catch {
    $statusCode  = $_.Exception.Response?.StatusCode
    $errorDetail = $_.ErrorDetails?.Message ?? $_.Exception.Message
    Write-Error "Dataverse write failed (HTTP $statusCode): $errorDetail"
    exit 1
}

# ---------------------------------------------------------------------------
# Verify
# ---------------------------------------------------------------------------
Write-Host "[4/4] Verifying record..." -ForegroundColor Yellow

$verifyUrl = "$ApiBase/$EntitySet($recordId)" +
             "?`$select=sprk_analysisplaybookid,sprk_name,sprk_playbookcode,sprk_ispublic,sprk_isdefault,sprk_playbookcapabilities"

try {
    $verified = Invoke-RestMethod -Uri $verifyUrl -Headers $headers -Method Get
}
catch {
    Write-Warning "Could not verify record (HTTP query failed): $($_.Exception.Message)"
    $verified = $null
}

Write-Host ""
Write-Host "=== Result ===" -ForegroundColor Cyan
Write-Host "  Record ID   : $recordId"
if ($verified) {
    Write-Host "  Name        : $($verified.sprk_name)"
    Write-Host "  Code        : $($verified.sprk_playbookcode)"
    Write-Host "  IsPublic    : $($verified.sprk_ispublic)"
    Write-Host "  IsDefault   : $($verified.sprk_isdefault)"
    Write-Host "  Capabilities: $($verified.sprk_playbookcapabilities)"
}
Write-Host ""
Write-Host "Done. Record ID: $recordId" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Verify in Dataverse: https://spaarkedev1.crm.dynamics.com/main.aspx?etn=sprk_analysisplaybook"
Write-Host "  2. Query via MCP:  SELECT sprk_analysisplaybookid, sprk_name, sprk_isdefault FROM sprk_analysisplaybook WHERE sprk_name = '$PlaybookName'"
Write-Host "  3. Open SpaarkeAi in standalone mode to confirm auto-load."
Write-Host ""
