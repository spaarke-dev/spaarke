<#
.SYNOPSIS
    Cleans up sprk_analysisknowledges records in Dataverse.

.DESCRIPTION
    Audits all knowledge records and performs:
    1. Deletes duplicates and misclassified records
    2. Updates remaining records with sprk_knowledgecode (KM-###)
    3. Sets sprk_knowledgedeliverytype to Inline (100000000)
    4. Populates sprk_tags (comma-separated keywords)
    5. Improves sprk_description for AI analysis quality

.PARAMETER Environment
    Target environment. Default: dev

.PARAMETER DryRun
    Preview changes without modifying Dataverse.

.EXAMPLE
    .\Cleanup-KnowledgeRecords.ps1 -DryRun
    .\Cleanup-KnowledgeRecords.ps1
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

Write-Host ''
Write-Host '=== Knowledge Records Cleanup ===' -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
if ($DryRun) {
    Write-Host 'Mode       : DRY RUN' -ForegroundColor Yellow
} else {
    Write-Host 'Mode       : LIVE' -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Authenticate
# ---------------------------------------------------------------------------
Write-Host '[1/4] Authenticating...' -ForegroundColor Yellow
$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv)
if (-not $token) { throw "Failed to get token. Run 'az login' first." }
Write-Host '  Token acquired.' -ForegroundColor Green

$headers = @{
    "Authorization"  = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"  = "4.0"
    "Content-Type"   = "application/json"
    "Accept"         = "application/json"
}

# ---------------------------------------------------------------------------
# Records to DELETE (duplicates, builder, misclassified)
# ---------------------------------------------------------------------------
$deleteRecords = @(
    # Duplicates of "Standard Contract Terms" (KM-001)
    @{ Id = '65a89c92-5218-f111-8343-7c1e520aa4df'; Name = 'standard-contract-clauses'; Reason = 'Duplicate of Standard Contract Terms' }
    @{ Id = '69a89c92-5218-f111-8343-7c1e520aa4df'; Name = 'standard-contract-terms'; Reason = 'Duplicate of Standard Contract Terms' }
    @{ Id = '9f70d3eb-71d9-f011-8406-7c1e520aa4df'; Name = 'Standard Contract Templates'; Reason = 'Duplicate of Standard Contract Terms' }

    # Duplicate of "Defined Terms" (KM-002)
    @{ Id = '6ca89c92-5218-f111-8343-7c1e520aa4df'; Name = 'defined-terms'; Reason = 'Duplicate of Defined Terms' }

    # Duplicate of "Risk Categories" (KM-003)
    @{ Id = '6ba89c92-5218-f111-8343-7c1e520aa4df'; Name = 'risk-categories'; Reason = 'Duplicate of Risk Categories' }

    # Duplicate of "NDA Standards" (KM-007)
    @{ Id = 'b76eef90-5218-f111-8342-7c1e525abd8b'; Name = 'nda-standard-provisions'; Reason = 'Duplicate of NDA Standards' }

    # Duplicate of "Lease Standards" (KM-009)
    @{ Id = '66a89c92-5218-f111-8343-7c1e520aa4df'; Name = 'lease-standard-provisions'; Reason = 'Duplicate of Lease Standards' }

    # Duplicate of "SLA Benchmarks" (KM-010)
    @{ Id = '68a89c92-5218-f111-8343-7c1e520aa4df'; Name = 'sla-standards'; Reason = 'Duplicate of SLA Benchmarks' }

    # Builder-specific (not used by analysis playbooks)
    @{ Id = '94f5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'KNW-BUILDER-001: Scope Catalog'; Reason = 'Builder-specific, not analysis knowledge' }
    @{ Id = '52811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'KNW-BUILDER-002: Reference Playbooks'; Reason = 'Builder-specific, not analysis knowledge' }
    @{ Id = '99f5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'KNW-BUILDER-003: Node Schema'; Reason = 'Builder-specific, not analysis knowledge' }
    @{ Id = '9af5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'KNW-BUILDER-004: Best Practices'; Reason = 'Builder-specific, not analysis knowledge' }

    # Misclassified — this is a skill (behavioral instruction), not knowledge (reference data)
    @{ Id = 'a270d3eb-71d9-f011-8406-7c1e520aa4df'; Name = 'Example Analyses'; Reason = 'Misclassified: behavioral instruction, not reference data (should be a skill)' }

    # Misclassified — style/formatting rules are skill behavior, not knowledge content
    @{ Id = 'b0addef0-71d9-f011-8406-7ced8d1dc988'; Name = 'Business Writing Guidelines'; Reason = 'Misclassified: style/formatting rules are skill behavior, not knowledge' }
)

# ---------------------------------------------------------------------------
# Records to UPDATE (keep, assign codes, tags, improve descriptions)
# ---------------------------------------------------------------------------
$updateRecords = @(
    @{
        Id          = '6b7c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name        = 'Standard Contract Terms'
        Code        = 'KM-001'
        Tags        = 'contract, clauses, terms, baseline, comparison, commercial'
        Description = 'Standard contract terms and clauses used as baseline for comparison during document analysis. Includes common provisions for indemnification, limitation of liability, termination, force majeure, governing law, and dispute resolution. Sourced from industry-standard commercial agreements.'
    }
    @{
        Id          = '737c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name        = 'Defined Terms'
        Code        = 'KM-002'
        Tags        = 'glossary, definitions, legal-terms, terminology'
        Description = 'Standard legal definitions and defined terms commonly used in commercial agreements. Provides canonical definitions for terms like Affiliate, Change of Control, Confidential Information, Force Majeure, Intellectual Property, Material Adverse Effect, and other frequently used legal concepts.'
    }
    @{
        Id          = '43b4825f-ede9-f011-8406-7ced8d1dc988'
        Name        = 'Risk Categories'
        Code        = 'KM-003'
        Tags        = 'risk, classification, taxonomy, assessment, scoring'
        Description = 'Taxonomy of risk categories used for document risk assessment and classification. Defines risk levels (Critical, High, Medium, Low) with scoring criteria across categories: financial exposure, regulatory compliance, operational impact, reputational risk, and legal liability.'
    }
    @{
        Id          = '6c7c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name        = 'Regulatory Guidelines'
        Code        = 'KM-004'
        Tags        = 'regulatory, compliance, gdpr, ccpa, sox, hipaa, privacy, data-protection'
        Description = 'Regulatory compliance requirements and guidelines for document analysis. Covers GDPR (EU data protection), CCPA (California privacy), SOX (financial controls), HIPAA (healthcare data), and industry-specific regulations. Used to flag compliance gaps and regulatory risks in contracts and policies.'
    }
    @{
        Id          = '400c7059-ede9-f011-8406-7ced8d1dc988'
        Name        = 'Best Practices'
        Code        = 'KM-005'
        Tags        = 'best-practices, review, methodology, quality, thoroughness'
        Description = 'Document review best practices and guidelines for thorough contract analysis. Covers systematic review methodology, clause-by-clause analysis approach, cross-reference validation, defined term tracking, and quality checkpoints for comprehensive legal document review.'
    }
    @{
        Id          = '46b4825f-ede9-f011-8406-7ced8d1dc988'
        Name        = 'DD Checklist'
        Code        = 'KM-006'
        Tags        = 'due-diligence, checklist, audit, transaction, m-and-a'
        Description = 'Due diligence checklist categories for comprehensive contract and document review. Organized by workstream: corporate governance, financial, tax, employment, intellectual property, real estate, environmental, litigation, regulatory, and commercial contracts.'
    }
    @{
        Id          = '44b4825f-ede9-f011-8406-7ced8d1dc988'
        Name        = 'NDA Standards'
        Code        = 'KM-007'
        Tags        = 'nda, confidentiality, non-disclosure, mutual, one-way'
        Description = 'Standard terms and benchmarks for Non-Disclosure Agreements including mutual and one-way NDAs. Covers definition of confidential information, exclusions, permitted disclosures, term and survival periods, return/destruction obligations, and remedies for breach.'
    }
    @{
        Id          = '887c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name        = 'Employment Standards'
        Code        = 'KM-008'
        Tags        = 'employment, compensation, benefits, non-compete, restrictive-covenants'
        Description = 'Standard employment agreement terms including compensation structures, benefit provisions, equity/option grants, non-compete and non-solicitation covenants, confidentiality obligations, termination provisions, severance terms, and change of control protections.'
    }
    @{
        Id          = '827c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name        = 'Lease Standards'
        Code        = 'KM-009'
        Tags        = 'lease, commercial, real-estate, rent, tenant, landlord'
        Description = 'Standard commercial lease terms and market benchmarks for real estate agreements. Covers base rent and escalation structures, CAM charges, tenant improvement allowances, renewal options, assignment and subletting provisions, maintenance obligations, and early termination rights.'
    }
    @{
        Id          = '907c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name        = 'SLA Benchmarks'
        Code        = 'KM-010'
        Tags        = 'sla, service-level, uptime, performance, metrics, penalties'
        Description = 'Industry-standard Service Level Agreement benchmarks and metrics. Covers uptime guarantees (99.9%, 99.95%, 99.99%), response time thresholds, resolution time targets, service credit structures, measurement methodologies, and exclusion categories.'
    }
    @{
        Id          = 'a070d3eb-71d9-f011-8406-7c1e520aa4df'
        Name        = 'Company Policies'
        Code        = 'KM-011'
        Tags        = 'policies, internal, organization, procedures, governance'
        Description = 'Internal company policies, procedures, and governance guidelines. Customer-specific content that provides organizational context for document analysis — includes acceptable use policies, data handling procedures, approval workflows, and delegation of authority matrices.'
    }
    @{
        Id          = 'b4addef0-71d9-f011-8406-7ced8d1dc988'
        Name        = 'Legal Reference Materials'
        Code        = 'KM-012'
        Tags        = 'legal, reference, terminology, clause-library, glossary'
        Description = 'Legal terminology glossaries, clause explanations, and regulatory reference materials. Provides authoritative definitions and explanations for legal concepts, standard clause types, and common contract structures used during document analysis.'
    }
    @{
        Id          = 'f958948e-5218-f111-8343-7ced8d1dc988'
        Name        = 'Commercial Risk Factors'
        Code        = 'KM-013'
        Tags        = 'risk, commercial, contract, assessment, red-flags'
        Description = 'Risk factor definitions for commercial contract risk assessment. Identifies high-risk provisions (unlimited liability, unilateral termination, auto-renewal with price escalation), medium-risk items (broad indemnification, IP assignment), and common red flags across contract types.'
    }
    @{
        Id          = 'fb58948e-5218-f111-8343-7ced8d1dc988'
        Name        = 'Confidentiality Best Practices'
        Code        = 'KM-014'
        Tags        = 'confidentiality, data-protection, information-security, nda'
        Description = 'Best practices for confidentiality provisions in commercial agreements. Covers scope of confidential information, carve-outs for publicly available information, permitted disclosure scenarios, handling of derivatives and compilations, return/destruction obligations, and survival periods.'
    }
    @{
        Id          = 'b86eef90-5218-f111-8342-7c1e525abd8b'
        Name        = 'Commercial Lease Benchmarks'
        Code        = 'KM-015'
        Tags        = 'lease, benchmarks, market-rates, commercial, real-estate'
        Description = 'Market benchmarks for evaluating commercial lease terms. Provides reference data for market rent ranges by class (A/B/C), typical CAM cost ratios, standard TI allowance ranges, common escalation structures, and lease term norms by property type and market.'
    }
    @{
        Id          = 'bb6eef90-5218-f111-8342-7c1e525abd8b'
        Name        = 'AP Fraud Indicators'
        Code        = 'KM-016'
        Tags        = 'fraud, accounts-payable, invoice, red-flags, financial'
        Description = 'Accounts payable fraud red flags and indicators for invoice and payment analysis. Covers duplicate invoice detection patterns, vendor master manipulation indicators, round-number anomalies, split-payment schemes, ghost vendor characteristics, and Benford law deviation thresholds.'
    }
    @{
        Id          = '67a89c92-5218-f111-8343-7c1e520aa4df'
        Name        = 'Invoice Validation Rules'
        Code        = 'KM-017'
        Tags        = 'invoice, validation, compliance, payment, accounts-payable'
        Description = 'Invoice validation and compliance rules for accounts payable analysis. Covers required invoice fields, tax calculation verification, payment term compliance, purchase order matching rules, approval threshold matrices, and regulatory requirements for invoice documentation.'
    }
    @{
        Id          = '6aa89c92-5218-f111-8343-7c1e520aa4df'
        Name        = 'Employment Law Reference'
        Code        = 'KM-018'
        Tags        = 'employment, labor-law, compliance, flsa, fmla, ada, discrimination'
        Description = 'Employment law compliance reference for agreement review. Covers FLSA wage and hour requirements, FMLA leave provisions, ADA accommodation obligations, Title VII protections, at-will employment doctrine, non-compete enforceability by jurisdiction, and WARN Act notification requirements.'
    }
)

# ---------------------------------------------------------------------------
# Step 2: Delete duplicates and misclassified records
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "[2/4] Deleting $($deleteRecords.Count) duplicate/misclassified records..." -ForegroundColor Yellow

$deleteCount = 0
foreach ($rec in $deleteRecords) {
    if ($DryRun) {
        Write-Host "  WOULD DELETE: $($rec.Name) — $($rec.Reason)" -ForegroundColor Gray
        $deleteCount++
    } else {
        try {
            Invoke-RestMethod -Uri "$ApiBase/sprk_analysisknowledges($($rec.Id))" -Headers $headers -Method Delete
            Write-Host "  Deleted: $($rec.Name) — $($rec.Reason)" -ForegroundColor Red
            $deleteCount++
        } catch {
            Write-Warning "  Failed to delete $($rec.Name): $($_.Exception.Message)"
        }
    }
}
Write-Host "  $deleteCount record(s) deleted." -ForegroundColor White

# ---------------------------------------------------------------------------
# Step 3: Update remaining records with codes, types, tags, descriptions
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "[3/4] Updating $($updateRecords.Count) records with codes, tags, and descriptions..." -ForegroundColor Yellow

$updateCount = 0
foreach ($rec in $updateRecords) {
    $body = @{
        sprk_knowledgecode         = $rec.Code
        sprk_knowledgedeliverytype = 100000000  # Inline
        sprk_tags                  = $rec.Tags
        sprk_description           = $rec.Description
    }

    if ($DryRun) {
        Write-Host "  WOULD UPDATE: $($rec.Code) $($rec.Name)" -ForegroundColor Gray
        Write-Host "    Tags: $($rec.Tags)" -ForegroundColor DarkGray
        $updateCount++
    } else {
        try {
            $jsonBody = $body | ConvertTo-Json -Depth 5 -Compress
            Invoke-RestMethod -Uri "$ApiBase/sprk_analysisknowledges($($rec.Id))" -Headers $headers -Method Patch `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
            Write-Host "  Updated: $($rec.Code) $($rec.Name)" -ForegroundColor Green
            $updateCount++
        } catch {
            Write-Warning "  Failed to update $($rec.Name): $($_.Exception.Message)"
        }
    }
}
Write-Host "  $updateCount record(s) updated." -ForegroundColor White

# ---------------------------------------------------------------------------
# Step 4: Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[4/4] Summary' -ForegroundColor Yellow
Write-Host "  Deleted : $deleteCount (duplicates, builder, misclassified)" -ForegroundColor White
Write-Host "  Updated : $updateCount (codes, delivery type, tags, descriptions)" -ForegroundColor White
Write-Host "  Remaining: $($updateRecords.Count) knowledge records" -ForegroundColor White
Write-Host ''

if ($DryRun) {
    Write-Host '=== DRY RUN COMPLETE — no changes made ===' -ForegroundColor Yellow
} else {
    Write-Host '=== Cleanup complete ===' -ForegroundColor Green
}
