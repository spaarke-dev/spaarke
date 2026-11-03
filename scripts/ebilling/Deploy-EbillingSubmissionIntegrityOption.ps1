<#
.SYNOPSIS
    eBilling R1 task 049 (D-125) - adds the system rule type SubmissionIntegrity (100000014) to the LOCAL choice sprk_policyversion.sprk_ruletype.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only until the owner says otherwise). Check-then-act: a second run inserts nothing and does not
    publish. Run -WhatIf first: it reads the column, prints what it would insert and writes nothing. Never run by the agent that wrote it.

    sprk_policyversion.sprk_ruletype is a LOCAL choice (task 070 verified it), so the value is added with InsertOptionValue on the column; the
    script refuses (exit 2, nothing written) if the column is bound to a global choice.

    The value mirrors Sprk.Bff.Api.Services.Signals.RuleType.SubmissionIntegrity exactly. 100000014 was the next free value after
    AiObservation (100000013) when checked; the script re-checks it is free on the live column.

    REFUSES (exit 2, nothing written) when:
      - the column is a global choice;
      - 100000014 is present with another label, or the label SubmissionIntegrity is present under another value;
      - an Ontology or billing option the BFF knows (100000000 to 100000013) is missing or relabelled (the closed set is out of sync).
    Options outside 100000000 to 100000014 are reported, not refused (another task may have added one: owner decision).

    Solution source (SpaarkeMaster Entities/sprk_PolicyVersion/Entity.xml) carries the same option; RuleTypeOptionSetSyncTests guards that.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the option is added in (InsertOptionValue SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSubmissionIntegrityOption.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSubmissionIntegrityOption.ps1

.NOTES
    Project: ebilling-r1, task 049, D-125. Dataverse helpers: EbillingSchema.psm1. Exit codes: 0 = done, 1 = error, 2 = escalation.
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
$NewValue = 100000014
$NewLabel = 'SubmissionIntegrity'

# The options the BFF already knows (RuleType.cs, 100000000 to 100000013): verified, never changed.
# (A generic SortedDictionary[int,string], not [ordered]@{}: an OrderedDictionary's integer indexer is POSITIONAL.)
$Known = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$i = 100000000
foreach ($name in 'Threshold', 'Switch', 'Existence', 'ApprovedTimekeeper', 'ApprovedRate', 'TimeIncrement', 'DailyHoursCap', 'NonBillableActivity',
    'ExpenseRule', 'InvoiceAge', 'CoreRecordWindow', 'Duplicate', 'RequiredData', 'AiObservation') {
    $Known.Add($i, $name); $i++
}

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
    Write-Host "Task 049: $Table.$Column option $NewValue $NewLabel on $EnvironmentUrl (solution $SolutionUniqueName)$(if ($DryRun) { ' [WhatIf]' })" -ForegroundColor Cyan
    $have = Get-RuleTypeOptions
    Write-Host "  before: $(Format-Options $have)"

    foreach ($v in $Known.Keys) {
        $cur = $have | Where-Object { $_.Value -eq $v }
        if (-not $cur -or $cur.Label -ne $Known[$v]) {
            throw [EscalationException]::new("Option $v is '$($cur.Label)', expected '$($Known[$v])'. Not touched; owner decision needed.")
        }
    }

    $byLabel = $have | Where-Object { $_.Label -eq $NewLabel -and $_.Value -ne $NewValue }
    if ($byLabel) { throw [EscalationException]::new("'$NewLabel' already exists as value $($byLabel.Value), expected $NewValue. Owner decision needed.") }

    $cur = $have | Where-Object { $_.Value -eq $NewValue }
    if ($cur -and $cur.Label -ne $NewLabel) { throw [EscalationException]::new("Value $NewValue is labelled '$($cur.Label)', expected '$NewLabel'. Owner decision needed.") }

    $other = @($have | Where-Object { $_.Value -notin @($Known.Keys) + $NewValue })
    if ($other.Count -gt 0) { Write-Host "  note: options outside the set the BFF knows: $(Format-Options $other)" -ForegroundColor Yellow }

    if ($cur) {
        $script:Skipped.Add("$Column=$NewValue"); Write-Host "  skipped  $NewValue $NewLabel (already present)" -ForegroundColor Yellow
    }
    elseif ($DryRun) {
        $script:Created.Add("$Column=$NewValue"); Write-Host "  WOULD INSERT $NewValue $NewLabel" -ForegroundColor Cyan
    }
    else {
        try {
            Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
                EntityLogicalName = $Table; AttributeLogicalName = $Column; Value = $NewValue; Label = (New-Label $NewLabel)
                SolutionUniqueName = $SolutionUniqueName
            } | Out-Null
        } catch {
            # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
            if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
            $found = $null
            for ($n = 0; $n -lt 12 -and -not $found; $n++) {
                $found = Get-RuleTypeOptions | Where-Object { $_.Value -eq $NewValue -and $_.Label -eq $NewLabel }
                if (-not $found) { Start-Sleep -Seconds 5 }
            }
            if (-not $found) { throw }
        }
        $script:Created.Add("$Column=$NewValue"); Write-Host "  INSERTED $NewValue $NewLabel" -ForegroundColor Green

        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
        Write-Host "  published $Table" -ForegroundColor Green

        # Verify by metadata read: the new option is there with its label, and every known option is unchanged.
        $after = Get-RuleTypeOptions
        Write-Host "  after:  $(Format-Options $after)"
        $bad = @()
        foreach ($k in $Known.Keys) {
            $o = $after | Where-Object { $_.Value -eq $k }
            if (-not $o -or $o.Label -ne $Known[$k]) { $bad += "$k expected '$($Known[$k])', got '$($o.Label)'" }
        }
        $o = $after | Where-Object { $_.Value -eq $NewValue }
        if (-not $o -or $o.Label -ne $NewLabel) { $bad += "$NewValue expected '$NewLabel', got '$($o.Label)'" }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  verified: $NewValue $NewLabel present, the 14 known options unchanged" -ForegroundColor Green
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
