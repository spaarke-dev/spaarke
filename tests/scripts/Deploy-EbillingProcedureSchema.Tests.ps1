# tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 task 080: scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 reads the core-table list from the C# source
# (InvoiceFamilyCoreRecordGuard.FilingCores) and builds every core-record lookup from it. These tests prove, with no live call:
#   1. Get-FilingCores parses the real source, accepts both C# initializer shapes, and REFUSES (EscalationException, which the
#      script turns into exit 2 with nothing written) a missing file, no declaration, a commented-out declaration, two
#      declarations, an empty list, a non-sprk_ name and a duplicate; a mere USE of FilingCores is not a declaration;
#   2. the plan built from the parsed list (Resolve-Plan, metadata reads stubbed): one record lookup per core table on
#      sprk_ruleset (RemoveLink), one typed lookup per core table on sprk_procedureassignment (Cascade), sprk_billingfirm and
#      sprk_billingresponsible on every core table (RemoveLink), the Procedure Version -> Procedure lookup is parental, and no
#      lookup whose target is a core table is Restrict. A fourth core table in the list adds its lookups (not hand-typed);
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
foreach ($name in 'Get-FilingCores', 'New-LookupSpec', 'Get-NewTableRel', 'Get-ExpectedDelete', 'New-CreatorSpec', 'Resolve-Plan') {
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
    It 'makes Procedure Version -> Procedure parental (reads Cascade) and the planned Restrict lookups target a Procedure, a Version, a firm or nothing core' {
        $p = @($three | Where-Object { $_.Table -eq 'sprk_procedureversion' -and $_.Column -eq 'sprk_procedure' })
        $p.Count | Should Be 1
        $p[0].Parental | Should Be $true
        (Get-ExpectedDelete $p[0]) | Should Be 'Cascade'
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
        $keys.Count | Should Be 13
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

Describe 'the script text' {
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
