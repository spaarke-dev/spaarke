<#
.SYNOPSIS
    eBilling R1 095f-2s (EB-D-240 / EB-D-242) - two additive Memo note columns: sprk_invoice.sprk_decisionnote and
    sprk_invoicecorrection.sprk_note. -WhatIf is a dry run, -Apply writes, -Verify checks.

.DESCRIPTION
    WHY. 095f part 2 lets a decision-maker attach a note to a decision. EB-D-240: the invoice's decision note is INTERNAL and lives in its
    own column, so it never shares sprk_correctionnote (the firm's own correction text, written only by the submission persister).
    EB-D-242: a correction row has no note column, so the note a correction carries goes on the row itself.

    THE COLUMNS (both Memo, MaxLength 2000, optional, NOT field-secured):
      sprk_invoice.sprk_decisionnote            SchemaName sprk_DecisionNote, label "Decision note". Internal: no firm-facing reader,
                                                allow-list or SPA type names it (a must-not-fire ArchTest guards that).
      sprk_invoicecorrection.sprk_note          SchemaName sprk_Note, label "Note". Not part of the row's derived id.

    THE SOLUTION. Both columns are added to -SolutionUniqueName (default SpaarkeMaster), which must be an unmanaged solution that contains
    BOTH tables. Otherwise it is an escalation (exit 2, nothing changed). This script never edits the SpaarkeMaster source files; a deploy
    task syncs the solution export afterwards.

    NO BACKFILL. Existing rows read blank.

    DEPLOY ORDER. Run -Apply (with the owner's go, and uac-r2 notified before and after) and check that -Verify exits 0 BEFORE any BFF build
    that selects or writes either column. The ArchTest InvoiceNoteColumnsGuardTests fails the build for a BFF that names a column that is
    neither in the solution source nor on its pending list.

    CONFLICTS (exit 2, nothing changed): a column exists with another type, another MaxLength, or field-secured. This script never alters an
    existing column.

    -VERIFY (read only; exit 0 = pass, 1 = a gap, each one named): both columns exist, are Memo, have MaxLength 2000 and are not field-secured.

    Publish is entity-scoped (PublishXml naming only these two tables, never the tenant-wide publish). Every -Apply run publishes, also when
    both columns already exist: a first run that created a column but stopped before its publish finished is healed by a re-run.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    The unmanaged solution the columns are added to. Default SpaarkeMaster. It must contain both tables.

.PARAMETER Apply
    Write: create the columns and publish the two tables. Without -Apply, -Verify or -WhatIf the script refuses to run.

.PARAMETER Verify
    Read-only check with a pass / fail exit code. Cannot be combined with -WhatIf or -Apply.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -Apply
    pwsh scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 095f-2s. Pattern: Deploy-EbillingFirmDescriptionColumn.ps1 (130e-1), extended to two tables. Every request goes
    through EbillingSchema.psm1's Invoke-Dv, which sends Consistency: Strong. Auth: the operator's own az CLI identity. No secrets.
    Exit codes: 0 = pass / done, 1 = a gap or an error, 2 = escalation (a conflicting column or an owning solution that lacks a table).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($DryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes.' }
if (-not $Apply -and -not $Verify -and -not $DryRun) { throw 'Choose a mode: -WhatIf (dry run), -Apply (write) or -Verify (check).' }

# ── Constants ──────────────────────────────────────────────────────────────────────────────────────────────────────────────
$ColumnMaxLength = 2000
$EntityComponentType = 1
# Solutions that contain every table and are never the place to add a column.
$SystemSolutions = @('Default', 'Active', 'Basic')

# The two columns. Column is the logical name the BFF will name; the ArchTest InvoiceNoteColumnsGuardTests keeps the same two names.
$ColumnSpecs = @(
    @{
        Table       = 'sprk_invoice'
        Column      = 'sprk_decisionnote'
        SchemaName  = 'sprk_DecisionNote'
        Label       = 'Decision note'
        Description = 'The internal note on the decision taken on this invoice (EB-D-240). Never shown to the firm; separate from sprk_correctionnote, the firm''s own correction text.'
    }
    @{
        Table       = 'sprk_invoicecorrection'
        Column      = 'sprk_note'
        SchemaName  = 'sprk_Note'
        Label       = 'Note'
        Description = 'The note the caller entered with this correction (EB-D-242). Not part of the row''s derived id.'
    }
)

function New-NoteColumnDef($Spec) {
    return New-MemoDef $Spec.SchemaName $Spec.Label $Spec.Description $ColumnMaxLength
}

# The solution to add the columns to. $Solutions is every solution that contains one table (objects with uniquename and ismanaged).
# Returns the requested unique name when it is an unmanaged, non-system solution that contains the table; otherwise throws an
# EscalationException saying why. Pure.
function Select-OwningSolution($Solutions, [string]$Requested, [string]$Table) {
    $candidates = @($Solutions | Where-Object { $_ -and $_.ismanaged -ne $true -and $SystemSolutions -notcontains [string]$_.uniquename } |
            ForEach-Object { [string]$_.uniquename } | Sort-Object -Unique)
    if (-not [string]::IsNullOrWhiteSpace($Requested) -and $candidates -contains $Requested) { return $Requested }
    $have = if ($candidates.Count -gt 0) { $candidates -join ', ' } else { 'none' }
    throw [EscalationException]::new("Solution '$Requested' does not contain $Table (unmanaged solutions that do: $have). Owner decision needed.")
}

# What the Publish step does (pure): 'whatif' on a dry run; otherwise 'publish' when this run wrote metadata and 'republish' when it did
# not. A re-run publishes too, so a column a timed-out first run created but never published becomes readable. PublishXml is idempotent.
function Get-PublishStep([bool]$DryRun, [int]$Wrote) {
    if ($DryRun) { return 'whatif' }
    if ($Wrote -gt 0) { return 'publish' }
    return 'republish'
}

# The PublishXml parameter: the tables of $Specs and nothing else (pure).
function Get-PublishParameterXml($Specs) {
    $entities = (@($Specs | ForEach-Object { $_.Table } | Sort-Object -Unique) | ForEach-Object { "<entity>$_</entity>" }) -join ''
    return "<importexportxml><entities>$entities</entities></importexportxml>"
}

# Every solution that contains the table as a component. Read only.
function Get-TableSolutions([string]$Table) {
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

# Every way the column of $Spec differs from the specification above; empty when it is as specified. Read only. A column that does not
# exist is one gap.
function Get-ColumnGaps($Spec) {
    $table = $Spec.Table; $column = $Spec.Column
    $gaps = [System.Collections.Generic.List[string]]::new()
    $attr = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$column')?`$select=LogicalName,AttributeType,IsSecured" -Retries 2
    if (-not $attr) { $gaps.Add("$table.$column does not exist"); return , $gaps }
    if ($attr.AttributeType -ne 'Memo') {
        $gaps.Add("$table.$column is '$($attr.AttributeType)', not Memo")
        return , $gaps
    }
    $memo = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$column')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength" -Retries 4
    if (-not $memo) { $gaps.Add("$table.$column length could not be read") }
    elseif ([int]$memo.MaxLength -ne $ColumnMaxLength) { $gaps.Add("$table.$column has MaxLength $($memo.MaxLength), expected $ColumnMaxLength") }
    if ($attr.IsSecured -eq $true) {
        $gaps.Add("$table.$column is field-secured; the note columns are not secured")
    }
    return , $gaps
}

# Creates one column (or skips it when it exists) and, for an existing column, escalates when it is not as specified. A dry run writes
# nothing (Add-Column reports WOULD CREATE).
function Invoke-ApplyColumn($Spec) {
    Set-EbEntity $Spec.Table
    $skippedBefore = $script:Skipped.Count
    Add-Column $Spec.Column (New-NoteColumnDef $Spec) 'Memo'
    # Add-Column checks the type only. An existing Memo of another length, or a secured one, is not this column: never altered here.
    if ($script:Skipped.Count -gt $skippedBefore) {
        $gaps = Get-ColumnGaps $Spec
        if ($gaps.Count -gt 0) {
            throw [EscalationException]::new("$($Spec.Table).$($Spec.Column) is not as specified: $($gaps -join '; '). Owner decision needed (this script does not alter an existing column).")
        }
    }
}

Write-Host ""
Write-Host "eBilling 095f-2s - $(($ColumnSpecs | ForEach-Object { "$($_.Table).$($_.Column)" }) -join ', ')" -ForegroundColor Cyan

if ($Verify) {
    Write-Host ("Environment: {0}   Mode: VERIFY (read only)" -f $EnvironmentUrl)
    Write-Host ""
    $allGaps = [System.Collections.Generic.List[string]]::new()
    try {
        $null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'none' -DryRun -Entity $ColumnSpecs[0].Table
        foreach ($spec in $ColumnSpecs) {
            if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$($spec.Table)')?`$select=LogicalName" -Retries 4)) { throw "$($spec.Table) does not exist in $EnvironmentUrl." }
            foreach ($g in (Get-ColumnGaps $spec)) { $allGaps.Add($g) }
        }
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    if ($allGaps.Count -eq 0) {
        foreach ($spec in $ColumnSpecs) { Write-Host "PASS: $($spec.Table).$($spec.Column) exists as Memo, MaxLength $ColumnMaxLength, not field-secured." -ForegroundColor Green }
        exit 0
    }
    Write-Host "GAPS:" -ForegroundColor Red
    foreach ($g in $allGaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}

try {
    # Initialised once to read, then again with the solution that owns the tables (the header every write carries).
    $null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'none' -DryRun:$DryRun -Entity $ColumnSpecs[0].Table
    foreach ($spec in $ColumnSpecs) { $null = Select-OwningSolution (Get-TableSolutions $spec.Table) $SolutionUniqueName $spec.Table }
    $Solution = $SolutionUniqueName
    $State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $Solution -DryRun:$DryRun -Entity $ColumnSpecs[0].Table
    $script:Created = $State.Created
    $script:Changed = $State.Changed
    $script:Skipped = $State.Skipped
    Write-Host ("Environment: {0}   Solution: {1}   Mode: {2}" -f $EnvironmentUrl, $Solution, $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' }))
    Write-Host ""

    $n = 0
    foreach ($spec in $ColumnSpecs) {
        $n++
        Write-Host "$n. $($spec.Table).$($spec.Column) (Memo, MaxLength $ColumnMaxLength, optional, not secured)" -ForegroundColor Cyan
        Invoke-ApplyColumn $spec
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $publishStep = Get-PublishStep $DryRun ($script:Created.Count + $script:Changed.Count)
    if ($publishStep -eq 'whatif') {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } else {
        # Entity-scoped to these two tables (never the tenant-wide publish). Also on a re-run that wrote nothing: an earlier run's publish may not have finished.
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = (Get-PublishParameterXml $ColumnSpecs) } | Out-Null
        if ($publishStep -eq 'publish') {
            Write-Host "  published $(($ColumnSpecs | ForEach-Object { $_.Table }) -join ', ')" -ForegroundColor Green
        } else {
            Write-Host "  nothing changed - published again (heals a first run whose publish did not finish)" -ForegroundColor Green
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
    Write-Host "A column, if created, stays (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
