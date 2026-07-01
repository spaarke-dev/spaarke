<#
.SYNOPSIS
    Enumerates every sprk_playbooknode row in the target Dataverse environment
    and emits a per-node review CSV for the owner to backfill `sprk_executortype`.

.DESCRIPTION
    spaarke-ai-platform-unification-r7 — Wave 5 task 050 (FR-19).

    READ-ONLY review tool. Does NOT mutate any Dataverse row.

    R7 reforms playbook dispatch so the orchestrator reads `node.sprk_executortype`
    directly (single-hop) instead of walking the legacy
    `node → Action.actiontypeid → lookup_row.executoractiontype` chain. After
    Wave 2 ships, the new column is load-bearing — but the ~94 existing
    `sprk_playbooknode` rows in spaarkedev1 have a NULL `sprk_executortype`
    until the owner manually backfills them. Per design.md / spec FR-19, the
    owner chose manual per-node review (94 rows is small; owner has the
    authoritative knowledge for ambiguous cases).

    This script enumerates every node row with its current Action lookup, the
    Action's advisory `sprk_executoractiontype` value (legacy hint), the parent
    playbook name, and a HEURISTIC suggested ExecutorType derived from the
    Action name. The owner reviews the CSV (task 052), fills in the
    `owner_decision_executortype` + `owner_notes` columns, and the migration
    script in task 053 (`Migrate-PlaybookNodes-to-ExecutorType.ps1`) writes the
    reviewed values back to Dataverse.

    Idempotency: this is a READ-ONLY tool. Re-running queries Dataverse + emits
    a fresh CSV (overwrites if exists). Zero Dataverse state mutation. Re-runs
    are always safe.

    Inference logic (name-pattern → suggested ExecutorType, with confidence
    label HIGH / MEDIUM / LOW / NONE). The same pattern table is applied first
    to the Action name, then to the Node name as a fallback (downgraded one
    confidence tier: HIGH→MEDIUM, MEDIUM→LOW), then to the Action's advisory
    `sprk_executoractiontype` int (LOW). Many R7 nodes have descriptive names
    like "AI Analysis", "Deliver Output", "Update Record" but no Action FK
    (structural nodes), so node-name inference recovers most of them.
      - "*Summarize*", "*Narrate*", "*AI Completion*" → AiCompletion (1)
            confidence HIGH (R7's new prompt-only executor)
      - "*AI Analysis*"                              → AiAnalysis (0)  HIGH
      - "*Entity Name Validator*"                    → EntityNameValidator (141) HIGH
      - "*Create Notification*"                      → CreateNotification (50) HIGH
      - "*Update Record*"                            → UpdateRecord (22) HIGH
      - "*Create Task*"                              → CreateTask (20) HIGH
      - "*Send Email*"                               → SendEmail (21) HIGH
      - "*Live Fact*"                                → LiveFact (80) HIGH
      - "*Evidence Sufficiency*"                     → EvidenceSufficiency (100) HIGH
      - "*Decline*"                                  → DeclineToFind (110) HIGH
      - "*Return*Insight*", "*Return*Artifact*"      → ReturnInsightArtifact (120) HIGH
      - "*Lookup*Membership*", "*User Membership*"   → LookupUserMembership (52) HIGH
      - "*Index Retrieve*", "*Knowledge Retrieve*"   → IndexRetrieve (90) HIGH
      - "*Load Knowledge*"                           → LoadKnowledge (142) HIGH
      - "*Return Response*"                          → ReturnResponse (143) HIGH
      - "*Sanitization*", "*Sanitize*"               → Sanitization (130) HIGH
      - "*Grounding*Verify*"                         → GroundingVerify (70) HIGH
      - "*Observation*Emit*"                         → ObservationEmit (140) HIGH
      - "*Deliver*Composite*"                        → DeliverComposite (42) HIGH
      - "*Deliver*Output*", "*Deliver*"              → DeliverOutput (40) MEDIUM
      - Fallback: Action's `sprk_executoractiontype` advisory value
            (if non-null) maps to enum value → confidence LOW
      - No signal at all → blank → confidence NONE (owner must decide)

    The owner overrides any suggestion that is wrong for the node's actual
    behavior. Suggestion is a starting point, not authoritative.

.PARAMETER Environment
    Target Dataverse environment label. Default: dev.

.PARAMETER DataverseUrl
    Base Dataverse URL (e.g. https://spaarkedev1.crm.dynamics.com).
    Defaults to the DATAVERSE_URL env var.

.PARAMETER DryRun
    Preview mode: queries Dataverse, computes suggestions, prints summary table
    to the console, but does NOT write the CSV file.

.PARAMETER OutputPath
    Path to emit the CSV. Default:
    projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-input.csv
    Path is relative to the current working directory unless rooted.

.EXAMPLE
    # Dry run against spaarkedev1 (auth + query smoke; nothing written)
    .\Review-PlaybookNodes-Dispatch.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DryRun

.EXAMPLE
    # Live run: query + emit CSV (default output path)
    .\Review-PlaybookNodes-Dispatch.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    # Live run with custom output path
    .\Review-PlaybookNodes-Dispatch.ps1 `
      -DataverseUrl https://spaarkedev1.crm.dynamics.com `
      -OutputPath C:\tmp\node-review.csv

.NOTES
    Prerequisites:
      - PowerShell 7+
      - Azure CLI installed and authenticated (az login)
      - Read access to sprk_playbooknode + sprk_analysisaction + sprk_analysisplaybook
        in the target Dataverse environment

    This script is READ-ONLY: zero POST/PATCH/DELETE calls. Re-running has no
    effect on Dataverse state.

    Related:
      - Task 051 runs this script against spaarkedev1 + produces the input CSV
      - Task 052 owner-checkpoint: owner reviews + edits the CSV
      - Task 053 authors Migrate-PlaybookNodes-to-ExecutorType.ps1 (writes
        reviewed values back to Dataverse)
      - Spec FR-19 (backfill obligation), FR-07 (single-hop dispatch),
        FR-08 (structural fallback deletion), Wave 5 plan
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',

    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [switch]$DryRun,

    [string]$OutputPath = 'projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-input.csv'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# ExecutorType enum mapping (mirrors src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs)
# ---------------------------------------------------------------------------
# Source of truth: the C# ExecutorType enum. Keep these in lockstep. If a
# new value is added in C#, add it here AND extend the inference table below.
$ExecutorTypeMap = [ordered]@{
    AiAnalysis            = 0
    AiCompletion          = 1
    AiEmbedding           = 2
    RuleEngine            = 10
    Calculation           = 11
    DataTransform         = 12
    CreateTask            = 20
    SendEmail             = 21
    UpdateRecord          = 22
    CallWebhook           = 23
    SendTeamsMessage      = 24
    Condition             = 30
    Parallel              = 31
    Wait                  = 32
    Start                 = 33
    DeliverOutput         = 40
    DeliverToIndex        = 41
    DeliverComposite      = 42
    CreateNotification    = 50
    QueryDataverse        = 51
    LookupUserMembership  = 52
    AgentService          = 60
    GroundingVerify       = 70
    LiveFact              = 80
    IndexRetrieve         = 90
    EvidenceSufficiency   = 100
    DeclineToFind         = 110
    ReturnInsightArtifact = 120
    Sanitization          = 130
    ObservationEmit       = 140
    EntityNameValidator   = 141
    LoadKnowledge         = 142
    ReturnResponse        = 143
}

# Reverse lookup: int value → enum name (used to render advisory-field fallback)
$ExecutorTypeReverseMap = @{}
foreach ($kvp in $ExecutorTypeMap.GetEnumerator()) {
    $ExecutorTypeReverseMap[$kvp.Value] = $kvp.Key
}

# ---------------------------------------------------------------------------
# Validate inputs + build API base
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Review Playbook Nodes Dispatch (FR-19 / R7 task 050) ===' -ForegroundColor Cyan
Write-Host "Environment      : $EnvironmentUrl"
Write-Host "Output (intended): $OutputPath"
if ($DryRun) {
    Write-Host 'Mode             : DRY RUN (no CSV written)' -ForegroundColor Yellow
} else {
    Write-Host 'Mode             : LIVE (CSV will be written)' -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Step 1: Authenticate
# ---------------------------------------------------------------------------
Write-Host '=== Step 1: Authenticate ===' -ForegroundColor Cyan
try {
    $tokenJson = az account get-access-token --resource $EnvironmentUrl 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Error "az account get-access-token failed:`n$tokenJson"
        exit 1
    }
    $token = ($tokenJson | ConvertFrom-Json).accessToken
    if (-not $token) {
        Write-Error 'Failed to extract access token from az output.'
        exit 1
    }
    Write-Host '  OK - token acquired' -ForegroundColor Green
} catch {
    Write-Error "Authentication failed: $($_.Exception.Message)"
    exit 1
}

$headers = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

# ---------------------------------------------------------------------------
# Step 2: Query playbook nodes (with Action + Playbook expansion)
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 2: Query sprk_playbooknodes ===' -ForegroundColor Cyan

# Web API query: nodes + expand Action (name, advisory ExecutorActionType) +
# expand parent Playbook (name). $top=500 well over the ~94-row corpus per
# 2026-06-28 count; if the corpus ever exceeds 500, switch to paging via
# @odata.nextLink — out of scope for this task.
$nodeSelect = 'sprk_playbooknodeid,sprk_name,sprk_executortype,_sprk_actionid_value,_sprk_playbookid_value'
$actionExpand = 'sprk_actionid($select=sprk_name,sprk_executoractiontype)'
$playbookExpand = 'sprk_playbookid($select=sprk_name)'
$queryUri = "$ApiBase/sprk_playbooknodes?`$select=$nodeSelect&`$expand=$actionExpand,$playbookExpand&`$top=500"

try {
    $response = Invoke-RestMethod -Uri $queryUri -Headers $headers -Method Get
} catch {
    $errMsg = $_.ErrorDetails.Message
    if (-not $errMsg) { $errMsg = $_.Exception.Message }
    Write-Error "Query failed: $errMsg"
    exit 1
}

$nodes = @($response.value)
Write-Host "  Retrieved $($nodes.Count) node row(s)" -ForegroundColor Green

if ($nodes.Count -eq 0) {
    Write-Warning 'No nodes returned. Aborting — nothing to review.'
    exit 0
}

# ---------------------------------------------------------------------------
# Step 3: Inference helpers
# ---------------------------------------------------------------------------

# Pattern table — module-scope so both the Action-name pass and the Node-name
# fallback share one definition. Order matters: more-specific patterns first so
# "Summarize" doesn't get swallowed by "AI Analysis", and "Deliver Composite"
# beats the generic "Deliver" fallback. Each entry: @(regex, ExecutorTypeName, baseConfidence).
$NamePatterns = @(
        # High-confidence specific patterns
        @('(?i)summari[sz]e',                            'AiCompletion',          'HIGH'),
        @('(?i)narrate',                                 'AiCompletion',          'HIGH'),
        @('(?i)ai\s*completion',                         'AiCompletion',          'HIGH'),
        @('(?i)entity\s*name\s*validator',               'EntityNameValidator',   'HIGH'),
        @('(?i)create\s*notification',                   'CreateNotification',    'HIGH'),
        @('(?i)update\s*record',                         'UpdateRecord',          'HIGH'),
        @('(?i)create\s*task',                           'CreateTask',            'HIGH'),
        @('(?i)send\s*email',                            'SendEmail',             'HIGH'),
        @('(?i)send\s*teams',                            'SendTeamsMessage',      'HIGH'),
        @('(?i)live\s*fact',                             'LiveFact',              'HIGH'),
        @('(?i)evidence\s*sufficiency',                  'EvidenceSufficiency',   'HIGH'),
        @('(?i)decline',                                 'DeclineToFind',         'HIGH'),
        @('(?i)return.*insight|return.*artifact',        'ReturnInsightArtifact', 'HIGH'),
        @('(?i)lookup.*membership|user\s*membership',    'LookupUserMembership',  'HIGH'),
        @('(?i)index\s*retrieve|knowledge\s*retrieve',   'IndexRetrieve',         'HIGH'),
        @('(?i)load\s*knowledge',                        'LoadKnowledge',         'HIGH'),
        @('(?i)return\s*response',                       'ReturnResponse',        'HIGH'),
        @('(?i)saniti[sz]ation|saniti[sz]e',             'Sanitization',          'HIGH'),
        @('(?i)grounding.*verify',                       'GroundingVerify',       'HIGH'),
        @('(?i)observation.*emit',                       'ObservationEmit',       'HIGH'),
        @('(?i)deliver.*composite',                      'DeliverComposite',      'HIGH'),
        @('(?i)query\s*dataverse',                       'QueryDataverse',        'HIGH'),
        @('(?i)agent\s*service',                         'AgentService',          'HIGH'),
        @('(?i)rule\s*engine',                           'RuleEngine',            'HIGH'),
        @('(?i)data\s*transform',                        'DataTransform',         'HIGH'),
        @('(?i)call\s*webhook',                          'CallWebhook',           'HIGH'),
        @('(?i)ai\s*embedding|embedding',                'AiEmbedding',           'HIGH'),
        @('(?i)ai\s*analysis',                           'AiAnalysis',            'HIGH'),

        # Medium-confidence general patterns (less specific — owner verify)
        @('(?i)deliver',                                 'DeliverOutput',         'MEDIUM'),
        @('(?i)start',                                   'Start',                 'MEDIUM'),
        @('(?i)condition',                               'Condition',             'MEDIUM'),
        @('(?i)parallel',                                'Parallel',              'MEDIUM'),
        @('(?i)wait',                                    'Wait',                  'MEDIUM'),
        @('(?i)calculat',                                'Calculation',           'MEDIUM')
)

# Match a single name string against the pattern table. Returns @{Value;Label;Confidence}
# or $null. Confidence is the pattern's base confidence (no downgrade).
function Get-NamePatternMatch {
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }
    foreach ($pattern in $NamePatterns) {
        $regex = $pattern[0]
        $execName = $pattern[1]
        $conf = $pattern[2]
        if ($Name -match $regex) {
            return @{
                Value      = $ExecutorTypeMap[$execName]
                Label      = $execName
                Confidence = $conf
            }
        }
    }
    return $null
}

# Downgrade a confidence level by one tier (HIGH→MEDIUM, MEDIUM→LOW, LOW→LOW).
# Used when the match came from a fallback signal (node name) instead of the
# primary signal (Action name).
function Get-DowngradedConfidence {
    param([string]$Confidence)
    switch ($Confidence) {
        'HIGH'   { return 'MEDIUM' }
        'MEDIUM' { return 'LOW' }
        default  { return 'LOW' }
    }
}

# Returns @{Value;Label;Confidence;Source} for a node, or $null if no signal.
# Source ∈ {action-name, node-name, advisory-int}. Confidence ∈ {HIGH;MEDIUM;LOW;NONE}.
# Priority: Action name (full confidence) → Node name (downgraded one tier) →
# advisory int field (always LOW).
function Get-SuggestedExecutorType {
    param(
        [string]$ActionName,
        [string]$NodeName,
        [Nullable[int]]$AdvisoryExecutorActionType
    )

    # 1. Action name (primary signal, full base confidence)
    $match = Get-NamePatternMatch -Name $ActionName
    if ($null -ne $match) {
        $match.Source = 'action-name'
        return $match
    }

    # 2. Node name (fallback signal, downgrade one tier — node names are author
    #    intent rather than canonical Action contract)
    $match = Get-NamePatternMatch -Name $NodeName
    if ($null -ne $match) {
        $match.Confidence = Get-DowngradedConfidence -Confidence $match.Confidence
        $match.Source = 'node-name'
        return $match
    }

    # 3. Advisory int from sprk_executoractiontype (always LOW — legacy hint)
    if ($null -ne $AdvisoryExecutorActionType -and $ExecutorTypeReverseMap.ContainsKey($AdvisoryExecutorActionType)) {
        return @{
            Value      = $AdvisoryExecutorActionType
            Label      = $ExecutorTypeReverseMap[$AdvisoryExecutorActionType]
            Confidence = 'LOW'
            Source     = 'advisory-int'
        }
    }

    # Nothing — owner must decide
    return $null
}

# Render the current sprk_executortype value (may already be backfilled) as
# "0 (AiAnalysis)" or empty string if null. Useful so the CSV records what's
# already set so the owner can spot prior-state in flight.
function Format-CurrentExecutorType {
    param([Nullable[int]]$Value)
    if ($null -eq $Value) { return '' }
    $label = if ($ExecutorTypeReverseMap.ContainsKey($Value)) { $ExecutorTypeReverseMap[$Value] } else { 'UNKNOWN' }
    return "$Value ($label)"
}

# ---------------------------------------------------------------------------
# Step 4: Build CSV rows
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 3: Compute suggestions ===' -ForegroundColor Cyan

$rows = @()
$counts = @{ HIGH = 0; MEDIUM = 0; LOW = 0; NONE = 0 }
$alreadyBackfilled = 0

foreach ($node in $nodes) {
    $actionName = if ($node.sprk_actionid) { $node.sprk_actionid.sprk_name } else { $null }
    $playbookName = if ($node.sprk_playbookid) { $node.sprk_playbookid.sprk_name } else { $null }
    $advisory = if ($node.sprk_actionid -and $null -ne $node.sprk_actionid.sprk_executoractiontype) { [int]$node.sprk_actionid.sprk_executoractiontype } else { $null }

    $currentExecutorType = $null
    if ($null -ne $node.sprk_executortype) {
        $currentExecutorType = [int]$node.sprk_executortype
        $alreadyBackfilled++
    }

    $suggestion = Get-SuggestedExecutorType -ActionName $actionName -NodeName $node.sprk_name -AdvisoryExecutorActionType $advisory

    if ($null -ne $suggestion) {
        $counts[$suggestion.Confidence]++
        $suggestedValue = $suggestion.Value
        $suggestedLabel = $suggestion.Label
        $confidence = $suggestion.Confidence
        $source = $suggestion.Source
    } else {
        $counts['NONE']++
        $suggestedValue = ''
        $suggestedLabel = ''
        $confidence = 'NONE'
        $source = ''
    }

    $rows += [PSCustomObject]@{
        node_id                                 = $node.sprk_playbooknodeid
        node_name                               = $node.sprk_name
        playbook_name                           = $playbookName
        action_id                               = $node._sprk_actionid_value
        action_name                             = $actionName
        current_sprk_executortype               = (Format-CurrentExecutorType -Value $currentExecutorType)
        current_advisory_executoractiontype     = if ($null -ne $advisory) { $advisory } else { '' }
        suggested_executortype                  = $suggestedValue
        suggested_executortype_label            = $suggestedLabel
        confidence                              = $confidence
        suggestion_source                       = $source
        owner_decision_executortype             = ''
        owner_notes                             = ''
    }
}

Write-Host "  Computed suggestions for $($rows.Count) node(s)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 5: Emit CSV (or skip for DryRun)
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 4: Emit output ===' -ForegroundColor Cyan

if ($DryRun) {
    Write-Host '  [DRY RUN] CSV NOT written. Console preview (first 20 rows):' -ForegroundColor Yellow
    Write-Host ''
    $rows | Select-Object -First 20 | Format-Table -AutoSize -Property `
        @{Name='node_name';Expression={$_.node_name}},
        @{Name='playbook';Expression={$_.playbook_name}},
        @{Name='action';Expression={$_.action_name}},
        @{Name='advisory';Expression={$_.current_advisory_executoractiontype}},
        @{Name='suggested';Expression={"$($_.suggested_executortype) $($_.suggested_executortype_label)"}},
        @{Name='conf';Expression={$_.confidence}}
} else {
    # Ensure parent directory exists
    $outputDir = Split-Path -Path $OutputPath -Parent
    if ($outputDir -and -not (Test-Path -Path $outputDir)) {
        try {
            New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
            Write-Host "  Created parent directory: $outputDir" -ForegroundColor Gray
        } catch {
            Write-Error "Failed to create parent directory '$outputDir': $($_.Exception.Message)"
            exit 1
        }
    }

    try {
        $rows | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
        Write-Host "  CSV written: $OutputPath" -ForegroundColor Green
    } catch {
        Write-Error "Failed to write CSV to '$OutputPath': $($_.Exception.Message)"
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Step 6: Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
Write-Host ("  Total nodes                          : {0}" -f $rows.Count) -ForegroundColor White
Write-Host ("  Already backfilled (sprk_executortype set) : {0}" -f $alreadyBackfilled) -ForegroundColor White
Write-Host ("  HIGH-confidence suggestions          : {0}" -f $counts.HIGH)   -ForegroundColor Green
Write-Host ("  MEDIUM-confidence suggestions        : {0}" -f $counts.MEDIUM) -ForegroundColor Yellow
Write-Host ("  LOW-confidence suggestions (advisory): {0}" -f $counts.LOW)    -ForegroundColor Yellow
Write-Host ("  NONE — owner decision REQUIRED       : {0}" -f $counts.NONE)   -ForegroundColor Red
Write-Host ''

if ($DryRun) {
    Write-Host '=== DRY RUN COMPLETE ===' -ForegroundColor Yellow
    Write-Host 'Re-run without -DryRun to emit the CSV.' -ForegroundColor Yellow
} else {
    Write-Host '=== DONE ===' -ForegroundColor Green
    Write-Host 'NEXT STEPS:' -ForegroundColor Cyan
    Write-Host "  1. Owner reviews $OutputPath"
    Write-Host '  2. Owner fills in owner_decision_executortype + owner_notes columns per node'
    Write-Host '  3. Owner saves as playbook-node-review-output.csv (or any agreed name)'
    Write-Host '  4. Task 053 author Migrate-PlaybookNodes-to-ExecutorType.ps1 to write reviewed values'
}
Write-Host ''
