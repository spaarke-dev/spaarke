# tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 task 080: scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 reads the core-table list from the C# source
# (InvoiceFamilyCoreRecordGuard.FilingCores) and builds every core-record lookup from it. These tests prove, with no live call:
#   1. Get-FilingCores parses the real source, accepts both C# initializer shapes, and REFUSES (EscalationException, which the
#      script turns into exit 2 with nothing written) a missing file, no declaration, a commented-out declaration, two
#      declarations, an empty list, a non-sprk_ name and a duplicate; a mere USE of FilingCores is not a declaration;
#   2. the plan built from the parsed list (Resolve-Plan, metadata reads stubbed): one record lookup per core table on
#      sprk_ruleset (RemoveLink), one typed lookup per core table on sprk_procedureassignment (Cascade), sprk_billingfirm and
#      sprk_billingresponsible on every core table (RemoveLink); every Cascade lookup (the assignment's core lookups and
#      sprk_excludedpolicy, plus Procedure Version -> Procedure, which is NOT parental) carries the exact delete-only cascade
#      configuration (Delete Cascade; Assign/Share/Unshare/Reparent/Merge/RollupView NoCascade), and Get-CascadeMismatches fires
#      on any member that differs; no lookup whose target is a core table is Restrict. A fourth core table in the list adds its
#      lookups (not hand-typed);
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
foreach ($name in 'Get-FilingCores', 'Get-CascadeDeleteOnly', 'New-LookupSpec', 'Get-NewTableRel', 'Get-ExpectedDelete', 'Get-ExpectedCascade', 'Get-CascadeMismatches', 'New-CreatorSpec', 'Resolve-Plan', 'Get-RequiredColumnSpecs', 'Get-RequiredLevelProblem', 'Get-PublishEntities', 'Get-MissingOptionValue') {
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
    It 'creates one typed lookup per core table on the assignment, Cascade' {
        $as = @($three | Where-Object { $_.Table -eq 'sprk_procedureassignment' -and $_.Column -in @('sprk_matter', 'sprk_project', 'sprk_workassignment') })
        $as.Count | Should Be 3
        @($as | Where-Object { $_.Delete -ne 'Cascade' }).Count | Should Be 0
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
        # the three core-record lookups and sprk_excludedpolicy on the assignment, plus sprk_procedureversion.sprk_procedure
        (@($cascade | ForEach-Object { "$($_.Table).$($_.Column)" } | Sort-Object) -join ',') | Should Be ((@('sprk_procedureassignment.sprk_matter', 'sprk_procedureassignment.sprk_project', 'sprk_procedureassignment.sprk_workassignment', 'sprk_procedureassignment.sprk_excludedpolicy', 'sprk_procedureversion.sprk_procedure') | Sort-Object) -join ',')
        foreach ($c in $cascade) {
            @(Get-CascadeMismatches $c.Cascade $exact).Count | Should Be 0
            @(Get-CascadeMismatches $exact (Get-ExpectedCascade $c)).Count | Should Be 0
            $c.Cascade.Keys.Count | Should Be 7
        }
        @($three | Where-Object { $_.Delete -ne 'Cascade' -and $_.Cascade }).Count | Should Be 0
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
