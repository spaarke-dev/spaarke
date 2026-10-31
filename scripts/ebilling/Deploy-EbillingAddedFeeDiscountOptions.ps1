<#
.SYNOPSIS
    eBilling R1 task 115 (EB-D-148) - adds the billing rule types AddedFee (100000015) and Discount (100000016) to the LOCAL choice
    sprk_policyversion.sprk_ruletype.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only until the owner says otherwise). Check-then-act: a second run inserts nothing and does not
    publish. Run -WhatIf first: it reads the column, prints what it would insert and writes nothing. Never run by the agent that wrote it;
    the dev run waits for ontology-r1's schema review of this script and its -WhatIf output, and the owner's OK (TASK-INDEX owner gates).

    sprk_policyversion.sprk_ruletype is a LOCAL choice (task 070 verified it), so each value is added with InsertOptionValue on the column;
    the script refuses (exit 2, nothing written) if the column is bound to a global choice.

    The values mirror Sprk.Bff.Api.Services.Signals.RuleType.AddedFee and .Discount exactly; each label is the member name, as for every
    other option (RuleTypeOptionSetSyncTests compares labels with names). 100000015 and 100000016 were free on dev on 2026-10-21 (read-only
    metadata check); the script re-checks them on the live column.

    REFUSES (exit 2, nothing written) when:
      - the column is a global choice;
      - 100000015 or 100000016 is present with another label, or the label AddedFee or Discount is present under another value;
      - an Ontology or billing option the BFF knows (100000000 to 100000013) is missing or relabelled (the closed set is out of sync);
      - 100000014 is present with a label other than SubmissionIntegrity.
    100000014 being absent (task 049's Deploy-EbillingSubmissionIntegrityOption.ps1 not yet run on the environment) is reported, not refused.
    Options outside 100000000 to 100000016 are reported, not refused (another task may have added one: owner decision).

    Solution source (SpaarkeMaster Entities/sprk_PolicyVersion/Entity.xml) carries the same options; RuleTypeOptionSetSyncTests guards that.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the options are added in (InsertOptionValue SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingAddedFeeDiscountOptions.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingAddedFeeDiscountOptions.ps1

.NOTES
    Project: ebilling-r1, task 115, EB-D-148. Dataverse helpers: EbillingSchema.psm1. Exit codes: 0 = done, 1 = error, 2 = escalation.
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

$Table = 'sprk_policyversion'
$Column = 'sprk_ruletype'

# The options this script adds, in value order.
$New = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$New.Add(100000015, 'AddedFee')
$New.Add(100000016, 'Discount')

# The options the BFF already knows (RuleType.cs, 100000000 to 100000013): verified, never changed.
# (A generic SortedDictionary[int,string], not [ordered]@{}: an OrderedDictionary's integer indexer is POSITIONAL.)
$Known = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$i = 100000000
foreach ($name in 'Threshold', 'Switch', 'Existence', 'ApprovedTimekeeper', 'ApprovedRate', 'TimeIncrement', 'DailyHoursCap', 'NonBillableActivity',
    'ExpenseRule', 'InvoiceAge', 'CoreRecordWindow', 'Duplicate', 'RequiredData', 'AiObservation') {
    $Known.Add($i, $name); $i++
}

# Task 049's system type: checked when present, reported when absent.
$SystemValue = 100000014
$SystemLabel = 'SubmissionIntegrity'

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

function Get-RuleTypeOptions {
    $pk = Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal,Name)"
    if ($pk.OptionSet.IsGlobal) { throw [EscalationException]::new("$Table.$Column is bound to a GLOBAL choice ($($pk.OptionSet.Name)); expected a local choice. Owner decision needed.") }
    return @($pk.OptionSet.Options | Sort-Object Value | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; Label = [string]$_.Label.UserLocalizedLabel.Label }
        })
}

function Format-Options($options) { return (($options | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ') }

try {
    Write-Host "Task 115: $Table.$Column options $(($New.Keys | ForEach-Object { "$_ $($New[$_])" }) -join ', ') on $EnvironmentUrl (solution $SolutionUniqueName)$(if ($DryRun) { ' [WhatIf]' })" -ForegroundColor Cyan
    $have = Get-RuleTypeOptions
    Write-Host "  before: $(Format-Options $have)"

    # Every check runs before any write, so a refusal leaves the column untouched.
    foreach ($v in $Known.Keys) {
        $cur = $have | Where-Object { $_.Value -eq $v }
        if (-not $cur -or $cur.Label -ne $Known[$v]) {
            throw [EscalationException]::new("Option $v is '$($cur.Label)', expected '$($Known[$v])'. Not touched; owner decision needed.")
        }
    }

    $system = $have | Where-Object { $_.Value -eq $SystemValue }
    if ($system -and $system.Label -ne $SystemLabel) {
        throw [EscalationException]::new("Value $SystemValue is labelled '$($system.Label)', expected '$SystemLabel'. Owner decision needed.")
    }
    if (-not $system) { Write-Host "  note: $SystemValue $SystemLabel is not present yet (task 049's option script has not run here); not required." -ForegroundColor Yellow }

    $toInsert = @()
    foreach ($v in $New.Keys) {
        $label = $New[$v]
        $byLabel = $have | Where-Object { $_.Label -eq $label -and $_.Value -ne $v }
        if ($byLabel) { throw [EscalationException]::new("'$label' already exists as value $($byLabel.Value), expected $v. Owner decision needed; do not renumber.") }

        $cur = $have | Where-Object { $_.Value -eq $v }
        if ($cur -and $cur.Label -ne $label) { throw [EscalationException]::new("Value $v is labelled '$($cur.Label)', expected '$label'. Owner decision needed; do not renumber.") }
        if (-not $cur) { $toInsert += $v }
    }

    $other = @($have | Where-Object { $_.Value -notin (@($Known.Keys) + $SystemValue + @($New.Keys)) })
    if ($other.Count -gt 0) { Write-Host "  note: options outside the set the BFF knows: $(Format-Options $other)" -ForegroundColor Yellow }

    foreach ($v in $New.Keys) {
        if ($v -notin $toInsert) { $script:Skipped.Add("$Column=$v"); Write-Host "  skipped  $v $($New[$v]) (already present)" -ForegroundColor Yellow }
    }

    if ($toInsert.Count -eq 0) {
        # Nothing to write: no insert, no publish.
    }
    elseif ($DryRun) {
        foreach ($v in $toInsert) { $script:Created.Add("$Column=$v"); Write-Host "  WOULD INSERT $v $($New[$v])" -ForegroundColor Cyan }
        Write-Host "  WOULD PUBLISH $Table" -ForegroundColor Cyan
    }
    else {
        foreach ($v in $toInsert) {
            $label = $New[$v]
            try {
                Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
                    EntityLogicalName = $Table; AttributeLogicalName = $Column; Value = $v; Label = (New-Label $label)
                    SolutionUniqueName = $SolutionUniqueName
                } | Out-Null
            } catch {
                # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
                if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
                $found = $null
                for ($n = 0; $n -lt 12 -and -not $found; $n++) {
                    $found = Get-RuleTypeOptions | Where-Object { $_.Value -eq $v -and $_.Label -eq $label }
                    if (-not $found) { Start-Sleep -Seconds 5 }
                }
                if (-not $found) { throw }
            }
            $script:Created.Add("$Column=$v"); Write-Host "  INSERTED $v $label" -ForegroundColor Green
        }

        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
        Write-Host "  published $Table" -ForegroundColor Green
    }

    if (-not $DryRun) {
        # Verify by metadata read: both new options are there with their labels, and every known option is unchanged.
        $after = Get-RuleTypeOptions
        Write-Host "  after:  $(Format-Options $after)"
        $bad = @()
        foreach ($k in $Known.Keys) {
            $o = $after | Where-Object { $_.Value -eq $k }
            if (-not $o -or $o.Label -ne $Known[$k]) { $bad += "$k expected '$($Known[$k])', got '$($o.Label)'" }
        }
        foreach ($k in $New.Keys) {
            $o = $after | Where-Object { $_.Value -eq $k }
            if (-not $o -or $o.Label -ne $New[$k]) { $bad += "$k expected '$($New[$k])', got '$($o.Label)'" }
        }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  verified: 100000015 AddedFee and 100000016 Discount present, the 14 known options unchanged" -ForegroundColor Green
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
    exit 1
}

Write-Host "Done: created $($script:Created.Count), skipped $($script:Skipped.Count)." -ForegroundColor Cyan
exit 0
