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
