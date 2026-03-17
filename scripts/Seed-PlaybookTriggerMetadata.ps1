<#
.SYNOPSIS
    Seeds trigger metadata fields on existing sprk_analysisplaybook records.

.DESCRIPTION
    Queries all sprk_analysisplaybook records from Dataverse and populates the
    four trigger metadata fields (added by Create-PlaybookTriggerFields.ps1):
    - sprk_triggerphrases: newline-delimited natural language phrases for semantic matching
    - sprk_recordtype: record type this playbook applies to (e.g., "matter", "project")
    - sprk_entitytype: Dataverse entity logical name (e.g., "sprk_analysisoutput")
    - sprk_tags: comma-delimited classification tags

    The script is idempotent: it only updates fields that are null or empty.
    Running it a second time produces zero updates if all fields are already populated.

    This script is part of the standard playbook deployment workflow. When a new
    playbook is created, run this script to populate its trigger metadata before
    the playbook embedding pipeline (task R2-016) generates embeddings.

    Authentication uses Azure CLI (az account get-access-token).

.PARAMETER EnvironmentUrl
    The Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER DryRun
    Preview all planned updates without making any PATCH calls.

.PARAMETER Force
    Overwrite existing (non-empty) field values. Use with caution.

.EXAMPLE
    # Dry run — preview what would be seeded
    .\Seed-PlaybookTriggerMetadata.ps1 -DryRun

.EXAMPLE
    # Seed dev environment
    .\Seed-PlaybookTriggerMetadata.ps1

.EXAMPLE
    # Force overwrite all trigger metadata
    .\Seed-PlaybookTriggerMetadata.ps1 -Force

.NOTES
    Project: SprkChat Platform Enhancement R2
    Task: 005 - Seed Playbook Trigger Metadata
    Created: 2026-03-17

    Prerequisites:
      - Azure CLI installed and authenticated (az login)
      - Create-PlaybookTriggerFields.ps1 must have been run first (R2-001)
      - PowerShell Core 7+ recommended

    Entities:
      sprk_analysisplaybooks — Playbook records (entity set name)
      sprk_analysisplaybook  — Playbook entity logical name

    Fields updated:
      sprk_triggerphrases    — Multiline text, max 4000 chars
      sprk_recordtype        — Single line text, max 100 chars
      sprk_entitytype        — Single line text, max 100 chars
      sprk_tags              — Multiline text, max 2000 chars
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [switch]$DryRun,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

# ============================================================================
# Seed Data — Trigger metadata for known playbooks
# ============================================================================
# Keys are playbook names (sprk_name) as they appear in Dataverse.
# Values contain the four trigger metadata fields.
# Trigger phrases should be diverse and represent how real users would phrase
# requests in a chat interface (lawyers, paralegals, project managers).

$SeedData = @{

    "Quick Document Review" = @{
        triggerPhrases = @(
            "review this document"
            "give me a quick review"
            "what does this document say"
            "scan this document for me"
            "do a quick review of this file"
            "summarize and review this document"
            "I need a quick document review"
            "can you look over this document"
            "tell me what this document is about"
            "quick scan of the uploaded file"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "document,review,quick,triage,general"
    }

    "Full Contract Analysis" = @{
        triggerPhrases = @(
            "analyze this contract"
            "do a full contract review"
            "review the contract in detail"
            "I need a thorough contract analysis"
            "break down this contract for me"
            "what are the key terms in this contract"
            "identify risks in this contract"
            "give me a comprehensive contract review"
            "review the agreement and flag issues"
            "analyze the clauses in this contract"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "contract,analysis,legal,comprehensive,clauses,risk"
    }

    "NDA Review" = @{
        triggerPhrases = @(
            "review this NDA"
            "analyze the non-disclosure agreement"
            "check this NDA for issues"
            "is this NDA standard"
            "review the confidentiality agreement"
            "what are the key terms in this NDA"
            "flag risks in this NDA"
            "I need an NDA review"
            "look over this non-disclosure agreement"
            "check the confidentiality terms"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "nda,confidentiality,legal,review,privacy"
    }

    "Lease Review" = @{
        triggerPhrases = @(
            "review this lease"
            "analyze the lease agreement"
            "what are the lease terms"
            "check this commercial lease"
            "review the rental agreement"
            "I need a lease review"
            "identify issues in this lease"
            "break down the lease terms"
            "review the office lease agreement"
            "analyze the landlord tenant obligations"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "lease,real-estate,commercial,legal,review,property"
    }

    "Employment Contract" = @{
        triggerPhrases = @(
            "review this employment agreement"
            "analyze the employment contract"
            "check the offer letter terms"
            "review the contractor agreement"
            "what are the compensation terms"
            "check the non-compete clause"
            "I need an employment contract review"
            "review the IP assignment provisions"
            "analyze the severance terms"
            "look over this hiring agreement"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "employment,HR,contract,legal,compensation,non-compete"
    }

    "Invoice Validation" = @{
        triggerPhrases = @(
            "validate this invoice"
            "check the invoice details"
            "process this invoice"
            "review the vendor invoice"
            "verify the invoice amounts"
            "I need invoice validation"
            "check this bill for errors"
            "review the payment terms on this invoice"
            "validate the line items"
            "is this invoice correct"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "invoice,financial,validation,accounts-payable,vendor"
    }

    "SLA Analysis" = @{
        triggerPhrases = @(
            "analyze this SLA"
            "review the service level agreement"
            "check the SLA terms"
            "what are the SLA commitments"
            "review the uptime guarantees"
            "I need an SLA analysis"
            "check the service credits"
            "review the performance metrics in this SLA"
            "analyze the service level objectives"
            "what happens if the SLA is breached"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "sla,service-level,compliance,legal,metrics,availability"
    }

    "Due Diligence Review" = @{
        triggerPhrases = @(
            "run due diligence on this document"
            "I need a due diligence review"
            "check this for due diligence"
            "perform a due diligence analysis"
            "review this document for due diligence purposes"
            "classify and assess this document"
            "what risks does this document present"
            "due diligence scan of this file"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "due-diligence,risk,review,compliance,assessment"
    }

    "Compliance Review" = @{
        triggerPhrases = @(
            "check this for compliance"
            "review compliance of this document"
            "is this document compliant"
            "run a compliance check"
            "review the policy compliance"
            "I need a compliance review"
            "check this contract against our policies"
            "analyze regulatory compliance"
            "flag compliance issues in this document"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "compliance,policy,regulatory,risk,review,governance"
    }

    "Risk-Focused Scan" = @{
        triggerPhrases = @(
            "scan this for risks"
            "what are the red flags"
            "do a risk scan"
            "identify risks in this document"
            "flag problematic clauses"
            "I need a risk assessment"
            "check for red flags in this contract"
            "quick risk scan of this file"
            "highlight the risky terms"
            "what should I be worried about in this document"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "risk,scan,red-flags,quick,legal,compliance"
    }

    # --- Scope-model compositions (if deployed with different names) ---

    "Standard Contract Review" = @{
        triggerPhrases = @(
            "review this contract"
            "analyze the agreement"
            "standard contract review"
            "check this contract for issues"
            "I need a contract reviewed"
            "look over this agreement"
            "what does this contract say"
            "review the terms and conditions"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "contract,review,standard,legal,terms"
    }

    "NDA Deep Review" = @{
        triggerPhrases = @(
            "deep review of this NDA"
            "thorough NDA analysis"
            "detailed NDA review"
            "review every section of this NDA"
            "comprehensive confidentiality review"
            "I need a detailed NDA analysis"
            "deep dive into this non-disclosure agreement"
            "analyze all NDA provisions"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "nda,deep-review,confidentiality,legal,detailed"
    }

    "Commercial Lease Analysis" = @{
        triggerPhrases = @(
            "analyze this commercial lease"
            "review the commercial lease terms"
            "check the NNN lease"
            "review this office lease"
            "what are the tenant obligations"
            "analyze the rent escalation terms"
            "commercial lease analysis"
            "review the retail lease agreement"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "lease,commercial,real-estate,analysis,NNN,legal"
    }

    "SLA Compliance Review" = @{
        triggerPhrases = @(
            "check SLA compliance"
            "review SLA compliance terms"
            "are we meeting the SLA"
            "SLA compliance review"
            "check the service level compliance"
            "review the SLA obligations"
            "analyze SLA compliance risks"
            "verify SLA adherence"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "sla,compliance,service-level,review,obligations"
    }

    "Employment Agreement Review" = @{
        triggerPhrases = @(
            "review the employment agreement"
            "analyze this employment contract"
            "check the employment terms"
            "review the offer letter"
            "I need an employment agreement reviewed"
            "what are the employment conditions"
            "review the non-compete and non-solicit"
            "analyze the IP assignment clause"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "employment,agreement,legal,HR,review,contract"
    }

    "Statement of Work Analysis" = @{
        triggerPhrases = @(
            "review this SOW"
            "analyze the statement of work"
            "check the deliverables in this SOW"
            "review the work order"
            "I need a SOW analysis"
            "what are the milestones in this SOW"
            "review the acceptance criteria"
            "analyze the project scope and fees"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "sow,statement-of-work,project,deliverables,legal"
    }

    "IP Assignment Review" = @{
        triggerPhrases = @(
            "review the IP assignment"
            "analyze intellectual property terms"
            "check the IP provisions"
            "review the work product assignment"
            "I need an IP assignment review"
            "check the background IP clauses"
            "analyze the invention assignment"
            "review the IP ownership terms"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "IP,intellectual-property,assignment,legal,review"
    }

    "Termination Risk Assessment" = @{
        triggerPhrases = @(
            "assess termination risks"
            "review the termination clauses"
            "what are the termination triggers"
            "check the termination provisions"
            "I need a termination risk assessment"
            "analyze the exit terms"
            "review the cure period and notice requirements"
            "what happens if we terminate this contract"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "termination,risk,assessment,legal,exit,notice"
    }

    "Quick Legal Scan" = @{
        triggerPhrases = @(
            "do a quick legal scan"
            "scan this for legal issues"
            "quick legal review"
            "give me a fast legal check"
            "triage this legal document"
            "quick scan for red flags"
            "I need a fast legal review"
            "check this document for legal risks quickly"
        ) -join "`n"
        recordType  = "matter"
        entityType  = "sprk_analysisoutput"
        tags        = "legal,scan,quick,triage,red-flags,general"
    }
}

# ============================================================================
# Helper Functions
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "  Acquiring token via Azure CLI..." -ForegroundColor Gray
    $token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv 2>$null

    if (-not $token) {
        throw "Failed to acquire access token. Run 'az login' first."
    }

    return $token.Trim()
}

function Get-DataverseHeaders {
    param([string]$BearerToken)
    return @{
        'Authorization'    = "Bearer $BearerToken"
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
}

function Invoke-DataverseGet {
    param(
        [string]$Endpoint,
        [hashtable]$Headers
    )
    $uri = "$ApiBase/$Endpoint"
    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
        return $response
    } catch {
        $errMsg = $_.ErrorDetails.Message
        if (-not $errMsg) { $errMsg = $_.Exception.Message }
        throw "GET $Endpoint failed: $errMsg"
    }
}

function Invoke-DataversePatch {
    param(
        [string]$Endpoint,
        [hashtable]$Body,
        [hashtable]$Headers
    )
    $uri = "$ApiBase/$Endpoint"
    $jsonBody = $Body | ConvertTo-Json -Depth 20 -Compress
    Invoke-RestMethod -Uri $uri -Headers $Headers -Method Patch `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody)) | Out-Null
}

# ============================================================================
# Main Execution
# ============================================================================

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Seed Playbook Trigger Metadata' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host 'Mode       : DRY RUN' -ForegroundColor Yellow
} elseif ($Force) {
    Write-Host 'Mode       : FORCE (will overwrite existing values)' -ForegroundColor Red
} else {
    Write-Host 'Mode       : LIVE (idempotent — skip non-empty fields)' -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Step 1: Authenticate
# ---------------------------------------------------------------------------
Write-Host '[1/4] Authenticating...' -ForegroundColor Yellow

$headers = $null
if (-not $DryRun) {
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    $headers = Get-DataverseHeaders -BearerToken $token
    Write-Host '  Token acquired.' -ForegroundColor Green
} else {
    Write-Host '  Skipped (dry run).' -ForegroundColor Gray
}

# ---------------------------------------------------------------------------
# Step 2: Query all playbook records
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[2/4] Querying playbook records...' -ForegroundColor Yellow

$playbooks = @()

if ($DryRun) {
    Write-Host '  Skipped (dry run) — will match against seed data keys.' -ForegroundColor Gray
    # In dry run, simulate playbooks from seed data keys
    $index = 0
    foreach ($name in $SeedData.Keys) {
        $playbooks += [PSCustomObject]@{
            sprk_analysisplaybookid = [guid]::NewGuid()
            sprk_name               = $name
            sprk_triggerphrases     = $null
            sprk_recordtype         = $null
            sprk_entitytype         = $null
            sprk_tags               = $null
        }
        $index++
    }
    Write-Host "  Simulated $($playbooks.Count) playbook(s) from seed data." -ForegroundColor Gray
} else {
    $select = "`$select=sprk_analysisplaybookid,sprk_name,sprk_triggerphrases,sprk_recordtype,sprk_entitytype,sprk_tags"
    $result = Invoke-DataverseGet -Endpoint "sprk_analysisplaybooks?$select" -Headers $headers

    if ($result -and $result.value) {
        $playbooks = $result.value
    }
    Write-Host "  Found $($playbooks.Count) playbook(s)." -ForegroundColor Green
}

if ($playbooks.Count -eq 0) {
    Write-Host ''
    Write-Host '  No playbooks found. Nothing to seed.' -ForegroundColor Yellow
    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------------------
# Step 3: Seed trigger metadata
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[3/4] Seeding trigger metadata...' -ForegroundColor Yellow
Write-Host ''

$updatedCount = 0
$skippedCount = 0
$noSeedCount  = 0

foreach ($playbook in $playbooks) {
    $playbookName = $playbook.sprk_name
    $playbookId   = $playbook.sprk_analysisplaybookid

    # Check if we have seed data for this playbook
    if (-not $SeedData.ContainsKey($playbookName)) {
        Write-Host "  [$playbookName] — no seed data defined, skipping." -ForegroundColor Gray
        $noSeedCount++
        continue
    }

    $seed = $SeedData[$playbookName]
    $updateBody = @{}
    $fieldsToUpdate = @()

    # Check each field — only update if empty (unless -Force)
    # sprk_triggerphrases
    $currentTrigger = $playbook.sprk_triggerphrases
    if ($Force -or [string]::IsNullOrWhiteSpace($currentTrigger)) {
        $updateBody['sprk_triggerphrases'] = $seed.triggerPhrases
        $fieldsToUpdate += 'sprk_triggerphrases'
    }

    # sprk_recordtype
    $currentRecordType = $playbook.sprk_recordtype
    if ($Force -or [string]::IsNullOrWhiteSpace($currentRecordType)) {
        $updateBody['sprk_recordtype'] = $seed.recordType
        $fieldsToUpdate += 'sprk_recordtype'
    }

    # sprk_entitytype
    $currentEntityType = $playbook.sprk_entitytype
    if ($Force -or [string]::IsNullOrWhiteSpace($currentEntityType)) {
        $updateBody['sprk_entitytype'] = $seed.entityType
        $fieldsToUpdate += 'sprk_entitytype'
    }

    # sprk_tags
    $currentTags = $playbook.sprk_tags
    if ($Force -or [string]::IsNullOrWhiteSpace($currentTags)) {
        $updateBody['sprk_tags'] = $seed.tags
        $fieldsToUpdate += 'sprk_tags'
    }

    # If nothing to update, skip
    if ($fieldsToUpdate.Count -eq 0) {
        Write-Host "  [$playbookName] — all fields already populated, skipping." -ForegroundColor DarkGray
        $skippedCount++
        continue
    }

    $fieldList = $fieldsToUpdate -join ', '

    if ($DryRun) {
        Write-Host "  [$playbookName] — WOULD UPDATE: $fieldList" -ForegroundColor Yellow
        $updatedCount++
    } else {
        try {
            Invoke-DataversePatch -Endpoint "sprk_analysisplaybooks($playbookId)" `
                -Body $updateBody -Headers $headers
            Write-Host "  [$playbookName] — UPDATED: $fieldList" -ForegroundColor Green
            $updatedCount++
        } catch {
            Write-Host "  [$playbookName] — FAILED: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# ---------------------------------------------------------------------------
# Step 4: Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[4/4] Summary' -ForegroundColor Cyan
Write-Host "  Playbooks found    : $($playbooks.Count)" -ForegroundColor White
Write-Host "  Updated            : $updatedCount" -ForegroundColor $(if ($updatedCount -gt 0) { 'Green' } else { 'White' })
Write-Host "  Skipped (populated): $skippedCount" -ForegroundColor White
Write-Host "  No seed data       : $noSeedCount" -ForegroundColor $(if ($noSeedCount -gt 0) { 'Yellow' } else { 'White' })
Write-Host ''

if ($DryRun) {
    Write-Host 'DRY RUN complete — no changes were made.' -ForegroundColor Yellow
} else {
    Write-Host 'Seeding complete!' -ForegroundColor Green
}

Write-Host ''
