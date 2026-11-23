<#
.SYNOPSIS
    eBilling R1 task 116 - prepares and (guarded) performs the removal of the two obsolete sprk_sourcedocument lookups on
    sprk_ruleset and sprk_ratecard (EB-D-148: a Procedure's document is the Procedure Version's Stored File).

.DESCRIPTION
    WITHOUT -Delete this script is READ-ONLY. For each of sprk_ruleset.sprk_sourcedocument and sprk_ratecard.sprk_sourcedocument it
    reports
      - the row count of the table and how many rows hold a value in the lookup (all states),
      - the system forms and the views (system and personal) that mention the column,
      - the relationship behind the lookup, and
      - RetrieveDependenciesForDelete for the column (each dependent named),
    then a verdict per column (READY, BLOCKED with the reasons, or ALREADY REMOVED). It only GETs (the metadata reads and the
    RetrieveDependenciesForDelete function); it writes nothing, publishes nothing and has no -WhatIf side effect to worry about.

    sprk_storedfile (task 088) has its own sprk_sourcedocument column and form/view mentions. They are not touched and are not counted:
    forms and views of sprk_storedfile are ignored.

    WITH -Delete -OwnerApproval '<date and who>' -Confirm it removes the lookups. DESTRUCTIVE and irreversible: project CLAUDE.md
    section 5 requires the owner's explicit OK first, quoted in the task notes. It runs only after Deploy-EbillingFormsViews.ps1 has
    taken the column off both main forms in the target environment and this task's BFF is deployed. Before it writes anything it
    re-runs the census and REFUSES (exit 2, nothing changed) unless
      - -OwnerApproval is given and -Confirm is given,
      - no row of either table holds a value in the column,
      - no form or view mentions the column,
      - RetrieveDependenciesForDelete lists nothing, and
      - the relationship behind each lookup is exactly the expected one.
    The table row count itself is reported but is not a blocker: a row whose lookup is empty loses nothing.
    Each delete also asks the operator through ShouldProcess; -WhatIf shows the steps without running them.

    The delete is the relationship delete (the lookup column and its name companion go with it), as the 011 script does for
    sprk_timekeeper.sprk_invoicelineitem, then an entity-scoped publish of the two tables. Never sprk_document rows or SPE containers (O-13).

    Idempotent: a column that is already gone is reported as such and skipped.

    This script does not touch src/dataverse/solutions/SpaarkeMaster/** (task 079 re-syncs the solution source from dev).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment. Dev only in R1.
.PARAMETER Delete
    Perform the delete. Without it the script only reports.
.PARAMETER OwnerApproval
    Required with -Delete. Free text recorded in the output: the date and who gave the owner's OK.

.EXAMPLE
    pwsh scripts/ebilling/Remove-EbillingSourceDocumentColumns.ps1
    pwsh scripts/ebilling/Remove-EbillingSourceDocumentColumns.ps1 -Delete -OwnerApproval '<date and who>' -Confirm -WhatIf

.NOTES
    Project: ebilling-r1, task 116. Exit codes: 0 = report / done, 1 = error, 2 = refused (blocker, missing approval or confirm).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$Delete,
    [string]$OwnerApproval
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Column = 'sprk_sourcedocument'

# The two tables, the entity-set name and id column for counting, and the one relationship each lookup must sit behind.
$Targets = @(
    @{ Table = 'sprk_ruleset';  EntitySet = 'sprk_rulesets';  IdColumn = 'sprk_rulesetid';  Relationship = 'sprk_document_ruleset_sourcedocument' }
    @{ Table = 'sprk_ratecard'; EntitySet = 'sprk_ratecards'; IdColumn = 'sprk_ratecardid'; Relationship = 'sprk_document_ratecard_sourcedocument' }
)
# Tables whose own sprk_sourcedocument is a different column and stays (088).
$OtherOwners = @('sprk_storedfile')

$null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun

# ---------------------------------------------------------------------------------------------------------------
# Reads. Everything in the census goes through Read-Dv, which can only GET.
# ---------------------------------------------------------------------------------------------------------------

function Read-Dv([string]$Path) { return Invoke-Dv -Path $Path }
function Read-DvOrNull([string]$Path) { return Get-DvOrNull -Path $Path }

# Short names for the component types RetrieveDependenciesForDelete reports.
function Get-ComponentKind([int]$Type) {
    switch ($Type) {
        1 { 'entity' }
        2 { 'attribute' }
        10 { 'relationship' }
        26 { 'view' }
        59 { 'chart' }
        60 { 'system form' }
        80 { 'app module' }
        default { "component type $Type" }
    }
}

function Get-Census($Target) {
    $table = $Target.Table
    $c = [ordered]@{ Table = $table; ColumnPresent = $false; RowCount = $null; NonNullCount = $null; Forms = @(); Views = @(); PersonalViews = @(); OtherReferences = @(); Relationships = @(); Dependencies = @() }

    $attr = Get-Attr $table $Column 'LogicalName,AttributeType,MetadataId,IsCustomAttribute'
    if (-not $attr) { return [pscustomobject]$c }
    $c.ColumnPresent = $true
    $c.AttributeId = $attr.MetadataId
    $c.AttributeType = $attr.AttributeType

    # Rows: exact counts over all states (Get-RowCountExact retries and returns $null when none could be had).
    $c.RowCount = Get-RowCountExact $Target.EntitySet $Target.IdColumn
    $c.NonNullCount = Get-RowCountExact $Target.EntitySet $Target.IdColumn "_${Column}_value ne null"

    # Forms and views that mention the column. Own table first; then any other table that is not a known other owner.
    $c.Forms = @((Read-Dv "systemforms?`$select=name,formid,type&`$filter=objecttypecode eq '$table' and contains(formxml,'$Column')").value |
        ForEach-Object { [pscustomobject]@{ Name = $_.name; Id = $_.formid; Type = $_.type } })
    $c.Views = @((Read-Dv "savedqueries?`$select=name,savedqueryid&`$filter=returnedtypecode eq '$table' and (contains(fetchxml,'$Column') or contains(layoutxml,'$Column'))").value |
        ForEach-Object { [pscustomobject]@{ Name = $_.name; Id = $_.savedqueryid } })
    $c.PersonalViews = @((Read-Dv "userqueries?`$select=name,userqueryid&`$filter=returnedtypecode eq '$table' and (contains(fetchxml,'$Column') or contains(layoutxml,'$Column'))").value |
        ForEach-Object { [pscustomobject]@{ Name = $_.name; Id = $_.userqueryid } })

    # A view or form of another table that selects this column through a link-entity alias would also break; the other owners'
    # own column of the same name is not a reference to this one.
    $others = [System.Collections.Generic.List[string]]::new()
    foreach ($f in @((Read-Dv "systemforms?`$select=name,formid,objecttypecode&`$filter=objecttypecode ne '$table' and contains(formxml,'$table') and contains(formxml,'$Column')").value)) {
        if ($f.objecttypecode -notin $OtherOwners) { $others.Add("form '$($f.name)' of $($f.objecttypecode)") }
    }
    foreach ($v in @((Read-Dv "savedqueries?`$select=name,returnedtypecode&`$filter=returnedtypecode ne '$table' and contains(fetchxml,'$table') and contains(fetchxml,'$Column')").value)) {
        if ($v.returnedtypecode -notin $OtherOwners) { $others.Add("view '$($v.name)' of $($v.returnedtypecode)") }
    }
    $c.OtherReferences = $others.ToArray()

    # The relationship behind the lookup.
    $c.Relationships = @((Read-Dv "EntityDefinitions(LogicalName='$table')/ManyToOneRelationships?`$select=SchemaName,MetadataId&`$filter=ReferencingAttribute eq '$Column'").value |
        ForEach-Object { [pscustomobject]@{ SchemaName = $_.SchemaName; MetadataId = $_.MetadataId } })

    # RetrieveDependenciesForDelete for the column, each dependent named.
    $deps = @((Read-Dv "RetrieveDependenciesForDelete(ObjectId=$($attr.MetadataId),ComponentType=2)").value)
    $c.Dependencies = @(foreach ($d in $deps) {
        $kind = Get-ComponentKind ([int]$d.dependentcomponenttype)
        $label = [string]$d.dependentcomponentobjectid
        if ([int]$d.dependentcomponenttype -eq 60) {
            $form = Read-DvOrNull "systemforms($label)?`$select=name,objecttypecode"
            if ($form) { $label = "$($form.objecttypecode) form '$($form.name)' ($label)" }
        }
        [pscustomobject]@{ Kind = $kind; Label = $label }
    })
    return [pscustomobject]$c
}

# ---------------------------------------------------------------------------------------------------------------
# Verdicts (pure: a census in, strings out). Pester lifts these.
# ---------------------------------------------------------------------------------------------------------------

# The reasons one column cannot be removed. Empty = READY. A column that is already gone has no blocker.
function Get-ColumnBlockers($Census, $Target) {
    $b = [System.Collections.Generic.List[string]]::new()
    if (-not $Census.ColumnPresent) { return $b.ToArray() }
    $t = $Census.Table
    if ($null -eq $Census.NonNullCount) { $b.Add("$t : the rows holding a value could not be counted") }
    elseif ($Census.NonNullCount -gt 0) { $b.Add("$($Census.NonNullCount) $t row(s) hold a value in $Column") }
    foreach ($f in @($Census.Forms)) { $b.Add("$t form '$($f.Name)' ($($f.Id)) still mentions $Column") }
    foreach ($v in @($Census.Views)) { $b.Add("$t view '$($v.Name)' still mentions $Column") }
    foreach ($v in @($Census.PersonalViews)) { $b.Add("$t personal view '$($v.Name)' still mentions $Column") }
    foreach ($o in @($Census.OtherReferences)) { $b.Add("$o mentions $t.$Column") }
    foreach ($d in @($Census.Dependencies)) { $b.Add("$t : dependent $($d.Kind) $($d.Label)") }
    $rels = @($Census.Relationships)
    if ($rels.Count -ne 1 -or $rels[0].SchemaName -ne $Target.Relationship) {
        $b.Add("$t : the relationship behind $Column is not exactly $($Target.Relationship) (found: $((($rels | ForEach-Object { $_.SchemaName }) -join ', ') -replace '^$', 'none'))")
    }
    return $b.ToArray()
}

# Everything that makes -Delete refuse: the flags, then each column's blockers. Empty = the delete may go on.
function Get-DeleteRefusals($Census, [string]$OwnerApproval, [bool]$Confirmed, $Targets) {
    $r = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($OwnerApproval)) { $r.Add("-OwnerApproval '<date and who>' is required: the owner's explicit OK comes first (project CLAUDE.md section 5)") }
    if (-not $Confirmed) { $r.Add('-Confirm is required') }
    foreach ($t in $Targets) {
        $c = @($Census | Where-Object { $_.Table -eq $t.Table })[0]
        if (-not $c) { $r.Add("$($t.Table) : no census"); continue }
        foreach ($x in (Get-ColumnBlockers $c $t)) { $r.Add($x) }
    }
    return $r.ToArray()
}

function Write-Census($Census, $Target) {
    $t = $Census.Table
    Write-Host "$t.$Column" -ForegroundColor Cyan
    if (-not $Census.ColumnPresent) { Write-Host '  column not found (already removed)' -ForegroundColor Yellow; return }
    Write-Host "  type            : $($Census.AttributeType) (MetadataId $($Census.AttributeId))"
    Write-Host "  rows            : $(if ($null -eq $Census.RowCount) { 'unknown' } else { $Census.RowCount })"
    Write-Host "  non-null        : $(if ($null -eq $Census.NonNullCount) { 'unknown' } else { $Census.NonNullCount })" -ForegroundColor $(if ($Census.NonNullCount -eq 0) { 'Green' } else { 'Red' })
    Write-Host "  relationship    : $((@($Census.Relationships) | ForEach-Object { $_.SchemaName }) -join ', ')"
    Write-Host "  forms           : $(@($Census.Forms).Count)"
    foreach ($f in @($Census.Forms)) { Write-Host "    main/other form '$($f.Name)' ($($f.Id), type $($f.Type))" }
    Write-Host "  views           : $(@($Census.Views).Count) system, $(@($Census.PersonalViews).Count) personal"
    foreach ($v in @($Census.Views)) { Write-Host "    view '$($v.Name)'" }
    foreach ($v in @($Census.PersonalViews)) { Write-Host "    personal view '$($v.Name)'" }
    Write-Host "  other tables    : $(@($Census.OtherReferences).Count) reference(s) (sprk_storedfile's own column is ignored)"
    foreach ($o in @($Census.OtherReferences)) { Write-Host "    $o" }
    Write-Host "  dependencies    : $(@($Census.Dependencies).Count) (RetrieveDependenciesForDelete)"
    foreach ($d in @($Census.Dependencies)) { Write-Host "    $($d.Kind): $($d.Label)" }
}

# ---------------------------------------------------------------------------------------------------------------
# Removal (only reached through -Delete after the refusals are empty)
# ---------------------------------------------------------------------------------------------------------------

function Remove-SourceDocumentLookup($Census, $Target) {
    if (-not $Census.ColumnPresent) { Write-Host "  skipped  $($Census.Table).$Column already gone" -ForegroundColor Yellow; return }
    $rel = @($Census.Relationships)[0]
    if (-not $PSCmdlet.ShouldProcess("$($Census.Table).$Column (relationship $($rel.SchemaName))", 'DELETE the lookup column and its relationship')) { return }
    Invoke-WhilePropagating { Invoke-Dv -Path "RelationshipDefinitions($($rel.MetadataId))" -Method DELETE | Out-Null } | Out-Null
    $gone = $false
    for ($i = 0; $i -lt 18 -and -not $gone; $i++) {
        $gone = -not (Get-Attr $Census.Table $Column)
        if (-not $gone) { Start-Sleep -Seconds 5 }
    }
    if (-not $gone) { throw "$($Census.Table).$Column is still readable after the relationship delete." }
    Write-Host "  DELETED  $($Census.Table).$Column (relationship $($rel.SchemaName))" -ForegroundColor Green
}

function Publish-Tables([string[]]$Tables) {
    if (-not $PSCmdlet.ShouldProcess(($Tables -join ', '), 'PublishXml (entity-scoped, never tenant-wide)')) { return }
    $ents = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities>$ents</entities></importexportxml>" } | Out-Null
    Write-Host "  published $($Tables -join ', ')" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ''
Write-Host "eBilling task 116 - sprk_sourcedocument on sprk_ruleset and sprk_ratecard" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if (-not $Delete) { 'REPORT (read-only, GET only)' } elseif ($DryRun) { 'DELETE -WhatIf (no writes)' } else { 'DELETE' })"
Write-Host ''

try {
    if ($EnvironmentUrl -notmatch 'spaarkedev1') { throw "Dev only in R1: $EnvironmentUrl" }
    if ($Delete -and -not $PSBoundParameters.ContainsKey('EnvironmentUrl') -and ([uri]$EnvironmentUrl).Host -ne 'spaarkedev1.crm.dynamics.com') {
        throw [EscalationException]::new("-Delete refuses the environment '$EnvironmentUrl' that came from `$env:DATAVERSE_URL: pass -EnvironmentUrl explicitly. Nothing was changed.")
    }

    $confirmed = [bool]($PSBoundParameters.ContainsKey('Confirm') -and $PSBoundParameters['Confirm'])
    $census = @(foreach ($t in $Targets) { Get-Census $t })
    foreach ($t in $Targets) { Write-Census (@($census | Where-Object { $_.Table -eq $t.Table })[0]) $t; Write-Host '' }

    $allBlockers = 0
    foreach ($t in $Targets) {
        $c = @($census | Where-Object { $_.Table -eq $t.Table })[0]
        $blockers = @(Get-ColumnBlockers $c $t)
        $allBlockers += $blockers.Count
        if (-not $c.ColumnPresent) { Write-Host "VERDICT $($t.Table).$Column : ALREADY REMOVED" -ForegroundColor Yellow }
        elseif ($blockers.Count -eq 0) { Write-Host "VERDICT $($t.Table).$Column : READY - no value, no form, no view, no dependent" -ForegroundColor Green }
        else {
            Write-Host "VERDICT $($t.Table).$Column : BLOCKED" -ForegroundColor Red
            foreach ($x in $blockers) { Write-Host "  - $x" -ForegroundColor Red }
        }
    }

    if (-not $Delete) {
        Write-Host ''
        Write-Host "Report only. Nothing was changed. To delete (after the owner's OK, the forms script and this task's BFF are deployed): -Delete -OwnerApproval '<date and who>' -Confirm." -ForegroundColor Cyan
        exit 0
    }

    $refusals = @(Get-DeleteRefusals $census $OwnerApproval $confirmed $Targets)
    if ($refusals.Count -gt 0) {
        throw [EscalationException]::new("Refusing to delete: $($refusals.Count) reason(s): $($refusals -join '; '). Nothing was changed.")
    }

    Write-Host ''
    Write-Host "Owner approval recorded: $OwnerApproval" -ForegroundColor Cyan
    foreach ($t in $Targets) {
        Write-Host "Removing $($t.Table).$Column" -ForegroundColor Cyan
        Remove-SourceDocumentLookup (@($census | Where-Object { $_.Table -eq $t.Table })[0]) $t
    }
    Publish-Tables @($Targets | ForEach-Object { $_.Table })
}
catch [EscalationException] {
    Write-Host ''
    Write-Host "ESCALATION - REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Steps already done stay done; re-run to continue (a column that is already gone is skipped).' -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($DryRun) { Write-Host 'WhatIf run: nothing was written.' -ForegroundColor Cyan }
exit 0
