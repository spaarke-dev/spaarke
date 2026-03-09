<#
.SYNOPSIS
    Seeds Knowledge scope records into Dataverse for JPS $ref resolution.

.DESCRIPTION
    Creates sprk_analysisknowledge records that JPS definitions reference via
    "$ref": "knowledge:{name}". The resolver (ScopeResolverService.GetKnowledgeByNameAsync)
    matches on sprk_name. Records are created if they don't exist, updated if content changed.

.PARAMETER Environment
    Target environment: 'dev' (default).

.PARAMETER WhatIf
    Preview mode — shows what would be created/updated without making changes.

.EXAMPLE
    .\Seed-KnowledgeScopes.ps1 -Environment dev
    .\Seed-KnowledgeScopes.ps1 -WhatIf
#>
param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Environment Configuration
# ---------------------------------------------------------------------------
$EnvConfig = @{
    'dev' = @{
        Url = 'https://spaarkedev1.crm.dynamics.com'
    }
}

$DataverseUrl = $EnvConfig[$Environment].Url
$ApiUrl = "$DataverseUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# Knowledge Scope Definitions
# ---------------------------------------------------------------------------
# Each entry: Name (matches $ref), Description, Content (the actual knowledge text)

$KnowledgeScopes = @(
    @{
        Name        = 'standard-contract-clauses'
        Description = 'Standard contract clause templates for comparison and analysis'
        Content     = @'
## Standard Contract Clauses Reference

### Indemnification
- Mutual indemnification for third-party IP claims
- Indemnitor covers direct damages from breach of representations
- Standard carve-outs: gross negligence, willful misconduct, confidentiality breach

### Limitation of Liability
- Cap: total fees paid in preceding 12 months
- Exclusion of consequential, incidental, special damages
- Carve-outs: indemnification obligations, IP infringement, confidentiality breach

### Termination
- Termination for convenience: 30-day written notice
- Termination for cause: 30-day cure period after written notice
- Effect: accrued rights survive; return/destroy confidential information

### Confidentiality
- Definition: non-public business, technical, financial information
- Obligations: reasonable care, need-to-know basis, no reverse engineering
- Duration: 3-5 years post-termination (trade secrets: indefinite)
- Exclusions: publicly known, independently developed, rightfully received

### Representations & Warranties
- Authority to enter agreement
- No conflict with existing obligations
- Compliance with applicable laws
- Services performed in professional manner

### Force Majeure
- Events: natural disasters, war, government actions, epidemics, utility failures
- Obligations: prompt notice, mitigation efforts, performance resumes when possible
- Extended force majeure (>90 days): either party may terminate
'@
    }

    @{
        Name        = 'commercial-risk-factors'
        Description = 'Risk factor definitions for contract risk assessment'
        Content     = @'
## Commercial Risk Factors

### High Risk Indicators
- Unlimited liability exposure
- One-sided indemnification (only one party indemnifies)
- No limitation on consequential damages
- Automatic renewal without notice period
- Assignment without consent
- Unilateral amendment rights
- Governing law in unfavorable jurisdiction
- No dispute resolution mechanism

### Medium Risk Indicators
- Liability cap below industry standard (<1x annual fees)
- Cure period less than 15 days
- Broad definition of confidential information without exclusions
- Non-compete or non-solicitation exceeding 2 years
- Audit rights without reasonable notice (less than 10 business days)

### Low Risk Indicators
- Standard mutual indemnification
- Liability cap at 1-2x annual contract value
- 30-day cure period for material breach
- Balanced termination rights
- Standard confidentiality with 3-year duration

### Risk Scoring Guidelines
- Critical (9-10): Immediate legal review required
- High (7-8): Negotiate before signing
- Medium (4-6): Acceptable with awareness
- Low (1-3): Standard commercial terms
'@
    }

    @{
        Name        = 'nda-standard-provisions'
        Description = 'Standard NDA provisions checklist for analysis'
        Content     = @'
## NDA Standard Provisions

### Essential Elements
- Clear definition of Confidential Information (inclusions and exclusions)
- Mutual vs. unilateral obligations specified
- Purpose limitation (specific business purpose stated)
- Duration of confidentiality obligations (typically 2-5 years)
- Permitted disclosures (employees, advisors, legal requirements)
- Return/destruction of materials upon termination
- Remedies for breach (injunctive relief, damages)

### Standard Exclusions from Confidential Information
- Information already in public domain (not through breach)
- Information known prior to disclosure
- Information independently developed without use of confidential info
- Information received from third party without restriction

### Red Flags in NDAs
- No definition of Confidential Information
- Perpetual obligations without justification
- No exclusions from confidentiality
- Residuals clause too broad
- No provision for legally compelled disclosure
- Non-compete disguised as NDA provision
'@
    }

    @{
        Name        = 'confidentiality-best-practices'
        Description = 'Best practices for confidentiality provisions'
        Content     = @'
## Confidentiality Best Practices

### Marking Requirements
- Written information: marked "Confidential" at time of disclosure
- Oral disclosures: identified as confidential at time of disclosure, confirmed in writing within 30 days
- Visual information: identified before or during presentation

### Protection Standards
- Same degree of care as own confidential information (not less than reasonable care)
- Access limited to need-to-know basis
- Written agreements with recipients containing substantially similar protections
- Secure storage and transmission

### Duration Guidelines
- General business information: 3 years
- Technical/trade secret information: 5 years or indefinite
- Financial information: 3 years
- Personal data: as required by applicable privacy laws

### Compliance Monitoring
- Regular audits of access controls
- Training for personnel handling confidential information
- Incident response procedures for unauthorized disclosure
- Record keeping of disclosures made
'@
    }

    @{
        Name        = 'lease-standard-provisions'
        Description = 'Standard commercial lease terms reference'
        Content     = @'
## Commercial Lease Standard Provisions

### Rent Structure
- Base rent with annual escalation (typically 2-4% or CPI)
- Triple net (NNN): tenant pays taxes, insurance, maintenance
- Modified gross: some expenses included in base rent
- Percentage rent: base plus percentage of gross sales (retail)

### Term & Renewal
- Initial term: typically 3-10 years
- Renewal options: 1-3 renewal terms of 3-5 years each
- Notice period for renewal: 6-12 months before expiration
- Fair market value rent reset at renewal

### Maintenance & Repairs
- Landlord: structural elements, roof, building systems, common areas
- Tenant: interior maintenance, HVAC filters, cosmetic repairs
- Capital improvements: landlord responsibility, may be amortized

### Key Protections
- Assignment and subletting rights
- Exclusive use provisions
- Co-tenancy requirements (retail)
- Tenant improvement allowance
- Early termination rights (kick-out clauses)
'@
    }

    @{
        Name        = 'commercial-lease-benchmarks'
        Description = 'Market benchmarks for evaluating commercial lease terms'
        Content     = @'
## Commercial Lease Benchmarks

### Rent Escalation
- Standard: 2-4% annual increase or CPI-linked
- Above market: >5% annual increase
- Below market: Fixed rent with no escalation

### Tenant Improvement Allowance
- Class A Office: $40-80 per square foot
- Class B Office: $20-40 per square foot
- Retail: $15-35 per square foot
- Industrial: $5-15 per square foot

### Operating Expense Caps
- Standard: 3-5% annual increase cap
- Favorable: 2-3% cap with base year stop
- Unfavorable: No cap on controllable expenses

### Free Rent Periods
- Standard: 1 month per year of term
- Favorable: >1 month per year
- Below market: No free rent period
'@
    }

    @{
        Name        = 'invoice-validation-rules'
        Description = 'Invoice validation and compliance rules'
        Content     = @'
## Invoice Validation Rules

### Required Fields
- Invoice number (unique identifier)
- Invoice date and due date
- Vendor name, address, and tax ID
- Line items with descriptions, quantities, unit prices
- Tax calculations (rate, amount, jurisdiction)
- Total amount due
- Payment terms and instructions

### Validation Checks
- Mathematical accuracy (line items, subtotals, tax, total)
- Duplicate invoice detection (same vendor, amount, date)
- Purchase order matching (3-way match: PO, receipt, invoice)
- Tax rate verification against jurisdiction
- Payment terms consistency with contract
- Vendor bank details match master records

### Compliance Requirements
- Tax invoice requirements per jurisdiction
- Withholding tax applicability
- Currency and exchange rate documentation
- Regulatory reporting thresholds
'@
    }

    @{
        Name        = 'ap-fraud-indicators'
        Description = 'Accounts payable fraud red flags and indicators'
        Content     = @'
## AP Fraud Indicators

### High-Risk Red Flags
- Round dollar amounts on invoices (no cents)
- Sequential invoice numbers from same vendor in short period
- Invoice amounts just below approval thresholds
- Vendor address matches employee address
- New vendor with immediate high-value invoices
- Invoices with no purchase order reference
- Bank account changes requested via email

### Medium-Risk Indicators
- Duplicate invoices with slight variations (date, amount)
- Invoices dated on weekends or holidays
- Vendor with P.O. Box only address
- Sudden increase in invoice frequency or amounts
- Services described vaguely ("consulting", "professional services")
- Missing or incomplete supporting documentation

### Pattern Analysis
- Multiple invoices just under review threshold
- Consistent round-number invoices
- Vendor only does business during certain periods
- Payments always to same bank account across different vendor names
'@
    }

    @{
        Name        = 'sla-standards'
        Description = 'SLA standard metrics and thresholds'
        Content     = @'
## SLA Standards Reference

### Availability/Uptime
- Enterprise: 99.99% (52.6 min downtime/year)
- Standard: 99.9% (8.77 hours downtime/year)
- Basic: 99.5% (43.8 hours downtime/year)
- Measurement: Monthly or quarterly calculation periods

### Response Time Tiers
- Critical (P1): 15 min response, 4 hour resolution target
- High (P2): 1 hour response, 8 hour resolution target
- Medium (P3): 4 hour response, 24 hour resolution target
- Low (P4): 8 hour response, 72 hour resolution target

### Performance Metrics
- Page load time: <3 seconds (95th percentile)
- API response time: <500ms (95th percentile)
- Throughput: defined per service type
- Error rate: <0.1% of total requests

### Remedies
- Service credits: 5-30% of monthly fee based on severity
- Termination rights: repeated SLA failures (3+ months)
- Root cause analysis: required within 5 business days of P1 incident
- Escalation matrix: defined contacts and response commitments
'@
    }

    @{
        Name        = 'standard-contract-terms'
        Description = 'General contract term definitions used across multiple action types'
        Content     = @'
## Standard Contract Terms Reference

### Payment Terms
- Net 30: Payment due 30 days from invoice date
- Net 60: Payment due 60 days from invoice date
- 2/10 Net 30: 2% discount if paid within 10 days
- Due on receipt: Payment due immediately
- Progress billing: Payments tied to milestones

### Intellectual Property
- Work-for-hire: Client owns all deliverables
- License grant: Provider retains ownership, grants usage license
- Joint ownership: Both parties share IP rights
- Pre-existing IP: Each party retains rights to prior IP
- Background IP: Provider tools and methodologies remain provider property

### Dispute Resolution
- Negotiation: Good faith discussions (30 days)
- Mediation: Non-binding mediation before arbitration
- Arbitration: Binding arbitration (AAA, JAMS, ICC rules)
- Litigation: Court proceedings in specified jurisdiction
- Escalation: Defined management escalation path

### Insurance Requirements
- Commercial general liability: $1M per occurrence / $2M aggregate
- Professional liability (E&O): $1M-5M per claim
- Cyber liability: $1M-5M (for technology services)
- Workers compensation: Statutory limits
- Auto liability: $1M combined single limit

### Governing Law Considerations
- Chosen jurisdiction should have connection to parties or performance
- Federal vs. state law applicability
- International considerations (CISG, choice of forum)
'@
    }

    @{
        Name        = 'employment-law'
        Description = 'Employment law compliance reference for agreement review'
        Content     = @'
## Employment Law Reference

### Non-Compete Enforceability
- Must be reasonable in scope, duration, and geography
- Typical enforceable duration: 6-24 months
- Must protect legitimate business interest
- Some states restrict or ban (e.g., California, Colorado, Minnesota)
- Consideration required (new employment or additional compensation)

### Non-Solicitation
- Customer non-solicitation: typically 12-24 months
- Employee non-solicitation: typically 12-24 months
- Must be limited to contacts during employment
- Generally more enforceable than non-competes

### Key Employment Agreement Provisions
- At-will vs. for-cause termination rights
- Severance and separation terms
- Change of control / golden parachute provisions
- Invention assignment and IP ownership
- Confidentiality and trade secret protections
- Garden leave provisions
- Clawback provisions for bonuses/equity

### Compliance Requirements
- Wage and hour laws (FLSA classification)
- Equal employment opportunity
- Benefits continuation (COBRA)
- WARN Act requirements for mass layoffs
- State-specific requirements (final paycheck timing, PTO payout)
'@
    }

    @{
        Name        = 'risk-categories'
        Description = 'Risk classification taxonomy for document analysis'
        Content     = @'
## Risk Classification Taxonomy

### Legal Risk
- Regulatory non-compliance
- Litigation exposure
- Intellectual property infringement
- Data privacy violations
- Contract enforceability issues

### Financial Risk
- Payment default exposure
- Unlimited liability
- Currency and exchange rate risk
- Tax liability uncertainty
- Revenue recognition issues

### Operational Risk
- Service delivery failures
- Key person dependency
- Technology obsolescence
- Supply chain disruption
- Business continuity gaps

### Strategic Risk
- Competitive positioning impact
- Market access limitations
- Reputational damage potential
- Vendor lock-in
- Exit cost exposure

### Compliance Risk
- Anti-bribery / FCPA violations
- Sanctions and export controls
- Environmental regulations
- Industry-specific regulations (HIPAA, SOX, GDPR)
- Reporting and disclosure requirements

### Risk Severity Scale
- Critical (9-10): Immediate action required, potential material impact
- High (7-8): Near-term action needed, significant exposure
- Medium (4-6): Monitor and mitigate, manageable exposure
- Low (1-3): Acceptable risk, standard provisions adequate
'@
    }

    @{
        Name        = 'defined-terms'
        Description = 'Legal defined terms glossary for document analysis'
        Content     = @'
## Legal Defined Terms Glossary

### Common Defined Terms
- **Affiliate**: Entity controlling, controlled by, or under common control (typically >50% ownership)
- **Business Day**: Monday-Friday excluding federal/bank holidays in specified jurisdiction
- **Change of Control**: Acquisition of >50% voting securities, merger, or sale of substantially all assets
- **Confidential Information**: Non-public information disclosed in connection with the agreement
- **Deliverables**: Work product, materials, or outputs to be provided under the agreement
- **Effective Date**: Date agreement becomes binding (execution date or specified date)
- **Force Majeure**: Events beyond reasonable control preventing performance
- **Indemnified Party / Indemnifying Party**: Party receiving / providing indemnification
- **Intellectual Property**: Patents, copyrights, trademarks, trade secrets, and other IP rights
- **Material Adverse Effect**: Change that significantly impairs ability to perform or value of transaction
- **Person**: Individual, corporation, partnership, LLC, trust, or other legal entity
- **Representatives**: Officers, directors, employees, agents, advisors, and contractors
- **Term**: Duration of the agreement from Effective Date to expiration or termination
- **Territory**: Geographic scope of rights or restrictions

### Interpretation Rules
- "Including" means "including without limitation"
- "Shall" and "will" are mandatory; "may" is permissive
- Headings are for convenience only and do not affect interpretation
- Singular includes plural and vice versa
- References to days mean calendar days unless "business days" specified
'@
    }
)

# ---------------------------------------------------------------------------
# Authentication
# ---------------------------------------------------------------------------
function Get-DataverseToken {
    param([string]$ResourceUrl)
    $resource = $ResourceUrl.TrimEnd('/')
    $token = az account get-access-token --resource $resource --query accessToken -o tsv 2>$null
    if (-not $token) {
        throw "Failed to acquire token via Azure CLI. Run 'az login' first."
    }
    return $token
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Seed Knowledge Scopes ==="
Write-Host "Environment : $DataverseUrl"
Write-Host "Records     : $($KnowledgeScopes.Count) knowledge scopes"
Write-Host "Mode        : $(if ($WhatIf) { 'WHATIF (preview only)' } else { 'LIVE' })"
Write-Host ""

if (-not $WhatIf) {
    Write-Host "Acquiring token via Azure CLI..."
    $token = Get-DataverseToken -ResourceUrl $DataverseUrl
    $headers = @{
        'Authorization' = "Bearer $token"
        'OData-MaxVersion' = '4.0'
        'OData-Version' = '4.0'
        'Accept' = 'application/json'
        'Content-Type' = 'application/json'
        'Prefer' = 'return=representation'
    }
}

$created = 0
$updated = 0
$unchanged = 0
$errors = 0

foreach ($scope in $KnowledgeScopes) {
    $name = $scope.Name
    Write-Host "[$name]"

    if ($WhatIf) {
        Write-Host "  WHATIF: Would create/update knowledge record '$name'"
        Write-Host "         Content: $([math]::Round($scope.Content.Length / 1024, 1)) KB"
        $created++
        continue
    }

    try {
        # Check if record exists
        $escapedName = $name.Replace("'", "''")
        $filter = "sprk_name eq '$escapedName'"
        $checkUrl = "$ApiUrl/sprk_analysisknowledges?`$filter=$([uri]::EscapeDataString($filter))&`$select=sprk_analysisknowledgeid,sprk_content&`$top=1"

        $existing = Invoke-RestMethod -Uri $checkUrl -Headers $headers -Method Get

        $body = @{
            sprk_name        = $name
            sprk_description = $scope.Description
            sprk_content     = $scope.Content
        } | ConvertTo-Json -Depth 10

        if ($existing.value.Count -gt 0) {
            $existingId = $existing.value[0].sprk_analysisknowledgeid
            $existingContent = $existing.value[0].sprk_content

            if ($existingContent -eq $scope.Content) {
                Write-Host "  UNCHANGED: Content already matches"
                $unchanged++
                continue
            }

            # Update existing record
            $updateUrl = "$ApiUrl/sprk_analysisknowledges($existingId)"
            Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $body | Out-Null
            Write-Host "  UPDATED: Content refreshed ($([math]::Round($scope.Content.Length / 1024, 1)) KB)"
            $updated++
        }
        else {
            # Create new record
            $createUrl = "$ApiUrl/sprk_analysisknowledges"
            Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $body | Out-Null
            Write-Host "  CREATED: New knowledge record ($([math]::Round($scope.Content.Length / 1024, 1)) KB)"
            $created++
        }
    }
    catch {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $errors++
    }
}

Write-Host ""
Write-Host "=== Summary ==="
Write-Host "Created   : $created"
Write-Host "Updated   : $updated"
Write-Host "Unchanged : $unchanged"
Write-Host "Errors    : $errors"

if ($errors -gt 0) {
    Write-Host ""
    Write-Host "Completed with $errors error(s)." -ForegroundColor Yellow
    exit 1
}
else {
    Write-Host ""
    Write-Host "All $($KnowledgeScopes.Count) knowledge scopes seeded successfully." -ForegroundColor Green
}
