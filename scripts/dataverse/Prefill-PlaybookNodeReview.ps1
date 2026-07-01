<#
.SYNOPSIS
    Pre-fill the playbook-node-review CSV with script suggestions + best-guess NONE-row values.

.DESCRIPTION
    spaarke-ai-platform-unification-r7 — Wave 5 task 052 assist.

    Reads `notes/drafts/playbook-node-review-input.csv` (94 rows produced by
    Review-PlaybookNodes-Dispatch.ps1), pre-fills the `owner_decision_executortype`
    + `owner_notes` columns, writes `notes/drafts/playbook-node-review-output.csv`.

    Pre-fill rules:
      - HIGH / MEDIUM / LOW confidence  →  copy `suggested_executortype` directly;
        note format: "AUTO-COPY (confidence=X): script suggested {label}."
      - NONE confidence (16 rows)        →  apply per-node best-guess from $NoneGuesses
        hashtable below; note format: "AUTO-GUESS (NONE-row): {rationale}. VERIFY if uncertain."

    Owner workflow: open the output CSV, skim the AUTO-GUESS rows + any LOW rows
    of concern, edit `owner_decision_executortype` for any that look wrong, save in place.
    Then notify Claude to run Migrate-PlaybookNodes-to-ExecutorType.ps1 (task 053).

    Owner-edit-friendly: the migration script reads ONLY node_id +
    owner_decision_executortype. owner_notes is for human reference and is
    ignored at PATCH time. Edit the decision; leave notes as-is or update.

.PARAMETER InputCsv
    Path to the input CSV. Default: projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-input.csv

.PARAMETER OutputCsv
    Path to write the output CSV. Default: projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv

.PARAMETER Force
    Overwrite output CSV if it exists. Default: false (errors if exists, protects
    against clobbering owner's manual edits).

.NOTES
    Per CLAUDE.md §11 component justification:
      Existing: no pre-fill helper exists; manual Excel review was the alternative.
      Extension: can't extend Review-PlaybookNodes-Dispatch.ps1 (different intent: read+suggest vs read+suggest+prefill).
      Cost-of-doing-nothing: owner does 94-row manual review in Excel; ~60 min vs ~10 min spot-check.
#>

param(
    [string]$InputCsv  = 'projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-input.csv',
    [string]$OutputCsv = 'projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

# -- Per-node best guesses for the 16 NONE-confidence rows --
# Keyed by node_id (GUID). Each value: integer ExecutorType + human-readable rationale.
# Reasoning recorded in commit body for traceability.
$NoneGuesses = @{
    # Document Profile playbook
    'c9334fb7-a415-f111-8343-7c1e520aa4df' = @{ V = 22; R = 'Save Profile -> UpdateRecord (write profile output to Dataverse record). High confidence on Save -> write semantics.' }
    'ca334fb7-a415-f111-8343-7c1e520aa4df' = @{ V = 0;  R = 'Profile Document with action "Document Profiler" -> AiAnalysis (Profiler action implies LLM prompt extracting structured data).' }
    '4ce880b6-e11e-f111-88b3-7ced8d1dc988' = @{ V = 41; R = 'Index Document -> DeliverToIndex (Index = vector-store write).' }
    # Document Summary playbook
    '00602067-9a16-f111-8343-7c1e520aa4df' = @{ V = 0;  R = 'Document Profile Analysis with action "Document Profiler" -> AiAnalysis (LLM prompt).' }
    'e514cfab-9d16-f111-8343-7c1e520aa4df' = @{ V = 0;  R = 'Document Profile (no action) -> AiAnalysis (best guess for a Summary-pipeline analysis step; VERIFY - could also be 42 DeliverComposite).' }
    # New Documents on Your Matters playbook
    'a6041587-5f2d-f111-88b5-7c1e520aa4df' = @{ V = 30; R = 'Check Results -> Condition (branching node; "Check" implies routing).' }
    # New Events on Matters/Projects playbook
    '2e2fdd9e-5f2d-f111-88b5-7c1e520aa4df' = @{ V = 30; R = 'Has New Events? -> Condition (question-mark node name = branch).' }
    # U.S. Patent Office Action Review playbook
    'c5a64493-572f-f111-88b5-7c1e520aa4df' = @{ V = 0;  R = 'Document Analysis with action "General Legal Document Review" -> AiAnalysis (Review = LLM prompt).' }
    'a1ea4d99-572f-f111-88b5-7c1e520aa4df' = @{ V = 0;  R = 'Extract Key Info with action "Extract Data" -> AiAnalysis (extraction = LLM prompt).' }
    'a2ea4d99-572f-f111-88b5-7c1e520aa4df' = @{ V = 0;  R = 'Generate Output with action "Prepare Response" -> AiAnalysis (prompt-driven response generation; VERIFY - could also be 40 DeliverOutput if pure delivery).' }
    # New Emails on Matters playbook
    '15999f92-5f2d-f111-88b5-7c1e525abd8b' = @{ V = 30; R = 'Has New Emails? -> Condition (question-mark = branch).' }
    # Tasks Overdue playbook
    '358b6395-a171-f111-ab0d-7ced8ddc4a05' = @{ V = 30; R = 'Check Results -> Condition (branching node).' }
    # Create New Project Pre-Fill playbook
    'dacac491-4f6c-f111-ab0e-7ced8ddc4a05' = @{ V = 0;  R = 'Extract Project Fields -> AiAnalysis (pre-fill = AI extraction from document).' }
    # Tasks Due Soon playbook
    '326ee29a-a171-f111-ab0d-7ced8ddc4cc6' = @{ V = 30; R = 'Check Results -> Condition (branching node).' }
    # Matter/Project Activity Summary playbook
    'e0b7e6a0-a171-f111-ab0d-7ced8ddc4cc6' = @{ V = 30; R = 'Has Activity? -> Condition (question-mark = branch).' }
    # New Work Assignments playbook
    'e8b7e6a0-a171-f111-ab0d-7ced8ddc4cc6' = @{ V = 30; R = 'Has Assignments? -> Condition (question-mark = branch).' }
}

# -- Validate inputs --
if (-not (Test-Path $InputCsv)) {
    Write-Error "Input CSV not found: $InputCsv"
    exit 1
}
if ((Test-Path $OutputCsv) -and -not $Force) {
    Write-Error "Output CSV already exists: $OutputCsv -- pass -Force to overwrite (will clobber any owner edits!)"
    exit 1
}

Write-Host '==============================================='
Write-Host 'Pre-fill PlaybookNode Review CSV'
Write-Host "Input : $InputCsv"
Write-Host "Output: $OutputCsv"
Write-Host '==============================================='

$rows = Import-Csv $InputCsv
Write-Host "Loaded $($rows.Count) rows."
Write-Host ''

$counts = @{ AutoCopy = 0; AutoGuess = 0; Unmapped = 0 }
$unmapped = @()
$noneRows = @()

foreach ($row in $rows) {
    if ($row.confidence -eq 'NONE') {
        if ($NoneGuesses.ContainsKey($row.node_id)) {
            $guess = $NoneGuesses[$row.node_id]
            $row.owner_decision_executortype = $guess.V
            $row.owner_notes = "AUTO-GUESS (NONE-row): $($guess.R)"
            $counts.AutoGuess++
            $noneRows += "  - node $($row.node_name) ($($row.playbook_name)) -> $($guess.V)"
        } else {
            # Unmapped NONE row -- leave blank for owner
            $counts.Unmapped++
            $unmapped += "  - $($row.node_id) | $($row.node_name) | $($row.playbook_name)"
        }
    } elseif (-not [string]::IsNullOrEmpty($row.suggested_executortype)) {
        $row.owner_decision_executortype = $row.suggested_executortype
        $row.owner_notes = "AUTO-COPY (confidence=$($row.confidence)): script suggested $($row.suggested_executortype_label) ($($row.suggested_executortype))."
        $counts.AutoCopy++
    } else {
        # Should not happen given the input shape; log
        $counts.Unmapped++
        $unmapped += "  - $($row.node_id) | $($row.node_name) | $($row.playbook_name) (no suggestion, not NONE)"
    }
}

# Write output
$rows | Export-Csv -Path $OutputCsv -NoTypeInformation
Write-Host "=== Pre-fill summary ==="
Write-Host "  AUTO-COPY (HIGH/MEDIUM/LOW): $($counts.AutoCopy) rows"
Write-Host "  AUTO-GUESS (NONE -> per-node mapping): $($counts.AutoGuess) rows"
Write-Host "  UNMAPPED (left blank for owner): $($counts.Unmapped) rows"
Write-Host ''
if ($unmapped.Count -gt 0) {
    Write-Host '  Unmapped rows requiring manual fill-in:' -ForegroundColor Yellow
    $unmapped | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}
if ($noneRows.Count -gt 0) {
    Write-Host ''
    Write-Host '  AUTO-GUESS rows worth spot-checking:' -ForegroundColor Cyan
    $noneRows | ForEach-Object { Write-Host $_ -ForegroundColor Cyan }
}
Write-Host ''
Write-Host "Output written to: $OutputCsv" -ForegroundColor Green
Write-Host 'Next: skim AUTO-GUESS rows; edit owner_decision_executortype for any you disagree with; save.'
Write-Host 'Then run: Migrate-PlaybookNodes-to-ExecutorType.ps1 -DryRun first, then live.'
