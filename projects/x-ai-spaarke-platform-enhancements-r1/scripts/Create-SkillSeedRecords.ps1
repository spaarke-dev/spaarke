<#
.SYNOPSIS
    Creates 10 system Skill records in the sprk_analysisskill Dataverse entity.

.DESCRIPTION
    Creates SKL-001 through SKL-010 in sprk_analysisskills.
    Each record has a production-quality prompt fragment in sprk_promptfragment.
    Uses sprk_skillcode as the alternate key (resolved by SkillLookupService.GetByCodeAsync).

    Prerequisites:
    - Azure CLI installed and authenticated: az login
    - Access to spaarkedev1.crm.dynamics.com (System Customizer or System Administrator)
    - PowerShell 7+

    This script is IDEMPOTENT — skips records that already have a matching sprk_skillcode.

.NOTES
    Task:    AIPL-031
    Entity:  sprk_analysisskill (collection: sprk_analysisskills)
    Created: 2026-02-23
#>

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

$BaseUrl  = "https://$Environment/api/data/v9.2"
$Collection = "sprk_analysisskills"

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Create-SkillSeedRecords.ps1" -ForegroundColor Cyan
Write-Host "  Target: $Environment" -ForegroundColor Cyan
Write-Host "  Task:   AIPL-031" -ForegroundColor Cyan
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
# Step 1: Verify no SKL-001-010 records exist
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 1: Checking for existing SKL-001-010 records..." -ForegroundColor Yellow

$result = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_skillcode&`$filter=sprk_skillcode ne null"
if ($result.Success) {
    $existing = $result.Data.value
    if ($existing.Count -eq 0) {
        Write-Host "  (none — safe to create all 10)" -ForegroundColor Green
    } else {
        Write-Host "  Existing records with sprk_skillcode:" -ForegroundColor Yellow
        foreach ($rec in $existing) {
            Write-Host "    - $($rec.sprk_skillcode) : $($rec.sprk_name)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Warning "  Could not query existing records: $($result.Error)"
}

# ---------------------------------------------------------------------------
# Step 2 & 3: Create 10 skill records
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 3: Creating skill records..." -ForegroundColor Yellow

$skills = @(
    @{
        sprk_skillcode       = "SKL-001"
        sprk_name            = "Citation Extraction"
        sprk_description     = "Inject citation instructions — every claim is followed by [Section: X, Page Y] references"
        sprk_promptfragment  = @"
For each claim, finding, or factual statement in your analysis, provide a citation in the format [Section: <section-name>, Page <N>]. If the page or section is unavailable, use [Document Reference]. Citations must immediately follow the referenced statement — do not group them at the end. If a fact appears in multiple locations, cite the primary occurrence. When quoting directly, use quotation marks and include the citation. All citations should be verifiable by a reader reviewing the original document.
"@
    }
    @{
        sprk_skillcode       = "SKL-002"
        sprk_name            = "Risk Flagging"
        sprk_description     = "Highlight clauses requiring legal review with [RISK: HIGH/MEDIUM/LOW] prefixes"
        sprk_promptfragment  = @"
As you analyze the document, explicitly flag clauses or provisions that require legal review. Use the prefix [RISK: HIGH], [RISK: MEDIUM], or [RISK: LOW] for each flagged item, followed by a brief explanation of the concern. Common risk indicators: unlimited liability, uncapped indemnification, automatic renewal without notification, unilateral modification rights, non-compete or exclusivity terms, IP assignment clauses, and unfavorable dispute resolution terms. Present all flagged items together in a dedicated 'Risk Summary' section.
"@
    }
    @{
        sprk_skillcode       = "SKL-003"
        sprk_name            = "Summary Generation"
        sprk_description     = "Generate an executive summary of 3-5 key bullet points at the start of the response"
        sprk_promptfragment  = @"
After completing your analysis, generate an executive summary consisting of exactly 3 to 5 bullet points. Each bullet should represent a key finding, obligation, or risk from the document. Lead each bullet with a bold topic label (e.g., **Term**, **Payment**, **Obligations**). The summary must be comprehensible to a non-legal reader in under 30 seconds. Place the executive summary at the beginning of your response under the heading '## Executive Summary'.
"@
    }
    @{
        sprk_skillcode       = "SKL-004"
        sprk_name            = "Date Extraction"
        sprk_description     = "Extract and normalize all dates to ISO 8601 format in a structured Key Dates table"
        sprk_promptfragment  = @"
Extract every date mentioned in the document and normalize it to ISO 8601 format (YYYY-MM-DD). For each date provide: the normalized date, the original text, and the context (e.g., effective date, expiry, payment due, notice period deadline). Organize dates in a table under the heading '## Key Dates' with columns: Normalized Date | Original Text | Context. Relative dates (e.g., '30 days after signing') should include the relative expression and, where a base date is determinable, a calculated ISO date. Flag any ambiguous or unresolvable date formats.
"@
    }
    @{
        sprk_skillcode       = "SKL-005"
        sprk_name            = "Party Identification"
        sprk_description     = "Identify all parties with full legal names, roles, shortened forms, and any stated contact details"
        sprk_promptfragment  = @"
Identify all parties named in this document. For each party provide: their full legal name as stated, any shortened form or defined term used, their role (e.g., Buyer, Seller, Licensor, Service Provider), and any contact information or jurisdiction of incorporation if stated. Present the party list under the heading '## Identified Parties'. Note any discrepancies between how a party is named in the preamble versus the signature block. Flag any party whose legal name or role is ambiguous.
"@
    }
    @{
        sprk_skillcode       = "SKL-006"
        sprk_name            = "Obligation Mapping"
        sprk_description     = "Map all mutual obligations into a structured table showing party, obligation, condition, and deadline"
        sprk_promptfragment  = @"
Identify all obligations imposed by this document. For each obligation determine: the obligated party, the beneficiary, the specific action required, and any conditions or deadlines. Present obligations in a structured table under '## Obligation Map' with columns: Party | Obligation | Condition | Deadline. Distinguish between mandatory obligations (must/shall) and discretionary ones (may/should). Include obligations for all parties — vendor, client, and any referenced third parties.
"@
    }
    @{
        sprk_skillcode       = "SKL-007"
        sprk_name            = "Defined Terms"
        sprk_description     = "Extract all defined terms and their definitions into an alphabetical glossary"
        sprk_promptfragment  = @"
Extract all defined terms from this document. Defined terms are words or phrases assigned a specific meaning, typically appearing in quotation marks upon first definition or in a dedicated 'Definitions' section. For each term provide: the term, its definition as stated, and the section where it is defined. Present terms alphabetically under '## Defined Terms Glossary'. Note any terms that are used throughout the document but never formally defined, flagging them as [UNDEFINED TERM].
"@
    }
    @{
        sprk_skillcode       = "SKL-008"
        sprk_name            = "Financial Terms"
        sprk_description     = "Extract all monetary amounts, payment schedules, rates, and financial obligations"
        sprk_promptfragment  = @"
Extract all financial and monetary provisions from this document. For each item include: the amount or rate, the currency, the payment schedule or trigger event, and the responsible party. Categories to cover: fees, payment terms, late-payment penalties, rate escalation, cap amounts, minimums, and financial adjustments. Present under '## Financial Terms Summary'. Flag provisions where the obligation is open-ended, uncapped, or dependent on undefined metrics, as these represent elevated financial risk.
"@
    }
    @{
        sprk_skillcode       = "SKL-009"
        sprk_name            = "Termination Analysis"
        sprk_description     = "Analyze all termination triggers, notice periods, cure periods, and post-termination consequences"
        sprk_promptfragment  = @"
Analyze all termination provisions in this document. Identify: the grounds for termination (cause vs. convenience), the required notice period for each type, any cure periods before termination becomes effective, each party's obligations upon termination (data return, outstanding payments, survival of provisions), and any post-termination restrictions. Present under '## Termination Analysis'. Flag provisions that allow termination without cause or with minimal notice, as these represent elevated contractual risk.
"@
    }
    @{
        sprk_skillcode       = "SKL-010"
        sprk_name            = "Jurisdiction and Governing Law"
        sprk_description     = "Identify applicable law, jurisdiction, dispute resolution mechanism, and arbitration body"
        sprk_promptfragment  = @"
Identify all governing law, jurisdiction, and dispute resolution provisions. For each provision state: the governing law (e.g., 'Laws of the State of New York'), the jurisdiction for legal proceedings, whether jurisdiction is exclusive or non-exclusive, the dispute resolution mechanism (litigation, arbitration, mediation), any named arbitration body and its rules, and the prevailing-party standard for legal fees. Present under '## Governing Law and Dispute Resolution'. Flag provisions creating asymmetric rights between the parties.
"@
    }
)

$created = 0
$skipped = 0

foreach ($skill in $skills) {
    $code = $skill.sprk_skillcode
    Write-Host "  Creating $code : $($skill.sprk_name)..." -NoNewline

    # Check if this specific code already exists
    $checkResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_skillcode='$code')?\`$select=sprk_name"
    if ($checkResult.Success) {
        Write-Host " [SKIP] already exists" -ForegroundColor Yellow
        $skipped++
        continue
    }

    $body = @{
        sprk_name           = $skill.sprk_name
        sprk_description    = $skill.sprk_description
        sprk_promptfragment = $skill.sprk_promptfragment.Trim()
        sprk_skillcode      = $code
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
Write-Host "  Created: $created  Skipped: $skipped  Total: $($created + $skipped)/10" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Step 4: Verify records and alternate key lookup
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Step 4: Verifying created records..." -ForegroundColor Yellow

$verifyResult = Invoke-DataverseApi -Endpoint "${Collection}?`$select=sprk_name,sprk_skillcode&`$filter=sprk_skillcode ne null&`$orderby=sprk_skillcode asc"
if ($verifyResult.Success) {
    $records = $verifyResult.Data.value
    Write-Host "  Records with sprk_skillcode set: $($records.Count)" -ForegroundColor Green
    foreach ($rec in $records) {
        Write-Host "    $($rec.sprk_skillcode) | $($rec.sprk_name)" -ForegroundColor Green
    }
} else {
    Write-Warning "  Verification query failed: $($verifyResult.Error)"
}

Write-Host ""
Write-Host "  Testing alternate key lookup: SKL-001..." -NoNewline
$lookupResult = Invoke-DataverseApi -Endpoint "${Collection}(sprk_skillcode='SKL-001')?`$select=sprk_name,sprk_skillcode"
if ($lookupResult.Success) {
    Write-Host " [OK] $($lookupResult.Data.sprk_skillcode) : $($lookupResult.Data.sprk_name)" -ForegroundColor Green
} else {
    Write-Host " [FAIL]" -ForegroundColor Red
    Write-Warning "  Alternate key lookup failed: $($lookupResult.Error)"
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  AIPL-031 complete." -ForegroundColor Cyan
Write-Host "  Run Get-SkillSeedRecords to verify catalog." -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Documentation: projects/ai-spaarke-platform-enhancements-r1/notes/design/scope-library-catalog.md"
