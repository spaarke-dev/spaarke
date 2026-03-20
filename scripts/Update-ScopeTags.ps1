<#
.SYNOPSIS
    Populates sprk_tags on actions, skills, and tools in Dataverse.

.DESCRIPTION
    Updates all coded scope records (ACT-*, SKL-*, TL-*) with comma-separated
    tags for use by the playbook design skill and scope-model-index refresh.

.PARAMETER Environment
    Target environment. Default: dev

.PARAMETER DryRun
    Preview changes without modifying Dataverse.
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
Write-Host '=== Update Scope Tags ===' -ForegroundColor Cyan
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

function Update-Records {
    param(
        [string]$EntitySet,
        [string]$IdField,
        [string]$CodeField,
        [hashtable[]]$Records,
        [string]$Label
    )

    Write-Host ''
    Write-Host "[$Label] Updating $($Records.Count) records..." -ForegroundColor Yellow

    # Fetch all records to build code->ID map
    $result = Invoke-RestMethod -Uri "$ApiBase/$EntitySet`?`$select=$IdField,$CodeField,sprk_name,sprk_tags&`$top=200" -Headers $headers
    $codeMap = @{}
    foreach ($r in $result.value) {
        $code = $r.$CodeField
        if ($code) { $codeMap[$code] = $r.$IdField }
    }

    $count = 0
    foreach ($rec in $Records) {
        $id = $codeMap[$rec.Code]
        if (-not $id) {
            Write-Warning "  $($rec.Code) not found in Dataverse — skipping"
            continue
        }

        $body = @{ sprk_tags = $rec.Tags }
        if ($DryRun) {
            Write-Host "  WOULD UPDATE: $($rec.Code) | tags: $($rec.Tags)" -ForegroundColor Gray
        } else {
            try {
                $jsonBody = $body | ConvertTo-Json -Compress
                Invoke-RestMethod -Uri "$ApiBase/$EntitySet($id)" -Headers $headers -Method Patch `
                    -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
                Write-Host "  Updated: $($rec.Code) | tags: $($rec.Tags)" -ForegroundColor Green
            } catch {
                Write-Warning "  Failed to update $($rec.Code): $($_.Exception.Message)"
            }
        }
        $count++
    }
    Write-Host "  $count record(s) processed." -ForegroundColor White
}

# ---------------------------------------------------------------------------
# Action tags
# ---------------------------------------------------------------------------
$actionTags = @(
    @{ Code = 'ACT-001'; Tags = 'contract, review, analysis, commercial, agreement' }
    @{ Code = 'ACT-002'; Tags = 'nda, non-disclosure, confidentiality, review' }
    @{ Code = 'ACT-003'; Tags = 'lease, real-estate, commercial, tenant, landlord' }
    @{ Code = 'ACT-004'; Tags = 'invoice, accounts-payable, financial, processing, validation' }
    @{ Code = 'ACT-005'; Tags = 'sla, service-level, performance, metrics, uptime' }
    @{ Code = 'ACT-006'; Tags = 'employment, agreement, compensation, non-compete, labor' }
    @{ Code = 'ACT-007'; Tags = 'sow, statement-of-work, deliverables, milestones, scope' }
    @{ Code = 'ACT-008'; Tags = 'general, legal, document, review, analysis, multi-type' }
    @{ Code = 'ACT-009'; Tags = 'response, drafting, reply, correspondence' }
    @{ Code = 'ACT-010'; Tags = 'extraction, entities, parties, names, organizations' }
    @{ Code = 'ACT-011'; Tags = 'profiler, classification, document-type, triage, metadata' }
    @{ Code = 'ACT-012'; Tags = 'search, semantic, rag, retrieval, query' }
    @{ Code = 'ACT-013'; Tags = 'comparison, clauses, diff, redline, benchmark' }
    @{ Code = 'ACT-014'; Tags = 'dates, extraction, deadlines, milestones, timeline' }
    @{ Code = 'ACT--015'; Tags = 'calculation, financial, values, formulas, computation' }
    @{ Code = 'ACT-016'; Tags = 'extraction, data, structured-output, fields, parsing' }
    @{ Code = 'ACT-017'; Tags = 'chat, assistant, qa, document, conversational' }
    @{ Code = 'ACT-018'; Tags = 'agreement, review, terms, provisions, analysis' }
    @{ Code = 'ACT-019'; Tags = 'comparison, documents, diff, side-by-side, versions' }
    @{ Code = 'ACT-020'; Tags = 'clauses, analysis, interpretation, provisions, breakdown' }
    @{ Code = 'ACT-021'; Tags = 'classification, document-type, triage, categorization' }
    @{ Code = 'ACT-022'; Tags = 'risk, detection, red-flags, compliance, assessment' }
)

Update-Records -EntitySet 'sprk_analysisactions' -IdField 'sprk_analysisactionid' `
    -CodeField 'sprk_actioncode' -Records $actionTags -Label '2/4 Actions'

# ---------------------------------------------------------------------------
# Skill tags
# ---------------------------------------------------------------------------
$skillTags = @(
    @{ Code = 'SKL-001'; Tags = 'citation, extraction, sourcing, references, evidence' }
    @{ Code = 'SKL-002'; Tags = 'risk, flagging, red-flags, warnings, assessment' }
    @{ Code = 'SKL-003'; Tags = 'summary, generation, overview, synopsis, condensation' }
    @{ Code = 'SKL-004'; Tags = 'date, extraction, deadlines, timeline, milestones' }
    @{ Code = 'SKL-005'; Tags = 'party, identification, names, entities, organizations' }
    @{ Code = 'SKL-006'; Tags = 'obligation, mapping, duties, commitments, requirements' }
    @{ Code = 'SKL-007'; Tags = 'defined-terms, glossary, definitions, terminology' }
    @{ Code = 'SKL-008'; Tags = 'financial, terms, payment, pricing, monetary' }
    @{ Code = 'SKL-009'; Tags = 'termination, exit, cancellation, expiration, renewal' }
    @{ Code = 'SKL-010'; Tags = 'jurisdiction, governing-law, venue, choice-of-law, disputes' }
    @{ Code = 'SKL-011'; Tags = 'action-oriented, recommendations, next-steps, actionable' }
    @{ Code = 'SKL-012'; Tags = 'clause, comparison, benchmark, diff, standard-vs-actual' }
    @{ Code = 'SKL-013'; Tags = 'compliance, check, regulatory, audit, validation' }
    @{ Code = 'SKL-014'; Tags = 'concise, writing, brevity, clear, succinct' }
    @{ Code = 'SKL-015'; Tags = 'contract, analysis, review, commercial, agreement' }
    @{ Code = 'SKL-018'; Tags = 'detailed, explanation, thorough, comprehensive, depth' }
    @{ Code = 'SKL-020'; Tags = 'employment, contract, labor, compensation, benefits' }
    @{ Code = 'SKL-021'; Tags = 'executive-summary, high-level, overview, brief' }
    @{ Code = 'SKL-022'; Tags = 'executive-summary, format, structure, layout, presentation' }
    @{ Code = 'SKL-023'; Tags = 'financial, expertise, accounting, valuation, fiscal' }
    @{ Code = 'SKL-025'; Tags = 'friendly, tone, approachable, informal, conversational' }
    @{ Code = 'SKL-026'; Tags = 'invoice, processing, ap, payment, validation' }
    @{ Code = 'SKL-028'; Tags = 'lease, review, real-estate, commercial, tenant' }
    @{ Code = 'SKL-029'; Tags = 'legal, expertise, jurisprudence, statutory, case-law' }
    @{ Code = 'SKL-030'; Tags = 'matter, extraction, pre-fill, entity, fields' }
    @{ Code = 'SKL-031'; Tags = 'nda, review, confidentiality, non-disclosure' }
    @{ Code = 'SKL-035'; Tags = 'professional, tone, formal, business, corporate' }
    @{ Code = 'SKL-037'; Tags = 'risk, assessment, scoring, evaluation, exposure' }
    @{ Code = 'SKL-043'; Tags = 'sla, analysis, service-level, performance, metrics' }
    @{ Code = 'SKL-044'; Tags = 'structured, headers, formatting, organization, sections' }
    @{ Code = 'SKL-046'; Tags = 'technical, expertise, engineering, systems, it' }
)

Update-Records -EntitySet 'sprk_analysisskills' -IdField 'sprk_analysisskillid' `
    -CodeField 'sprk_skillcode' -Records $skillTags -Label '3/4 Skills'

# ---------------------------------------------------------------------------
# Tool tags
# ---------------------------------------------------------------------------
$toolTags = @(
    @{ Code = 'TL-001'; Tags = 'search, document, retrieval, rag, semantic' }
    @{ Code = 'TL-002'; Tags = 'analysis, retrieval, context, previous-results' }
    @{ Code = 'TL-003'; Tags = 'knowledge, retrieval, reference, lookup, context' }
    @{ Code = 'TL-004'; Tags = 'text, refinement, editing, rewriting, polish' }
    @{ Code = 'TL-005'; Tags = 'citation, extraction, references, sourcing, evidence' }
    @{ Code = 'TL-006'; Tags = 'summary, generation, condensation, overview' }
    @{ Code = 'TL-007'; Tags = 'risk, red-flags, detection, compliance, warnings' }
    @{ Code = 'TL-008'; Tags = 'party, extraction, names, entities, organizations' }
    @{ Code = 'TL-010'; Tags = 'profiler, document-type, classification, metadata, triage' }
)

Update-Records -EntitySet 'sprk_analysistools' -IdField 'sprk_analysistoolid' `
    -CodeField 'sprk_toolcode' -Records $toolTags -Label '4/4 Tools'

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host 'Summary' -ForegroundColor Yellow
Write-Host "  Actions: $($actionTags.Count) tagged" -ForegroundColor White
Write-Host "  Skills : $($skillTags.Count) tagged" -ForegroundColor White
Write-Host "  Tools  : $($toolTags.Count) tagged" -ForegroundColor White

if ($DryRun) {
    Write-Host "`n=== DRY RUN COMPLETE ===" -ForegroundColor Yellow
} else {
    Write-Host "`n=== Tags updated ===" -ForegroundColor Green
}
