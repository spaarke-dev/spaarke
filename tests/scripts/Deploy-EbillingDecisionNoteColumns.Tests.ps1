# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Deploy-EbillingDecisionNoteColumns.Tests.ps1
# ebilling-r1 095f-2s (EB-D-240 / EB-D-242): the schema script for sprk_invoice.sprk_decisionnote and sprk_invoicecorrection.sprk_note.
# The script cannot be dot-sourced (it begins with `using module` and runs against the environment), so its functions and the constants they
# read are lifted out of its own syntax tree and run here. The real EbillingSchema.psm1 is imported and its Dataverse calls are mocked inside
# the module (no environment, no network), so the -WhatIf and "-Apply on an existing column" proofs run the module's real Add-Column.
# The text checks pin the parts that only run live: the mode guards, the entity-scoped publish and the module that sends Consistency: Strong.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Deploy-EbillingDecisionNoteColumns.ps1'
    $script:ModulePath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'EbillingSchema.psm1'
    $script:Text = Get-Content -Raw $script:ScriptPath

    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'ColumnMaxLength', 'EntityComponentType', 'SystemSolutions', 'ColumnSpecs'
    $wantedFunctions = 'New-NoteColumnDef', 'Select-OwningSolution', 'Get-PublishStep', 'Get-PublishParameterXml', 'Get-TableSolutions', 'Get-ColumnGaps', 'Invoke-ApplyColumn'

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

    # The module's exception class is not exported by Import-Module: a test double with the same name and shape.
    class EscalationException : System.Exception {
        EscalationException([string]$message) : base($message) {}
    }
    # The real module: Add-Column, Initialize-EbillingSchema, Set-EbEntity, New-MemoDef. Its Dataverse reads and writes are mocked per test.
    Import-Module $script:ModulePath -Force

    $EnvironmentUrl = 'https://example.invalid'
    . ([scriptblock]::Create($pieces -join "`n"))

    function New-Solution([string]$Name, [bool]$Managed = $false) { [pscustomobject]@{ uniquename = $Name; ismanaged = $Managed } }
}

AfterAll { Remove-Module EbillingSchema -ErrorAction SilentlyContinue; Remove-Variable EbNoteState -Scope Global -ErrorAction SilentlyContinue }

Describe 'ColumnSpecs and New-NoteColumnDef: the two columns the script creates' {
    It 'is exactly the two columns, on their tables' {
        $ColumnSpecs.Count | Should -Be 2
        $pairs = @($ColumnSpecs | ForEach-Object { "$($_.Table).$($_.Column)" })
        $pairs | Should -Be @('sprk_invoice.sprk_decisionnote', 'sprk_invoicecorrection.sprk_note')
    }

    It 'defines <Column> as an optional, unsecured Memo of 2000 with its label' -ForEach @(
        @{ Index = 0; Column = 'sprk_decisionnote'; Schema = 'sprk_DecisionNote'; Label = 'Decision note' }
        @{ Index = 1; Column = 'sprk_note'; Schema = 'sprk_Note'; Label = 'Note' }
    ) {
        $spec = $ColumnSpecs[$Index]
        $def = New-NoteColumnDef $spec
        $def.'@odata.type' | Should -Be 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
        $def.SchemaName | Should -Be $Schema
        $def.SchemaName.ToLowerInvariant() | Should -Be $Column
        $def.DisplayName.LocalizedLabels[0].Label | Should -Be $Label
        $def.MaxLength | Should -Be 2000
        $def.RequiredLevel.Value | Should -Be 'None'
        $def.ContainsKey('IsSecured') | Should -BeFalse
    }

    It 'defaults to SpaarkeMaster' {
        $script:Text | Should -Match "\[string\]\`$SolutionUniqueName = 'SpaarkeMaster'"
    }
}

Describe 'Select-OwningSolution: the MSCRM.SolutionUniqueName the writes carry' {
    It 'accepts the requested unmanaged solution that contains the table' {
        $solutions = @((New-Solution 'Default'), (New-Solution 'SpaarkeMaster'), (New-Solution 'Other'))
        Select-OwningSolution $solutions 'SpaarkeMaster' 'sprk_invoice' | Should -Be 'SpaarkeMaster'
    }

    It 'escalates (must fire) when <Case>' -ForEach @(
        @{ Case = 'the named solution does not contain the table'; Solutions = @(,@('Other', $false)); Requested = 'SpaarkeMaster' }
        @{ Case = 'the named solution is managed'; Solutions = @(,@('SpaarkeMaster', $true)); Requested = 'SpaarkeMaster' }
        @{ Case = 'the named solution is a system solution'; Solutions = @(,@('Default', $false)); Requested = 'Default' }
        @{ Case = 'no solution is named'; Solutions = @(,@('SpaarkeMaster', $false)); Requested = '' }
    ) {
        $thrown = $null
        # -ForEach data is built at discovery, before BeforeAll defines New-Solution: the rows are (name, managed) pairs.
        $built = @($Solutions | ForEach-Object { New-Solution $_[0] $_[1] })
        try { Select-OwningSolution $built $Requested 'sprk_invoicecorrection' } catch { $thrown = $_.Exception }
        $thrown | Should -Not -BeNullOrEmpty
        $thrown.GetType().Name | Should -Be 'EscalationException'
        $thrown.Message | Should -BeLike '*does not contain sprk_invoicecorrection*'
    }
}

Describe 'Get-TableSolutions: the solutions read from solutioncomponents' {
    BeforeEach { $script:Paths = [System.Collections.Generic.List[string]]::new() }

    It 'reads the entity components of the named table and each solution once' {
        $fake = @{
            Entity     = @{ MetadataId = 'aaaaaaaa-0000-0000-0000-000000000001' }
            Components = @(@{ _solutionid_value = 's1' }, @{ _solutionid_value = 's2' }, @{ _solutionid_value = 's1' })
            Solutions  = @{ s1 = (New-Solution 'SpaarkeMaster'); s2 = (New-Solution 'Default') }
        }
        Mock Get-DvOrNull {
            $script:Paths.Add($Path)
            if ($Path -like 'solutions(*') { return $fake.Solutions[($Path -split '[()]')[1]] }
            return $fake.Entity
        }
        Mock Invoke-Dv { $script:Paths.Add($Path); return @{ value = $fake.Components } }
        $r = Get-TableSolutions 'sprk_invoicecorrection'
        @($r | ForEach-Object uniquename) | Should -Be @('SpaarkeMaster', 'Default')
        $script:Paths[0] | Should -Be "EntityDefinitions(LogicalName='sprk_invoicecorrection')?`$select=MetadataId"
        ($script:Paths | Where-Object { $_ -like 'solutioncomponents*' }) | Should -BeLike '*objectid eq aaaaaaaa-0000-0000-0000-000000000001 and componenttype eq 1*'
        @($script:Paths | Where-Object { $_ -like 'solutions(*' }).Count | Should -Be 2
    }

    It 'throws when the table does not exist' {
        Mock Get-DvOrNull { $null }
        { Get-TableSolutions 'sprk_invoice' } | Should -Throw '*does not exist*'
    }
}

Describe 'Get-ColumnGaps: the -Verify check (column, Memo, MaxLength 2000, not secured), both columns' {
    BeforeEach {
        $script:Paths = [System.Collections.Generic.List[string]]::new()
        $script:Fake = @{
            Attribute = @{ AttributeType = 'Memo'; IsSecured = $false }
            Memo      = @{ MaxLength = 2000 }
        }
        Mock Get-DvOrNull {
            $script:Paths.Add($Path)
            if ($Path -like '*MemoAttributeMetadata*') { return $script:Fake.Memo }
            return $script:Fake.Attribute
        }
    }

    It 'finds no gap when <Column> is as specified (must not fire)' -ForEach @(
        @{ Index = 0; Column = 'sprk_decisionnote' }
        @{ Index = 1; Column = 'sprk_note' }
    ) {
        $gaps = Get-ColumnGaps $ColumnSpecs[$Index]
        $gaps.Count | Should -Be 0
    }

    It 'names a missing column on its own table (must fire)' {
        $script:Fake.Attribute = $null
        $gaps = Get-ColumnGaps $ColumnSpecs[1]
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -Be 'sprk_invoicecorrection.sprk_note does not exist'
    }

    It 'names another type and stops there (must fire)' {
        $script:Fake.Attribute.AttributeType = 'String'
        $gaps = Get-ColumnGaps $ColumnSpecs[0]
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike "sprk_invoice.sprk_decisionnote is 'String', not Memo"
    }

    It 'names another MaxLength (must fire)' {
        $script:Fake.Memo = @{ MaxLength = 4000 }
        $gaps = Get-ColumnGaps $ColumnSpecs[0]
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike '*MaxLength 4000, expected 2000'
    }

    It 'names a field-secured column (must fire)' {
        $script:Fake.Attribute.IsSecured = $true
        $gaps = Get-ColumnGaps $ColumnSpecs[1]
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike '*is field-secured*'
    }

    It 'reads the length through the Memo metadata cast and IsSecured from the attribute' {
        $null = Get-ColumnGaps $ColumnSpecs[1]
        $script:Paths | Should -Contain "EntityDefinitions(LogicalName='sprk_invoicecorrection')/Attributes(LogicalName='sprk_note')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
        ($script:Paths -join '|') | Should -BeLike '*IsSecured*'
    }
}

Describe 'Invoke-ApplyColumn with the real Add-Column: -WhatIf writes nothing, -Apply on an existing column is a no-op' {
    BeforeEach {
        $global:EbNoteState = @{ Posts = 0; Created = $false; Exists = $false }
        # Module-internal: Add-Column's reads and writes. A column "exists" once $global:EbNoteState.Created is set or the test says so.
        Mock -ModuleName EbillingSchema Invoke-Dv { if ($Method -eq 'POST') { $global:EbNoteState.Posts++; $global:EbNoteState.Created = $true } }
        Mock -ModuleName EbillingSchema Get-DvOrNull { if ($global:EbNoteState.Exists -or $global:EbNoteState.Created) { return [pscustomobject]@{ LogicalName = 'x'; AttributeType = 'Memo' } } return $null }
        # Test scope: Get-ColumnGaps (called for an existing column) reads through the exported Get-DvOrNull.
        Mock Get-DvOrNull {
            if ($Path -like '*MemoAttributeMetadata*') { return [pscustomobject]@{ MaxLength = 2000 } }
            return [pscustomobject]@{ AttributeType = 'Memo'; IsSecured = $false }
        }
    }

    It '-WhatIf (dry run) sends no write for either column and reports both as would-create (must not fire a POST)' {
        $s = Initialize-EbillingSchema -EnvironmentUrl 'https://example.invalid' -SolutionUniqueName 'SpaarkeMaster' -DryRun -Entity 'sprk_invoice'
        $script:Created = $s.Created; $script:Changed = $s.Changed; $script:Skipped = $s.Skipped
        foreach ($spec in $ColumnSpecs) { Invoke-ApplyColumn $spec }
        $global:EbNoteState.Posts | Should -Be 0
        $s.Created.Count | Should -Be 2
        $s.Created | Should -Be @('sprk_invoice.sprk_decisionnote', 'sprk_invoicecorrection.sprk_note')
        Get-PublishStep $true ($s.Created.Count + $s.Changed.Count) | Should -Be 'whatif'
    }

    It '-Apply on columns that already exist creates nothing and changes nothing (no-op)' {
        $global:EbNoteState.Exists = $true
        $s = Initialize-EbillingSchema -EnvironmentUrl 'https://example.invalid' -SolutionUniqueName 'SpaarkeMaster' -Entity 'sprk_invoice'
        $script:Created = $s.Created; $script:Changed = $s.Changed; $script:Skipped = $s.Skipped
        foreach ($spec in $ColumnSpecs) { Invoke-ApplyColumn $spec }
        $global:EbNoteState.Posts | Should -Be 0
        $s.Created.Count | Should -Be 0
        $s.Changed.Count | Should -Be 0
        $s.Skipped.Count | Should -Be 2
        Get-PublishStep $false ($s.Created.Count + $s.Changed.Count) | Should -Be 'republish'
    }

    It '-Apply on a missing column creates it on the right table, once (must fire a POST)' {
        $s = Initialize-EbillingSchema -EnvironmentUrl 'https://example.invalid' -SolutionUniqueName 'SpaarkeMaster' -Entity 'sprk_invoice'
        $script:Created = $s.Created; $script:Changed = $s.Changed; $script:Skipped = $s.Skipped
        Invoke-ApplyColumn $ColumnSpecs[1]
        $global:EbNoteState.Posts | Should -Be 1
        $s.Created | Should -Be @('sprk_invoicecorrection.sprk_note')
        Should -Invoke -ModuleName EbillingSchema Invoke-Dv -Times 1 -ParameterFilter {
            $Method -eq 'POST' -and $Path -eq "EntityDefinitions(LogicalName='sprk_invoicecorrection')/Attributes" -and $Body.SchemaName -eq 'sprk_Note'
        }
    }

    It 'escalates (must fire) when an existing column is not as specified, and writes nothing' {
        $global:EbNoteState.Exists = $true
        Mock Get-DvOrNull {
            if ($Path -like '*MemoAttributeMetadata*') { return [pscustomobject]@{ MaxLength = 4000 } }
            return [pscustomobject]@{ AttributeType = 'Memo'; IsSecured = $false }
        }
        $s = Initialize-EbillingSchema -EnvironmentUrl 'https://example.invalid' -SolutionUniqueName 'SpaarkeMaster' -Entity 'sprk_invoice'
        $script:Created = $s.Created; $script:Changed = $s.Changed; $script:Skipped = $s.Skipped
        $thrown = $null
        try { Invoke-ApplyColumn $ColumnSpecs[0] } catch { $thrown = $_.Exception }
        $thrown.GetType().Name | Should -Be 'EscalationException'
        $thrown.Message | Should -BeLike '*sprk_invoice.sprk_decisionnote is not as specified*MaxLength 4000*'
        $global:EbNoteState.Posts | Should -Be 0
    }
}

Describe 'Get-PublishStep and Get-PublishParameterXml: publish only the two tables' {
    It 'publishes after a run that wrote a column' { Get-PublishStep $false 1 | Should -Be 'publish' }

    It 'publishes again on a re-run that wrote nothing (a first run whose publish did not finish is healed)' {
        Get-PublishStep $false 0 | Should -Be 'republish'
    }

    It 'never publishes on a dry run (must not fire)' {
        Get-PublishStep $true 0 | Should -Be 'whatif'
        Get-PublishStep $true 2 | Should -Be 'whatif'
    }

    It 'names exactly sprk_invoice and sprk_invoicecorrection' {
        Get-PublishParameterXml $ColumnSpecs | Should -Be '<importexportxml><entities><entity>sprk_invoice</entity><entity>sprk_invoicecorrection</entity></entities></importexportxml>'
    }
}

Describe 'The script text: what only runs live' {
    It 'loads EbillingSchema.psm1, which sends Consistency: Strong on every request' {
        $script:Text | Should -Match '(?m)^using module \.\\EbillingSchema\.psm1'
        Get-Content -Raw $script:ModulePath | Should -Match "Consistency\s*=\s*'Strong'"
    }

    It 'publishes through the two-table helper only, never the tenant-wide publish' {
        $script:Text | Should -Match 'ParameterXml = \(Get-PublishParameterXml \$ColumnSpecs\)'
        # The tenant-wide publish is banned repo-wide by the D-83 lint in Publish-SolutionComponents.Tests.ps1, which greps this script too.
    }

    It 'builds no request of its own (no Invoke-RestMethod, no headers)' {
        $script:Text | Should -Not -Match 'Invoke-RestMethod|Invoke-WebRequest|\$headers'
    }

    It 'refuses to run without a mode, and keeps -Verify separate from -WhatIf and -Apply' {
        $script:Text | Should -Match 'Choose a mode: -WhatIf'
        $script:Text | Should -Match '-WhatIf and -Verify are separate modes'
        $script:Text | Should -Match '-Apply and -Verify are separate modes'
    }

    It 'never alters an existing column (no PUT or PATCH of attribute metadata)' {
        $script:Text | Should -Not -Match "-Method\s+'?(PUT|PATCH)"
    }

    It 'names no BFF-writer or SpaarkeMaster path (no solution source edit)' {
        $script:Text | Should -Not -Match 'src/dataverse/solutions'
    }
}
