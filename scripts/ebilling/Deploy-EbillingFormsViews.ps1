<#
.SYNOPSIS
    eBilling R1 task 029 - builds the WP1 forms, views and navigation in dev (main form + Active view for each new WP1 table,
    the WP1 columns on the sprk_invoice / sprk_billingevent / sprk_timekeeper main forms, the "Invoices to review" view, and the
    sitemap entries), then publishes ONLY the touched entities / sitemaps / app modules.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only in R1). Every step is check-then-act; a second run reports no change and writes
    nothing (no publish either). Run -WhatIf first: it reads everything, prints what it would do and writes nothing.

    What it does (spec FR-08 / FR-35 / FR-48; tasks/029-wp1-apply-and-export.poml steps 2-3):
      Forms   Rewrites the stub main form of sprk_ebillingsetting, sprk_ratecard, sprk_ratecardrate, sprk_billingcodeset,
              sprk_billingcode, sprk_ruleset, sprk_invoicesubmission, sprk_invoiceapproval, sprk_externalidentifier,
              sprk_billingexport and sprk_invoicecorrection (all controls read-only) from the table below; the rate card form gets a
              rates subgrid, the code set form a codes subgrid, the approval form an "Eligible approvers" (N:N contact) subgrid.
              On the EXISTING sprk_timekeeper, sprk_invoice and sprk_billingevent main forms only the section "section_ebilling" is
              added or refreshed (everything else on those forms is left byte for byte). Read-only (task 014/016 constraint):
              the five invoice totals and sprk_billingevent.sprk_amount (the Adjustment line amount is computed by
              IInvoiceTotalsService; Fee / Expense lines are written by the extraction pipeline, not typed) are disabled controls.
              Forms carry no script and no business rule (ADR-002).
      Views   Replaces the columns of each table's "Active ..." view; creates "Invoices to review" on sprk_invoice (In Review
              invoices with a Pending sprk_invoiceapproval that lists the current user's contact as an eligible approver:
              contact.sprk_systemuser equals the current user).
      Sitemap sprk_SpaarkeAdministration gets an eBilling area (settings, timekeeper, rate card, code set, rule set);
              sprk_MatterManagement gets the invoice submission and billing export entries after the sprk_invoice entry.
      Publish PublishXml for the touched entities, the two sitemaps and the two app modules only. Never PublishAllXml.

    Pending collateral (D-103): publishing an entity also publishes pending forms / views of that entity. The script lists any pending
    form or view that it does not edit itself and refuses to publish unless -AllowPendingCollateral is given.

    This is also the procedure later serial tasks reuse (053, 079, 112, 136, 153): add the new column names to the spec table
    ($Spec) and re-run.

.PARAMETER EnvironmentUrl
    Default https://spaarkedev1.crm.dynamics.com (dev only in R1).
.PARAMETER SolutionUniqueName
    Default SpaarkeMaster. New views are created inside it.
.PARAMETER AllowPendingCollateral
    Continue when another pending (unpublished) form / view of a touched entity would be published too.
.PARAMETER FormsOnly
    Run step 1 (forms) and the entity-scoped publish only; skip views, navigation and app tables. Added by task 122 (#2235) to take
    sprk_findingsjson off the live sprk_invoicesubmission form: on spaarkedev1 (2026-10-23) step 3 throws in the
    sprk_MatterManagement sitemap (sprk_invoice is listed in two areas and sprk_invoicesubmission sits in area_finance), after step 1
    has already written the form and before step 4 publishes it.
.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1
    pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -FormsOnly -WhatIf
#>
using module .\EbillingSchema.psm1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$AllowPendingCollateral,
    [switch]$FormsOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($EnvironmentUrl -notmatch 'spaarkedev1') { throw "Dev only in R1: $EnvironmentUrl" }
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$WhatIfPreference
. (Join-Path $PSScriptRoot '..' 'lib' 'Publish-SolutionComponents.ps1')
$Ctx = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl

$Changes = [System.Collections.Generic.List[string]]::new()
$Unchanged = [System.Collections.Generic.List[string]]::new()
$Touched = [System.Collections.Generic.HashSet[string]]::new()
$EditedIds = [System.Collections.Generic.HashSet[string]]::new()
$Evidence = [ordered]@{}

# ------------------------------------------------------------------------------------------------------------------
# Spec. Column names only; labels, control types and the grid / form plumbing come from live metadata.
#   Form     'Replace' = the stub main form is rewritten; 'Section' = only section_ebilling on an existing main form.
#   ReadOnly = every control disabled (invoicecorrection) or the listed ones.
# ------------------------------------------------------------------------------------------------------------------
$Spec = [ordered]@{
    sprk_ebillingsetting   = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(
            @{ Name = 'general'; Label = 'General'; Cols = 'sprk_name', 'sprk_singleton', 'sprk_defaultcodeset', 'ownerid'; ReadOnly = 'sprk_singleton' }
            @{ Name = 'approvers'; Label = 'Approver defaults (used when a record has no approver role)'; Cols = 'sprk_matterapproverroles', 'sprk_projectapproverroles', 'sprk_workassignmentapproverroles' }
            @{ Name = 'reliability'; Label = 'Reliability and confidence weights'; Cols = 'sprk_reliabilitythreshold', 'sprk_weightanalyzer', 'sprk_weightarithmetic', 'sprk_weightreferencematch', 'sprk_weightselfreport', 'sprk_arithmeticfailcap', 'sprk_referencemissingcap' }
            @{ Name = 'processing'; Label = 'Processing'; Cols = 'sprk_codingchunksize', 'sprk_relatedwindowdays', 'sprk_firmnoticeaienabled' }
            @{ Name = 'apexport'; Label = 'AP export'; Cols = 'sprk_apexportmapping' }
        )
        View = 'sprk_name', 'sprk_defaultcodeset', 'sprk_reliabilitythreshold', 'sprk_codingchunksize', 'sprk_relatedwindowdays', 'modifiedon'
    }
    sprk_timekeeper        = @{
        Form = 'Section'; FormName = 'Timekeeper main form'
        Sections = @(@{ Name = 'ebilling'; Label = 'Timekeeper roster'; Cols = 'sprk_firm', 'sprk_classification', 'sprk_effectivefrom', 'sprk_effectiveto' })
        View = 'sprk_name', 'sprk_firm', 'sprk_classification', 'sprk_contact', 'sprk_effectivefrom', 'sprk_effectiveto'
    }
    sprk_ratecard          = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'General'; Cols = 'sprk_name', 'sprk_firm', 'sprk_effectivefrom', 'sprk_effectiveto', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_sourcedocument', 'ownerid' })
        Subgrids = @(@{ Name = 'rates'; Label = 'Rates'; Target = 'sprk_ratecardrate'; Relationship = 'sprk_ratecard_ratecardrate_ratecard' })
        View = 'sprk_name', 'sprk_firm', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_effectivefrom', 'sprk_effectiveto'
    }
    sprk_ratecardrate      = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'General'; Cols = 'sprk_name', 'sprk_ratecard', 'sprk_timekeeper', 'sprk_classification', 'sprk_rackrate', 'sprk_approvedrate', 'sprk_effectivefrom', 'sprk_effectiveto', 'ownerid' })
        View = 'sprk_name', 'sprk_ratecard', 'sprk_timekeeper', 'sprk_classification', 'sprk_rackrate', 'sprk_approvedrate', 'sprk_effectivefrom', 'sprk_effectiveto'
    }
    sprk_billingcodeset    = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'General'; Cols = 'sprk_name', 'sprk_version', 'sprk_body', 'sprk_sourceurl', 'ownerid' })
        Subgrids = @(@{ Name = 'codes'; Label = 'Codes'; Target = 'sprk_billingcode'; Relationship = 'sprk_billingcodeset_billingcode_codeset' })
        View = 'sprk_name', 'sprk_version', 'sprk_body', 'sprk_sourceurl'
    }
    sprk_billingcode       = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'General'; Cols = 'sprk_code', 'sprk_codeset', 'sprk_codetype', 'sprk_phase', 'sprk_description', 'sprk_guidancetext', 'ownerid' })
        View = 'sprk_code', 'sprk_codeset', 'sprk_codetype', 'sprk_phase', 'sprk_description'
        OrderBy = 'sprk_code'
    }
    sprk_ruleset           = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'General'; Cols = 'sprk_name', 'sprk_domain', 'sprk_scope', 'sprk_firm', 'sprk_visibletofirms', 'sprk_effectivefrom', 'sprk_effectiveto', 'sprk_sourcedocument', 'statuscode', 'ownerid' }
            @{ Name = 'notconverted'; Label = 'Source clauses not converted'; Cols = 'sprk_notconverted' })
        View = 'sprk_name', 'sprk_domain', 'sprk_scope', 'sprk_firm', 'sprk_visibletofirms', 'sprk_effectivefrom', 'sprk_effectiveto', 'statuscode'
    }
    sprk_invoicesubmission = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(
            @{ Name = 'general'; Label = 'Submission'; Cols = 'sprk_name', 'sprk_trackingnumber', 'sprk_firminvoicenumber', 'sprk_submissionsequence', 'sprk_vendororg', 'sprk_submitter', 'sprk_channel', 'sprk_processingstatus', 'sprk_receivedon', 'sprk_ponumber', 'sprk_outcomereasoncode', 'sprk_adminattention', 'sprk_correlationid', 'ownerid' }
            @{ Name = 'links'; Label = 'Resulting invoice and documents'; Cols = 'sprk_invoice', 'sprk_resubmissionof', 'sprk_document', 'sprk_reportdocument' }
            @{ Name = 'regarding'; Label = 'Regarding'; Cols = 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment' }
            @{ Name = 'findings'; Label = 'Firm notice'; Cols = 'sprk_firmnoticetext' }  # task 122 (#2235): sprk_findingsjson is BFF-only, never on a form
        )
        View = 'sprk_trackingnumber', 'sprk_firminvoicenumber', 'sprk_vendororg', 'sprk_channel', 'sprk_processingstatus', 'sprk_receivedon', 'sprk_invoice', 'sprk_submitter'
    }
    sprk_invoiceapproval   = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'Approval'; Cols = 'sprk_name', 'sprk_invoice', 'sprk_approvalstatus', 'sprk_approvedamount', 'sprk_decidedby', 'sprk_decidedon', 'sprk_decisionrecord', 'sprk_eligibleresolvedon', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'ownerid' })
        Subgrids = @(@{ Name = 'eligible'; Label = 'Eligible approvers'; Target = 'contact'; Relationship = 'sprk_invoiceapproval_eligibleapprover_contact'; ViewName = 'Active Contacts' })
        View = 'sprk_name', 'sprk_invoice', 'sprk_approvalstatus', 'sprk_approvedamount', 'sprk_decidedby', 'sprk_decidedon'
    }
    sprk_externalidentifier = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'Identifier'; Cols = 'sprk_name', 'sprk_identifiertype', 'sprk_identifiervalue', 'sprk_normalizedvalue', 'sprk_issuingorganization', 'sprk_issuingsystem', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'ownerid' })
        View = 'sprk_name', 'sprk_identifiertype', 'sprk_identifiervalue', 'sprk_issuingorganization', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject'
    }
    sprk_billingexport     = @{
        Form = 'Replace'; FormName = 'Information'
        Sections = @(@{ Name = 'general'; Label = 'Export batch'; Cols = 'sprk_name', 'sprk_exportstatus', 'sprk_invoicecount', 'sprk_totalamount', 'sprk_formats', 'sprk_failurereason', 'sprk_csvdocument', 'sprk_jsondocument', 'ownerid' }
            @{ Name = 'mapping'; Label = 'Mapping snapshot'; Cols = 'sprk_mappingsnapshot' })
        View = 'sprk_name', 'sprk_exportstatus', 'sprk_invoicecount', 'sprk_totalamount', 'createdon'
    }
    sprk_invoicecorrection = @{
        Form = 'Replace'; FormName = 'Information'; AllReadOnly = $true
        Sections = @(
            @{ Name = 'general'; Label = 'Correction (read-only)'; Cols = 'sprk_name', 'sprk_invoice', 'sprk_invoiceline', 'sprk_fieldname', 'sprk_outcometype', 'sprk_islabel', 'sprk_aiconfidence', 'sprk_sourcecode', 'sprk_sourceversion', 'sprk_reviewedby', 'sprk_reviewedon', 'sprk_reviewerrole', 'ownerid' }
            @{ Name = 'values'; Label = 'Values'; Cols = 'sprk_aivalue', 'sprk_finalvalue' }
            @{ Name = 'regarding'; Label = 'Regarding'; Cols = 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment' }
        )
        View = 'sprk_name', 'sprk_invoice', 'sprk_fieldname', 'sprk_outcometype', 'sprk_islabel', 'sprk_reviewedby', 'sprk_reviewedon'
    }
    sprk_invoice           = @{
        Form = 'Section'; FormName = 'Invoice main form'
        Sections = @(@{ Name = 'ebilling'; Label = 'eBilling'
                Cols = 'sprk_matter', 'sprk_project', 'sprk_regardingworkassignment', 'sprk_supersedesinvoice', 'sprk_billingperiodstart', 'sprk_billingperiodend', 'sprk_invoiceduedate', 'sprk_currency', 'sprk_extractionstatus', 'sprk_confidence', 'sprk_billingapproverrole', 'sprk_billingexport',
                'sprk_feestotal', 'sprk_expensestotal', 'sprk_lineadjustmentstotal', 'sprk_invoiceadjustmentstotal', 'sprk_approvedtotal'
                ReadOnly = 'sprk_feestotal', 'sprk_expensestotal', 'sprk_lineadjustmentstotal', 'sprk_invoiceadjustmentstotal', 'sprk_approvedtotal' })
        # IInvoiceTotalsService is the one owner of the five totals (I-1): disabled, never editable on a form.
        ForceReadOnly = 'sprk_feestotal', 'sprk_expensestotal', 'sprk_lineadjustmentstotal', 'sprk_invoiceadjustmentstotal', 'sprk_approvedtotal'
    }
    sprk_billingevent      = @{
        Form = 'Section'; FormName = 'Billing Event main form'
        Sections = @(@{ Name = 'ebilling'; Label = 'Invoice line'
                Cols = 'sprk_linestatus', 'sprk_taskcode', 'sprk_taskcodeasbilled', 'sprk_activitycode', 'sprk_activitycodeasbilled', 'sprk_expensecode', 'sprk_expensecodeasbilled', 'sprk_resolvedtimekeeper', 'sprk_approvedrate', 'sprk_approvedamount', 'sprk_codingconfidence', 'sprk_extractionconfidence',
                'sprk_adjustmentmethod', 'sprk_adjustmentvalue', 'sprk_adjustmentrule', 'sprk_adjustmentreason', 'sprk_reviewedby', 'sprk_reviewedon' })
        # Adjustment-line amount: computed by IInvoiceTotalsService; Fee / Expense lines are written by the pipeline (no manual typing).
        ForceReadOnly = 'sprk_amount'
    }
}

# ------------------------------------------------------------------------------------------------------------------
# Plumbing
# ------------------------------------------------------------------------------------------------------------------
function Get-DetGuid([string]$Seed) {
    $h = [System.Security.Cryptography.SHA1]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes("ebilling029:$Seed"))
    return ([guid]::new([byte[]]$h[0..15])).ToString('B').ToUpperInvariant()
}
function Esc([string]$s) { return [System.Security.SecurityElement]::Escape($s) }

function Get-TableMeta([string]$Table) {
    $r = Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')?`$select=ObjectTypeCode,PrimaryIdAttribute,PrimaryNameAttribute&`$expand=Attributes(`$select=LogicalName,AttributeType,AttributeTypeName,DisplayName)"
    $attrs = @{}
    foreach ($a in $r.Attributes) { $attrs[$a.LogicalName] = $a }
    return [pscustomobject]@{ Table = $Table; Otc = $r.ObjectTypeCode; Pk = $r.PrimaryIdAttribute; Prim = $r.PrimaryNameAttribute; Attrs = $attrs }
}
function Get-Label($meta, [string]$Col) {
    if (-not $meta.Attrs.ContainsKey($Col)) { throw "$($meta.Table).$Col does not exist in dev metadata (a WP1 script has not run?)" }
    if ($script:LabelCache.ContainsKey("$($meta.Table).$Col")) { return $script:LabelCache["$($meta.Table).$Col"] }
    $a = $meta.Attrs[$Col]
    $l = $null
    if ($a.DisplayName -and $a.DisplayName.LocalizedLabels -and @($a.DisplayName.LocalizedLabels).Count -gt 0) { $l = @($a.DisplayName.LocalizedLabels)[0].Label }
    if ([string]::IsNullOrWhiteSpace($l)) {
        # The bulk $expand read can return an empty DisplayName for an attribute whose label is fine (eventually consistent
        # metadata); re-read the single attribute. Never fall back to a guessed label: a wrong label is written into the form.
        for ($i = 0; $i -lt 4 -and [string]::IsNullOrWhiteSpace($l); $i++) {
            $one = Invoke-Dv -Path "EntityDefinitions(LogicalName='$($meta.Table)')/Attributes(LogicalName='$Col')?`$select=LogicalName,DisplayName"
            if ($one.DisplayName -and $one.DisplayName.LocalizedLabels -and @($one.DisplayName.LocalizedLabels).Count -gt 0) { $l = @($one.DisplayName.LocalizedLabels)[0].Label }
            if ([string]::IsNullOrWhiteSpace($l)) { Start-Sleep -Seconds 2 }
        }
    }
    if ([string]::IsNullOrWhiteSpace($l)) { throw "$($meta.Table).$Col has no display label in dev metadata" }
    $script:LabelCache["$($meta.Table).$Col"] = $l
    return $l
}
function Get-ClassId($meta, [string]$Col) {
    $a = $meta.Attrs[$Col]
    switch ($a.AttributeType) {
        'String' { return '{4273EDBD-AC1D-40D3-9FB2-095C621B552D}' }
        'Memo' { return '{E0DECE4B-6FC8-4A8F-A065-082708572369}' }
        { $_ -in 'Lookup', 'Owner', 'Customer' } { return '{270BD3DB-D9AF-4782-9025-509E298DEC0A}' }
        { $_ -in 'Picklist', 'State' } { return '{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}' }
        'Status' { return '{5D68B988-0661-4DB2-BC3E-17598AD3BE6C}' }
        'Boolean' { return '{67FAC785-CD58-4F9F-ABB3-4B7DDC6ED5ED}' }
        'DateTime' { return '{5B773807-9FB2-42DB-97C3-7A91EFF8ADFF}' }
        'Money' { return '{533B9E00-756B-4312-95A0-DC888637AC78}' }
        { $_ -in 'Decimal', 'Double' } { return '{C3EFE0C3-0EC6-42BE-8349-CBD9079DFD8E}' }
        'Integer' { return '{C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F}' }
        'Virtual' {
            if ($a.AttributeTypeName.Value -eq 'MultiSelectPicklistType') { return '{4AA28AB7-9C13-4F57-A73D-AD894D048B5F}' }
            throw "$($meta.Table).$Col is a virtual column of type $($a.AttributeTypeName.Value): no control class known"
        }
        default { throw "$($meta.Table).$Col has attribute type $($a.AttributeType): no control class known" }
    }
}

function New-CellXml($meta, [string]$Col, [bool]$Disabled, [string]$Seed, [bool]$Wide) {
    $label = Esc (Get-Label $meta $Col)
    $cls = Get-ClassId $meta $Col
    $dis = if ($Disabled) { ' disabled="true"' } else { '' }
    $span = if ($Wide) { ' colspan="2" rowspan="3"' } else { '' }
    return "<cell id=""$(Get-DetGuid "$Seed/cell/$Col")""$span><labels><label description=""$label"" languagecode=""1033"" /></labels><control id=""$Col"" classid=""$cls"" datafieldname=""$Col""$dis /></cell>"
}

function New-SectionXml($meta, $Section, [bool]$AllReadOnly, [string[]]$ForceReadOnly, [string]$SectionName) {
    $seed = "$($meta.Table)/$SectionName"
    $rows = [System.Text.StringBuilder]::new()
    $pending = $null
    foreach ($col in $Section.Cols) {
        if ($col -eq 'ownerid' -and -not $meta.Attrs.ContainsKey('ownerid')) { continue }   # organization-owned table (no owner column)
        [void](Get-Label $meta $col)   # fails with a clear message when a WP1 column is missing in dev
        $ro = $AllReadOnly -or ($Section.ContainsKey('ReadOnly') -and ($Section.ReadOnly -contains $col)) -or ($ForceReadOnly -contains $col)
        $wide = ($meta.Attrs[$col].AttributeType -eq 'Memo')
        if ($wide) {
            if ($pending) { [void]$rows.Append("<row>$pending</row>"); $pending = $null }
            [void]$rows.Append("<row>$(New-CellXml $meta $col $ro $seed $true)</row>")
            continue
        }
        $cell = New-CellXml $meta $col $ro $seed $false
        if ($null -eq $pending) { $pending = $cell } else { [void]$rows.Append("<row>$pending$cell</row>"); $pending = $null }
    }
    if ($pending) { [void]$rows.Append("<row>$pending</row>") }
    $label = Esc $Section.Label
    return "<section name=""$SectionName"" id=""$(Get-DetGuid "$seed/section")"" IsUserDefined=""1"" showlabel=""true"" showbar=""false"" columns=""11"" celllabelposition=""Top"" labelwidth=""115"" celllabelalignment=""Left"" layout=""varwidth""><labels><label description=""$label"" languagecode=""1033"" /></labels><rows>$($rows.ToString())</rows></section>"
}

function Get-ActiveViewId([string]$Table, [string]$Name) {
    $q = if ($Name) { "name eq '$Name' and querytype eq 0" } else { "querytype eq 0 and isdefault eq true" }
    $r = Invoke-Dv -Path "savedqueries?`$select=savedqueryid,name&`$filter=returnedtypecode eq '$Table' and $q"
    $rows = @($r.value)
    if ($rows.Count -eq 0) { throw "No view '$Name' for $Table" }
    return $rows[0].savedqueryid
}

function New-SubgridSectionXml($Table, $Sub) {
    $seed = "$Table/sub/$($Sub.Name)"
    $viewId = (Get-ActiveViewId $Sub.Target $Sub["ViewName"])
    $label = Esc $Sub.Label
    return "<section name=""section_sub_$($Sub.Name)"" id=""$(Get-DetGuid "$seed/section")"" IsUserDefined=""1"" showlabel=""true"" showbar=""false"" columns=""1"" celllabelposition=""Top"" layout=""varwidth""><labels><label description=""$label"" languagecode=""1033"" /></labels><rows><row><cell id=""$(Get-DetGuid "$seed/cell")"" rowspan=""6"" colspan=""1"" showlabel=""false""><labels><label description=""$label"" languagecode=""1033"" /></labels><control indicationOfSubgrid=""true"" id=""subgrid_$($Sub.Name)"" classid=""{E7A81278-8635-4D9E-8D4D-59480B391C5B}""><parameters><RecordsPerPage>10</RecordsPerPage><AutoExpand>Fixed</AutoExpand><EnableQuickFind>false</EnableQuickFind><EnableViewPicker>false</EnableViewPicker><EnableChartPicker>false</EnableChartPicker><ChartGridMode>Grid</ChartGridMode><RelationshipName>$($Sub.Relationship)</RelationshipName><TargetEntityType>$($Sub.Target)</TargetEntityType><ViewId>{$($viewId.ToUpperInvariant())}</ViewId><ViewIds>{$($viewId.ToUpperInvariant())}</ViewIds></parameters></control></cell></row></rows></section>"
}

function New-ReplaceFormXml($meta, $S) {
    $ro = [bool]($S.ContainsKey('AllReadOnly') -and $S.AllReadOnly)
    $secs = [System.Text.StringBuilder]::new()
    foreach ($sec in $S.Sections) { [void]$secs.Append((New-SectionXml $meta $sec $ro @() "section_$($sec.Name)")) }
    if ($S.ContainsKey('Subgrids')) { foreach ($sub in $S.Subgrids) { [void]$secs.Append((New-SubgridSectionXml $meta.Table $sub)) } }
    return "<form><tabs><tab verticallayout=""true"" id=""$(Get-DetGuid "$($meta.Table)/tab")"" name=""tab_general"" IsUserDefined=""1"" expanded=""true"" showlabel=""true""><labels><label description=""General"" languagecode=""1033"" /></labels><columns><column width=""100%""><sections>$($secs.ToString())</sections></column></columns></tab></tabs></form>"
}

function Get-MainForm([string]$Table, [string]$Name) {
    $r = Invoke-Dv -Path "systemforms?`$select=formid,name,formxml&`$filter=objecttypecode eq '$Table' and type eq 2 and name eq '$Name'"
    $rows = @($r.value)
    if ($rows.Count -ne 1) { throw "Expected exactly one main form '$Name' on $Table, found $($rows.Count)" }
    return $rows[0]
}

function Write-Form([string]$Table, $Form, [string]$NewXml, [string]$What) {
    [void]$EditedIds.Add($Form.formid.ToLowerInvariant())
    if ($WhatIfPreference) { $Changes.Add("WOULD $What ($Table form $($Form.formid))"); Write-Host "  WOULD  $What" -ForegroundColor Yellow; return }
    Invoke-Dv -Path "systemforms($($Form.formid))" -Method PATCH -Body @{ formxml = $NewXml } | Out-Null
    [void]$Touched.Add($Table)
    $Changes.Add("$What ($Table form $($Form.formid))"); Write-Host "  CHANGED  $What" -ForegroundColor Green
}

function Sync-Form([string]$Table, $S) {
    $meta = $script:Metas[$Table]
    $form = Get-MainForm $Table $S.FormName
    $Evidence["form:$Table"] = $form.formid
    if ($S.Form -eq 'Replace') {
        $want = New-ReplaceFormXml $meta $S
        $cur = [xml]$form.formxml; $wantDoc = [xml]$want
        if ($cur.OuterXml -eq $wantDoc.OuterXml) { $Unchanged.Add("$Table form"); Write-Host "  unchanged  $Table main form" -ForegroundColor DarkGray; return }
        Write-Form $Table $form $want "main form '$($S.FormName)' rebuilt"
        return
    }
    # Section mode: only section_ebilling is added / refreshed; read-only flags on listed existing controls.
    $doc = [xml]$form.formxml
    $force = if ($S.ContainsKey('ForceReadOnly')) { @($S.ForceReadOnly) } else { @() }
    $sec = $S.Sections[0]
    $secName = "section_$($sec.Name)"
    $wantSec = ([xml](New-SectionXml $meta $sec $false $force $secName)).DocumentElement
    $oldSec = $doc.SelectSingleNode("//section[@name='$secName']")
    $dirty = $false
    if ($oldSec) {
        if ($oldSec.OuterXml -ne $wantSec.OuterXml) { [void]$oldSec.ParentNode.ReplaceChild($doc.ImportNode($wantSec, $true), $oldSec); $dirty = $true }
    } else {
        $host_ = $doc.SelectSingleNode('//tabs/tab[1]/columns/column[1]/sections')
        if (-not $host_) { throw "$Table '$($S.FormName)': no first tab / column / sections node" }
        [void]$host_.AppendChild($doc.ImportNode($wantSec, $true)); $dirty = $true
    }
    # Existing controls (outside section_ebilling) that must be read-only.
    foreach ($col in $force) {
        foreach ($c in @($doc.SelectNodes("//control[@datafieldname='$col']"))) {
            if ($c.ParentNode.ParentNode.ParentNode.ParentNode.GetAttribute('name') -eq $secName) { continue }
            if ($c.GetAttribute('disabled') -ne 'true') { $c.SetAttribute('disabled', 'true'); $dirty = $true }
        }
    }
    if (-not $dirty) { $Unchanged.Add("$Table form"); Write-Host "  unchanged  $Table '$($S.FormName)'" -ForegroundColor DarkGray; return }
    Write-Form $Table $form $doc.OuterXml "section '$secName' / read-only controls on '$($S.FormName)'"
}

# --- views -------------------------------------------------------------------------------------------------------
function New-ViewXml($meta, [string[]]$Cols, [string]$OrderBy) {
    $order = if ($OrderBy) { $OrderBy } else { $meta.Prim }
    $attrs = ($Cols | ForEach-Object { "<attribute name=""$_"" />" }) -join ''
    $fetch = "<fetch version=""1.0"" mapping=""logical""><entity name=""$($meta.Table)""><attribute name=""$($meta.Pk)"" />$attrs<order attribute=""$order"" descending=""false"" /><filter type=""and""><condition attribute=""statecode"" operator=""eq"" value=""0"" /></filter></entity></fetch>"
    $cells = ($Cols | ForEach-Object { $w = if ($_ -eq $order -or $_ -eq $meta.Prim) { 220 } else { 150 }; "<cell name=""$_"" width=""$w"" />" }) -join ''
    $layout = "<grid name=""resultset"" object=""$($meta.Otc)"" jump=""$order"" select=""1"" icon=""1"" preview=""1""><row name=""result"" id=""$($meta.Pk)"">$cells</row></grid>"
    return @{ Fetch = $fetch; Layout = $layout }
}
function Get-CellNames([string]$LayoutXml) { return @(([xml]$LayoutXml).SelectNodes('//cell') | ForEach-Object { $_.GetAttribute('name') }) }

function Sync-ActiveView([string]$Table, $S) {
    if (-not $S.ContainsKey('View')) { return }
    $meta = $script:Metas[$Table]
    foreach ($c in $S.View) { [void](Get-Label $meta $c) }
    $id = Get-ActiveViewId $Table $null
    $cur = Invoke-Dv -Path "savedqueries($id)?`$select=name,fetchxml,layoutxml"
    $Evidence["view:$Table"] = "$id ($($cur.name))"
    $order = if ($S.ContainsKey('OrderBy')) { $S.OrderBy } else { '' }
    $want = New-ViewXml $meta $S.View $order
    $curCells = (Get-CellNames $cur.layoutxml) -join ','
    $wantCells = (Get-CellNames $want.Layout) -join ','
    if ($curCells -eq $wantCells) { $Unchanged.Add("$Table view"); Write-Host "  unchanged  $Table '$($cur.name)'" -ForegroundColor DarkGray; return }
    [void]$EditedIds.Add($id.ToLowerInvariant())
    if ($WhatIfPreference) { $Changes.Add("WOULD set columns of '$($cur.name)' ($id): $wantCells"); Write-Host "  WOULD  columns of '$($cur.name)'" -ForegroundColor Yellow; return }
    Invoke-Dv -Path "savedqueries($id)" -Method PATCH -Body @{ fetchxml = $want.Fetch; layoutxml = $want.Layout } | Out-Null
    [void]$Touched.Add($Table)
    $Changes.Add("columns of '$($cur.name)' ($id): $wantCells"); Write-Host "  CHANGED  columns of '$($cur.name)'" -ForegroundColor Green
}

function Sync-InvoicesToReview {
    $table = 'sprk_invoice'
    $meta = $script:Metas[$table]
    $name = 'Invoices to review'
    $cols = 'sprk_invoicenumber', 'sprk_name', 'sprk_vendororg', 'sprk_invoicedate', 'sprk_totalamount', 'statuscode'
    $attrs = ($cols | ForEach-Object { "<attribute name=""$_"" />" }) -join ''
    # In Review (statuscode 1, statecode 0) invoices that have a Pending approval (100000000) naming the CURRENT USER's contact as
    # an eligible approver (the intersect of the N:N, then contact.sprk_systemuser = current user).
    $fetch = "<fetch version=""1.0"" mapping=""logical"" distinct=""true""><entity name=""sprk_invoice""><attribute name=""sprk_invoiceid"" />$attrs<order attribute=""sprk_invoicedate"" descending=""true"" /><filter type=""and""><condition attribute=""statecode"" operator=""eq"" value=""0"" /><condition attribute=""statuscode"" operator=""eq"" value=""1"" /></filter><link-entity name=""sprk_invoiceapproval"" from=""sprk_invoice"" to=""sprk_invoiceid"" link-type=""inner"" alias=""ap""><filter type=""and""><condition attribute=""statecode"" operator=""eq"" value=""0"" /><condition attribute=""sprk_approvalstatus"" operator=""eq"" value=""100000000"" /></filter><link-entity name=""sprk_invoiceapproval_eligibleapprover_contact"" from=""sprk_invoiceapprovalid"" to=""sprk_invoiceapprovalid"" link-type=""inner"" visible=""false"" intersect=""true""><link-entity name=""contact"" from=""contactid"" to=""contactid"" link-type=""inner"" alias=""approvercontact""><filter type=""and""><condition attribute=""sprk_systemuser"" operator=""eq-userid"" /></filter></link-entity></link-entity></link-entity></entity></fetch>"
    $cells = ($cols | ForEach-Object { $w = if ($_ -eq 'sprk_name') { 220 } else { 150 }; "<cell name=""$_"" width=""$w"" />" }) -join ''
    $layout = "<grid name=""resultset"" object=""$($meta.Otc)"" jump=""sprk_name"" select=""1"" icon=""1"" preview=""1""><row name=""result"" id=""sprk_invoiceid"">$cells</row></grid>"
    $r = Invoke-Dv -Path "savedqueries?`$select=savedqueryid,fetchxml,layoutxml&`$filter=returnedtypecode eq '$table' and name eq '$name' and querytype eq 0"
    $rows = @($r.value)
    $desc = 'In Review invoices that wait for me: a Pending approval lists my contact as an eligible approver (FR-35).'
    if ($rows.Count -eq 0) {
        if ($WhatIfPreference) { $Changes.Add("WOULD create view '$name'"); Write-Host "  WOULD  create view '$name'" -ForegroundColor Yellow; return }
        $body = @{ name = $name; description = $desc; returnedtypecode = $table; querytype = 0; isdefault = $false; fetchxml = $fetch; layoutxml = $layout }
        Invoke-Dv -Path 'savedqueries' -Method POST -Body $body -ExtraHeaders @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        $new = @((Invoke-Dv -Path "savedqueries?`$select=savedqueryid&`$filter=returnedtypecode eq '$table' and name eq '$name' and querytype eq 0").value)[0]
        $Evidence['view:Invoices to review'] = $new.savedqueryid
        [void]$Touched.Add($table)
        $Changes.Add("created view '$name' ($($new.savedqueryid))"); Write-Host "  CREATED  view '$name' $($new.savedqueryid)" -ForegroundColor Green
        return
    }
    $id = $rows[0].savedqueryid
    $Evidence['view:Invoices to review'] = $id
    $normCur = ([xml]$rows[0].fetchxml).SelectSingleNode('//link-entity').OuterXml
    $normWant = ([xml]$fetch).SelectSingleNode('//link-entity').OuterXml
    if ($normCur -eq $normWant -and (Get-CellNames $rows[0].layoutxml) -join ',' -eq (Get-CellNames $layout) -join ',') { $Unchanged.Add("view $name"); Write-Host "  unchanged  view '$name'" -ForegroundColor DarkGray; return }
    [void]$EditedIds.Add($id.ToLowerInvariant())
    if ($WhatIfPreference) { $Changes.Add("WOULD update view '$name'"); Write-Host "  WOULD  update view '$name'" -ForegroundColor Yellow; return }
    Invoke-Dv -Path "savedqueries($id)" -Method PATCH -Body @{ fetchxml = $fetch; layoutxml = $layout; description = $desc } | Out-Null
    [void]$Touched.Add($table)
    $Changes.Add("updated view '$name' ($id)"); Write-Host "  CHANGED  view '$name'" -ForegroundColor Green
}

# --- sitemaps ----------------------------------------------------------------------------------------------------
function New-SubAreaXml([string]$Id, [string]$Entity) {
    return "<SubArea Id=""$Id"" Icon=""/_imgs/imagestrips/transparent_spacer.gif"" Entity=""$Entity"" Client=""All,Outlook,OutlookLaptopClient,OutlookWorkstationClient,Web"" AvailableOffline=""true"" PassParams=""false"" Sku=""All,OnPremise,Live,SPLA"" />"
}
function Sync-SiteMap([string]$UniqueName, [scriptblock]$Edit) {
    $sm = @((Invoke-Dv -Path "sitemaps?`$select=sitemapid,sitemapxml&`$filter=sitemapnameunique eq '$UniqueName'").value)
    if ($sm.Count -ne 1) { throw "Expected one sitemap $UniqueName, found $($sm.Count)" }
    $doc = [xml]$sm[0].sitemapxml
    $dirty = [bool](& $Edit $doc)
    $Evidence["sitemap:$UniqueName"] = $sm[0].sitemapid
    if (-not $dirty) { $Unchanged.Add("sitemap $UniqueName"); Write-Host "  unchanged  sitemap $UniqueName" -ForegroundColor DarkGray; return }
    if ($WhatIfPreference) { $Changes.Add("WOULD update sitemap $UniqueName"); Write-Host "  WOULD  update sitemap $UniqueName" -ForegroundColor Yellow; return }
    Invoke-Dv -Path "sitemaps($($sm[0].sitemapid))" -Method PATCH -Body @{ sitemapxml = $doc.OuterXml } | Out-Null
    $script:SiteMapsTouched += $sm[0].sitemapid
    $Changes.Add("updated sitemap $UniqueName ($($sm[0].sitemapid))"); Write-Host "  CHANGED  sitemap $UniqueName" -ForegroundColor Green
}
function Add-SubArea($doc, $parentXPath, [string]$Id, [string]$Entity, $afterXPath) {
    if ($doc.SelectSingleNode("//SubArea[@Entity='$Entity']")) { return $false }
    $parent = $doc.SelectSingleNode($parentXPath)
    $frag = $doc.CreateDocumentFragment(); $frag.InnerXml = New-SubAreaXml $Id $Entity
    $after = if ($afterXPath) { $doc.SelectSingleNode($afterXPath) } else { $null }
    if ($after) { [void]$parent.InsertAfter($frag, $after) } else { [void]$parent.AppendChild($frag) }
    return $true
}

# A sitemap entry only shows when the table is a component of the app module (type 1). AddAppComponents can answer 204 and add
# nothing (project CLAUDE.md section 6), so the result is read back.
function Sync-AppTables([string]$AppUniqueName, [string[]]$Tables) {
    $app = @((Invoke-Dv -Path "appmodules?`$select=appmoduleid,appmoduleidunique&`$filter=uniquename eq '$AppUniqueName'").value)[0]
    $have = @((Invoke-Dv -Path "appmodulecomponents?`$select=objectid&`$filter=_appmoduleidunique_value eq $($app.appmoduleidunique) and componenttype eq 1").value | ForEach-Object { "$($_.objectid)" })
    foreach ($t in $Tables) {
        $id = (Invoke-Dv -Path "EntityDefinitions(LogicalName='$t')?`$select=MetadataId").MetadataId
        if ($have -contains $id) { $Unchanged.Add("app $AppUniqueName has $t"); Write-Host "  unchanged  $AppUniqueName has $t" -ForegroundColor DarkGray; continue }
        if ($WhatIfPreference) { $Changes.Add("WOULD add $t to app $AppUniqueName"); Write-Host "  WOULD  add $t to app $AppUniqueName" -ForegroundColor Yellow; continue }
        Invoke-Dv -Path 'AddAppComponents' -Method POST -Body @{ AppId = $app.appmoduleid; Components = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.entity'; entityid = $id }) } | Out-Null
        $after = @((Invoke-Dv -Path "appmodulecomponents?`$select=objectid&`$filter=_appmoduleidunique_value eq $($app.appmoduleidunique) and componenttype eq 1").value | ForEach-Object { "$($_.objectid)" })
        if ($after -notcontains $id) {
            # Seen on spaarkedev1 2026-10-16 for every table; appmodulecomponent has no Create message either. Only the app designer
            # (or a solution import) adds a table to an app, so this is a manual step reported at the end, not a failure.
            $script:AppTablesMissing += "$t -> $AppUniqueName"
            Write-Host "  WARN  AddAppComponents accepted $t but $AppUniqueName does not list it: add the table in the app designer (Save + Publish)" -ForegroundColor Yellow
            continue
        }
        $script:AppsTouched += $app.appmoduleid
        $Changes.Add("added $t to app $AppUniqueName"); Write-Host "  CHANGED  added $t to app $AppUniqueName" -ForegroundColor Green
    }
}

# ------------------------------------------------------------------------------------------------------------------
# Run
# ------------------------------------------------------------------------------------------------------------------
$script:SiteMapsTouched = @()
$script:AppsTouched = @()
$script:AppTablesMissing = @()
$script:LabelCache = @{}
$script:Metas = @{}
Write-Host "eBilling task 029 - forms, views, navigation   $EnvironmentUrl   solution $SolutionUniqueName   $(if ($WhatIfPreference) { 'WHATIF (no writes)' } else { 'APPLY' })" -ForegroundColor Cyan

Write-Host "`n0. Metadata and pending collateral (read only)"
foreach ($t in $Spec.Keys) { $script:Metas[$t] = Get-TableMeta $t }
$collateral = [System.Collections.Generic.List[string]]::new()
foreach ($t in $Spec.Keys) {
    foreach ($c in @(Get-EntityPublishCollateral -Context $Ctx -Entity $t)) { $collateral.Add("$t | $c") }
}
if ($collateral.Count -gt 0) {
    Write-Host "  pending (unpublished) forms / views of the touched entities:" -ForegroundColor Yellow
    $collateral | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}
$Evidence['collateralBefore'] = @($collateral)

Write-Host "`n1. Forms"
foreach ($t in $Spec.Keys) { Write-Host " $t"; Sync-Form $t $Spec[$t] }

if ($FormsOnly) {
    Write-Host "`n2-3. Views, navigation and app tables skipped (-FormsOnly)"
} else {
Write-Host "`n2. Views"
foreach ($t in $Spec.Keys) { Sync-ActiveView $t $Spec[$t] }
Sync-InvoicesToReview

Write-Host "`n3. Navigation"
Sync-SiteMap 'sprk_SpaarkeAdministration' {
    param($doc)
    $dirty = $false
    $siteMap = $doc.SelectSingleNode('/SiteMap')
    if (-not $doc.SelectSingleNode("//Area[@Id='area_ebilling']")) {
        $frag = $doc.CreateDocumentFragment()
        $frag.InnerXml = '<Area Id="area_ebilling" ResourceId="SitemapDesigner.NewTitle" DescriptionResourceId="SitemapDesigner.NewTitle" ShowGroups="true" IntroducedVersion="7.0.0.0"><Titles><Title LCID="1033" Title="eBilling" /></Titles><Group Id="group_ebilling" ResourceId="SitemapDesigner.NewGroup" DescriptionResourceId="SitemapDesigner.NewGroup" IntroducedVersion="7.0.0.0" IsProfile="false" ToolTipResourseId="SitemapDesigner.Unknown"><Titles><Title LCID="1033" Title="Billing administration" /></Titles></Group></Area>'
        [void]$siteMap.AppendChild($frag); $dirty = $true
    }
    foreach ($e in @(@('subarea_eb_setting', 'sprk_ebillingsetting'), @('subarea_eb_timekeeper', 'sprk_timekeeper'), @('subarea_eb_ratecard', 'sprk_ratecard'), @('subarea_eb_codeset', 'sprk_billingcodeset'), @('subarea_eb_ruleset', 'sprk_ruleset'))) {
        # sprk_timekeeper is also on the Matter Management sitemap; the entity check is per sitemap document.
        if (Add-SubArea $doc "//Area[@Id='area_ebilling']/Group[@Id='group_ebilling']" $e[0] $e[1] $null) { $dirty = $true }
    }
    return $dirty
}
Sync-SiteMap 'sprk_MatterManagement' {
    param($doc)
    $dirty = $false
    $inv = "//SubArea[@Entity='sprk_invoice']"
    if (-not $doc.SelectSingleNode($inv)) { throw 'sprk_MatterManagement has no sprk_invoice subarea' }
    $group = $doc.SelectSingleNode($inv).ParentNode.GetAttribute('Id')
    if (Add-SubArea $doc "//Group[@Id='$group']" 'subarea_eb_submission' 'sprk_invoicesubmission' $inv) { $dirty = $true }
    if (Add-SubArea $doc "//Group[@Id='$group']" 'subarea_eb_export' 'sprk_billingexport' "//SubArea[@Entity='sprk_invoicesubmission']") { $dirty = $true }
    return $dirty
}

Sync-AppTables 'sprk_SpaarkeAdministration' @('sprk_ebillingsetting', 'sprk_timekeeper', 'sprk_ratecard', 'sprk_billingcodeset', 'sprk_ruleset')
Sync-AppTables 'sprk_MatterManagement' @('sprk_invoicesubmission', 'sprk_billingexport')
}

Write-Host "`n4. Publish (entity-scoped; never PublishAllXml)"
$pendingOthers = @($collateral | Where-Object {
        $id = ($_ -replace '^.*\(([0-9a-fA-F-]{36})\)\s*$', '$1').ToLowerInvariant()
        -not $EditedIds.Contains($id)
    })
if ($Touched.Count -eq 0 -and $script:SiteMapsTouched.Count -eq 0 -and $script:AppsTouched.Count -eq 0) {
    Write-Host '  nothing changed - no publish' -ForegroundColor DarkGray
} elseif ($WhatIfPreference) {
    Write-Host "  WhatIf: publish skipped (would publish: $(@($Touched) -join ', '))"
} else {
    if ($pendingOthers.Count -gt 0 -and -not $AllowPendingCollateral) {
        Write-Host '  REFUSING to publish: pending forms / views this script does not edit would go live too:' -ForegroundColor Red
        $pendingOthers | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        Write-Host '  Re-run with -AllowPendingCollateral after the owner of those changes agrees.' -ForegroundColor Red
        exit 2
    }
    if ($pendingOthers.Count -gt 0) { Write-Host "  -AllowPendingCollateral: publishing $($pendingOthers.Count) other pending item(s) of the touched entities" -ForegroundColor Yellow }
    $apps = foreach ($u in 'sprk_SpaarkeAdministration', 'sprk_MatterManagement') { @((Invoke-Dv -Path "appmodules?`$select=appmoduleid&`$filter=uniquename eq '$u'").value)[0].appmoduleid }
    $ents = (@($Touched | Sort-Object) | ForEach-Object { "<entity>$_</entity>" }) -join ''
    $parts = ''
    if ($ents) { $parts += "<entities>$ents</entities>" }
    if ($script:SiteMapsTouched.Count -gt 0) {
        $parts += '<sitemaps>' + ((@($script:SiteMapsTouched) | ForEach-Object { "<sitemap>{$_}</sitemap>" }) -join '') + '</sitemaps>'
    }
    if ($script:SiteMapsTouched.Count -gt 0 -or $script:AppsTouched.Count -gt 0) {
        $parts += '<appmodules>' + ((@($apps) | ForEach-Object { "<appmodule>{$_}</appmodule>" }) -join '') + '</appmodules>'
    }
    Invoke-PublishXml -Context $Ctx -ParameterXml "<importexportxml>$parts</importexportxml>"
    Write-Host "  published: entities $(@($Touched | Sort-Object) -join ', '); sitemaps $($script:SiteMapsTouched.Count); app modules $(if ($script:SiteMapsTouched.Count -gt 0 -or $script:AppsTouched.Count -gt 0) { 2 } else { 0 })" -ForegroundColor Green
    $left = @()
    foreach ($t in $Touched) { $left += @(Get-EntityPublishCollateral -Context $Ctx -Entity $t | ForEach-Object { "$t | $_" }) }
    if ($left.Count -gt 0) { Write-Host '  WARN still unpublished after publish:' -ForegroundColor Yellow; $left | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow } }
    else { Write-Host '  read-back: no pending form / view left on the touched entities' -ForegroundColor Green }
}

Write-Host "`n================ Summary ================"
Write-Host "changed  : $($Changes.Count)"
$Changes | ForEach-Object { Write-Host "   $_" }
Write-Host "unchanged: $($Unchanged.Count)"
Write-Host "`nEvidence (ids):"
foreach ($k in $Evidence.Keys) { Write-Host "   $k = $($Evidence[$k] -join '; ')" }
if ($script:AppTablesMissing.Count -gt 0) {
    Write-Host "`nMANUAL STEP (app designer, make.powerapps.com): add these tables to the app, then Save and Publish. Until then the sitemap entry opens nothing:" -ForegroundColor Yellow
    $script:AppTablesMissing | ForEach-Object { Write-Host "   $_" -ForegroundColor Yellow }
}
if ($WhatIfPreference) { Write-Host 'WhatIf run: nothing was written.' }
