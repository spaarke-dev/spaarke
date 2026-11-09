# Pester 5 or 6. Run: Invoke-Pester tests/scripts/Set-CustomerOwnerTeams.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (ubuntu-latest).
# unified-access-control-r2 task 194b R4b-4: scripts/Set-CustomerOwnerTeams.ps1 over a mocked Dataverse (read-only: every
# write is a test failure). Pins B-F1 (no business unit named -SecureBusinessUnitName refuses: Secure Record is never
# accepted as a customer unit) and B-K4 (a dry run whose drift is an owner decision exits non-zero with the summary).
# Task 198 Part A: the Direct-message mode (-DirectMessageBusinessUnitName) refuses the root, Secure Record, a unit that is
# not a direct child of the root, a missing or ambiguous unit, a unit with any user, a team with any member and an
# ambiguous team; a clean unit plans (dry run) and verifies; neither mode reads the other's config.

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
    $global:SetCustomerOwnerTeamsTest_DirectId = '00000000-0000-4000-8000-000000000005'
    $global:SetCustomerOwnerTeamsTest_RoleId = '00000000-0000-4000-8000-000000000006'
    $global:SetCustomerOwnerTeamsTest_CopyId = '00000000-0000-4000-8000-000000000007'
    $global:SetCustomerOwnerTeamsTest_DirectUnitName = 'Spaarke Direct Messages'

    # Each table's Read privilege name, as the config files declare it (metadata agrees with them here), and a stable id.
    $global:SetCustomerOwnerTeamsTest_Privileges = @{}
    $global:SetCustomerOwnerTeamsTest_PrivilegeIds = @{}
    foreach ($file in 'restricted-content-owner-role.json', 'customer-configuration-owner-role.json', 'direct-message-owner-role.json') {
        $config = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' $file) -Raw | ConvertFrom-Json
        foreach ($t in @($config.tables)) {
            $global:SetCustomerOwnerTeamsTest_Privileges[$t.logicalName] = $t.privilegeName
            $global:SetCustomerOwnerTeamsTest_PrivilegeIds[$t.logicalName] = [guid]::NewGuid()
        }
    }
    $global:SetCustomerOwnerTeamsTest_RestrictedTeamName = (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' 'restricted-content-owner-role.json') -Raw |
            ConvertFrom-Json).teamName
    $script:DirectConfig = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' 'direct-message-owner-role.json') -Raw | ConvertFrom-Json
    $global:SetCustomerOwnerTeamsTest_DirectTeamName = $script:DirectConfig.teamName
    $global:SetCustomerOwnerTeamsTest_DirectRoleName = $script:DirectConfig.roleName
    # What the set-up Direct role holds: Read at Basic on exactly the Direct config's tables.
    $global:SetCustomerOwnerTeamsTest_DirectHeld = @(foreach ($t in @($script:DirectConfig.tables)) {
            [pscustomobject]@{ PrivilegeId = $global:SetCustomerOwnerTeamsTest_PrivilegeIds[$t.logicalName]; PrivilegeName = $t.privilegeName; Depth = 'Basic' }
        })

    # Rows are objects, as Invoke-RestMethod returns them (a hashtable's own Count would answer for a single row).
    # The world each test reads (reset in BeforeEach): which team exists, where, how many of it, with how many members;
    # extra business units; users homed in the Direct unit; and whether the Direct role is fully set up.
    function Reset-World {
        $global:SetCustomerOwnerTeamsTest_WithTeam = $false
        $global:SetCustomerOwnerTeamsTest_TeamName = $global:SetCustomerOwnerTeamsTest_RestrictedTeamName
        $global:SetCustomerOwnerTeamsTest_TeamUnitId = $global:SetCustomerOwnerTeamsTest_CustomerId
        $global:SetCustomerOwnerTeamsTest_TeamCount = 1
        $global:SetCustomerOwnerTeamsTest_Members = 0
        $global:SetCustomerOwnerTeamsTest_ExtraUnits = @()
        $global:SetCustomerOwnerTeamsTest_DirectUsers = 0
        $global:SetCustomerOwnerTeamsTest_DirectRoleSetUp = $false
    }

    # The Direct team, set up exactly as the config declares (team, role, the Direct unit's copy assigned, Read at Basic).
    function Use-CleanDirectSetup {
        $global:SetCustomerOwnerTeamsTest_WithTeam = $true
        $global:SetCustomerOwnerTeamsTest_TeamName = $global:SetCustomerOwnerTeamsTest_DirectTeamName
        $global:SetCustomerOwnerTeamsTest_TeamUnitId = $global:SetCustomerOwnerTeamsTest_DirectId
        $global:SetCustomerOwnerTeamsTest_DirectRoleSetUp = $true
    }

    # The mock bodies run from the script under test, so the world they read is global (removed in AfterAll).
    function Invoke-Script([hashtable]$Arguments) {
        $output = & $script:Script @Arguments 6>&1 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    function Invoke-Direct([hashtable]$Extra = @{}) {
        $arguments = @{ EnvironmentUrl = $script:Url; DirectMessageBusinessUnitName = $global:SetCustomerOwnerTeamsTest_DirectUnitName }
        foreach ($k in $Extra.Keys) { $arguments[$k] = $Extra[$k] }
        Invoke-Script $arguments
    }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'SetCustomerOwnerTeamsTest_*' -ErrorAction SilentlyContinue
}

Describe 'Set-CustomerOwnerTeams.ps1 (mocked Dataverse, no writes)' {
    BeforeEach {
        Reset-World
        $global:LASTEXITCODE = 0
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            if ($Method -ne 'Get') { throw "Test: the script wrote ($Method $Uri)." }
            $u = [string]$Uri
            $w = 'SetCustomerOwnerTeamsTest_'
            $root = (Get-Variable -Scope Global -Name "${w}RootId").Value
            $directId = (Get-Variable -Scope Global -Name "${w}DirectId").Value
            $roleId = (Get-Variable -Scope Global -Name "${w}RoleId").Value
            $copyId = (Get-Variable -Scope Global -Name "${w}CopyId").Value
            $teamId = (Get-Variable -Scope Global -Name "${w}TeamId").Value
            $setUp = (Get-Variable -Scope Global -Name "${w}DirectRoleSetUp").Value
            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ name = 'test' }) } }
            if ($u -match '/businessunits\?') {
                return @{ value = @(
                        [pscustomobject]@{ businessunitid = $root; name = 'Spaarke'; _parentbusinessunitid_value = $null }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_SecureId; name = 'Secure Record'; _parentbusinessunitid_value = $root }
                        [pscustomobject]@{ businessunitid = $global:SetCustomerOwnerTeamsTest_CustomerId; name = 'Customer One'; _parentbusinessunitid_value = $root }
                        [pscustomobject]@{ businessunitid = $directId; name = $global:SetCustomerOwnerTeamsTest_DirectUnitName; _parentbusinessunitid_value = $root }
                        @($global:SetCustomerOwnerTeamsTest_ExtraUnits)
                    )
                }
            }
            if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/Privileges") {
                return @{ value = @([pscustomobject]@{ PrivilegeType = 'Read'; Name = $global:SetCustomerOwnerTeamsTest_Privileges[$Matches[1]]; PrivilegeId = $global:SetCustomerOwnerTeamsTest_PrivilegeIds[$Matches[1]] }) }
            }
            if ($u -match '/systemusers\?') {
                if ($u -notmatch [regex]::Escape("_businessunitid_value eq $directId")) { throw "Test: unexpected user read $u" }
                $n = [int]$global:SetCustomerOwnerTeamsTest_DirectUsers
                return @{ value = @(if ($n -gt 0) { 1..$n | ForEach-Object { [pscustomobject]@{ systemuserid = [guid]::NewGuid() } } }) }
            }
            if ($u -match '/teams\?') {
                if ($global:SetCustomerOwnerTeamsTest_WithTeam -and $u -match [regex]::Escape(($global:SetCustomerOwnerTeamsTest_TeamName -replace "'", "''"))) {
                    $count = [int]$global:SetCustomerOwnerTeamsTest_TeamCount
                    return @{ value = @(1..$count | ForEach-Object {
                                [pscustomobject]@{ teamid = $(if ($_ -eq 1) { $teamId } else { [guid]::NewGuid().ToString() }); name = $global:SetCustomerOwnerTeamsTest_TeamName; teamtype = 0; isdefault = $false; _businessunitid_value = $global:SetCustomerOwnerTeamsTest_TeamUnitId }
                            })
                    }
                }
                return @{ value = @() }
            }
            if ($u -match '/teammembership_association') {
                $n = [int]$global:SetCustomerOwnerTeamsTest_Members
                return @{ value = @(if ($n -gt 0) { 1..$n | ForEach-Object { [pscustomobject]@{ systemuserid = [guid]::NewGuid(); fullname = "member $_" } } }) }
            }
            if ($u -match '/roles\(([^)]+)\)/teamroles_association') {
                if ($setUp -and $Matches[1] -eq $copyId) { return @{ value = @([pscustomobject]@{ teamid = $teamId; name = $global:SetCustomerOwnerTeamsTest_TeamName }) } }
                return @{ value = @() }
            }
            if ($u -match '/teams\([^)]+\)/teamroles_association') {
                if ($setUp) { return @{ value = @([pscustomobject]@{ roleid = $copyId; name = $global:SetCustomerOwnerTeamsTest_DirectRoleName }) } }
                return @{ value = @() }
            }
            if ($u -match '/systemuserroles_association') { return @{ value = @() } }
            if ($u -match 'RetrieveRolePrivilegesRole') {
                if (-not $setUp) { throw "Test: unexpected read $u" }
                return @{ RolePrivileges = @($global:SetCustomerOwnerTeamsTest_DirectHeld) }
            }
            if ($u -match '/roles\?') {
                if (-not $setUp) { return @{ value = @() } }
                if ($u -match '_parentrootroleid_value eq') {
                    return @{ value = @(
                            [pscustomobject]@{ roleid = $roleId; _businessunitid_value = $root }
                            [pscustomobject]@{ roleid = $copyId; _businessunitid_value = $directId }
                        )
                    }
                }
                if ($u -match [regex]::Escape($global:SetCustomerOwnerTeamsTest_DirectRoleName)) {
                    return @{ value = @([pscustomobject]@{ roleid = $roleId; name = $global:SetCustomerOwnerTeamsTest_DirectRoleName; _parentrootroleid_value = $roleId }) }
                }
                return @{ value = @() }
            }
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

    It 'Customer mode reads only the customer files: it never plans the Direct team' {
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One' }
        $r.Output | Should -Not -Match ([regex]::Escape($global:SetCustomerOwnerTeamsTest_DirectTeamName))
        $r.Output | Should -Not -Match 'Direct BU'
    }

    It 'Customer mode refuses a direct config file' {
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One'; ConfigPaths = @(Join-Path $script:RepoRoot 'config' 'direct-message-owner-role.json') }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "names business unit 'direct'"
    }

    Context 'Direct-message mode (-DirectMessageBusinessUnitName)' {
        It 'must fire: the root business unit is refused' {
            $r = Invoke-Script @{ EnvironmentUrl = $script:Url; DirectMessageBusinessUnitName = 'Spaarke' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'is the ROOT business unit'
            $r.Output | Should -Not -Match 'PLAN'
        }

        It 'must fire: the Secure Record unit is refused' {
            $r = Invoke-Script @{ EnvironmentUrl = $script:Url; DirectMessageBusinessUnitName = 'Secure Record' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'is the Secure Record business unit'
            $r.Output | Should -Not -Match 'PLAN'
        }

        It 'must fire: a unit that is not a direct child of the root is refused' {
            $global:SetCustomerOwnerTeamsTest_ExtraUnits = @([pscustomobject]@{ businessunitid = '00000000-0000-4000-8000-000000000008'; name = 'Nested Direct'; _parentbusinessunitid_value = $global:SetCustomerOwnerTeamsTest_CustomerId })
            $r = Invoke-Script @{ EnvironmentUrl = $script:Url; DirectMessageBusinessUnitName = 'Nested Direct' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'is not a direct child of the root'
            $r.Output | Should -Not -Match 'PLAN'
        }

        It 'must fire: a missing unit is refused and never created' {
            $r = Invoke-Script @{ EnvironmentUrl = $script:Url; DirectMessageBusinessUnitName = 'Nonexistent'; Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'does not create the Direct-message business unit'
        }

        It 'must fire: an ambiguous unit name is refused' {
            $global:SetCustomerOwnerTeamsTest_ExtraUnits = @([pscustomobject]@{ businessunitid = '00000000-0000-4000-8000-000000000009'; name = $global:SetCustomerOwnerTeamsTest_DirectUnitName; _parentbusinessunitid_value = $global:SetCustomerOwnerTeamsTest_RootId })
            $r = Invoke-Direct
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'Expected exactly one business unit'
            $r.Output | Should -Not -Match 'PLAN'
        }

        It 'must fire: a unit with any user is refused' {
            $global:SetCustomerOwnerTeamsTest_DirectUsers = 1
            $r = Invoke-Direct
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'has 1 user\(s\) homed in it'
            $r.Output | Should -Not -Match 'PLAN'
        }

        It 'must fire: a unit with any user is refused under -Apply, before any write' {
            $global:SetCustomerOwnerTeamsTest_DirectUsers = 2
            $r = Invoke-Direct @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'has 2 user\(s\) homed in it'
            $r.Output | Should -Not -Match 'the script wrote'
        }

        It 'must fire: a Direct team with a member is an owner decision (dry run exits 1)' {
            Use-CleanDirectSetup
            $global:SetCustomerOwnerTeamsTest_Members = 1
            $r = Invoke-Direct
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'TEAM MEMBERS'
            $r.Output | Should -Match 'OWNER DECISION NEEDED: 1 finding'
        }

        It 'must fire: a Direct team with a member is refused by -Apply, and no member is removed' {
            Use-CleanDirectSetup
            $global:SetCustomerOwnerTeamsTest_Members = 1
            $r = Invoke-Direct @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'Refused: the drift above is an owner decision'
            $r.Output | Should -Not -Match 'the script wrote'
        }

        It 'must fire: a Direct team with a member fails -Verify' {
            Use-CleanDirectSetup
            $global:SetCustomerOwnerTeamsTest_Members = 1
            $r = Invoke-Direct @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'VERIFY FAIL'
        }

        It 'must fire: two Direct teams of that name in the unit are refused' {
            Use-CleanDirectSetup
            $global:SetCustomerOwnerTeamsTest_TeamCount = 2
            $r = Invoke-Direct
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'Expected at most one team named'
        }

        It 'must fire: -Verify on a unit without the team exits 1 (team and role missing)' {
            $r = Invoke-Direct @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'TEAM MISSING'
            $r.Output | Should -Match 'ROLE MISSING'
        }

        It 'must fire: the Direct mode refuses a customer config file' {
            $r = Invoke-Direct @{ ConfigPaths = @(Join-Path $script:RepoRoot 'config' 'restricted-content-owner-role.json') }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "names business unit 'customer'"
        }

        It 'must not fire: a clean unit dry-runs to the Direct plan only, exit 0' {
            $r = Invoke-Direct
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'Direct BU   : Spaarke Direct Messages'
            $r.Output | Should -Match ([regex]::Escape("create the Owner team '$($global:SetCustomerOwnerTeamsTest_DirectTeamName)'"))
            $r.Output | Should -Match ([regex]::Escape("create the role '$($global:SetCustomerOwnerTeamsTest_DirectRoleName)'"))
            $r.Output | Should -Match 'prvReadsprk_Communication'
            $r.Output | Should -Match 'Re-run with -Apply'
            $r.Output | Should -Not -Match 'OWNER DECISION NEEDED'
            $r.Output | Should -Not -Match ([regex]::Escape($global:SetCustomerOwnerTeamsTest_RestrictedTeamName))
            $r.Output | Should -Not -Match 'INFO'
        }

        It 'must not fire: a unit set up as the config declares passes -Verify, exit 0' {
            Use-CleanDirectSetup
            $r = Invoke-Direct @{ Verify = $true }
            $r.Output | Should -Match 'Drift       : none'
            $r.Output | Should -Match 'VERIFY PASS'
            $r.ExitCode | Should -Be 0
        }

        It 'must not fire: a unit set up as the config declares has nothing to apply, exit 0' {
            Use-CleanDirectSetup
            $r = Invoke-Direct @{ Apply = $true }
            $r.Output | Should -Match 'Nothing to do'
            $r.Output | Should -Not -Match 'the script wrote'
            $r.ExitCode | Should -Be 0
        }
    }
}
