<#
.SYNOPSIS
    Updates existing Analysis Skill prompt fragments in Dataverse with enriched content.

.DESCRIPTION
    Updates sprk_analysisskills records with richer, more structured prompt fragments.
    Matches on sprk_name (existing skill name). Does NOT create new records.

    Skills are plain-text prompt fragments that augment an Action's base prompt.
    JPS definitions reference them via "$ref": "skill:{name}".

.PARAMETER Environment
    Target environment: 'dev' (default).

.PARAMETER WhatIf
    Preview mode - shows what would be updated without making changes.

.EXAMPLE
    .\Seed-AnalysisSkills.ps1 -Environment dev
    .\Seed-AnalysisSkills.ps1 -WhatIf
#>
param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}
$ApiUrl = "$DataverseUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# Skill Updates: Map existing skill name -> enriched prompt fragment
# ---------------------------------------------------------------------------
# Only updates sprk_promptfragment. Name and description remain unchanged.
# Use single-quoted here-strings (@'...'@) to avoid PowerShell variable expansion.

$SkillUpdates = @(

    @{
        Name     = 'Contract Analysis'
        Fragment = @'
Perform a comprehensive contract analysis covering these areas:

1. PARTIES AND RELATIONSHIPS: Identify all parties with full legal names, entity types, roles, and defined terms used in the agreement.

2. SCOPE AND SERVICES: Describe the subject matter, deliverables, service levels, and any exclusions or limitations on scope.

3. KEY DATES AND TERM: Extract effective date, expiration, renewal terms, notice periods, and milestone deadlines. Flag approaching deadlines.

4. FINANCIAL TERMS: Document all fees, payment schedules, escalation mechanisms, expense provisions, and financial obligations.

5. OBLIGATIONS: Map each party's obligations with deadlines, conditions, and consequences of non-performance.

6. TERMINATION: Analyze termination triggers (convenience, cause, insolvency), cure periods, surviving obligations, and exit costs.

7. RISK AND LIABILITY: Evaluate limitation of liability, indemnification, insurance requirements, and consequential damages treatment. Score overall risk 1-10.

8. INTELLECTUAL PROPERTY: Determine ownership model (work-for-hire, license, joint), pre-existing IP treatment, and IP indemnification.

9. CONFIDENTIALITY: Assess scope of confidential information, duration, permitted disclosures, and return/destruction obligations.

10. NON-STANDARD CLAUSES: Flag any unusual, one-sided, or potentially problematic provisions not typically found in this agreement type.

For each finding, cite the specific contract section. Flag items as [ACTION REQUIRED] or [REVIEW RECOMMENDED].
'@
    }

    @{
        Name     = 'Risk Assessment'
        Fragment = @'
Identify and categorize all risks using this framework:

RISK CATEGORIES:
- Legal: Regulatory non-compliance, litigation exposure, enforceability issues
- Financial: Payment default, unlimited liability, currency risk, tax uncertainty
- Operational: Service delivery failures, key person dependency, technology obsolescence
- Compliance: Anti-bribery (FCPA), sanctions, data privacy (GDPR/CCPA), industry regulations
- Strategic: Competitive impact, vendor lock-in, reputational damage, exit cost exposure

SEVERITY SCORING (1-10):
- Critical (9-10): Immediate action required, potential material impact
- High (7-8): Near-term action needed, significant exposure
- Medium (4-6): Monitor and mitigate, manageable exposure
- Low (1-3): Acceptable risk, standard provisions adequate

For each risk identified:
1. Describe the specific risk and its source (cite section)
2. Assign category and severity score
3. Assess likelihood (probable, possible, unlikely)
4. Estimate potential impact (financial, operational, reputational)
5. Recommend a specific mitigation action
6. Flag as [ACTION REQUIRED] if severity >= 7

Conclude with an overall risk rating and top 3 priority actions.
'@
    }

    @{
        Name     = 'NDA Review'
        Fragment = @'
Analyze this Non-Disclosure Agreement focusing on:

1. STRUCTURE: Is this mutual or unilateral? Identify disclosing and receiving parties.

2. DEFINITION OF CONFIDENTIAL INFORMATION:
   - Is it clearly defined with specific inclusions?
   - Are standard exclusions present (public domain, prior knowledge, independent development, third-party receipt)?
   - Is there a residuals clause? If so, assess its breadth.

3. PERMITTED USE AND DISCLOSURE:
   - Is the purpose limitation specific and adequate?
   - Who can receive disclosures (employees, advisors, affiliates)?
   - Are recipients bound by similar obligations?

4. OBLIGATIONS AND PROTECTIONS:
   - Standard of care (reasonable care, same as own confidential info)
   - Marking requirements (written, oral with follow-up)
   - Return/destruction obligations and timelines
   - Legally compelled disclosure provisions

5. TERM AND DURATION:
   - Agreement term vs. confidentiality obligation duration
   - Flag perpetual obligations or durations exceeding 5 years
   - Survival provisions after termination

6. RED FLAGS:
   - Non-compete provisions disguised as NDA terms
   - Overly broad definition without exclusions
   - No provision for legally compelled disclosure
   - Missing return/destruction requirements
   - One-sided obligations in a supposedly mutual NDA

7. ENFORCEABILITY: Governing law, jurisdiction, available remedies (injunctive relief, damages).
'@
    }

    @{
        Name     = 'Lease Review'
        Fragment = @'
Analyze this lease agreement covering:

1. RENT AND FINANCIAL TERMS:
   - Base rent amount and payment schedule
   - Escalation mechanism (fixed %, CPI, market reset)
   - Operating expenses allocation (NNN, modified gross, full service)
   - Security deposit amount and return conditions
   - Late payment penalties and grace periods

2. TERM AND RENEWAL:
   - Initial term length
   - Renewal options (number, duration, rent basis)
   - Notice periods for renewal exercise
   - Early termination rights (kick-out clauses) and fees

3. TENANT OBLIGATIONS:
   - Permitted use restrictions
   - Maintenance and repair responsibilities
   - Insurance requirements (types, amounts, additional insured)
   - Alteration and improvement rights
   - Assignment and subletting restrictions

4. LANDLORD OBLIGATIONS:
   - Structural maintenance and building systems
   - Common area maintenance
   - Tenant improvement allowance
   - Quiet enjoyment guarantee

5. KEY PROTECTIONS:
   - Exclusive use provisions
   - Co-tenancy requirements (retail)
   - Subordination, non-disturbance, and attornment (SNDA)
   - Casualty and condemnation provisions
   - Environmental provisions

6. BENCHMARKS: Compare key terms against market standards (escalation rate, TI allowance, free rent, expense caps).
'@
    }

    @{
        Name     = 'Employment Contract'
        Fragment = @'
Analyze this employment agreement focusing on:

1. COMPENSATION AND BENEFITS:
   - Base salary, bonus structure, commission plans
   - Equity/stock option grants (vesting schedule, acceleration triggers)
   - Benefits package (health, retirement, PTO)
   - Expense reimbursement and perquisites

2. TERM AND TERMINATION:
   - At-will vs. for-cause employment
   - Definition of "cause" for termination
   - Notice period requirements (both parties)
   - Severance terms and conditions

3. RESTRICTIVE COVENANTS:
   - Non-compete: scope, duration, geography (flag if >2 years or overly broad)
   - Non-solicitation: customers and employees, duration
   - Non-disclosure: scope, duration, survival
   - Enforceability assessment based on jurisdiction

4. INTELLECTUAL PROPERTY:
   - Invention assignment scope
   - Prior inventions exclusion
   - Work-for-hire provisions
   - Moral rights waiver (if applicable)

5. CHANGE OF CONTROL:
   - Golden parachute provisions
   - Equity acceleration on change of control
   - Retention bonuses or stay agreements

6. COMPLIANCE:
   - FLSA classification (exempt vs. non-exempt)
   - State-specific requirements (final paycheck, PTO payout)
   - Clawback provisions
   - Arbitration vs. litigation for disputes

Flag any provisions that may be unenforceable in the governing jurisdiction.
'@
    }

    @{
        Name     = 'Compliance Check'
        Fragment = @'
Verify document compliance against these regulatory frameworks:

1. DATA PRIVACY:
   - GDPR: Data processing agreements, legal basis, cross-border transfer mechanisms, DPO requirements
   - CCPA/CPRA: Consumer rights provisions, opt-out mechanisms, data sale restrictions
   - Data breach notification: Timeline, scope, notification parties
   - Data retention and deletion obligations

2. ANTI-CORRUPTION:
   - FCPA/UK Bribery Act representations and warranties
   - Anti-kickback provisions
   - Gift and entertainment limitations
   - Third-party due diligence requirements

3. FINANCIAL REGULATIONS:
   - SOX compliance (if applicable to public companies)
   - PCI DSS (payment card data handling)
   - AML/KYC requirements

4. INDUSTRY-SPECIFIC:
   - HIPAA (healthcare data protections)
   - ITAR/EAR (export controls)
   - Environmental regulations

For each compliance gap:
- Cite the specific regulatory requirement
- Identify the missing or inadequate provision
- Assess potential legal/financial impact
- Recommend specific remediation language
'@
    }

    @{
        Name     = 'Clause Comparison'
        Fragment = @'
Compare document clauses against standard terms and best practices:

1. For each key clause, assess:
   - How does it compare to industry-standard language?
   - Is it more favorable to one party? Which one?
   - What specific language differs from standard?
   - What is the practical impact of the difference?

2. COMPARISON CATEGORIES:
   - Balanced: Both parties treated equitably
   - Favorable (to client): Better than standard terms
   - Unfavorable (to client): Worse than standard terms
   - Non-standard: Unusual language requiring review
   - Missing: Expected clause not present

3. For each unfavorable or non-standard clause:
   - Quote the specific problematic language
   - Explain why it deviates from standard
   - Suggest alternative standard language
   - Assess negotiation leverage (must-have vs. nice-to-have)

4. Output a comparison matrix: Clause | Standard | Document | Assessment | Action
'@
    }

    @{
        Name     = 'Invoice Processing'
        Fragment = @'
Extract and validate invoice data:

1. HEADER INFORMATION:
   - Invoice number, date, due date
   - Vendor name, address, tax ID
   - Customer/bill-to information
   - Purchase order reference

2. LINE ITEMS: For each line item extract:
   - Description, quantity, unit price, extended amount
   - Tax rate and tax amount per line
   - Discount applied (if any)

3. TOTALS:
   - Subtotal, tax total, shipping/handling, grand total
   - Verify mathematical accuracy (flag discrepancies)

4. VALIDATION CHECKS:
   - Duplicate invoice detection indicators (same vendor + similar amount + close date)
   - Round dollar amount flag (potential fraud indicator)
   - Amount vs. approval threshold check
   - Payment terms consistency with standard vendor terms
   - Tax rate verification against jurisdiction

5. Flag any anomalies: missing fields, calculation errors, potential fraud indicators, or compliance issues.
'@
    }

    @{
        Name     = 'SLA Analysis'
        Fragment = @'
Extract and evaluate SLA provisions:

1. AVAILABILITY METRICS:
   - Uptime commitment (99.9%, 99.99%, etc.)
   - Measurement period (monthly, quarterly, annual)
   - Calculation methodology (included/excluded downtime)
   - Planned maintenance windows and notification requirements

2. RESPONSE AND RESOLUTION:
   - Severity tier definitions (P1/Critical through P4/Low)
   - Response time commitments per tier
   - Resolution time targets per tier
   - Escalation procedures and contacts

3. PERFORMANCE METRICS:
   - Throughput, latency, error rate targets
   - Reporting frequency and format
   - Measurement tools and methodology

4. REMEDIES:
   - Service credit calculation (% of monthly fee per % below target)
   - Maximum service credits per period
   - Credit request process and deadlines
   - Termination rights for repeated SLA failures

5. BENCHMARK ASSESSMENT:
   - Compare commitments against industry standards
   - Flag any SLAs significantly below market
   - Identify missing metrics for the service type
   - Assess whether remedies are meaningful (are credits capped too low?)
'@
    }

    @{
        Name     = 'Executive Summary'
        Fragment = @'
Generate an executive summary following these guidelines:

1. LENGTH: 150-300 words maximum - concise enough for a busy executive.

2. STRUCTURE:
   - Opening: Document type, parties, and primary purpose (1-2 sentences)
   - Key Terms: Most important financial, timeline, and scope terms (2-3 sentences)
   - Risk Highlights: Top 2-3 risk items requiring attention (1-2 sentences each)
   - Recommendation: Clear action recommendation (approve, negotiate, reject, escalate)

3. TONE:
   - Business-focused, not legal jargon
   - Quantify where possible (dollar values, year terms, day notice periods)
   - Lead with the most important information
   - Flag urgency if deadlines are approaching

4. DO NOT include:
   - Legal citations or section references (save for detailed analysis)
   - Hedging language ("it appears", "it seems")
   - Boilerplate findings (only noteworthy items)
'@
    }

    @{
        Name     = 'Termination Analysis'
        Fragment = @'
Analyze all termination provisions:

1. TERMINATION TRIGGERS:
   - For convenience: which parties, notice period required
   - For cause: what constitutes "cause", cure period length
   - For insolvency/bankruptcy
   - For change of control
   - Automatic termination conditions

2. EXIT IMPLICATIONS:
   - Surviving obligations (which sections, for how long)
   - Data return/destruction requirements and timelines
   - Transition assistance obligations
   - Final payment obligations (accelerated payments, wind-down fees)
   - Non-compete/non-solicitation activation
   - IP rights upon termination (license continuation, escrow)

3. FLAG these issues:
   - One-sided termination rights (only one party can terminate for convenience)
   - Unreasonably short cure periods (less than 15 days)
   - Vague "material breach" definitions
   - Missing transition assistance requirements
   - No data return/destruction obligations
'@
    }

    @{
        Name     = 'Obligation Mapping'
        Fragment = @'
Map all obligations into a structured framework:

For EACH obligation identified:
1. PARTY: Which party bears the obligation
2. OBLIGATION: Specific action or restraint required
3. CATEGORY: Performance, Payment, Reporting, Compliance, Confidentiality, Insurance, Post-Termination
4. TRIGGER: When it must be performed (deadline, event, ongoing)
5. CONSEQUENCE: What happens if breached (penalty, termination right, indemnification)
6. SECTION: Contract section reference

FLAG any obligations that are:
- Ambiguously worded (unclear who, what, or when)
- Potentially conflicting with other obligations in the document
- Missing deadlines or trigger conditions
- Disproportionately assigned to one party

Output as a structured table: Party | Obligation | Category | Trigger/Deadline | Consequence | Section
'@
    }

    @{
        Name     = 'Party Identification'
        Fragment = @'
Identify ALL parties, entities, and individuals:

1. PRIMARY PARTIES:
   - Full legal name (including entity type: Inc., LLC, Ltd.)
   - Role in the agreement (buyer, seller, licensor, service provider)
   - Jurisdiction of organization
   - Defined term used in the agreement ("Company", "Vendor")

2. SECONDARY PARTIES:
   - Affiliates and subsidiaries mentioned
   - Guarantors or sureties
   - Third-party beneficiaries
   - Agents or representatives

3. SIGNATORIES:
   - Name and title of each signatory
   - Authority basis (officer, authorized representative)
   - Date of signature

4. FLAG: Mismatched entity names between preamble and signature block, missing entity type designations, unclear signatory authority.
'@
    }

    @{
        Name     = 'Risk Flagging'
        Fragment = @'
Highlight clauses requiring legal review using this severity framework:

[RISK: CRITICAL] - Immediate legal review required. Includes: unlimited liability, no indemnification, IP assignment without consideration, governing law in hostile jurisdiction.

[RISK: HIGH] - Negotiate before signing. Includes: one-sided indemnification, liability cap below 1x annual fees, automatic renewal without notice, broad assignment rights.

[RISK: MEDIUM] - Acceptable with awareness. Includes: cure period under 20 days, non-compete exceeding 1 year, broad force majeure definition, limited audit rights.

[RISK: LOW] - Standard commercial terms. Includes: mutual indemnification, standard liability cap (1-2x annual value), 30-day termination notice, balanced obligations.

For each flagged clause:
- Quote the specific language
- Cite the section reference
- Explain why it is risky
- Suggest alternative standard language
'@
    }

    @{
        Name     = 'Summary Generation'
        Fragment = @'
Generate a summary with these components:

1. KEY FINDINGS (3-5 bullet points):
   - Lead with the most important finding
   - Each bullet should be one actionable sentence
   - Include quantified data where available

2. CRITICAL ITEMS requiring immediate attention (if any):
   - Flag with [ACTION REQUIRED]
   - Include deadline if time-sensitive

3. RECOMMENDATIONS (2-3 items):
   - Specific, actionable next steps
   - Prioritized by urgency and impact

Keep the total summary under 500 words. Use business language, not legal jargon.
'@
    }

    @{
        Name     = 'Date Extraction'
        Fragment = @'
Extract and normalize ALL dates and time-sensitive provisions:

1. FIXED DATES: Effective date, expiration, signature dates, milestones. Normalize to ISO 8601 format (YYYY-MM-DD).

2. CALCULATED DEADLINES: Notice periods, cure periods, renewal exercise dates, payment due dates. Show the calculation method.

3. RECURRING OBLIGATIONS: Periodic reporting, insurance renewals, audit windows, annual escalation dates.

For each date:
- The specific date or calculation
- What triggers or depends on this date
- Consequence of missing it
- Section reference

Output as a Key Dates table: Date | Description | Trigger/Consequence | Section

FLAG: Ambiguous time references ("promptly", "reasonable time"), conflicting dates across sections, missing deadlines for key obligations.
'@
    }

    @{
        Name     = 'Financial Terms'
        Fragment = @'
Extract ALL monetary amounts and financial obligations:

1. FEES: Base fees, variable fees, one-time charges, reimbursable expenses (with caps if stated).
2. PAYMENT SCHEDULE: Due dates, payment terms (Net 30/60), early payment discounts, late penalties.
3. ESCALATION: Annual increases (fixed %, CPI, market), price protection, volume discounts.
4. LIABILITY: Liability cap amount, indemnification caps, insurance minimums.
5. TERMINATION COSTS: Early termination fees, wind-down costs, accelerated payments.

Output as a Financial Summary table: Item | Amount | Frequency | Conditions | Section

Flag any unclear total cost of ownership, aggressive escalation (>5% annual), or missing expense caps.
'@
    }

    @{
        Name     = 'Jurisdiction and Governing Law'
        Fragment = @'
Identify and assess all jurisdictional provisions:

1. GOVERNING LAW: Applicable law, jurisdiction, and whether it favors a particular party.
2. DISPUTE RESOLUTION: Negotiation, mediation, arbitration, or litigation path. Identify administering body (AAA, JAMS, ICC).
3. VENUE: Where disputes must be filed. Assess convenience for both parties.
4. JURY WAIVER: Present or absent.
5. CLASS ACTION WAIVER: Present or absent.
6. ATTORNEYS FEES: Prevailing party provision or American rule.

FLAG: Governing law in unfavorable jurisdiction, mandatory arbitration without appeal rights, inconvenient venue requirements, missing dispute resolution mechanism.
'@
    }

    @{
        Name     = 'Citation Extraction'
        Fragment = @'
For every factual claim or finding in your analysis:

1. Cite the specific contract section: [Section X.Y] or [Clause Z(a)]
2. For direct quotes, use exact language in quotation marks with section reference
3. For paraphrased findings, reference the section where the information appears
4. If a finding spans multiple sections, cite all relevant sections
5. If information is NOT found in the document, explicitly state "Not addressed in the document"
6. Never infer or assume facts not explicitly stated in the document

Format: "Finding text [Section: X.Y, Page: N]" where page numbers are available.
'@
    }

    @{
        Name     = 'Defined Terms'
        Fragment = @'
Extract all defined terms from the document:

1. For each defined term:
   - The term as written (in quotes)
   - Its full definition
   - Where it is defined (section reference)
   - Where it is used (key sections referencing it)

2. Flag any issues:
   - Terms used but never defined
   - Terms defined but never used
   - Circular definitions (Term A defined using Term B which uses Term A)
   - Inconsistent usage (term used differently than defined)

Output as an alphabetical glossary: Term | Definition | Defined In | Used In
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
    if (-not $token) { throw "Failed to acquire token. Run 'az login' first." }
    return $token
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Update Analysis Skills ==="
Write-Host "Environment : $DataverseUrl"
Write-Host "Skills      : $($SkillUpdates.Count) to update"
Write-Host "Mode        : $(if ($WhatIf) { 'WHATIF' } else { 'LIVE' })"
Write-Host ""

if (-not $WhatIf) {
    Write-Host "Acquiring token..."
    $token = Get-DataverseToken -ResourceUrl $DataverseUrl
    $headers = @{
        'Authorization' = "Bearer $token"
        'OData-MaxVersion' = '4.0'
        'OData-Version' = '4.0'
        'Accept' = 'application/json'
        'Content-Type' = 'application/json'
    }
}

$updated = 0; $unchanged = 0; $notFound = 0; $errors = 0

foreach ($skill in $SkillUpdates) {
    $name = $skill.Name
    Write-Host "[$name]"

    if ($WhatIf) {
        Write-Host "  WHATIF: Would update fragment ($([math]::Round($skill.Fragment.Length / 1024, 1)) KB)"
        $updated++
        continue
    }

    try {
        $escaped = $name.Replace("'", "''")
        $filter = "sprk_name eq '$escaped'"
        $url = "$ApiUrl/sprk_analysisskills?`$filter=$([uri]::EscapeDataString($filter))&`$select=sprk_analysisskillid,sprk_promptfragment&`$top=1"
        $existing = Invoke-RestMethod -Uri $url -Headers $headers -Method Get

        if ($existing.value.Count -eq 0) {
            Write-Host "  NOT FOUND: No skill with name '$name'"
            $notFound++
            continue
        }

        $id = $existing.value[0].sprk_analysisskillid
        $currentFragment = $existing.value[0].sprk_promptfragment

        if ($currentFragment -eq $skill.Fragment) {
            Write-Host "  UNCHANGED"
            $unchanged++
            continue
        }

        $body = @{ sprk_promptfragment = $skill.Fragment } | ConvertTo-Json -Depth 5
        Invoke-RestMethod -Uri "$ApiUrl/sprk_analysisskills($id)" -Headers $headers -Method Patch -Body $body | Out-Null
        Write-Host "  UPDATED: $([math]::Round($skill.Fragment.Length / 1024, 1)) KB (was $([math]::Round($currentFragment.Length / 1024, 1)) KB)"
        $updated++
    }
    catch {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $errors++
    }
}

Write-Host ""
Write-Host "=== Summary ==="
Write-Host "Updated   : $updated"
Write-Host "Unchanged : $unchanged"
Write-Host "Not Found : $notFound"
Write-Host "Errors    : $errors"

if ($errors -gt 0) { exit 1 }
