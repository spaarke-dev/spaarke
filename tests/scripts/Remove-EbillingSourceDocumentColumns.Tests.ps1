# Pester 5. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Remove-EbillingSourceDocumentColumns.Tests.ps1
# ebilling-r1 task 116. Remove-EbillingSourceDocumentColumns.ps1 is read-only by default and refuses -Delete while anything still uses the
# column. These tests run the script's own functions (lifted out of its syntax tree, as Deploy-EbillingProcedureVersionPublishedOn.Tests.ps1
# does) against a test double of the Dataverse calls: no environment, no network.
#   R1  the census reads both columns and counts what holds a value / mentions the column / depends on it;
#   R2  the verdicts: READY only when no row holds a value, no form or view mentions the column, nothing depends on it, and the
#       relationship is the expected one; an already-removed column is not a blocker;
#   R3  -Delete is refused (every reason named) without the owner's approval, without -Confirm, or while any blocker stands;
#   R4  read-only by construction: the census can only GET, and the only -Method uses are the DELETE and the PublishXml POST inside
#       the two removal functions.

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Remove-EbillingSourceDocumentColumns.ps1'

    $tokens = $null; $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'Column', 'Targets', 'OtherOwners'
    $wantedFunctions = 'Read-Dv', 'Read-DvOrNull', 'Get-ComponentKind', 'Get-Census', 'Get-ColumnBlockers', 'Get-DeleteRefusals'

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

    # Test doubles of the module's calls. $script:Fake is set per test.
    function Invoke-Dv {
        param([string]$Path, [string]$Method = 'GET', $Body = $null)
        if ($Method -ne 'GET') { throw "the census wrote: $Method $Path" }
        $script:Fake.Calls.Add($Path)
        foreach ($rule in $script:Fake.Responses) { if ($Path -match $rule.Match) { return $rule.Result } }
        return [pscustomobject]@{ value = @() }
    }
    function Get-DvOrNull { param([string]$Path, [int]$Retries = 0) return Invoke-Dv -Path $Path }
    function Get-Attr { param([string]$Table, [string]$Column, [string]$Select) if ($script:Fake.Present -contains $Table) { return [pscustomobject]@{ MetadataId = "attr-$Table"; AttributeType = 'Lookup' } } return $null }
    function Get-RowCountExact { param([string]$EntitySet, [string]$IdColumn, [string]$Filter = '') if ($Filter) { return $script:Fake.NonNull[$EntitySet] } return $script:Fake.Rows[$EntitySet] }

    . ([scriptblock]::Create($pieces -join "`n"))

    function New-Fake {
        $f = @{
            Present   = @('sprk_ruleset', 'sprk_ratecard')
            Rows      = @{ sprk_rulesets = 0; sprk_ratecards = 0 }
            NonNull   = @{ sprk_rulesets = 0; sprk_ratecards = 0 }
            Calls     = [System.Collections.Generic.List[string]]::new()
            Responses = @(
                @{ Match = 'ManyToOneRelationships.*sprk_ruleset|sprk_ruleset.*ManyToOneRelationships'; Result = [pscustomobject]@{ value = @([pscustomobject]@{ SchemaName = 'sprk_document_ruleset_sourcedocument'; MetadataId = 'rel-1' }) } }
                @{ Match = 'ManyToOneRelationships.*sprk_ratecard|sprk_ratecard.*ManyToOneRelationships'; Result = [pscustomobject]@{ value = @([pscustomobject]@{ SchemaName = 'sprk_document_ratecard_sourcedocument'; MetadataId = 'rel-2' }) } }
            )
        }
        return $f
    }
    function New-Census([string]$Table, [hashtable]$Over = @{}) {
        $t = @($Targets | Where-Object { $_.Table -eq $Table })[0]
        $c = [ordered]@{
            Table = $Table; ColumnPresent = $true; RowCount = 0; NonNullCount = 0; Forms = @(); Views = @(); PersonalViews = @(); OtherReferences = @(); Dependencies = @()
            Relationships = @([pscustomobject]@{ SchemaName = $t.Relationship; MetadataId = 'rel' })
        }
        foreach ($k in $Over.Keys) { $c[$k] = $Over[$k] }
        return [pscustomobject]$c
    }
}

Describe 'the targets' {
    It 'name exactly the two tables and the column, not sprk_storedfile' {
        @($Targets | ForEach-Object { $_.Table }) | Should -Be @('sprk_ruleset', 'sprk_ratecard')
        $Column | Should -Be 'sprk_sourcedocument'
        $OtherOwners | Should -Contain 'sprk_storedfile'
    }
}

Describe 'Get-Census (R1)' {
    BeforeEach { $script:Fake = New-Fake }

    It 'reads a clean column as present with zero values, forms, views and dependents, and only GETs' {
        $c = Get-Census $Targets[0]
        $c.ColumnPresent | Should -BeTrue
        $c.RowCount | Should -Be 0
        $c.NonNullCount | Should -Be 0
        @($c.Forms).Count | Should -Be 0
        @($c.Dependencies).Count | Should -Be 0
        @($c.Relationships)[0].SchemaName | Should -Be 'sprk_document_ruleset_sourcedocument'
        $script:Fake.Calls | Should -Contain 'RetrieveDependenciesForDelete(ObjectId=attr-sprk_ruleset,ComponentType=2)'
    }

    It 'counts rows that hold a value separately from the rows' {
        $script:Fake.Rows['sprk_rulesets'] = 5
        $script:Fake.NonNull['sprk_rulesets'] = 2
        $c = Get-Census $Targets[0]
        $c.RowCount | Should -Be 5
        $c.NonNullCount | Should -Be 2
    }

    It 'names a form that mentions the column and the dependent behind it' {
        $script:Fake.Responses = @(
            @{ Match = 'systemforms\?.*objecttypecode eq .sprk_ratecard.*contains'; Result = [pscustomobject]@{ value = @([pscustomobject]@{ name = 'Information'; formid = 'f-1'; type = 2 }) } }
            @{ Match = 'RetrieveDependenciesForDelete'; Result = [pscustomobject]@{ value = @([pscustomobject]@{ dependentcomponenttype = 60; dependentcomponentobjectid = 'f-1' }) } }
            @{ Match = 'systemforms\(f-1\)'; Result = [pscustomobject]@{ name = 'Information'; objecttypecode = 'sprk_ratecard' } }
            @{ Match = 'ManyToOneRelationships'; Result = [pscustomobject]@{ value = @([pscustomobject]@{ SchemaName = 'sprk_document_ratecard_sourcedocument'; MetadataId = 'r' }) } }
        )
        $c = Get-Census $Targets[1]
        @($c.Forms).Count | Should -Be 1
        $c.Forms[0].Id | Should -Be 'f-1'
        @($c.Dependencies).Count | Should -Be 1
        $c.Dependencies[0].Kind | Should -Be 'system form'
        $c.Dependencies[0].Label | Should -BeLike "*sprk_ratecard form 'Information'*"
    }

    It 'ignores sprk_storedfile''s own sprk_sourcedocument as another table''s reference, but reports any other table' {
        $script:Fake.Responses = @(
            @{ Match = 'systemforms\?.*objecttypecode ne'; Result = [pscustomobject]@{ value = @(
                [pscustomobject]@{ name = 'Stored file'; formid = 'f-9'; objecttypecode = 'sprk_storedfile' }
                [pscustomobject]@{ name = 'Other'; formid = 'f-8'; objecttypecode = 'sprk_other' }) } }
            @{ Match = 'ManyToOneRelationships'; Result = [pscustomobject]@{ value = @([pscustomobject]@{ SchemaName = 'sprk_document_ruleset_sourcedocument'; MetadataId = 'r' }) } }
        )
        $c = Get-Census $Targets[0]
        @($c.OtherReferences) | Should -Be @("form 'Other' of sprk_other")
    }

    It 'reports a column that is already gone as not present, with no row read' {
        $script:Fake.Present = @('sprk_ratecard')
        $c = Get-Census $Targets[0]
        $c.ColumnPresent | Should -BeFalse
        $script:Fake.Calls.Count | Should -Be 0
    }

    It 'keeps a failed count as unknown (null), never zero' {
        $script:Fake.NonNull['sprk_rulesets'] = $null
        (Get-Census $Targets[0]).NonNullCount | Should -BeNullOrEmpty
    }
}

Describe 'Get-ColumnBlockers (R2)' {
    It 'is empty for a clean column behind the expected relationship (READY)' {
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset') $Targets[0]).Count | Should -Be 0
        @(Get-ColumnBlockers (New-Census 'sprk_ratecard') $Targets[1]).Count | Should -Be 0
    }

    It 'is empty for a column that is already gone' {
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ ColumnPresent = $false; Relationships = @() }) $Targets[0]).Count | Should -Be 0
    }

    It 'does not count the table''s rows when none holds a value' {
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ RowCount = 40; NonNullCount = 0 }) $Targets[0]).Count | Should -Be 0
    }

    It 'blocks on a row that holds a value, naming how many' {
        $b = @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ NonNullCount = 3 }) $Targets[0])
        $b.Count | Should -Be 1
        $b[0] | Should -BeLike '3 sprk_ruleset row(s) hold a value*'
    }

    It 'blocks when the rows holding a value could not be counted' {
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ NonNullCount = $null }) $Targets[0]).Count | Should -Be 1
    }

    It 'blocks on a form, a view, a personal view, another table and a dependent, each named' {
        $c = New-Census 'sprk_ratecard' @{
            Forms = @([pscustomobject]@{ Name = 'Information'; Id = 'f-1'; Type = 2 })
            Views = @([pscustomobject]@{ Name = 'Active Rate Cards'; Id = 'v-1' })
            PersonalViews = @([pscustomobject]@{ Name = 'Mine'; Id = 'u-1' })
            OtherReferences = @("form 'Other' of sprk_other")
            Dependencies = @([pscustomobject]@{ Kind = 'view'; Label = 'v-1' })
        }
        $b = @(Get-ColumnBlockers $c $Targets[1])
        $b.Count | Should -Be 5
        ($b -join '|') | Should -Match "form 'Information'"
        ($b -join '|') | Should -Match "view 'Active Rate Cards'"
        ($b -join '|') | Should -Match "personal view 'Mine'"
        ($b -join '|') | Should -Match 'sprk_other'
        ($b -join '|') | Should -Match 'dependent view v-1'
    }

    It 'blocks when the relationship is not exactly the expected one, or there is more than one' {
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ Relationships = @([pscustomobject]@{ SchemaName = 'sprk_other_rel'; MetadataId = 'x' }) }) $Targets[0]).Count | Should -Be 1
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ Relationships = @() }) $Targets[0]).Count | Should -Be 1
        $two = @([pscustomobject]@{ SchemaName = 'sprk_document_ruleset_sourcedocument'; MetadataId = 'a' }, [pscustomobject]@{ SchemaName = 'sprk_document_ruleset_sourcedocument'; MetadataId = 'b' })
        @(Get-ColumnBlockers (New-Census 'sprk_ruleset' @{ Relationships = $two }) $Targets[0]).Count | Should -Be 1
    }
}

Describe 'Get-DeleteRefusals (R3)' {
    BeforeAll {
        $script:Clean = @((New-Census 'sprk_ruleset'), (New-Census 'sprk_ratecard'))
    }

    It 'allows the delete only with the owner''s approval, -Confirm and a clean census' {
        @(Get-DeleteRefusals $script:Clean '2026-11-10 owner' $true $Targets).Count | Should -Be 0
    }

    It 'refuses without the approval text (empty, blank or missing)' {
        foreach ($approval in @('', '   ', $null)) {
            $r = @(Get-DeleteRefusals $script:Clean $approval $true $Targets)
            $r.Count | Should -Be 1
            $r[0] | Should -BeLike '*-OwnerApproval*'
        }
    }

    It 'refuses without -Confirm' {
        $r = @(Get-DeleteRefusals $script:Clean 'owner' $false $Targets)
        $r.Count | Should -Be 1
        $r[0] | Should -Be '-Confirm is required'
    }

    It 'refuses while either column still has a value, a form, a view or a dependent, even with approval and -Confirm' {
        $dirty = @((New-Census 'sprk_ruleset'), (New-Census 'sprk_ratecard' @{ NonNullCount = 1; Forms = @([pscustomobject]@{ Name = 'Information'; Id = 'f'; Type = 2 }) }))
        $r = @(Get-DeleteRefusals $dirty 'owner' $true $Targets)
        $r.Count | Should -Be 2
        ($r -join '|') | Should -Match 'sprk_ratecard'
        ($r -join '|') | Should -Not -Match 'sprk_ruleset'
    }

    It 'names every reason at once (approval, confirm and blockers)' {
        $dirty = @((New-Census 'sprk_ruleset' @{ NonNullCount = 2 }), (New-Census 'sprk_ratecard'))
        @(Get-DeleteRefusals $dirty '' $false $Targets).Count | Should -Be 3
    }

    It 'refuses a missing census rather than treating it as clean' {
        $r = @(Get-DeleteRefusals @((New-Census 'sprk_ruleset')) 'owner' $true $Targets)
        $r.Count | Should -Be 1
        $r[0] | Should -BeLike 'sprk_ratecard : no census'
    }

    It 'allows the delete when one column is already gone and the other is clean' {
        $half = @((New-Census 'sprk_ruleset' @{ ColumnPresent = $false; Relationships = @() }), (New-Census 'sprk_ratecard'))
        @(Get-DeleteRefusals $half 'owner' $true $Targets).Count | Should -Be 0
    }
}

Describe 'read-only by construction (R4)' {
    It 'Read-Dv and the census use no -Method at all' {
        foreach ($name in 'Read-Dv', 'Read-DvOrNull', 'Get-Census') {
            $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)[0]
            $methods = $fn.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandParameterAst] -and $n.ParameterName -eq 'Method' }, $true)
            $methods.Count | Should -Be 0 -Because "$name must only GET"
        }
    }

    It 'the only -Method uses are DELETE in Remove-SourceDocumentLookup and POST (PublishXml) in Publish-Tables' {
        $uses = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandParameterAst] -and $n.ParameterName -eq 'Method' }, $true)
        $uses.Count | Should -Be 2
        foreach ($u in $uses) {
            $cmd = $u.Parent
            $fn = $cmd
            while ($fn -and $fn -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $fn = $fn.Parent }
            $fn | Should -Not -BeNullOrEmpty
            $value = $cmd.CommandElements[[array]::IndexOf($cmd.CommandElements, $u) + 1].Extent.Text
            if ($fn.Name -eq 'Remove-SourceDocumentLookup') { $value | Should -Be 'DELETE' }
            elseif ($fn.Name -eq 'Publish-Tables') { $value | Should -Be 'POST' }
            else { throw "-Method $value used in $($fn.Name)" }
        }
    }

    It 'both writing functions ask ShouldProcess before they write' {
        foreach ($name in 'Remove-SourceDocumentLookup', 'Publish-Tables') {
            $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)[0]
            $fn.Extent.Text | Should -Match 'ShouldProcess'
            $fn.Extent.Text.IndexOf('ShouldProcess') | Should -BeLessThan $fn.Extent.Text.IndexOf('-Method')
        }
    }

    It 'the removal functions are called only after Get-DeleteRefusals, inside the -Delete path' {
        $text = $script:Ast.Extent.Text
        $text.IndexOf('Get-DeleteRefusals $census') | Should -BeGreaterThan 0
        $text.IndexOf('Remove-SourceDocumentLookup (@') | Should -BeGreaterThan $text.IndexOf('Get-DeleteRefusals $census')
        $text.IndexOf('if (-not $Delete)') | Should -BeLessThan $text.IndexOf('Get-DeleteRefusals $census')
    }
}
