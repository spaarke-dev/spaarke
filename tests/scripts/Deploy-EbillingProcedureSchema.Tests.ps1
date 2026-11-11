# tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 task 080: scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 reads the core-table list from the C# source
# (InvoiceFamilyCoreRecordGuard.FilingCores) and builds every core-record lookup from it. These tests prove, with no live call:
#   1. Get-FilingCores parses the real source, accepts both C# initializer shapes, and REFUSES (EscalationException, which the
#      script turns into exit 2 with nothing written) a missing file, no declaration, a commented-out declaration, two
#      declarations, an empty list, a non-sprk_ name and a duplicate; a mere USE of FilingCores is not a declaration;
#   2. the plan built from the parsed list (Resolve-Plan, metadata reads stubbed): one record lookup per core table on
#      sprk_ruleset (RemoveLink), one typed lookup per core table on sprk_procedureassignment (Cascade), sprk_billingfirm and
#      sprk_billingresponsible on every core table (RemoveLink); the assignment's core lookups are RemoveLink with every cascade
#      member NoCascade; only sprk_procedureassignment.sprk_excludedpolicy and sprk_procedureversion.sprk_procedure are Delete Cascade
#      (the one parental relationship each table may have; Dataverse 0x80047007), each with the exact delete-only configuration, and
#      Get-CascadeMismatches fires on any member that differs; the plan guard fires on two Delete Cascade lookups on one table and on a
#      Cascade core lookup; the in-place repair (known drift, PUT body, ORDER before sprk_excludedpolicy) is pinned; no lookup whose
#      target is a core table is Restrict. A fourth core table in the list adds its lookups (not hand-typed);
#   3. the script text names no core table at all (no hand-typed list).
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/ebilling/Deploy-EbillingProcedureSchema.ps1'
$script:GuardPath = Join-Path $repoRoot 'src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs'
$script:Work = Join-Path ([System.IO.Path]::GetTempPath()) ("procschema-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:Work | Out-Null

# The script's EscalationException normally comes from the module (`using module`); a stand-in with the same name is enough here.
if (-not ('EscalationException' -as [type])) {
    Add-Type -TypeDefinition 'public class EscalationException : System.Exception { public EscalationException(string m) : base(m) {} }'
}

# Pull the functions under test out of the script (it cannot be dot-sourced: it runs on load).
$errs = $null; $tokens = $null
$script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)
foreach ($name in 'Get-FilingCores', 'Get-CascadeDeleteOnly', 'Get-CascadeNone', 'Test-KnownCascadeDrift', 'Get-CascadeFixBody', 'Get-ParentalPlanProblems', 'Get-ParentalProblems', 'Get-OtherParentalRelationships', 'Assert-NoOtherParental', 'New-LookupSpec', 'Get-NewTableRel', 'Get-ExpectedDelete', 'Get-ExpectedCascade', 'Get-CascadeMismatches', 'New-CreatorSpec', 'Resolve-Plan', 'Get-RequiredColumnSpecs', 'Get-RequiredLevelProblem', 'Get-PublishEntities', 'Get-MissingOptionValue') {
    $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) | Select-Object -First 1
    Invoke-Expression $fn.Extent.Text
}

# Variables Resolve-Plan reads from the script scope.
$RuleSet = 'sprk_ruleset'; $Procedure = 'sprk_procedure'; $Version = 'sprk_procedureversion'; $Assignment = 'sprk_procedureassignment'
$PolicyVersion = 'sprk_policyversion'; $RateCard = 'sprk_ratecard'; $ScopeColumn = 'sprk_scope'; $CreatorColumn = 'sprk_createdbyperson'
$EnvironmentUrl = 'https://example.invalid'

# A stand-in for the metadata read: only table definitions of the current fixture exist.
$script:Fixture = @()
function Get-DvOrNull([string]$Path, [int]$Retries = 0) {
    if ($Path -match "EntityDefinitions\(LogicalName='([^']+)'\)\?") {
        $t = $Matches[1]
        if ($script:Fixture -contains $t) {
            $label = (($t -replace '^sprk_', '') -replace '(?<=[a-z])(?=assignment|record)', ' ')
            return [pscustomobject]@{ LogicalName = $t; SchemaName = $t; DisplayName = [pscustomobject]@{ UserLocalizedLabel = [pscustomobject]@{ Label = $label } } }
        }
    }
    return $null
}

function Write-Cs([string]$Name, [string]$Text) {
    $p = Join-Path $script:Work $Name
    Set-Content -LiteralPath $p -Value $Text -Encoding UTF8
    return $p
}

Describe 'Get-FilingCores' {
    It 'reads the three core tables from the real guard source, in source order' {
        (Get-FilingCores $script:GuardPath) -join ',' | Should Be 'sprk_matter,sprk_project,sprk_workassignment'
    }
    It 'accepts the collection-expression shape' {
        $p = Write-Cs 'a.cs' 'private static readonly string[] FilingCores = ["sprk_matter", "sprk_agreement"];'
        (Get-FilingCores $p) -join ',' | Should Be 'sprk_matter,sprk_agreement'
    }
    It 'accepts the new string[] { } shape over several lines, ignoring a line comment' {
        $p = Write-Cs 'b.cs' "static readonly string[] FilingCores = new string[] {`n    `"sprk_matter`", // the matter`n    `"sprk_project`"`n};"
        (Get-FilingCores $p) -join ',' | Should Be 'sprk_matter,sprk_project'
    }
    It 'a use of FilingCores next to the declaration is not a second declaration' {
        $p = Write-Cs 'c.cs' "FilingCores = [`"sprk_matter`"];`nvar x = FilingCores.Select(c => c).ToArray();`nif (FilingCores == null) { }"
        (Get-FilingCores $p) -join ',' | Should Be 'sprk_matter'
    }
    It 'refuses a missing file' {
        { Get-FilingCores (Join-Path $script:Work 'nope.cs') } | Should Throw 'does not exist'
    }
    It 'refuses a source with no declaration (only a use)' {
        $p = Write-Cs 'd.cs' 'var x = FilingCores.Select(c => c);'
        { Get-FilingCores $p } | Should Throw 'exactly one FilingCores initializer'
    }
    It 'refuses a commented-out declaration' {
        $p = Write-Cs 'e.cs' '// private static readonly string[] FilingCores = ["sprk_matter"];'
        { Get-FilingCores $p } | Should Throw 'exactly one FilingCores initializer'
    }
    It 'refuses two declarations' {
        $p = Write-Cs 'f.cs' "FilingCores = [`"sprk_matter`"];`nFilingCores = [`"sprk_project`"];"
        { Get-FilingCores $p } | Should Throw 'exactly one FilingCores initializer'
    }
    It 'refuses an empty list' {
        $p = Write-Cs 'g.cs' 'FilingCores = [];'
        { Get-FilingCores $p } | Should Throw 'lists no table names'
    }
    It 'refuses a name that is not a sprk_ table' {
        $p = Write-Cs 'h.cs' 'FilingCores = ["account"];'
        { Get-FilingCores $p } | Should Throw 'not a sprk_ table'
    }
    It 'refuses a duplicate' {
        $p = Write-Cs 'i.cs' 'FilingCores = ["sprk_matter", "sprk_matter"];'
        { Get-FilingCores $p } | Should Throw 'twice'
    }
}

Describe 'Resolve-Plan (the plan follows the parsed list)' {
    $script:Fixture = @('sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_agreement')
    Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment')
    $three = $script:Lookups
    $threeSecured = $script:Secured

    It 'creates one record lookup per core table on the rule set, RemoveLink' {
        $rs = @($three | Where-Object { $_.Table -eq 'sprk_ruleset' -and $_.Column -like 'sprk_*' -and $_.Column -in @('sprk_matter', 'sprk_project', 'sprk_workassignment') })
        $rs.Count | Should Be 3
        @($rs | Where-Object { $_.Delete -ne 'RemoveLink' }).Count | Should Be 0
    }
    It 'creates one typed lookup per core table on the assignment, RemoveLink with nothing cascading (NOT Cascade: parental limit)' {
        $as = @($three | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Column -in @('sprk_matter', 'sprk_project', 'sprk_workassignment') })
        $as.Count | Should Be 3
        @($as | Where-Object { $_.Delete -ne 'RemoveLink' }).Count | Should Be 0
        foreach ($c in $as) {
            @(Get-CascadeMismatches (Get-ExpectedCascade $c) @{ Delete = 'RemoveLink'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; RollupView = 'NoCascade' }).Count | Should Be 0
            $c.DriftFrom | Should Be 'Cascade'
            ($c.Desc -match 'REMOVELINK') | Should Be $true
            ($c.Desc -match 'planner \(task 117\)') | Should Be $true
            ($c.Desc -match 'sweep as the backstop') | Should Be $true
            ($c.Desc -match 'CASCADE: deleting the record') | Should Be $false
        }
    }
    It 'puts sprk_billingfirm and sprk_billingresponsible on every core table, RemoveLink, to sprk_organization' {
        foreach ($t in 'sprk_matter', 'sprk_project', 'sprk_workassignment') {
            foreach ($c in 'sprk_billingfirm', 'sprk_billingresponsible') {
                $l = @($three | Where-Object { $_.Table -eq $t -and $_.Column -eq $c })
                $l.Count | Should Be 1
                $l[0].Target | Should Be 'sprk_organization'
                $l[0].Delete | Should Be 'RemoveLink'
            }
        }
    }
    It 'puts the billing columns on no table that is not a core table' {
        @($three | Where-Object { $_.Column -in 'sprk_billingfirm', 'sprk_billingresponsible' -and $_.Table -notin 'sprk_matter', 'sprk_project', 'sprk_workassignment' }).Count | Should Be 0
    }
    It 'never reads Restrict on a lookup whose target is a core table (must-fire: a Restrict would be found)' {
        @($three | Where-Object { $_.Target -in 'sprk_matter', 'sprk_project', 'sprk_workassignment' -and $_.Delete -eq 'Restrict' }).Count | Should Be 0
        # the control: the same scan DOES find a Restrict when one is there
        $planted = @($three) + (New-LookupSpec 'sprk_procedureassignment' 'sprk_x' 'sprk_X' 'rel_x' 'sprk_matter' 'X' 'x' 'Restrict')
        @($planted | Where-Object { $_.Target -in 'sprk_matter', 'sprk_project', 'sprk_workassignment' -and $_.Delete -eq 'Restrict' }).Count | Should Be 1
    }
    It 'makes Procedure Version -> Procedure a delete-only cascade (NOT parental)' {
        $p = @($three | Where-Object { $_.Table -eq 'sprk_procedureversion' -and $_.Column -eq 'sprk_procedure' })
        $p.Count | Should Be 1
        $p[0].PSObject.Properties.Name -contains 'Parental' | Should Be $false
        (Get-ExpectedDelete $p[0]) | Should Be 'Cascade'
        $p[0].Cascade.Delete | Should Be 'Cascade'
        $p[0].Cascade.Reparent | Should Be 'NoCascade'
    }
    It 'gives EVERY Cascade lookup the exact custom configuration, and nothing else gets one' {
        $exact = @{ Assign = 'NoCascade'; Delete = 'Cascade'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; RollupView = 'NoCascade' }
        $cascade = @($three | Where-Object { $_.Delete -eq 'Cascade' })
        # the ONE parental relationship per table: sprk_excludedpolicy on the assignment, sprk_procedure on the version
        (@($cascade | ForEach-Object { "$($_.Table).$($_.Column)" } | Sort-Object) -join ',') | Should Be ((@('sprk_procedureassignment.sprk_excludedpolicy', 'sprk_procedureversion.sprk_procedure') | Sort-Object) -join ',')
        foreach ($c in $cascade) {
            @(Get-CascadeMismatches $c.Cascade $exact).Count | Should Be 0
            @(Get-CascadeMismatches $exact (Get-ExpectedCascade $c)).Count | Should Be 0
            $c.Cascade.Keys.Count | Should Be 7
        }
        # the only other lookups carrying an explicit configuration are the three assignment core lookups (RemoveLink, all NoCascade)
        (@($three | Where-Object { $_.Delete -ne 'Cascade' -and $_.Cascade } | ForEach-Object { "$($_.Table).$($_.Column)" } | Sort-Object) -join ',') | Should Be 'sprk_procedureassignment.sprk_matter,sprk_procedureassignment.sprk_project,sprk_procedureassignment.sprk_workassignment'
    }
    It 'expects Assign / Share / Unshare / Reparent NoCascade on every non-cascade lookup too (none is parental)' {
        foreach ($s in @($three | Where-Object { $_.Delete -ne 'Cascade' })) {
            $e = Get-ExpectedCascade $s
            $e.Delete | Should Be $s.Delete
            foreach ($k in 'Assign', 'Share', 'Unshare', 'Reparent') { $e[$k] | Should Be 'NoCascade' }
        }
    }
    It 'the restricted lookups target a Procedure, a Version, a firm or nothing core' {
        $restrict = @($three | Where-Object { $_.Delete -eq 'Restrict' } | ForEach-Object { "$($_.Table).$($_.Column)" } | Sort-Object)
        $expected = @('sprk_policyversion.sprk_procedureversion', 'sprk_procedureassignment.sprk_firm', 'sprk_procedureassignment.sprk_pinnedversion', 'sprk_procedureassignment.sprk_procedure', 'sprk_procedure.sprk_firm', 'sprk_ratecard.sprk_procedureversion', 'sprk_ruleset.sprk_procedure') | Sort-Object
        ($restrict -join ',') | Should Be ($expected -join ',')
    }
    It 'uses unique relationship names of at most 100 characters' {
        $names = @($three | ForEach-Object { $_.Rel })
        @($names | Select-Object -Unique).Count | Should Be $names.Count
        @($names | Where-Object { $_.Length -gt 100 }).Count | Should Be 0
    }
    It 'names the rule-set and assignment relationships as the old script did' {
        @($three | Where-Object { $_.Table -eq 'sprk_ruleset' -and $_.Column -eq 'sprk_workassignment' })[0].Rel | Should Be 'sprk_workassignment_ruleset_workassignment'
        @($three | Where-Object { $_.Table -eq 'sprk_ruleset' -and $_.Column -eq 'sprk_matter' })[0].Rel | Should Be 'sprk_matter_ruleset_matter'
    }
    It 'secures the rule set record lookups with the other P9 columns' {
        $keys = @($threeSecured | ForEach-Object { "$($_.Table).$($_.Column)" })
        foreach ($k in 'sprk_ruleset.sprk_matter', 'sprk_ruleset.sprk_project', 'sprk_ruleset.sprk_workassignment', 'sprk_ruleset.sprk_scope', 'sprk_ruleset.sprk_firm', 'sprk_ruleset.sprk_procedure',
            'sprk_procedure.sprk_appliesto', 'sprk_procedure.sprk_firm', 'sprk_procedure.sprk_status',
            'sprk_procedureversion.sprk_effectivefrom', 'sprk_procedureversion.sprk_effectiveto', 'sprk_procedureversion.sprk_status', 'sprk_procedureversion.sprk_notconverted') {
            ($keys -contains $k) | Should Be $true
        }
    }
    It 'secures the five columns ontology F1-1 added, and the secured set is exactly 18 + one per core table beyond the three' {
        $keys = @($threeSecured | ForEach-Object { "$($_.Table).$($_.Column)" })
        foreach ($k in 'sprk_procedure.sprk_area', 'sprk_procedure.sprk_proceduretype', 'sprk_policyversion.sprk_procedureversion', 'sprk_policyversion.sprk_publishstate', 'sprk_ratecard.sprk_procedureversion') {
            ($keys -contains $k) | Should Be $true
        }
        $keys.Count | Should Be 18
        @($keys | Select-Object -Unique).Count | Should Be 18
    }
    It 'secures only columns the plan creates (with the right kind) plus the two existing rule set columns sprk_scope and sprk_firm' {
        foreach ($sec in $threeSecured) {
            if ($sec.Table -eq 'sprk_ruleset' -and $sec.Column -in 'sprk_scope', 'sprk_firm') { continue }
            if ($sec.Kind -eq 'Lookup') { @($three | Where-Object { $_.Table -eq $sec.Table -and $_.Column -eq $sec.Column }).Count | Should Be 1 }
            else { @($script:Columns | Where-Object { $_.Table -eq $sec.Table -and $_.Column -eq $sec.Column -and $_.Type -eq $sec.Kind }).Count | Should Be 1 }
        }
    }
    It 'does not secure the columns that must stay writable (name, notes, firm-facing name, reason, mode, version number)' {
        foreach ($c in 'sprk_name', 'sprk_notes', 'sprk_firmfacingname', 'sprk_reason', 'sprk_mode', 'sprk_versionnumber') {
            @($threeSecured | Where-Object { $_.Column -eq $c }).Count | Should Be 0
        }
    }
    It 'puts sprk_createdbyperson on exactly the six tables ontology F4-1 names, RemoveLink, and NOT on the rate card or its rates' {
        $tables = @($three | Where-Object { $_.Column -eq 'sprk_createdbyperson' } | ForEach-Object { $_.Table } | Sort-Object)
        ($tables -join ',') | Should Be ((@('sprk_policy', 'sprk_policyversion', 'sprk_procedure', 'sprk_procedureassignment', 'sprk_procedureversion', 'sprk_ruleset') | Sort-Object) -join ',')
        @($three | Where-Object { $_.Column -eq 'sprk_createdbyperson' -and $_.Delete -ne 'RemoveLink' }).Count | Should Be 0
        @($three | Where-Object { $_.Column -eq 'sprk_createdbyperson' -and $_.Table -in 'sprk_ratecard', 'sprk_ratecardrate' }).Count | Should Be 0
        @($three | Where-Object { $_.Column -eq 'sprk_createdbyperson' -and $_.Target -ne 'systemuser' }).Count | Should Be 0
    }
    It 'names the policy and policy-version creator relationships by the D-173 shape' {
        @($three | Where-Object { $_.Table -eq 'sprk_policy' -and $_.Column -eq 'sprk_createdbyperson' })[0].Rel | Should Be 'sprk_systemuser_sprk_policy_createdbyperson'
        @($three | Where-Object { $_.Table -eq 'sprk_policyversion' -and $_.Column -eq 'sprk_createdbyperson' })[0].Rel | Should Be 'sprk_systemuser_sprk_policyversion_createdbyperson'
    }
    It 'does not secure sprk_createdbyperson (task 081 does)' {
        @($threeSecured | Where-Object { $_.Column -eq 'sprk_createdbyperson' }).Count | Should Be 0
    }
    It 'adds the lookups of a FOURTH core table without any script change (the list is not hand-typed)' {
        Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_agreement')
        $four = $script:Lookups
        @($four | Where-Object { $_.Column -eq 'sprk_agreement' -and $_.Table -in 'sprk_ruleset', 'sprk_procedureassignment' }).Count | Should Be 2
        @($four | Where-Object { $_.Table -eq 'sprk_agreement' -and $_.Column -in 'sprk_billingfirm', 'sprk_billingresponsible' }).Count | Should Be 2
        @($script:Secured | Where-Object { $_.Table -eq 'sprk_ruleset' -and $_.Column -eq 'sprk_agreement' }).Count | Should Be 1
        $four.Count | Should Be ($three.Count + 4)
        Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment')
    }
    It 'refuses a FilingCores entry whose table does not exist' {
        { Resolve-Plan @('sprk_matter', 'sprk_nosuchtable') } | Should Throw 'does not exist'
    }
}

Describe 'Get-CascadeMismatches (the verify comparison)' {
    $exact = @{ Assign = 'NoCascade'; Delete = 'Cascade'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; RollupView = 'NoCascade' }
    It 'is silent on the exact configuration (must-not-fire)' {
        @(Get-CascadeMismatches $exact $exact).Count | Should Be 0
        @(Get-CascadeMismatches (Get-CascadeDeleteOnly) $exact).Count | Should Be 0
    }
    foreach ($m in 'Assign', 'Share', 'Unshare', 'Reparent') {
        It "fires when $m is Cascade (must-fire)" {
            $actual = $exact.Clone(); $actual[$m] = 'Cascade'
            $r = @(Get-CascadeMismatches $actual $exact)
            $r.Count | Should Be 1
            ($r[0] -match $m) | Should Be $true
        }
    }
    foreach ($m in 'Merge', 'RollupView') {
        It "fires when $m is Cascade (must-fire)" {
            $actual = $exact.Clone(); $actual[$m] = 'Cascade'
            @(Get-CascadeMismatches $actual $exact).Count | Should Be 1
        }
    }
    It 'fires when Delete is not Cascade, and on a parental configuration (must-fire)' {
        $actual = $exact.Clone(); $actual.Delete = 'RemoveLink'
        @(Get-CascadeMismatches $actual $exact).Count | Should Be 1
        $parental = @{ Assign = 'Cascade'; Delete = 'Cascade'; Merge = 'Cascade'; Reparent = 'Cascade'; Share = 'Cascade'; Unshare = 'Cascade'; RollupView = 'Cascade' }
        @(Get-CascadeMismatches $parental $exact).Count | Should Be 6
    }
    It 'fires when a member cannot be read back (must-fire)' {
        $actual = $exact.Clone(); $actual.Remove('Share')
        @(Get-CascadeMismatches $actual $exact).Count | Should Be 1
    }
}

Describe 'required levels (ontology F4-2)' {
    It 'raises exactly sprk_procedureversion.sprk_procedure (lookup) and sprk_versionnumber (integer) to SystemRequired' {
        $r = @(Get-RequiredColumnSpecs)
        $r.Count | Should Be 2
        (@($r | ForEach-Object { "$($_.Table).$($_.Column):$($_.Kind):$($_.Level)" } | Sort-Object) -join ',') | Should Be 'sprk_procedureversion.sprk_procedure:Lookup:SystemRequired,sprk_procedureversion.sprk_versionnumber:Integer:SystemRequired'
    }
    $spec = @{ Table = 'sprk_procedureversion'; Column = 'sprk_versionnumber'; Kind = 'Integer'; Level = 'SystemRequired' }
    It 'reads SystemRequired as no problem (must-not-fire)' {
        (Get-RequiredLevelProblem $spec ([pscustomobject]@{ RequiredLevel = [pscustomobject]@{ Value = 'SystemRequired' } })) | Should BeNullOrEmpty
    }
    foreach ($lvl in 'None', 'Recommended', 'ApplicationRequired') {
        It "fires when the level reads $lvl (must-fire)" {
            $m = Get-RequiredLevelProblem $spec ([pscustomobject]@{ RequiredLevel = [pscustomobject]@{ Value = $lvl } })
            ($m -match $lvl) | Should Be $true
        }
    }
    It 'fires when the column cannot be read (must-fire)' {
        (Get-RequiredLevelProblem $spec $null) | Should Not BeNullOrEmpty
    }
    It 'the module accepts Integer and exports Set-IntegerRequiredLevel' {
        $mod = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/ebilling/EbillingSchema.psm1') -Raw
        ($mod -match "ValidateSet\('String', 'Lookup', 'Picklist', 'Integer'\)") | Should Be $true
        ($mod -match "function Set-IntegerRequiredLevel") | Should Be $true
        ($mod -match "'Set-IntegerRequiredLevel'") | Should Be $true
    }
}

Describe 'the publish list' {
    It 'is the three new tables, the rule set, policy version, rate card and the core tables - and nothing referenced-only' {
        $script:Fixture = @('sprk_matter', 'sprk_project', 'sprk_workassignment')
        Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment')
        $list = @(Get-PublishEntities | Sort-Object)
        ($list -join ',') | Should Be ((@('sprk_procedure', 'sprk_procedureversion', 'sprk_procedureassignment', 'sprk_ruleset', 'sprk_policyversion', 'sprk_ratecard', 'sprk_matter', 'sprk_project', 'sprk_workassignment') | Sort-Object) -join ',')
    }
    It 'never names contact, systemuser, sprk_organization or sprk_policy (must-fire if one comes back)' {
        foreach ($t in 'contact', 'systemuser', 'sprk_organization', 'sprk_policy') { (@(Get-PublishEntities) -contains $t) | Should Be $false }
    }
    It 'follows the parsed core list' {
        $script:Fixture = @('sprk_matter', 'sprk_agreement')
        Resolve-Plan @('sprk_matter', 'sprk_agreement')
        (@(Get-PublishEntities) -contains 'sprk_agreement') | Should Be $true
        $script:Fixture = @('sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_agreement')
    }
}

Describe 'Get-MissingOptionValue (the sprk_scope re-read after securing)' {
    $want = 100000000, 100000001, 100000002, 100000003
    function New-Opt($vals) { return @($vals | ForEach-Object { [pscustomobject]@{ Value = $_; Label = "L$_" } }) }
    It 'is empty when all four options are present (must-not-fire)' {
        @(Get-MissingOptionValue (New-Opt $want) $want).Count | Should Be 0
    }
    It 'is empty when extra options exist (must-not-fire)' {
        @(Get-MissingOptionValue (New-Opt ($want + 100000009)) $want).Count | Should Be 0
    }
    It 'names a lost option (must-fire)' {
        $m = @(Get-MissingOptionValue (New-Opt (100000000, 100000001, 100000003)) $want)
        $m.Count | Should Be 1
        $m[0] | Should Be 100000002
    }
    It 'names every option when the re-read returns none (must-fire)' {
        @(Get-MissingOptionValue @() $want).Count | Should Be 4
    }
}

Describe 'the parental limit (one Delete Cascade relationship per table, dev run 2026-10-21)' {
    $script:Fixture = @('sprk_matter', 'sprk_project', 'sprk_workassignment')
    Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment')
    $plan = $script:Lookups
    $cores = 'sprk_matter', 'sprk_project', 'sprk_workassignment'
    function New-Rel($col, $delete = 'NoCascade', $assign = 'NoCascade', $share = 'NoCascade', $merge = 'NoCascade') {
        return [pscustomobject]@{ SchemaName = "rel_$col"; ReferencingAttribute = $col; CascadeConfiguration = [pscustomobject]@{ Delete = $delete; Assign = $assign; Share = $share; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = $merge; RollupView = 'NoCascade' } }
    }

    It 'the real plan has at most one Delete Cascade lookup per table and passes the plan guard (must-not-fire)' {
        @(Get-ParentalPlanProblems $plan $cores).Count | Should Be 0
        foreach ($g in @($plan | Where-Object { $_.Delete -eq 'Cascade' } | Group-Object Table)) { $g.Count | Should Be 1 }
    }
    It 'fires on two Delete Cascade lookups on one table (must-fire)' {
        $two = @($plan) + (New-LookupSpec 'sprk_procedureassignment' 'sprk_other' 'sprk_Other' 'rel_other' 'sprk_policy' 'Other' 'd' 'Cascade')
        $r = @(Get-ParentalPlanProblems $two $cores)
        $r.Count | Should Be 1
        ($r[0] -match 'sprk_procedureassignment has 2 Delete = Cascade') | Should Be $true
    }
    It 'fires when a core lookup on the assignment is set to Cascade (must-fire)' {
        $bad = @($plan | Where-Object { -not ($_.Table -eq 'sprk_procedureassignment' -and $_.Column -eq 'sprk_project') }) + (New-LookupSpec 'sprk_procedureassignment' 'sprk_project' 'sprk_Project' 'rel_p' 'sprk_project' 'Project' 'd' 'Cascade')
        $r = @(Get-ParentalPlanProblems $bad $cores)
        ($r.Count -ge 1) | Should Be $true
        (@($r | Where-Object { $_ -match 'core table sprk_project' }).Count) | Should Be 1
    }
    It 'the three core lookups are RemoveLink and never Cascade, even on a fourth core table' {
        $script:Fixture = @('sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_agreement')
        Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_agreement') | Out-Null
        $four = $script:Lookups
        @($four | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Target -in 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_agreement' -and $_.Delete -ne 'RemoveLink' }).Count | Should Be 0
        @(Get-ParentalPlanProblems $four ($cores + 'sprk_agreement')).Count | Should Be 0
        Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment')
    }

    Context 'Test-KnownCascadeDrift (the one drift repaired in place)' {
        $spec = @($plan | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Column -eq 'sprk_matter' })[0]
        $known = [pscustomobject]@{ Delete = 'Cascade'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; RollupView = 'NoCascade' }
        It 'accepts exactly Delete Cascade with the rest NoCascade (must-not-fire as a refusal)' {
            (Test-KnownCascadeDrift $spec $known) | Should Be $true
        }
        It 'refuses any other difference (must-fire as a refusal)' {
            $m = $known.PSObject.Copy(); $m.Delete = 'Restrict'
            (Test-KnownCascadeDrift $spec $m) | Should Be $false
            foreach ($k in 'Assign', 'Share', 'Unshare', 'Reparent', 'Merge') {
                $x = $known.PSObject.Copy(); $x.$k = 'Cascade'
                (Test-KnownCascadeDrift $spec $x) | Should Be $false
            }
            (Test-KnownCascadeDrift $spec $null) | Should Be $false
        }
        It 'never applies to a lookup without a DriftFrom (sprk_excludedpolicy, version -> Procedure)' {
            foreach ($c in 'sprk_excludedpolicy') {
                (Test-KnownCascadeDrift @($plan | Where-Object { $_.Column -eq $c })[0] $known) | Should Be $false
            }
            (Test-KnownCascadeDrift @($plan | Where-Object { $_.Table -eq 'sprk_procedureversion' -and $_.Column -eq 'sprk_procedure' })[0] $known) | Should Be $false
        }
        It 'an already-fixed relationship is no drift and no mismatch (second run changes nothing)' {
            $fixed = [pscustomobject]@{ Delete = 'RemoveLink'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; RollupView = 'NoCascade' }
            (Test-KnownCascadeDrift $spec $fixed) | Should Be $false
            @(Get-CascadeMismatches $fixed (Get-ExpectedCascade $spec)).Count | Should Be 0
        }
    }

    Context 'Get-CascadeFixBody (the in-place PUT payload)' {
        $spec = @($plan | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Column -eq 'sprk_matter' })[0]
        function New-ReadRel {
            return [pscustomobject]@{
                '@odata.context' = 'https://example.invalid/api/data/v9.2/$metadata#RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata/$entity'; MetadataId = 'e460e4bf-8ecd-f111-a05d-3833c5e9614d'
                SchemaName = 'sprk_matter_procedureassignment_matter'; ReferencedEntity = 'sprk_matter'; ReferencingAttribute = 'sprk_matter'
                CascadeConfiguration = [pscustomobject]@{ Assign = 'NoCascade'; Delete = 'Cascade'; Archive = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; RollupView = 'NoCascade' }
            }
        }
        It 'sets Delete RemoveLink and Assign / Share / Unshare / Reparent / Merge NoCascade, and nothing else' {
            $before = New-ReadRel
            $body = Get-CascadeFixBody (New-ReadRel) (Get-ExpectedCascade $spec)
            $body.CascadeConfiguration.Delete | Should Be 'RemoveLink'
            foreach ($k in 'Assign', 'Share', 'Unshare', 'Reparent', 'Merge') { $body.CascadeConfiguration.$k | Should Be 'NoCascade' }
            $body.CascadeConfiguration.Archive | Should Be 'RemoveLink'
            $body.CascadeConfiguration.RollupView | Should Be 'NoCascade'
            $body.MetadataId | Should Be $before.MetadataId
            $body.SchemaName | Should Be $before.SchemaName
            # The cast-path read has no @odata.type (the dev run of 2026-10-21 was refused 0x80048d19 without it).
            $body.'@odata.type' | Should Be 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
            @($body.PSObject.Properties.Name) -contains '@odata.context' | Should Be $false
            @($body.PSObject.Properties.Name).Count | Should Be @($before.PSObject.Properties.Name).Count
        }
        It 'reads back clean against the six expected members (must-not-fire) and would be caught if Delete stayed Cascade (must-fire)' {
            $six = @{ Delete = 'RemoveLink'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade' }
            $body = Get-CascadeFixBody (New-ReadRel) (Get-ExpectedCascade $spec)
            @(Get-CascadeMismatches $body.CascadeConfiguration $six).Count | Should Be 0
            @(Get-CascadeMismatches (New-ReadRel).CascadeConfiguration $six).Count | Should Be 1
        }
        It 'sets all six members whatever the read values were, Merge included (the payload does not rely on the known drift)' {
            $odd = New-ReadRel
            foreach ($k in 'Assign', 'Share', 'Unshare', 'Reparent', 'Merge') { $odd.CascadeConfiguration.$k = 'Cascade' }
            $body = Get-CascadeFixBody $odd (Get-ExpectedCascade $spec)
            $body.CascadeConfiguration.Delete | Should Be 'RemoveLink'
            foreach ($k in 'Assign', 'Share', 'Unshare', 'Reparent', 'Merge') { $body.CascadeConfiguration.$k | Should Be 'NoCascade' }
        }
        It 'refuses an expected configuration without one of the six members (must-fire)' {
            { Get-CascadeFixBody (New-ReadRel) @{ Delete = 'RemoveLink'; Assign = 'NoCascade' } } | Should Throw 'has no'
        }
    }

    Context 'Get-ParentalProblems (the verify of the final shape)' {
        $final = @(
            (New-Rel 'owninguser'), (New-Rel 'owningbusinessunit' 'Restrict'), (New-Rel 'sprk_matter' 'RemoveLink'), (New-Rel 'sprk_project' 'RemoveLink'),
            (New-Rel 'sprk_procedure' 'Restrict'), (New-Rel 'sprk_excludedpolicy' 'Cascade'), (New-Rel 'sprk_firm' 'Restrict')
        )
        It 'is silent on the final shape (must-not-fire)' {
            @(Get-ParentalProblems 'sprk_procedureassignment' $final 'sprk_excludedpolicy').Count | Should Be 0
        }
        It 'is silent when only Merge cascades (Dataverse sets Merge on lookups to contact) (must-not-fire)' {
            $r = @($final) + (New-Rel 'sprk_contact' 'RemoveLink' 'NoCascade' 'NoCascade' 'Cascade')
            @(Get-ParentalProblems 'sprk_procedureassignment' $r 'sprk_excludedpolicy').Count | Should Be 0
        }
        It 'fires when sprk_matter is still Delete Cascade (must-fire)' {
            $r = @($final | Where-Object { $_.ReferencingAttribute -ne 'sprk_matter' }) + (New-Rel 'sprk_matter' 'Cascade')
            $p = @(Get-ParentalProblems 'sprk_procedureassignment' $r 'sprk_excludedpolicy')
            $p.Count | Should Be 1
            ($p[0] -match 'sprk_matter') | Should Be $true
        }
        It 'fires on any other member cascading: assign or share (must-fire)' {
            foreach ($k in 'assign', 'share') {
                $r = @($final | Where-Object { $_.ReferencingAttribute -ne 'sprk_project' })
                $r += if ($k -eq 'assign') { New-Rel 'sprk_project' 'RemoveLink' 'Cascade' } else { New-Rel 'sprk_project' 'RemoveLink' 'NoCascade' 'Cascade' }
                @(Get-ParentalProblems 'sprk_procedureassignment' $r 'sprk_excludedpolicy').Count | Should Be 1
            }
        }
        It 'fires when the allowed relationship is missing or is not Delete Cascade (must-fire)' {
            @(Get-ParentalProblems 'sprk_procedureassignment' @($final | Where-Object { $_.ReferencingAttribute -ne 'sprk_excludedpolicy' }) 'sprk_excludedpolicy').Count | Should Be 1
            $r = @($final | Where-Object { $_.ReferencingAttribute -ne 'sprk_excludedpolicy' }) + (New-Rel 'sprk_excludedpolicy' 'RemoveLink')
            @(Get-ParentalProblems 'sprk_procedureassignment' $r 'sprk_excludedpolicy').Count | Should Be 1
        }
        It 'works the same for the version table (only sprk_procedure is parental)' {
            $v = @((New-Rel 'owninguser'), (New-Rel 'sprk_procedure' 'Cascade'), (New-Rel 'sprk_previousversion' 'RemoveLink'))
            @(Get-ParentalProblems 'sprk_procedureversion' $v 'sprk_procedure').Count | Should Be 0
            @(Get-ParentalProblems 'sprk_procedureversion' (@($v) + (New-Rel 'sprk_extra' 'Cascade')) 'sprk_procedure').Count | Should Be 1
        }
    }

    Context 'ORDER: the in-place repair, read back, before sprk_excludedpolicy is created' {
        function Get-Attr($t, $c, $sel) { return $null }
        function Get-DvOrNull([string]$Path, [int]$Retries = 0) {
            if ($Path -match 'ManyToOneRelationships') { return [pscustomobject]@{ value = @($script:Rels) } }
            return $null
        }
        $excluded = @($plan | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Column -eq 'sprk_excludedpolicy' })[0]
        $matterSpec = @($plan | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Column -eq 'sprk_matter' })[0]
        It 'refuses to create sprk_excludedpolicy while sprk_matter is still Delete Cascade (must-fire)' {
            $DryRun = $false; $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new()
            $script:Rels = @((New-Rel 'owninguser'), (New-Rel 'sprk_matter' 'Cascade'))
            { Assert-NoOtherParental $excluded } | Should Throw 'already has a parental relationship'
        }
        It 'refuses in apply mode even when a repair is merely planned (the PUT has not landed) (must-fire)' {
            $DryRun = $false
            $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new(); $script:InPlaceFixes.Add(@{ Spec = $matterSpec; Cascade = $true; Desc = $true })
            $script:Rels = @((New-Rel 'owninguser'), (New-Rel 'sprk_matter' 'Cascade'))
            { Assert-NoOtherParental $excluded } | Should Throw 'already has a parental relationship'
        }
        It 'refuses on ANY other parental relationship, not only sprk_matter (must-fire)' {
            $DryRun = $false; $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new()
            $script:Rels = @((New-Rel 'owninguser'), (New-Rel 'sprk_matter' 'RemoveLink'), (New-Rel 'sprk_workassignment' 'RemoveLink' 'Cascade'))
            { Assert-NoOtherParental $excluded } | Should Throw 'already has a parental relationship'
        }
        It 'proceeds once the PUT has landed and sprk_matter reads back RemoveLink (must-not-fire)' {
            $DryRun = $false; $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new()
            $script:Rels = @((New-Rel 'owninguser'), (New-Rel 'sprk_matter' 'RemoveLink'))
            { Assert-NoOtherParental $excluded } | Should Not Throw
        }
        It 'in -WhatIf a PLANNED repair counts as done, an unplanned Cascade does not' {
            $DryRun = $true
            $script:Rels = @((New-Rel 'owninguser'), (New-Rel 'sprk_matter' 'Cascade'))
            $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new(); $script:InPlaceFixes.Add(@{ Spec = $matterSpec; Cascade = $true; Desc = $true })
            { Assert-NoOtherParental $excluded } | Should Not Throw
            $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new()
            { Assert-NoOtherParental $excluded } | Should Throw 'already has a parental relationship'
        }
        It 'does not look at a lookup that is not Delete Cascade, or one that already exists' {
            $DryRun = $false; $script:InPlaceFixes = [System.Collections.Generic.List[object]]::new()
            $script:Rels = @((New-Rel 'sprk_matter' 'Cascade'))
            { Assert-NoOtherParental $matterSpec } | Should Not Throw
        }
        It 'the script runs the repair (PUT, then read back) before it creates any other assignment lookup, and the guard runs inside every create' {
            $text = Get-Content -LiteralPath $script:ScriptPath -Raw
            $step4 = $text.IndexOf('Write-Host "4. P4 table')
            $repair = $text.IndexOf('Repair-CoreLookupDrift', $step4)
            $firstCreate = $text.IndexOf('Invoke-LookupSpec $s', $step4)
            $excludedCreate = $text.IndexOf('$_.Column -notin $script:Cores.Table', $step4)
            ($step4 -gt 0 -and $repair -gt $step4 -and $firstCreate -gt $repair -and $excludedCreate -gt $repair) | Should Be $true
            # inside the repair: PUT first, then the read-back loop, then the Changed entry
            $fn = $text.IndexOf('function Repair-CoreLookupDrift')
            $put = $text.IndexOf('Invoke-Dv -Path "RelationshipDefinitions(', $fn)
            $back = $text.IndexOf('Get-LookupRelationship $s.Table $s.Column', $put)
            $done = $text.IndexOf('CHANGED  $tag CascadeConfiguration', $back)
            ($fn -gt 0 -and $put -gt $fn -and $back -gt $put -and $done -gt $back) | Should Be $true
            # the guard is the first statement of Invoke-LookupSpec
            $il = $text.IndexOf('function Invoke-LookupSpec($s) {')
            $text.Substring($il, 120) -match 'Assert-NoOtherParental \$s' | Should Be $true
        }
    }

    It 'Resolve-Plan itself stops (escalation) when the plan guard reports a problem, and runs on when it does not (must-fire / must-not-fire at the source)' {
        $script:Fixture = @('sprk_matter', 'sprk_project', 'sprk_workassignment')
        { Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment') } | Should Not Throw
        function Get-ParentalPlanProblems { return @('sprk_procedureassignment has 2 Delete = Cascade lookups') }
        { Resolve-Plan @('sprk_matter', 'sprk_project', 'sprk_workassignment') } | Should Throw 'one-parental-relationship-per-table'
    }
}

Describe 'the script text' {
    It 'grants the writer profile before the reader (review K2)' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $w = $text.IndexOf('Id = $script:WriterId; Name = $WriterProfileName')
        $r = $text.IndexOf('Id = $script:ReaderId; Name = $ReaderProfileName')
        ($w -gt 0 -and $r -gt $w) | Should Be $true
    }
    It 'checks the scope options inside the secure-then-grant try, only when it just secured sprk_scope (review K4)' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $text -match 'if \(\$needSecure -and \$t -eq \$RuleSet -and \$c -eq \$ScopeColumn\) \{ Assert-ScopeOptionsIntact \}' | Should Be $true
    }
    It 'builds the publish list from Get-PublishEntities, not a typed list' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $text -match '\$entities = Get-PublishEntities' | Should Be $true
        $text -match "RateCard, 'sprk_policy', 'sprk_organization'" | Should Be $false
    }
    It 'names no core table: the list is parsed, never typed' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        $text -match 'sprk_matter\b' | Should Be $false
        $text -match 'sprk_project\b' | Should Be $false
        $text -match 'sprk_workassignment\b' | Should Be $false
    }
    It 'never creates the items the task forbids' {
        $text = Get-Content -LiteralPath $script:ScriptPath -Raw
        # created = appears in an Add-* / New-* call; mentions in comments and the "never created" checks are fine
        $text -match "Add-Column\s+'sprk_isexceptions'" | Should Be $false
        $text -match "Add-Column\s+'sprk_allbusinessunits'" | Should Be $false
        $text -match "Add-Table\s+-Table\s+'sprk_storedfile'" | Should Be $false
        $text -match 'securedfrombusinessunit' | Should Be $true   # only the negative check mentions it
        $text -match "Add-ManyToMany" | Should Be $false
    }
}

Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
