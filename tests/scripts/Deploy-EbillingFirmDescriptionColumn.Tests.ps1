# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Deploy-EbillingFirmDescriptionColumn.Tests.ps1
# ebilling-r1 130e-1 (DEF-060): the schema script for sprk_policyversion.sprk_firmdescription. The script cannot be dot-sourced (it begins
# with `using module` and runs against the environment), so its functions and the constants they read are lifted out of its own syntax tree
# and run here against a test double of the Dataverse calls (no environment, no network). The text checks pin the parts that only run live:
# the mode guards, the entity-scoped publish and the module that sends Consistency: Strong.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Deploy-EbillingFirmDescriptionColumn.ps1'
    $script:Text = Get-Content -Raw $script:ScriptPath

    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'Table', 'Column', 'ColumnSchemaName', 'ColumnMaxLength', 'EntityComponentType', 'SystemSolutions', 'Description'
    $wantedFunctions = 'New-FirmDescriptionDef', 'Select-OwningSolution', 'Get-TableSolutions', 'Get-ColumnGaps'

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

    # The module's exception and helpers, as test doubles. Get-DvOrNull / Invoke-Dv answer from $script:Fake by path.
    class EscalationException : System.Exception {
        EscalationException([string]$message) : base($message) {}
    }
    function New-Label([string]$Text) {
        return @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ Label = $Text; LanguageCode = 1033 }) }
    }
    function New-MemoDef([string]$Schema, [string]$Display, [string]$Description, [int]$MaxLength) {
        return @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
            SchemaName    = $Schema
            DisplayName   = New-Label $Display
            Description   = New-Label $Description
            RequiredLevel = @{ Value = 'None' }
            MaxLength     = $MaxLength
            Format        = 'TextArea'
        }
    }
    function Get-DvOrNull([string]$Path, [int]$Retries) {
        $script:Paths.Add($Path)
        if ($Path -like '*MemoAttributeMetadata*') { return $script:Fake.Memo }
        if ($Path -like '*Attributes(*') { return $script:Fake.Attribute }
        if ($Path -like 'EntityDefinitions*') { return $script:Fake.Entity }
        if ($Path -like 'solutions(*') { $id = ($Path -split '[()]')[1]; return $script:Fake.Solutions[$id] }
        throw "unexpected read $Path"
    }
    function Invoke-Dv([string]$Path) {
        $script:Paths.Add($Path)
        if ($Path -like 'solutioncomponents*') { return @{ value = $script:Fake.Components } }
        throw "unexpected read $Path"
    }

    $EnvironmentUrl = 'https://example.invalid'
    . ([scriptblock]::Create($pieces -join "`n"))

    function New-Solution([string]$Name, [bool]$Managed = $false) { [pscustomobject]@{ uniquename = $Name; ismanaged = $Managed } }
}

Describe 'New-FirmDescriptionDef: the column definition the script sends' {
    It 'is a Memo named as the BFF reads it, MaxLength 2000, optional' {
        $def = New-FirmDescriptionDef
        $def.'@odata.type' | Should -Be 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
        $def.SchemaName | Should -Be 'sprk_FirmDescription'
        $def.SchemaName.ToLowerInvariant() | Should -Be $Column
        $def.MaxLength | Should -Be 2000
        $def.RequiredLevel.Value | Should -Be 'None'
        $def.ContainsKey('IsSecured') | Should -BeFalse -Because 'EB-D-213: the column is not field-secured'
    }

    It 'targets sprk_policyversion.sprk_firmdescription, the column BillingFactLoader selects' {
        $Table | Should -Be 'sprk_policyversion'
        $Column | Should -Be 'sprk_firmdescription'
        $loader = Get-Content -Raw (Join-Path $script:RepoRoot 'src' 'server' 'api' 'Sprk.Bff.Api' 'Services' 'Signals' 'Billing' 'BillingFactLoader.cs')
        $loader | Should -Match 'FirmDescriptionColumn\s*=\s*"sprk_firmdescription"'
    }
}

Describe 'Select-OwningSolution: the MSCRM.SolutionUniqueName the writes carry' {
    It 'picks the one unmanaged solution that contains the table, ignoring managed and system solutions' {
        $solutions = @((New-Solution 'Default'), (New-Solution 'Active'), (New-Solution 'Basic'), (New-Solution 'SomeManaged' $true), (New-Solution 'SpaarkeMaster'))
        Select-OwningSolution $solutions '' | Should -Be 'SpaarkeMaster'
    }

    It 'accepts a named solution only when it contains the table' {
        $solutions = @((New-Solution 'SpaarkeMaster'), (New-Solution 'SpaarkeEbilling'))
        Select-OwningSolution $solutions 'SpaarkeEbilling' | Should -Be 'SpaarkeEbilling'
    }

    It 'escalates (must fire) when <Case>' -ForEach @(
        @{ Case = 'no unmanaged solution contains the table'; Solutions = @(@('Default', $false), @('Managed', $true)); Requested = ''; Like = '*No unmanaged solution*' }
        @{ Case = 'more than one does and none is named'; Solutions = @(@('A', $false), @('B', $false)); Requested = ''; Like = '*More than one*A, B*' }
        @{ Case = 'the named one does not contain the table'; Solutions = @(,@('SpaarkeMaster', $false)); Requested = 'Other'; Like = "*'Other' does not contain*" }
        @{ Case = 'the named one is managed'; Solutions = @(,@('Managed', $true)); Requested = 'Managed'; Like = "*'Managed' does not contain*" }
    ) {
        $thrown = $null
        # -ForEach data is built at discovery, before BeforeAll defines New-Solution: the rows are (name, managed) pairs.
        $built = @($Solutions | ForEach-Object { New-Solution $_[0] $_[1] })
        try { Select-OwningSolution $built $Requested } catch { $thrown = $_.Exception }
        $thrown | Should -Not -BeNullOrEmpty
        $thrown.GetType().Name | Should -Be 'EscalationException'
        $thrown.Message | Should -BeLike $Like
    }
}

Describe 'Get-TableSolutions: the solutions read from solutioncomponents' {
    BeforeEach { $script:Paths = [System.Collections.Generic.List[string]]::new() }

    It 'reads the entity components of the table and each solution once' {
        $script:Fake = @{
            Entity     = @{ MetadataId = 'aaaaaaaa-0000-0000-0000-000000000001' }
            Components = @(@{ _solutionid_value = 's1' }, @{ _solutionid_value = 's2' }, @{ _solutionid_value = 's1' })
            Solutions  = @{ s1 = (New-Solution 'SpaarkeMaster'); s2 = (New-Solution 'Default') }
        }
        $r = Get-TableSolutions
        @($r | ForEach-Object uniquename) | Should -Be @('SpaarkeMaster', 'Default')
        ($script:Paths | Where-Object { $_ -like 'solutioncomponents*' }) | Should -BeLike '*objectid eq aaaaaaaa-0000-0000-0000-000000000001 and componenttype eq 1*'
        @($script:Paths | Where-Object { $_ -like 'solutions(*' }).Count | Should -Be 2
    }

    It 'throws when the table does not exist' {
        $script:Fake = @{ Entity = $null }
        { Get-TableSolutions } | Should -Throw '*does not exist*'
    }
}

Describe 'Get-ColumnGaps: the -Verify check (column, Memo, MaxLength 2000, not secured)' {
    BeforeEach {
        $script:Paths = [System.Collections.Generic.List[string]]::new()
        $script:Fake = @{
            Attribute = @{ LogicalName = 'sprk_firmdescription'; AttributeType = 'Memo'; IsSecured = $false }
            Memo      = @{ MaxLength = 2000 }
        }
    }

    It 'finds no gap when the column is as specified (must not fire)' {
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 0
    }

    It 'names a missing column' {
        $script:Fake.Attribute = $null
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -Be 'sprk_policyversion.sprk_firmdescription does not exist'
    }

    It 'names another type and stops there' {
        $script:Fake.Attribute.AttributeType = 'String'
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike "*is 'String', not Memo"
    }

    It 'names another MaxLength' {
        $script:Fake.Memo = @{ MaxLength = 4000 }
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike '*MaxLength 4000, expected 2000'
    }

    It 'names a field-secured column (EB-D-213: unsecured)' {
        $script:Fake.Attribute.IsSecured = $true
        $gaps = Get-ColumnGaps
        $gaps.Count | Should -Be 1
        $gaps[0] | Should -BeLike '*is field-secured*'
    }

    It 'reads the length through the Memo metadata cast and IsSecured from the attribute' {
        $null = Get-ColumnGaps
        $script:Paths | Should -Contain "EntityDefinitions(LogicalName='sprk_policyversion')/Attributes(LogicalName='sprk_firmdescription')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
        ($script:Paths -join '|') | Should -BeLike '*IsSecured*'
    }
}

Describe 'The script text: what only runs live' {
    It 'loads EbillingSchema.psm1, which sends Consistency: Strong on every request' {
        $script:Text | Should -Match '(?m)^using module \.\\EbillingSchema\.psm1'
        Get-Content -Raw (Join-Path $script:RepoRoot 'scripts' 'ebilling' 'EbillingSchema.psm1') | Should -Match "Consistency\s*=\s*'Strong'"
    }

    It 'publishes sprk_policyversion only, never the tenant-wide publish' {
        $script:Text | Should -Match '<entities><entity>\$Table</entity></entities>'
        $script:Text | Should -Not -Match 'PublishAllXml'
    }

    It 'refuses to run without a mode, and keeps -Verify separate from -WhatIf and -Apply' {
        $script:Text | Should -Match "Choose a mode: -WhatIf"
        $script:Text | Should -Match "-WhatIf and -Verify are separate modes"
        $script:Text | Should -Match "-Apply and -Verify are separate modes"
    }

    It 'never alters an existing column (no PUT or PATCH of attribute metadata)' {
        $script:Text | Should -Not -Match "-Method\s+'?(PUT|PATCH)"
    }
}
