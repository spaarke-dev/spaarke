<#
.SYNOPSIS
  /provision-environment Steps 6b, 6c, 6d — the handoff report runs/{runId}.md (on EVERY terminal outcome) and, when
  Step 6a marked the registry stale, runs/{runId}-registry-stale.md.

.DESCRIPTION
  -Action Write  renders templates/handoff-report.md with every {token} substituted (PLX-12 — never write literal
                 {runId}) and writes runs/{runId}.md; writes the registry-stale diagnostic when the session says so.
                 The report keeps `<!-- FILL: ... -->` markers for the parts only the skill can judge (handler table,
                 traps, invariants, Step 6e/6f results, deviations). -Status overrides the run status for a report
                 written without a terminal run (e.g. SkillDetached under Fallback F3).
  -Action Check  fails (exit 1) while any {token} or FILL marker is left in runs/{runId}.md; then exits 5 when the
                 registry is stale (AFTER both artifacts exist — Bucket B HIGH#10), else 0.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [Parameter(Mandatory)][ValidateSet('Write', 'Check')][string]$Action,
  [string]$Status
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$i = Get-ProvisionIntake -Session $s
$runId = $s.runId; $cpEnv = $s.controlPlaneEnv; $customerId = [string]$i['customerId']
if (-not $runId) { Stop-Provision '[skill] No runId in the session — there is no run to report on (a pre-dispatch stop leaves its runs/pre-dispatch-*.json diagnostic instead).' }
$reportPath = "runs/$runId.md"
$stalePath = "runs/$runId-registry-stale.md"

if ($Action -eq 'Check') {
  $text = Get-Content -LiteralPath $reportPath -Raw
  $left = @([regex]::Matches($text, '\{[a-zA-Z][a-zA-Z0-9]*\}|<!-- FILL') | ForEach-Object Value | Select-Object -Unique)
  if ($left.Count -gt 0) { Stop-Provision "[skill] $reportPath still has unfilled parts: $($left -join ', '). Fill them, then rerun -Action Check." }
  Write-Host "  [PASS] $reportPath complete." -ForegroundColor Green
  if ($s.registryStale) {
    Write-Warning "Registry STALE — MANUAL RECOVERY REQUIRED; see $stalePath. Exit 5 (after both artifacts are written)."
    exit 5
  }
  exit 0
}

$l2Base = Get-L2Base -Env $cpEnv
$run = $null
try { $run = Invoke-L2Request -Env $cpEnv -Method GET -Uri "$l2Base/api/runs/$runId`?customerId=$([Uri]::EscapeDataString($customerId))" -RunId $runId }
catch { Write-Warning "Could not read run $runId ($($_.Exception.Message)) — writing the report from the session only." }

$startedAt = if ($run -and $run.createdOn) { ([datetimeoffset]$run.createdOn).ToString('o') } else { 'unknown' }
$completedAt = if ($run -and $run.completedOn) { ([datetimeoffset]$run.completedOn).ToString('o') } else { 'not completed' }
$duration = if ($run -and $run.createdOn -and $run.completedOn) { '{0:hh\:mm\:ss}' -f (([datetimeoffset]$run.completedOn) - ([datetimeoffset]$run.createdOn)) } else { 'n/a' }
$statusText = if ($Status) { $Status } elseif ($run) { [string]$run.status } else { 'unknown' }
$tierCeiling = switch -CaseSensitive ([string]$i['tier']) { 'smb' { 700 } 'enterprise' { 2500 } 'dedicated' { 5000 } default { 'unknown' } }
$setupStatus = if ($s.registryStale) { 'STALE — see ' + $stalePath } elseif ($statusText -eq 'Completed') { 'Ready' } else { 'InProgress (run not Completed)' }
$tokens = [ordered]@{
  runId = $runId; customerId = $customerId; tenantId = $i['tenantId']; tenancyModel = $i['tenancyModel']; profile = $i['profile']
  startedAt = $startedAt; completedAt = $completedAt; duration = $duration; status = $statusText; l2Base = $l2Base
  estimatedMonthlyUsd = $i['estimatedMonthlyUsd']; tier = $i['tier']; tierCeiling = $tierCeiling
  provisionedOn = $(if ($s.registryCompletedOn) { $s.registryCompletedOn } else { 'not set' })
  bffVersion = $(if ($s.registryBffVersion) { $s.registryBffVersion } else { 'not set' })
  solutionVersion = $(if ($s.registrySolutionVersion) { $s.registrySolutionVersion } else { 'not set' })
  setupStatus = $setupStatus
}
$report = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'templates/handoff-report.md') -Raw
foreach ($k in $tokens.Keys) { $report = $report.Replace('{' + $k + '}', [string]$tokens[$k]) }
New-Item -Path 'runs' -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
Set-Content -LiteralPath $reportPath -Value $report -Encoding utf8
Write-Host "HANDOFF=$reportPath (fill every <!-- FILL --> marker, then run -Action Check)"

if ($s.registryStale) {
  # 6c — supplements, never replaces, the mandatory handoff report.
  $f = $s.registryFields
  $fieldsJson = if ($f) { ($f | ConvertTo-Json -Depth 4) } else { '{ }' }
  $registryDvUrl = try { Get-RegistryDvUrl -Env $cpEnv } catch { '<registry env URL>' }
  $staleReport = @"
# Registry Stale — Run $runId

**⚠ MANUAL RECOVERY REQUIRED**

The provisioning run reached RunStatus.Completed successfully, but the operator-side Step 6a Dataverse registry PATCH
FAILED. The customer's ``sprk_dataverseenvironment`` row ($($s.environmentId)) is missing some or all of the promoted
Ready-state columns (sprk_provisionedon, sprk_bffversion, sprk_solutionversion, sprk_azuresubscriptionid,
sprk_resourcegroupname, sprk_appservicename, sprk_keyvaultname, sprk_bffappid, sprk_containertypeid,
sprk_ClientCacheBustToken) and possibly sprk_setupstatus = Ready (2).

The customer's Azure resources are provisioned correctly and the L2 control-plane has released the I5 concurrency
guard (``sprk_currentrunid`` via ICustomerRunGuard.ReleaseAsync). Customer-facing functionality works; only the
operator dashboards and downstream automation that query these columns are affected.

## Diagnostic

$($s.registryStaleDiagnostic)

## Values to write (registry env $registryDvUrl, row $($s.environmentId))

``````json
$fieldsJson
``````

## Manual Recovery (choose ONE)

### Option A — re-run the reconciliation (Web API, operator's az token)
``````powershell
pwsh -NoProfile -File scripts/provision-environment/Sync-RegistryOnCompletion.ps1 -SessionFile $SessionFile -Action ApplyWebApi
``````

### Option B — Power Apps Portal
1. Open https://make.powerapps.com → the registry environment ($registryDvUrl)
2. Tables → sprk_dataverseenvironment → row ``$($s.environmentId)``
3. Set the columns above; set sprk_setupstatus to Ready only if it is not Ready already
4. Save

(``pac data update`` is not available — pac 1.46 ``pac data`` has only export/import.)

## Post-recovery verification

Read the row (Dataverse MCP read_query or the Web API) and expect ``sprk_provisionedon`` set and
``sprk_setupstatus`` = Ready (2). If not, retry the recovery.

## Do NOT

- **Do NOT touch** ``sprk_currentrunid`` (released by ICustomerRunGuard — single-writer invariant)
- **Do NOT touch** ``sprk_tenantid`` (I1 invariant — set at placeholder-create, NEVER re-writable)
- **Do NOT re-write** ``sprk_setupstatus`` when it already reads Ready

## Escalation

If manual recovery fails repeatedly, file a GitHub Issue with this file (``$stalePath``), the handoff report
(``$reportPath``) and the Dataverse error message from the recovery attempt.
"@
  Set-Content -LiteralPath $stalePath -Value $staleReport -Encoding utf8
  Write-Warning "Registry-stale diagnostic written to $stalePath — MANUAL RECOVERY REQUIRED. Handoff report at $reportPath."
}
exit 0
