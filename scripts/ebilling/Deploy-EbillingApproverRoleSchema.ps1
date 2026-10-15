<#
.SYNOPSIS
    eBilling R1 task 024 - brings the billing approver role choice (D-33, FR-14) into the SpaarkeMaster solution and adds the
    firm billing-contact lookup on sprk_organization (D-45, FR-39).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every item
    as skipped and makes no write (no publish either). It never relabels, renumbers, removes or retypes an existing option,
    option set or column, and never writes record data.

    Items (tasks/024-approver-role-billing-contact.poml, spec FR-14 / D-33 / D-45):
      0  Read and print the evidence: the global choice sprk_billingapproverrole (every value and label), the column on
         sprk_matter / sprk_project / sprk_workassignment (type, choice it is bound to), the count of rows holding a value on each
         table, and the SpaarkeMaster membership of the choice and of each table.
      1  Global choice sprk_billingapproverrole: created with the seven D-33 values only when absent; if present it is left
         untouched and checked against the D-33 list (value for value, label compared ignoring case).
      2  Column sprk_billingapproverrole on sprk_matter, sprk_project and sprk_workassignment: a MULTI-SELECT bound to the global
         choice, created only when absent. See "Matter" below for the one case that is not additive.
      3  sprk_organization.sprk_billingcontact: lookup to contact (display "Billing Contact", delete = RemoveLink, all other
         cascades NoCascade), created only when absent.
      4  SpaarkeMaster membership: the choice (component type 9) and, for a table that is NOT a whole-table component
         (rootcomponentbehavior 0), each column (type 2). Components created in 1-3 carry the MSCRM.SolutionUniqueName header.

    Matter (DEF-011, GitHub #1941). D-33 / FR-14 call the role field multi-select on Matter, Project and Work Assignment. In dev
    Project and Work Assignment are multi-select but sprk_matter.sprk_billingapproverrole is a SINGLE-select picklist. A column
    type cannot change in place, so making Matter match means dropping the column and creating it again: DESTRUCTIVE.
      - Without -ReplaceMatterApproverRole the script changes nothing on Matter. It prints the replacement evidence (rows holding
        a value, dependencies, references in forms / views / charts / business rules) and reports the column as DEFERRED.
      - With -ReplaceMatterApproverRole it runs the same read-only pre-checks again and REFUSES (exit 2, nothing changed) when
        any Matter row holds a value, when the dependency API lists anything other than the platform's own generated
        FormFillFieldOptOut skill config for the column, or when a form / view / chart / business rule / workflow of
        sprk_matter references the column (so no form or view has to be re-added by hand: if one ever does, the script stops
        and lists it). Only then does it delete the single-select column, wait until it is gone and create the multi-select on
        the same global choice. Owner OK is required before using the switch (CLAUDE.md project section 5: deleting a column).
        -WhatIf with the switch runs the pre-checks and writes nothing.

    ADR-002: no plugin, business rule or flow defaults the field; an empty value is resolved by the BFF from the settings
    record (D-54 / D-59, task 103). No form, view, script or default value is added here (FR-14); task 029 exports the
    forms and views.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create refused
    with "already exists" counts as skipped after a type check on a settled read; customization / publish locks and transient
    faults are retried with backoff.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER ReplaceMatterApproverRole
    DESTRUCTIVE, needs the owner's OK. Drops the single-select sprk_matter.sprk_billingapproverrole and recreates it as a
    multi-select on the same global choice, refusing when any Matter row holds a value (see Matter above).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingApproverRoleSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingApproverRoleSchema.ps1

.NOTES
    Project: ebilling-r1, task 024. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Patterns: Deploy-EbillingApprovalSchema.ps1 (021), Deploy-ChartDefinitionEntity.ps1 (global choice),
    Create-ScopeCapabilityFields.ps1 (multi-select), Deploy-AccessEventEntity.ps1 (AddSolutionComponent).
    Exit codes: 0 = done (created / skipped / deferred), 1 = error, 2 = escalation (conflicting existing item or a refused guard).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$ReplaceMatterApproverRole
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$ChoiceName = 'sprk_billingapproverrole'
$Column = 'sprk_billingapproverrole'
$RoleTables = 'sprk_matter', 'sprk_project', 'sprk_workassignment'
$OrgTable = 'sprk_organization'
$ContactColumn = 'sprk_billingcontact'
# D-33 values. The label of 100000005 is "Assigned to External" in dev (lower-case "to"); created with the dev spelling so a
# fresh environment matches what task 029 exports. Existing labels are compared ignoring case and never edited.
$ChoiceOptions = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$ChoiceOptions.Add(100000000, 'Assigned Attorney 1')
$ChoiceOptions.Add(100000001, 'Assigned Attorney 2')
$ChoiceOptions.Add(100000002, 'Assigned Paralegal 1')
$ChoiceOptions.Add(100000003, 'Assigned Paralegal 2')
$ChoiceOptions.Add(100000004, 'Assigned To Internal')
$ChoiceOptions.Add(100000005, 'Assigned to External')
$ChoiceOptions.Add(100000006, 'Assigned To')
$RoleDescription = 'Which assignment fields hold the approvers of this record (D-33). Several may be chosen. Empty means the default from the eBilling settings record (D-54 / D-59).'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Deferred = $State.Deferred

# ---------------------------------------------------------------------------------------------------------------
# Script-specific helpers
# ---------------------------------------------------------------------------------------------------------------

function Get-Choice { return Get-DvOrNull "GlobalOptionSetDefinitions(Name='$ChoiceName')" }

function ConvertTo-OptionMap($choice) {
    $m = [System.Collections.Generic.SortedDictionary[int, string]]::new()
    foreach ($o in ($choice.Options | Sort-Object Value)) { $m[[int]$o.Value] = [string]$o.Label.UserLocalizedLabel.Label }
    return $m
}

function Format-OptionMap($map) { return (($map.Keys | ForEach-Object { "$_=$($map[$_])" }) -join ', ') }

# Kind of an existing column: 'multi' (MultiSelectPicklist), 'single' (Picklist) or the raw type, with the choice it uses.
function Get-RoleColumnInfo([string]$Table) {
    $a = Get-Attr $Table $Column 'LogicalName,AttributeType,AttributeTypeName,MetadataId'
    if (-not $a) { return $null }
    $kind = switch ($a.AttributeTypeName.Value) { 'MultiSelectPicklistType' { 'multi' } 'PicklistType' { 'single' } default { [string]$a.AttributeTypeName.Value } }
    $cast = if ($kind -eq 'multi') { 'MultiSelectPicklistAttributeMetadata' } elseif ($kind -eq 'single') { 'PicklistAttributeMetadata' } else { $null }
    $set = $null
    if ($cast) {
        $x = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.$cast`?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)" -Retries 2
        if ($x -and $x.OptionSet) { $set = $x.OptionSet }
    }
    return [pscustomobject]@{ Kind = $kind; MetadataId = $a.MetadataId; ChoiceName = $set.Name; IsGlobal = $set.IsGlobal }
}

function Get-RowsWithRole([string]$Table) {
    $set = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 4
    if (-not $set) { return $null }
    return Get-RowCountExact $set.EntitySetName "$($Table)id" "$Column ne null"
}

function Get-RowsTotal([string]$Table) {
    $set = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 4
    if (-not $set) { return $null }
    return Get-RowCountExact $set.EntitySetName "$($Table)id"
}

function New-RoleColumnDef([string]$ChoiceId) {
    return @{
        '@odata.type'                = 'Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata'
        SchemaName                   = 'sprk_BillingApproverRole'
        DisplayName                  = New-Label 'Billing Approver Role'
        Description                  = New-Label $RoleDescription
        RequiredLevel                = @{ Value = 'None' }
        'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions($ChoiceId)"
    }
}

# Everything that would be lost or break if sprk_matter.sprk_billingapproverrole were dropped. Read-only.
function Get-MatterReplacementEvidence($info) {
    $ev = [ordered]@{ Total = $null; WithValue = $null; Dependencies = @(); UnexpectedDependencies = @(); SkillConfigIds = @(); References = @() }
    $ev.Total = Get-RowsTotal 'sprk_matter'
    $ev.WithValue = Get-RowsWithRole 'sprk_matter'
    # Dependency API: what refuses or breaks a delete of the column (component type 2 = attribute).
    $deps = Get-DvOrNull "RetrieveDependenciesForDelete(ObjectId=$($info.MetadataId),ComponentType=2)"
    $list = @(); $bad = @(); $skills = @()
    foreach ($d in @($deps.value)) {
        $desc = "type $($d.dependentcomponenttype) id $($d.dependentcomponentobjectid)"
        $ok = $false
        if ([int]$d.dependentcomponenttype -eq 10314) {
            # aiskillconfig: the platform's own generated "FormFillFieldOptOut" config for this column. It blocks the column delete
            # (0x8004f01f), so the replacement deletes it first.
            $sk = Get-DvOrNull "aiskillconfigs($($d.dependentcomponentobjectid))?`$select=uniquename,aiskill"
            if ($sk) {
                $desc = "aiskillconfig $($sk.uniquename)"
                $ok = ($sk.uniquename -eq "ais_FormFillFieldOptOut_sprk_matter_$Column")
                if ($ok) { $skills += "$($d.dependentcomponentobjectid)" }
            }
        }
        $list += $desc
        if (-not $ok) { $bad += $desc }
    }
    $ev.Dependencies = $list
    $ev.UnexpectedDependencies = $bad
    $ev.SkillConfigIds = $skills
    # Direct text scan of the places a column is placed on: forms, views, personal views, charts, workflows / business rules.
    $refs = @()
    foreach ($f in @((Get-DvOrNull "systemforms?`$select=name,formxml&`$filter=objecttypecode eq 'sprk_matter'").value)) { if ("$($f.formxml)" -match $Column) { $refs += "form '$($f.name)'" } }
    foreach ($v in @((Get-DvOrNull "savedqueries?`$select=name,fetchxml,layoutxml&`$filter=returnedtypecode eq 'sprk_matter'").value)) { if ("$($v.fetchxml)$($v.layoutxml)" -match $Column) { $refs += "view '$($v.name)'" } }
    foreach ($v in @((Get-DvOrNull "userqueries?`$select=name,fetchxml,layoutxml&`$filter=returnedtypecode eq 'sprk_matter'").value)) { if ("$($v.fetchxml)$($v.layoutxml)" -match $Column) { $refs += "personal view '$($v.name)'" } }
    foreach ($c in @((Get-DvOrNull "savedqueryvisualizations?`$select=name,datadescription,presentationdescription&`$filter=primaryentitytypecode eq 'sprk_matter'").value)) { if ("$($c.datadescription)$($c.presentationdescription)" -match $Column) { $refs += "chart '$($c.name)'" } }
    foreach ($w in @((Get-DvOrNull "workflows?`$select=name,category,xaml,clientdata&`$filter=primaryentity eq 'sprk_matter'").value)) { if ("$($w.xaml)$($w.clientdata)" -match $Column) { $refs += "workflow / business rule '$($w.name)'" } }
    $ev.References = $refs
    return $ev
}

function Write-MatterEvidence($ev) {
    Write-Host "  Matter replacement evidence (DEF-011):" -ForegroundColor Cyan
    Write-Host "    sprk_matter rows: total $($ev.Total), holding a value in $Column`: $($ev.WithValue)"
    Write-Host "    dependencies (RetrieveDependenciesForDelete): $(@($ev.Dependencies).Count)$(if (@($ev.Dependencies).Count) { ' - ' + (@($ev.Dependencies) -join '; ') })"
    Write-Host "    references in forms / views / charts / workflows: $(@($ev.References).Count)$(if (@($ev.References).Count) { ' - ' + (@($ev.References) -join '; ') })"
}

# Runs the guards and (only with the switch, outside -WhatIf) drops the single-select Matter column and recreates it.
function Invoke-MatterReplacement($info, [string]$ChoiceId) {
    $ev = Get-MatterReplacementEvidence $info
    Write-MatterEvidence $ev
    if (-not $ReplaceMatterApproverRole) {
        $script:Deferred.Add("sprk_matter.$Column is single-select (DEF-011): making it multi-select drops and recreates the column - owner OK needed, then re-run with -ReplaceMatterApproverRole")
        Write-Host "  DEFERRED $Column on sprk_matter (single-select; replacement needs the owner's OK, switch -ReplaceMatterApproverRole not given)" -ForegroundColor DarkYellow
        return
    }
    # --- guards: refuse, change nothing ---
    if ($null -eq $ev.WithValue) { throw [EscalationException]::new("sprk_matter rows holding a value in $Column could not be counted; refusing to drop the column. Nothing was changed.") }
    if ($ev.WithValue -gt 0) { throw [EscalationException]::new("$($ev.WithValue) sprk_matter row(s) hold a value in $Column; dropping the column would lose them. Nothing was changed.") }
    if (@($ev.UnexpectedDependencies).Count -gt 0) { throw [EscalationException]::new("sprk_matter.$Column has dependencies other than the platform's generated skill config: $(@($ev.UnexpectedDependencies) -join '; '). Nothing was changed.") }
    if (@($ev.References).Count -gt 0) { throw [EscalationException]::new("sprk_matter.$Column is placed on: $(@($ev.References) -join '; '). Dropping it would remove it from those; re-add them by hand after the replacement or extend this script. Nothing was changed.") }
    if ($info.ChoiceName -ne $ChoiceName -or -not $info.IsGlobal) { throw [EscalationException]::new("sprk_matter.$Column uses '$($info.ChoiceName)' (global: $($info.IsGlobal)), not the global choice $ChoiceName. Owner decision needed. Nothing was changed.") }
    if ($DryRun) {
        $script:Changed.Add("sprk_matter.$Column replaced by multi-select")
        Write-Host "  WOULD DROP sprk_matter.$Column (single-select, 0 rows hold a value) and CREATE it as a multi-select on $ChoiceName" -ForegroundColor Cyan
        return
    }
    Write-Host "  DROPPING sprk_matter.$Column (single-select; 0 of $($ev.Total) rows hold a value)" -ForegroundColor Magenta
    foreach ($id in @($ev.SkillConfigIds)) {
        Write-Host "  DELETING generated aiskillconfig $id (FormFillFieldOptOut for the old column; it blocks the column delete)" -ForegroundColor Magenta
        Invoke-Dv -Path "aiskillconfigs($id)" -Method DELETE | Out-Null
    }
    Invoke-Dv -Path "EntityDefinitions(LogicalName='sprk_matter')/Attributes($($info.MetadataId))" -Method DELETE | Out-Null
    $gone = $false
    for ($i = 0; $i -lt 24 -and -not $gone; $i++) {
        if (-not (Get-Attr 'sprk_matter' $Column)) { $gone = $true } else { Start-Sleep -Seconds 5 }
    }
    if (-not $gone) { throw "sprk_matter.$Column was deleted but still reads as present after 120 s. Re-run the script (without the switch it will report the state)." }
    Set-EbEntity 'sprk_matter'
    Add-Column -Column $Column -Definition (New-RoleColumnDef $ChoiceId) -ExpectedType 'Virtual'
    $script:Changed.Add("sprk_matter.$Column replaced by multi-select")
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 024 - billing approver role and firm billing contact" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })$(if ($ReplaceMatterApproverRole) { '   -ReplaceMatterApproverRole' })"
Write-Host ""

try {
    foreach ($ref in @('contact', 'sprk_organization') + $RoleTables) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) { throw "$ref does not exist in $EnvironmentUrl." }
    }
    $sol = Get-DvOrNull "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'"
    if (-not $sol -or @($sol.value).Count -eq 0) { throw "Solution $SolutionUniqueName does not exist in $EnvironmentUrl." }
    $SolutionId = @($sol.value)[0].solutionid

    # --- 0. Read the current state (evidence; also the baseline the verification compares against) ---------------------
    Write-Host "0. Current state" -ForegroundColor Cyan
    $choice0 = Get-Choice
    $Before = @{ Options = $null; Rows = @{} }
    if ($choice0) {
        $Before.Options = ConvertTo-OptionMap $choice0
        Write-Host "  global choice $ChoiceName ($($choice0.MetadataId)): $(Format-OptionMap $Before.Options)"
    } else { Write-Host "  global choice ${ChoiceName}: absent" }
    foreach ($t in $RoleTables) {
        $info = Get-RoleColumnInfo $t
        $n = if ($info) { Get-RowsWithRole $t } else { $null }
        $Before.Rows[$t] = $n
        Write-Host "  $t.$Column`: $(if ($info) { "$($info.Kind)-select, choice '$($info.ChoiceName)' (global: $($info.IsGlobal)), rows holding a value: $n" } else { 'absent' })"
    }

    # --- 1. Global choice ---------------------------------------------------------------------------------------------
    Write-Host "1. Global choice $ChoiceName" -ForegroundColor Cyan
    $choice = $choice0
    if ($choice) {
        $have = ConvertTo-OptionMap $choice
        $diff = @()
        foreach ($k in $ChoiceOptions.Keys) {
            if (-not $have.ContainsKey([int]$k)) { $diff += "missing $k=$($ChoiceOptions[$k])" }
            elseif ($have[[int]$k] -ne $ChoiceOptions[$k]) {
                if ($have[[int]$k] -ieq $ChoiceOptions[$k]) { Write-Host "  note     $k label is '$($have[[int]$k])' (D-33 text spells it '$($ChoiceOptions[$k])'; casing only, left as is)" -ForegroundColor DarkYellow }
                else { $diff += "$k is '$($have[[int]$k])', expected '$($ChoiceOptions[$k])'" }
            }
        }
        foreach ($k in $have.Keys) { if (-not $ChoiceOptions.ContainsKey([int]$k)) { $diff += "extra $k=$($have[[int]$k])" } }
        if ($diff.Count -gt 0) { throw [EscalationException]::new("Global choice $ChoiceName differs from the D-33 list: $($diff -join '; '). Observed: $(Format-OptionMap $have). Not changed.") }
        $script:Skipped.Add("optionset $ChoiceName"); Write-Host "  skipped  $ChoiceName (exists, $($have.Count) options match D-33)" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Created.Add("optionset $ChoiceName"); Write-Host "  WOULD CREATE global choice $ChoiceName ($($ChoiceOptions.Count) options)" -ForegroundColor Cyan
    } else {
        $options = @(foreach ($k in $ChoiceOptions.Keys) { @{ Value = [int]$k; Label = New-Label $ChoiceOptions[$k] } })
        $def = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            Name           = $ChoiceName
            DisplayName    = New-Label 'Billing Approver Role'
            IsGlobal       = $true
            OptionSetType  = 'Picklist'
            Options        = $options
        }
        try { Invoke-Dv -Path 'GlobalOptionSetDefinitions' -Method POST -Body $def -ExtraHeaders $State.SolutionHeader | Out-Null; $created = $true }
        catch { if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }; $created = $false }
        for ($i = 0; $i -lt 12 -and -not $choice; $i++) { $choice = Get-Choice; if (-not $choice) { Start-Sleep -Seconds 5 } }
        if (-not $choice) { throw "Global choice $ChoiceName was created but is not readable after 60 s." }
        if ($created) { $script:Created.Add("optionset $ChoiceName"); Write-Host "  CREATED  global choice $ChoiceName ($($ChoiceOptions.Count) options)" -ForegroundColor Green }
        else { $script:Skipped.Add("optionset $ChoiceName"); Write-Host "  skipped  $ChoiceName (exists, found via create refusal)" -ForegroundColor Yellow }
    }
    $ChoiceId = if ($choice) { $choice.MetadataId } else { '00000000-0000-0000-0000-000000000000' }

    # --- 2. Multi-select columns --------------------------------------------------------------------------------------
    Write-Host "2. Column $Column on Matter, Project, Work Assignment" -ForegroundColor Cyan
    foreach ($t in $RoleTables) {
        Set-EbEntity $t
        $info = Get-RoleColumnInfo $t
        if ($info -and $info.Kind -eq 'single') {
            if ($t -ne 'sprk_matter') { throw [EscalationException]::new("$t.$Column is a single-select picklist; only sprk_matter has a replacement path (DEF-011). Owner decision needed.") }
            Invoke-MatterReplacement $info $ChoiceId
            continue
        }
        if ($info -and $info.Kind -ne 'multi') { throw [EscalationException]::new("$t.$Column exists as '$($info.Kind)', not a multi-select. Owner decision needed.") }
        if ($info -and ($info.ChoiceName -ne $ChoiceName -or -not $info.IsGlobal)) {
            throw [EscalationException]::new("$t.$Column is a multi-select on '$($info.ChoiceName)' (global: $($info.IsGlobal)), not the global choice $ChoiceName. Owner decision needed.")
        }
        Add-Column -Column $Column -Definition (New-RoleColumnDef $ChoiceId) -ExpectedType 'Virtual'
    }

    # --- 3. Firm billing contact --------------------------------------------------------------------------------------
    Write-Host "3. $OrgTable.$ContactColumn (firm billing contact, D-45)" -ForegroundColor Cyan
    Set-EbEntity $OrgTable
    $contactLookups = @((Get-DvOrNull "EntityDefinitions(LogicalName='$OrgTable')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets").value | Where-Object { $_.Targets -contains 'contact' })
    $other = @($contactLookups | Where-Object { $_.LogicalName -notin 'sprk_primarycontact1', 'sprk_primarycontact2', $ContactColumn })
    if ($other.Count -gt 0) {
        throw [EscalationException]::new("$OrgTable already has contact lookup(s) other than sprk_primarycontact1 / 2: $(($other | ForEach-Object { $_.LogicalName }) -join ', '). Ask whether to reuse one instead of adding $ContactColumn. Not changed.")
    }
    Add-Lookup -Column $ContactColumn -SchemaName 'sprk_BillingContact' -RelSchema 'sprk_organization_billingcontact_contact' -Target 'contact' `
        -Display 'Billing Contact' -Description 'The firm''s billing contact: receives firm invoice notices (rejection, return, approval with adjustments) in addition to the submitter (D-45).' `
        -DeleteBehavior 'RemoveLink'

    # --- 4. SpaarkeMaster membership ----------------------------------------------------------------------------------
    Write-Host "4. $SolutionUniqueName membership" -ForegroundColor Cyan
    function Test-InSolution([string]$ObjectId, [int]$Type) {
        $r = Get-DvOrNull "solutioncomponents?`$select=componenttype,rootcomponentbehavior&`$filter=_solutionid_value eq $SolutionId and objectid eq $ObjectId and componenttype eq $Type"
        return @($r.value)
    }
    function Add-ToSolution([string]$ObjectId, [int]$Type, [string]$Label) {
        if ($DryRun) { $script:Changed.Add("solution $Label"); Write-Host "  WOULD ADD $Label to $SolutionUniqueName (type $Type)" -ForegroundColor Cyan; return }
        Invoke-Dv -Path 'AddSolutionComponent' -Method POST -Body @{ ComponentId = $ObjectId; ComponentType = $Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        $ok = $false
        for ($i = 0; $i -lt 12 -and -not $ok; $i++) { if ((Test-InSolution $ObjectId $Type).Count -gt 0) { $ok = $true } else { Start-Sleep -Seconds 5 } }
        if (-not $ok) { throw "$Label was added to $SolutionUniqueName but is not listed as a component after 60 s." }
        $script:Changed.Add("solution $Label"); Write-Host "  ADDED    $Label to $SolutionUniqueName (type $Type)" -ForegroundColor Green
    }
    if ($choice) {
        if ((Test-InSolution $choice.MetadataId 9).Count -gt 0) { $script:Skipped.Add("solution $ChoiceName"); Write-Host "  skipped  choice $ChoiceName (already a component)" -ForegroundColor Yellow }
        else { Add-ToSolution $choice.MetadataId 9 "global choice $ChoiceName" }
    }
    $colsPerTable = [ordered]@{ sprk_matter = @($Column); sprk_project = @($Column); sprk_workassignment = @($Column); sprk_organization = @($ContactColumn) }
    foreach ($t in $colsPerTable.Keys) {
        $td = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=MetadataId" -Retries 2
        $whole = @(Test-InSolution $td.MetadataId 1 | Where-Object { [int]$_.rootcomponentbehavior -eq 0 }).Count -gt 0
        if ($whole) { $script:Skipped.Add("solution $t whole table"); Write-Host "  skipped  $t is a whole-table component (its columns are included)" -ForegroundColor Yellow; continue }
        foreach ($c in $colsPerTable[$t]) {
            $a = Get-Attr $t $c 'LogicalName,MetadataId'
            if (-not $a) { if ($DryRun) { continue }; if ($t -eq 'sprk_matter' -and $script:Deferred.Count -gt 0) { continue }; throw "$t.$c is missing; cannot add it to $SolutionUniqueName." }
            if ((Test-InSolution $a.MetadataId 2).Count -gt 0) { $script:Skipped.Add("solution $t.$c"); Write-Host "  skipped  $t.$c (already a component)" -ForegroundColor Yellow }
            else { Add-ToSolution $a.MetadataId 2 "$t.$c" }
        }
    }

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_organization</entity></entities><optionsets><optionset>$ChoiceName</optionset></optionsets></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns; existing values unchanged) -----------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()
        foreach ($e in $script:Expect) {
            $a = $null
            for ($i = 0; $i -lt 12 -and -not $a; $i++) {
                $a = Get-Attr $e.Table $e.Column
                if (-not $a) { Start-Sleep -Seconds 5 }
            }
            if (-not $a) { $bad.Add("$($e.Table).$($e.Column) missing"); continue }
            if ($a.AttributeType -ne $e.Type) { $bad.Add("$($e.Table).$($e.Column) is $($a.AttributeType), expected $($e.Type)"); continue }
            if ($e.Type -eq 'Lookup') {
                $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
            }
        }
        # Choice: values and labels identical to the baseline (or to D-33 when this run created it).
        $after = Get-Choice
        if (-not $after) { $bad.Add("global choice $ChoiceName missing") }
        else {
            $now = ConvertTo-OptionMap $after
            Write-Host "  choice $ChoiceName`: $(Format-OptionMap $now)"
            if ($Before.Options -and (Format-OptionMap $now) -ne (Format-OptionMap $Before.Options)) { $bad.Add("choice options changed: before [$(Format-OptionMap $Before.Options)] after [$(Format-OptionMap $now)]") }
        }
        # Columns: multi-select on the global choice, and the row counts holding a value unchanged.
        foreach ($t in $RoleTables) {
            $info = Get-RoleColumnInfo $t
            if (-not $info) { $bad.Add("$t.$Column missing"); continue }
            $n = Get-RowsWithRole $t
            Write-Host "  $t.$Column`: $($info.Kind)-select on '$($info.ChoiceName)' (global: $($info.IsGlobal)); rows holding a value: before $($Before.Rows[$t]), after $n"
            if ($info.Kind -ne 'multi' -and -not ($t -eq 'sprk_matter' -and $script:Deferred.Count -gt 0)) { $bad.Add("$t.$Column is $($info.Kind)-select, expected multi") }
            if ($info.ChoiceName -ne $ChoiceName) { $bad.Add("$t.$Column uses '$($info.ChoiceName)', expected $ChoiceName") }
            if ($null -ne $Before.Rows[$t] -and $n -ne $Before.Rows[$t]) { $bad.Add("$t rows holding a value changed: before $($Before.Rows[$t]), after $n") }
        }
        # Membership.
        if ($after -and (Test-InSolution $after.MetadataId 9).Count -eq 0) { $bad.Add("global choice not in $SolutionUniqueName") }
        foreach ($t in $colsPerTable.Keys) {
            $td = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=MetadataId" -Retries 2
            $tc = @(Test-InSolution $td.MetadataId 1)
            $whole = @($tc | Where-Object { [int]$_.rootcomponentbehavior -eq 0 }).Count -gt 0
            Write-Host "  $t in ${SolutionUniqueName}: $(if ($whole) { 'whole table' } elseif ($tc.Count) { 'table with listed subcomponents' } else { 'columns only' })"
            if (-not $whole) {
                foreach ($c in $colsPerTable[$t]) {
                    $a = Get-Attr $t $c 'MetadataId'
                    if ($a -and (Test-InSolution $a.MetadataId 2).Count -eq 0) { $bad.Add("$t.$c not in $SolutionUniqueName") }
                }
            }
        }
        $cl = Get-DvOrNull "EntityDefinitions(LogicalName='$OrgTable')/ManyToOneRelationships?`$select=SchemaName,CascadeConfiguration&`$filter=ReferencingAttribute eq '$ContactColumn'"
        $rel = @($cl.value)[0]
        if (-not $rel) { $bad.Add("no many-to-one relationship for $ContactColumn") }
        else {
            Write-Host ("  {0}: {1}  delete {2}, assign {3}, share {4}" -f $ContactColumn, $rel.SchemaName, $rel.CascadeConfiguration.Delete, $rel.CascadeConfiguration.Assign, $rel.CascadeConfiguration.Share)
            if ($rel.CascadeConfiguration.Delete -ne 'RemoveLink') { $bad.Add("$ContactColumn delete behaviour is $($rel.CascadeConfiguration.Delete), expected RemoveLink") }
        }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type and target; existing values unchanged" -ForegroundColor Green
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
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
