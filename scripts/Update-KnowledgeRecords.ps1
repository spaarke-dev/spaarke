<#
.SYNOPSIS
    Updates RAG Index knowledge records and converts Inline records to JSON taxonomy.

.DESCRIPTION
    1. Updates 10 RAG Index records: clean names, assign KNW-### codes, populate tags
    2. Converts 5 Inline records from markdown to structured JSON taxonomy format
       (reads taxonomy JSON from scripts/seed-data/knowledge-content/*.json)

.PARAMETER Environment
    Target environment. Default: dev

.PARAMETER DryRun
    Preview changes without modifying Dataverse.

.EXAMPLE
    .\Update-KnowledgeRecords.ps1 -DryRun
    .\Update-KnowledgeRecords.ps1
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$envMap = @{ 'dev' = 'https://spaarkedev1.crm.dynamics.com' }
$EnvironmentUrl = $envMap[$Environment]
$ApiBase = "$EnvironmentUrl/api/data/v9.2"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ContentDir = Join-Path $ScriptDir 'seed-data\knowledge-content'

Write-Host ''
Write-Host '=== Update Knowledge Records ===' -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
if ($DryRun) { Write-Host 'Mode       : DRY RUN' -ForegroundColor Yellow }
else         { Write-Host 'Mode       : LIVE' -ForegroundColor Green }
Write-Host ''

# Authenticate
Write-Host '[1/4] Authenticating...' -ForegroundColor Yellow
$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv)
if (-not $token) { throw "Failed to get token. Run 'az login' first." }
Write-Host '  Token acquired.' -ForegroundColor Green

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
}

# ---------------------------------------------------------------------------
# Step 2: Query RAG Index records to get their IDs
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[2/4] Querying RAG Index records...' -ForegroundColor Yellow

$filter = "sprk_knowledgedeliverytype eq 100000002"
$select = "sprk_analysisknowledgeid,sprk_name,sprk_knowledgecode,sprk_knowledgedeliverytype,sprk_tags"
$result = Invoke-RestMethod -Uri "$ApiBase/sprk_analysisknowledges?`$filter=$filter&`$select=$select&`$top=50" -Headers $headers
$ragRecords = $result.value

Write-Host "  Found $($ragRecords.Count) RAG Index records." -ForegroundColor White

# Build name-to-ID map for RAG records
$ragMap = @{}
foreach ($r in $ragRecords) {
    $ragMap[$r.sprk_name] = $r.sprk_analysisknowledgeid
}

# ---------------------------------------------------------------------------
# RAG Index record updates: clean names, assign KNW codes, add tags
# ---------------------------------------------------------------------------
$ragUpdates = @(
    @{
        OldName     = 'KNW-001-contract-terms-glossary'
        NewName     = 'Common Contract Terms Glossary'
        Code        = 'KNW-001'
        Tags        = 'glossary, contract, definitions, terms, commercial'
        Description = 'Vectorized reference document: definitions for 50+ standard contract terms including acceptance, amendment, arbitration, assignment, breach, cap on liability, confidentiality, force majeure, governing law, indemnification, IP, limitation of liability, representations and warranties, severability, termination, and waiver.'
    }
    @{
        OldName     = 'KNW-002-nda-checklist'
        NewName     = 'NDA Review Checklist'
        Code        = 'KNW-002'
        Tags        = 'checklist, nda, review, confidentiality, non-disclosure'
        Description = 'Vectorized reference document: 20-item checklist for NDA review covering party identification, purpose, confidential information definition and scope, standard exclusions, standard of care, permitted use, permitted disclosure, agreement term, survival of obligations, return/destruction, injunctive relief, and governing law.'
    }
    @{
        OldName     = 'KNW-003-lease-agreement-standards'
        NewName     = 'Lease Agreement Standards'
        Code        = 'KNW-003'
        Tags        = 'lease, commercial, standards, real-estate, rent'
        Description = 'Vectorized reference document: commercial lease standards covering base rent and escalation, operating expense structures, controllable vs. uncontrollable expense caps, permitted use, exclusive use rights, renewal/expansion/termination options, tenant improvement allowance, and holdover provisions.'
    }
    @{
        OldName     = 'KNW-004-invoice-processing-guide'
        NewName     = 'Invoice Processing Guide'
        Code        = 'KNW-004'
        Tags        = 'invoice, accounts-payable, processing, validation, matching'
        Description = 'Vectorized reference document: AP invoice processing guide covering invoice types, required fields, three-way and two-way matching, matching tolerances, duplicate detection, vendor master file validation, PO validation, contract compliance, tax validation, exception handling, and approval workflow.'
    }
    @{
        OldName     = 'KNW-005-sla-metrics-reference'
        NewName     = 'SLA Metrics Reference'
        Code        = 'KNW-005'
        Tags        = 'sla, metrics, availability, performance, uptime'
        Description = 'Vectorized reference document: SLA/SLO/SLI definitions, availability tiers, response time/latency metrics, throughput, error rate targets, incident severity levels with response time and resolution SLAs, tiered service credit schedules, and measurement methodology.'
    }
    @{
        OldName     = 'KNW-006-employment-law-quick-reference'
        NewName     = 'Employment Law Quick Reference'
        Code        = 'KNW-006'
        Tags        = 'employment, labor-law, compliance, flsa, non-compete'
        Description = 'Vectorized reference document: US employment law fundamentals covering at-will employment, employee vs. independent contractor classification, FLSA wage and overtime, non-compete enforceability by state, DTSA immunity notice, work made for hire, invention assignment, FMLA, ADA, USERRA, WARN Act, and OWBPA requirements.'
    }
    @{
        OldName     = 'KNW-007-ip-assignment-clause-library'
        NewName     = 'IP Assignment Clause Library'
        Code        = 'KNW-007'
        Tags        = 'IP, assignment, clauses, work-product, patents'
        Description = 'Vectorized reference document: annotated IP assignment clauses including broad work product assignment, independent contractor work-for-hire, background IP definition and license-back, prior inventions exclusion, AI/ML model and training data assignment, open source compliance, moral rights waiver, and future patents assignment.'
    }
    @{
        OldName     = 'KNW-008-termination-and-remedy-provisions'
        NewName     = 'Termination And Remedy Provisions'
        Code        = 'KNW-008'
        Tags        = 'termination, remedies, provisions, breach, cure'
        Description = 'Vectorized reference document: comprehensive termination and remedy reference covering termination for cause triggers, notice and cure periods, termination for convenience, termination fees, post-termination transition, return/destruction of data, survival provisions, damages types, liquidated damages, injunctive relief, and set-off rights.'
    }
    @{
        OldName     = 'KNW-009-governing-law-and-jurisdiction-guide'
        NewName     = 'Governing Law And Jurisdiction Guide'
        Code        = 'KNW-009'
        Tags        = 'jurisdiction, governing-law, arbitration, disputes, venue'
        Description = 'Vectorized reference document: governing law and dispute resolution reference covering common US/international law choices, conflict-of-law exclusions, exclusive vs. non-exclusive jurisdiction, arbitration institutions, tiered dispute resolution, interim relief, and New York Convention enforcement.'
    }
    @{
        OldName     = 'KNW-010-legal-red-flags-catalog'
        NewName     = 'Legal Document Red Flags Catalog'
        Code        = 'KNW-010'
        Tags        = 'red-flags, risk, compliance, detection, severity'
        Description = 'Vectorized reference document: 32 red flags across 10 categories with severity ratings covering liability/indemnification, payment/financial, termination, data/privacy, IP, contract structure, dispute resolution, confidentiality, employment/personnel, and miscellaneous flags.'
    }
)

# ---------------------------------------------------------------------------
# Step 3: Update RAG Index records
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "[3/4] Updating $($ragUpdates.Count) RAG Index records..." -ForegroundColor Yellow

$ragCount = 0
foreach ($rec in $ragUpdates) {
    $id = $ragMap[$rec.OldName]
    if (-not $id) {
        Write-Warning "  $($rec.OldName) not found in Dataverse - skipping"
        continue
    }

    $body = @{
        sprk_name          = $rec.NewName
        sprk_knowledgecode = $rec.Code
        sprk_tags          = $rec.Tags
        sprk_description   = $rec.Description
    }

    if ($DryRun) {
        Write-Host "  WOULD UPDATE: $($rec.OldName)" -ForegroundColor Gray
        Write-Host "    -> Name: $($rec.NewName)" -ForegroundColor DarkGray
        Write-Host "    -> Code: $($rec.Code)" -ForegroundColor DarkGray
        Write-Host "    -> Tags: $($rec.Tags)" -ForegroundColor DarkGray
        $ragCount++
    } else {
        try {
            $jsonBody = $body | ConvertTo-Json -Depth 5 -Compress
            Invoke-RestMethod -Uri "$ApiBase/sprk_analysisknowledges($id)" -Headers $headers -Method Patch `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
            Write-Host "  Updated: $($rec.Code) $($rec.NewName)" -ForegroundColor Green
            $ragCount++
        } catch {
            Write-Warning "  Failed to update $($rec.OldName): $($_.Exception.Message)"
        }
    }
}
Write-Host "  $ragCount RAG Index record(s) updated." -ForegroundColor White

# ---------------------------------------------------------------------------
# Step 4: Convert 5 Inline records from markdown to JSON taxonomy
#         Content loaded from scripts/seed-data/knowledge-content/*.json
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[4/4] Converting 5 Inline records to JSON taxonomy format...' -ForegroundColor Yellow

$inlineUpdates = @(
    @{
        Id       = '46b4825f-ede9-f011-8406-7ced8d1dc988'
        Name     = 'DD Checklist'
        Code     = 'KM-006'
        File     = 'km-006-dd-checklist.json'
    }
    @{
        Id       = '907c3e5e-ede9-f011-8406-7c1e520aa4df'
        Name     = 'SLA Benchmarks'
        Code     = 'KM-010'
        File     = 'km-010-sla-benchmarks.json'
    }
    @{
        Id       = 'f958948e-5218-f111-8343-7ced8d1dc988'
        Name     = 'Commercial Risk Factors'
        Code     = 'KM-013'
        File     = 'km-013-commercial-risk-factors.json'
    }
    @{
        Id       = 'bb6eef90-5218-f111-8342-7c1e525abd8b'
        Name     = 'AP Fraud Indicators'
        Code     = 'KM-016'
        File     = 'km-016-ap-fraud-indicators.json'
    }
    @{
        Id       = '67a89c92-5218-f111-8343-7c1e520aa4df'
        Name     = 'Invoice Validation Rules'
        Code     = 'KM-017'
        File     = 'km-017-invoice-validation-rules.json'
    }
)

$inlineCount = 0
foreach ($rec in $inlineUpdates) {
    $filePath = Join-Path $ContentDir $rec.File
    if (-not (Test-Path $filePath)) {
        Write-Warning "  Content file not found: $filePath - skipping $($rec.Code)"
        continue
    }

    $content = Get-Content -Path $filePath -Raw -Encoding UTF8

    # Escape the JSON content as a string value for the PATCH body
    # ConvertTo-Json mangles nested JSON, so build manually
    $escapedContent = $content.Replace('\', '\\').Replace('"', '\"').Replace("`r`n", '\r\n').Replace("`n", '\n').Replace("`r", '\r').Replace("`t", '\t')
    $jsonBody = "{`"sprk_content`":`"$escapedContent`"}"

    if ($DryRun) {
        Write-Host "  WOULD CONVERT: $($rec.Code) $($rec.Name) -> JSON taxonomy" -ForegroundColor Gray
        Write-Host "    Source: $($rec.File)" -ForegroundColor DarkGray
        $inlineCount++
    } else {
        try {
            Invoke-RestMethod -Uri "$ApiBase/sprk_analysisknowledges($($rec.Id))" -Headers $headers -Method Patch `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
            Write-Host "  Converted: $($rec.Code) $($rec.Name) -> JSON taxonomy" -ForegroundColor Green
            $inlineCount++
        } catch {
            Write-Warning "  Failed to convert $($rec.Code): $($_.Exception.Message)"
        }
    }
}
Write-Host "  $inlineCount Inline record(s) converted to JSON taxonomy." -ForegroundColor White

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host 'Summary' -ForegroundColor Yellow
Write-Host "  RAG Index records updated: $ragCount (names, codes, tags)" -ForegroundColor White
Write-Host "  Inline records converted : $inlineCount (markdown -> JSON taxonomy)" -ForegroundColor White

if ($DryRun) {
    Write-Host "`n=== DRY RUN COMPLETE ===" -ForegroundColor Yellow
} else {
    Write-Host "`n=== Update complete ===" -ForegroundColor Green
}
