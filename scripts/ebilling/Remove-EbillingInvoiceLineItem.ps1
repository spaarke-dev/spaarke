<#
.SYNOPSIS
    eBilling R1 task 011 - retires sprk_invoicelineitem (D-18, FR-04): dependency / usage inventory, and the guarded delete.

.DESCRIPTION
    sprk_billingevent is the Invoice Line (task 010); sprk_invoicelineitem is the superseded table.

    WITHOUT -Delete this script is READ-ONLY. It prints the inventory (row count, forms, views, relationships, app modules,
    AI skill configs, workflows, site maps, charts, plug-in steps, roles, RetrieveDependenciesForDelete) and a verdict
    (READY or BLOCKED with the reasons). It writes nothing.

    WITH -Delete -OwnerApproval '<date and who>' it removes the table. DESTRUCTIVE: project CLAUDE.md section 5 requires the
    owner's explicit OK first, quoted in the task notes. Before it writes anything it re-runs the inventory and REFUSES
    (exit 2, nothing changed) if
      - the table holds any row (unless -DeleteRows, below), or
      - a sprk_timekeeper row holds a value in the sprk_invoicelineitem lookup, or
      - a system form other than the three known ones mentions the table, or
      - RetrieveDependenciesForDelete lists a dependent that is not one of the expected kinds
        (the two forms with a subgrid, the timekeeper lookup relationship, the table's AI skill configs, an app module), or
      - a view / workflow / site map / chart / personal view of another table mentions the table.

    Removal order (project constraint): form subgrids and navigation entries on the sprk_billingevent and sprk_invoice forms
    -> publish those two tables -> the sprk_invoicelineitem lookup on sprk_timekeeper (relationship delete; the column goes with
    it) -> the AI skill config opt-outs -> the app module registrations -> re-run RetrieveDependenciesForDelete (must be empty;
    refuses otherwise) -> [rows, only with -DeleteRows] -> the table's own views -> the table. Then it verifies: the entity reads
    404, and the main forms of sprk_billingevent, sprk_invoice and sprk_timekeeper load and no longer mention the table.

    -DeleteRows (requires -Delete) deletes the table's rows too, but ONLY if every row id is in the list of the four rows found
    on 2026-10-15 (test data: two "Line item #1" rows on test invoices, "Attorney Fees" and "Line Item 1" on INV-002 / INV-005).
    A fifth row, or a changed id set, refuses. All columns of the rows are first written to -BackupPath. It exists because the
    owner may decide the rows are disposable; it is never implied by -Delete.

    Idempotent: if the table is already gone it reports so and exits 0.

    This script does not touch src/dataverse/solutions/SpaarkeMaster/** (task 029 re-syncs the solution source from dev).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment. Dev only in R1.

.PARAMETER Delete
    Perform the delete. Without it the script only reports.

.PARAMETER OwnerApproval
    Required with -Delete. Free text recorded in the output: the date and who gave the owner's OK.

.PARAMETER DeleteRows
    With -Delete: also delete the four known test rows (after backing them up). Refuses if the rows differ from the known four.

.PARAMETER BackupPath
    Where -DeleteRows writes the rows as JSON before deleting them.

.EXAMPLE
    pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1
    pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1 -Delete -OwnerApproval '2026-10-16 owner' -WhatIf
    pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1 -Delete -OwnerApproval '2026-10-16 owner' -DeleteRows

.NOTES
    Project: ebilling-r1, task 011. Exit codes: 0 = done / report, 1 = error, 2 = refused (rows or unexpected dependency).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$Delete,
    [string]$OwnerApproval,
    [switch]$DeleteRows,
    [string]$BackupPath = (Join-Path ([System.IO.Path]::GetTempPath()) 'sprk_invoicelineitem-rows-backup.json')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Entity = 'sprk_invoicelineitem'
$EntitySet = 'sprk_invoicelineitems'
$TimekeeperRelationship = 'sprk_InvoiceLineItem_Timekeeper_1n'

# The three system forms that carry a subgrid or a navigation entry for the table (dev, 2026-10-15).
$KnownForms = @{
    '54b1c4d9-cebc-f111-aaad-7c1e520a989f' = 'sprk_billingevent'   # Billing Event main form: subgrid tab + nav entry
    'fc621f4e-ff20-4e50-9ec4-e6e758b909fc' = 'sprk_billingevent'   # Information form: nav entry only
    '93aa1c69-0406-f111-8406-7c1e525abd8b' = 'sprk_invoice'        # Invoice main form: subgrid section + nav entry
}
# The four rows in dev on 2026-10-15 (test data); -DeleteRows may delete these and nothing else.
$KnownTestRows = @(
    'ed1c6d8b-1f0c-f111-8342-7ced8d1dc988', '0815a065-e10c-f111-8342-7ced8d1dc988',
    '2f2ce1d3-2b48-f111-bec7-7ced8d1dc988', '75945818-9e9c-f111-b8de-7ced8ddc4cc6'
)

$null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun -Entity $Entity

# ---------------------------------------------------------------------------------------------------------------
# Form clean-up (pure function: form XML in, form XML out)
# ---------------------------------------------------------------------------------------------------------------

# Removes every subgrid that targets the table (with its section, and its tab when the tab is left with no section) and every
# navigation entry of a relationship to the table. Returns the new XML and what was removed. A mention that is neither a
# subgrid nor a navigation entry is left in place and counted in Remaining, so the caller can refuse.
function ConvertTo-FormWithoutLineItem([string]$FormXml, [string]$Table) {
    $doc = [xml]$FormXml
    $removed = [System.Collections.Generic.List[string]]::new()
    foreach ($ctl in @($doc.SelectNodes("//control[parameters/TargetEntityType='$Table']"))) {
        $section = $ctl.SelectSingleNode('ancestor::section[1]')
        if (-not $section -or -not $section.ParentNode) { continue }
        $tab = $section.SelectSingleNode('ancestor::tab[1]')
        $sectionName = $section.GetAttribute('name')
        [void]$section.ParentNode.RemoveChild($section)
        $removed.Add("subgrid $($ctl.GetAttribute('id')) (section $sectionName)")
        if ($tab -and $tab.ParentNode -and $tab.SelectNodes('.//section').Count -eq 0) {
            $removed.Add("tab $($tab.GetAttribute('name')) (no section left)")
            [void]$tab.ParentNode.RemoveChild($tab)
        }
    }
    foreach ($nav in @($doc.SelectNodes('//NavBarByRelationshipItem'))) {
        if ($nav.ParentNode -and $nav.GetAttribute('RelationshipName') -match '(?i)InvoiceLineItem') {
            $removed.Add("navigation entry $($nav.GetAttribute('Id'))")
            [void]$nav.ParentNode.RemoveChild($nav)
        }
    }
    $xml = $doc.OuterXml
    return [pscustomobject]@{
        Xml       = $xml
        Removed   = $removed
        Remaining = [regex]::Matches($xml, '(?i)invoicelineitem').Count
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Inventory (read-only)
# ---------------------------------------------------------------------------------------------------------------

function Get-Inventory {
    $inv = [ordered]@{}
    $ent = Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')?`$select=MetadataId,LogicalName,EntitySetName,OwnershipType,IsManaged" -Retries 2
    if (-not $ent) { return $null }
    $inv.Entity = $ent
    $id = $ent.MetadataId

    # Rows (exact count, all states) and the row ids.
    $inv.RowCount = Get-RowCountExact $EntitySet 'sprk_invoicelineitemid'
    if ($null -eq $inv.RowCount) { throw "Could not count the rows of $Entity." }
    $inv.Rows = @((Invoke-Dv "${EntitySet}?`$select=sprk_invoicelineitemid,sprk_name,createdon,modifiedon,statecode,_sprk_invoice_value,_sprk_billingevent_value").value)

    # Timekeeper rows using the obsolete lookup.
    $tkAttr = Get-Attr 'sprk_timekeeper' 'sprk_invoicelineitem' 'LogicalName,AttributeType,MetadataId'
    $inv.TimekeeperLookupPresent = [bool]$tkAttr
    $inv.TimekeeperRowsUsingLookup = if ($tkAttr) { Get-RowCountExact 'sprk_timekeepers' 'sprk_timekeeperid' '_sprk_invoicelineitem_value ne null' } else { 0 }

    # Relationships of the table.
    $inv.ManyToOne = @((Invoke-Dv "EntityDefinitions(LogicalName='$Entity')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,IsCustomRelationship").value)
    $inv.OneToMany = @((Invoke-Dv "EntityDefinitions(LogicalName='$Entity')/OneToManyRelationships?`$select=SchemaName,ReferencingEntity,IsCustomRelationship").value)
    $inv.ManyToMany = @((Invoke-Dv "EntityDefinitions(LogicalName='$Entity')/ManyToManyRelationships?`$select=SchemaName").value)

    # The table's own components (they go with it).
    $inv.OwnForms = @((Invoke-Dv "systemforms?`$select=name,type&`$filter=objecttypecode eq '$Entity'").value)
    $inv.OwnViews = @((Invoke-Dv "savedqueries?`$select=name,savedqueryid,isdefault&`$filter=returnedtypecode eq '$Entity'").value)
    $inv.AiSkillConfigs = @((Invoke-Dv "aiskillconfigs?`$select=uniquename,aiskillconfigid&`$filter=_entity_value eq $id").value)
    $inv.AppModuleComponents = @((Invoke-Dv "appmodulecomponents?`$select=appmodulecomponentid,_appmoduleidunique_value&`$filter=objectid eq $id and componenttype eq 1").value)

    # References from other components (anything here is a dependency to remove or a blocker).
    $forms = @((Invoke-Dv "systemforms?`$select=name,formid,objecttypecode&`$filter=contains(formxml,'invoicelineitem') and objecttypecode ne '$Entity'").value)
    $inv.ReferencingForms = $forms
    $inv.OtherViews = @((Invoke-Dv "savedqueries?`$select=name,returnedtypecode&`$filter=contains(fetchxml,'invoicelineitem') and returnedtypecode ne '$Entity'").value)
    $inv.PersonalViews = @((Invoke-Dv "userqueries?`$select=name&`$filter=returnedtypecode eq '$Entity' or contains(fetchxml,'invoicelineitem')").value)
    $inv.Workflows = @((Invoke-Dv "workflows?`$select=name,category&`$filter=primaryentity eq '$Entity' or contains(clientdata,'invoicelineitem') or contains(xaml,'invoicelineitem')").value)
    $inv.SiteMaps = @((Invoke-Dv "sitemaps?`$select=sitemapname&`$filter=contains(sitemapxml,'invoicelineitem')").value)
    $inv.Charts = @((Invoke-Dv "savedqueryvisualizations?`$select=name&`$filter=primaryentitytypecode eq '$Entity'").value)
    $inv.OtherLookupsToTable = @((Invoke-Dv "RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencingEntity&`$filter=ReferencedEntity eq '$Entity' and IsCustomRelationship eq true").value)
    $inv.GridConfigs = @((Invoke-Dv "sprk_gridconfigurations?`$select=sprk_name&`$filter=contains(sprk_entitylogicalname,'invoicelineitem') or contains(sprk_configjson,'invoicelineitem')").value)
    $inv.Privileges = @((Invoke-Dv "privileges?`$select=name&`$filter=contains(name,'invoicelineitem')").value)

    # Pre-flight for the write phase, so every refusal happens before the first write: run the (pure) form transform on the
    # three known forms, and read the schema name of the relationship behind the timekeeper lookup.
    $inv.FormChecks = @(foreach ($formId in $KnownForms.Keys) {
        $form = Get-DvOrNull "systemforms($formId)?`$select=name,formxml,objecttypecode"
        if (-not $form) { [pscustomobject]@{ FormId = $formId; Name = '(form not found)'; Table = $KnownForms[$formId]; Missing = $true; Removed = 0; Remaining = 0 }; continue }
        $res = ConvertTo-FormWithoutLineItem $form.formxml $Entity
        [pscustomobject]@{ FormId = $formId; Name = $form.name; Table = $form.objecttypecode; Missing = $false; Removed = $res.Removed.Count; Remaining = $res.Remaining }
    })
    $inv.TimekeeperRelationships = if ($tkAttr) {
        @((Invoke-Dv "EntityDefinitions(LogicalName='sprk_timekeeper')/ManyToOneRelationships?`$select=SchemaName,MetadataId&`$filter=ReferencingAttribute eq 'sprk_invoicelineitem'").value)
    } else { @() }

    # RetrieveDependenciesForDelete for the table, each dependent classified.
    $deps = @((Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$id,ComponentType=1)").value)
    $aiIds = @($inv.AiSkillConfigs | ForEach-Object { $_.aiskillconfigid })
    $classified = foreach ($d in $deps) {
        $kind = $null; $label = $d.dependentcomponentobjectid; $expected = $false
        switch ([int]$d.dependentcomponenttype) {
            60 {
                $kind = 'system form'
                if ($KnownForms.ContainsKey($d.dependentcomponentobjectid)) { $expected = $true; $label = "$($KnownForms[$d.dependentcomponentobjectid]) form $($d.dependentcomponentobjectid)" }
            }
            10 {
                $kind = 'relationship'
                $rel = Get-DvOrNull "RelationshipDefinitions($($d.dependentcomponentobjectid))?`$select=SchemaName"
                if ($rel) { $label = $rel.SchemaName; if ($rel.SchemaName -eq $TimekeeperRelationship) { $expected = $true } }
            }
            10314 {
                $kind = 'AI skill config'
                if ($aiIds -contains $d.dependentcomponentobjectid) { $expected = $true; $label = ($inv.AiSkillConfigs | Where-Object { $_.aiskillconfigid -eq $d.dependentcomponentobjectid }).uniquename }
            }
            80 {
                $kind = 'app module'
                $app = Get-DvOrNull "appmodules($($d.dependentcomponentobjectid))?`$select=name,uniquename"
                if ($app) { $label = "$($app.name) ($($app.uniquename))" }
                if ($inv.AppModuleComponents.Count -gt 0) { $expected = $true }
            }
            default { $kind = "component type $($d.dependentcomponenttype)" }
        }
        [pscustomobject]@{ Kind = $kind; Label = $label; Expected = $expected; ObjectId = $d.dependentcomponentobjectid }
    }
    $inv.Dependencies = @($classified)
    return $inv
}

# The reasons the delete must refuse. Empty = ready.
function Get-Blockers($inv, [bool]$RowsMayGo) {
    $b = [System.Collections.Generic.List[string]]::new()
    if ($inv.RowCount -gt 0) {
        $unknown = @($inv.Rows | Where-Object { $KnownTestRows -notcontains $_.sprk_invoicelineitemid })
        if (-not $RowsMayGo) { $b.Add("$($inv.RowCount) row(s) in $Entity (FR-04 negative case: nothing is deleted while rows exist)") }
        elseif ($unknown.Count -gt 0) { $b.Add("$($unknown.Count) row(s) are not among the four known test rows: $(($unknown | ForEach-Object { $_.sprk_invoicelineitemid }) -join ', ')") }
    }
    if ($inv.TimekeeperRowsUsingLookup -gt 0) { $b.Add("$($inv.TimekeeperRowsUsingLookup) sprk_timekeeper row(s) hold a value in sprk_invoicelineitem") }
    foreach ($fc in $inv.FormChecks | Where-Object { -not $_.Missing -and $_.Remaining -gt 0 }) { $b.Add("form '$($fc.Name)' ($($fc.Table), $($fc.FormId)) would still mention the table after removing its subgrid and navigation entry ($($fc.Remaining) mention(s))") }
    if ($inv.TimekeeperLookupPresent -and (@($inv.TimekeeperRelationships).Count -ne 1 -or @($inv.TimekeeperRelationships)[0].SchemaName -ne $TimekeeperRelationship)) {
        $b.Add("the relationship behind sprk_timekeeper.sprk_invoicelineitem is not exactly $TimekeeperRelationship")
    }
    foreach ($f in $inv.ReferencingForms) { if (-not $KnownForms.ContainsKey($f.formid)) { $b.Add("system form '$($f.name)' ($($f.objecttypecode), $($f.formid)) mentions the table") } }
    foreach ($d in $inv.Dependencies | Where-Object { -not $_.Expected }) { $b.Add("unexpected dependency: $($d.Kind) $($d.Label)") }
    foreach ($v in $inv.OtherViews) { $b.Add("view '$($v.name)' of $($v.returnedtypecode) mentions the table") }
    foreach ($v in $inv.PersonalViews) { $b.Add("personal view '$($v.name)' mentions the table") }
    foreach ($w in $inv.Workflows) { $b.Add("workflow / flow '$($w.name)' mentions the table") }
    foreach ($s in $inv.SiteMaps) { $b.Add("site map '$($s.sitemapname)' mentions the table") }
    foreach ($c in $inv.Charts) { $b.Add("chart '$($c.name)' is built on the table") }
    foreach ($r in $inv.OtherLookupsToTable | Where-Object { $_.SchemaName -ne $TimekeeperRelationship }) { $b.Add("relationship $($r.SchemaName) ($($r.ReferencingEntity)) targets the table") }
    foreach ($g in $inv.GridConfigs) { $b.Add("grid configuration '$($g.sprk_name)' mentions the table") }
    if ($inv.ManyToMany.Count -gt 0) { $b.Add("many-to-many relationship(s): $(($inv.ManyToMany | ForEach-Object { $_.SchemaName }) -join ', ')") }
    return $b
}

function Write-Inventory($inv) {
    Write-Host "Table      : $Entity ($($inv.Entity.OwnershipType), managed=$($inv.Entity.IsManaged), MetadataId $($inv.Entity.MetadataId))"
    Write-Host "Rows       : $($inv.RowCount)" -ForegroundColor $(if ($inv.RowCount -gt 0) { 'Red' } else { 'Green' })
    foreach ($r in $inv.Rows) {
        $mark = if ($KnownTestRows -contains $r.sprk_invoicelineitemid) { 'known test row' } else { 'NOT a known test row' }
        Write-Host ("  {0}  '{1}'  created {2}  invoice {3}  billingevent {4}  [{5}]" -f $r.sprk_invoicelineitemid, $r.sprk_name, $r.createdon, $r._sprk_invoice_value, $r._sprk_billingevent_value, $mark)
    }
    Write-Host "Timekeeper : lookup sprk_timekeeper.sprk_invoicelineitem present=$($inv.TimekeeperLookupPresent); timekeeper rows using it: $($inv.TimekeeperRowsUsingLookup)"
    Write-Host "Relations  : $($inv.ManyToOne.Count) many-to-one ($(@($inv.ManyToOne | Where-Object IsCustomRelationship).Count) custom), $($inv.OneToMany.Count) one-to-many ($(@($inv.OneToMany | Where-Object IsCustomRelationship).Count) custom), $($inv.ManyToMany.Count) many-to-many"
    Write-Host "Own forms  : $($inv.OwnForms.Count)   own views: $($inv.OwnViews.Count)   AI skill configs: $($inv.AiSkillConfigs.Count)   app module registrations: $($inv.AppModuleComponents.Count)"
    Write-Host "Referenced by other components:"
    Write-Host ("  system forms of other tables: {0}  ({1})" -f $inv.ReferencingForms.Count, (($inv.ReferencingForms | ForEach-Object { "$($_.objecttypecode) $($_.name)" }) -join '; '))
    Write-Host ("  views of other tables: {0}   personal views: {1}   workflows / flows: {2}   site maps: {3}   charts: {4}   grid configurations: {5}" -f $inv.OtherViews.Count, $inv.PersonalViews.Count, $inv.Workflows.Count, $inv.SiteMaps.Count, $inv.Charts.Count, $inv.GridConfigs.Count)
    Write-Host ("  lookups on other tables: {0}  ({1})" -f $inv.OtherLookupsToTable.Count, (($inv.OtherLookupsToTable | ForEach-Object { "$($_.ReferencingEntity) via $($_.SchemaName)" }) -join '; '))
    Write-Host ("  security privileges: {0}  (removed with the table; role XML is refreshed by task 029)" -f $inv.Privileges.Count)
    Write-Host "RetrieveDependenciesForDelete: $($inv.Dependencies.Count) dependent(s)"
    foreach ($d in $inv.Dependencies) { Write-Host ("  {0,-9} {1}: {2}" -f $(if ($d.Expected) { 'expected' } else { 'UNEXPECTED' }), $d.Kind, $d.Label) -ForegroundColor $(if ($d.Expected) { 'Gray' } else { 'Red' }) }
}

# ---------------------------------------------------------------------------------------------------------------
# Removal steps
# ---------------------------------------------------------------------------------------------------------------

# Entity-scoped publish (never PublishAll: other sessions may have unpublished work in dev).
function Publish-Tables([string[]]$Tables) {
    if ($DryRun) { Write-Host "  WOULD PUBLISH $($Tables -join ', ')" -ForegroundColor Cyan; return }
    $ents = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities>$ents</entities></importexportxml>" } | Out-Null
    Write-Host "  published $($Tables -join ', ')" -ForegroundColor Green
}

# The pre-flight (Get-Inventory / Get-Blockers) has already run this transform on every form, so the throw below is only a safety
# net for a form that changed in between.
function Remove-FormReferences {
    foreach ($formId in $KnownForms.Keys) {
        $form = Invoke-Dv "systemforms($formId)?`$select=name,formxml,objecttypecode,ismanaged"
        $res = ConvertTo-FormWithoutLineItem $form.formxml $Entity
        if ($res.Remaining -gt 0) { throw [EscalationException]::new("Form '$($form.name)' ($formId) still mentions the table after removing its subgrid and navigation entry ($($res.Remaining) mention(s)); it changed after the pre-flight. Forms edited before this one stay edited; nothing else was removed.") }
        if ($res.Removed.Count -eq 0) { Write-Host "  skipped  $($form.objecttypecode) form '$($form.name)': no reference left" -ForegroundColor Yellow; continue }
        if ($DryRun) { Write-Host "  WOULD EDIT $($form.objecttypecode) form '$($form.name)': remove $($res.Removed -join '; ')" -ForegroundColor Cyan; continue }
        Invoke-Dv -Path "systemforms($formId)" -Method PATCH -Body @{ formxml = $res.Xml } | Out-Null
        Write-Host "  EDITED   $($form.objecttypecode) form '$($form.name)': removed $($res.Removed -join '; ')" -ForegroundColor Green
    }
    # Always publish (a previous run may have edited the forms and stopped before publishing).
    Publish-Tables @('sprk_billingevent', 'sprk_invoice')
}

function Remove-TimekeeperLookup($inv) {
    if (-not $inv.TimekeeperLookupPresent) { Write-Host "  skipped  sprk_timekeeper.sprk_invoicelineitem already gone" -ForegroundColor Yellow; return }
    $rel = @((Invoke-Dv "EntityDefinitions(LogicalName='sprk_timekeeper')/ManyToOneRelationships?`$select=SchemaName,MetadataId&`$filter=ReferencingAttribute eq 'sprk_invoicelineitem'").value)
    if ($rel.Count -ne 1 -or $rel[0].SchemaName -ne $TimekeeperRelationship) { throw [EscalationException]::new("The relationship behind sprk_timekeeper.sprk_invoicelineitem is not exactly $TimekeeperRelationship (it changed after the pre-flight). Steps before this one stay done.") }
    if ($DryRun) { Write-Host "  WOULD DELETE relationship $TimekeeperRelationship (column sprk_timekeeper.sprk_invoicelineitem and its name companion go with it)" -ForegroundColor Cyan; return }
    Invoke-WhilePropagating { Invoke-Dv -Path "RelationshipDefinitions($($rel[0].MetadataId))" -Method DELETE | Out-Null } | Out-Null
    $gone = $false
    for ($i = 0; $i -lt 18 -and -not $gone; $i++) {
        $gone = -not (Get-Attr 'sprk_timekeeper' 'sprk_invoicelineitem')
        if (-not $gone) { Start-Sleep -Seconds 5 }
    }
    if (-not $gone) { throw "sprk_timekeeper.sprk_invoicelineitem is still readable after the relationship delete." }
    Write-Host "  DELETED  relationship $TimekeeperRelationship (column removed)" -ForegroundColor Green
}

function Remove-ByIds([string]$Label, [string]$Set, $Ids) {
    foreach ($id in $Ids) {
        if ($DryRun) { Write-Host "  WOULD DELETE $Label $id" -ForegroundColor Cyan; continue }
        try { Invoke-Dv -Path "$Set($id)" -Method DELETE | Out-Null; Write-Host "  DELETED  $Label $id" -ForegroundColor Green }
        catch {
            if ($_.Exception.Data['Status'] -eq 404) { Write-Host "  skipped  $Label $id already gone" -ForegroundColor Yellow }
            else { throw }
        }
    }
}

function Export-RowBackup {
    $all = Invoke-Dv $EntitySet
    $json = $all.value | ConvertTo-Json -Depth 8
    if ($DryRun) { Write-Host "  WOULD WRITE backup of $(@($all.value).Count) row(s) to $BackupPath" -ForegroundColor Cyan; return }
    Set-Content -LiteralPath $BackupPath -Value $json -Encoding utf8
    Write-Host "  backup of $(@($all.value).Count) row(s) written to $BackupPath" -ForegroundColor Green
}

function Test-VerifyAfterDelete {
    $problems = [System.Collections.Generic.List[string]]::new()
    if (Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')?`$select=LogicalName" -Retries 2) { $problems.Add("EntityDefinitions(LogicalName='$Entity') still answers") }
    else { Write-Host "  EntityDefinitions(LogicalName='$Entity') -> 404" -ForegroundColor Green }
    foreach ($t in 'sprk_billingevent', 'sprk_invoice', 'sprk_timekeeper') {
        $forms = @((Invoke-Dv "systemforms?`$select=name,formxml&`$filter=objecttypecode eq '$t' and type eq 2").value)
        $bad = 0
        foreach ($f in $forms) {
            try { [void][xml]$f.formxml } catch { $problems.Add("$t form '$($f.name)' is not valid XML"); continue }
            if ($f.formxml -match '(?i)invoicelineitem') { $bad++; $problems.Add("$t form '$($f.name)' still mentions the table") }
        }
        Write-Host "  $t main forms: $($forms.Count) load as XML, $bad still mention the table" -ForegroundColor $(if ($bad -eq 0) { 'Green' } else { 'Red' })
    }
    return $problems
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 011 - retire $Entity" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if (-not $Delete) { 'REPORT (read-only)' } elseif ($DryRun) { 'DELETE -WhatIf (no writes)' } else { 'DELETE' })"
Write-Host ""

try {
    if ($DeleteRows -and -not $Delete) { throw "-DeleteRows needs -Delete." }
    if ($Delete -and -not $PSBoundParameters.ContainsKey('EnvironmentUrl') -and ([uri]$EnvironmentUrl).Host -ne 'spaarkedev1.crm.dynamics.com') {
        throw [EscalationException]::new("-Delete refuses the environment '$EnvironmentUrl' that came from `$env:DATAVERSE_URL: pass -EnvironmentUrl explicitly (dev is https://spaarkedev1.crm.dynamics.com). Nothing was changed.")
    }
    if ($Delete -and [string]::IsNullOrWhiteSpace($OwnerApproval)) {
        throw "-Delete needs -OwnerApproval '<date and who>': the owner's explicit OK is required before the table is deleted (project CLAUDE.md section 5)."
    }

    $inv = Get-Inventory
    if (-not $inv) {
        Write-Host "$Entity does not exist in $EnvironmentUrl (already retired). Nothing to do." -ForegroundColor Yellow
        exit 0
    }
    Write-Inventory $inv
    $blockers = @(Get-Blockers $inv ([bool]$DeleteRows))
    Write-Host ""
    if ($blockers.Count -eq 0) { Write-Host "VERDICT: READY - no row, no unexpected dependency." -ForegroundColor Green }
    else {
        Write-Host "VERDICT: BLOCKED" -ForegroundColor Red
        foreach ($x in $blockers) { Write-Host "  - $x" -ForegroundColor Red }
    }

    if (-not $Delete) {
        Write-Host ""
        Write-Host "Report only. Nothing was changed. To delete (after the owner's OK): -Delete -OwnerApproval '<date and who>' [-DeleteRows]." -ForegroundColor Cyan
        exit 0
    }

    if ($blockers.Count -gt 0) {
        throw [EscalationException]::new("Refusing to delete ${Entity}: $($blockers.Count) blocker(s) listed above. Nothing was changed.")
    }

    Write-Host ""
    Write-Host "Owner approval recorded: $OwnerApproval" -ForegroundColor Cyan
    $ran = [System.Collections.Generic.List[string]]::new()
    Write-Host "0. Backup" -ForegroundColor Cyan
    if ($inv.RowCount -gt 0) { Export-RowBackup; $ran.Add('0 row backup written') } else { Write-Host "  no rows - nothing to back up" -ForegroundColor Yellow }

    Write-Host "1. Form subgrids and navigation entries (sprk_billingevent, sprk_invoice)" -ForegroundColor Cyan
    Remove-FormReferences
    $ran.Add('1 form subgrids / navigation entries removed and sprk_billingevent, sprk_invoice published')

    Write-Host "2. sprk_timekeeper.sprk_invoicelineitem lookup" -ForegroundColor Cyan
    Remove-TimekeeperLookup $inv
    Publish-Tables @('sprk_timekeeper')
    $ran.Add('2 timekeeper lookup removed and sprk_timekeeper published')

    Write-Host "3. AI skill config opt-outs" -ForegroundColor Cyan
    Remove-ByIds 'aiskillconfig' 'aiskillconfigs' @($inv.AiSkillConfigs | ForEach-Object { $_.aiskillconfigid })
    $ran.Add('3 AI skill configs removed')

    Write-Host "4. App module registrations" -ForegroundColor Cyan
    # appmodulecomponent rows cannot be deleted directly (0x80040800); the app's RemoveAppComponents action removes the table
    # from each app that lists it. Copilot table-search entries (dvtablesearchentity) naming the table go first.
    foreach ($ts in @((Invoke-Dv "dvtablesearchentities?`$select=dvtablesearchentityid&`$filter=entitylogicalname eq '$Entity'").value)) {
        if ($DryRun) { Write-Host "  WOULD DELETE table-search entry $($ts.dvtablesearchentityid)" -ForegroundColor Cyan; continue }
        Invoke-Dv -Path "dvtablesearchentities($($ts.dvtablesearchentityid))" -Method DELETE | Out-Null
        Write-Host "  DELETED  table-search entry $($ts.dvtablesearchentityid)" -ForegroundColor Green
    }
    # Known limit (dev, 2026-10-15): on 'Spaarke Platform' (sprk_SpaarkePlatform, an M365 Copilot app) RemoveAppComponents
    # returns 204 but keeps the table; step 5 then refuses and names the app. Remove the table in the app designer and re-run.
    $apps =@($inv.AppModuleComponents | ForEach-Object { $_._appmoduleidunique_value } | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($unique in $apps) {
        $app = @((Invoke-Dv "appmodules?`$select=appmoduleid,name&`$filter=appmoduleidunique eq $unique").value) | Select-Object -First 1
        if (-not $app) { Write-Host "  skipped  app $unique not found" -ForegroundColor Yellow; continue }
        if ($DryRun) { Write-Host "  WOULD REMOVE table from app '$($app.name)'" -ForegroundColor Cyan; continue }
        Invoke-Dv -Path 'RemoveAppComponents' -Method POST -Body @{
            AppId      = $app.appmoduleid
            Components = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.entity'; entityid = "$($inv.Entity.MetadataId)" })
        } | Out-Null
        Write-Host "  REMOVED  table from app '$($app.name)'" -ForegroundColor Green
        # The removal reaches the dependency graph only once the app is published.
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><appmodules><appmodule>$($app.appmoduleid)</appmodule></appmodules></importexportxml>" } | Out-Null
        Write-Host "  published app '$($app.name)'" -ForegroundColor Green
    }
    $ran.Add('4 app module registrations removed')

    Write-Host "5. RetrieveDependenciesForDelete must now be empty" -ForegroundColor Cyan
    if ($DryRun) { Write-Host "  WhatIf: not re-checked (nothing was removed)" -ForegroundColor Cyan }
    else {
        $left = @()
        for ($i = 0; $i -lt 6; $i++) {
            $left = @((Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$($inv.Entity.MetadataId),ComponentType=1)").value)
            if ($left.Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        if ($left.Count -gt 0) { throw [EscalationException]::new("$($left.Count) dependent(s) remain after the removals: $(($left | ForEach-Object { "type $($_.dependentcomponenttype) $($_.dependentcomponentobjectid)" }) -join '; '). The table, its rows and the dependencies listed above were NOT deleted.") }
        Write-Host "  no dependents left" -ForegroundColor Green
        $ran.Add('5 dependency re-check clean')
    }

    Write-Host "6. Rows" -ForegroundColor Cyan
    if ($inv.RowCount -gt 0) { Remove-ByIds 'row' $EntitySet @($inv.Rows | ForEach-Object { $_.sprk_invoicelineitemid }) } else { Write-Host "  none" -ForegroundColor Yellow }
    if ($inv.RowCount -gt 0 -and -not $DryRun) { $ran.Add('6 rows deleted (backup file kept)') }

    Write-Host "7. The table's own views" -ForegroundColor Cyan
    foreach ($v in $inv.OwnViews) {
        if ($DryRun) { Write-Host "  WOULD DELETE view '$($v.name)'" -ForegroundColor Cyan; continue }
        try { Invoke-Dv -Path "savedqueries($($v.savedqueryid))" -Method DELETE | Out-Null; Write-Host "  DELETED  view '$($v.name)'" -ForegroundColor Green }
        catch { Write-Host "  not deleted separately (goes with the table): view '$($v.name)': $($_.Exception.Message)" -ForegroundColor Yellow }
    }

    Write-Host "8. The table" -ForegroundColor Cyan
    if ($DryRun) { Write-Host "  WOULD DELETE EntityDefinitions($($inv.Entity.MetadataId)) ($Entity)" -ForegroundColor Cyan }
    else {
        # A row may have been created since the pre-flight (nothing blocks a writer while this runs): recount right before the delete.
        $nowRows = Get-RowCountExact $EntitySet 'sprk_invoicelineitemid'
        if ($null -eq $nowRows -or $nowRows -gt 0) {
            throw [EscalationException]::new("Refusing the final table delete: $(if ($null -eq $nowRows) { 'the rows could not be counted' } else { "$nowRows row(s) exist in $Entity" }). Steps already run: $($ran -join '; '). The table itself was NOT deleted; re-run once the rows are dealt with.")
        }
        Invoke-WhilePropagating { Invoke-Dv -Path "EntityDefinitions($($inv.Entity.MetadataId))" -Method DELETE | Out-Null } | Out-Null
        $gone = $false
        for ($i = 0; $i -lt 24 -and -not $gone; $i++) {
            $gone = -not (Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')?`$select=LogicalName")
            if (-not $gone) { Start-Sleep -Seconds 5 }
        }
        if (-not $gone) { throw "$Entity is still readable 120 s after the delete." }
        Write-Host "  DELETED  $Entity" -ForegroundColor Green
        Publish-Tables @('sprk_billingevent', 'sprk_invoice', 'sprk_timekeeper')
    }

    Write-Host "9. Verify" -ForegroundColor Cyan
    if ($DryRun) { Write-Host "  WhatIf: not verified" -ForegroundColor Cyan }
    else {
        $problems = Test-VerifyAfterDelete
        if ($problems.Count -gt 0) { throw "Verification failed: $($problems -join '; ')" }
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Steps already done stay done; re-run to continue (every step is skipped when its work is already done)." -ForegroundColor Red
    exit 1
}

Write-Host ""
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
