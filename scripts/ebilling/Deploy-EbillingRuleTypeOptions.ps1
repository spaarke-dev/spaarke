<#
.SYNOPSIS
    eBilling R1 task 070 - adds the eleven billing rule types (spec FR-24) to the LOCAL choice sprk_policyversion.sprk_ruletype.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only in R1). Check-then-act: a second run inserts nothing and does not publish.
    Run -WhatIf first: it reads the column, prints what it would insert and writes nothing.

    The values mirror Sprk.Bff.Api.Services.Signals.RuleType exactly (FR-24 order, 100000003 to 100000013, so catalogue
    position N = 100000002 + N). The three Ontology options (Threshold 100000000, Switch 100000001, Existence 100000002)
    are verified and never changed.

    REFUSES (exit 2, nothing written) when:
      - an Ontology option is missing or relabelled;
      - a billing value is present with a different label, or a billing label is present under a different value;
      - the column holds any option outside the 14 expected (another task added a value: owner decision).

    Solution source (SpaarkeMaster Entities/sprk_PolicyVersion/Entity.xml) is NOT edited here; see the task 070 escalation.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the options are added in (InsertOptionValue SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingRuleTypeOptions.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingRuleTypeOptions.ps1

.NOTES
    Project: ebilling-r1, task 070. Dataverse helpers: EbillingSchema.psm1. Exit codes: 0 = done, 1 = error, 2 = escalation.
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

# Ontology options: verified, never changed (RuleType.cs "do not renumber").
# (A generic Dictionary[int,string], not [ordered]@{}: an OrderedDictionary's integer indexer is POSITIONAL.)
$Ontology = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$Ontology.Add(100000000, 'Threshold'); $Ontology.Add(100000001, 'Switch'); $Ontology.Add(100000002, 'Existence')
# Billing options in FR-24 order (RuleType.cs, task 070).
$Billing = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$i = 100000003
foreach ($name in 'ApprovedTimekeeper', 'ApprovedRate', 'TimeIncrement', 'DailyHoursCap', 'NonBillableActivity', 'ExpenseRule',
    'InvoiceAge', 'CoreRecordWindow', 'Duplicate', 'RequiredData', 'AiObservation') {
    $Billing.Add($i, $name); $i++
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
    Write-Host "Task 070: $Table.$Column billing options on $EnvironmentUrl (solution $SolutionUniqueName)$(if ($DryRun) { ' [WhatIf]' })" -ForegroundColor Cyan
    $have = Get-RuleTypeOptions
    Write-Host "  before: $(Format-Options $have)"

    foreach ($v in $Ontology.Keys) {
        $cur = $have | Where-Object { $_.Value -eq $v }
        if (-not $cur -or $cur.Label -ne $Ontology[$v]) {
            throw [EscalationException]::new("Ontology option $v is '$($cur.Label)', expected '$($Ontology[$v])'. Not touched; owner decision needed.")
        }
    }

    # Task 049 (D-125) added SubmissionIntegrity = 100000014 to the column (Deploy-EbillingSubmissionIntegrityOption.ps1): tolerated when
    # present, never inserted here, so this script stays re-runnable after it.
    $expected = @($Ontology.Keys) + @($Billing.Keys) + 100000014
    $foreign = @($have | Where-Object { $_.Value -notin $expected })
    if ($foreign.Count -gt 0) {
        throw [EscalationException]::new("$Table.$Column holds options outside the 14 expected: $(Format-Options $foreign). Owner decision needed.")
    }

    foreach ($v in $Billing.Keys) {
        $label = $Billing[$v]
        $tag = "$Column=$v"
        $cur = $have | Where-Object { $_.Value -eq $v }
        $byLabel = $have | Where-Object { $_.Label -eq $label -and $_.Value -ne $v }
        if ($byLabel) { throw [EscalationException]::new("'$label' already exists as value $($byLabel.Value), expected $v. Owner decision needed.") }
        if ($cur) {
            if ($cur.Label -ne $label) { throw [EscalationException]::new("Value $v is labelled '$($cur.Label)', expected '$label'. Owner decision needed.") }
            $script:Skipped.Add($tag); Write-Host "  skipped  $v $label" -ForegroundColor Yellow
            continue
        }
        if ($DryRun) { $script:Created.Add($tag); Write-Host "  WOULD INSERT $v $label" -ForegroundColor Cyan; continue }
        try {
            Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
                EntityLogicalName = $Table; AttributeLogicalName = $Column; Value = $v; Label = (New-Label $label)
                SolutionUniqueName = $SolutionUniqueName
            } | Out-Null
        } catch {
            # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
            if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
            $found = $null
            for ($i = 0; $i -lt 12 -and -not $found; $i++) {
                $found = Get-RuleTypeOptions | Where-Object { $_.Value -eq $v -and $_.Label -eq $label }
                if (-not $found) { Start-Sleep -Seconds 5 }
            }
            if (-not $found) { throw }
        }
        $script:Created.Add($tag); Write-Host "  INSERTED $v $label" -ForegroundColor Green
    }

    if (-not $DryRun -and $script:Created.Count -gt 0) {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
        Write-Host "  published $Table" -ForegroundColor Green
    }

    if (-not $DryRun) {
        # Verify by metadata read: exactly the 14 values with their labels.
        $after = Get-RuleTypeOptions
        Write-Host "  after:  $(Format-Options $after)"
        $all = [System.Collections.Generic.SortedDictionary[int, string]]::new()
        foreach ($k in $Ontology.Keys) { $all.Add($k, $Ontology[$k]) }; foreach ($k in $Billing.Keys) { $all.Add($k, $Billing[$k]) }
        $bad = @()
        foreach ($k in $all.Keys) {
            $o = $after | Where-Object { $_.Value -eq $k }
            if (-not $o -or $o.Label -ne $all[$k]) { $bad += "$k expected '$($all[$k])', got '$($o.Label)'" }
        }
        $systemPresent = @($after | Where-Object { $_.Value -eq 100000014 -and $_.Label -eq 'SubmissionIntegrity' }).Count
        if ($after.Count -ne ($all.Count + $systemPresent)) { $bad += "expected $($all.Count + $systemPresent) options, found $($after.Count)" }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  verified: $($all.Count) options match RuleType.cs" -ForegroundColor Green
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
    Write-Host "Options already inserted stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}" -f $script:Changed.Count)
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
