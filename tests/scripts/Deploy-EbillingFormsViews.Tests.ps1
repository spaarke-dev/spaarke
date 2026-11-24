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

        $final = Get-EbillingSection $script:FakeForm.formxml
        $sourceQv = $sec.SelectSingleNode("rows/row/cell[control[@id='qv_billingexport']]").OuterXml
        $final.SelectSingleNode("rows/row/cell[control[@id='qv_billingexport']]").OuterXml | Should -BeExactly $sourceQv
        foreach ($col in $script:Spec['sprk_invoice'].Sections[0].Cols) { @((Get-FieldIds $final) | Where-Object { $_ -eq $col }).Count | Should -Be 1 -Because $col }
    }
}
