<#
.SYNOPSIS
    eBilling R1 130e-1 (DEF-060, EB-D-212 / EB-D-213) - sprk_policyversion.sprk_firmdescription, the per-rule firm description.
    -WhatIf is a dry run, -Apply writes, -Verify checks.

.DESCRIPTION
    WHY. A billing Signal's firm reason (sprk_signal.sprk_firmreason) is the one sentence the firm reads about why a rule fired. It is
    copied from the policy version the Signal cites, so the text is fixed with that version: a published version's row is never edited
    (the BFF puts a change on a new draft version), so the description cannot change under a Signal that already cites it.

    THE COLUMN. sprk_policyversion.sprk_firmdescription ("Firm Description"), Memo, MaxLength 2000 (storage), optional, NOT field-secured
    (EB-D-213: unsecured, like sprk_messagetemplate). The length actually allowed is 200 characters, checked by the BFF
    (FirmDescriptionRules), not by the column. The BFF also refuses braces, markup, invisible characters and text that names the rule set
    or the rule, at edit, at publish and again when it copies the text onto a Signal.

    THE SOLUTION. The column is added to the unmanaged solution that already contains sprk_policyversion (header MSCRM.SolutionUniqueName),
    read from solutioncomponents. -SolutionUniqueName names it explicitly; it must then be one of the solutions that contain the table.
    No such solution, or more than one when none is named, is an escalation (exit 2, nothing changed).

    NO BACKFILL. Existing versions read blank, and a blank description means the Signal carries no firm reason (as today).

    DEPLOY ORDER. Run -Apply (with the owner's go) and check that -Verify exits 0 BEFORE the BFF build that carries 130e-1: that build
    selects the column on every policy version read.

    CONFLICTS (exit 2, nothing changed): the column exists with another type, another MaxLength, or field-secured. This script never alters
    an existing column.

    -VERIFY (read only; exit 0 = pass, 1 = a gap, each one named): the column exists, is Memo, has MaxLength 2000 and is not field-secured.

    Publish is entity-scoped (PublishXml for sprk_policyversion only, never the tenant-wide publish). Every -Apply run publishes, also when
    the column already exists: a first run that created the column but stopped before its publish finished (a timeout) is healed by a
    re-run. Publishing a table that has nothing unpublished changes nothing.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    The unmanaged solution the column is added to. Default: the one unmanaged solution that contains sprk_policyversion.

.PARAMETER Apply
    Write: create the column and publish sprk_policyversion. Without -Apply, -Verify or -WhatIf the script refuses to run.

.PARAMETER Verify
    Read-only check with a pass / fail exit code. Cannot be combined with -WhatIf or -Apply.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingFirmDescriptionColumn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingFirmDescriptionColumn.ps1 -Apply
    pwsh scripts/ebilling/Deploy-EbillingFirmDescriptionColumn.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 130e-1. Pattern: Deploy-EbillingInvoiceReviewDetailColumn.ps1 (one column, escalation, -WhatIf / -Apply /
    -Verify, entity-scoped publish only on a write). Every request goes through EbillingSchema.psm1's Invoke-Dv, which sends
    Consistency: Strong. Auth: the operator's own az CLI identity. No secrets.
    Exit codes: 0 = pass / done, 1 = a gap or an error, 2 = escalation (a conflicting column or no single owning solution).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = '',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($DryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes.' }
if (-not $Apply -and -not $Verify -and -not $DryRun) { throw 'Choose a mode: -WhatIf (dry run), -Apply (write) or -Verify (check).' }

# ── Constants (the BFF reads the column as BillingFactLoader.FirmDescriptionColumn) ─────────────────────────────────────────
$Table = 'sprk_policyversion'
$Column = 'sprk_firmdescription'
$ColumnSchemaName = 'sprk_FirmDescription'
$ColumnMaxLength = 2000
$EntityComponentType = 1
# Solutions that contain every table and are never the place to add a column.
$SystemSolutions = @('Default', 'Active', 'Basic')

$Description = "The one sentence the firm reads when this rule flags an invoice line (130e-1). Copied onto the billing Signal it creates. At most 200 characters; no braces, markup or invisible characters; does not name the rule set or the rule. Fixed once the version is published."

function New-FirmDescriptionDef {
    return New-MemoDef $ColumnSchemaName 'Firm Description' $Description $ColumnMaxLength
}

# The solution to add the column to. $Solutions is every solution that contains the table (objects with uniquename and ismanaged).
# Returns the unique name, or throws an EscalationException naming why none can be chosen. Pure.
function Select-OwningSolution($Solutions, [string]$Requested) {
    $candidates = @($Solutions | Where-Object { $_ -and $_.ismanaged -ne $true -and $SystemSolutions -notcontains [string]$_.uniquename } |
            ForEach-Object { [string]$_.uniquename } | Sort-Object -Unique)
    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        if ($candidates -contains $Requested) { return $Requested }
        $have = if ($candidates.Count -gt 0) { $candidates -join ', ' } else { 'none' }
        throw [EscalationException]::new("Solution '$Requested' does not contain $Table (unmanaged solutions that do: $have). Owner decision needed.")
    }
    if ($candidates.Count -eq 1) { return $candidates[0] }
    if ($candidates.Count -eq 0) {
        throw [EscalationException]::new("No unmanaged solution other than $($SystemSolutions -join '/') contains $Table. Pass -SolutionUniqueName (owner decision).")
    }
    throw [EscalationException]::new("More than one unmanaged solution contains ${Table}: $($candidates -join ', '). Pass -SolutionUniqueName (owner decision).")
}

# What the Publish step does (pure): 'whatif' on a dry run; otherwise 'publish' when this run wrote metadata and 'republish' when it did
# not. A re-run publishes too, so a column a timed-out first run created but never published becomes readable. PublishXml is idempotent.
function Get-PublishStep([bool]$DryRun, [int]$Wrote) {
    if ($DryRun) { return 'whatif' }
    if ($Wrote -gt 0) { return 'publish' }
    return 'republish'
}

# Every solution that contains the table as a component. Read only.
function Get-TableSolutions {
    $entity = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId" -Retries 4
    if (-not $entity) { throw "$Table does not exist in $EnvironmentUrl." }
    $components = @((Invoke-Dv -Path "solutioncomponents?`$select=_solutionid_value&`$filter=objectid eq $($entity.MetadataId) and componenttype eq $EntityComponentType").value)
    $solutions = [System.Collections.Generic.List[object]]::new()
    foreach ($id in @($components | ForEach-Object { [string]$_._solutionid_value } | Sort-Object -Unique)) {
        $solution = Get-DvOrNull "solutions($id)?`$select=uniquename,ismanaged"
        if ($solution) { $solutions.Add($solution) }
    }
    return , $solutions
}

# Every way the column differs from the specification above; empty when it is as specified. Read only. A column that does not exist is one gap.
function Get-ColumnGaps {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $attr = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,IsSecured" -Retries 2
    if (-not $attr) { $gaps.Add("$Table.$Column does not exist"); return , $gaps }
    if ($attr.AttributeType -ne 'Memo') {
        $gaps.Add("$Table.$Column is '$($attr.AttributeType)', not Memo")
        return , $gaps
    }
    $memo = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength" -Retries 4
    if (-not $memo) { $gaps.Add("$Table.$Column length could not be read") }
    elseif ([int]$memo.MaxLength -ne $ColumnMaxLength) { $gaps.Add("$Table.$Column has MaxLength $($memo.MaxLength), expected $ColumnMaxLength") }
    if ($attr.IsSecured -eq $true) {
        $gaps.Add("$Table.$Column is field-secured; EB-D-213 decided it is not secured")
    }
    return , $gaps
}

Write-Host ""
Write-Host "eBilling 130e-1 - $Table.$Column" -ForegroundColor Cyan

if ($Verify) {
    Write-Host ("Environment: {0}   Mode: VERIFY (read only)" -f $EnvironmentUrl)
    Write-Host ""
    try {
        $null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'none' -DryRun -Entity $Table
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
        $gaps = Get-ColumnGaps
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    if ($gaps.Count -eq 0) {
        Write-Host "PASS: $Table.$Column exists as Memo, MaxLength $ColumnMaxLength, not field-secured." -ForegroundColor Green
        exit 0
    }
    Write-Host "GAPS:" -ForegroundColor Red
    foreach ($g in $gaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}

try {
    # Initialised once to read, then again with the solution that owns the table (the header every write carries).
    $null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'none' -DryRun:$DryRun -Entity $Table
    $Solution = Select-OwningSolution (Get-TableSolutions) $SolutionUniqueName
    $State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $Solution -DryRun:$DryRun -Entity $Table
    $script:Created = $State.Created
    $script:Changed = $State.Changed
    $script:Skipped = $State.Skipped
    Write-Host ("Environment: {0}   Solution: {1}   Mode: {2}" -f $EnvironmentUrl, $Solution, $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' }))
    Write-Host ""

    Write-Host "1. $Table.$Column (Memo, MaxLength $ColumnMaxLength, optional, not secured)" -ForegroundColor Cyan
    Add-Column $Column (New-FirmDescriptionDef) 'Memo'

    # Add-Column checks the type only. An existing Memo of another length, or a secured one, is not this column: never altered here.
    if ($script:Skipped.Count -gt 0) {
        $gaps = Get-ColumnGaps
        if ($gaps.Count -gt 0) {
            throw [EscalationException]::new("$Table.$Column is not as specified: $($gaps -join '; '). Owner decision needed (this script does not alter an existing column).")
        }
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $publishStep = Get-PublishStep $DryRun ($script:Created.Count + $script:Changed.Count)
    if ($publishStep -eq 'whatif') {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } else {
        # Entity-scoped (never the tenant-wide publish). Also on a re-run that wrote nothing: an earlier run's publish may not have finished.
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
        } | Out-Null
        if ($publishStep -eq 'publish') {
            Write-Host "  published $Table" -ForegroundColor Green
        } else {
            Write-Host "  nothing changed - published $Table again (heals a first run whose publish did not finish)" -ForegroundColor Green
        }
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
    Write-Host "The column, if created, stays (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
