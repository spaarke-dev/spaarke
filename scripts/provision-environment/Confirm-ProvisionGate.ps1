<#
.SYNOPSIS
  /provision-environment Step 3 — the confirmation gate. Records the operator's literal phrase in the session.

.DESCRIPTION
  Interactive: -Phrase is EXACTLY what the operator typed, passed verbatim. Only the literal
  `proceed with provisioning` is accepted — never "y", "yes", "go", "ok" (spec §4.3a.4, NFR-11). Anything else exits 7:
  the skill re-asks the same gate.
  Batch (BAT-03): the gate was satisfied at Step 1.0 by intake.confirmationAcknowledgment; this re-checks it and
  records it. -Phrase is not accepted in batch mode.
  Start-ProvisioningRun.ps1 refuses to POST unless this script recorded the phrase.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [string]$Phrase
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile

if ($s.mode -eq 'batch') {
  if ($PSBoundParameters.ContainsKey('Phrase')) { Stop-Provision '[skill] Batch mode: the confirmation comes from intake.confirmationAcknowledgment, not -Phrase.' }
  $i = Get-ProvisionIntake -Session $s
  if ($i['confirmationAcknowledgment'] -ne $ConfirmationLiteral) {
    Stop-Provision "[skill] Batch intake HARD STOP: intake.confirmationAcknowledgment MUST equal the literal 'proceed with provisioning'. Got: '$($i['confirmationAcknowledgment'])'."
  }
  $sha = (Get-FileHash -Path $s.intakeFile -Algorithm SHA256).Hash
  Update-ProvisionSession -SessionFile $SessionFile -Values @{ confirmationPhrase = $ConfirmationLiteral; intakeFileSha256 = $sha } | Out-Null
  Write-Host '  [BATCH] Confirmation gate satisfied by intake.confirmationAcknowledgment (validated at Step 1.0).' -ForegroundColor Cyan
  exit 0
}

# Same comparison as the original Read-Host loop (`-ne`, PowerShell's case-insensitive compare).
if ($Phrase -ne $ConfirmationLiteral) {
  Write-Host '(literal phrase required; try again OR press Ctrl+C to abort)'
  Write-Host 'FIELD=confirmationPhrase'
  exit 7
}
Update-ProvisionSession -SessionFile $SessionFile -Values @{ confirmationPhrase = $Phrase } | Out-Null
Write-Host '  [PASS] Confirmation gate: operator typed the literal phrase.' -ForegroundColor Green
exit 0
