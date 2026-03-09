<#
.SYNOPSIS
    Creates 8 system Tool records in the sprk_analysistool Dataverse entity.

.DESCRIPTION
    Creates TL-001 through TL-008 in sprk_analysistools.
    Each record has a sprk_handlerclass (matched to MapHandlerClassToToolType in ScopeResolverService)
    and a JSON Schema in sprk_configuration.
    Uses sprk_toolcode as the alternate key (resolved by ToolLookupService.GetByCodeAsync).

    Prerequisites:
    - Azure CLI installed and authenticated: az login
    - Access to spaarkedev1.crm.dynamics.com (System Customizer or System Administrator)
    - PowerShell 7+

    This script is IDEMPOTENT — skips records that already have a matching sprk_toolcode.

.NOTES
    Task:    AIPL-033
    Entity:  sprk_analysistool (collection: sprk_analysistools)
    Created: 2026-02-23

    Handler class → ToolType mapping (from ScopeResolverService.MapHandlerClassToToolType):
      Contains "Summary"         → ToolType.Summary
      Contains "RiskDetector"    → ToolType.RiskDetector
      Contains "EntityExtractor" → ToolType.EntityExtractor
      (default)                  → ToolType.Custom
#>

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

$BaseUrl    = "https://$Environment/api/data/v9.2"
$Collection = "sprk_analysistools"

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Create-ToolSeedRecords.ps1" -ForegroundColor Cyan
Write-Host "  Target: $Environment" -ForegroundColor Cyan
Write-Host "  Task:   AIPL-033" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Authentication
# ---------------------------------------------------------------------------

Write-Host "Step 0: Obtaining Azure access token..." -ForegroundColor Yellow
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv 2>&1)
if (-not $token -or $token -like "*ERROR*" -or $token -like "*error*") {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}
Write-Host "  Token obtained." -ForegroundColor Green

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $uri = "$BaseUrl/$Endpoint"
    $params = @{ Uri = $uri; Headers = $headers; Method = $Method }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10 -Compress) }
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

# ---------------------------------------------------------------------------
# Step 1: Verify no TL-001-008 records exist
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 1: Checking for existing TL-001-008 records..." -ForegroundColor Yellow

$result = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_toolcode&`$filter=sprk_toolcode ne null"
if ($result.Success) {
    $existing = $result.Data.value
    if ($existing.Count -eq 0) {
        Write-Host "  (none — safe to create all 8)" -ForegroundColor Green
    } else {
        Write-Host "  Existing records with sprk_toolcode:" -ForegroundColor Yellow
        foreach ($rec in $existing) {
            Write-Host "    - $($rec.sprk_toolcode) : $($rec.sprk_name)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Warning "  Could not query existing records: $($result.Error)"
}

# ---------------------------------------------------------------------------
# Step 2 & 3: Create 8 tool records
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 3: Creating tool records..." -ForegroundColor Yellow

# JSON Schema strings for each tool's sprk_configuration field
$tools = @(
    @{
        sprk_toolcode      = "TL-001"
        sprk_name          = "DocumentSearch"
        sprk_description   = "Search the knowledge base and document index for relevant content matching a query"
        sprk_handlerclass  = "DocumentSearchHandler"
        # ToolType: Custom (no matching substring in MapHandlerClassToToolType)
        sprk_configuration = '{"type":"object","properties":{"query":{"type":"string","description":"Search query text"},"topK":{"type":"integer","description":"Maximum number of results to return","default":5},"indexName":{"type":"string","description":"Optional: target a specific AI Search index"}},"required":["query"]}'
    }
    @{
        sprk_toolcode      = "TL-002"
        sprk_name          = "AnalysisRetrieval"
        sprk_description   = "Retrieve previously computed analysis results for a specific document or analysis session"
        sprk_handlerclass  = "AnalysisQueryHandler"
        # ToolType: Custom
        sprk_configuration = '{"type":"object","properties":{"documentId":{"type":"string","description":"Document identifier to retrieve analysis for"},"analysisType":{"type":"string","description":"Optional: filter by analysis type (e.g., summary, risk)"}},"required":["documentId"]}'
    }
    @{
        sprk_toolcode      = "TL-003"
        sprk_name          = "KnowledgeRetrieval"
        sprk_description   = "Retrieve specific knowledge source content by identifier or type from the knowledge store"
        sprk_handlerclass  = "KnowledgeRetrievalHandler"
        # ToolType: Custom
        sprk_configuration = '{"type":"object","properties":{"knowledgeId":{"type":"string","description":"Knowledge source identifier"},"contentType":{"type":"string","description":"Type of knowledge content to retrieve","enum":["inline","rag","document"]}},"required":["knowledgeId"]}'
    }
    @{
        sprk_toolcode      = "TL-004"
        sprk_name          = "TextRefinement"
        sprk_description   = "Refine, reformat, or restructure a text section using AI-assisted editing"
        sprk_handlerclass  = "TextRefinementHandler"
        # ToolType: Custom
        sprk_configuration = '{"type":"object","properties":{"text":{"type":"string","description":"Input text to refine"},"instruction":{"type":"string","description":"Refinement instruction (e.g., summarize, rewrite formally, bullet points)"},"maxLength":{"type":"integer","description":"Maximum output length in characters"}},"required":["text","instruction"]}'
    }
    @{
        sprk_toolcode      = "TL-005"
        sprk_name          = "CitationExtractor"
        sprk_description   = "Extract and normalize citation references from analysis results and document text"
        sprk_handlerclass  = "CitationExtractorHandler"
        # ToolType: EntityExtractor (contains "EntityExtractor" — wait, CitationExtractorHandler doesn't contain "EntityExtractor")
        # Actually from the bash script: CitationExtractorHandler → ToolType.EntityExtractor because it "contains EntityExtractor"
        # But "CitationExtractorHandler" does NOT contain "EntityExtractor". Looking again at the bash script comments:
        # TL-005: CitationExtractor → "CitationExtractorHandler" (contains "EntityExtractor") → EntityExtractor
        # This seems wrong. Let me re-check ScopeResolverService mapping.
        # From the task: "TL-005 CitationExtractor → 'CitationExtractorHandler' (contains 'EntityExtractor')"
        # The task says it contains "EntityExtractor" but "CitationExtractorHandler" doesn't contain that substring.
        # The bash script agent made a mistake in comments. The handler is "CitationExtractorHandler" and maps to Custom.
        # But the task file says to use a handler that makes the ToolType map to EntityExtractor.
        # Let me just use "CitationExtractorHandler" as specified in the task.
        sprk_configuration = '{"type":"object","properties":{"text":{"type":"string","description":"Text to extract citations from"},"format":{"type":"string","description":"Citation format standard","enum":["bluebook","apa","mla","auto"],"default":"auto"},"includeContext":{"type":"boolean","description":"Include surrounding context for each citation","default":false}},"required":["text"]}'
    }
    @{
        sprk_toolcode      = "TL-006"
        sprk_name          = "SummaryGenerator"
        sprk_description   = "Generate structured summaries of document sections or complete documents"
        sprk_handlerclass  = "SummaryGeneratorHandler"
        # ToolType: Summary (contains "Summary")
        sprk_configuration = '{"type":"object","properties":{"text":{"type":"string","description":"Text to summarize"},"summaryType":{"type":"string","description":"Type of summary to generate","enum":["executive","detailed","bullet","one-sentence"],"default":"executive"},"maxWords":{"type":"integer","description":"Maximum word count for the summary","default":200},"focusAreas":{"type":"array","items":{"type":"string"},"description":"Optional: specific topics to emphasize"}},"required":["text"]}'
    }
    @{
        sprk_toolcode      = "TL-007"
        sprk_name          = "RedFlagDetector"
        sprk_description   = "Detect risk indicators, problematic clauses, and compliance issues in document sections"
        sprk_handlerclass  = "RedFlagDetectorHandler"
        # ToolType: RiskDetector (contains "RiskDetector")
        sprk_configuration = '{"type":"object","properties":{"text":{"type":"string","description":"Document text to analyze for risk indicators"},"riskCategories":{"type":"array","items":{"type":"string"},"description":"Risk categories to check (e.g., liability, compliance, financial)"},"severity":{"type":"string","description":"Minimum severity threshold","enum":["low","medium","high","critical"],"default":"medium"}},"required":["text"]}'
    }
    @{
        sprk_toolcode      = "TL-008"
        sprk_name          = "PartyExtractor"
        sprk_description   = "Extract and normalize party information (people, organizations, roles) from document text"
        sprk_handlerclass  = "PartyExtractorHandler"
        # ToolType: EntityExtractor (contains "EntityExtractor" — wait, "PartyExtractorHandler" doesn't contain "EntityExtractor" either)
        # From the task: TL-008 PartyExtractor → "PartyExtractorHandler" (contains "EntityExtractor") → EntityExtractor
        # "PartyExtractorHandler" does NOT contain "EntityExtractor". Using as-is per task specification.
        sprk_configuration = '{"type":"object","properties":{"text":{"type":"string","description":"Document text to extract parties from"},"partyTypes":{"type":"array","items":{"type":"string"},"description":"Party types to extract","enum":["person","organization","role","all"],"default":["all"]},"includeAliases":{"type":"boolean","description":"Include alternate names or aliases","default":true}},"required":["text"]}'
    }
)

$created = 0
$skipped = 0

foreach ($tool in $tools) {
    $code = $tool.sprk_toolcode
    Write-Host "  Creating $code : $($tool.sprk_name)..." -NoNewline

    # Check if this specific code already exists
    $checkResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_toolcode='$code')?`$select=sprk_name"
    if ($checkResult.Success) {
        Write-Host " [SKIP] already exists" -ForegroundColor Yellow
        $skipped++
        continue
    }

    $body = @{
        sprk_name          = $tool.sprk_name
        sprk_description   = $tool.sprk_description
        sprk_handlerclass  = $tool.sprk_handlerclass
        sprk_configuration = $tool.sprk_configuration
        sprk_toolcode      = $code
    }

    $postResult = Invoke-DataverseApi -Endpoint $Collection -Method "POST" -Body $body
    if ($postResult.Success) {
        Write-Host " [OK]" -ForegroundColor Green
        $created++
    } else {
        Write-Host " [FAIL]" -ForegroundColor Red
        Write-Warning "    Error: $($postResult.Error)"
    }
}

Write-Host ""
Write-Host "  Created: $created  Skipped: $skipped  Total: $($created + $skipped)/8" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Step 4: Verify records and alternate key lookup
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 4: Verifying created records..." -ForegroundColor Yellow

$verifyResult = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_toolcode,sprk_handlerclass&`$filter=sprk_toolcode ne null&`$orderby=sprk_toolcode asc"
if ($verifyResult.Success) {
    $records = $verifyResult.Data.value
    Write-Host "  Records with sprk_toolcode set: $($records.Count)" -ForegroundColor Green
    foreach ($rec in $records) {
        Write-Host "    $($rec.sprk_toolcode) | $($rec.sprk_name) | $($rec.sprk_handlerclass)" -ForegroundColor Green
    }
} else {
    Write-Warning "  Verification query failed: $($verifyResult.Error)"
}

Write-Host ""
Write-Host "  Testing alternate key lookup: TL-001..." -NoNewline
$lookupResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_toolcode='TL-001')?`$select=sprk_name,sprk_toolcode,sprk_handlerclass"
if ($lookupResult.Success) {
    Write-Host " [OK] $($lookupResult.Data.sprk_toolcode) : $($lookupResult.Data.sprk_name)" -ForegroundColor Green
} else {
    Write-Host " [FAIL]" -ForegroundColor Red
    Write-Warning "  Alternate key lookup failed: $($lookupResult.Error)"
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  AIPL-033 complete." -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Documentation: projects/ai-spaarke-platform-enhancements-r1/notes/design/scope-library-catalog.md"
