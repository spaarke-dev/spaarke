# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Remove-EbillingInvoiceExtractionFormControls.Tests.ps1
# ebilling-r1 task 194 (ISS-045). Remove-EbillingInvoiceExtractionFormControls.ps1 takes sprk_extractionstatus and sprk_extractedjson off the
# live sprk_invoice main and quick-create forms. It begins with `using module` and runs against an environment, so its functions and the
# constants they read are lifted out of its own syntax tree (as Remove-EbillingRateCardCoreLookups.Tests.ps1 does) and run against a stateful
# test double of the Dataverse calls: no environment, no network.
#   T1  the pure form transform (controls, emptied row / section / column, widths, shapes it must not edit);
#   T2  the default mode (no switch) is read-only: it writes nothing, publishes nothing, asks for no write; it shows the pending changes;
#   T3  -Verify fails while a control is on either form (or on any other invoice form), and passes once both are gone;
#   T4  -Apply: the dev-only pre-flight runs first; refuses on an unexpected form shape before the first write; -WhatIf writes nothing; the
#       happy path edits both forms and publishes once, entity-scoped; a second -Apply is a no-op pass; only the two form ids are edited;
#       unpublished edits on a target form are never overwritten (resume = equal text only); foreign pending collateral refuses;
#       a change that appears during the run stops the publish;
#   T5  structure: reads only through Read-Dv, writes only behind Confirm-Write, no direct REST call, the shared context carries
#       Consistency: Strong;
#   T6  the repository sources no longer name the controls; the columns and the option set stay; PB-013 is gone from the seed.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Remove-EbillingInvoiceExtractionFormControls.ps1'

    $tokens = $null; $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'DevHost', 'Table', 'Controls', 'ExpectedForms', 'RetiredPattern'
    $wantedFunctions = 'Get-PreflightRefusals', 'Get-MentionCount', 'Get-MentionedControls', 'Set-EvenColumnWidths', 'ConvertTo-FormWithoutRetiredControls',
        'Read-Dv', 'ConvertTo-FormId', 'ConvertTo-CanonicalXml', 'Get-State', 'Write-State', 'Get-PublishContext', 'Get-CollateralItemId', 'Get-PendingReport',
        'Write-PendingReport', 'Test-AlreadyClean', 'Get-ApplyBlockers', 'Get-VerifyProblems', 'Edit-Forms', 'Publish-Invoice', 'Invoke-Cleanup'

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

    $script:MainId = '93aa1c69-0406-f111-8406-7c1e525abd8b'
    $script:QcId = 'c5e5807d-e20c-f111-8342-7c1e520aa4df'
    $script:DevUrl = 'https://spaarkedev1.crm.dynamics.com'

    # ---- Test doubles of the calls, over a stateful fake of dev. $script:Dev is set per test. ----
    # The form layers: Forms[].formxml is the PUBLISHED text (the plain read); Pending[id] is the UNPUBLISHED text (RetrieveUnpublished).
    # A PATCH writes the unpublished layer; PublishXml moves every pending text to the published layer.
    function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }
    # -WhatIf is a double that answers no; $script:Allow = $false makes every write refuse.
    function Confirm-Write { param([string]$Target, [string]$Action) $script:Dev.Asked.Add("$Action | $Target"); return [bool]$script:Allow }
    function Initialize-EbillingSchema { param([string]$EnvironmentUrl, [string]$SolutionUniqueName, [switch]$DryRun, [string]$Entity) $script:Dev.InitCalls.Add($EnvironmentUrl); return $null }

    $EnvironmentUrl = $script:DevUrl
    # Like the real shared context: no Consistency header of its own.
    function Get-DataverseApiContext { param([string]$EnvironmentUrl) $script:Dev.ContextCalls++; return @{ Api = 'https://dev.invalid/api/data/v9.2'; Headers = @{ Authorization = 'Bearer x'; Accept = 'application/json' } } }
    function Get-EntityPublishCollateral {
        param([hashtable]$Context, [string]$Entity, [string[]]$ExcludeIds = @())
        $d = $script:Dev
        $d.CollateralCalls.Add([pscustomobject]@{ Entity = $Entity; ExcludeIds = @($ExcludeIds); Consistency = $Context.Headers['Consistency'] })
        $lines = [System.Collections.Generic.List[string]]::new()
        foreach ($f in $d.Forms) { if ($d.Pending.ContainsKey($f.formid)) { $lines.Add("systemforms: $($f.name) ($($f.formid))") } }
        foreach ($c in @($d.Collateral)) { $lines.Add($c) }
        if ($d.Writes.Count -gt 0 -and -not ($d.Published.Count -gt 0)) { foreach ($c in @($d.CollateralAfterEdit)) { $lines.Add($c) } }
        $skip = @($ExcludeIds | ForEach-Object { $_.ToLowerInvariant() })
        return @($lines | Where-Object { $m = [regex]::Match($_, '\(([^()]+)\)\s*$'); -not $m.Success -or $skip -notcontains $m.Groups[1].Value.ToLowerInvariant() })
    }
    function Invoke-PublishXml {
        param([hashtable]$Context, [string]$ParameterXml)
        if ($ParameterXml -notmatch '^<importexportxml>.+</importexportxml>$') { throw 'ParameterXml must be an <importexportxml> document.' }
        $d = $script:Dev
        $d.PublishContexts.Add($Context)
        $d.Writes.Add('POST PublishXml'); $d.Published.Add($ParameterXml)
        foreach ($f in $d.Forms) { if ($d.Pending.ContainsKey($f.formid)) { $f.formxml = $d.Pending[$f.formid] } }
        $d.Pending.Clear()
    }
    function Invoke-Dv {
        param([string]$Path, [string]$Method = 'GET', $Body = $null, [hashtable]$ExtraHeaders = @{}, [switch]$NoRetry)
        $d = $script:Dev
        if ($Method -ne 'GET') {
            $d.Writes.Add("$Method $Path")
            if ($Path -match '^systemforms\((?<id>[^)]+)\)$') {
                $d.Pending[$Matches.id] = $Body.formxml
                if ($d.AfterPatch) { & $d.AfterPatch $Matches.id }
            }
            return $null
        }
        $d.Gets.Add($Path)
        if ($Path -match '^systemforms\((?<id>[^)]+)\)/Microsoft\.Dynamics\.CRM\.RetrieveUnpublished\(\)') {
            $form = $d.Forms | Where-Object { $_.formid -eq $Matches.id }
            $xml = if ($d.Pending.ContainsKey($Matches.id)) { $d.Pending[$Matches.id] } else { $form.formxml }
            return [pscustomobject]@{ formxml = $xml }
        }
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
    # What this run writes for each form (the same transform the script applies).
    $script:MainWritten = (ConvertTo-FormWithoutRetiredControls $script:MainXml).Xml
    $script:QcWritten = (ConvertTo-FormWithoutRetiredControls $script:QuickCreateXml).Xml

    function New-Dev {
        return @{
            Forms = @(
                @{ formid = $script:MainId; name = 'Invoice main form'; type = 2; formxml = $script:MainXml }
                @{ formid = $script:QcId; name = 'Invoice quick create form'; type = 7; formxml = $script:QuickCreateXml }
            )
            Pending = @{}
            AfterPatch = $null
            Writes = [System.Collections.Generic.List[string]]::new()
            Gets = [System.Collections.Generic.List[string]]::new()
            Asked = [System.Collections.Generic.List[string]]::new()
            Published = [System.Collections.Generic.List[string]]::new()
            PublishContexts = [System.Collections.Generic.List[object]]::new()
            InitCalls = [System.Collections.Generic.List[string]]::new()
            ContextCalls = 0
            Collateral = @()
            CollateralAfterEdit = @()
            CollateralCalls = [System.Collections.Generic.List[object]]::new()
        }
    }
    function New-CleanDev {
        $d = New-Dev
        $d.Forms[0].formxml = $script:CleanMainXml
        $d.Forms[1].formxml = $script:CleanQuickCreateXml
        return $d
    }
    # The run, with the host output captured.
    function Invoke-Run([bool]$Apply = $false, [bool]$Verify = $false, [bool]$WhatIf = $false, [bool]$Allow = $false, [string]$Url = $script:DevUrl) {
        $script:PublishContext = $null
        return (Invoke-Cleanup $Apply $Verify $WhatIf $Allow $Url 6>&1 | Out-String)
    }
    function Get-Refusal([scriptblock]$Action) {
        $script:PublishContext = $null
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
        @($ExpectedForms | ForEach-Object { "$($_.Type):$($_.Id):$($_.Name)" }) | Should -Be @('2:93aa1c69-0406-f111-8406-7c1e525abd8b:Invoice main form', '7:c5e5807d-e20c-f111-8342-7c1e520aa4df:Invoice quick create form')
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
        $out = Invoke-Run
        $out | Should -Match 'VERDICT: READY'
        $out | Should -Match 'Report only'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 0
        @($script:Dev.Gets | Where-Object { $_ -like 'systemforms?*' }).Count | Should -Be 1
        (Get-FormXml $script:MainId) | Should -Be $script:MainXml
        (Get-FormXml $script:QcId) | Should -Be $script:QuickCreateXml
    }

    It 'shows the pending-changes line (none) and reads the pending items WITHOUT excluding the two target forms (F2)' {
        $out = Invoke-Run
        $out | Should -Match 'pending \(unpublished\) items on sprk_invoice: none'
        $script:Dev.CollateralCalls.Count | Should -Be 1
        $script:Dev.CollateralCalls[0].Entity | Should -Be 'sprk_invoice'
        $script:Dev.CollateralCalls[0].ExcludeIds.Count | Should -Be 0
    }

    It 'reports BLOCKED when someone left unpublished changes on a target form: it would be overwritten (F2), and writes nothing' {
        $script:Dev.Pending[$script:QcId] = $script:QuickCreateXml -replace 'sprk_totalamount', 'sprk_someoneelse'
        $out = Invoke-Run
        $out | Should -Match 'OVERWRITE RISK'
        $out | Should -Match 'VERDICT: BLOCKED'
        $out | Should -Match 'would overwrite'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'reports a target form whose pending text is exactly this run''s edit as a resume (no risk line), READY' {
        $script:Dev.Pending[$script:QcId] = $script:QcWritten
        $out = Invoke-Run
        $out | Should -Not -Match 'OVERWRITE RISK'
        $out | Should -Match 'VERDICT: READY'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'reports a resume although Dataverse stores the pending text with other insignificant whitespace' {
        $script:Dev.Pending[$script:MainId] = ($script:MainWritten -replace '><', ">`n  <")
        $out = Invoke-Run
        $out | Should -Not -Match 'OVERWRITE RISK'
        $out | Should -Match 'VERDICT: READY'
    }

    It 'lists other pending items as would-go-live lines and blocks on them' {
        $script:Dev.Collateral = @("savedqueries: Someone's draft (v-other)")
        $out = Invoke-Run
        $out | Should -Match "would go live   savedqueries: Someone's draft"
        $out | Should -Match 'VERDICT: BLOCKED'
    }

    It 'reports ALREADY CLEAN on clean forms, still writing nothing, and reads no pending items (nothing would be published)' {
        $script:Dev = New-CleanDev
        $out = Invoke-Run
        $out | Should -Match 'ALREADY CLEAN'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.CollateralCalls.Count | Should -Be 0
    }

    It 'reports BLOCKED (and still writes nothing) on an unexpected form shape' {
        $script:Dev.Forms[0].formxml = $script:MainXml -replace '</form>', '<events><event name="onchange" attribute="sprk_extractionstatus"><Handlers /></event></events></form>'
        $out = Invoke-Run
        $out | Should -Match 'VERDICT: BLOCKED'
        $script:Dev.Writes.Count | Should -Be 0
    }
}

Describe '-Verify (T3)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'fails while both controls are on the forms' {
        $e = Get-Refusal { Invoke-Run $false $true | Out-Null }
        $e | Should -Not -BeNullOrEmpty
        $e.Message | Should -Match 'Verification failed'
        $e.Message | Should -Match "Invoice quick create form' still names sprk_extractionstatus, sprk_extractedjson"
        $e.Message | Should -Match "Invoice main form' still names sprk_extractionstatus"
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'fails while only the main form still shows its control' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms[0].formxml = $script:MainXml
        $e = Get-Refusal { Invoke-Run $false $true | Out-Null }
        $e.Message | Should -Match "Invoice main form' still names"
        $e.Message | Should -Not -Match 'quick create form. still names'
    }

    It 'fails while only the quick-create form still shows a control' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms[1].formxml = $script:QuickCreateXml
        $e = Get-Refusal { Invoke-Run $false $true | Out-Null }
        $e.Message | Should -Match 'quick create form. still names'
        $e.Message | Should -Not -Match 'Invoice main form. still names'
    }

    It 'fails while any OTHER invoice form still names a control' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms += @{ formid = 'f-other'; name = 'Information'; type = 2; formxml = $script:MainXml }
        $e = Get-Refusal { Invoke-Run $false $true | Out-Null }
        $e.Message | Should -Match "form 'Information' \(f-other\) still names sprk_extractionstatus"
    }

    It 'passes when both controls are gone from both forms' {
        $script:Dev = New-CleanDev
        $out = Invoke-Run $false $true
        $out | Should -Match 'neither sprk_extractionstatus nor sprk_extractedjson'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'fails (nothing can be confirmed) when an expected form is missing' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms = @($script:Dev.Forms[0])
        $e = Get-Refusal { Invoke-Run $false $true | Out-Null }
        $e.Message | Should -Match 'Verification failed'
        $e.Message | Should -Match 'quick create form'
    }
}

Describe '-Apply (T4)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'edits both forms, publishes sprk_invoice once (entity-scoped) and verifies' {
        $out = Invoke-Run $true
        $script:Dev.Writes | Should -Be @("PATCH systemforms($($script:MainId))", "PATCH systemforms($($script:QcId))", 'POST PublishXml')
        $script:Dev.Published.Count | Should -Be 1
        $script:Dev.Published[0] | Should -Be '<importexportxml><entities><entity>sprk_invoice</entity></entities></importexportxml>'
        Get-MentionCount (Get-FormXml $script:MainId) | Should -Be 0
        Get-MentionCount (Get-FormXml $script:QcId) | Should -Be 0
        @(([xml](Get-FormXml $script:QcId)).SelectNodes('//column') | ForEach-Object { $_.GetAttribute('width') }) | Should -Be @('50%', '50%')
        $out | Should -Match 'neither sprk_extractionstatus nor sprk_extractedjson'
    }

    It 'never excludes its own forms from the pending read (before the edit and right before the publish)' {
        Invoke-Run $true | Out-Null
        $script:Dev.CollateralCalls.Count | Should -Be 2
        foreach ($c in $script:Dev.CollateralCalls) { $c.ExcludeIds.Count | Should -Be 0 }
    }

    It 'is idempotent: a second -Apply finds both forms clean, writes nothing and verifies' {
        Invoke-Run $true | Out-Null
        $script:Dev.Writes.Clear()
        $out = Invoke-Run $true
        $out | Should -Match 'already clean'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It '-Apply -WhatIf asks for every write, writes nothing and leaves both forms as they were' {
        $script:Allow = $false
        Invoke-Run $true $false $true | Out-Null
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 3
        (Get-FormXml $script:MainId) | Should -Be $script:MainXml
        (Get-FormXml $script:QcId) | Should -Be $script:QuickCreateXml
    }

    It 'refuses on an unexpected form shape before the first write: an event handler names a control' {
        $script:Dev.Forms[1].formxml = $script:QuickCreateXml -replace '</form>', '<events><event name="onload" attribute="sprk_extractedjson"><Handlers /></event></events></form>'
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'Refusing to apply'
        $e.Message | Should -Match 'would still name a retired control'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 0
    }

    It 'refuses on an unexpected form shape before the first write: a control outside a section cell' {
        $script:Dev.Forms[0].formxml = $script:MainXml -replace '<tabs>', '<header><rows><row><cell><control id="sprk_extractionstatus" datafieldname="sprk_extractionstatus" /></cell></row></rows></header><tabs>'
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'not in a cell of a row of a section'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses when an expected form is missing (pinned by id) before the first write' {
        $script:Dev.Forms = @($script:Dev.Forms[0])
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match "expected quick create form 'Invoice quick create form' \(c5e5807d-e20c-f111-8342-7c1e520aa4df, type 7\) was not found"
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'treats a form with the expected id but another type as not found' {
        $script:Dev.Forms[1].type = 11
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'was not found'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'a form that merely has the expected NAME but another id is not edited, and a control on it is a blocker (K1)' {
        $script:Dev = New-CleanDev
        $script:Dev.Forms += @{ formid = 'f-other'; name = 'Invoice main form'; type = 2; formxml = $script:MainXml }
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match "form 'Invoice main form' \(f-other, type 2\) names sprk_extractionstatus but is not one of the two forms"
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'any other invoice form that names a control blocks -Apply before the first write and is never edited, whatever its type (K1)' {
        foreach ($type in 2, 7, 11, 6) {
            $script:Dev = New-Dev
            $script:Dev.Forms += @{ formid = "f-other-$type"; name = "Other $type"; type = $type; formxml = $script:MainXml }
            $e = Get-Refusal { Invoke-Run $true | Out-Null }
            $e | Should -BeOfType ([EscalationException])
            $e.Message | Should -Match "Other $type"
            $script:Dev.Writes.Count | Should -Be 0
            (Get-FormXml "f-other-$type") | Should -Be $script:MainXml
        }
    }

    It 'edits only the two expected form ids when the other forms are clean' {
        $script:Dev.Forms += @{ formid = 'f-info'; name = 'Information'; type = 2; formxml = $script:CleanMainXml }
        Invoke-Run $true | Out-Null
        @($script:Dev.Writes | Where-Object { $_ -like 'PATCH *' }) | Should -Be @("PATCH systemforms($($script:MainId))", "PATCH systemforms($($script:QcId))")
    }

    It 'refuses (exit 2) when a target form has unpublished changes that are not this run''s edit: nothing is written (F2)' {
        $script:Dev.Pending[$script:MainId] = $script:MainXml -replace 'sprk_currency', 'sprk_someoneelse'
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'has unpublished changes that are not the edit this script would write'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
        $script:Dev.Pending[$script:MainId] | Should -Match 'sprk_someoneelse'
    }

    It '-AllowPendingCollateral does NOT override an overwrite of a target form''s unpublished changes' {
        $script:Dev.Pending[$script:QcId] = $script:QuickCreateXml -replace 'sprk_totalamount', 'sprk_someoneelse'
        $e = Get-Refusal { Invoke-Run $true $false $false $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'would overwrite'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'resumes: a target form whose pending text is exactly this run''s edit is written again and published' {
        $script:Dev.Pending[$script:QcId] = $script:QcWritten
        $out = Invoke-Run $true
        $script:Dev.Published.Count | Should -Be 1
        Get-MentionCount (Get-FormXml $script:QcId) | Should -Be 0
        $out | Should -Not -Match 'OVERWRITE RISK'
    }

    It 'refuses when another pending form or view of sprk_invoice would go live with the publish, unless -AllowPendingCollateral' {
        $script:Dev.Collateral = @("savedqueries: Someone's draft (v-other)")
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'would go live with the publish'
        $script:Dev.Writes.Count | Should -Be 0

        $script:Dev = New-Dev
        $script:Dev.Collateral = @("savedqueries: Someone's draft (v-other)")
        Invoke-Run $true $false $false $true | Out-Null
        $script:Dev.Published.Count | Should -Be 1
    }

    It 'stops before the publish when another pending item appears DURING the run (exit 1, not published)' {
        $script:Dev.CollateralAfterEdit = @("savedqueries: Appeared mid-run (v-late)")
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -Not -BeOfType ([EscalationException])
        $e.Message | Should -Match 'appeared and would go live too'
        $e.Message | Should -Match 'v-late'
        $script:Dev.Published.Count | Should -Be 0
        $script:Dev.Writes | Should -Not -Contain 'POST PublishXml'
    }

    It 'with -AllowPendingCollateral an item that appears during the run is accepted, and the publish goes ahead' {
        $script:Dev.CollateralAfterEdit = @("savedqueries: Appeared mid-run (v-late)")
        Invoke-Run $true $false $false $true | Out-Null
        $script:Dev.Published.Count | Should -Be 1
    }

    It 'stops before the publish when a target form is changed by someone else DURING the run (exit 1, not published)' {
        $script:Dev.AfterPatch = { param($id) if ($id -eq $script:QcId) { $script:Dev.Pending[$script:QcId] = $script:QcWritten -replace 'sprk_name', 'sprk_changedmidrun' } }
        $e = Get-Refusal { Invoke-Run $true | Out-Null }
        $e | Should -Not -BeOfType ([EscalationException])
        $e.Message | Should -Match 'a target form changed during the run'
        $script:Dev.Published.Count | Should -Be 0
    }
}

Describe 'the dev-only pre-flight (T4b)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'refuses -Apply on a host that is not dev BEFORE anything is initialised, read or written' {
        $e = Get-Refusal { Invoke-Run $true $false $false $false 'https://spaarke-prod.crm.dynamics.com' | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $e.Message | Should -Match 'dev only'
        $script:Dev.InitCalls.Count | Should -Be 0
        $script:Dev.Gets.Count | Should -Be 0
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.ContextCalls | Should -Be 0
    }

    It 'refuses -Apply -WhatIf on a host that is not dev too (the refusal does not depend on -WhatIf)' {
        $e = Get-Refusal { Invoke-Run $true $false $true $false 'https://spaarke-prod.crm.dynamics.com' | Out-Null }
        $e | Should -BeOfType ([EscalationException])
        $script:Dev.InitCalls.Count | Should -Be 0
    }

    It 'does not refuse the read-only modes on another host (report and -Verify are read-only), and dev proceeds to initialise' {
        (Get-PreflightRefusals $false 'https://spaarke-prod.crm.dynamics.com') | Should -BeNullOrEmpty
        (Get-PreflightRefusals $true $script:DevUrl) | Should -BeNullOrEmpty
        Invoke-Run $true | Out-Null
        $script:Dev.InitCalls.Count | Should -Be 1
        $script:Dev.InitCalls[0] | Should -Be $script:DevUrl
    }

    It 'the script entry calls only Invoke-Cleanup (the pre-flight lives inside it, so a test can reach it)' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $entry = $text.Substring($text.LastIndexOf('# Entry'))
        $entry | Should -Match 'Invoke-Cleanup'
        $entry | Should -Not -Match 'Get-PreflightRefusals|Initialize-EbillingSchema'
    }
}

Describe 'the shared publish context carries Consistency: Strong (F4)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true; $script:PublishContext = $null }

    It 'sets the header on the context the pending-collateral reads and the PublishXml POST use' {
        $ctx = Get-PublishContext
        $ctx.Headers['Consistency'] | Should -Be 'Strong'
        $ctx.Headers['Authorization'] | Should -Be 'Bearer x'
    }

    It 'the context passed to Get-EntityPublishCollateral and Invoke-PublishXml has it, through a whole -Apply' {
        Invoke-Run $true | Out-Null
        $script:Dev.CollateralCalls.Count | Should -BeGreaterThan 0
        foreach ($c in $script:Dev.CollateralCalls) { $c.Consistency | Should -Be 'Strong' }
        $script:Dev.PublishContexts.Count | Should -Be 1
        $script:Dev.PublishContexts[0].Headers['Consistency'] | Should -Be 'Strong'
    }

    It 'makes the context once and reuses it' {
        $a = Get-PublishContext; $b = Get-PublishContext
        $script:Dev.ContextCalls | Should -Be 1
        [object]::ReferenceEquals($a, $b) | Should -BeTrue
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
