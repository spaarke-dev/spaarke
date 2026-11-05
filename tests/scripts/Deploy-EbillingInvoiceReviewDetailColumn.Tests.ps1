# Pester 5. Run: Invoke-Pester tests/scripts/Deploy-EbillingInvoiceReviewDetailColumn.Tests.ps1
# ebilling-r1 082c-2 script fix (found by the ontology peer when it applied the column on dev, 2026-11-05). Two defects the text-only
# InvoiceReviewDetailScriptAgreementTests could not see, because they only show when PowerShell RUNS the functions:
#   1. $Options is [ordered]@{ 659490000 = ... } with int keys, and $Options[$int] on an OrderedDictionary indexes by POSITION, not by key, so it
#      returned $null: New-ReviewDetailDef built the three options with BLANK labels and Get-ColumnGaps compared against $null.
#   2. Get-SubmissionHoldReasons returned an empty HashSet, which the pipeline unrolls to $null, so UnionWith($null) threw and -Verify reported
#      "the reverse invariant could not be read".
# The script cannot be dot-sourced (it begins with `using module` and runs against the environment), so its functions and the constants they read
# are lifted out of its own syntax tree and run here against a test double of the Dataverse calls (no environment, no network).

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingInvoiceReviewDetailColumn.ps1'

    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'Table', 'Column', 'ColumnSchemaName', 'Options', 'FirmMissingReason', 'ProcedureReasons', 'RulesNotChecked', 'OverriddenReason', 'Description', 'InReviewStatusCode', 'EntitySet'
    $wantedFunctions = 'New-ReviewDetailDef', 'Get-ColumnGaps', 'Get-InvariantBreaks', 'Get-SubmissionHoldReasons'

    $pieces = [System.Collections.Generic.List[string]]::new()
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($statement in $ast.EndBlock.Statements) {
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

    # The Dataverse calls, as a test double. Get-DvOrNull answers the attribute read and the picklist read from $script:Fake.
    function New-Label([string]$Text) {
        return @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ Label = $Text; LanguageCode = 1033 }) }
    }
    function Get-DvOrNull([string]$Path, [int]$Retries) {
        if ($Path -like '*PicklistAttributeMetadata*') { return $script:Fake.Picklist }
        return $script:Fake.Attribute
    }
    function Invoke-Dv([string]$Path) { return $script:Fake.Rows }

    . ([scriptblock]::Create($pieces -join "`n"))

    $script:Expected = [ordered]@{
        659490000 = 'Waiting for billing terms'
        659490001 = 'Billing terms could not be checked'
        659490002 = 'Waiting for the firm to be confirmed'
    }

    function New-Picklist([hashtable]$Labels, [bool]$IsGlobal = $false) {
        @{
            OptionSet = @{
                IsGlobal = $IsGlobal
                Options  = @($Labels.GetEnumerator() | Sort-Object Name | ForEach-Object {
                        @{ Value = [int]$_.Key; Label = @{ UserLocalizedLabel = @{ Label = $_.Value } } }
                    })
            }
        }
    }
}

Describe 'New-ReviewDetailDef: the column definition the script sends' {
    It 'carries the three values, each with its label (an int key must read by key, never by position)' {
        $def = New-ReviewDetailDef
        $options = @($def.OptionSet.Options)

        $options.Count | Should -Be 3
        foreach ($i in 0..2) {
            $value = [int]@($script:Expected.Keys)[$i]
            $options[$i].Value | Should -Be $value
            $options[$i].Label.LocalizedLabels[0].Label | Should -Be $script:Expected[[object]$value]
        }
    }

    It 'is a local choice, optional, named as the BFF writes it' {
        $def = New-ReviewDetailDef
        $def.OptionSet.IsGlobal | Should -BeFalse
        $def.SchemaName | Should -Be 'sprk_ReviewDetail'
        $def.RequiredLevel.Value | Should -Be 'None'
    }
}

Describe 'Get-ColumnGaps: the -Verify comparison of the live options' {
    BeforeEach {
        $script:Fake = @{ Attribute = @{ LogicalName = 'sprk_reviewdetail'; AttributeType = 'Picklist'; IsSecured = $false }; Picklist = $null; Rows = @{ value = @() } }
    }

    It 'finds no gap when the live options carry the three pinned labels' {
        $script:Fake.Picklist = New-Picklist @{ 659490000 = 'Waiting for billing terms'; 659490001 = 'Billing terms could not be checked'; 659490002 = 'Waiting for the firm to be confirmed' }
        # Assigned directly, as the script does: the function returns the list wrapped (, $gaps) so an empty one survives the pipeline.
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 0
    }

    It 'names a BLANK live label against the expected label (the old script compared against $null and so could not tell)' {
        $script:Fake.Picklist = New-Picklist @{ 659490000 = ''; 659490001 = 'Billing terms could not be checked'; 659490002 = 'Waiting for the firm to be confirmed' }
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike "*option 659490000 is '', expected 'Waiting for billing terms'"
    }

    It 'names a missing option with its expected label, and an extra option' {
        $script:Fake.Picklist = New-Picklist @{ 659490000 = 'Waiting for billing terms'; 659490001 = 'Billing terms could not be checked'; 659490009 = 'On hold' }
        $gaps = Get-ColumnGaps
        $gaps | Should -Contain "sprk_invoice.sprk_reviewdetail has no option 659490002 ('Waiting for the firm to be confirmed')"
        ($gaps -join '|') | Should -BeLike '*has an extra option 659490009 (''On hold'')*'
    }
}

Describe 'Get-SubmissionHoldReasons: the reverse invariant read' {
    It 'returns an EMPTY set (not $null) for a submission with no hold, and UnionWith accepts it' {
        foreach ($submission in @(
                @{ sprk_outcomereasoncode = $null; sprk_findingsjson = $null }
                @{ sprk_outcomereasoncode = 'ebilling.submission.accepted'; sprk_findingsjson = '   ' }
                @{ sprk_outcomereasoncode = $null; sprk_findingsjson = '{"pipelineFindings":[],"lineFindings":[]}' })) {
            $r = Get-SubmissionHoldReasons ([pscustomobject]$submission)
            $null -eq $r | Should -BeFalse
            ($r -is [System.Collections.Generic.HashSet[string]]) | Should -BeTrue
            $r.Count | Should -Be 0

            $owner = [System.Collections.Generic.HashSet[string]]::new()
            { $owner.UnionWith((Get-SubmissionHoldReasons ([pscustomobject]$submission))) } | Should -Not -Throw
            $owner.Count | Should -Be 0
        }
    }

    It 'collects the outcome reason, the findings reasons and the not-checked line cause' {
        $json = '{"reasonCode":"ebilling.submission.procedure_gap","pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing"},null],' +
                '"lineFindings":[{"code":"rules.not_checked","cause":"ebilling.submission.procedures_unresolved"},{"code":"other","cause":"ebilling.submission.firm_missing"}]}'
        $r = Get-SubmissionHoldReasons ([pscustomobject]@{ sprk_outcomereasoncode = 'ebilling.submission.firm_missing'; sprk_findingsjson = $json })
        $r.Count | Should -Be 3
        $r.Contains('ebilling.submission.firm_missing') | Should -BeTrue
        $r.Contains('ebilling.submission.procedure_gap') | Should -BeTrue
        $r.Contains('ebilling.submission.procedures_unresolved') | Should -BeTrue
    }

    It 'task 114: a submission released by an Override holds no Procedure reason (the gap stays in its findings as evidence); the firm marker still holds' {
        $gap = '{"reasonCode":"ebilling.submission.procedure_gap","pipelineFindings":[{"reasonCode":"ebilling.submission.procedure_gap"}],' +
               '"lineFindings":[{"code":"rules.not_checked","cause":"ebilling.submission.procedures_unresolved"}]}'
        $released = Get-SubmissionHoldReasons ([pscustomobject]@{ sprk_outcomereasoncode = 'ebilling.submission.overridden'; sprk_findingsjson = $gap })
        $released.Count | Should -Be 0
        $held = Get-SubmissionHoldReasons ([pscustomobject]@{ sprk_outcomereasoncode = $null; sprk_findingsjson = $gap })
        $held.Contains('ebilling.submission.procedure_gap') | Should -BeTrue
        $firm = '{"pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing"},{"reasonCode":"ebilling.submission.procedure_gap"}]}'
        $both = Get-SubmissionHoldReasons ([pscustomobject]@{ sprk_outcomereasoncode = 'ebilling.submission.overridden'; sprk_findingsjson = $firm })
        $both.Count | Should -Be 1
        $both.Contains('ebilling.submission.firm_missing') | Should -BeTrue
    }

    It 'holds findings that cannot be parsed as procedures not checked (fail closed)' {
        $r = Get-SubmissionHoldReasons ([pscustomobject]@{ sprk_outcomereasoncode = $null; sprk_findingsjson = '{not json' })
        $r.Count | Should -Be 1
        $r.Contains('ebilling.submission.procedures_unresolved') | Should -BeTrue
    }
}

Describe 'Get-InvariantBreaks: an empty read is an empty list, not $null' {
    It 'returns an empty array that survives assignment' {
        $script:Fake = @{ Rows = @{ value = @() } }
        $r = Get-InvariantBreaks
        $null -eq $r | Should -BeFalse
        $r.Count | Should -Be 0
    }

    It 'returns each row' {
        $script:Fake = @{ Rows = @{ value = @([pscustomobject]@{ sprk_invoiceid = 'a'; statuscode = 2 }) } }
        $r = Get-InvariantBreaks
        $r.Count | Should -Be 1
        $r[0].sprk_invoiceid | Should -Be 'a'
    }
}
