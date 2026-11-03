<#
.SYNOPSIS
  /provision-environment — records an interactive-mode decision in the session file.

.DESCRIPTION
  Only the values the operator decides in interactive mode, never a gate result:
    -IntakeFile            Step 1: runs/{customerId}-intake.json, written from the operator's answers
    -Env                   Step 1d: control-plane env (dev | demo | prod)
    -SkipExternalPrereqs   -SkipStep0_5 (the operator attests every applicable prerequisite was checked by hand)
  The confirmation phrase is recorded only by Confirm-ProvisionGate.ps1; environmentId only by
  Invoke-RegistryPlaceholder.ps1; mcpFallbackActive only by Set-McpStatus.ps1.
  Refuses to change values in a batch session (they come from the validated intake).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [string]$IntakeFile,
  [ValidateSet('dev', 'demo', 'prod')][string]$Env,
  [switch]$SkipExternalPrereqs
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
if ($s.mode -eq 'batch') { Stop-Provision '[skill] Set-ProvisionSession is for interactive sessions; batch values come from the validated intake file.' }

$v = @{}
if ($IntakeFile) {
  if (-not (Test-Path -LiteralPath $IntakeFile)) { Stop-Provision "[skill] Intake file '$IntakeFile' not found." }
  $full = (Resolve-Path -LiteralPath $IntakeFile).Path
  # D15: the intake carries personal data — it must be ignored by git when inside a repository.
  $dir = Split-Path -Parent $full
  if ((git -C $dir rev-parse --is-inside-work-tree 2>$null) -eq 'true') {
    git -C $dir check-ignore -q -- $full
    if ($LASTEXITCODE -ne 0) { Stop-Provision "[skill] HARD STOP: '$IntakeFile' would be tracked by git and carries personal data (users). Name it runs/{customerId}-intake.json (ignored)." }
  }
  $v.intakeFile = $full
}
if ($Env) { $v.controlPlaneEnv = $Env }
if ($SkipExternalPrereqs) { $v.skipExternalPrereqs = $true }
if ($v.Count -eq 0) { Stop-Provision '[skill] Nothing to record — pass -IntakeFile, -Env or -SkipExternalPrereqs.' }
Update-ProvisionSession -SessionFile $SessionFile -Values $v | Out-Null
Write-Host "Session updated: $($v.Keys -join ', ')"
exit 0
