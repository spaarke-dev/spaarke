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
    $wantedFunctions = 'Read-Dv', 'Read-DvOrNull', 'Get-ComponentKind', 'Get-Census', 'Get-ColumnBlockers', 'Get-DeleteRefusals', 'Get-PublishContext', 'Get-PendingCollateral', 'Publish-Tables'

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

    # The caller-can-read-every-row check lives in the shared module (the rate card script uses it too): lifted from the module's own tree.
    $script:ModulePath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'EbillingSchema.psm1'
    $moduleAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ModulePath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The module does not parse: $($errors[0].Message)" }
    $gapFunction = @($moduleAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ReadAllRowsGap' }, $true))
    if ($gapFunction.Count -ne 1) { throw 'Get-ReadAllRowsGap not found in EbillingSchema.psm1' }
    $pieces.Add($gapFunction[0].Extent.Text)

    # Test doubles of the module's calls. $script:Fake is set per test.
    $EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com'
    function Confirm-Write { param([string]$Target, [string]$Action) $script:Fake.Asked.Add("$Action | $Target"); return [bool]$script:Fake.Allow }
    # The shared D-83 procedure (scripts/lib/Publish-SolutionComponents.ps1). $script:Fake.Collateral: table -> pending items (text).
    function Get-DataverseApiContext { param([string]$EnvironmentUrl) return @{ Api = 'https://dev.invalid/api/data/v9.2'; Headers = @{} } }
    function Get-EntityPublishCollateral {
        param([hashtable]$Context, [string]$Entity, [string[]]$ExcludeIds = @())
        $script:Fake.CollateralCalls.Add($Entity)
        return @(@($script:Fake.Collateral[$Entity]) | Where-Object { $_ })
    }
    function Invoke-PublishXml {
        param([hashtable]$Context, [string]$ParameterXml)
        if ($ParameterXml -notmatch '^<importexportxml>.+</importexportxml>$') { throw 'ParameterXml must be an <importexportxml> document.' }
        $script:Fake.Published.Add($ParameterXml)
    }
    function Invoke-Dv {
        param([string]$Path, [string]$Method = 'GET', $Body = $null)
        if ($Method -ne 'GET') { throw "the census wrote: $Method $Path" }
        $script:Fake.Calls.Add($Path)
        # The caller's identity and rights (Get-ReadAllRowsGap). $script:Fake.Who: Admin, Depth ($null = no Read privilege), Fault (message = every call throws).
        $w = $script:Fake.Who
        if ($Path -eq 'WhoAmI' -or $Path -match '^systemusers\(|^privileges\?') {
            if ($w.Fault) { throw $w.Fault }
            if ($Path -eq 'WhoAmI') { return [pscustomobject]@{ UserId = 'u-1' } }
            if ($Path -match 'systemuserroles_association') { return [pscustomobject]@{ value = @(if ($w.Admin) { [pscustomobject]@{ name = 'System Administrator' } }) } }
            if ($Path -match '^privileges\?') { return [pscustomobject]@{ value = @([pscustomobject]@{ privilegeid = 'P-READ' }) } }
            if ($Path -match 'RetrieveUserPrivileges') {
                $held = @(if ($w.Depth) { [pscustomobject]@{ PrivilegeId = 'p-read'; Depth = $w.Depth } })
                return [pscustomobject]@{ RolePrivileges = $held }
            }
        }
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
            Who       = @{ Admin = $true; Depth = 'Global'; Fault = $null }
            Allow     = $true
            Asked     = [System.Collections.Generic.List[string]]::new()
            Published = [System.Collections.Generic.List[string]]::new()
            Collateral = @{}
            CollateralCalls = [System.Collections.Generic.List[string]]::new()
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
            Table = $Table; ColumnPresent = $true; ReadGap = $null; RowCount = 0; NonNullCount = 0; Forms = @(); Views = @(); PersonalViews = @(); OtherReferences = @(); Dependencies = @()
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

Describe 'the caller must be shown to read every row before any count is believed (review pass 1, F1)' {
    BeforeEach { $script:Fake = New-Fake }

    It 'a caller with organization Read (a System Administrator holds it) is counted as before' {
        $script:Fake.Rows['sprk_rulesets'] = 5; $script:Fake.NonNull['sprk_rulesets'] = 2
        $c = Get-Census $Targets[0]
        $c.ReadGap | Should -BeNullOrEmpty
        $c.RowCount | Should -Be 5
        $c.NonNullCount | Should -Be 2
    }

    It 'a non-admin with organization (Global) Read is counted' {
        $script:Fake.Who.Admin = $false; $script:Fake.Who.Depth = 'Global'
        (Get-Census $Targets[1]).ReadGap | Should -BeNullOrEmpty
    }

    It 'a role named System Administrator is no pass: below organization depth it is a gap (no role-name shortcut, fix round 2)' {
        $script:Fake.Who.Admin = $true; $script:Fake.Who.Depth = 'Local'
        (Get-Census $Targets[1]).ReadGap | Should -BeLike "*Read depth on sprk_ratecard is 'Local'*"
    }

    It 'a non-admin below organization depth: nothing is counted (unknown), the gap names the depth and the table, and the column is blocked' {
        $script:Fake.Who.Admin = $false; $script:Fake.Who.Depth = 'Deep'
        $script:Fake.Rows['sprk_ratecards'] = 9; $script:Fake.NonNull['sprk_ratecards'] = 9
        $c = Get-Census $Targets[1]
        $c.ReadGap | Should -BeLike "*Read depth on sprk_ratecard is 'Deep'*"
        $c.RowCount | Should -BeNullOrEmpty
        $c.NonNullCount | Should -BeNullOrEmpty
        $b = @(Get-ColumnBlockers $c $Targets[1])
        $b.Count | Should -Be 1
        $b[0] | Should -BeLike 'sprk_ratecard : cannot confirm every sprk_ratecard row can be read*'
    }

    It 'a fault in the rights check is a gap (refuses), never a pass' {
        $script:Fake.Who.Fault = 'timeout'
        $c = Get-Census $Targets[0]
        $c.ReadGap | Should -BeLike '*the check failed (timeout)*'
        @(Get-ColumnBlockers $c $Targets[0]).Count | Should -Be 1
    }

    It '-Delete refuses with the gap named, even with approval and -Confirm' {
        $script:Fake.Who.Admin = $false; $script:Fake.Who.Depth = 'Local'
        $census = @((Get-Census $Targets[0]), (Get-Census $Targets[1]))
        $r = @(Get-DeleteRefusals $census 'owner' $true $Targets)
        $r.Count | Should -Be 2
        ($r -join '|') | Should -Match 'Read depth on sprk_ruleset is ''Local'''
    }
}

Describe 'pending collateral (D-83) must not go live with the publish (review pass 1, F1)' {
    BeforeEach { $script:Fake = New-Fake; $script:Clean = @((New-Census 'sprk_ruleset'), (New-Census 'sprk_ratecard')) }

    It 'Get-PendingCollateral reads each table through the shared procedure and names table and item' {
        $script:Fake.Collateral['sprk_ratecard'] = @('savedqueries: Mine (v-1)')
        @(Get-PendingCollateral @('sprk_ruleset', 'sprk_ratecard')) | Should -Be @('sprk_ratecard | savedqueries: Mine (v-1)')
        $script:Fake.CollateralCalls | Should -Be @('sprk_ruleset', 'sprk_ratecard')
    }

    It 'Get-DeleteRefusals refuses while pending collateral exists, naming it and the switch' {
        $r = @(Get-DeleteRefusals $script:Clean 'owner' $true $Targets @('sprk_ratecard | savedqueries: Mine (v-1)') $false)
        $r.Count | Should -Be 1
        $r[0] | Should -BeLike '*Mine (v-1)*'
        $r[0] | Should -BeLike '*-AllowPendingCollateral*'
    }

    It 'Get-DeleteRefusals goes on with -AllowPendingCollateral, and with no pending item' {
        @(Get-DeleteRefusals $script:Clean 'owner' $true $Targets @('sprk_ratecard | savedqueries: Mine (v-1)') $true).Count | Should -Be 0
        @(Get-DeleteRefusals $script:Clean 'owner' $true $Targets @() $false).Count | Should -Be 0
    }

    It 'Publish-Tables publishes exactly the two tables, entity-scoped, through Invoke-PublishXml when nothing is pending' {
        Publish-Tables @('sprk_ruleset', 'sprk_ratecard') $false
        $script:Fake.Published.Count | Should -Be 1
        $script:Fake.Published[0] | Should -Be '<importexportxml><entities><entity>sprk_ruleset</entity><entity>sprk_ratecard</entity></entities></importexportxml>'
    }

    It 'Publish-Tables stops with a plain error (exit 1, the deletes are done) when pending collateral appeared, and publishes nothing' {
        $script:Fake.Collateral['sprk_ruleset'] = @('systemforms: Late (f-1)')
        $e = $null
        try { Publish-Tables @('sprk_ruleset', 'sprk_ratecard') $false } catch { $e = $_.Exception }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Not -Be 'EscalationException'
        $e.Message | Should -BeLike '*Late (f-1)*'
        $e.Message | Should -BeLike '*-AllowPendingCollateral*'
        $script:Fake.Published.Count | Should -Be 0
    }

    It 'Publish-Tables publishes despite pending collateral with -AllowPendingCollateral, and not at all when the confirmation says no (-WhatIf)' {
        $script:Fake.Collateral['sprk_ruleset'] = @('systemforms: Late (f-1)')
        Publish-Tables @('sprk_ruleset', 'sprk_ratecard') $true
        $script:Fake.Published.Count | Should -Be 1
        $script:Fake.Published.Clear(); $script:Fake.CollateralCalls.Clear(); $script:Fake.Allow = $false
        Publish-Tables @('sprk_ruleset', 'sprk_ratecard') $true
        $script:Fake.Published.Count | Should -Be 0
        $script:Fake.CollateralCalls.Count | Should -Be 0
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

    It 'the only -Method use is the DELETE in Remove-SourceDocumentLookup; the publish goes through the shared D-83 procedure, not a hand-rolled POST' {
        $uses = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandParameterAst] -and $n.ParameterName -eq 'Method' }, $true)
        $uses.Count | Should -Be 1
        foreach ($u in $uses) {
            $cmd = $u.Parent
            $fn = $cmd
            while ($fn -and $fn -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $fn = $fn.Parent }
            $fn | Should -Not -BeNullOrEmpty
            $value = $cmd.CommandElements[[array]::IndexOf($cmd.CommandElements, $u) + 1].Extent.Text
            if ($fn.Name -eq 'Remove-SourceDocumentLookup') { $value | Should -Be 'DELETE' }
            else { throw "-Method $value used in $($fn.Name)" }
        }
        $script:Ast.Extent.Text | Should -Not -Match "Path 'PublishXml'"
        $script:Ast.Extent.Text | Should -Match "(?m)^\. \(Join-Path \`$PSScriptRoot '\.\.' 'lib' 'Publish-SolutionComponents\.ps1'\)"
    }

    It 'both writing functions ask for confirmation before they write; Publish-Tables checks pending collateral before Invoke-PublishXml' {
        $del = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Remove-SourceDocumentLookup' }, $true)[0].Extent.Text
        $del | Should -Match 'ShouldProcess'
        $del.IndexOf('ShouldProcess') | Should -BeLessThan $del.IndexOf('-Method')
        $pub = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Publish-Tables' }, $true)[0].Extent.Text
        $pub.IndexOf('Confirm-Write') | Should -BeGreaterOrEqual 0
        $pub.IndexOf('Get-PendingCollateral') | Should -BeGreaterThan $pub.IndexOf('Confirm-Write')
        $pub.IndexOf('Invoke-PublishXml') | Should -BeGreaterThan $pub.IndexOf('Get-PendingCollateral')
    }

    It 'the pending-collateral read and the refusals come before the first delete, and the census checks read access before it counts' {
        $text = $script:Ast.Extent.Text
        $text.IndexOf('$pendingCollateral = @(Get-PendingCollateral') | Should -BeGreaterThan $text.LastIndexOf('if (-not $Delete)')
        $text.IndexOf('Get-DeleteRefusals $census') | Should -BeGreaterThan $text.IndexOf('$pendingCollateral = @(Get-PendingCollateral')
        $census = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-Census' }, $true)[0].Extent.Text
        $census.IndexOf('$c.RowCount = Get-RowCountExact') | Should -BeGreaterThan $census.IndexOf('$c.ReadGap = Get-ReadAllRowsGap')
    }

    It 'the removal functions are called only after Get-DeleteRefusals, inside the -Delete path' {
        $text = $script:Ast.Extent.Text
        $text.IndexOf('Get-DeleteRefusals $census') | Should -BeGreaterThan 0
        $text.IndexOf('Remove-SourceDocumentLookup (@') | Should -BeGreaterThan $text.IndexOf('Get-DeleteRefusals $census')
        $text.IndexOf('if (-not $Delete)') | Should -BeLessThan $text.IndexOf('Get-DeleteRefusals $census')
    }
}
