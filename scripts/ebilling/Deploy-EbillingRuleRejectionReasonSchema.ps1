<#
.SYNOPSIS
    eBilling R1 task 077 follow-up (D-113, FR-28) - the reason an admin gives when rejecting a drafted rule:
    sprk_policyversion.sprk_rejectionreason.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. The one item is check-then-create; a second run reports it as
    skipped and makes no write (no publish either).

    Item (projects/ebilling-r1/notes/077-rule-review-page.md, "D-113 reject reason"):
      1  sprk_policyversion.sprk_rejectionreason: multiline text, max 2,000, not required. The decision state of a drafted
         rule (Proposed / Accepted / Rejected, sprk_proposalstatus) is a column of the policy VERSION, so the reason sits
         beside it. Written only by the BFF (RuleSetService.DecideAsync, reject; cleared by accept and by an edit that returns
         the draft to Proposed). Admin-only: no firm projection reads it (GetFirmClauseAsync selects its columns by name).

    The required rule (a reject needs a reason, 1 to 2,000 characters after trimming) is enforced by the BFF, not by schema:
    the column's required level stays None because Accepted and Proposed versions have no reason.

    NEVER deletes or renames anything. A same-name column of another type STOPS the script with exit code 2 and an
    ESCALATION message. Publish is entity-scoped (PublishXml for sprk_policyversion), and only when this run wrote metadata.

    sprk_policyversion belongs to spaarke-ontology-platform-r1; this script only ADDS a column to it, as task 019 did.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1

.NOTES
    Project: ebilling-r1, task 077 (D-113). Dev only in R1; task 079 runs it. Task 053 syncs SpaarkeMaster (export the
    solution after the run; this script does not edit the solution source). Run it BEFORE the BFF build that reads the column
    is deployed: RuleSetService selects sprk_rejectionreason on every version read, so a BFF without the column faults on
    the rule set read. Pattern: scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1 (the sprk_firmreason memo).
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference

$VersionTable = 'sprk_policyversion'
$ReasonColumn = 'sprk_rejectionreason'

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $VersionTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

Write-Host ""
Write-Host "eBilling task 077 (D-113) - rule rejection reason" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$VersionTable')?`$select=LogicalName" -Retries 4)) {
        throw "$VersionTable does not exist in $EnvironmentUrl."
    }

    Write-Host "1. $VersionTable.$ReasonColumn" -ForegroundColor Cyan
    Set-EbEntity $VersionTable
    Add-Column $ReasonColumn (New-MemoDef 'sprk_RejectionReason' 'Rejection Reason' 'Why an admin rejected this drafted billing rule (1 to 2,000 characters). Written server-side only; admin-only, never shown to firms (D-113).' 2000) 'Memo'
    $metadataWritten = ($script:Created.Count -gt 0)

    Write-Host "Publish" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $metadataWritten) {
        Write-Host "  no metadata changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$VersionTable</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  published $VersionTable" -ForegroundColor Green
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Items already created stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
