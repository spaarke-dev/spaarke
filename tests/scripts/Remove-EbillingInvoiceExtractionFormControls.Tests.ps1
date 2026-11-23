# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Remove-EbillingInvoiceExtractionFormControls.Tests.ps1
# ebilling-r1 task 194 (ISS-045). Remove-EbillingInvoiceExtractionFormControls.ps1 takes sprk_extractionstatus and sprk_extractedjson off the
# live sprk_invoice main and quick-create forms. It begins with `using module` and runs against an environment, so its functions and the
# constants they read are lifted out of its own syntax tree (as Remove-EbillingRateCardCoreLookups.Tests.ps1 does) and run against a stateful
# test double of the Dataverse calls: no environment, no network.
#   T1  the pure form transform (controls, emptied row / section / column, widths, shapes it must not edit);
#   T2  the default mode (no switch) is read-only: it writes nothing, publishes nothing, asks for no write;
#   T3  -Verify fails while a control is on either form, and passes once both are gone;
#   T4  -Apply: refuses on an unexpected form shape before the first write; -WhatIf writes nothing; the happy path edits both forms and
#       publishes once, entity-scoped; a second -Apply is a no-op pass; pending collateral and a non-dev host refuse;
#   T5  structure: reads only through Read-Dv, writes only behind Confirm-Write, no direct REST call (Consistency: Strong stays in the module);
#   T6  the repository sources no longer name the controls; the columns and the option set stay; PB-013 is gone from the seed.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Remove-EbillingInvoiceExtractionFormControls.ps1'

    $tokens = $null; $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'DevHost', 'Table', 'Controls', 'ExpectedForms', 'EditableTypes', 'RetiredPattern'
    $wantedFunctions = 'Get-PreflightRefusals', 'Get-MentionCount', 'Get-MentionedControls', 'Set-EvenColumnWidths', 'ConvertTo-FormWithoutRetiredControls',
        'Read-Dv', 'Get-State', 'Write-State', 'Test-AlreadyClean', 'Get-ApplyBlockers', 'Get-VerifyProblems',
        'Edit-Forms', 'Get-PublishContext', 'Get-PendingCollateral', 'Publish-Invoice', 'Invoke-Cleanup'

    $pieces = [System.Collections.Generic.List[string]]::new()
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($statement in $script:Ast.EndBlock.Statements) {
        if ($statement -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $statement.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $statement.Left.VariablePath.UserPath -in $wantedVariables) {
            $pieces.Add($statement.Extent.Text); $found.Add($statement.Left.VariablePath.UserPath)
        }
        elseif ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $statement.Name -in $wantedFunctions) {
            $pieces.Add($statement.Extent.Text); $found.Add($statement.Name)
        }
    }
    $missing = @($wantedVariables + $wantedFunctions | Where-Object { $_ -notin $found })
    if ($missing.Count -gt 0) { throw "Not found in the script: $($missing -join ', ')" }

    # The bounded transient retry the script wraps Invoke-PublishXml in, with the module's own transient test and error readers.
    $script:ModulePath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'EbillingSchema.psm1'
    $moduleAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ModulePath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The module does not parse: $($errors[0].Message)" }
    foreach ($moduleFunction in 'Invoke-WithTransientRetry', 'Test-Transient', 'Get-StatusCode', 'Get-ErrorText') {
        $hit = @($moduleAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $moduleFunction }, $true))
        if ($hit.Count -ne 1) { throw "$moduleFunction not found in EbillingSchema.psm1" }
        $pieces.Add($hit[0].Extent.Text)
    }

    # The module's exception class is not exported by Import-Module: a test double with the same name and shape.
    class EscalationException : System.Exception {
        EscalationException([string]$message) : base($message) {}
    }

    # ---- Test doubles of the calls, over a stateful fake of dev. $script:Dev is set per test. ----
    function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }
    # -WhatIf is a double that answers no; $script:Allow = $false makes every write refuse.
    function Confirm-Write { param([string]$Target, [string]$Action) $script:Dev.Asked.Add("$Action | $Target"); return [bool]$script:Allow }

    $EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com'
    function Get-DataverseApiContext { param([string]$EnvironmentUrl) return @{ Api = 'https://dev.invalid/api/data/v9.2'; Headers = @{} } }
    function Get-EntityPublishCollateral {
        param([hashtable]$Context, [string]$Entity, [string[]]$ExcludeIds = @())
        $d = $script:Dev
        $d.CollateralCalls.Add([pscustomobject]@{ Entity = $Entity; ExcludeIds = @($ExcludeIds) })
        return @(@($d.Collateral) | Where-Object { $ExcludeIds -notcontains $_.Id } | ForEach-Object { $_.Text })
    }
    function Invoke-PublishXml {
        param([hashtable]$Context, [string]$ParameterXml)
        if ($ParameterXml -notmatch '^<importexportxml>.+</importexportxml>$') { throw 'ParameterXml must be an <importexportxml> document.' }
        $script:Dev.Writes.Add('POST PublishXml'); $script:Dev.Published.Add($ParameterXml)
    }
    function Invoke-Dv {
        param([string]$Path, [string]$Method = 'GET', $Body = $null, [hashtable]$ExtraHeaders = @{}, [switch]$NoRetry)
        $d = $script:Dev
        if ($Method -ne 'GET') {
            $d.Writes.Add("$Method $Path")
            if ($Path -match '^systemforms\((?<id>.+)\)$') { ($d.Forms | Where-Object { $_.formid -eq $Matches.id }).formxml = $Body.formxml }
            return $null
        }
        $d.Gets.Add($Path)
        if ($Path -match '^systemforms\?') { return [pscustomobject]@{ value = @($d.Forms | ForEach-Object { [pscustomobject]$_ }) } }
        return [pscustomobject]@{ value = @() }
    }

    . ([scriptblock]::Create($pieces -join "`n"))

    # ---- Fixtures: the shapes of the two live forms (the quick-create one has the retired controls in its own third column) ----
    function New-Cell([string]$Name) { "<cell id=""{$Name}""><labels><label description=""$Name"" languagecode=""1033"" /></labels><control id=""$Name"" classid=""{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"" datafieldname=""$Name"" /></cell>" }
    $script:QuickCreateXml = "<form><tabs><tab name=""tab_1""><columns>" +
        "<column width=""34%""><sections><section name=""c1""><rows><row>$(New-Cell 'sprk_name')</row><row>$(New-Cell 'sprk_invoicenumber')</row></rows></section></sections></column>" +
        "<column width=""33%""><sections><section name=""c2""><rows><row>$(New-Cell 'sprk_totalamount')</row></rows></section></sections></column>" +
        "<column width=""33%""><sections><section name=""tab_1_column_3_section_1""><rows><row>$(New-Cell 'sprk_extractionstatus')</row><row>$(New-Cell 'sprk_extractedjson')</row></rows></section></sections></column>" +
        "</columns></tab></tabs></form>"
    $script:MainXml = "<form><tabs><tab name=""tab_overview""><columns><column width=""100%""><sections>" +
        "<section name=""section_ebilling""><rows>" +
        "<row>$(New-Cell 'sprk_currency')$(New-Cell 'sprk_invoiceduedate')</row>" +
        "<row>$(New-Cell 'sprk_extractionstatus')$(New-Cell 'sprk_confidence')</row>" +
        "<row>$(New-Cell 'sprk_billingapproverrole')$(New-Cell 'sprk_billingexport')</row>" +
        "</rows></section></sections></column></columns></tab></tabs></form>"
    $script:CleanQuickCreateXml = $script:QuickCreateXml -replace '<column width="33%"><sections><section name="tab_1_column_3_section_1">.*?</column>', ''
    $script:CleanMainXml = $script:MainXml -replace '<cell id="\{sprk_extractionstatus\}">.*?</cell>', ''

    function New-Dev {
        return @{
            Forms = @(
                @{ formid = 'f-main'; name = 'Invoice main form'; type = 2; formxml = $script:MainXml }
                @{ formid = 'f-qc'; name = 'Invoice quick create form'; type = 7; formxml = $script:QuickCreateXml }
            )
            Writes = [System.Collections.Generic.List[string]]::new()
            Gets = [System.Collections.Generic.List[string]]::new()
            Asked = [System.Collections.Generic.List[string]]::new()
            Published = [System.Collections.Generic.List[string]]::new()
            Collateral = @()
            CollateralCalls = [System.Collections.Generic.List[object]]::new()
        }
    }
    function New-CleanDev {
        $d = New-Dev
        $d.Forms[0].formxml = $script:CleanMainXml
        $d.Forms[1].formxml = $script:CleanQuickCreateXml
        return $d
    }
    function Get-Refusal([scriptblock]$Action) {
        try { & $Action } catch { return $_.Exception }
        return $null
    }
    function Get-FormXml([string]$Id) { return ($script:Dev.Forms | Where-Object { $_.formid -eq $Id }).formxml }
}

Describe 'the fixtures are what the tests say they are (T1)' {
    It 'show the controls before and none after the clean-up' {
        (Get-MentionCount $script:MainXml) | Should -BeGreaterThan 0
        (Get-MentionCount $script:CleanMainXml) | Should -Be 0
        (Get-MentionCount $script:QuickCreateXml) | Should -BeGreaterThan 0
        (Get-MentionCount $script:CleanQuickCreateXml) | Should -Be 0
    }
}

Describe 'the targets (T1)' {
    It 'name exactly the two retired columns on sprk_invoice, and the main and quick-create forms' {
        $Table | Should -Be 'sprk_invoice'
        $Controls | Should -Be @('sprk_extractionstatus', 'sprk_extractedjson')
        @($ExpectedForms | ForEach-Object { "$($_.Type):$($_.Name)" }) | Should -Be @('2:Invoice main form', '7:Invoice quick create form')
        $EditableTypes | Should -Be @(2, 7)
    }
    It 'never names another invoice column' {
        ($Controls -join ' ') | Should -Not -Match 'sprk_confidence|sprk_aisummary|sprk_totalamount'
        Get-MentionCount 'sprk_confidence sprk_extractionstatusx sprk_extractedjsonid' | Should -Be 0
        Get-MentionCount 'datafieldname="sprk_extractionstatus"' | Should -Be 1
    }
}

Describe 'ConvertTo-FormWithoutRetiredControls (T1)' {
    It 'takes the control out of a two-cell row and keeps the row with its one remaining cell (main form)' {
        $r = ConvertTo-FormWithoutRetiredControls $script:MainXml
        $r.Problems.Count | Should -Be 0
        $r.Remaining | Should -Be 0
        $r.Removed | Should -Be @('control sprk_extractionstatus')
        $doc = [xml]$r.Xml
        @($doc.SelectNodes('//control') | ForEach-Object { $_.GetAttribute('datafieldname') }) | Should -Be @('sprk_currency', 'sprk_invoiceduedate', 'sprk_confidence', 'sprk_billingapproverrole', 'sprk_billingexport')
        $doc.SelectNodes('//row').Count | Should -Be 3
        foreach ($row in $doc.SelectNodes('//row')) { $row.SelectNodes('cell').Count | Should -BeGreaterThan 0 }
    }

    It 'takes both controls and the rows, section and column they leave empty (quick create), and rebalances the widths' {
        $r = ConvertTo-FormWithoutRetiredControls $script:QuickCreateXml
        $r.Problems.Count | Should -Be 0
        $r.Remaining | Should -Be 0
        $r.Removed | Should -Contain 'control sprk_extractionstatus'
        $r.Removed | Should -Contain 'control sprk_extractedjson'
        @($r.Removed | Where-Object { $_ -eq 'an emptied row' }).Count | Should -Be 2
        $r.Removed | Should -Contain 'an emptied column'
        $doc = [xml]$r.Xml
        @($doc.SelectNodes('//column') | ForEach-Object { $_.GetAttribute('width') }) | Should -Be @('50%', '50%')
        @($doc.SelectNodes('//control') | ForEach-Object { $_.GetAttribute('datafieldname') }) | Should -Be @('sprk_name', 'sprk_invoicenumber', 'sprk_totalamount')
        $doc.SelectNodes('//row[not(cell)]').Count | Should -Be 0
        $doc.SelectNodes('//section[not(rows/row)]').Count | Should -Be 0
    }

    It 'keeps a section that still holds other rows, and the other columns as they are' {
        $withOther = $script:QuickCreateXml -replace "(<row>$([regex]::Escape((New-Cell 'sprk_extractedjson')))</row>)", "`$1<row>$(New-Cell 'sprk_other')</row>"
        $r = ConvertTo-FormWithoutRetiredControls $withOther
        $r.Problems.Count | Should -Be 0
        $r.Removed | Should -Not -Contain 'an emptied column'
        $doc = [xml]$r.Xml
        @($doc.SelectNodes('//column') | ForEach-Object { $_.GetAttribute('width') }) | Should -Be @('34%', '33%', '33%')
        @($doc.SelectNodes('//control') | ForEach-Object { $_.GetAttribute('datafieldname') }) | Should -Contain 'sprk_other'
    }

    It 'leaves a form that names neither control with nothing removed and no problem' {
        $r = ConvertTo-FormWithoutRetiredControls $script:CleanQuickCreateXml
        $r.Removed.Count | Should -Be 0
        $r.Problems.Count | Should -Be 0
        $r.Remaining | Should -Be 0
    }

    It 'reports a mention it cannot remove (an event handler) so the caller refuses' {
        $withEvent = $script:MainXml -replace '</form>', '<events><event name="onchange" attribute="sprk_extractionstatus"><Handlers /></event></events></form>'
        (ConvertTo-FormWithoutRetiredControls $withEvent).Remaining | Should -BeGreaterThan 0
    }

    It 'reports a control that is not in a cell of a row of a section (a header) as a problem, and removes nothing there' {
        $inHeader = '<form><header><rows><row><cell><control id="sprk_extractionstatus" datafieldname="sprk_extractionstatus" /></cell></row></rows></header><tabs /></form>'
        $r = ConvertTo-FormWithoutRetiredControls $inHeader
        $r.Problems.Count | Should -Be 1
        $r.Removed.Count | Should -Be 0
        $r.Remaining | Should -BeGreaterThan 0
    }

    It 'reports a form xml that does not parse, and a root that is not a form element' {
        (ConvertTo-FormWithoutRetiredControls '<form><tabs>').Problems.Count | Should -Be 1
        (ConvertTo-FormWithoutRetiredControls '<other><tabs /></other>').Problems.Count | Should -Be 1
    }

    It 'reports a tab it would leave without columns' {
        $only = "<form><tabs><tab><columns><column width=""100%""><sections><section name=""s""><rows><row>$(New-Cell 'sprk_extractionstatus')</row></rows></section></sections></column></columns></tab></tabs></form>"
        (ConvertTo-FormWithoutRetiredControls $only).Problems | Should -Contain 'removing the controls would leave a tab without columns'
    }
}

Describe 'the default mode (T2): read-only' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'reads the forms, reports READY, and writes, publishes and asks for nothing' {
        $out = Invoke-Cleanup $false $false $false 6>&1 | Out-String
        $out | Should -Match 'VERDICT: READY'
        $out | Should -Match 'Report only'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 0
        $script:Dev.CollateralCalls.Count | Should -Be 0
        @($script:Dev.Gets | Where-Object { $_ -like 'systemforms?*' }).Count | Should -Be 1
        (Get-FormXml 'f-main') | Should -Be $script:MainXml
        (Get-FormXml 'f-qc') | Should -Be $script:QuickCreateXml
    }

    It 'reports ALREADY CLEAN on clean forms, still writing nothing' {
        $script:Dev = New-CleanDev
        $out = Invoke-Cleanup $false $false $false 6>&1 | Out-String
        $out | Should -Match 'ALREADY CLEAN'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'reports BLOCKED (and still writes nothing) on an unexpected form shape' {
        $script:Dev.Forms[0].formxml = $script:MainXml -replace '</form>', '<events><event name="onchange" attribute="sprk_extractionstatus"><Handlers /></event></events></form>'
        $out = Invoke-Cleanup $false $false $false 6>&1 | Out-String
        $out | Should -Match 'VERDICT: BLOCKED'
        $script:Dev.Writes.Count | Should -Be 0
    }
}

Describe '-Verify (T3)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'fails while both controls are on the forms' {
        $e = Get-Refusal { Invoke-Cleanup $false $true $false 6>&1 | Out-Null }
        $e | Should -Not -BeNullOrEmpty
        $e.Message | Should -Match 'Verification failed'
        $e.Message | Should -Match "Invoice quick create form' still names sprk_extractionstatus, sprk_extractedjson"
        $e.Message | Should -Match "Invoice main form' still names sprk_extractionstatus"
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'fails while only the main form still shows its control' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms[0].formxml = $script:MainXml
        $e = Get-Refusal { Invoke-Cleanup $false $true $false 6>&1 | Out-Null }
        $e.Message | Should -Match "Invoice main form' still names"
        $e.Message | Should -Not -Match 'quick create form. still names'
    }

    It 'fails while only the quick-create form still shows a control' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms[1].formxml = $script:QuickCreateXml
        $e = Get-Refusal { Invoke-Cleanup $false $true $false 6>&1 | Out-Null }
        $e.Message | Should -Match 'quick create form. still names'
        $e.Message | Should -Not -Match 'Invoice main form. still names'
    }

    It 'passes when both controls are gone from both forms' {
        $script:Dev = New-CleanDev
        $out = Invoke-Cleanup $false $true $false 6>&1 | Out-String
        $out | Should -Match 'neither sprk_extractionstatus nor sprk_extractedjson'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'fails (nothing can be confirmed) when an expected form is missing' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms = @($script:Dev.Forms[0])
        $e = Get-Refusal { Invoke-Cleanup $false $true $false 6>&1 | Out-Null }
        $e.Message | Should -Match 'Verification failed'
        $e.Message | Should -Match 'quick create form'
    }
}

Describe '-Apply (T4)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'edits both forms, publishes sprk_invoice once (entity-scoped) and verifies' {
        $out = Invoke-Cleanup $true $false $false 6>&1 | Out-String
        $script:Dev.Writes | Should -Be @('PATCH systemforms(f-main)', 'PATCH systemforms(f-qc)', 'POST PublishXml')
        $script:Dev.Published.Count | Should -Be 1
        $script:Dev.Published[0] | Should -Be '<importexportxml><entities><entity>sprk_invoice</entity></entities></importexportxml>'
        Get-MentionCount (Get-FormXml 'f-main') | Should -Be 0
        Get-MentionCount (Get-FormXml 'f-qc') | Should -Be 0
        @(([xml](Get-FormXml 'f-qc')).SelectNodes('//column') | ForEach-Object { $_.GetAttribute('width') }) | Should -Be @('50%', '50%')
        $out | Should -Match 'neither sprk_extractionstatus nor sprk_extractedjson'
        # D-83: the collateral check excludes the script's own two forms.
        $script:Dev.CollateralCalls[0].Entity | Should -Be 'sprk_invoice'
        $script:Dev.CollateralCalls[0].ExcludeIds | Should -Contain 'f-main'
        $script:Dev.CollateralCalls[0].ExcludeIds | Should -Contain 'f-qc'
    }

    It 'is idempotent: a second -Apply finds both forms clean, writes nothing and verifies' {
        Invoke-Cleanup $true $false $false 6>&1 | Out-Null
        $script:Dev.Writes.Clear()
        $out = Invoke-Cleanup $true $false $false 6>&1 | Out-String
        $out | Should -Match 'already clean'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It '-Apply -WhatIf asks for every write, writes nothing and leaves both forms as they were' {
        $script:Allow = $false
        Invoke-Cleanup $true $false $true 6>&1 | Out-Null
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 3
        (Get-FormXml 'f-main') | Should -Be $script:MainXml
        (Get-FormXml 'f-qc') | Should -Be $script:QuickCreateXml
    }

    It 'refuses on an unexpected form shape before the first write: an event handler names a control' {
        $script:Dev.Forms[1].formxml = $script:QuickCreateXml -replace '</form>', '<events><event name="onload" attribute="sprk_extractedjson"><Handlers /></event></events></form>'
        $e = Get-Refusal { Invoke-Cleanup $true $false $false 6>&1 | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'Refusing to apply'
        $e.Message | Should -Match 'would still name a retired control'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 0
    }

    It 'refuses on an unexpected form shape before the first write: a control outside a section cell' {
        $script:Dev.Forms[0].formxml = $script:MainXml -replace '<tabs>', '<header><rows><row><cell><control id="sprk_extractionstatus" datafieldname="sprk_extractionstatus" /></cell></row></rows></header><tabs>'
        $e = Get-Refusal { Invoke-Cleanup $true $false $false 6>&1 | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'not in a cell of a row of a section'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses when the quick-create form is missing, and when the main form is ambiguous' {
        $script:Dev.Forms = @($script:Dev.Forms[0])
        $e = Get-Refusal { Invoke-Cleanup $true $false $false 6>&1 | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match "expected exactly one quick create form 'Invoice quick create form'"
        $script:Dev.Writes.Count | Should -Be 0

        $script:Dev = New-Dev
        $script:Dev.Forms += @{ formid = 'f-main2'; name = 'Invoice main form'; type = 2; formxml = $script:MainXml }
        $e = Get-Refusal { Invoke-Cleanup $true $false $false 6>&1 | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match "expected exactly one main form 'Invoice main form'"
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses when another pending form or view of sprk_invoice would go live with the publish, unless -AllowPendingCollateral' {
        $script:Dev.Collateral = @(@{ Id = 'v-other'; Text = "view 'Someone''s draft' (v-other)" })
        $e = Get-Refusal { Invoke-Cleanup $true $false $false 6>&1 | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'would go live with the publish'
        $script:Dev.Writes.Count | Should -Be 0

        $script:Dev = New-Dev
        $script:Dev.Collateral = @(@{ Id = 'v-other'; Text = "view 'Someone''s draft' (v-other)" })
        Invoke-Cleanup $true $false $false $true 6>&1 | Out-Null
        $script:Dev.Published.Count | Should -Be 1
    }

    It 'does not count its own forms as collateral' {
        $script:Dev.Collateral = @(@{ Id = 'f-main'; Text = 'form f-main' }, @{ Id = 'f-qc'; Text = 'form f-qc' })
        Invoke-Cleanup $true $false $false 6>&1 | Out-Null
        $script:Dev.Published.Count | Should -Be 1
    }

    It 'refuses a host that is not dev, before any Dataverse call' {
        (Get-PreflightRefusals $true 'https://spaarke-prod.crm.dynamics.com') | Should -Match 'dev only'
        (Get-PreflightRefusals $true 'https://spaarkedev1.crm.dynamics.com') | Should -BeNullOrEmpty
        (Get-PreflightRefusals $false 'https://spaarke-prod.crm.dynamics.com') | Should -BeNullOrEmpty
    }

    It 'a form of another type that names a control is reported and not edited' {
        $script:Dev.Forms += @{ formid = 'f-card'; name = 'Invoice card'; type = 11; formxml = $script:MainXml }
        Invoke-Cleanup $true $false $false 6>&1 | Out-Null
        $script:Dev.Writes | Should -Not -Contain 'PATCH systemforms(f-card)'
        (Get-FormXml 'f-card') | Should -Be $script:MainXml
    }
}

Describe 'the script is read-only except behind Confirm-Write (T5)' {
    BeforeAll {
        $script:Text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $script:Fns = @($script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false))
    }

    It 'has no REST call of its own (every Web API call goes through EbillingSchema.psm1 or the shared publish procedure)' {
        $script:Text | Should -Not -Match '(?i)Invoke-RestMethod|Invoke-WebRequest|\bcurl\b|\baz\s+rest'
        $script:Text | Should -Match '(?m)^using module \.\\EbillingSchema\.psm1'
    }

    It 'sends the writes only from functions that ask Confirm-Write first' {
        foreach ($fn in $script:Fns) {
            $body = $fn.Extent.Text
            if ($body -match "-Method\s+(PATCH|POST|PUT|DELETE)" -or $body -match 'Invoke-PublishXml') {
                $fn.Name | Should -BeIn @('Edit-Forms', 'Publish-Invoice')
                $body | Should -Match 'Confirm-Write'
            }
        }
    }

    It 'reads only through Read-Dv (GET)' {
        $script:Text | Should -Not -Match 'Read-Dv[^\r\n]*-Method'
    }

    It 'is dev only and starts in report mode (nothing is written without -Apply)' {
        $script:Text | Should -Match "DevHost = 'spaarkedev1\.crm\.dynamics\.com'"
        $script:Text | Should -Match 'Invoke-Cleanup \(\[bool\]\$Apply\)'
        $script:Text | Should -Match 'if \(-not \$ApplyRequested -and -not \$VerifyRequested\)'
    }
}

Describe 'the repository sources (T6)' {
    BeforeAll {
        $script:Master = Join-Path $script:RepoRoot 'src' 'dataverse' 'solutions' 'SpaarkeMaster'
        $script:FormDir = Join-Path $script:Master 'Entities' 'sprk_Invoice' 'FormXml'
    }

    It 'the four invoice form sources no longer name either control, and stay well-formed with no empty section or orphan row of ours' {
        $files = @(Get-ChildItem -Path (Join-Path $script:FormDir 'main'), (Join-Path $script:FormDir 'quickCreate') -Filter '*.xml' -File)
        $files.Count | Should -Be 6   # two main forms and one quick-create, each with a _managed copy
        foreach ($f in $files) {
            $text = [System.IO.File]::ReadAllText($f.FullName)
            Get-MentionCount $text | Should -Be 0 -Because $f.Name
            { [xml]$text } | Should -Not -Throw
        }
        foreach ($f in @(Get-ChildItem -Path (Join-Path $script:FormDir 'quickCreate') -Filter '*.xml' -File)) {
            $doc = [xml][System.IO.File]::ReadAllText($f.FullName)
            $doc.SelectNodes('//row[not(cell)]').Count | Should -Be 0 -Because $f.Name
            $doc.SelectNodes('//section[not(rows/row)]').Count | Should -Be 0 -Because $f.Name
            @($doc.SelectNodes('//column') | ForEach-Object { $_.GetAttribute('width') }) | Should -Be @('50%', '50%') -Because $f.Name
        }
    }

    It 'the main form keeps its Confidence control (the row it shared with the retired one stays, with one cell)' {
        $f = Get-ChildItem -Path (Join-Path $script:FormDir 'main') -Filter '{93aa1c69-*}.xml' -File | Where-Object { $_.Name -notlike '*_managed.xml' }
        $doc = [xml][System.IO.File]::ReadAllText($f.FullName)
        $conf = $doc.SelectSingleNode("//control[@datafieldname='sprk_confidence']")
        $conf | Should -Not -BeNullOrEmpty
        $conf.SelectSingleNode('ancestor::row[1]').SelectNodes('cell').Count | Should -Be 1
    }

    It 'the eBilling section of Deploy-EbillingFormsViews.ps1 no longer lists the extraction status' {
        $text = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1') -Raw
        Get-MentionCount $text | Should -Be 0
        $text | Should -Match "'sprk_currency', 'sprk_confidence', 'sprk_billingapproverrole'"
    }

    It 'the columns and the option set stay (a separate, destructive owner decision)' {
        $entity = [System.IO.File]::ReadAllText((Join-Path $script:Master 'Entities' 'sprk_Invoice' 'Entity.xml'))
        $entity | Should -Match 'sprk_extractionstatus'
        $entity | Should -Match 'sprk_extractedjson'
        Test-Path -LiteralPath (Join-Path $script:Master 'OptionSets' 'sprk_extractionstatus.xml') | Should -BeTrue
    }

    It 'PB-013 is gone from the playbook seed (which still parses and keeps its other playbooks) and its verify script is retired' {
        $seed = Join-Path $script:RepoRoot 'scripts' 'seed-data' 'playbooks.json'
        $json = Get-Content -LiteralPath $seed -Raw | ConvertFrom-Json
        @($json.playbooks | ForEach-Object { $_.id }) | Should -Be @('PB-001', 'PB-002', 'PB-011', 'PB-010', 'PB-012')
        (Get-Content -LiteralPath $seed -Raw) | Should -Not -Match 'PB-013|sprk_extractionstatus|sprk_extractedjson|Finance Invoice Processing'
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'seed-data' 'Verify-Finance-Playbook.ps1') | Should -BeFalse
    }
}
