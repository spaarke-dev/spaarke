# Pester 5. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Deploy-EbillingProcedureVersionPublishedOn.Tests.ps1
# ebilling-r1 DEF-061 fix round 1. The text-only ProcedureVersionPublishedOnAgreementTests pin the script's constants and its source; they cannot
# see what PowerShell does when it RUNS the script's date handling and its readability check. These tests run the script's own functions (lifted
# out of its syntax tree, as Set-EbillingInvoiceMoneyFieldSecurity.Tests.ps1 does) against a test double of the Dataverse calls: no environment,
# no network.
#   F1  the backfill's audit date: Invoke-RestMethod in PowerShell 7 hands `createdon` back as a [datetime] that is already UTC. Re-parsing it
#       shifted it by the operator's offset, and under en-GB swapped day and month (or threw part way through a run). The result must be the
#       exact UTC instant for a [datetime] and for a string, under en-US and en-GB.
#   F2  the operator must be shown to read the whole table (organization-depth Read), or -Verify / -Backfill exit 1.

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Deploy-EbillingProcedureVersionPublishedOn.ps1'

    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'Table', 'Column', 'StatusActive', 'StatusSuperseded', 'StatusRetired'
    $wantedFunctions = 'ConvertTo-UtcInstant', 'ConvertTo-StartOfDayUtc', 'Get-OperatorReadDepth', 'Get-ReadabilityGap', 'Get-StampPlan', 'Write-RowsRead'

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

    # The Dataverse call, as a test double: WhoAmI, the privilege lookup and the operator's privileges.
    function Invoke-Dv {
        param([string]$Path)
        if ($script:Fake.Throw) { throw $script:Fake.Throw }
        if ($Path -eq 'WhoAmI') { return [pscustomobject]@{ UserId = 'u-1' } }
        if ($Path -like 'privileges?*') { return [pscustomobject]@{ value = @($script:Fake.Privilege) } }
        if ($Path -like 'systemusers(u-1)/*RetrieveUserPrivileges*') { return [pscustomobject]@{ RolePrivileges = @($script:Fake.Held) } }
        throw "unexpected GET $Path"
    }

    . ([scriptblock]::Create($pieces -join "`n"))

    # The exact instant every conversion below must produce. 2026-03-24 has a day above 12 (a swap throws under en-US); 2026-03-04 is ambiguous.
    $script:Instants = @(
        [datetime]::new(2026, 3, 24, 13, 14, 15, [System.DateTimeKind]::Utc),
        [datetime]::new(2026, 3, 4, 5, 6, 7, [System.DateTimeKind]::Utc),
        [datetime]::new(2026, 12, 31, 23, 59, 59, [System.DateTimeKind]::Utc)
    )

    function Use-Culture([string]$Name, [scriptblock]$Body) {
        $saved = [System.Threading.Thread]::CurrentThread.CurrentCulture
        try {
            [System.Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]::GetCultureInfo($Name)
            & $Body
        } finally { [System.Threading.Thread]::CurrentThread.CurrentCulture = $saved }
    }
}

Describe 'ConvertTo-UtcInstant: the audit time as the exact UTC instant' {
    It 'returns the instant itself for a UTC [datetime] (already UTC: not re-parsed, not shifted), under <culture>' -ForEach @(
        @{ culture = 'en-US' }, @{ culture = 'en-GB' }
    ) {
        Use-Culture $culture {
            foreach ($instant in $script:Instants) {
                $result = ConvertTo-UtcInstant $instant
                $result.Ticks | Should -Be $instant.Ticks
                $result.Kind | Should -Be ([System.DateTimeKind]::Utc)
            }
        }
    }

    It 'returns the instant for a LOCAL-kind [datetime] of the same instant (the operator''s offset is undone), under <culture>' -ForEach @(
        @{ culture = 'en-US' }, @{ culture = 'en-GB' }
    ) {
        Use-Culture $culture {
            foreach ($instant in $script:Instants) {
                $result = ConvertTo-UtcInstant $instant.ToLocalTime()
                $result.Ticks | Should -Be $instant.Ticks
                $result.Kind | Should -Be ([System.DateTimeKind]::Utc)
            }
        }
    }

    It 'reads an ISO string as UTC with the invariant culture (no day/month swap, no throw), under <culture>' -ForEach @(
        @{ culture = 'en-US' }, @{ culture = 'en-GB' }
    ) {
        Use-Culture $culture {
            foreach ($instant in $script:Instants) {
                foreach ($text in @($instant.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [cultureinfo]::InvariantCulture),
                        $instant.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [cultureinfo]::InvariantCulture))) {
                    $result = ConvertTo-UtcInstant $text
                    $result.Ticks | Should -Be $instant.Ticks -Because "'$text' under $culture"
                    $result.Kind | Should -Be ([System.DateTimeKind]::Utc)
                }
            }
        }
    }

    It 'the old re-parse WOULD have been wrong (the control that proves these cases bite)' {
        # [datetime]::Parse of the culture-formatted text of a UTC [datetime], as the script did: en-GB text read under en-US swaps or throws.
        $instant = $script:Instants[0]
        $text = Use-Culture 'en-GB' { $instant.ToString() }
        { Use-Culture 'en-US' { [datetime]::Parse($text).ToUniversalTime() } } | Should -Throw
    }
}

Describe 'ConvertTo-StartOfDayUtc: the effective-from fallback' {
    It 'is midnight UTC of the date, for a [datetime] and for a string, under <culture>' -ForEach @(
        @{ culture = 'en-US' }, @{ culture = 'en-GB' }
    ) {
        Use-Culture $culture {
            foreach ($date in @([datetime]::new(2026, 3, 24), [datetime]::new(2026, 3, 4), [datetime]::new(2026, 1, 1))) {
                $expected = [datetime]::new($date.Year, $date.Month, $date.Day, 0, 0, 0, [System.DateTimeKind]::Utc)
                (ConvertTo-StartOfDayUtc $date).Ticks | Should -Be $expected.Ticks
                (ConvertTo-StartOfDayUtc $date.ToString('yyyy-MM-dd', [cultureinfo]::InvariantCulture)).Ticks | Should -Be $expected.Ticks
                (ConvertTo-StartOfDayUtc ($date.ToString('yyyy-MM-dd', [cultureinfo]::InvariantCulture) + 'T00:00:00Z')).Ticks | Should -Be $expected.Ticks
                (ConvertTo-StartOfDayUtc $date).Kind | Should -Be ([System.DateTimeKind]::Utc)
            }
        }
    }
}

Describe 'Get-ReadabilityGap: the operator must be shown to read every version' {
    BeforeEach {
        $script:Fake = @{ Throw = $null; Privilege = [pscustomobject]@{ privilegeid = 'P-READ' }
            Held = @([pscustomobject]@{ PrivilegeId = 'p-read'; Depth = 'Global' }, [pscustomobject]@{ PrivilegeId = 'p-other'; Depth = 'Basic' }) }
    }

    It 'passes at organization depth' {
        Get-ReadabilityGap | Should -BeNullOrEmpty
    }

    It 'passes when the best of several grants is organization depth' {
        $script:Fake.Held = @([pscustomobject]@{ PrivilegeId = 'P-READ'; Depth = 'Local' }, [pscustomobject]@{ PrivilegeId = 'P-READ'; Depth = 'Global' })
        Get-ReadabilityGap | Should -BeNullOrEmpty
    }

    It 'fails at <depth> depth, saying every version was not confirmed read' -ForEach @(
        @{ depth = 'Basic' }, @{ depth = 'Local' }, @{ depth = 'Deep' }
    ) {
        $script:Fake.Held = @([pscustomobject]@{ PrivilegeId = 'P-READ'; Depth = $depth })
        Get-ReadabilityGap | Should -BeLike "cannot confirm every version was read*'$depth'*"
    }

    It 'fails when the operator holds no Read privilege on the table' {
        $script:Fake.Held = @([pscustomobject]@{ PrivilegeId = 'p-other'; Depth = 'Global' })
        Get-ReadabilityGap | Should -BeLike "cannot confirm every version was read*none found*"
    }

    It 'fails when the privilege cannot be looked up, or the check itself faults' {
        $script:Fake.Privilege = $null
        Get-ReadabilityGap | Should -BeLike 'cannot confirm every version was read*'
        $script:Fake.Privilege = [pscustomobject]@{ privilegeid = 'P-READ' }
        $script:Fake.Throw = 'HTTP 403'
        Get-ReadabilityGap | Should -BeLike 'cannot confirm every version was read*HTTP 403*'
    }
}

Describe 'Get-StampPlan: rows read per status, and what the backfill would stamp' {
    It 'counts every status, leaves Retired and Draft alone, and stamps only unstamped Active and Superseded versions that have a from' {
        $rows = @(
            [pscustomobject]@{ sprk_procedureversionid = 'a'; sprk_status = 100000001; sprk_effectivefrom = '2026-01-01'; sprk_publishedon = $null },
            [pscustomobject]@{ sprk_procedureversionid = 'b'; sprk_status = 100000001; sprk_effectivefrom = '2026-01-01'; sprk_publishedon = [datetime]::UtcNow },
            [pscustomobject]@{ sprk_procedureversionid = 'c'; sprk_status = 100000002; sprk_effectivefrom = '2025-01-01'; sprk_publishedon = $null },
            [pscustomobject]@{ sprk_procedureversionid = 'd'; sprk_status = 100000002; sprk_effectivefrom = $null; sprk_publishedon = $null },
            [pscustomobject]@{ sprk_procedureversionid = 'e'; sprk_status = 100000003; sprk_effectivefrom = '2025-01-01'; sprk_publishedon = $null },
            [pscustomobject]@{ sprk_procedureversionid = 'f'; sprk_status = 100000000; sprk_effectivefrom = '2025-01-01'; sprk_publishedon = $null },
            [pscustomobject]@{ sprk_procedureversionid = 'g'; sprk_status = $null; sprk_effectivefrom = '2025-01-01'; sprk_publishedon = $null }
        )

        $plan = Get-StampPlan $rows $true

        $plan.Total | Should -Be 7
        $plan.Active | Should -Be 2
        $plan.Superseded | Should -Be 2
        $plan.RetiredLeft | Should -Be 1
        $plan.Draft | Should -Be 1
        $plan.Masked | Should -Be 1
        $plan.AlreadyStamped | Should -Be 1
        $plan.NoFrom | Should -Be 1
        @($plan.ToStamp | ForEach-Object { $_.sprk_procedureversionid }) | Should -Be @('a', 'c')
    }
}
