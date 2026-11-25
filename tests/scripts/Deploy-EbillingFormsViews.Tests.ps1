# Pester 5. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Deploy-EbillingFormsViews.Tests.ps1
# ebilling-r1 task 116. Deploy-EbillingFormsViews.ps1 rewrites the main forms of sprk_ruleset and sprk_ratecard from its $Spec table. Since
# EB-D-148 a Procedure's document is the Procedure Version's Stored File, so neither form may carry sprk_sourcedocument any more (the column
# is removed later by Remove-EbillingSourceDocumentColumns.ps1, which refuses while a form still mentions it). The test reads the script's
# own $Spec out of its syntax tree: no environment, no network.

BeforeAll {
    $path = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1'
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }
    $assign = $ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $_.Left.VariablePath.UserPath -eq 'Spec'
    } | Select-Object -First 1
    if (-not $assign) { throw 'The $Spec table was not found in the script.' }
    $script:SpecText = $assign.Extent.Text
    $script:Spec = $null
    Invoke-Expression $script:SpecText
    $script:Spec = $Spec
}

Describe 'the forms spec no longer carries sprk_sourcedocument (116)' {
    It 'has no sprk_sourcedocument anywhere in the spec (forms or views)' {
        $script:SpecText | Should -Not -Match 'sprk_sourcedocument'
    }

    It '<table> main form has no sprk_sourcedocument control' -ForEach @(@{ table = 'sprk_ruleset' }, @{ table = 'sprk_ratecard' }) {
        $cols = @($script:Spec[$table].Sections | ForEach-Object { $_.Cols })
        $cols | Should -Not -Contain 'sprk_sourcedocument'
        $cols | Should -Contain 'sprk_name'
        $cols | Should -Contain 'ownerid'
    }

    It 'sprk_ruleset keeps its general section columns' {
        $general = @($script:Spec['sprk_ruleset'].Sections | Where-Object { $_.Name -eq 'general' })[0].Cols
        $general | Should -Be @('sprk_name', 'sprk_domain', 'sprk_scope', 'sprk_firm', 'sprk_visibletofirms', 'sprk_effectivefrom', 'sprk_effectiveto', 'statuscode', 'ownerid')
    }

    It 'sprk_ratecard keeps its general section columns and the rates subgrid' {
        $general = @($script:Spec['sprk_ratecard'].Sections | Where-Object { $_.Name -eq 'general' })[0].Cols
        $general | Should -Be @('sprk_name', 'sprk_firm', 'sprk_effectivefrom', 'sprk_effectiveto', 'ownerid')
        $script:Spec['sprk_ratecard'].Subgrids[0].Target | Should -Be 'sprk_ratecardrate'
    }

    # ebilling-r1 task 193: the card's three core-record lookups are retired (Remove-EbillingRateCardCoreLookups.ps1), so the form and the
    # view must not name them: a re-run of this script would otherwise put them back on the form and fail on the missing columns.
    It 'sprk_ratecard form and view name none of the three retired core-record lookups (193)' {
        $general = @($script:Spec['sprk_ratecard'].Sections | Where-Object { $_.Name -eq 'general' })[0].Cols
        foreach ($retired in 'sprk_matter', 'sprk_project', 'sprk_workassignment') {
            $general | Should -Not -Contain $retired
            @($script:Spec['sprk_ratecard'].View) | Should -Not -Contain $retired
        }
        @($script:Spec['sprk_ratecard'].View) | Should -Be @('sprk_name', 'sprk_firm', 'sprk_effectivefrom', 'sprk_effectiveto')
    }
    It 'sprk_ruleset has no section bound to sprk_notconverted, which nothing writes since 116 (fix round 1)' {
        @($script:Spec['sprk_ruleset'].Sections | ForEach-Object { $_.Name }) | Should -Not -Contain 'notconverted'
        @($script:Spec['sprk_ruleset'].Sections | ForEach-Object { $_.Cols }) | Should -Not -Contain 'sprk_notconverted'
    }
}


# ebilling-r1 ISS-046 (#3001). In Section mode the script used to rebuild section_ebilling from its Cols list and REPLACE the section, so the
# qv_billingexport "Batch" quick view that task 151e added to the invoice main form (no script creates it) was deleted without warning on
# every re-run. Section mode now MERGES: the spec's field cells are added or updated, everything else in the section stays where it is.
# The tests load the script's own functions out of its syntax tree and drive Sync-Form against a fake Invoke-Dv: no environment, no network.
Describe 'Section mode keeps the cells the spec does not own (ISS-046)' {
    BeforeAll {
        $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1'
        $tokens = $null; $errors = $null
        $scriptAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }
        # Every top-level function of the script (the plumbing, Sync-Form), none of its run code.
        foreach ($fn in $scriptAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) { Invoke-Expression $fn.Extent.Text }

        # A fake Dataverse: one main form row; GET returns it, PATCH replaces its formxml and is counted.
        function Invoke-Dv {
            param([string]$Path, [string]$Method = 'GET', $Body, $ExtraHeaders)
            if ($Method -eq 'GET' -and $Path -like 'systemforms?*') { return [pscustomobject]@{ value = @($script:FakeForm) } }
            if ($Path -like 'systemforms(*)' -and $Method -eq 'PATCH') {
                $script:FakePatches += , $Body.formxml
                $script:FakeForm.formxml = $Body.formxml
                return $null
            }
            throw "fake Invoke-Dv: unexpected call $Method $Path"
        }

        $script:AttrTypes = @{
            sprk_matter = 'Lookup'; sprk_project = 'Lookup'; sprk_regardingworkassignment = 'Lookup'; sprk_supersedesinvoice = 'Lookup'; sprk_billingexport = 'Lookup'
            sprk_billingperiodstart = 'DateTime'; sprk_billingperiodend = 'DateTime'; sprk_invoiceduedate = 'DateTime'
            sprk_currency = 'String'; sprk_confidence = 'Decimal'; sprk_billingapproverrole = 'MultiSelectVirtual'
            sprk_feestotal = 'Money'; sprk_expensestotal = 'Money'; sprk_lineadjustmentstotal = 'Money'; sprk_invoiceadjustmentstotal = 'Money'; sprk_approvedtotal = 'Money'
        }
        function New-FakeInvoiceMeta([hashtable]$Labels = @{}) {
            $attrs = @{}
            foreach ($col in $script:Spec['sprk_invoice'].Sections[0].Cols) {
                $type = $script:AttrTypes[$col]
                $label = if ($Labels.ContainsKey($col)) { $Labels[$col] } else { "Label of $col" }
                $a = @{ LogicalName = $col; AttributeType = $(if ($type -eq 'MultiSelectVirtual') { 'Virtual' } else { $type }); DisplayName = @{ LocalizedLabels = @(@{ Label = $label }) } }
                if ($type -eq 'MultiSelectVirtual') { $a.AttributeTypeName = @{ Value = 'MultiSelectPicklistType' } }
                $attrs[$col] = [pscustomobject]$a
            }
            return [pscustomobject]@{ Table = 'sprk_invoice'; Otc = 10001; Pk = 'sprk_invoiceid'; Prim = 'sprk_name'; Attrs = $attrs }
        }

        # The Batch quick-view row exactly as the SpaarkeMaster source has it (task 151e). Its control is bound to the same column as the
        # plain Export Batch lookup, which is why an "is the field present" check by datafieldname alone must not match it.
        $script:QuickViewRow = '<row><cell id="{7E4F1B2A-3C5D-4E8F-9A06-B1D2C3E4F5A7}"><labels><label description="Batch" languagecode="1033" /></labels><control id="qv_billingexport" classid="{5C5600E0-1D6E-4205-A272-BE80DA87FD42}" datafieldname="sprk_billingexport" disabled="false" uniqueid="{3B6A9C1E-72D4-4F08-8E15-A4C7D9E2B610}"><parameters><QuickForms>&lt;QuickFormIds&gt;&lt;QuickFormId entityname="sprk_billingexport"&gt;2C568B46-E9DD-4774-BCFA-00C436CB4EBA&lt;/QuickFormId&gt;&lt;/QuickFormIds&gt;</QuickForms><ControlMode>Edit</ControlMode></parameters></control></cell></row>'

        # An invoice main form: a first section, then section_ebilling as the script writes it (with the quick-view row inserted after the
        # row that holds Export Batch, as in the source), then a Files section.
        function New-InvoiceFormXml($meta, [switch]$WithQuickView, [scriptblock]$Mutate) {
            $S = $script:Spec['sprk_invoice']
            $force = @($S.ForceReadOnly)
            $sec = New-SectionXml $meta $S.Sections[0] $false $force 'section_ebilling'
            $doc = [xml]"<form><tabs><tab name=""tab_1""><columns><column width=""100%""><sections><section name=""section_summary""><rows><row><cell id=""{AAAAAAAA-0000-0000-0000-000000000001}""><control id=""sprk_name"" datafieldname=""sprk_name"" classid=""{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"" /></cell></row></rows></section>$sec<section name=""section_files""><rows><row><cell id=""{AAAAAAAA-0000-0000-0000-000000000002}""><control id=""files"" classid=""{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}"" /></cell></row></rows></section></sections></column></columns></tab></tabs></form>"
            if ($WithQuickView) {
                $export = $doc.SelectSingleNode("//section[@name='section_ebilling']//cell[control[@id='sprk_billingexport']]")
                $frag = $doc.CreateDocumentFragment(); $frag.InnerXml = $script:QuickViewRow
                [void]$export.ParentNode.ParentNode.InsertAfter($frag, $export.ParentNode)
            }
            if ($Mutate) { & $Mutate $doc }
            return $doc.OuterXml
        }

        function Invoke-InvoiceSectionRun($meta, [string]$FormXml) {
            $script:FakeForm = [pscustomobject]@{ formid = '93aa1c69-0406-f111-8406-7c1e525abd8b'; name = 'Invoice main form'; formxml = $FormXml }
            $script:FakePatches = @()
            $script:Metas = @{ sprk_invoice = $meta }
            $script:LabelCache = @{}
            $script:EditedIds = [System.Collections.Generic.HashSet[string]]::new()
            $script:Touched = [System.Collections.Generic.HashSet[string]]::new()
            $script:Changes = [System.Collections.Generic.List[string]]::new()
            $script:Unchanged = [System.Collections.Generic.List[string]]::new()
            $script:Evidence = [ordered]@{}
            Sync-Form 'sprk_invoice' $script:Spec['sprk_invoice'] 6>$null
        }
        function Get-EbillingSection([string]$Xml) { return ([xml]$Xml).SelectSingleNode("//section[@name='section_ebilling']") }
        function Get-FieldIds($Section) { return @($Section.SelectNodes('rows/row/cell/control') | ForEach-Object { $_.GetAttribute('id') }) }
        function Remove-Cell($Doc, [string]$ControlId) { $c = $Doc.SelectSingleNode("//cell[control[@id='$ControlId']]"); [void]$c.ParentNode.RemoveChild($c) }
    }

    BeforeEach { $script:LabelCache = @{} }

    It 'keeps the qv_billingexport quick view, in its own row and in its place, after a rebuild that has other work to do' {
        $meta = New-FakeInvoiceMeta
        # A spec cell is missing from the live form, so the run must write: the quick view must survive that write.
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate { param($d) Remove-Cell $d 'sprk_currency' }
        $rowsBefore = @((Get-EbillingSection $xml).SelectNodes('rows/row'))
        $qvIndex = (0..($rowsBefore.Count - 1) | Where-Object { $rowsBefore[$_].SelectSingleNode("cell/control[@id='qv_billingexport']") })[0]

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $rowsAfter = @((Get-EbillingSection $script:FakeForm.formxml).SelectNodes('rows/row'))
        @($rowsAfter | Where-Object { $_.SelectSingleNode("cell/control[@id='qv_billingexport']") }).Count | Should -Be 1
        # Same position, and byte for byte the same row (its parameters included).
        $rowsAfter[$qvIndex].SelectSingleNode("cell/control[@id='qv_billingexport']") | Should -Not -BeNullOrEmpty
        $rowsAfter[$qvIndex].OuterXml | Should -BeExactly $rowsBefore[$qvIndex].OuterXml
        # Every row that was there is still there, in order, at the same index.
        for ($i = 0; $i -lt $rowsBefore.Count; $i++) {
            $rowsAfter[$i].OuterXml | Should -BeExactly $rowsBefore[$i].OuterXml -Because "row $i"
        }
    }

    It 'writes nothing when the form already has the quick view and every spec cell (a run with no spec change)' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 0
        $script:Changes.Count | Should -Be 0
        $script:Unchanged | Should -Contain 'sprk_invoice form'
        $script:Touched.Count | Should -Be 0
    }

    It 'a run that writes is followed by a run that writes nothing, and the quick view is still there' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate { param($d) $d.SelectSingleNode("//control[@id='sprk_feestotal']").SetAttribute('disabled', 'false') }

        Invoke-InvoiceSectionRun $meta $xml
        $script:FakePatches.Count | Should -Be 1
        $written = $script:FakeForm.formxml

        Invoke-InvoiceSectionRun $meta $written
        $script:FakePatches.Count | Should -Be 0
        @((Get-EbillingSection $written).SelectNodes("rows/row/cell/control[@id='qv_billingexport']")).Count | Should -Be 1
    }

    It 'still adds a field cell the spec owns when it is missing' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate { param($d) Remove-Cell $d 'sprk_billingperiodstart' }
        (Get-FieldIds (Get-EbillingSection $xml)) | Should -Not -Contain 'sprk_billingperiodstart'

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $sec = Get-EbillingSection $script:FakeForm.formxml
        @((Get-FieldIds $sec) | Where-Object { $_ -eq 'sprk_billingperiodstart' }).Count | Should -Be 1
        $sec.SelectSingleNode("rows/row/cell/control[@id='sprk_billingperiodstart']").GetAttribute('datafieldname') | Should -Be 'sprk_billingperiodstart'
    }

    It 'adds the Export Batch lookup when only the quick view bound to the same column is there, and leaves the quick view alone' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate { param($d) Remove-Cell $d 'sprk_billingexport' }

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $sec = Get-EbillingSection $script:FakeForm.formxml
        @($sec.SelectNodes("rows/row/cell/control[@id='sprk_billingexport']")).Count | Should -Be 1
        $qv = @($sec.SelectNodes("rows/row/cell/control[@id='qv_billingexport']"))
        $qv.Count | Should -Be 1
        $qv[0].GetAttribute('classid') | Should -Be '{5C5600E0-1D6E-4205-A272-BE80DA87FD42}'
    }

    It 'updates a field cell the spec owns when it differs (a total that is not disabled, a changed label), in place' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate {
            param($d)
            $d.SelectSingleNode("//control[@id='sprk_approvedtotal']").SetAttribute('disabled', 'false')
            $d.SelectSingleNode("//cell[control[@id='sprk_currency']]/labels/label").SetAttribute('description', 'Stale label')
        }
        $idsBefore = Get-FieldIds (Get-EbillingSection $xml)

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $sec = Get-EbillingSection $script:FakeForm.formxml
        $sec.SelectSingleNode("rows/row/cell/control[@id='sprk_approvedtotal']").GetAttribute('disabled') | Should -Be 'true'
        $sec.SelectSingleNode("rows/row/cell[control[@id='sprk_currency']]/labels/label").GetAttribute('description') | Should -Be 'Label of sprk_currency'
        # Updated where it stands: the order of every control is what it was.
        (Get-FieldIds $sec) | Should -Be $idsBefore
    }

    It 'does not duplicate a spec field that is already in the section, across two runs, quick view bound to the same column or not' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate { param($d) $d.SelectSingleNode("//control[@id='sprk_feestotal']").SetAttribute('disabled', 'false') }

        Invoke-InvoiceSectionRun $meta $xml
        Invoke-InvoiceSectionRun $meta $script:FakeForm.formxml

        $ids = Get-FieldIds (Get-EbillingSection $script:FakeForm.formxml)
        foreach ($col in $script:Spec['sprk_invoice'].Sections[0].Cols) { @($ids | Where-Object { $_ -eq $col }).Count | Should -Be 1 -Because $col }
        @($ids | Where-Object { $_ -eq 'qv_billingexport' }).Count | Should -Be 1
    }

    It 'keeps a subgrid and a web resource cell in the section too, and a control the spec does not list' {
        $meta = New-FakeInvoiceMeta
        $extra = '<row><cell id="{BBBBBBBB-0000-0000-0000-000000000001}" rowspan="6" colspan="1" showlabel="false"><labels><label description="Lines" languagecode="1033" /></labels><control indicationOfSubgrid="true" id="subgrid_lines" classid="{E7A81278-8635-4D9E-8D4D-59480B391C5B}"><parameters><TargetEntityType>sprk_billingevent</TargetEntityType></parameters></control></cell></row><row><cell id="{BBBBBBBB-0000-0000-0000-000000000002}"><control id="WebResource_x" classid="{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}"><parameters><Url>sprk_x.html</Url></parameters></control></cell></row><row><cell id="{BBBBBBBB-0000-0000-0000-000000000003}"><labels><label description="Other" languagecode="1033" /></labels><control id="sprk_notinspec" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" datafieldname="sprk_notinspec" /></cell></row>'
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate {
            param($d)
            $rows = $d.SelectSingleNode("//section[@name='section_ebilling']/rows")
            $frag = $d.CreateDocumentFragment(); $frag.InnerXml = $extra; [void]$rows.AppendChild($frag)
            Remove-Cell $d 'sprk_currency'
        }

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $ids = Get-FieldIds (Get-EbillingSection $script:FakeForm.formxml)
        foreach ($keep in 'subgrid_lines', 'WebResource_x', 'sprk_notinspec', 'qv_billingexport', 'sprk_currency') { $ids | Should -Contain $keep }
    }

    It 'makes no write in -WhatIf mode on a form that needs one, and records the intent' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -WithQuickView -Mutate { param($d) Remove-Cell $d 'sprk_currency' }
        $WhatIfPreference = $true
        try { Invoke-InvoiceSectionRun $meta $xml } finally { $WhatIfPreference = $false }
        $script:FakePatches.Count | Should -Be 0
        $script:Changes.Count | Should -Be 1
        $script:Changes[0] | Should -BeLike 'WOULD *'
    }

    It 'a section that does not exist yet is still added whole (first run on a form without section_ebilling)' {
        $meta = New-FakeInvoiceMeta
        $xml = New-InvoiceFormXml $meta -Mutate { param($d) $s = $d.SelectSingleNode("//section[@name='section_ebilling']"); [void]$s.ParentNode.RemoveChild($s) }

        Invoke-InvoiceSectionRun $meta $xml

        $script:FakePatches.Count | Should -Be 1
        (Get-FieldIds (Get-EbillingSection $script:FakeForm.formxml)) | Should -Contain 'sprk_matter'
    }

    It 'leaves the invoice main form of the SpaarkeMaster source with its quick view intact' {
        $src = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'Entities' 'sprk_Invoice' 'FormXml' 'main' '{93aa1c69-0406-f111-8406-7c1e525abd8b}.xml'
        Test-Path -LiteralPath $src | Should -BeTrue
        $formXml = ([xml](Get-Content -LiteralPath $src -Raw)).SelectSingleNode('//systemform/form').OuterXml
        # Metadata that matches the source: each spec column gets the type its control class says and the label its cell carries.
        $classToType = @{ '{270BD3DB-D9AF-4782-9025-509E298DEC0A}' = 'Lookup'; '{5B773807-9FB2-42DB-97C3-7A91EFF8ADFF}' = 'DateTime'; '{4273EDBD-AC1D-40D3-9FB2-095C621B552D}' = 'String'; '{C3EFE0C3-0EC6-42BE-8349-CBD9079DFD8E}' = 'Decimal'; '{4AA28AB7-9C13-4F57-A73D-AD894D048B5F}' = 'MultiSelectVirtual'; '{533B9E00-756B-4312-95A0-DC888637AC78}' = 'Money' }
        $labels = @{}
        $sec = ([xml]$formXml).SelectSingleNode("//section[@name='section_ebilling']")
        foreach ($c in $sec.SelectNodes('rows/row/cell[control]')) {
            $ctl = $c.SelectSingleNode('control'); $id = $ctl.GetAttribute('id')
            if ($id -eq $ctl.GetAttribute('datafieldname')) { $labels[$id] = $c.SelectSingleNode('labels/label').GetAttribute('description'); $script:AttrTypes[$id] = $classToType[$ctl.GetAttribute('classid').ToUpperInvariant()] }
        }
        $meta = New-FakeInvoiceMeta $labels

        Invoke-InvoiceSectionRun $meta $formXml

        # The source already carries every spec cell and every forced-read-only control (inside the section too), so the run has nothing to write.
        $script:FakePatches.Count | Should -Be 0
        $final = Get-EbillingSection $script:FakeForm.formxml
        $sourceQv = $sec.SelectSingleNode("rows/row/cell[control[@id='qv_billingexport']]").OuterXml
        $final.SelectSingleNode("rows/row/cell[control[@id='qv_billingexport']]").OuterXml | Should -BeExactly $sourceQv
        foreach ($col in $script:Spec['sprk_invoice'].Sections[0].Cols) { @((Get-FieldIds $final) | Where-Object { $_ -eq $col }).Count | Should -Be 1 -Because $col }
    }
}

# ebilling-r1 ISS-046 fix round 1. F1: the ForceReadOnly sweep used to skip every control inside section_ebilling, which was safe only while
# Section mode rebuilt the section. The section is merged now, so a forced-read-only column that sits in it without being in the spec's Cols
# (sprk_vendororg, sprk_totalamount on the invoice form) was never disabled. The sweep now covers the section too.
Describe 'ForceReadOnly reaches controls inside section_ebilling that the spec does not own (ISS-046 fix round 1)' {
    BeforeAll {
        $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1'
        $tokens = $null; $errors = $null
        $scriptAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
        foreach ($fn in $scriptAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) { Invoke-Expression $fn.Extent.Text }
        function Invoke-Dv {
            param([string]$Path, [string]$Method = 'GET', $Body, $ExtraHeaders)
            if ($Method -eq 'GET' -and $Path -like 'systemforms?*') { return [pscustomobject]@{ value = @($script:FakeForm) } }
            if ($Path -like 'systemforms(*)' -and $Method -eq 'PATCH') { $script:FakePatches += , $Body.formxml; $script:FakeForm.formxml = $Body.formxml; return $null }
            throw "fake Invoke-Dv: unexpected call $Method $Path"
        }
        $script:Types = @{
            sprk_matter = 'Lookup'; sprk_project = 'Lookup'; sprk_regardingworkassignment = 'Lookup'; sprk_supersedesinvoice = 'Lookup'; sprk_billingexport = 'Lookup'
            sprk_billingperiodstart = 'DateTime'; sprk_billingperiodend = 'DateTime'; sprk_invoiceduedate = 'DateTime'
            sprk_currency = 'String'; sprk_confidence = 'Decimal'; sprk_billingapproverrole = 'MultiSelectVirtual'
            sprk_feestotal = 'Money'; sprk_expensestotal = 'Money'; sprk_lineadjustmentstotal = 'Money'; sprk_invoiceadjustmentstotal = 'Money'; sprk_approvedtotal = 'Money'
        }
        function New-Meta([hashtable]$Labels = @{}, [hashtable]$TypeOverride = @{}) {
            $attrs = @{}
            foreach ($col in $script:Spec['sprk_invoice'].Sections[0].Cols) {
                $type = if ($TypeOverride.ContainsKey($col)) { $TypeOverride[$col] } else { $script:Types[$col] }
                $label = if ($Labels.ContainsKey($col)) { $Labels[$col] } else { "Label of $col" }
                $a = @{ LogicalName = $col; AttributeType = $(if ($type -eq 'MultiSelectVirtual') { 'Virtual' } else { $type }); DisplayName = @{ LocalizedLabels = @(@{ Label = $label }) } }
                if ($type -eq 'MultiSelectVirtual') { $a.AttributeTypeName = @{ Value = 'MultiSelectPicklistType' } }
                $attrs[$col] = [pscustomobject]$a
            }
            return [pscustomobject]@{ Table = 'sprk_invoice'; Otc = 10001; Pk = 'sprk_invoiceid'; Prim = 'sprk_name'; Attrs = $attrs }
        }
        # section_ebilling as the script writes it, plus a row holding a plain control for each of $ExtraCols that the spec's Cols do not list.
        function New-FormXml($meta, [string[]]$ExtraCols = @(), [string[]]$DisabledExtras = @(), [scriptblock]$Mutate) {
            $S = $script:Spec['sprk_invoice']
            $sec = New-SectionXml $meta $S.Sections[0] $false @($S.ForceReadOnly) 'section_ebilling'
            $doc = [xml]"<form><tabs><tab name=""tab_1""><columns><column width=""100%""><sections><section name=""section_summary""><rows><row><cell id=""{AAAAAAAA-0000-0000-0000-000000000001}""><control id=""sprk_name"" datafieldname=""sprk_name"" classid=""{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"" /></cell></row></rows></section>$sec</sections></column></columns></tab></tabs></form>"
            $rows = $doc.SelectSingleNode("//section[@name='section_ebilling']/rows")
            $n = 0
            foreach ($col in $ExtraCols) {
                $n++
                $dis = if ($DisabledExtras -contains $col) { ' disabled="true"' } else { '' }
                $frag = $doc.CreateDocumentFragment()
                $frag.InnerXml = "<row><cell id=""{CCCCCCCC-0000-0000-0000-00000000000$n}""><labels><label description=""Extra $col"" languagecode=""1033"" /></labels><control id=""$col"" classid=""{533B9E00-756B-4312-95A0-DC888637AC78}"" datafieldname=""$col""$dis /></cell></row>"
                [void]$rows.AppendChild($frag)
            }
            if ($Mutate) { & $Mutate $doc }
            return $doc.OuterXml
        }
        function Invoke-Run($meta, [string]$FormXml) {
            $script:FakeForm = [pscustomobject]@{ formid = '93aa1c69-0406-f111-8406-7c1e525abd8b'; name = 'Invoice main form'; formxml = $FormXml }
            $script:FakePatches = @()
            $script:Metas = @{ sprk_invoice = $meta }
            $script:LabelCache = @{}
            $script:EditedIds = [System.Collections.Generic.HashSet[string]]::new()
            $script:Touched = [System.Collections.Generic.HashSet[string]]::new()
            $script:Changes = [System.Collections.Generic.List[string]]::new()
            $script:Unchanged = [System.Collections.Generic.List[string]]::new()
            $script:Evidence = [ordered]@{}
            Sync-Form 'sprk_invoice' $script:Spec['sprk_invoice'] 6>$null
        }
        function Get-Section([string]$Xml) { return ([xml]$Xml).SelectSingleNode("//section[@name='section_ebilling']") }
    }

    It 'the forced columns the fixture relies on are in ForceReadOnly and not in the spec Cols (the premise of the test)' {
        $S = $script:Spec['sprk_invoice']
        foreach ($col in 'sprk_vendororg', 'sprk_totalamount') {
            @($S.ForceReadOnly) | Should -Contain $col
            @($S.Sections[0].Cols) | Should -Not -Contain $col
        }
    }

    It 'MUST FIRE: disables an ENABLED forced-read-only control inside section_ebilling that Cols does not list, and writes' {
        $meta = New-Meta
        $xml = New-FormXml $meta -ExtraCols 'sprk_vendororg', 'sprk_totalamount'
        (Get-Section $xml).SelectSingleNode("rows/row/cell/control[@id='sprk_vendororg']").GetAttribute('disabled') | Should -Not -Be 'true'

        Invoke-Run $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $sec = Get-Section $script:FakeForm.formxml
        foreach ($col in 'sprk_vendororg', 'sprk_totalamount') {
            $sec.SelectSingleNode("rows/row/cell/control[@id='$col']").GetAttribute('disabled') | Should -Be 'true' -Because $col
        }
        # It was only disabled: the cell is still where it was, once.
        @($sec.SelectNodes("rows/row/cell/control[@id='sprk_vendororg']")).Count | Should -Be 1
    }

    It 'MUST NOT FIRE: the same controls already disabled is a no-op (nothing written, nothing touched)' {
        $meta = New-Meta
        $xml = New-FormXml $meta -ExtraCols 'sprk_vendororg', 'sprk_totalamount' -DisabledExtras 'sprk_vendororg', 'sprk_totalamount'

        Invoke-Run $meta $xml

        $script:FakePatches.Count | Should -Be 0
        $script:Changes.Count | Should -Be 0
        $script:Unchanged | Should -Contain 'sprk_invoice form'
    }

    It 'a run that disabled the control is followed by a run that writes nothing' {
        $meta = New-Meta
        $xml = New-FormXml $meta -ExtraCols 'sprk_vendororg'
        Invoke-Run $meta $xml
        $script:FakePatches.Count | Should -Be 1
        Invoke-Run $meta $script:FakeForm.formxml
        $script:FakePatches.Count | Should -Be 0
    }

    It 'MUST NOT FIRE: the spec cells in the section (already disabled by Get-SpecCells) give the sweep nothing to change' {
        $meta = New-Meta
        Invoke-Run $meta (New-FormXml $meta)
        $script:FakePatches.Count | Should -Be 0
    }
}

# ISS-046 fix round 1, F2 test gaps: a Memo (wide, colspan) spec cell, and a label that needs XML escaping.
Describe 'Section merge: wide cells and labels that need escaping (ISS-046 fix round 1)' {
    BeforeAll {
        $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1'
        $tokens = $null; $errors = $null
        $scriptAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
        foreach ($fn in $scriptAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) { Invoke-Expression $fn.Extent.Text }
        function Invoke-Dv {
            param([string]$Path, [string]$Method = 'GET', $Body, $ExtraHeaders)
            if ($Method -eq 'GET' -and $Path -like 'systemforms?*') { return [pscustomobject]@{ value = @($script:FakeForm) } }
            if ($Path -like 'systemforms(*)' -and $Method -eq 'PATCH') { $script:FakePatches += , $Body.formxml; $script:FakeForm.formxml = $Body.formxml; return $null }
            throw "fake Invoke-Dv: unexpected call $Method $Path"
        }
        $script:Types = @{
            sprk_matter = 'Lookup'; sprk_project = 'Lookup'; sprk_regardingworkassignment = 'Lookup'; sprk_supersedesinvoice = 'Lookup'; sprk_billingexport = 'Lookup'
            sprk_billingperiodstart = 'DateTime'; sprk_billingperiodend = 'DateTime'; sprk_invoiceduedate = 'DateTime'
            sprk_currency = 'String'; sprk_confidence = 'Decimal'; sprk_billingapproverrole = 'MultiSelectVirtual'
            sprk_feestotal = 'Money'; sprk_expensestotal = 'Money'; sprk_lineadjustmentstotal = 'Money'; sprk_invoiceadjustmentstotal = 'Money'; sprk_approvedtotal = 'Money'
        }
        function New-Meta([hashtable]$Labels = @{}, [hashtable]$TypeOverride = @{}) {
            $attrs = @{}
            foreach ($col in $script:Spec['sprk_invoice'].Sections[0].Cols) {
                $type = if ($TypeOverride.ContainsKey($col)) { $TypeOverride[$col] } else { $script:Types[$col] }
                $label = if ($Labels.ContainsKey($col)) { $Labels[$col] } else { "Label of $col" }
                $a = @{ LogicalName = $col; AttributeType = $(if ($type -eq 'MultiSelectVirtual') { 'Virtual' } else { $type }); DisplayName = @{ LocalizedLabels = @(@{ Label = $label }) } }
                if ($type -eq 'MultiSelectVirtual') { $a.AttributeTypeName = @{ Value = 'MultiSelectPicklistType' } }
                $attrs[$col] = [pscustomobject]$a
            }
            return [pscustomobject]@{ Table = 'sprk_invoice'; Otc = 10001; Pk = 'sprk_invoiceid'; Prim = 'sprk_name'; Attrs = $attrs }
        }
        function New-FormXml($meta, [scriptblock]$Mutate) {
            $S = $script:Spec['sprk_invoice']
            $sec = New-SectionXml $meta $S.Sections[0] $false @($S.ForceReadOnly) 'section_ebilling'
            $doc = [xml]"<form><tabs><tab name=""tab_1""><columns><column width=""100%""><sections><section name=""section_summary""><rows><row><cell id=""{AAAAAAAA-0000-0000-0000-000000000001}""><control id=""sprk_name"" datafieldname=""sprk_name"" classid=""{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"" /></cell></row></rows></section>$sec</sections></column></columns></tab></tabs></form>"
            if ($Mutate) { & $Mutate $doc }
            return $doc.OuterXml
        }
        function Invoke-Run($meta, [string]$FormXml) {
            $script:FakeForm = [pscustomobject]@{ formid = '93aa1c69-0406-f111-8406-7c1e525abd8b'; name = 'Invoice main form'; formxml = $FormXml }
            $script:FakePatches = @()
            $script:Metas = @{ sprk_invoice = $meta }
            $script:LabelCache = @{}
            $script:EditedIds = [System.Collections.Generic.HashSet[string]]::new()
            $script:Touched = [System.Collections.Generic.HashSet[string]]::new()
            $script:Changes = [System.Collections.Generic.List[string]]::new()
            $script:Unchanged = [System.Collections.Generic.List[string]]::new()
            $script:Evidence = [ordered]@{}
            Sync-Form 'sprk_invoice' $script:Spec['sprk_invoice'] 6>$null
        }
        function Get-Section([string]$Xml) { return ([xml]$Xml).SelectSingleNode("//section[@name='section_ebilling']") }
        function Remove-Cell($Doc, [string]$ControlId) { $c = $Doc.SelectSingleNode("//cell[control[@id='$ControlId']]"); [void]$c.ParentNode.RemoveChild($c) }
    }

    It 'adds a missing Memo cell wide (colspan 2, rowspan 3) in a row of its own, and a re-run is a no-op' {
        $meta = New-Meta -TypeOverride @{ sprk_currency = 'Memo' }
        $xml = New-FormXml $meta { param($d) Remove-Cell $d 'sprk_currency' }
        @((Get-Section $xml).SelectNodes("rows/row/cell/control[@id='sprk_currency']")).Count | Should -Be 0

        Invoke-Run $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $sec = Get-Section $script:FakeForm.formxml
        $cell = @($sec.SelectNodes("rows/row/cell[control[@id='sprk_currency']]"))
        $cell.Count | Should -Be 1
        $cell[0].GetAttribute('colspan') | Should -Be '2'
        $cell[0].GetAttribute('rowspan') | Should -Be '3'
        $cell[0].SelectSingleNode('control').GetAttribute('classid') | Should -Be '{E0DECE4B-6FC8-4A8F-A065-082708572369}'
        # Alone in its row.
        @($cell[0].ParentNode.SelectNodes('cell')).Count | Should -Be 1

        Invoke-Run $meta $script:FakeForm.formxml
        $script:FakePatches.Count | Should -Be 0
    }

    It 'a Memo cell that is plain on the live form (no span) is replaced in place with the wide cell' {
        $meta = New-Meta -TypeOverride @{ sprk_currency = 'Memo' }
        $xml = New-FormXml $meta { param($d) $c = $d.SelectSingleNode("//cell[control[@id='sprk_currency']]"); $c.RemoveAttribute('colspan'); $c.RemoveAttribute('rowspan') }

        Invoke-Run $meta $xml

        $script:FakePatches.Count | Should -Be 1
        $cell = (Get-Section $script:FakeForm.formxml).SelectSingleNode("rows/row/cell[control[@id='sprk_currency']]")
        $cell.GetAttribute('colspan') | Should -Be '2'
    }

    It 'a label containing an ampersand, angle brackets, an apostrophe and a double quote is written escaped, reads back the same, and the next run is a no-op' {
        $label = 'Fees & <Costs> ''quoted'' "double"'
        $meta = New-Meta -Labels @{ sprk_currency = $label }
        $xml = New-FormXml $meta { param($d) Remove-Cell $d 'sprk_currency' }

        Invoke-Run $meta $xml
        $script:FakePatches.Count | Should -Be 1
        $read = (Get-Section $script:FakeForm.formxml).SelectSingleNode("rows/row/cell[control[@id='sprk_currency']]/labels/label").GetAttribute('description')
        $read | Should -BeExactly $label

        Invoke-Run $meta $script:FakeForm.formxml
        $script:FakePatches.Count | Should -Be 0
        $script:Changes.Count | Should -Be 0
    }

    It 'a label with those characters that differs on the live form is replaced in place, and the next run is a no-op' {
        $label = 'A & B < C ''x'' "y"'
        $meta = New-Meta -Labels @{ sprk_currency = $label }
        $xml = New-FormXml $meta { param($d) $d.SelectSingleNode("//cell[control[@id='sprk_currency']]/labels/label").SetAttribute('description', 'Old') }

        Invoke-Run $meta $xml
        $script:FakePatches.Count | Should -Be 1
        (Get-Section $script:FakeForm.formxml).SelectSingleNode("rows/row/cell[control[@id='sprk_currency']]/labels/label").GetAttribute('description') | Should -BeExactly $label

        Invoke-Run $meta $script:FakeForm.formxml
        $script:FakePatches.Count | Should -Be 0
    }
}

# ebilling-r1 DEF-046 (#2312). The sprk_MatterManagement sitemap lists sprk_invoice in two areas (area_mattermanagement/group_work and
# area_finance/group_c6c6d7dd). The script anchored the billing export entry on a document-wide "the sprk_invoicesubmission subarea", which
# lives in the Finance group, and inserted it into the group of the FIRST sprk_invoice entry: InsertAfter threw "The reference node is not a
# child of this node" in step 3, after the forms were written. The navigation edit is now pure XML (Update-MatterManagementSiteMap), anchors
# inside the target group only, refuses with the area / group named when it cannot place the entries, and runs on a copy before step 1.
Describe 'Matter Management navigation edit (DEF-046)' {
    BeforeAll {
        $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1'
        $tokens = $null; $errors = $null
        $scriptAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }
        foreach ($fn in $scriptAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) { Invoke-Expression $fn.Extent.Text }

        function New-Sub([string]$Id, [string]$Entity) { return "<SubArea Id=""$Id"" Entity=""$Entity"" />" }
        function New-Grp([string]$Id, [string[]]$Subs) { return "<Group Id=""$Id"">$($Subs -join '')</Group>" }
        function New-SiteMap([string[]]$Areas) { return [xml]"<SiteMap>$($Areas -join '')</SiteMap>" }
        # The Finance area as the live sitemap has it: an Invoice group with the submission, a Review group with the export.
        function New-FinanceArea([bool]$Submission = $true, [bool]$Export = $true) {
            $inv = @((New-Sub 'subarea_a33586d7' 'sprk_invoice'), (New-Sub 'subarea_efbdd6ac' 'sprk_invoiceapproval'), (New-Sub 'subarea_7d64963b' 'sprk_invoicecorrection'), (New-Sub 'subarea_9a9e59db' 'sprk_billingevent'))
            if ($Submission) { $inv += (New-Sub 'subarea_d9b7c277' 'sprk_invoicesubmission') }
            $rev = @((New-Sub 'subarea_4839733e' 'sprk_spendsignal'))
            if ($Export) { $rev += (New-Sub 'subarea_8f8f4243' 'sprk_billingexport') }
            return "<Area Id=""area_finance"">$(New-Grp 'group_billing' @((New-Sub 's1' 'sprk_budget')))$(New-Grp 'group_c6c6d7dd' $inv)$(New-Grp 'group_review' $rev)</Area>"
        }
        $script:WorkArea = "<Area Id=""area_mattermanagement"">$(New-Grp 'group_work' @((New-Sub 'subarea_x' 'sprk_matter'), (New-Sub 'subarea_fc9da905' 'sprk_invoice'), (New-Sub 'subarea_y' 'sprk_event')))</Area>"
        function Get-Entities($Doc, [string]$GroupId) { return @($Doc.SelectNodes("//Group[@Id='$GroupId']/SubArea") | ForEach-Object { $_.GetAttribute('Entity') }) }
    }

    It 'MUST FIRE (the dev shape): sprk_invoice in two areas, submission present, export missing -> export goes after the submission in the Finance Invoice group, no exception' {
        $doc = New-SiteMap @($script:WorkArea, (New-FinanceArea $true $false))
        $script:changed = $null
        { $script:changed = Update-MatterManagementSiteMap $doc } | Should -Not -Throw
        $script:changed | Should -BeTrue
        $inv = Get-Entities $doc 'group_c6c6d7dd'
        $inv[($inv.IndexOf('sprk_invoicesubmission') + 1)] | Should -Be 'sprk_billingexport'
        @($doc.SelectNodes("//SubArea[@Entity='sprk_billingexport']")).Count | Should -Be 1
        # the other area's group is not touched
        Get-Entities $doc 'group_work' | Should -Be @('sprk_matter', 'sprk_invoice', 'sprk_event')
    }

    It 'MUST FIRE: sprk_invoice in two areas, neither entry present -> both go into the Finance Invoice group (submission after the invoice, export after the submission)' {
        $doc = New-SiteMap @($script:WorkArea, (New-FinanceArea $false $false))
        { Update-MatterManagementSiteMap $doc | Out-Null } | Should -Not -Throw
        $inv = Get-Entities $doc 'group_c6c6d7dd'
        $inv[($inv.IndexOf('sprk_invoice') + 1)] | Should -Be 'sprk_invoicesubmission'
        $inv[($inv.IndexOf('sprk_invoicesubmission') + 1)] | Should -Be 'sprk_billingexport'
        Get-Entities $doc 'group_work' | Should -Be @('sprk_matter', 'sprk_invoice', 'sprk_event')
    }

    It 'the same shape with the work area LAST (document order must not decide the group)' {
        $doc = New-SiteMap @((New-FinanceArea $true $false), $script:WorkArea)
        { Update-MatterManagementSiteMap $doc | Out-Null } | Should -Not -Throw
        Get-Entities $doc 'group_c6c6d7dd' | Should -Contain 'sprk_billingexport'
        Get-Entities $doc 'group_work' | Should -Not -Contain 'sprk_billingexport'
    }

    It 'a duplicate Group Id in the other area does not redirect the insert (the old //Group[@Id=...] lookup took the first one)' {
        $dupWork = "<Area Id=""area_mattermanagement"">$(New-Grp 'group_c6c6d7dd' @((New-Sub 'subarea_fc9da905' 'sprk_invoice')))</Area>"
        $doc = New-SiteMap @($dupWork, (New-FinanceArea $true $false))
        { Update-MatterManagementSiteMap $doc | Out-Null } | Should -Not -Throw
        @($doc.SelectNodes("//Area[@Id='area_finance']/Group[@Id='group_c6c6d7dd']/SubArea[@Entity='sprk_billingexport']")).Count | Should -Be 1
        @($doc.SelectNodes("//Area[@Id='area_mattermanagement']//SubArea[@Entity='sprk_billingexport']")).Count | Should -Be 0
    }

    It 'idempotent: both entries already listed (anywhere) -> no change, and a second run after an insert changes nothing' {
        $listed = New-SiteMap @($script:WorkArea, (New-FinanceArea $true $true))
        $before = $listed.OuterXml
        Update-MatterManagementSiteMap $listed | Should -BeFalse
        $listed.OuterXml | Should -Be $before
        $doc = New-SiteMap @($script:WorkArea, (New-FinanceArea $true $false))
        Update-MatterManagementSiteMap $doc | Should -BeTrue
        $once = $doc.OuterXml
        Update-MatterManagementSiteMap $doc | Should -BeFalse
        $doc.OuterXml | Should -Be $once
    }

    It 'MUST NOT FIRE: a normal single-area sitemap with one sprk_invoice still gets both entries after it, in order' {
        $doc = New-SiteMap @("<Area Id=""area_one"">$(New-Grp 'group_work' @((New-Sub 'a' 'sprk_matter'), (New-Sub 'b' 'sprk_invoice'), (New-Sub 'c' 'sprk_event')))</Area>")
        Update-MatterManagementSiteMap $doc | Should -BeTrue
        Get-Entities $doc 'group_work' | Should -Be @('sprk_matter', 'sprk_invoice', 'sprk_invoicesubmission', 'sprk_billingexport', 'sprk_event')
    }

    It 'refuses a sitemap without sprk_invoice, naming the entity, and writes nothing' {
        $doc = New-SiteMap @("<Area Id=""area_one"">$(New-Grp 'group_work' @((New-Sub 'a' 'sprk_matter')))</Area>")
        $before = $doc.OuterXml
        { Update-MatterManagementSiteMap $doc } | Should -Throw '*no sprk_invoice subarea*'
        $doc.OuterXml | Should -Be $before
    }

    It 'refuses an ambiguous sitemap (two invoice groups, no eBilling sibling in either) with both areas and groups named, and writes nothing' {
        $second = "<Area Id=""area_other"">$(New-Grp 'group_inv2' @((New-Sub 'q' 'sprk_invoice')))</Area>"
        $doc = New-SiteMap @($script:WorkArea, $second)
        $before = $doc.OuterXml
        $err = $null
        try { Update-MatterManagementSiteMap $doc | Out-Null } catch { $err = $_.Exception.Message }
        $err | Should -Match 'area_mattermanagement/group_work'
        $err | Should -Match 'area_other/group_inv2'
        $err | Should -Match 'nothing was written'
        $doc.OuterXml | Should -Be $before
    }

    It 'refuses an ambiguous sitemap where two invoice groups both hold an eBilling sibling' {
        $a = "<Area Id=""area_a"">$(New-Grp 'g_a' @((New-Sub '1' 'sprk_invoice'), (New-Sub '2' 'sprk_invoiceapproval')))</Area>"
        $b = "<Area Id=""area_b"">$(New-Grp 'g_b' @((New-Sub '3' 'sprk_invoice'), (New-Sub '4' 'sprk_invoicecorrection')))</Area>"
        $doc = New-SiteMap @($a, $b)
        $before = $doc.OuterXml
        { Update-MatterManagementSiteMap $doc } | Should -Throw '*area_a/g_a*area_b/g_b*'
        $doc.OuterXml | Should -Be $before
    }

    It 'Add-SubAreaToGroup refuses a missing target group (no NullReference) and a SubArea Id already used by another entity' {
        $doc = New-SiteMap @($script:WorkArea)
        { Add-SubAreaToGroup $doc $null 'subarea_eb_x' 'sprk_something' $null } | Should -Throw '*target group does not exist*'
        $grp = $doc.SelectSingleNode("//Group[@Id='group_work']")
        { Add-SubAreaToGroup $doc $grp 'subarea_x' 'sprk_something' $null } | Should -Throw "*already has a SubArea with Id 'subarea_x'*"
    }

    It 'Add-SubAreaToGroup appends when the anchor is not in the group (an anchor in another group is never used)' {
        $doc = New-SiteMap @($script:WorkArea, (New-FinanceArea $true $true))
        $grp = $doc.SelectSingleNode("//Group[@Id='group_work']")
        Add-SubAreaToGroup $doc $grp 'subarea_new' 'sprk_newthing' 'sprk_invoicesubmission' | Should -BeTrue
        (Get-Entities $doc 'group_work')[-1] | Should -Be 'sprk_newthing'
    }

    It 'the SpaarkeMaster source sitemap (the real duplicate shape) is accepted and already complete: no change' {
        $src = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'AppModuleSiteMaps' 'sprk_MatterManagement' 'AppModuleSiteMap.xml'
        $doc = [xml](Get-Content -LiteralPath $src -Raw)
        @($doc.SelectNodes("//SubArea[@Entity='sprk_invoice']")).Count | Should -BeGreaterThan 1   # the premise: sprk_invoice is listed twice
        $script:changed = $null
        { $script:changed = Update-MatterManagementSiteMap $doc } | Should -Not -Throw
        $script:changed | Should -BeFalse
    }

    It 'the SpaarkeMaster source sitemap with the two entries taken out gets them back in the Finance Invoice group' {
        $src = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'AppModuleSiteMaps' 'sprk_MatterManagement' 'AppModuleSiteMap.xml'
        $doc = [xml](Get-Content -LiteralPath $src -Raw)
        foreach ($e in 'sprk_invoicesubmission', 'sprk_billingexport') { foreach ($n in @($doc.SelectNodes("//SubArea[@Entity='$e']"))) { [void]$n.ParentNode.RemoveChild($n) } }
        { Update-MatterManagementSiteMap $doc | Out-Null } | Should -Not -Throw
        $parent = $doc.SelectSingleNode("//SubArea[@Entity='sprk_invoicesubmission']").ParentNode
        $parent.GetAttribute('Id') | Should -Be 'group_c6c6d7dd'
        $parent.ParentNode.GetAttribute('Id') | Should -Be 'area_finance'
        $doc.SelectSingleNode("//SubArea[@Entity='sprk_billingexport']").ParentNode | Should -Be $parent
    }

    It 'the preflight runs before the first form is written, and not in -FormsOnly' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $pre = $text.IndexOf('Update-MatterManagementSiteMap $mmCopy')
        $forms = $text.IndexOf('Sync-Form $t $Spec[$t]')
        $pre | Should -BeGreaterThan 0
        $pre | Should -BeLessThan $forms
        $text | Should -Match '(?s)if \(-not \$FormsOnly\) \{\s*Write-Host "`n0b\. Navigation preflight'
    }
}
