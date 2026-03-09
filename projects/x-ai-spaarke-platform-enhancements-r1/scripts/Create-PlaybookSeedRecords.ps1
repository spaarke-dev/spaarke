<#
.SYNOPSIS
    Creates 10 system Playbook records in the sprk_aiplaybook Dataverse entity.

.DESCRIPTION
    Creates PB-001 through PB-010 in sprk_aiplaybooks.
    Each record composes Actions, Skills, Knowledge Sources, and Tools into a complete
    analysis workflow via a canvas JSON stored in sprk_canvasjson.
    Uses sprk_externalid as the alternate key for idempotent upsert.

    Prerequisites:
    - Azure CLI installed and authenticated: az login
    - Access to spaarkedev1.crm.dynamics.com (System Customizer or System Administrator)
    - PowerShell 7+

    This script is IDEMPOTENT — skips records that already have a matching sprk_externalid.

    Scope compositions per playbook:
      PB-001: Standard Contract Review     — ACT-001 + SKL-001,002,003    + KNW-001       + TL-001,002
      PB-002: NDA Deep Review              — ACT-002 + SKL-001,004,005    + KNW-002       + TL-001,002,007
      PB-003: Commercial Lease Analysis    — ACT-003 + SKL-003,006        + KNW-003       + TL-001,002
      PB-004: Invoice Validation           — ACT-004 + SKL-004,008        + KNW-004       + TL-001,003
      PB-005: SLA Compliance Review        — ACT-005 + SKL-002,006        + KNW-005       + TL-001,002,007
      PB-006: Employment Agreement Review  — ACT-006 + SKL-005,007,009    + KNW-006,007   + TL-001,002
      PB-007: Statement of Work Analysis   — ACT-007 + SKL-003,006        + KNW-005       + TL-001,002
      PB-008: IP Assignment Review         — ACT-001 + SKL-007,009        + KNW-007       + TL-001,002,008
      PB-009: Termination Risk Assessment  — ACT-001 + SKL-002,009        + KNW-008       + TL-001,007
      PB-010: Quick Legal Scan             — ACT-008 + SKL-002,010        + KNW-010       + TL-001,007

.NOTES
    Task:    AIPL-034
    Entity:  sprk_aiplaybook (collection: sprk_aiplaybooks)
    Created: 2026-02-23

    ADR Compliance:
    - ADR-002: Records created via data import/script — no plugin processing
    - Canvas JSON uses external code references (ACT-*, SKL-*, KNW-*, TL-*) — resolved at runtime
      by PlaybookScopeResolver. Not Dataverse record GUIDs — those are environment-specific.
#>

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

$BaseUrl    = "https://$Environment/api/data/v9.2"
$Collection = "sprk_analysisplaybooks"

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Create-PlaybookSeedRecords.ps1" -ForegroundColor Cyan
Write-Host "  Target: $Environment" -ForegroundColor Cyan
Write-Host "  Task:   AIPL-034" -ForegroundColor Cyan
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
# Step 1: Verify no PB-001-010 records exist
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 1: Checking for existing PB-001-010 records..." -ForegroundColor Yellow

$result = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_externalid&`$filter=sprk_externalid ne null"
if ($result.Success) {
    $existing = $result.Data.value | Where-Object { $_.sprk_externalid -like "PB-*" }
    if ($existing.Count -eq 0) {
        Write-Host "  (none — safe to create all 10)" -ForegroundColor Green
    } else {
        Write-Host "  Existing PB records:" -ForegroundColor Yellow
        foreach ($rec in $existing) {
            Write-Host "    - $($rec.sprk_externalid) : $($rec.sprk_name)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Warning "  Could not query existing records: $($result.Error)"
}

# ---------------------------------------------------------------------------
# Step 2 & 3: Create 10 playbook records
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 3: Creating playbook records..." -ForegroundColor Yellow

# Canvas JSON uses external code references (ACT-*, SKL-*, KNW-*, TL-*).
# The PlaybookScopeResolver in the BFF resolves these codes to Dataverse record IDs
# at playbook execution time via alternate key lookups, making canvas JSON
# environment-portable (same JSON works across dev/test/prod).

$playbooks = @(
    @{
        sprk_externalid          = "PB-001"
        sprk_name                = "Standard Contract Review"
        sprk_description         = "Comprehensive review of commercial contracts, MSAs, and vendor agreements. Extracts parties, obligations, key dates, and identifies risk clauses. Generates executive summary with citation support."
        sprk_targetdocumenttype  = "Contract"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-001","skillIds":["SKL-001","SKL-002","SKL-003"],"knowledgeSourceIds":["KNW-001"],"toolIds":["TL-001","TL-002"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-002"
        sprk_name                = "NDA Deep Review"
        sprk_description         = "Deep review of non-disclosure and confidentiality agreements. Identifies all parties, extracts key dates, flags unbalanced obligations and unfavorable residuals clauses. Includes red-flag risk detection."
        sprk_targetdocumenttype  = "NDA"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-002","skillIds":["SKL-001","SKL-004","SKL-005"],"knowledgeSourceIds":["KNW-002"],"toolIds":["TL-001","TL-002","TL-007"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-003"
        sprk_name                = "Commercial Lease Analysis"
        sprk_description         = "Analysis of commercial and residential lease agreements. Generates executive summary, maps landlord and tenant obligations, and identifies lease terms, renewal options, and maintenance responsibilities."
        sprk_targetdocumenttype  = "Lease"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-003","skillIds":["SKL-003","SKL-006"],"knowledgeSourceIds":["KNW-003"],"toolIds":["TL-001","TL-002"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-004"
        sprk_name                = "Invoice Validation"
        sprk_description         = "Validation of vendor invoices and AP documents. Extracts key dates and payment terms, identifies financial amounts and schedules, and cross-references against company knowledge sources for compliance."
        sprk_targetdocumenttype  = "Invoice"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-004","skillIds":["SKL-004","SKL-008"],"knowledgeSourceIds":["KNW-004"],"toolIds":["TL-001","TL-003"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-005"
        sprk_name                = "SLA Compliance Review"
        sprk_description         = "Review of service level agreements and managed service contracts. Flags risk clauses around SLO commitments and credit provisions, maps vendor and client obligations, and retrieves relevant SLA benchmarks."
        sprk_targetdocumenttype  = "SLA"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-005","skillIds":["SKL-002","SKL-006"],"knowledgeSourceIds":["KNW-005"],"toolIds":["TL-001","TL-002","TL-007"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-006"
        sprk_name                = "Employment Agreement Review"
        sprk_description         = "Review of employment contracts, offer letters, and contractor agreements. Identifies all parties, extracts defined terms, analyzes termination provisions, and maps obligations for both employer and employee."
        sprk_targetdocumenttype  = "EmploymentAgreement"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-006","skillIds":["SKL-005","SKL-007","SKL-009"],"knowledgeSourceIds":["KNW-006","KNW-007"],"toolIds":["TL-001","TL-002"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-007"
        sprk_name                = "Statement of Work Analysis"
        sprk_description         = "Analysis of statements of work, task orders, and project agreements. Generates executive summary of deliverables and milestones, maps mutual obligations, and retrieves relevant SLA and performance benchmarks."
        sprk_targetdocumenttype  = "StatementOfWork"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-007","skillIds":["SKL-003","SKL-006"],"knowledgeSourceIds":["KNW-005"],"toolIds":["TL-001","TL-002"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-008"
        sprk_name                = "IP Assignment Review"
        sprk_description         = "Review of intellectual property assignment clauses in contracts and employment agreements. Extracts and glossarizes all defined IP terms, analyzes termination and reversion rights, and detects overbroad assignment language."
        sprk_targetdocumenttype  = "Contract"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-001","skillIds":["SKL-007","SKL-009"],"knowledgeSourceIds":["KNW-007"],"toolIds":["TL-001","TL-002","TL-008"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-009"
        sprk_name                = "Termination Risk Assessment"
        sprk_description         = "Focused assessment of termination risk in contracts. Flags high-severity risk clauses, provides full termination analysis of triggers and cure periods, and retrieves reference provisions on termination and remedy standards."
        sprk_targetdocumenttype  = "Contract"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-001","skillIds":["SKL-002","SKL-009"],"knowledgeSourceIds":["KNW-008"],"toolIds":["TL-001","TL-007"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
    @{
        sprk_externalid          = "PB-010"
        sprk_name                = "Quick Legal Scan"
        sprk_description         = "Fast risk-focused scan of any legal document. Flags high-priority risk clauses and identifies applicable law and jurisdiction. Optimized for speed — use when a full review is not required but red flags must be surfaced quickly."
        sprk_targetdocumenttype  = "LegalDocument"
        sprk_isactive            = $true
        sprk_issystem            = $true
        sprk_canvasjson          = '{"actionId":"ACT-008","skillIds":["SKL-002","SKL-010"],"knowledgeSourceIds":["KNW-010"],"toolIds":["TL-001","TL-007"],"configuration":{"temperature":0.3,"maxTokens":2000,"streaming":true}}'
    }
)

$created = 0
$skipped = 0
$failed  = 0

foreach ($playbook in $playbooks) {
    $externalId = $playbook.sprk_externalid
    Write-Host "  Creating $externalId : $($playbook.sprk_name)..." -NoNewline

    # Check if this specific external ID already exists
    $checkResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_externalid='$externalId')?`$select=sprk_name,sprk_externalid"
    if ($checkResult.Success) {
        Write-Host " [SKIP] already exists" -ForegroundColor Yellow
        $skipped++
        continue
    }

    $body = @{
        sprk_name               = $playbook.sprk_name
        sprk_externalid         = $externalId
        sprk_description        = $playbook.sprk_description
        sprk_targetdocumenttype = $playbook.sprk_targetdocumenttype
        sprk_canvasjson         = $playbook.sprk_canvasjson
        sprk_isactive           = $playbook.sprk_isactive
        sprk_issystem           = $playbook.sprk_issystem
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
Write-Host "  Created: $created  Skipped: $skipped  Failed: $failed  Total: $($created + $skipped)/$($playbooks.Count)" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Step 4: Verify records and alternate key lookup
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 4: Verifying created records..." -ForegroundColor Yellow

$verifyResult = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_externalid,sprk_targetdocumenttype,sprk_isactive,sprk_issystem&`$filter=sprk_externalid ne null&`$orderby=sprk_externalid asc"
if ($verifyResult.Success) {
    $records = $verifyResult.Data.value | Where-Object { $_.sprk_externalid -like "PB-*" }
    Write-Host "  PB records in Dataverse: $($records.Count)" -ForegroundColor Green
    foreach ($rec in $records) {
        Write-Host "    $($rec.sprk_externalid) | $($rec.sprk_name) | doctype=$($rec.sprk_targetdocumenttype) | active=$($rec.sprk_isactive) | system=$($rec.sprk_issystem)" -ForegroundColor Green
    }
} else {
    Write-Warning "  Verification query failed: $($verifyResult.Error)"
}

Write-Host ""
Write-Host "  Testing alternate key lookup: PB-001..." -NoNewline
$lookupResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_externalid='PB-001')?`$select=sprk_name,sprk_externalid,sprk_canvasjson"
if ($lookupResult.Success) {
    Write-Host " [OK] $($lookupResult.Data.sprk_externalid) : $($lookupResult.Data.sprk_name)" -ForegroundColor Green
    Write-Host "    Canvas JSON: $($lookupResult.Data.sprk_canvasjson)" -ForegroundColor DarkGray
} else {
    Write-Host " [FAIL]" -ForegroundColor Red
    Write-Warning "  Alternate key lookup failed: $($lookupResult.Error)"
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  AIPL-034 complete." -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  NOTE: Canvas JSON references scope codes (ACT-*, SKL-*, KNW-*, TL-*)." -ForegroundColor Yellow
Write-Host "  PlaybookScopeResolver resolves these to Dataverse record IDs at runtime." -ForegroundColor Yellow
Write-Host "  Prerequisites: AIPL-030 (Actions), AIPL-031 (Skills), AIPL-032 (Knowledge)," -ForegroundColor Yellow
Write-Host "  and AIPL-033 (Tools) must have been run to create the referenced scope records." -ForegroundColor Yellow
Write-Host ""
Write-Host "  NOTE: AnalysisWorkspace playbook dropdown verification requires AIPL-056" -ForegroundColor Yellow
Write-Host "  (SprkChat integration) and AIPL-037 (Workstream B deployment) to be complete." -ForegroundColor Yellow
Write-Host ""
Write-Host "  Documentation: projects/ai-spaarke-platform-enhancements-r1/notes/design/scope-library-catalog.md"
Write-Host ""
