<#
.SYNOPSIS
    eBilling R1 decision D-84 - gives sprk_userprofile.sprk_primaryrole its own global choice (sprk_userprofilerole) and
    reduces the global choice sprk_timekeeperrole to exactly the 20 LEDES timekeeper classifications.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only in R1). Every step is check-then-act; a second run reports no change and
    writes nothing (no publish either). Run -WhatIf first: it reads everything, prints what it would do and writes nothing.

    Why (decisions-log D-83 / D-84): sprk_timekeeperrole was shared by the invoice side (LEDES classifications) and by the
    Console user profile (in-house roles such as General Counsel). D-84: the user profile keeps its 14 values and labels
    (the Console picker userProfileService.ts PRIMARY_ROLE_OPTIONS and StatedProfileReader.cs hard-code them, so they must
    not change) in its own list, and the 10 non-LEDES options leave sprk_timekeeperrole.

    Steps
      0  Evidence (read only): the user-profile rows holding a role, the column's dependencies (RetrieveDependenciesForDelete),
         its placements on forms / views / charts / workflows.
      1  Global choice sprk_userprofilerole ("User Profile Role") with the 14 values 100000000-100000013 and the labels they
         have in dev, created in the solution when absent (a present one is compared value for value, label for label).
      2  Column sprk_userprofile.sprk_primaryrole. A column cannot be re-bound in place, so while it is still bound to
         sprk_timekeeperrole the script: saves the row values and the form / view placements to
         projects/ebilling-r1/notes/d84-userprofile-primaryrole-predrop.json; takes the column off the forms / views (the
         form cell is replaced by an empty cell with the same id, so the position is kept); deletes the platform-generated
         aiskillconfig ais_FormFillFieldOptOut_sprk_userprofile_sprk_primaryrole (it blocks the column delete with
         0x8004f01f); drops the column; recreates it with the same schema name sprk_PrimaryRole, display name, description
         and required level, bound to sprk_userprofilerole; puts it back on the same forms / views at the same position;
         restores the saved values and verifies each. If an earlier run stopped after the drop, the column is recreated and
         restored from the saved file.
         REFUSES (exit 2, nothing changed) on any dependency other than the generated skill config and the forms / views
         listed in the evidence, on a chart / workflow / business rule / personal view that uses the column, and on a
         placement it cannot take off and restore.
      3  Only after sprk_primaryrole is bound to sprk_userprofilerole: the 10 non-LEDES options (100000000 Senior Partner,
         ...02 Senior Associate, ...05 Specialist, ...07 Practice Support, ...08 Administrator, ...09 Legal Operations,
         ...10 Senior Counsel, ...11 General Counsel, ...12 Counsel, ...13 Associate General Counsel) are deleted from
         sprk_timekeeperrole. REFUSES (exit 2) when a row holds one of those values in a column bound to the choice, or
         when a column other than the four that stay bound binds the choice:
           sprk_contactorganization.sprk_role, sprk_billingevent.sprk_timekeeperrole,
           sprk_timekeeper.sprk_classification, sprk_ratecardrate.sprk_classification.
      4  Publish (only when something changed) and verify: the 20 LEDES options with their ExternalValue codes remain, the
         column is bound to sprk_userprofilerole with the 14 options, every saved value reads back.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingUserProfileRoleChoice.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingUserProfileRoleChoice.ps1

.NOTES
    Project: ebilling-r1, decision D-84 (follow-up to task 017). Dataverse helpers: EbillingSchema.psm1. Pattern for the
    guarded drop and recreate: Deploy-EbillingApproverRoleSchema.ps1 (Invoke-MatterReplacement).
    Exit codes: 0 = done (created / changed / skipped), 1 = error, 2 = escalation (a refused guard; nothing further changed).
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

$OldChoice = 'sprk_timekeeperrole'
$NewChoice = 'sprk_userprofilerole'
$NewChoiceDisplay = 'User Profile Role'
$Table = 'sprk_userprofile'
$Column = 'sprk_primaryrole'
$ColumnSchema = 'sprk_PrimaryRole'
$SkillConfigName = "ais_FormFillFieldOptOut_$($Table)_$Column"
$SavedFile = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\projects\ebilling-r1\notes\d84-userprofile-primaryrole-predrop.json'))

# The 14 original values and labels (userProfileService.ts PRIMARY_ROLE_OPTIONS, StatedProfileReader.cs; read from dev on
# 2026-10-15 and identical). Step 1 checks them against sprk_timekeeperrole while those options still exist there.
$UserProfileOptions = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$UserProfileSource = @{
    100000000 = 'Senior Partner'; 100000001 = 'Partner'; 100000002 = 'Senior Associate'; 100000003 = 'Associate'
    100000004 = 'Paralegal'; 100000005 = 'Specialist'; 100000006 = 'Other'; 100000007 = 'Practice Support'
    100000008 = 'Administrator'; 100000009 = 'Legal Operations'; 100000010 = 'Senior Counsel'; 100000011 = 'General Counsel'
    100000012 = 'Counsel'; 100000013 = 'Associate General Counsel'
}
foreach ($k in $UserProfileSource.Keys) { $UserProfileOptions.Add([int]$k, [string]$UserProfileSource[$k]) }
# The 10 options that are not LEDES classifications (017 notes section 8).
$NonLedes = 100000000, 100000002, 100000005, 100000007, 100000008, 100000009, 100000010, 100000011, 100000012, 100000013
# The 20 LEDES classifications that remain (017 notes section 8.3): value, label, ExternalValue code.
$Ledes = [System.Collections.Generic.SortedDictionary[int, object[]]]::new()
$LedesSource = @{
    100000001 = @('Partner', 'PARTNR'); 100000003 = @('Associate', 'ASSOC'); 100000004 = @('Paralegal', 'PARALG'); 100000006 = @('Other', 'NBOTHR')
    100000014 = @('Of Counsel', 'OFCOUN'); 100000015 = @('Associate Trainee', 'TRANEE'); 100000016 = @('Staff Attorney', 'STFATT')
    100000017 = @('Secondee', 'SECNDE'); 100000018 = @('Legal Intern', 'LGLINT'); 100000019 = @('Legal Assistant', 'LGLAST')
    100000020 = @('Clerk or Secretary', 'CLKSEC'); 100000021 = @('Librarian', 'LIBRRN'); 100000022 = @('Research Specialist/Researcher', 'RSRCHR')
    100000023 = @('Analyst', 'ANALST'); 100000024 = @('High-Level Non-Lawyer Professional', 'HLNLPF'); 100000025 = @('Legal Project Manager', 'LGPRMG')
    100000026 = @('IP Agent', 'IPAGNT'); 100000027 = @('Costs Counsel', 'CSTCNS'); 100000028 = @('Contract Attorney', 'TEMPAT')
    100000029 = @('Contract Other Staff', 'TEMPOS')
}
foreach ($k in $LedesSource.Keys) { $Ledes.Add([int]$k, [object[]]$LedesSource[$k]) }
# The columns that stay bound to sprk_timekeeperrole (D-83).
$StayBound = @(
    @{ Table = 'sprk_contactorganization'; Column = 'sprk_role' }
    @{ Table = 'sprk_billingevent'; Column = 'sprk_timekeeperrole' }
    @{ Table = 'sprk_timekeeper'; Column = 'sprk_classification' }
    @{ Table = 'sprk_ratecardrate'; Column = 'sprk_classification' }
)

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------

# PATCH without If-Match is an upsert and a systemform formxml update is then silently ignored (scripts/Lock-CoreAncestorStampColumnsOnForms.ps1).
$IfMatch = @{ 'If-Match' = '*' }
$FormattedHeader = @{ Prefer = 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"' }

# A form / view update lands in the unpublished layer; a normal read (and the dependency check) sees the published one, so the
# change is published before it is read back.
function Publish-UserProfile { Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null }

function Get-ChoiceDef([string]$Name) { return Get-DvOrNull "GlobalOptionSetDefinitions(Name='$Name')" }

function ConvertTo-OptionList($choice) {
    return @($choice.Options | Sort-Object Value | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; Label = [string]$_.Label.UserLocalizedLabel.Label; External = [string]$_.ExternalValue }
        })
}

function Format-OptionList($list) { return (($list | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ') }

# The global choice a picklist column is bound to ($null when the column is missing).
function Get-BoundChoice([string]$T, [string]$C) {
    $a = Get-Attr $T $C 'LogicalName,AttributeType,MetadataId'
    if (-not $a) { return $null }
    if ($a.AttributeType -ne 'Picklist') { throw [EscalationException]::new("$T.$C is a '$($a.AttributeType)' column, expected a Picklist. Nothing was changed.") }
    $x = Get-DvOrNull "EntityDefinitions(LogicalName='$T')/Attributes(LogicalName='$C')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=GlobalOptionSet(`$select=Name,IsGlobal)" -Retries 2
    $name = if ($x -and $x.GlobalOptionSet) { [string]$x.GlobalOptionSet.Name } else { '' }
    return [pscustomobject]@{ MetadataId = $a.MetadataId; ChoiceName = $name; IsGlobal = [bool]$x.GlobalOptionSet.IsGlobal }
}

function Get-RowsHoldingValues([string]$T, [string]$C, [int[]]$Values) {
    $set = Get-DvOrNull "EntityDefinitions(LogicalName='$T')?`$select=EntitySetName" -Retries 4
    if (-not $set) { return $null }
    $filter = '(' + (($Values | ForEach-Object { "$C eq $_" }) -join ' or ') + ')'
    return Get-RowCountExact $set.EntitySetName "$($T)id" $filter
}

# All user-profile rows holding a role (any state), with the label Dataverse shows.
function Get-ProfileRows {
    $rows = [System.Collections.Generic.List[object]]::new()
    $r = Invoke-Dv "sprk_userprofiles?`$select=sprk_userprofileid,sprk_name,sprk_primaryrole,statecode&`$filter=sprk_primaryrole ne null" -ExtraHeaders $FormattedHeader
    while ($r) {
        foreach ($x in @($r.value)) {
            $rows.Add([pscustomobject]@{
                    Id = [string]$x.sprk_userprofileid; Name = [string]$x.sprk_name; Value = [int]$x.sprk_primaryrole
                    Label = [string]$x.'sprk_primaryrole@OData.Community.Display.V1.FormattedValue'; StateCode = [int]$x.statecode
                })
        }
        $next = $r.'@odata.nextLink'
        $r = if ($next) { Invoke-Dv $next -ExtraHeaders $FormattedHeader } else { $null }
    }
    return , @($rows)
}

# ---- form / view XML ----
# A cell that holds the column's control. Cells are not nested; a self-closing cell is excluded from the start match.
$CellPattern = '<cell\b(?:"[^"]*"|[^">])*(?<!/)>(?:(?!</cell>).)*?datafieldname="' + $Column + '"(?:(?!</cell>).)*?</cell>'

function Find-ColumnCells([string]$xml) { return @([regex]::Matches($xml, $CellPattern, 'Singleline') | ForEach-Object { $_.Value }) }

function Get-CellId([string]$cellXml) {
    $m = [regex]::Match($cellXml, '^<cell\b(?:"[^"]*"|[^">])*?\bid="(\{[^"}]+\})"')
    if (-not $m.Success) { throw [EscalationException]::new("A form cell holding $Column has no id attribute; it cannot be taken off and put back at the same position. Nothing was changed.") }
    return $m.Groups[1].Value
}

function Get-CellIdList([string]$xml) { return @([regex]::Matches($xml, '<cell\b(?:"[^"]*"|[^">])*?\bid="(\{[^"}]+\})"') | ForEach-Object { $_.Groups[1].Value }) }

# The empty cell that keeps the position (the same shape the form already uses for its empty cells).
function New-PlaceholderCell([string]$id) { return '<cell locklevel="0" id="' + $id + '"><labels><label description="" languagecode="1033" /></labels></cell>' }

function Remove-ColumnFromView([string]$fetch, [string]$layout) {
    $f = [regex]::Replace($fetch, '\s*<attribute\s+name="' + $Column + '"\s*/>', '')
    $l = [regex]::Replace($layout, '\s*<cell\s+name="' + $Column + '"[^>]*/>', '')
    return [pscustomobject]@{ Fetch = $f; Layout = $l }
}

# Everything that places the column somewhere. Read only.
function Get-Placements {
    $p = [ordered]@{ Forms = @(); Views = @(); Other = @() }
    foreach ($f in @((Invoke-Dv "systemforms?`$select=formid,name,type,formxml&`$filter=objecttypecode eq '$Table'").value)) {
        if ("$($f.formxml)" -match $Column) {
            $cells = @(Find-ColumnCells $f.formxml)
            $p.Forms += [pscustomobject]@{ FormId = [string]$f.formid; Name = [string]$f.name; Xml = [string]$f.formxml; Cells = $cells; Occurrences = ([regex]::Matches($f.formxml, $Column)).Count }
        }
    }
    foreach ($v in @((Invoke-Dv "savedqueries?`$select=savedqueryid,name,fetchxml,layoutxml&`$filter=returnedtypecode eq '$Table'").value)) {
        if ("$($v.fetchxml)$($v.layoutxml)" -match $Column) {
            $p.Views += [pscustomobject]@{ ViewId = [string]$v.savedqueryid; Name = [string]$v.name; Fetch = [string]$v.fetchxml; Layout = [string]$v.layoutxml }
        }
    }
    foreach ($v in @((Invoke-Dv "userqueries?`$select=name,fetchxml,layoutxml&`$filter=returnedtypecode eq '$Table'").value)) { if ("$($v.fetchxml)$($v.layoutxml)" -match $Column) { $p.Other += "personal view '$($v.name)'" } }
    foreach ($c in @((Invoke-Dv "savedqueryvisualizations?`$select=name,datadescription,presentationdescription&`$filter=primaryentitytypecode eq '$Table'").value)) { if ("$($c.datadescription)$($c.presentationdescription)" -match $Column) { $p.Other += "chart '$($c.name)'" } }
    foreach ($w in @((Invoke-Dv "workflows?`$select=name,xaml,clientdata&`$filter=primaryentity eq '$Table'").value)) { if ("$($w.xaml)$($w.clientdata)" -match $Column) { $p.Other += "workflow / business rule '$($w.name)'" } }
    return $p
}

# RetrieveDependenciesForDelete of the column, classified. Anything that is not the generated skill config, a form or a view
# found in the placements is unexpected.
function Get-ColumnDependencies([string]$MetadataId, $Placements) {
    $deps = Get-DvOrNull "RetrieveDependenciesForDelete(ObjectId=$MetadataId,ComponentType=2)"
    $all = @(); $bad = @(); $skills = @()
    foreach ($d in @($deps.value)) {
        $t = [int]$d.dependentcomponenttype; $id = [string]$d.dependentcomponentobjectid
        $desc = "type $t id $id"; $ok = $false
        if ($t -eq 10314) {
            $sk = Get-DvOrNull "aiskillconfigs($id)?`$select=uniquename"
            if ($sk) { $desc = "aiskillconfig $($sk.uniquename)"; if ($sk.uniquename -eq $SkillConfigName) { $ok = $true; $skills += $id } }
        } elseif ($t -eq 60) {
            $f = @($Placements.Forms | Where-Object { $_.FormId -eq $id })
            if ($f.Count) { $desc = "form '$($f[0].Name)' ($id)"; $ok = $true }
        } elseif ($t -eq 26) {
            $v = @($Placements.Views | Where-Object { $_.ViewId -eq $id })
            if ($v.Count) { $desc = "view '$($v[0].Name)' ($id)"; $ok = $true }
        }
        $all += $desc
        if (-not $ok) { $bad += $desc }
    }
    return [pscustomobject]@{ All = $all; Unexpected = $bad; SkillConfigIds = $skills; NonSkill = @($all | Where-Object { $_ -notlike 'aiskillconfig *' }) }
}

function Read-SavedState { if (Test-Path -LiteralPath $SavedFile) { return Get-Content -LiteralPath $SavedFile -Raw | ConvertFrom-Json } else { return $null } }

# Restores whatever the saved file says is missing: form cells (placeholder -> original cell), view columns, row values.
# Idempotent: a placement that is already back, a value that is already there, is skipped. Returns the number of writes.
function Restore-FromSaved($saved) {
    $writes = 0
    foreach ($sf in @($saved.forms)) {
        $cur = Get-DvOrNull "systemforms($($sf.formId))?`$select=formxml"
        if (-not $cur) { throw [EscalationException]::new("Saved form '$($sf.name)' ($($sf.formId)) no longer exists; it cannot be restored. See $SavedFile.") }
        $xml = [string]$cur.formxml
        if (@(Find-ColumnCells $xml).Count -gt 0) { $script:Skipped.Add("form '$($sf.name)' placement"); Write-Host "  skipped  form '$($sf.name)' already holds $Column" -ForegroundColor Yellow; continue }
        $ph = New-PlaceholderCell $sf.cellId
        $idx = $xml.IndexOf($ph, [System.StringComparison]::Ordinal)
        if ($idx -lt 0) { throw [EscalationException]::new("Form '$($sf.name)' neither holds $Column nor the placeholder cell $($sf.cellId); it was edited since the column was taken off. Put it back by hand (cell XML in $SavedFile). Nothing further changed.") }
        if ($DryRun) { $script:Changed.Add("form '$($sf.name)' placement"); Write-Host "  WOULD PUT $Column back on form '$($sf.name)' (cell $($sf.cellId))" -ForegroundColor Cyan; $writes++; continue }
        $new = $xml.Substring(0, $idx) + $sf.cellXml + $xml.Substring($idx + $ph.Length)
        Invoke-Dv "systemforms($($sf.formId))" -Method PATCH -ExtraHeaders $IfMatch -Body @{ formxml = $new } | Out-Null
        $script:Changed.Add("form '$($sf.name)' placement"); $writes++
        Write-Host "  RESTORED $Column on form '$($sf.name)' at the same position (cell $($sf.cellId))" -ForegroundColor Green
    }
    foreach ($sv in @($saved.views)) {
        $cur = Get-DvOrNull "savedqueries($($sv.viewId))?`$select=fetchxml,layoutxml"
        if (-not $cur) { throw [EscalationException]::new("Saved view '$($sv.name)' ($($sv.viewId)) no longer exists; it cannot be restored. See $SavedFile.") }
        if ("$($cur.fetchxml)$($cur.layoutxml)" -match $Column) { $script:Skipped.Add("view '$($sv.name)' placement"); Write-Host "  skipped  view '$($sv.name)' already holds $Column" -ForegroundColor Yellow; continue }
        $stripped = Remove-ColumnFromView $sv.fetch $sv.layout
        if ([string]$cur.fetchxml -ne $stripped.Fetch -or [string]$cur.layoutxml -ne $stripped.Layout) {
            throw [EscalationException]::new("View '$($sv.name)' was edited since the column was taken off; it cannot be restored mechanically. Original XML is in $SavedFile. Nothing further changed.")
        }
        if ($DryRun) { $script:Changed.Add("view '$($sv.name)' placement"); Write-Host "  WOULD PUT $Column back on view '$($sv.name)'" -ForegroundColor Cyan; $writes++; continue }
        Invoke-Dv "savedqueries($($sv.viewId))" -Method PATCH -ExtraHeaders $IfMatch -Body @{ fetchxml = $sv.fetch; layoutxml = $sv.layout } | Out-Null
        $script:Changed.Add("view '$($sv.name)' placement"); $writes++
        Write-Host "  RESTORED $Column on view '$($sv.name)'" -ForegroundColor Green
    }
    if ($writes -gt 0 -and -not $DryRun) {
        Publish-UserProfile
        foreach ($sf in @($saved.forms)) {
            $chk = Get-DvOrNull "systemforms($($sf.formId))?`$select=formxml"
            if (@(Find-ColumnCells ([string]$chk.formxml)).Count -ne 1) { throw "Form '$($sf.name)' does not hold $Column after the restore and publish; the original cell XML is in $SavedFile." }
        }
    }
    foreach ($sr in @($saved.rows)) {
        if (-not (Get-DvOrNull "sprk_userprofiles($($sr.id))?`$select=sprk_userprofileid")) { Write-Host "  note     row $($sr.id) ($($sr.name)) no longer exists; value not restored" -ForegroundColor DarkYellow; continue }
        # A column created a moment ago can be unknown to the data layer for a few seconds ("Could not find a property named").
        $cur = $null
        for ($i = 0; $i -lt 18 -and -not $cur; $i++) {
            try { $cur = Invoke-Dv "sprk_userprofiles($($sr.id))?`$select=sprk_primaryrole" }
            catch { if ($_.Exception.Message -notmatch '(?i)could not find a property|0x80060888') { throw }; Start-Sleep -Seconds 5 }
        }
        if (-not $cur) { throw "sprk_userprofiles.$Column is not readable after 90 s; re-run the script to restore the saved value." }
        if ($null -ne $cur.sprk_primaryrole -and [int]$cur.sprk_primaryrole -eq [int]$sr.value) { $script:Skipped.Add("row $($sr.name) value"); Write-Host "  skipped  row '$($sr.name)' already holds $($sr.value) ($($sr.label))" -ForegroundColor Yellow; continue }
        if ($null -ne $cur.sprk_primaryrole) { Write-Host "  note     row '$($sr.name)' now holds $($cur.sprk_primaryrole), saved $($sr.value) ($($sr.label)); left as is" -ForegroundColor DarkYellow; continue }
        if ($DryRun) { $script:Changed.Add("row $($sr.name) value"); Write-Host "  WOULD RESTORE row '$($sr.name)' = $($sr.value) ($($sr.label))" -ForegroundColor Cyan; $writes++; continue }
        Invoke-Dv "sprk_userprofiles($($sr.id))" -Method PATCH -ExtraHeaders $IfMatch -Body @{ sprk_primaryrole = [int]$sr.value } | Out-Null
        $script:Changed.Add("row $($sr.name) value"); $writes++
        Write-Host "  RESTORED row '$($sr.name)' = $($sr.value) ($($sr.label))" -ForegroundColor Green
    }
    return $writes
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling D-84 - user-profile role choice; $OldChoice becomes LEDES-only" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in @($Table) + @($StayBound | ForEach-Object { $_.Table })) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) { throw "$ref does not exist in $EnvironmentUrl." }
    }
    $sol = Get-DvOrNull "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'"
    if (-not $sol -or @($sol.value).Count -eq 0) { throw "Solution $SolutionUniqueName does not exist in $EnvironmentUrl." }
    $SolutionId = @($sol.value)[0].solutionid
    $oldDef = Get-ChoiceDef $OldChoice
    if (-not $oldDef) { throw [EscalationException]::new("Global choice $OldChoice does not exist in $EnvironmentUrl. Nothing was changed.") }
    $savedNow = Read-SavedState

    # --- 0. Evidence ---------------------------------------------------------------------------------------------------
    Write-Host "0. Current state" -ForegroundColor Cyan
    $oldOptions = ConvertTo-OptionList $oldDef
    Write-Host "  global choice ${OldChoice}: $($oldOptions.Count) options"
    $newDef = Get-ChoiceDef $NewChoice
    Write-Host "  global choice ${NewChoice}: $(if ($newDef) { "$(@($newDef.Options).Count) options" } else { 'absent' })"
    $bound = Get-BoundChoice $Table $Column
    if ($bound) { Write-Host "  $Table.$Column`: bound to '$($bound.ChoiceName)' (global: $($bound.IsGlobal))" } else { Write-Host "  $Table.$Column`: absent" }
    $rowsBefore = @()
    $placements = $null
    if ($bound) {
        $rowsBefore = Get-ProfileRows
        Write-Host "  $Table rows holding $Column`: $($rowsBefore.Count) (of $(Get-RowCountExact 'sprk_userprofiles' 'sprk_userprofileid') in total)"
        foreach ($r in $rowsBefore) { Write-Host "    row $($r.Id) '$($r.Name)': $($r.Value) ($($r.Label)), statecode $($r.StateCode)" }
        $placements = Get-Placements
        Write-Host "  placements: $(@($placements.Forms).Count) form(s) [$((@($placements.Forms) | ForEach-Object { $_.Name + ' ' + $_.FormId }) -join '; ')], $(@($placements.Views).Count) view(s), $(@($placements.Other).Count) chart / workflow / personal view$(if (@($placements.Other).Count) { ' - ' + (@($placements.Other) -join '; ') })"
        $dep0 = Get-ColumnDependencies $bound.MetadataId $placements
        Write-Host "  dependencies (RetrieveDependenciesForDelete): $(@($dep0.All).Count)$(if (@($dep0.All).Count) { ' - ' + (@($dep0.All) -join '; ') })"
    } elseif ($savedNow) {
        Write-Host "  saved pre-drop state: $SavedFile ($(@($savedNow.rows).Count) row value(s), $(@($savedNow.forms).Count) form placement(s), $(@($savedNow.views).Count) view placement(s))"
    }

    # --- 1. Global choice sprk_userprofilerole -------------------------------------------------------------------------
    Write-Host "1. Global choice $NewChoice" -ForegroundColor Cyan
    if ($newDef) {
        $have = ConvertTo-OptionList $newDef
        $diff = @()
        foreach ($k in $UserProfileOptions.Keys) {
            $h = $have | Where-Object { $_.Value -eq [int]$k }
            if (-not $h) { $diff += "missing $k=$($UserProfileOptions[$k])" } elseif ($h.Label -cne $UserProfileOptions[$k]) { $diff += "$k is '$($h.Label)', expected '$($UserProfileOptions[$k])'" }
        }
        if ($diff.Count) { throw [EscalationException]::new("Global choice $NewChoice differs from the 14 user-profile roles: $($diff -join '; '). Observed: $(Format-OptionList $have). Not changed.") }
        $extra = @($have | Where-Object { -not $UserProfileOptions.ContainsKey($_.Value) })
        if ($extra.Count) { Write-Host "  note     $NewChoice also holds $($extra.Count) option(s) beyond the original 14: $(Format-OptionList $extra) (left as is)" -ForegroundColor DarkYellow }
        $script:Skipped.Add("optionset $NewChoice"); Write-Host "  skipped  $NewChoice (exists, the 14 values and labels match)" -ForegroundColor Yellow
    } else {
        # Copied from dev: while the options still exist in sprk_timekeeperrole they must read exactly as the embedded list.
        $diff = @()
        foreach ($k in $UserProfileOptions.Keys) {
            $o = $oldOptions | Where-Object { $_.Value -eq [int]$k }
            if ($o -and $o.Label -cne $UserProfileOptions[$k]) { $diff += "$k is '$($o.Label)' in $OldChoice, expected '$($UserProfileOptions[$k])'" }
        }
        if ($diff.Count) { throw [EscalationException]::new("The labels in dev differ from the Console's hard-coded list: $($diff -join '; '). Owner decision needed. Nothing was changed.") }
        if ($DryRun) {
            $script:Created.Add("optionset $NewChoice"); Write-Host "  WOULD CREATE global choice $NewChoice ($($UserProfileOptions.Count) options: $(($UserProfileOptions.Keys | ForEach-Object { "$_=$($UserProfileOptions[$_])" }) -join ', '))" -ForegroundColor Cyan
        } else {
            $options = @(foreach ($k in $UserProfileOptions.Keys) { @{ Value = [int]$k; Label = New-Label $UserProfileOptions[$k] } })
            $def = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'; Name = $NewChoice; DisplayName = New-Label $NewChoiceDisplay
                IsGlobal = $true; OptionSetType = 'Picklist'; Options = $options
            }
            try { Invoke-Dv -Path 'GlobalOptionSetDefinitions' -Method POST -Body $def -ExtraHeaders $State.SolutionHeader | Out-Null; $createdChoice = $true }
            catch { if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }; $createdChoice = $false }
            for ($i = 0; $i -lt 12 -and -not $newDef; $i++) { $newDef = Get-ChoiceDef $NewChoice; if (-not $newDef) { Start-Sleep -Seconds 5 } }
            if (-not $newDef) { throw "Global choice $NewChoice was created but is not readable after 60 s." }
            if ($createdChoice) { $script:Created.Add("optionset $NewChoice"); Write-Host "  CREATED  global choice $NewChoice ($($UserProfileOptions.Count) options)" -ForegroundColor Green }
            else { $script:Skipped.Add("optionset $NewChoice"); Write-Host "  skipped  $NewChoice (exists, found via create refusal)" -ForegroundColor Yellow }
        }
    }
    # Solution membership of the choice (component type 9).
    if ($newDef) {
        $inSol = @((Get-DvOrNull "solutioncomponents?`$select=componenttype&`$filter=_solutionid_value eq $SolutionId and objectid eq $($newDef.MetadataId) and componenttype eq 9").value)
        if ($inSol.Count) { $script:Skipped.Add("solution $NewChoice"); Write-Host "  skipped  $NewChoice already a $SolutionUniqueName component" -ForegroundColor Yellow }
        elseif ($DryRun) { $script:Changed.Add("solution $NewChoice") }
        else {
            Invoke-Dv -Path 'AddSolutionComponent' -Method POST -Body @{ ComponentId = $newDef.MetadataId; ComponentType = 9; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
            $script:Changed.Add("solution $NewChoice"); Write-Host "  ADDED    $NewChoice to $SolutionUniqueName" -ForegroundColor Green
        }
    }

    # --- 2. sprk_userprofile.sprk_primaryrole -------------------------------------------------------------------------
    Write-Host "2. Column $Table.$Column" -ForegroundColor Cyan
    $rebound = $false          # true when the column was dropped / is gone and needs recreating by this run
    $saved = $null
    if ($bound -and $bound.ChoiceName -eq $NewChoice) {
        $script:Skipped.Add("$Table.$Column binding"); Write-Host "  skipped  $Column already bound to $NewChoice" -ForegroundColor Yellow
        if ($savedNow) { Write-Host "  checking the saved placements and values are all in place"; $null = Restore-FromSaved $savedNow }
    } elseif ($bound -and $bound.ChoiceName -ne $OldChoice) {
        throw [EscalationException]::new("$Table.$Column is bound to '$($bound.ChoiceName)', neither $OldChoice nor $NewChoice. Owner decision needed. Nothing was changed.")
    } elseif ($bound) {
        # ---- guards: refuse, change nothing ----
        if (-not $bound.IsGlobal) { throw [EscalationException]::new("$Table.$Column uses a local choice '$($bound.ChoiceName)', not the global $OldChoice. Nothing was changed.") }
        if (@($placements.Other).Count) { throw [EscalationException]::new("$Table.$Column is used by: $(@($placements.Other) -join '; '). The script does not take those off and put them back. Nothing was changed.") }
        foreach ($f in @($placements.Forms)) {
            if ($f.Cells.Count -ne 1) { throw [EscalationException]::new("Form '$($f.Name)' holds $Column in $($f.Cells.Count) cells; expected exactly 1. Nothing was changed.") }
            if ($f.Occurrences -ne ([regex]::Matches($f.Cells[0], $Column)).Count) { throw [EscalationException]::new("Form '$($f.Name)' references $Column outside its cell (header, footer, events or a hidden field). Nothing was changed.") }
        }
        foreach ($v in @($placements.Views)) {
            $s = Remove-ColumnFromView $v.Fetch $v.Layout
            if ("$($s.Fetch)$($s.Layout)" -match $Column) { throw [EscalationException]::new("View '$($v.Name)' uses $Column in a filter, sort or link; the script only takes it off the column list. Nothing was changed.") }
        }
        if (@($dep0.Unexpected).Count) { throw [EscalationException]::new("$Table.$Column has dependencies other than the generated skill config and its forms / views: $(@($dep0.Unexpected) -join '; '). Nothing was changed.") }
        if (@($dep0.SkillConfigIds).Count -gt 1) { throw [EscalationException]::new("$Table.$Column has $(@($dep0.SkillConfigIds).Count) skill configs; expected at most 1. Nothing was changed.") }
        $attrFull = Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=SchemaName,RequiredLevel,IsAuditEnabled,IsValidForAdvancedFind,DisplayName,Description"
        if ($attrFull.SchemaName -cne $ColumnSchema) { throw [EscalationException]::new("$Table.$Column has schema name '$($attrFull.SchemaName)', expected $ColumnSchema. Nothing was changed.") }
        foreach ($r in $rowsBefore) { if (-not $UserProfileOptions.ContainsKey($r.Value)) { throw [EscalationException]::new("Row '$($r.Name)' holds $($r.Value), which is not one of the 14 user-profile roles; it could not be restored. Nothing was changed.") } }

        if ($DryRun) {
            $script:Changed.Add("$Table.$Column rebound to $NewChoice")
            Write-Host "  WOULD SAVE $(@($rowsBefore).Count) row value(s) and $(@($placements.Forms).Count) form / $(@($placements.Views).Count) view placement(s) to $SavedFile" -ForegroundColor Cyan
            Write-Host "  WOULD TAKE $Column off the form(s) / view(s), DELETE the generated skill config $SkillConfigName, DROP $Column, CREATE it as $ColumnSchema bound to $NewChoice ('$($attrFull.DisplayName.UserLocalizedLabel.Label)', required $($attrFull.RequiredLevel.Value))" -ForegroundColor Cyan
            Write-Host "  WOULD RESTORE the placement(s) and the value(s), then verify" -ForegroundColor Cyan
        } else {
            # ---- save the state that the drop destroys (merged with an earlier interrupted attempt) ----
            $savedForms = [System.Collections.Generic.List[object]]::new()
            foreach ($f in @($placements.Forms)) {
                $cellXml = $f.Cells[0]
                $savedForms.Add([ordered]@{ formId = $f.FormId; name = $f.Name; cellId = (Get-CellId $cellXml); cellXml = $cellXml; cellIds = @(Get-CellIdList $f.Xml); formXml = $f.Xml })
            }
            if ($savedNow) { foreach ($sf in @($savedNow.forms)) { if (-not ($savedForms | Where-Object { $_.formId -eq $sf.formId })) { $savedForms.Add([ordered]@{ formId = $sf.formId; name = $sf.name; cellId = $sf.cellId; cellXml = $sf.cellXml; cellIds = @($sf.cellIds); formXml = $sf.formXml }) } } }
            $savedViews = [System.Collections.Generic.List[object]]::new()
            foreach ($v in @($placements.Views)) { $savedViews.Add([ordered]@{ viewId = $v.ViewId; name = $v.Name; fetch = $v.Fetch; layout = $v.Layout }) }
            if ($savedNow) { foreach ($sv in @($savedNow.views)) { if (-not ($savedViews | Where-Object { $_.viewId -eq $sv.viewId })) { $savedViews.Add([ordered]@{ viewId = $sv.viewId; name = $sv.name; fetch = $sv.fetch; layout = $sv.layout }) } } }
            $doc = [ordered]@{
                decision = 'D-84'; savedAt = (Get-Date).ToUniversalTime().ToString('o'); environment = $EnvironmentUrl
                note = "State of $Table.$Column before it was dropped and recreated on $NewChoice. Values and placements are restored from this file by Deploy-EbillingUserProfileRoleChoice.ps1."
                column = [ordered]@{
                    schemaName = $attrFull.SchemaName; displayName = [string]$attrFull.DisplayName.UserLocalizedLabel.Label
                    description = [string]$attrFull.Description.UserLocalizedLabel.Label; requiredLevel = [string]$attrFull.RequiredLevel.Value
                    isAuditEnabled = [bool]$attrFull.IsAuditEnabled.Value; isValidForAdvancedFind = [bool]$attrFull.IsValidForAdvancedFind.Value
                    boundChoice = $bound.ChoiceName; metadataId = $bound.MetadataId
                }
                rows = @($rowsBefore | ForEach-Object { [ordered]@{ id = $_.Id; name = $_.Name; value = $_.Value; label = $_.Label; statecode = $_.StateCode } })
                forms = @($savedForms)
                views = @($savedViews)
            }
            New-Item -ItemType Directory -Force -Path (Split-Path $SavedFile) | Out-Null
            ($doc | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $SavedFile -Encoding utf8
            Write-Host "  SAVED    pre-drop state: $SavedFile ($(@($rowsBefore).Count) row value(s), $($savedForms.Count) form(s), $($savedViews.Count) view(s))" -ForegroundColor Green
            $saved = Read-SavedState

            # ---- take the column off the forms and views ----
            foreach ($f in @($placements.Forms)) {
                $ph = New-PlaceholderCell (Get-CellId $f.Cells[0])
                $idx = $f.Xml.IndexOf($f.Cells[0], [System.StringComparison]::Ordinal)
                $new = $f.Xml.Substring(0, $idx) + $ph + $f.Xml.Substring($idx + $f.Cells[0].Length)
                Invoke-Dv "systemforms($($f.FormId))" -Method PATCH -ExtraHeaders $IfMatch -Body @{ formxml = $new } | Out-Null
                Write-Host "  REMOVED  $Column from form '$($f.Name)' (empty cell $(Get-CellId $f.Cells[0]) keeps the position)" -ForegroundColor Magenta
            }
            foreach ($v in @($placements.Views)) {
                $s = Remove-ColumnFromView $v.Fetch $v.Layout
                Invoke-Dv "savedqueries($($v.ViewId))" -Method PATCH -ExtraHeaders $IfMatch -Body @{ fetchxml = $s.Fetch; layoutxml = $s.Layout } | Out-Null
                Write-Host "  REMOVED  $Column from view '$($v.Name)'" -ForegroundColor Magenta
            }
            if (@($placements.Forms).Count + @($placements.Views).Count -gt 0) {
                Publish-UserProfile
                foreach ($f in @($placements.Forms)) {
                    $chk = Invoke-Dv "systemforms($($f.FormId))?`$select=formxml"
                    if ("$($chk.formxml)" -match $Column) { throw [EscalationException]::new("The update of form '$($f.Name)' did not take (it still holds $Column after the PATCH and publish). Nothing was dropped.") }
                }
                Write-Host "  published the form / view change; the forms no longer reference $Column" -ForegroundColor Magenta
            }
            # ---- the dependency API must now show only the generated skill config ----
            $depNow = $null
            for ($i = 0; $i -lt 8; $i++) {
                $depNow = Get-ColumnDependencies $bound.MetadataId (Get-Placements)
                if (@($depNow.NonSkill).Count -eq 0) { break }
                Start-Sleep -Seconds 5
            }
            if (@($depNow.NonSkill).Count) {
                Write-Host "  dependencies remain after taking the column off: $(@($depNow.NonSkill) -join '; ') - putting the placements back" -ForegroundColor Red
                $null = Restore-FromSaved $saved
                throw [EscalationException]::new("$Table.$Column still has dependencies after it was taken off the forms / views: $(@($depNow.NonSkill) -join '; '). The placements were put back; the column was not dropped.")
            }
            # ---- delete the skill config, drop the column ----
            Write-Host "  DROPPING $Table.$Column ($(@($rowsBefore).Count) row value(s) saved)" -ForegroundColor Magenta
            foreach ($id in @($depNow.SkillConfigIds)) {
                Write-Host "  DELETING generated aiskillconfig $id ($SkillConfigName; it blocks the column delete with 0x8004f01f)" -ForegroundColor Magenta
                Invoke-Dv -Path "aiskillconfigs($id)" -Method DELETE | Out-Null
            }
            Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Attributes($($bound.MetadataId))" -Method DELETE | Out-Null
            $gone = $false
            for ($i = 0; $i -lt 24 -and -not $gone; $i++) { if (-not (Get-Attr $Table $Column)) { $gone = $true } else { Start-Sleep -Seconds 5 } }
            if (-not $gone) { throw "$Table.$Column was deleted but still reads as present after 120 s. Re-run the script: it recreates and restores from $SavedFile." }
            $rebound = $true
        }
    } else {
        # The column is gone: an earlier run stopped after the drop. Recreate and restore from the saved file.
        if (-not $savedNow) { throw [EscalationException]::new("$Table.$Column does not exist and there is no saved pre-drop state at $SavedFile. Owner decision needed. Nothing was changed.") }
        if ($DryRun) { Write-Host "  WOULD RECREATE $Column on $NewChoice and restore from $SavedFile" -ForegroundColor Cyan; $script:Changed.Add("$Table.$Column rebound to $NewChoice") }
        else { $saved = $savedNow; $rebound = $true }
    }

    if ($rebound) {
        $col = $saved.column
        if (-not $newDef) { throw "Global choice $NewChoice is not readable." }
        $def = @{
            '@odata.type'                = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
            SchemaName                   = $ColumnSchema
            DisplayName                  = New-Label ([string]$col.displayName)
            RequiredLevel                = @{ Value = [string]$col.requiredLevel }
            IsAuditEnabled               = @{ Value = [bool]$col.isAuditEnabled }
            'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions($($newDef.MetadataId))"
        }
        if ([string]$col.description) { $def.Description = New-Label ([string]$col.description) }
        Set-EbEntity $Table
        $r = Invoke-DvCreate -Path "EntityDefinitions(LogicalName='$Table')/Attributes" -Body $def -Column $Column -ExpectedType 'Picklist'
        Wait-AttrVisible $Column
        $script:Changed.Add("$Table.$Column rebound to $NewChoice")
        Write-Host "  $(if ($r -eq 'created') { 'CREATED ' } else { 'found   ' }) $Table.$Column as $ColumnSchema on $NewChoice (display '$($col.displayName)', required $($col.requiredLevel))" -ForegroundColor Green
        Write-Host "  restoring placements and values from the saved state"
        $null = Restore-FromSaved $saved
    }
    $script:Expect.Add(@{ Table = $Table; Column = $Column; Type = 'Picklist' })

    # --- 3. Remove the 10 non-LEDES options from sprk_timekeeperrole ----------------------------------------------------
    Write-Host "3. Options of $OldChoice" -ForegroundColor Cyan
    $oldDef = Get-ChoiceDef $OldChoice
    $present = @(ConvertTo-OptionList $oldDef | Where-Object { $NonLedes -contains $_.Value })
    if ($present.Count -eq 0) {
        $script:Skipped.Add("$OldChoice non-LEDES options"); Write-Host "  skipped  none of the 10 non-LEDES options is present" -ForegroundColor Yellow
    } else {
        # The column must already be off the choice (in WhatIf it is only planned).
        $now = Get-BoundChoice $Table $Column
        $planned = $DryRun -and (-not $now -or $now.ChoiceName -eq $OldChoice)
        if (-not $planned -and $now.ChoiceName -ne $NewChoice) { throw [EscalationException]::new("$Table.$Column is bound to '$($now.ChoiceName)', not $NewChoice; the options are not deleted until it is. Nothing was changed.") }
        # Which columns bind the choice: exactly the four that stay (plus sprk_primaryrole until it is rebound).
        $expectIds = @{}
        foreach ($sb in $StayBound) {
            $a = Get-Attr $sb.Table $sb.Column 'LogicalName,MetadataId'
            if (-not $a) { throw [EscalationException]::new("$($sb.Table).$($sb.Column) does not exist; expected it to stay bound to $OldChoice. Nothing was changed.") }
            $expectIds[([string]$a.MetadataId).ToLower()] = "$($sb.Table).$($sb.Column)"
        }
        $unexpected = @()
        $bindings = @()
        for ($i = 0; $i -lt 12; $i++) {
            $deps = Get-DvOrNull "RetrieveDependenciesForDelete(ObjectId=$($oldDef.MetadataId),ComponentType=9)"
            $unexpected = @(); $bindings = @()
            foreach ($d in @($deps.value)) {
                $id = ([string]$d.dependentcomponentobjectid).ToLower(); $t = [int]$d.dependentcomponenttype
                if ($t -eq 2 -and $expectIds.ContainsKey($id)) { $bindings += $expectIds[$id] }
                elseif ($planned -and $t -eq 2 -and $bound -and $id -eq ([string]$bound.MetadataId).ToLower()) { $bindings += "$Table.$Column (to be rebound)" }
                else { $unexpected += "type $t id $id" }
            }
            if ($unexpected.Count -eq 0 -or $DryRun) { break }
            Start-Sleep -Seconds 5      # the dependency API can lag behind the column drop
        }
        Write-Host "  columns bound to ${OldChoice}: $($bindings -join ', ')"
        if ($unexpected.Count) {
            # Resolve the unexpected ones for the message: attribute ids to table.column where possible.
            throw [EscalationException]::new("Unexpected component(s) depend on ${OldChoice}: $($unexpected -join '; '). Only the four columns that stay bound (and $Table.$Column) may. Nothing was changed.")
        }
        # No row anywhere may hold one of the 10 values in a bound column.
        $holding = @()
        foreach ($sb in $StayBound) {
            $n = Get-RowsHoldingValues $sb.Table $sb.Column $NonLedes
            if ($null -eq $n) { throw [EscalationException]::new("Rows of $($sb.Table) holding a non-LEDES $($sb.Column) value could not be counted; refusing to delete options. Nothing was changed.") }
            Write-Host "  $($sb.Table).$($sb.Column): rows holding one of the 10 non-LEDES values: $n"
            if ($n -gt 0) { $holding += "$($sb.Table).$($sb.Column) ($n)" }
            $dv = (Get-DvOrNull "EntityDefinitions(LogicalName='$($sb.Table)')/Attributes(LogicalName='$($sb.Column)')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=DefaultFormValue").DefaultFormValue
            if ($null -ne $dv -and $NonLedes -contains [int]$dv) { $holding += "$($sb.Table).$($sb.Column) default value $dv" }
        }
        if (-not $planned) { Write-Host "  ${Table}.${Column} is bound to $NewChoice (its own values are not affected)" }
        if ($holding.Count) { throw [EscalationException]::new("A non-LEDES value is still in use: $($holding -join '; '). Remap those rows first. Nothing was changed.") }
        foreach ($o in $present) {
            if ($DryRun) { $script:Changed.Add("$OldChoice option $($o.Value) $($o.Label) deleted"); Write-Host "  WOULD DELETE option $($o.Value) $($o.Label)" -ForegroundColor Cyan; continue }
            try { Invoke-WhilePropagating { Invoke-Dv -Path 'DeleteOptionValue' -Method POST -Body @{ OptionSetName = $OldChoice; Value = $o.Value } | Out-Null } | Out-Null }
            catch { if ($_.Exception.Message -notmatch '(?i)does not exist|could not find|0x80048401|0x80040217') { throw } }
            $script:Changed.Add("$OldChoice option $($o.Value) $($o.Label) deleted"); Write-Host "  DELETED  option $($o.Value) $($o.Label)" -ForegroundColor Green
        }
    }

    # --- Publish (only when something changed; entity- and choice-scoped, never PublishAllXml) -------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) { Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan }
    elseif (-not $wrote) { Write-Host "  nothing changed - no publish" -ForegroundColor Yellow }
    else {
        $ents = (@($Table) + @($StayBound | ForEach-Object { $_.Table }) | Select-Object -Unique | ForEach-Object { "<entity>$_</entity>" }) -join ''
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities>$ents</entities><optionsets><optionset>$OldChoice</optionset><optionset>$NewChoice</optionset></optionsets></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (settled metadata and data reads) ----------------------------------------------------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()
        # sprk_timekeeperrole: exactly the 20 LEDES options with their codes.
        $o = ConvertTo-OptionList (Get-ChoiceDef $OldChoice)
        for ($i = 0; $i -lt 6 -and $o.Count -ne $Ledes.Count; $i++) { Start-Sleep -Seconds 5; $o = ConvertTo-OptionList (Get-ChoiceDef $OldChoice) }
        Write-Host "  ${OldChoice}: $($o.Count) options"
        foreach ($x in $o) {
            if (-not $Ledes.ContainsKey($x.Value)) { $bad.Add("$OldChoice holds non-LEDES option $($x.Value) $($x.Label)"); continue }
            if ($x.Label -cne $Ledes[$x.Value][0] -or $x.External -cne $Ledes[$x.Value][1]) { $bad.Add("$OldChoice option $($x.Value) is '$($x.Label)' / '$($x.External)', expected '$($Ledes[$x.Value][0])' / '$($Ledes[$x.Value][1])'") }
        }
        foreach ($k in $Ledes.Keys) { if (-not ($o | Where-Object { $_.Value -eq [int]$k })) { $bad.Add("$OldChoice lacks LEDES option $k $($Ledes[$k][0])") } }
        Write-Host ("    " + (($o | ForEach-Object { "$($_.Value) $($_.Label) [$($_.External)]" }) -join '; '))
        # sprk_userprofilerole: the 14 options; the column is bound to it.
        $nd = ConvertTo-OptionList (Get-ChoiceDef $NewChoice)
        Write-Host "  ${NewChoice}: $($nd.Count) options - $(Format-OptionList $nd)"
        foreach ($k in $UserProfileOptions.Keys) { $h = $nd | Where-Object { $_.Value -eq [int]$k }; if (-not $h -or $h.Label -cne $UserProfileOptions[$k]) { $bad.Add("$NewChoice option $k is not '$($UserProfileOptions[$k])'") } }
        if ($nd.Count -ne $UserProfileOptions.Count) { Write-Host "  note     $NewChoice holds $($nd.Count) options, the original list has $($UserProfileOptions.Count)" -ForegroundColor DarkYellow }
        $b = Get-BoundChoice $Table $Column
        if (-not $b) { $bad.Add("$Table.$Column missing") }
        else {
            Write-Host "  $Table.$Column`: bound to '$($b.ChoiceName)' (global: $($b.IsGlobal))"
            if ($b.ChoiceName -ne $NewChoice -or -not $b.IsGlobal) { $bad.Add("$Table.$Column is bound to '$($b.ChoiceName)', expected the global $NewChoice") }
            $pk = Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=SchemaName,RequiredLevel&`$expand=GlobalOptionSet(`$select=Name,Options)"
            Write-Host "    schema $($pk.SchemaName), required $($pk.RequiredLevel.Value), $(@($pk.GlobalOptionSet.Options).Count) options on the column"
            if ($pk.SchemaName -cne $ColumnSchema) { $bad.Add("$Table.$Column schema name is $($pk.SchemaName)") }
        }
        # The four columns stay bound to sprk_timekeeperrole.
        foreach ($sb in $StayBound) {
            $x = Get-BoundChoice $sb.Table $sb.Column
            if (-not $x -or $x.ChoiceName -ne $OldChoice) { $bad.Add("$($sb.Table).$($sb.Column) is not bound to $OldChoice") }
        }
        # Values: every saved row reads back; forms hold the column at the same position.
        $sv = Read-SavedState
        if ($sv) {
            foreach ($sr in @($sv.rows)) {
                $curRow = Invoke-Dv "sprk_userprofiles($($sr.id))?`$select=sprk_primaryrole" -ExtraHeaders $FormattedHeader
                $lbl = [string]$curRow.'sprk_primaryrole@OData.Community.Display.V1.FormattedValue'
                Write-Host "  row '$($sr.name)': $($curRow.sprk_primaryrole) reads '$lbl' (saved $($sr.value) '$($sr.label)')"
                if ($null -eq $curRow.sprk_primaryrole -or [int]$curRow.sprk_primaryrole -ne [int]$sr.value -or $lbl -ne $sr.label) { $bad.Add("row '$($sr.name)' reads $($curRow.sprk_primaryrole) '$lbl', saved $($sr.value) '$($sr.label)'") }
            }
            foreach ($sf in @($sv.forms)) {
                $cur = Get-DvOrNull "systemforms($($sf.formId))?`$select=formxml"
                $ids = @(Get-CellIdList ([string]$cur.formxml))
                $same = (($ids -join ',') -eq (@($sf.cellIds) -join ','))
                $has = @(Find-ColumnCells ([string]$cur.formxml)).Count -eq 1
                Write-Host "  form '$($sf.name)': $Column $(if ($has) { 'present' } else { 'MISSING' }), cell order $(if ($same) { 'identical to before' } else { 'DIFFERENT' }), formxml $(if ([string]$cur.formxml -ceq [string]$sf.formXml) { 'identical to before' } else { 'differs from before' })"
                if (-not $has) { $bad.Add("form '$($sf.name)' lacks $Column") }
                if (-not $same) { $bad.Add("form '$($sf.name)' cell order changed") }
            }
        } else {
            foreach ($r in @(Get-ProfileRows)) { Write-Host "  row '$($r.Name)': $($r.Value) '$($r.Label)'" }
        }
        if ($bad.Count) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all checks passed" -ForegroundColor Green
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
    Write-Host "The script is re-runnable; if the column was dropped, the re-run recreates and restores it from $SavedFile." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join '; '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
