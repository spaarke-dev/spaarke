<#
.SYNOPSIS
    Seeds the default global SYS- persona row in sprk_aipersona (R6 Pillar 1, FR-04).

.DESCRIPTION
    Creates the single SYS-DEFAULT persona row that the Pillar 1 resolver
    (IScopeResolverService.ResolvePersonaForChatAsync — task 003) returns when no
    tenant CUST- or playbook-attached override exists.

    FR-04 binding: the sprk_systemprompt field MUST contain the VERBATIM text
    currently returned by PlaybookChatContextProvider.BuildDefaultSystemPrompt(null)
    (the "standalone" branch, lines 541-569 as of 2026-06-07). After task 005 wires
    SprkChatAgentFactory.CreateAgentAsync to the resolver, the agent's voice with
    no playbook/tenant override MUST be character-for-character identical to today.

    Row shape:
      sprk_name              = "SYS-DEFAULT"                 (matches task 004 goal)
      sprk_personacode       = "SYS-DEF"                     (10-char limit per schema)
      sprk_description       = "Default Spaarke AI persona; fallback when no tenant or playbook override exists"
      sprk_systemprompt      = <verbatim text from PlaybookChatContextProvider>
      sprk_scopetype         = 100000000 (Global)
      sprk_tags              = "system,default,fallback,chat"
      sprk_availableadhoc    = true
      sprk_parentpersonaid   = null                          (root of inheritance chain)

    Idempotency: query by sprk_name first. If found:
      - If sprk_systemprompt already matches verbatim → UNCHANGED (no-op)
      - If text drifted → UPDATE with PATCH (heals drift on re-run)
      - If shape (scopetype/availableadhoc) differs from spec → UPDATE
    If not found → CREATE with POST.

    Pattern: clone of scripts/Seed-KnowledgeScopes.ps1 (canonical scope-seed exemplar).

.PARAMETER DataverseUrl
    The Dataverse environment URL. Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER WhatIf
    Preview-only mode — describes what would be created/updated without modifying Dataverse.

.EXAMPLE
    # Preview without modifying
    .\Seed-AiPersonaDefault.ps1 -WhatIf

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Seed-AiPersonaDefault.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project : spaarke-ai-platform-unification-r6 (Pillar 1)
    Task    : 004 (D-A-04) — Seed Default Global SYS- Persona Row
    Source  : src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs
              BuildDefaultSystemPrompt(null) — standalone-mode branch, lines 541-569
    Reference text : projects/spaarke-ai-platform-unification-r6/notes/task-004-system-prompt-source.txt
    Pattern : scripts/Seed-KnowledgeScopes.ps1 (canonical exemplar)
    ADRs    : ADR-027 (sprk_ prefix; unmanaged), ADR-029 (BFF size delta = 0 MB — no BFF code change)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Verbatim system prompt text — FR-04 binding (DO NOT EDIT without updating the
# source method at src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs).
# Captured from master HEAD on 2026-06-07.
# -----------------------------------------------------------------------------

$SystemPrompt = @'
You are Spaarke AI, an intelligent assistant for legal professionals using the Spaarke platform.
You help with document analysis, matter management, legal research, financial analysis, and general questions about the user's work.

## Your Capabilities
You have access to powerful tools — use them proactively:

- **SearchDocuments**: Search the document index to find relevant content. Use this when the user asks about documents, contracts, agreements, filings, or any content stored in Spaarke.
- **SearchDiscovery**: Broad discovery search across all indexed documents. Use this when the user asks to find matters, projects, documents, or explore what's available.
- **GetKnowledgeSource**: Retrieve full content from a specific knowledge source. Use after SearchDocuments identifies a relevant source.
- **SearchKnowledgeBase**: Search the knowledge base for reference information, policies, and best practices.
- **GetAnalysisResult** / **GetAnalysisSummary**: Retrieve prior analysis results for documents that have been analyzed.
- **RefineText**: Help the user improve, rewrite, or restructure text.

## Instructions
- When the user asks about their matters, projects, or documents, **always use SearchDiscovery or SearchDocuments first** — don't say you can't access their data.
- When you find relevant documents, summarize what you found and offer to analyze further.
- If the user asks to analyze a document but none is loaded, suggest they upload one or help them search for it.
- Cite sources and document names when referencing search results.
- Be proactive — if a search returns relevant results, highlight key findings.
- Format responses in clear, readable Markdown with headings and structure.

## Workspace Tab Conflict Resolution
- If a workspace tab update (update_workspace_tab) refuses with status "stale_read", the user edited the tab since your last read. Re-read the tab from the current workspace state in your next turn before re-attempting the update. User edits always win.

## What You Know About
- Legal documents (contracts, agreements, court filings, memos, briefs)
- Matter management (case details, timelines, budgets, parties)
- Financial data (budgets, invoices, billing, cost analysis)
- Document comparison and review workflows
- Legal research and case law (when Bing Grounding is available)
'@

# Normalize line endings to LF so verbatim diff is stable regardless of the
# git autocrlf setting on the shell host (the C# source file is stored with
# repo .gitattributes; PS here-strings adopt host line endings).
$SystemPrompt = $SystemPrompt -replace "`r`n", "`n"

# -----------------------------------------------------------------------------
# Row shape
# -----------------------------------------------------------------------------

$Row = @{
    Name               = "SYS-DEFAULT"
    PersonaCode        = "SYS-DEF"                          # max 10 chars per schema
    Description        = "Default Spaarke AI persona; fallback when no tenant or playbook override exists."
    SystemPrompt       = $SystemPrompt
    ScopeType          = 100000000                          # Global
    Tags               = "system,default,fallback,chat"
    AvailableAdHoc     = $true
    # ParentPersonaId is intentionally not set — root of inheritance chain
}

# -----------------------------------------------------------------------------
# Authentication
# -----------------------------------------------------------------------------

function Get-DataverseToken {
    param([string]$ResourceUrl)
    $resource = $ResourceUrl.TrimEnd('/')
    $token = az account get-access-token --resource $resource --query accessToken -o tsv 2>$null
    if (-not $token) {
        throw "Failed to acquire token via Azure CLI. Run 'az login' first."
    }
    return $token
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ApiUrl = "$DataverseUrl/api/data/v9.2"

Write-Host ""
Write-Host "=== Seed AI Persona — SYS-DEFAULT (R6 D-A-04) ==="
Write-Host "Environment   : $DataverseUrl"
Write-Host "Mode          : $(if ($WhatIf) { 'WHATIF (preview only)' } else { 'LIVE' })"
Write-Host "Prompt bytes  : $($SystemPrompt.Length) chars"
Write-Host ""

if ($WhatIf) {
    Write-Host "[WHATIF] Would query sprk_aipersonas?`$filter=sprk_name eq 'SYS-DEFAULT'"
    Write-Host "[WHATIF] If absent  -> POST sprk_aipersonas with shape:"
    Write-Host "           sprk_name           = $($Row.Name)"
    Write-Host "           sprk_personacode    = $($Row.PersonaCode)"
    Write-Host "           sprk_scopetype      = $($Row.ScopeType) (Global)"
    Write-Host "           sprk_availableadhoc = $($Row.AvailableAdHoc)"
    Write-Host "           sprk_tags           = $($Row.Tags)"
    Write-Host "           sprk_systemprompt   = <$($SystemPrompt.Length)-char verbatim text>"
    Write-Host "[WHATIF] If present -> compare sprk_systemprompt + shape; PATCH only on drift."
    Write-Host ""
    Write-Host "No modifications performed." -ForegroundColor Yellow
    exit 0
}

Write-Host "Acquiring token via Azure CLI..."
$token = Get-DataverseToken -ResourceUrl $DataverseUrl
$headers = @{
    'Authorization'    = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
    'Prefer'           = 'return=representation'
}

# ---- Step 1: Check for existing row by sprk_name ----
Write-Host ""
Write-Host "Step 1: Querying for existing SYS-DEFAULT row..." -ForegroundColor Cyan

$escapedName = $Row.Name.Replace("'", "''")
$filter = "sprk_name eq '$escapedName'"
$selectFields = "sprk_aipersonaid,sprk_name,sprk_personacode,sprk_systemprompt,sprk_scopetype,sprk_availableadhoc,sprk_tags,sprk_description"
$checkUrl = "$ApiUrl/sprk_aipersonas?`$filter=$([uri]::EscapeDataString($filter))&`$select=$selectFields&`$top=1"

$existing = Invoke-RestMethod -Uri $checkUrl -Headers $headers -Method Get

# Build the POST/PATCH body (Web API attribute names).
$body = @{
    sprk_name              = $Row.Name
    sprk_personacode       = $Row.PersonaCode
    sprk_description       = $Row.Description
    sprk_systemprompt      = $Row.SystemPrompt
    sprk_scopetype         = $Row.ScopeType
    sprk_tags              = $Row.Tags
    sprk_availableadhoc    = $Row.AvailableAdHoc
} | ConvertTo-Json -Depth 10

$action = ""
$resultId = $null

if ($existing.value.Count -gt 0) {
    $existingRow = $existing.value[0]
    $existingId = $existingRow.sprk_aipersonaid
    $existingPrompt = $existingRow.sprk_systemprompt

    # Normalize stored value for compare (Dataverse may persist with CRLF on intake).
    if ($existingPrompt) {
        $existingPromptNormalized = $existingPrompt -replace "`r`n", "`n"
    } else {
        $existingPromptNormalized = $null
    }

    $promptMatches      = ($existingPromptNormalized -eq $SystemPrompt)
    $shapeMatches       = (
        $existingRow.sprk_scopetype -eq $Row.ScopeType -and
        $existingRow.sprk_availableadhoc -eq $Row.AvailableAdHoc -and
        $existingRow.sprk_personacode -eq $Row.PersonaCode
    )

    if ($promptMatches -and $shapeMatches) {
        Write-Host "  UNCHANGED: row exists, prompt + shape already match" -ForegroundColor Gray
        Write-Host "  sprk_aipersonaid: $existingId"
        $action = "unchanged"
        $resultId = $existingId
    } else {
        Write-Host "  DRIFT DETECTED: PATCH-ing row to re-sync prompt + shape" -ForegroundColor Yellow
        if (-not $promptMatches) {
            Write-Host "    sprk_systemprompt drift: stored=$($existingPromptNormalized.Length) chars / expected=$($SystemPrompt.Length) chars"
        }
        if (-not $shapeMatches) {
            Write-Host "    Shape drift: scopetype/availableadhoc/personacode out of spec"
        }

        $updateUrl = "$ApiUrl/sprk_aipersonas($existingId)"
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $body | Out-Null
        Write-Host "  UPDATED: row patched to spec" -ForegroundColor Green
        $action = "updated"
        $resultId = $existingId
    }
} else {
    # ---- Step 2: Create the row ----
    Write-Host "  Row absent — POST-ing new SYS-DEFAULT row" -ForegroundColor Gray

    $createUrl = "$ApiUrl/sprk_aipersonas"
    $created = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $body
    Write-Host "  CREATED: SYS-DEFAULT row" -ForegroundColor Green
    Write-Host "  sprk_aipersonaid: $($created.sprk_aipersonaid)"
    $action = "created"
    $resultId = $created.sprk_aipersonaid
}

# ---- Step 3: Verify ----
Write-Host ""
Write-Host "Step 2: Verifying row content..." -ForegroundColor Cyan

$verifyUrl = "$ApiUrl/sprk_aipersonas($resultId)?`$select=$selectFields"
$verified = Invoke-RestMethod -Uri $verifyUrl -Headers $headers -Method Get

$verifiedPrompt = $verified.sprk_systemprompt -replace "`r`n", "`n"
$verifiedPromptMatches = ($verifiedPrompt -eq $SystemPrompt)

Write-Host "  sprk_name           : $($verified.sprk_name)"
Write-Host "  sprk_personacode    : $($verified.sprk_personacode)"
Write-Host "  sprk_scopetype      : $($verified.sprk_scopetype) (expected: $($Row.ScopeType) Global)"
Write-Host "  sprk_availableadhoc : $($verified.sprk_availableadhoc)"
Write-Host "  sprk_tags           : $($verified.sprk_tags)"
Write-Host "  sprk_systemprompt   : $($verifiedPrompt.Length) chars"

if (-not $verifiedPromptMatches) {
    Write-Host ""
    Write-Host "  HARD FAIL: sprk_systemprompt drift detected after write!" -ForegroundColor Red
    Write-Host "  Expected: $($SystemPrompt.Length) chars"
    Write-Host "  Got     : $($verifiedPrompt.Length) chars"
    exit 1
}

Write-Host "  Verbatim match: OK" -ForegroundColor Green

# ---- Summary ----
Write-Host ""
Write-Host "=== Summary ==="
Write-Host "Action            : $action"
Write-Host "sprk_aipersonaid  : $resultId"
Write-Host "Verbatim match    : OK"
Write-Host ""
Write-Host "Seed complete." -ForegroundColor Green
