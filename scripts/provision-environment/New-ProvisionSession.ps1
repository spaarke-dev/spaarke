<#
.SYNOPSIS
  /provision-environment — creates the session file; in batch mode also runs Step 1.0 (validate + bind the intake).

.DESCRIPTION
  Interactive: records mode, the control-plane env when the operator gave it, and the operator UPN.
  Batch (Step 1.0, task 203c / A03):
    - validates the intake against scripts/provisioning-prereqs/intake.schema.json with ajv-cli + ajv-formats via npx
      (same invocation as the `prereqs` job in ci-router.yml; ajv-formats makes `format: uuid` actually checked);
    - T245c / D15: refuses an intake file git would track (it carries personal data);
    - BAT-01/BAT-03: requires intake.confirmationAcknowledgment == 'proceed with provisioning';
    - binds controlPlaneEnv, environmentId, skipExternalPrereqs; prints the BAT-04..09 policies.
  The operator UPN always comes from `az ad signed-in-user show` — NEVER from an intake field (NFR-11 spoof risk).

  -ResumeRunId <id> attaches the session to an existing run (the `--resume <runId>` flag).
  Prints `SESSION=<path>` on success. Exit 1 on any hard stop.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('Interactive', 'Batch')][string]$Mode,
  [string]$BatchIntakeFile,
  [ValidateSet('dev', 'demo', 'prod')][string]$Env,
  [string]$RunsDir = 'runs',
  # Re-attach to a run that already exists (WAITING.md "--resume", Fallback F3 re-invocation, Step 1a
  # REGISTRY=in-progress). Records the runId so Start-ProvisioningRun refuses a second POST and
  # Watch-ProvisioningRun polls the existing run. No L2 call is made here.
  [string]$ResumeRunId
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force

New-Item -Path $RunsDir -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
$sessionFile = Join-Path $RunsDir ("provision-session-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

$operatorUpn = az ad signed-in-user show --query userPrincipalName -o tsv 2>$null
$session = [ordered]@{
  schema                  = 1
  createdAt               = (Get-Date -Format 'o')
  mode                    = $Mode.ToLowerInvariant()
  intakeFile              = $null
  controlPlaneEnv         = if ($Env) { $Env } else { $null }
  operatorUpn             = $operatorUpn
  skipExternalPrereqs     = $false
  mcpFallbackActive       = $false
  environmentId           = $null
  isUpgradeRun            = $false
  confirmationPhrase      = $null
  intakeFileSha256        = $null
  runId                   = $null
  alreadyResumed          = $false
  registryStale           = $false
  registryStaleDiagnostic = $null
  prereqResults           = @{}
}

if ($Mode -eq 'Batch') {
  if (-not $BatchIntakeFile -or -not (Test-Path -LiteralPath $BatchIntakeFile)) {
    Stop-Provision "[skill] Batch HARD STOP: -BatchIntakeFile '$BatchIntakeFile' not found."
  }
  $repoRoot = Get-ProvisionRepoRoot
  $schemaPath = Join-Path $repoRoot 'scripts/provisioning-prereqs/intake.schema.json'

  # Fallback if npx is unavailable: any Draft 2020-12 validator that checks formats (e.g., check-jsonschema).
  $validationOutput = & npx --yes -p ajv-cli@5 -p ajv-formats@3 ajv validate `
    --spec=draft2020 -c ajv-formats `
    -s $schemaPath -d $BatchIntakeFile 2>&1
  if ($LASTEXITCODE -ne 0) {
    Stop-Provision "Batch intake failed JSON Schema validation ($schemaPath):`n$validationOutput"
  }

  # T245c / owner decision D15: the intake carries personal data (the H11 user list). It may live in the L2 run
  # document, never in git — so refuse a file git would track. Inside a repo it must be ignored (`runs/*-intake.json`
  # is); `git check-ignore` exits 1 for a tracked file and for an untracked-but-not-ignored one.
  $intakeFullPath = (Resolve-Path $BatchIntakeFile).Path
  $intakeDir = Split-Path -Parent $intakeFullPath
  if ((git -C $intakeDir rev-parse --is-inside-work-tree 2>$null) -eq 'true') {
    git -C $intakeDir check-ignore -q -- $intakeFullPath
    if ($LASTEXITCODE -ne 0) {
      Stop-Provision "[skill] Batch intake HARD STOP: '$BatchIntakeFile' is inside a git repository and not ignored, and it carries personal data (users). Keep it outside the repo, or name it runs/{customerId}-intake.json (ignored). If it is already tracked: git rm --cached it."
    }
  }

  $intake = Get-Content $BatchIntakeFile -Raw | ConvertFrom-Json -AsHashtable -Depth 10

  # BAT-01/BAT-03: interactive mode requires the literal phrase typed at Step 3; batch mode requires the same phrase
  # in intake.confirmationAcknowledgment (const in schema). Step 4.0 sends it plus the intake SHA-256 (NFR-11).
  if ($intake['confirmationAcknowledgment'] -ne $ConfirmationLiteral) {
    Stop-Provision "[skill] Batch intake HARD STOP: intake.confirmationAcknowledgment MUST equal the literal 'proceed with provisioning' (batch equivalent of Step 3 interactive gate per wave-0-adr-note Decision 3 / BAT-03). Got: '$($intake['confirmationAcknowledgment'])'."
  }
  Write-Host "  [PASS] Confirmation attestation: '$($intake['confirmationAcknowledgment'])' (SHA-256 of intake file captured at Step 4 for audit trail)" -ForegroundColor Green

  $session.intakeFile          = $intakeFullPath
  $session.controlPlaneEnv     = $intake['controlPlaneEnv']    # ISH-12: intake field is controlPlaneEnv
  $session.environmentId       = $intake['environmentId']      # may be null → Step 1f creates the placeholder
  $session.skipExternalPrereqs = [bool]$intake['skipExternalPrereqs']

  # T229: there is no cost-overrun waiver (costEnvelopePolicy=warnAndProceed was the shared-trial tier's; every stamp
  # is dedicated since D-12). An over-budget intake stops at Step 2 and, failing that, at H0.
  $p = Get-BatchPolicies -Intake $intake
  Write-Host ("  Batch policies: mcpDisconnectPolicy={0} acknowledgeUpgradeMode={1} onFailedPolicy={2} onQuarantinedPolicy={3} onManualGatePolicy={4} postmortemFile={5}" -f `
      $p.mcpDisconnectPolicy, $p.acknowledgeUpgradeMode, $p.onFailedPolicy, $p.onQuarantinedPolicy, $p.onManualGatePolicy, $p.postmortemFile)
  Write-Host "Batch intake loaded from $BatchIntakeFile (schema-validated + batch policies bound)."
}

if ($ResumeRunId) {
  if ($ResumeRunId -notmatch '^[A-Za-z0-9-]{8,64}$') { Stop-Provision "[skill] -ResumeRunId '$ResumeRunId' is not a run id." }
  $session.runId = $ResumeRunId
  Write-Host "  Attached to existing run $ResumeRunId — skip Step 4.0 (it refuses a second POST); continue at Step 4a (Watch-ProvisioningRun.ps1)."
}

($session | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $sessionFile -Encoding utf8
Write-Host "SESSION=$sessionFile"
exit 0
