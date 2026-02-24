<#
.SYNOPSIS
    Creates 10 system Knowledge Source records in the sprk_content Dataverse entity.

.DESCRIPTION
    Creates KNW-001 through KNW-010 in sprk_contents.
    Each record stores legal reference content in sprk_contenttext for RAG retrieval via the
    knowledge-index in Azure AI Search. Content is read from the markdown files in
    notes/design/knowledge-sources/ and embedded inline into the Dataverse record.

    Uses sprk_externalid as the alternate key for idempotent upsert.

    Prerequisites:
    - Azure CLI installed and authenticated: az login
    - Access to spaarkedev1.crm.dynamics.com (System Customizer or System Administrator)
    - PowerShell 7+

    This script is IDEMPOTENT — skips records that already have a matching sprk_externalid.

.NOTES
    Task:    AIPL-032
    Entity:  sprk_content (collection: sprk_contents)
    Created: 2026-02-23

    ─────────────────────────────────────────────────────────────────────────────
    AI SEARCH INDEXING NOTE (Step 3 of AIPL-032)
    ─────────────────────────────────────────────────────────────────────────────
    This script creates the Dataverse records only. AI Search indexing of the
    knowledge source content is a SEPARATE step performed post-AIPL-018 deployment.

    Indexing workflow (after AIPL-018 deploys KnowledgeBaseEndpoints.cs):

      1. Ensure Dataverse records exist (this script)
      2. Deploy KnowledgeBaseEndpoints.cs via AIPL-018 (creates /api/ai/knowledge endpoint)
      3. Call the indexing endpoint for each knowledge source:

         POST /api/ai/knowledge/index
         {
           "externalId": "KNW-001",
           "tenantId": "system",
           "chunkSize": 512,
           "overlapSize": 64
         }

         OR batch index all system knowledge sources:

         POST /api/ai/knowledge/index/batch
         {
           "tenantId": "system",
           "filter": "system"
         }

      4. Verify indexing via test-search endpoint:
         GET /api/ai/knowledge/test-search
             ?query=force+majeure+clause&tenantId=system&topK=3

    Knowledge sources are indexed with tenantId="system" (ADR-014: shared across all tenants).
    The RAG pipeline in RagQueryBuilder.cs will include system knowledge sources in all
    tenant-scoped queries via the knowledge-index hybrid search.

    Index: knowledge-index (Azure AI Search: spaarke-search-dev.search.windows.net)
    Fields: id, tenantId, externalId, contentType, chunkIndex, chunkText, contentVector
    Semantic config: knowledge-semantic-config (see infrastructure/ai-search/knowledge-index.json)
    ─────────────────────────────────────────────────────────────────────────────

    ADR Compliance:
    - ADR-002: Records created via data import/script — no plugin processing
    - ADR-014: tenantId="system" for all system-level knowledge sources
#>

param(
    [string]$Environment     = "spaarkedev1.crm.dynamics.com",
    [string]$ContentFilePath = $PSScriptRoot + "\..\notes\design\knowledge-sources"
)

$ErrorActionPreference = "Stop"

$BaseUrl    = "https://$Environment/api/data/v9.2"
$Collection = "sprk_analysisknowledges"

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Create-KnowledgeSourceRecords.ps1" -ForegroundColor Cyan
Write-Host "  Target: $Environment" -ForegroundColor Cyan
Write-Host "  Task:   AIPL-032" -ForegroundColor Cyan
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

function Read-ContentFile {
    param([string]$FilePath)
    if (Test-Path $FilePath) {
        return (Get-Content -Path $FilePath -Raw -Encoding UTF8)
    } else {
        Write-Warning "  Content file not found: $FilePath"
        return $null
    }
}

# ---------------------------------------------------------------------------
# Step 1: Verify no KNW-001-010 records exist
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 1: Checking for existing KNW-001-010 records..." -ForegroundColor Yellow

$result = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_externalid&`$filter=sprk_externalid ne null"
if ($result.Success) {
    $existing = $result.Data.value | Where-Object { $_.sprk_externalid -like "KNW-*" }
    if ($existing.Count -eq 0) {
        Write-Host "  (none — safe to create all 10)" -ForegroundColor Green
    } else {
        Write-Host "  Existing KNW records:" -ForegroundColor Yellow
        foreach ($rec in $existing) {
            Write-Host "    - $($rec.sprk_externalid) : $($rec.sprk_name)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Warning "  Could not query existing records: $($result.Error)"
}

# ---------------------------------------------------------------------------
# Step 2: Load content from markdown files
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 2: Loading content from markdown files in:" -ForegroundColor Yellow
Write-Host "  $ContentFilePath" -ForegroundColor Yellow

$contentFiles = @{
    "KNW-001" = Join-Path $ContentFilePath "KNW-001-contract-terms-glossary.md"
    "KNW-002" = Join-Path $ContentFilePath "KNW-002-nda-checklist.md"
    "KNW-003" = Join-Path $ContentFilePath "KNW-003-lease-agreement-standards.md"
    "KNW-004" = Join-Path $ContentFilePath "KNW-004-invoice-processing-guide.md"
    "KNW-005" = Join-Path $ContentFilePath "KNW-005-sla-metrics-reference.md"
    "KNW-006" = Join-Path $ContentFilePath "KNW-006-employment-law-quick-reference.md"
    "KNW-007" = Join-Path $ContentFilePath "KNW-007-ip-assignment-clause-library.md"
    "KNW-008" = Join-Path $ContentFilePath "KNW-008-termination-and-remedy-provisions.md"
    "KNW-009" = Join-Path $ContentFilePath "KNW-009-governing-law-and-jurisdiction-guide.md"
    "KNW-010" = Join-Path $ContentFilePath "KNW-010-legal-red-flags-catalog.md"
}

$loadedContent = @{}
foreach ($key in $contentFiles.Keys) {
    $content = Read-ContentFile -FilePath $contentFiles[$key]
    if ($content) {
        $loadedContent[$key] = $content
        $wordCount = ($content -split '\s+').Count
        Write-Host "  Loaded $key ($wordCount words)" -ForegroundColor Green
    } else {
        Write-Warning "  Failed to load content for $key"
    }
}

# ---------------------------------------------------------------------------
# Step 3: Define knowledge source metadata
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 3: Creating knowledge source records..." -ForegroundColor Yellow

$knowledgeSources = @(
    @{
        sprk_externalid  = "KNW-001"
        sprk_name        = "Common Contract Terms Glossary"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-002"
        sprk_name        = "NDA Review Checklist"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-003"
        sprk_name        = "Lease Agreement Standards"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-004"
        sprk_name        = "Invoice Processing Guide"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-005"
        sprk_name        = "SLA Metrics Reference"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-006"
        sprk_name        = "Employment Law Quick Reference"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-007"
        sprk_name        = "IP Assignment Clause Library"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-008"
        sprk_name        = "Termination and Remedy Provisions"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-009"
        sprk_name        = "Governing Law and Jurisdiction Guide"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
    @{
        sprk_externalid  = "KNW-010"
        sprk_name        = "Legal Document Red Flags Catalog"
        sprk_contenttype = "Reference"
        sprk_isactive    = $true
        sprk_issystem    = $true
        sprk_tenantid    = "system"
    }
)

$created = 0
$skipped = 0
$failed  = 0

foreach ($ks in $knowledgeSources) {
    $externalId = $ks.sprk_externalid
    Write-Host "  Creating $externalId : $($ks.sprk_name)..." -NoNewline

    # Check if this specific external ID already exists
    $checkResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_externalid='$externalId')?`$select=sprk_name,sprk_externalid"
    if ($checkResult.Success) {
        Write-Host " [SKIP] already exists" -ForegroundColor Yellow
        $skipped++
        continue
    }

    # Ensure content was loaded
    if (-not $loadedContent.ContainsKey($externalId)) {
        Write-Host " [FAIL] content not loaded" -ForegroundColor Red
        $failed++
        continue
    }

    $body = @{
        sprk_name        = $ks.sprk_name
        sprk_externalid  = $externalId
        sprk_contenttext = $loadedContent[$externalId]
        sprk_contenttype = $ks.sprk_contenttype
        sprk_isactive    = $ks.sprk_isactive
        sprk_issystem    = $ks.sprk_issystem
        sprk_tenantid    = $ks.sprk_tenantid
    }

    $postResult = Invoke-DataverseApi -Endpoint $Collection -Method "POST" -Body $body
    if ($postResult.Success) {
        Write-Host " [OK]" -ForegroundColor Green
        $created++
    } else {
        Write-Host " [FAIL]" -ForegroundColor Red
        Write-Warning "    Error: $($postResult.Error)"
        $failed++
    }
}

Write-Host ""
Write-Host "  Created: $created  Skipped: $skipped  Failed: $failed  Total: $($created + $skipped)/$($knowledgeSources.Count)" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Step 4: Verify records and alternate key lookup
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 4: Verifying created records..." -ForegroundColor Yellow

$verifyResult = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_externalid,sprk_contenttype,sprk_isactive,sprk_issystem,sprk_tenantid&`$filter=sprk_externalid ne null&`$orderby=sprk_externalid asc"
if ($verifyResult.Success) {
    $records = $verifyResult.Data.value | Where-Object { $_.sprk_externalid -like "KNW-*" }
    Write-Host "  KNW records in Dataverse: $($records.Count)" -ForegroundColor Green
    foreach ($rec in $records) {
        Write-Host "    $($rec.sprk_externalid) | $($rec.sprk_name) | type=$($rec.sprk_contenttype) | active=$($rec.sprk_isactive) | system=$($rec.sprk_issystem) | tenant=$($rec.sprk_tenantid)" -ForegroundColor Green
    }
} else {
    Write-Warning "  Verification query failed: $($verifyResult.Error)"
}

Write-Host ""
Write-Host "  Testing alternate key lookup: KNW-001..." -NoNewline
$lookupResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_externalid='KNW-001')?`$select=sprk_name,sprk_externalid,sprk_tenantid"
if ($lookupResult.Success) {
    Write-Host " [OK] $($lookupResult.Data.sprk_externalid) : $($lookupResult.Data.sprk_name) (tenant: $($lookupResult.Data.sprk_tenantid))" -ForegroundColor Green
} else {
    Write-Host " [FAIL]" -ForegroundColor Red
    Write-Warning "  Alternate key lookup failed: $($lookupResult.Error)"
}

# ---------------------------------------------------------------------------
# Step 5: AI Search indexing reminder
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  AIPL-032 Dataverse records complete." -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  NEXT STEP: Index content into AI Search (post-AIPL-018 deployment)" -ForegroundColor Yellow
Write-Host ""
Write-Host "  After KnowledgeBaseEndpoints.cs is deployed, call:" -ForegroundColor Yellow
Write-Host ""
Write-Host "    POST $($BaseUrl.Replace('/api/data/v9.2',''))/api/ai/knowledge/index/batch" -ForegroundColor White
Write-Host "    Body: { `"tenantId`": `"system`", `"filter`": `"system`" }" -ForegroundColor White
Write-Host ""
Write-Host "  Verify indexing via test-search:" -ForegroundColor Yellow
Write-Host "    GET /api/ai/knowledge/test-search?query=force+majeure&tenantId=system&topK=3" -ForegroundColor White
Write-Host ""
Write-Host "  Documentation: projects/ai-spaarke-platform-enhancements-r1/notes/design/scope-library-catalog.md"
Write-Host ""
