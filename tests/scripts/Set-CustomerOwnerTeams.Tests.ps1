# Pester 5. Run: Invoke-Pester tests/scripts/Set-CustomerOwnerTeams.Tests.ps1
# unified-access-control-r2 task 194b R4b-4: scripts/Set-CustomerOwnerTeams.ps1 over a mocked Dataverse (read-only: every
# write is a test failure). Pins B-F1 (no business unit named -SecureBusinessUnitName refuses: Secure Record is never
# accepted as a customer unit) and B-K4 (a dry run whose drift is an owner decision exits non-zero with the summary).

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Set-CustomerOwnerTeams.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    $global:SetCustomerOwnerTeamsTest_RootId = '00000000-0000-4000-8000-000000000001'
    $global:SetCustomerOwnerTeamsTest_SecureId = '00000000-0000-4000-8000-000000000002'
    $global:SetCustomerOwnerTeamsTest_CustomerId = '00000000-0000-4000-8000-000000000003'
    $global:SetCustomerOwnerTeamsTest_TeamId = '00000000-0000-4000-8000-000000000004'

    # Each table's Read privilege name, as the config files declare it (metadata agrees with them here).
    $global:SetCustomerOwnerTeamsTest_Privileges = @{}
    foreach ($file in 'restricted-content-owner-role.json', 'customer-configuration-owner-role.json') {
        $config = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' $file) -Raw | ConvertFrom-Json
        foreach ($t in @($config.tables)) { $global:SetCustomerOwnerTeamsTest_Privileges[$t.logicalName] = $t.privilegeName }
    }
    $global:SetCustomerOwnerTeamsTest_RestrictedTeamName = (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' 'restricted-content-owner-role.json') -Raw |
            ConvertFrom-Json).teamName

    # Rows are objects, as Invoke-RestMethod returns them (a hashtable's own Count would answer for a single row).
    # The world each test reads: the restricted-content team in the customer unit (with $global:SetCustomerOwnerTeamsTest_Members members), or none.
    $global:SetCustomerOwnerTeamsTest_WithTeam = $false
    $global:SetCustomerOwnerTeamsTest_Members = 0

    # The mock bodies run from the script under test, so the world they read is global (removed in AfterAll).
    function Invoke-Script([hashtable]$Arguments) {
        $output = & $script:Script @Arguments 6>&1 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'SetCustomerOwnerTeamsTest_*' -ErrorAction SilentlyContinue
}

Describe 'Set-CustomerOwnerTeams.ps1 (mocked Dataverse, no writes)' {
    BeforeEach {
        $global:SetCustomerOwnerTeamsTest_WithTeam = $false
        $global:SetCustomerOwnerTeamsTest_Members = 0
        $global:LASTEXITCODE = 0
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            if ($Method -ne 'Get') { throw "Test: the script wrote ($Method $Uri)." }
            $u = [string]$Uri
            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ name = 'test' }) } }
            if ($u -match '/businessunits\?') {
                return @{ value = @(
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_RootId; name = 'Spaarke'; _parentbusinessunitid_value = $null }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_SecureId; name = 'Secure Record'; _parentbusinessunitid_value = $global:SetCustomerOwnerTeamsTest_RootId }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_CustomerId; name = 'Customer One'; _parentbusinessunitid_value = $global:SetCustomerOwnerTeamsTest_RootId }
                    )
                }
            }
            if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/Privileges") {
                return @{ value = @([pscustomobject]@{ PrivilegeType = 'Read'; Name = $global:SetCustomerOwnerTeamsTest_Privileges[$Matches[1]]; PrivilegeId = [guid]::NewGuid() }) }
            }
            if ($u -match '/teams\?') {
                if ($global:SetCustomerOwnerTeamsTest_WithTeam -and $u -match [regex]::Escape($global:SetCustomerOwnerTeamsTest_RestrictedTeamName)) {
                    return @{ value = @([pscustomobject]@{ teamid = $global:SetCustomerOwnerTeamsTest_TeamId; name = $global:SetCustomerOwnerTeamsTest_RestrictedTeamName; teamtype = 0; isdefault = $false; _businessunitid_value = $global:SetCustomerOwnerTeamsTest_CustomerId }) }
                }
                return @{ value = @() }
            }
            if ($u -match '/teammembership_association') {
                $n = [int]$global:SetCustomerOwnerTeamsTest_Members
                return @{ value = @(if ($n -gt 0) { 1..$n | ForEach-Object { [pscustomobject]@{ systemuserid = [guid]::NewGuid(); fullname = "member $_" } } }) }
            }
            if ($u -match '/teamroles_association') { return @{ value = @() } }
            if ($u -match '/roles\?') { return @{ value = @() } }
            throw "Test: unexpected read $u"
        }
    }

    It 'B-F1 must fire: no business unit named -SecureBusinessUnitName refuses, so Secure Record is never taken for a customer unit' {
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Secure Record'; SecureBusinessUnitName = 'Nonexistent' }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "No business unit is named 'Nonexistent'"
        $r.Output | Should -Not -Match 'PLAN'
    }

    It 'B-F1 must fire: with the right name, Secure Record is refused as a customer unit' {
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Secure Record' }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'is the Secure Record business unit'
    }

    It 'B-K4 must fire: a dry run whose drift is an owner decision (a team member) exits 1 with the summary' {
        $global:SetCustomerOwnerTeamsTest_WithTeam = $true
        $global:SetCustomerOwnerTeamsTest_Members = 1
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One' }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'OWNER DECISION NEEDED: 1 finding'
        $r.Output | Should -Not -Match 'Re-run with -Apply'
    }

    It 'B-K4 must not fire: a dry run whose drift -Apply can fix exits 0 with the plan' {
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One' }
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'Re-run with -Apply'
        $r.Output | Should -Not -Match 'OWNER DECISION NEEDED'
    }
}
