# Pester 5 or 6. Run: Invoke-Pester tests/scripts/Set-CustomerOwnerTeams.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (ubuntu-latest).
# unified-access-control-r2 task 194b R4b-4: scripts/Set-CustomerOwnerTeams.ps1 over a mocked Dataverse (read-only: every
# write is a test failure). Pins B-F1 (no business unit named -SecureBusinessUnitName refuses: Secure Record is never
# accepted as a customer unit) and B-K4 (a dry run whose drift is an owner decision exits non-zero with the summary).

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Set-CustomerOwnerTeams.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    # Mock needs the command to exist; a runner without the az CLI gets a stub (the mock replaces it in every test).
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }
    $global:SetCustomerOwnerTeamsTest_RootId = '00000000-0000-4000-8000-000000000001'
    $global:SetCustomerOwnerTeamsTest_SecureId = '00000000-0000-4000-8000-000000000002'
    $global:SetCustomerOwnerTeamsTest_CustomerId = '00000000-0000-4000-8000-000000000003'
    $global:SetCustomerOwnerTeamsTest_TeamId = '00000000-0000-4000-8000-000000000004'

    # Each table's Read privilege name, as the config files declare it (metadata agrees with them here).
    $global:SetCustomerOwnerTeamsTest_Privileges = @{}
    foreach ($file in 'restricted-content-owner-role.json', 'customer-configuration-owner-role.json', 'billing-rule-readers-role.json') {
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

# ebilling-r1 task 089d: the 'Billing Rule Readers' member team (config/billing-rule-readers-role.json), run alone through
# -ConfigPaths so -Verify's exit code is this team's. The world: the team in the customer unit (or in the root, or in Secure
# Record), its role in the root with the customer unit's copy assigned to it, the role's privileges, and whether each member
# holds the billing-admin privilege.
Describe 'Set-CustomerOwnerTeams.ps1: the Billing Rule Readers member team (mocked Dataverse, no writes)' {
    BeforeAll {
        $script:BillingConfig = Join-Path $script:RepoRoot 'config' 'billing-rule-readers-role.json'
        $billing = Get-Content -LiteralPath $script:BillingConfig -Raw | ConvertFrom-Json
        $global:SetCustomerOwnerTeamsTest_BillingTeamName = $billing.teamName
        $global:SetCustomerOwnerTeamsTest_BillingRoleName = $billing.roleName
        $global:SetCustomerOwnerTeamsTest_MemberPrivilege = $billing.memberPrivilege
        $global:SetCustomerOwnerTeamsTest_BillingTeamId = '00000000-0000-4000-8000-000000000011'
        $global:SetCustomerOwnerTeamsTest_RootRoleId = '00000000-0000-4000-8000-000000000012'
        $global:SetCustomerOwnerTeamsTest_CopyRoleId = '00000000-0000-4000-8000-000000000013'
        $global:SetCustomerOwnerTeamsTest_PolicyReadId = '00000000-0000-4000-8000-000000000014'
        $global:SetCustomerOwnerTeamsTest_ChildId = '00000000-0000-4000-8000-000000000021'          # a child unit of Customer One
        $global:SetCustomerOwnerTeamsTest_OtherCustomerId = '00000000-0000-4000-8000-000000000022'  # another customer's top unit
        function Invoke-Billing([switch]$Verify, [string]$ConfigPath = $script:BillingConfig) {
            $a = @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One'; ConfigPaths = @($ConfigPath) }
            if ($Verify) { $a.Verify = $true }
            Invoke-Script $a
        }
    }

    BeforeEach {
        $global:LASTEXITCODE = 0
        # The world each test reads (a test changes what it needs). A member is 'admin' (holds the member privilege) or 'user'.
        $global:SetCustomerOwnerTeamsTest_BillingTeamUnit = $global:SetCustomerOwnerTeamsTest_CustomerId
        $global:SetCustomerOwnerTeamsTest_BillingMembers = @('admin')
        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadsprk_Policy'; Id = $global:SetCustomerOwnerTeamsTest_PolicyReadId; Depth = 'Deep' })
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            if ($Method -ne 'Get') { throw "Test: the script wrote ($Method $Uri)." }
            $u = [uri]::UnescapeDataString([string]$Uri)
            $root = $global:SetCustomerOwnerTeamsTest_RootId
            $customer = $global:SetCustomerOwnerTeamsTest_CustomerId
            $teamId = $global:SetCustomerOwnerTeamsTest_BillingTeamId
            $teamName = $global:SetCustomerOwnerTeamsTest_BillingTeamName
            $rootRole = $global:SetCustomerOwnerTeamsTest_RootRoleId
            $copyRole = $global:SetCustomerOwnerTeamsTest_CopyRoleId
            $roleName = $global:SetCustomerOwnerTeamsTest_BillingRoleName
            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ name = 'test' }) } }
            if ($u -match '/businessunits\?') {
                return @{ value = @(
                        [pscustomobject]@{ businessunitid = $root; name = 'Spaarke'; _parentbusinessunitid_value = $null }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_SecureId; name = 'Secure Record'; _parentbusinessunitid_value = $root }
                        [pscustomobject]@{ businessunitid = $customer; name = 'Customer One'; _parentbusinessunitid_value = $root }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_ChildId; name = 'Customer One Litigation'; _parentbusinessunitid_value = $customer }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_OtherCustomerId; name = 'Customer Two'; _parentbusinessunitid_value = $root }
                    )
                }
            }
            if ($u -match "EntityDefinitions\(LogicalName='sprk_policy'\)/Privileges") {
                return @{ value = @([pscustomobject]@{ PrivilegeType = 'Read'; Name = 'prvReadsprk_Policy'; PrivilegeId = [guid]$global:SetCustomerOwnerTeamsTest_PolicyReadId }) }
            }
            if ($u -match '/teams\?') {
                return @{ value = @([pscustomobject]@{ teamid = $teamId; name = $teamName; teamtype = 0; isdefault = $false; _businessunitid_value = $global:SetCustomerOwnerTeamsTest_BillingTeamUnit }) }
            }
            if ($u -match '/teams\([^)]+\)/teammembership_association') {
                $i = 0
                return @{ value = @(foreach ($kind in $global:SetCustomerOwnerTeamsTest_BillingMembers) {
                            $i++
                            $homeUnit = if ($kind -eq 'elsewhere') { $global:SetCustomerOwnerTeamsTest_OtherCustomerId } elseif ($i -eq 1) { $customer } else { $global:SetCustomerOwnerTeamsTest_ChildId }
                            [pscustomobject]@{ systemuserid = "00000000-0000-4000-9000-00000000000$i"; fullname = "$kind $i"; _businessunitid_value = $homeUnit }
                        })
                }
            }
            if ($u -match '/systemusers\(00000000-0000-4000-9000-00000000000(\d)\)/Microsoft\.Dynamics\.CRM\.RetrieveUserPrivileges') {
                $kind = @($global:SetCustomerOwnerTeamsTest_BillingMembers)[[int]$Matches[1] - 1]
                $names = if ($kind -in 'admin', 'elsewhere') { @('prvReadsprk_Policy', $global:SetCustomerOwnerTeamsTest_MemberPrivilege) } else { @('prvReadsprk_Policy') }
                return @{ RolePrivileges = @($names | ForEach-Object { [pscustomobject]@{ PrivilegeName = $_; Depth = 'Basic'; PrivilegeId = [guid]::NewGuid() } }) }
            }
            if ($u -match '/teams\([^)]+\)/teamroles_association') { return @{ value = @([pscustomobject]@{ roleid = $copyRole; name = $roleName }) } }
            if ($u -match '/roles\?.*name eq') { return @{ value = @([pscustomobject]@{ roleid = $rootRole; name = $roleName; _parentrootroleid_value = $rootRole }) } }
            if ($u -match '/roles\?.*_parentrootroleid_value eq') {
                return @{ value = @(
                        [pscustomobject]@{ roleid = $rootRole; _businessunitid_value = $root }
                        [pscustomobject]@{ roleid = $copyRole; _businessunitid_value = $customer }
                    )
                }
            }
            if ($u -match "/roles\($copyRole\)/teamroles_association") { return @{ value = @([pscustomobject]@{ teamid = $teamId; name = $teamName }) } }
            if ($u -match '/roles\([^)]+\)/(teamroles|systemuserroles)_association') { return @{ value = @() } }
            if ($u -match 'RetrieveRolePrivilegesRole') {
                return @{ RolePrivileges = @($global:SetCustomerOwnerTeamsTest_Held | ForEach-Object { [pscustomobject]@{ PrivilegeName = $_.Name; PrivilegeId = [guid]$_.Id; Depth = $_.Depth } }) }
            }
            throw "Test: unexpected read $u"
        }
    }

    It 'must not fire: the shipped shape (Deep Read on sprk_policy only, billing-admin members homed in the customer unit and a child unit) verifies, and -Verify reports unit, privileges, depth and member count' {
        $global:SetCustomerOwnerTeamsTest_BillingMembers = @('admin', 'admin')
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match 'VERIFY PASS'
        $r.Output | Should -Match 'bu=Customer One \(00000000-0000-4000-8000-000000000003\) members=2'
        $r.Output | Should -Match 'Privilege   : prvReadsprk_Policy at Deep'
        $r.Output | Should -Not -Match 'TEAM MEMBERS|MEMBER UNIT|MEMBER PRIVILEGE'
    }

    It 'must fire: a billing admin homed in ANOTHER customer''s unit is not a member this team may have' {
        $global:SetCustomerOwnerTeamsTest_BillingMembers = @('admin', 'elsewhere')
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "MEMBER UNIT: .*'elsewhere 2'.* is homed in 'Customer Two'"
        $r.Output | Should -Not -Match 'MEMBER PRIVILEGE'
    }

    It 'must fire: the role holds sprk_policy Read at Global' {
        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadsprk_Policy'; Id = $global:SetCustomerOwnerTeamsTest_PolicyReadId; Depth = 'Global' })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE DEPTH: prvReadsprk_Policy \(sprk_policy\) at Global, expected Deep'
    }

    It 'must fire: the role holds an extra privilege (Read on sprk_policyversion)' {
        $global:SetCustomerOwnerTeamsTest_Held += @(@{ Name = 'prvReadsprk_PolicyVersion'; Id = '00000000-0000-4000-8000-000000000015'; Depth = 'Deep' })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE OUTSIDE THE FILE: .* holds prvReadsprk_PolicyVersion at Deep'
    }

    It 'must fire: a member without the billing-admin privilege fails -Verify, and a dry run asks for an owner decision' {
        $global:SetCustomerOwnerTeamsTest_BillingMembers = @('admin', 'user')
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "MEMBER PRIVILEGE: .*'user 2'.* does not hold prvWritesprk_ruleset"
        $r.Output | Should -Not -Match "'admin 1'.* does not hold"
        $dry = Invoke-Billing
        $dry.ExitCode | Should -Be 1
        $dry.Output | Should -Match 'OWNER DECISION NEEDED: 1 finding'
    }

    It 'must fire: a team of the name in the ROOT fails -Verify and is an owner decision' {
        $global:SetCustomerOwnerTeamsTest_BillingTeamUnit = $global:SetCustomerOwnerTeamsTest_RootId
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "TEAM PLACE: a team named 'Billing Rule Readers' .* sits in the ROOT business unit"
        $dry = Invoke-Billing
        $dry.ExitCode | Should -Be 1
        $dry.Output | Should -Match 'OWNER DECISION NEEDED'
    }

    It 'must fire: a team of the name in Secure Record fails -Verify' {
        $global:SetCustomerOwnerTeamsTest_BillingTeamUnit = $global:SetCustomerOwnerTeamsTest_SecureId
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'TEAM PLACE: .* sits in the Secure Record business unit'
    }

    It 'must fire: a member-team config asking for Global, Local or Basic depth is refused before any read' {
        foreach ($depth in 'Global', 'Local', 'Basic') {
            $bad = Join-Path $TestDrive "billing-$depth.json"
            $c = Get-Content -LiteralPath $script:BillingConfig -Raw | ConvertFrom-Json
            $c.depth = $depth
            $c | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $bad -Encoding UTF8
            $r = Invoke-Billing -ConfigPath $bad
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "declares a member team at depth '$depth'"
            $r.Output | Should -Not -Match 'Environment :'
        }
    }
}
