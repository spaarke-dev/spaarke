<#
.SYNOPSIS
  /provision-environment Step 7 (7a, 7b batch path, 7c, 7d) — the MANDATORY per-run lessons-learned.md.

.DESCRIPTION
  Runs on EVERY outcome: Completed, Failed, Cancelled, Quarantined, or a manual abort.
  -Action Prepare   7a: copies provisioning-runs/_templates/lessons-learned.md to provisioning-runs/{customerId}-{runId}/
                    and fills {customerId} {runId} {operatorUpn} {ts}.
                    7b batch: intake.postmortemFile → must exist and carry the four required ## sections
                    (What went right / What went wrong / Recommendations for next run / Sign-off), else
                    runs/{runId}-postmortem-invalid.json + exit 6; copied verbatim + auto metadata appended. No
                    postmortemFile → the auto-generated minimum shape. Interactive: the skill then walks the operator
                    through each template section and edits the file.
  -Action Finalize  7c: appends | {customerId}-{runId} | {date} | {outcome} | {lessonCount} | to provisioning-runs/INDEX.md
                    and prints the 7d summary. Then the operator commits the run folder.
  Both paths record the Step 0.5 / 1e-ter prerequisite results (or that they were skipped) — they MUST appear in the
  postmortem.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [Parameter(Mandatory)][ValidateSet('Prepare', 'Finalize')][string]$Action,
  [Parameter(Mandatory)][ValidateSet('Completed', 'Failed', 'Cancelled', 'Quarantined', 'ManualAbort')][string]$Outcome
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$i = Get-ProvisionIntake -Session $s
$customerId = [string]$i['customerId']
$runId = if ($s.runId) { $s.runId } else { 'pre-dispatch-' + (Get-Date $s.createdAt -Format 'yyyyMMdd-HHmmss') }
$repoRoot = Get-ProvisionRepoRoot
$runDir = "provisioning-runs/$customerId-$runId"
$lessonsPath = Join-Path $runDir 'lessons-learned.md'
$runOutcome = $Outcome

$pr = $s.prereqResults
$prereqSummary = if (-not $pr) { 'not run' } else {
  (@('env', 'customer') | ForEach-Object {
      $v = $pr[$_]
      if ($v -eq 'skipped') { "$_ pass: SKIPPED (skipExternalPrereqs / -SkipStep0_5)" }
      elseif ($v) { $failed = @($v | Where-Object { -not $_.passed } | ForEach-Object { $_.id }); "$_ pass: $(@($v).Count) checked, $(@($v).Count - $failed.Count) passed$(if ($failed) { ', FAILED: ' + ($failed -join ', ') })" }
      else { "$_ pass: not run" } }) -join '; '
}

if ($Action -eq 'Prepare') {
  New-Item -Path $runDir -ItemType Directory -Force | Out-Null
  Copy-Item -Path (Join-Path $repoRoot 'provisioning-runs/_templates/lessons-learned.md') -Destination $lessonsPath -Force
  $content = Get-Content $lessonsPath -Raw
  $content = $content -replace '\{customerId\}', $customerId
  $content = $content -replace '\{runId\}', $runId
  $content = $content -replace '\{operatorUpn\}', [string]$s.operatorUpn
  $content = $content -replace '\{ts\}', (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')
  Set-Content -Path $lessonsPath -Value $content -Encoding utf8

  if ($s.mode -eq 'batch') {
    $pmFile = (Get-BatchPolicies -Intake $i).postmortemFile
    $gitSha = git rev-parse HEAD
    if ($pmFile) {
      $postmortemAbs = if ([System.IO.Path]::IsPathRooted($pmFile)) { $pmFile } else { Join-Path $repoRoot $pmFile }
      if (-not (Test-Path $postmortemAbs)) {
        $diag = Write-ProvisionDiagnostic -Path "runs/$runId-postmortem-invalid.json" -Data @{ runId = $runId; reason = "postmortemFile '$postmortemAbs' not found on disk"; remedy = 'Author the file at the path in intake.postmortemFile OR omit the field to auto-generate a minimum lessons-learned.md' }
        Stop-Provision "[skill] Batch HARD STOP (BAT-09): intake.postmortemFile references '$postmortemAbs' which does not exist. Diagnostic: $diag" 6
      }
      $required = @('What went right', 'What went wrong', 'Recommendations for next run', 'Sign-off')
      $pm = Get-Content -Raw -Path $postmortemAbs
      $missing = @($required | Where-Object { $pm -notmatch "(?im)^##\s+$([regex]::Escape($_))" })
      if ($missing.Count -gt 0) {
        $diag = Write-ProvisionDiagnostic -Path "runs/$runId-postmortem-invalid.json" -Data @{ runId = $runId; postmortemFile = $postmortemAbs; missingSections = $missing; remedy = 'Add the missing ## headings, or omit intake.postmortemFile to auto-generate the minimum shape' }
        Stop-Provision "[skill] Batch HARD STOP (BAT-09): intake.postmortemFile is missing required sections: $($missing -join ', '). Diagnostic: $diag" 6
      }
      Copy-Item -Path $postmortemAbs -Destination $lessonsPath -Force
      Add-Content -Path $lessonsPath -Value @"

---

## Auto-populated metadata (BAT-09)

- **runId**: $runId
- **customerId**: $customerId
- **outcome**: $runOutcome
- **prerequisites (Step 0.5 / 1e-ter)**: $prereqSummary
- **git-sha**: $gitSha
- **written-at**: $(Get-Date -Format 'o')
- **source-postmortem**: $pmFile (validated + copied verbatim by skill Step 7b)
"@
      Write-Host "  [BATCH] Postmortem copied from $pmFile + metadata appended." -ForegroundColor Cyan
    } else {
      Set-Content -Path $lessonsPath -Encoding utf8 -Value @"
# Lessons Learned — $customerId / $runId

## What went right
- Run reached terminal state '$runOutcome' without manual gate escalation beyond design tolerance.
- Prerequisites (Step 0.5 / 1e-ter): $prereqSummary

## What went wrong
- No operator-authored lessons for this batch run. If lessons DO exist for this run, an operator SHOULD amend this file post-hoc via a follow-up commit citing the runId.

## Recommendations for next run
- (none — auto-generated postmortem; consider providing intake.postmortemFile on future batch runs for higher-fidelity lessons capture)

## Sign-off
- Author: batch-mode auto-generation (BAT-09 auto-minimum path)
- Reviewer: pending (operator should review + amend if lessons emerge)
- git-sha: $gitSha
- written-at: $(Get-Date -Format 'o')
"@
      Write-Host '  [BATCH] Auto-generated minimum lessons-learned.md (no intake.postmortemFile supplied).' -ForegroundColor Cyan
    }
  } else {
    Write-Host "Prerequisite results to summarise under What went right / What went wrong: $prereqSummary"
  }
  Write-Host "LESSONS=$lessonsPath"
  exit 0
}

# Finalize — 7c + 7d
if (-not (Test-Path $lessonsPath)) { Stop-Provision "[skill] $lessonsPath does not exist — run -Action Prepare first (Step 7 is MANDATORY)." }
$lessonCount = (Select-String -Path $lessonsPath -Pattern '^### Lesson ').Count
$indexRow = "| $customerId-$runId | $(Get-Date -Format 'yyyy-MM-dd') | $runOutcome | $lessonCount |"
Add-Content -Path (Join-Path $repoRoot 'provisioning-runs/INDEX.md') -Value $indexRow
Write-Host @"
POSTMORTEM CAPTURED

  Lessons written: $lessonsPath
  Lesson count:    $lessonCount
  INDEX.md row:    $indexRow

  Next step: commit the entire $customerId-$runId/ folder (Step 6b handoff report + this postmortem + all artifacts).
"@
exit 0
