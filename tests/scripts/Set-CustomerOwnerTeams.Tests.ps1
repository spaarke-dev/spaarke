# Pester 5.7.1 (6.x does not load the containers here). Run: Invoke-Pester tests/scripts/Set-CustomerOwnerTeams.Tests.ps1
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
    $global:SetCustomerOwnerTeamsTest_OrgId = '00000000-0000-4000-8000-000000000005'

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
        $global:SetCustomerOwnerTeamsTest_TeamUnit = $global:SetCustomerOwnerTeamsTest_CustomerId
        $global:LASTEXITCODE = 0
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            if ($Method -ne 'Get') { throw "Test: the script wrote ($Method $Uri)." }
            $u = [string]$Uri
            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ organizationid = $global:SetCustomerOwnerTeamsTest_OrgId; name = 'test'; orgdborgsettings = '<OrgSettings><EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>' }) } }
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
                    return @{ value = @([pscustomobject]@{ teamid = $global:SetCustomerOwnerTeamsTest_TeamId; name = $global:SetCustomerOwnerTeamsTest_RestrictedTeamName; teamtype = 0; isdefault = $false; _businessunitid_value = $global:SetCustomerOwnerTeamsTest_TeamUnit }) }
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

    It 'must fire: an owner team of the name in the ROOT is TEAM PLACE drift and an owner decision' {
        $global:SetCustomerOwnerTeamsTest_WithTeam = $true
        $global:SetCustomerOwnerTeamsTest_TeamUnit = $global:SetCustomerOwnerTeamsTest_RootId
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One' }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "TEAM PLACE: a team named '$([regex]::Escape($global:SetCustomerOwnerTeamsTest_RestrictedTeamName))' .* sits in the ROOT business unit"
        $r.Output | Should -Match 'OWNER DECISION NEEDED'
    }

    It 'must fire: an owner team of the name in Secure Record is TEAM PLACE drift' {
        $global:SetCustomerOwnerTeamsTest_WithTeam = $true
        $global:SetCustomerOwnerTeamsTest_TeamUnit = $global:SetCustomerOwnerTeamsTest_SecureId
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One' }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'TEAM PLACE: .* sits in the Secure Record business unit'
    }
}

# ebilling-r1 task 089d (option R): the 'Billing Rule Reader' role assigned directly to users
# (config/billing-rule-readers-role.json), run alone through -ConfigPaths so -Verify's exit code is this role's. The world:
# the org setting, the role in the root with a copy in every unit, isinherited per copy, the role's privileges, who holds
# which copy (users with a home unit and whether they hold the billing-admin marker; teams), and the users the assignment
# helper names. Every write is counted and fails the test unless the test allows that one write.
Describe 'Set-CustomerOwnerTeams.ps1: the directly assigned Billing Rule Reader role (mocked Dataverse)' {
    BeforeAll {
        $script:BillingConfig = Join-Path $script:RepoRoot 'config' 'billing-rule-readers-role.json'
        $billing = Get-Content -LiteralPath $script:BillingConfig -Raw | ConvertFrom-Json
        $global:SetCustomerOwnerTeamsTest_BillingRoleName = $billing.roleName
        $global:SetCustomerOwnerTeamsTest_HolderPrivilege = $billing.holderPrivilege
        $global:SetCustomerOwnerTeamsTest_PolicyReadId = '00000000-0000-4000-8000-000000000014'
        $global:SetCustomerOwnerTeamsTest_PlatformPrivilegeId = '00000000-0000-4000-8000-000000000016'  # prvReadSdkMessage, added by the platform on role create
        $global:SetCustomerOwnerTeamsTest_ChildId = '00000000-0000-4000-8000-000000000021'          # a child unit of Customer One
        $global:SetCustomerOwnerTeamsTest_OtherCustomerId = '00000000-0000-4000-8000-000000000022'  # another customer's top unit
        $global:SetCustomerOwnerTeamsTest_OtherChildId = '00000000-0000-4000-8000-000000000023'     # a child unit of Customer Two
        # One copy of the role per unit (the root copy is the role itself), keyed by a short name the tests use. 'otherchild'
        # is a unit only (a holder's home); it has no entry in CopyId, so the role has no copy in it here.
        $global:SetCustomerOwnerTeamsTest_Unit = @{
            root       = $global:SetCustomerOwnerTeamsTest_RootId
            secure     = $global:SetCustomerOwnerTeamsTest_SecureId
            customer   = $global:SetCustomerOwnerTeamsTest_CustomerId
            child      = $global:SetCustomerOwnerTeamsTest_ChildId
            other      = $global:SetCustomerOwnerTeamsTest_OtherCustomerId
            otherchild = $global:SetCustomerOwnerTeamsTest_OtherChildId
        }
        $global:SetCustomerOwnerTeamsTest_CopyId = @{
            root     = '00000000-0000-4000-8000-000000000101'
            secure   = '00000000-0000-4000-8000-000000000102'
            customer = '00000000-0000-4000-8000-000000000103'
            child    = '00000000-0000-4000-8000-000000000104'
            other    = '00000000-0000-4000-8000-000000000105'
        }
        $global:SetCustomerOwnerTeamsTest_CandidateId = '00000000-0000-4000-9000-000000000099'
        function Invoke-Billing([switch]$Verify, [switch]$Apply, [string]$ConfigPath = $script:BillingConfig, [guid[]]$Assign) {
            $a = @{ EnvironmentUrl = $script:Url; CustomerBusinessUnitName = 'Customer One'; ConfigPaths = @($ConfigPath) }
            if ($Verify) { $a.Verify = $true }
            if ($Apply) { $a.Apply = $true }
            if ($Assign) { $a.AssignBillingRuleReader = $Assign }
            Invoke-Script $a
        }
        function New-BadConfig([string]$Name, [scriptblock]$Change) {
            $c = Get-Content -LiteralPath $script:BillingConfig -Raw | ConvertFrom-Json
            & $Change $c
            $path = Join-Path $TestDrive "billing-$Name.json"
            $c | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
            $path
        }
    }

    BeforeEach {
        $global:LASTEXITCODE = 0
        # The world each test reads (a test changes what it needs).
        $global:SetCustomerOwnerTeamsTest_OrgSettings = '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>'
        $global:SetCustomerOwnerTeamsTest_RoleExists = $true
        $global:SetCustomerOwnerTeamsTest_IsInherited = 0
        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadsprk_Policy'; Id = $global:SetCustomerOwnerTeamsTest_PolicyReadId; Depth = 'Deep' })
        # Users holding a copy: Copy = which unit's copy; Home = the unit the user is homed in; Admin = holds the marker.
        $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'customer'; Home = 'child'; Admin = $true })
        $global:SetCustomerOwnerTeamsTest_TeamHolders = @()  # each: @{ Copy = '...' }
        # The user -AssignBillingRuleReader names.
        $global:SetCustomerOwnerTeamsTest_Candidate = @{ Home = 'child'; Admin = $true }
        $global:SetCustomerOwnerTeamsTest_Writes = [System.Collections.Generic.List[string]]::new()
        $global:SetCustomerOwnerTeamsTest_WriteBodies = [System.Collections.Generic.List[string]]::new()  # parallel to Writes
        $global:SetCustomerOwnerTeamsTest_Events = [System.Collections.Generic.List[string]]::new()      # org reads and every write, in order
        $global:SetCustomerOwnerTeamsTest_AllowOrgPatch = $false
        $global:SetCustomerOwnerTeamsTest_SecureUnderCustomer = $false  # K1: Secure Record placed under Customer One instead of the root
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $false  # the role create/patch, privilege add and user assignment change the world
        $global:SetCustomerOwnerTeamsTest_CopyIsInherited = @{}     # per-copy isinherited that differs from the role's (key = unit short name)
        $global:SetCustomerOwnerTeamsTest_OrgReadQueue = [System.Collections.Generic.Queue[string]]::new()  # org reads answered from here first
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            $u = [uri]::UnescapeDataString([string]$Uri)
            $unit = $global:SetCustomerOwnerTeamsTest_Unit
            $copy = $global:SetCustomerOwnerTeamsTest_CopyId
            $roleName = $global:SetCustomerOwnerTeamsTest_BillingRoleName
            function UserId([int]$i) { '00000000-0000-4000-9000-{0:D12}' -f $i }
            function PrivilegesOf([bool]$admin) {
                $names = if ($admin) { @('prvReadsprk_Policy', $global:SetCustomerOwnerTeamsTest_HolderPrivilege) } else { @('prvReadsprk_Policy') }
                @{ RolePrivileges = @($names | ForEach-Object { [pscustomobject]@{ PrivilegeName = $_; Depth = 'Basic'; PrivilegeId = [guid]::NewGuid() } }) }
            }
            if ($Method -ne 'Get') {
                $bodyText = if ($null -ne $Body) { [Text.Encoding]::UTF8.GetString($Body) } else { '' }
                $global:SetCustomerOwnerTeamsTest_Writes.Add("$Method $u")
                $global:SetCustomerOwnerTeamsTest_WriteBodies.Add($bodyText)
                $global:SetCustomerOwnerTeamsTest_Events.Add("$Method $u")
                if ($global:SetCustomerOwnerTeamsTest_AllowOrgPatch -and $Method -eq 'Patch' -and $u -match '/organizations\(') {
                    $global:SetCustomerOwnerTeamsTest_OrgSettings = ($bodyText | ConvertFrom-Json).orgdborgsettings
                    return $null
                }
                if ($global:SetCustomerOwnerTeamsTest_AllowRoleWrites) {
                    $json = if ($bodyText) { $bodyText | ConvertFrom-Json } else { $null }
                    if ($Method -eq 'Post' -and $u -match '/api/data/v9\.2/roles$') {
                        # The role is created holding one privilege the platform adds on create (outside the file), and the
                        # role's own isinherited from the body. The script must take that privilege off.
                        $global:SetCustomerOwnerTeamsTest_RoleExists = $true
                        $global:SetCustomerOwnerTeamsTest_IsInherited = $json.isinherited
                        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadSdkMessage'; Id = $global:SetCustomerOwnerTeamsTest_PlatformPrivilegeId; Depth = 'Basic' })
                        return $null
                    }
                    if ($Method -eq 'Patch' -and $u -match '/api/data/v9\.2/roles\(([^)]+)\)$') {
                        # Copies follow the root role.
                        $global:SetCustomerOwnerTeamsTest_IsInherited = $json.isinherited
                        $global:SetCustomerOwnerTeamsTest_CopyIsInherited = @{}
                        return $null
                    }
                    if ($Method -eq 'Post' -and $u -match 'roles\(([^)]+)\)/Microsoft\.Dynamics\.CRM\.AddPrivilegesRole$') {
                        $global:SetCustomerOwnerTeamsTest_Held = @($global:SetCustomerOwnerTeamsTest_Held) + @($json.Privileges | ForEach-Object { @{ Name = $_.PrivilegeName; Id = $_.PrivilegeId; Depth = $_.Depth } })
                        return $null
                    }
                    if ($Method -eq 'Post' -and $u -match 'roles\(([^)]+)\)/Microsoft\.Dynamics\.CRM\.RemovePrivilegeRole$') {
                        $gone = [string]$json.Privilege.privilegeid
                        $global:SetCustomerOwnerTeamsTest_Held = @($global:SetCustomerOwnerTeamsTest_Held | Where-Object { $_.Id -ne $gone })
                        return $null
                    }
                    if ($Method -eq 'Post' -and $u -match 'systemusers\(([^)]+)\)/systemuserroles_association/\$ref$') {
                        # The user now holds whichever copy the body names (a copy the mock does not know is held as 'unknown').
                        $userId = $Matches[1]
                        $copyId = if ([string]$json.'@odata.id' -match 'roles\(([^)]+)\)$') { $Matches[1] } else { '' }
                        $key = @($global:SetCustomerOwnerTeamsTest_CopyId.Keys | Where-Object { $global:SetCustomerOwnerTeamsTest_CopyId[$_] -eq $copyId })[0]
                        $global:SetCustomerOwnerTeamsTest_UserHolders = @($global:SetCustomerOwnerTeamsTest_UserHolders) + @(@{
                                Id = $userId; Copy = $(if ($key) { $key } else { 'unknown' }); Home = $global:SetCustomerOwnerTeamsTest_Candidate.Home; Admin = $global:SetCustomerOwnerTeamsTest_Candidate.Admin
                            })
                        return $null
                    }
                }
                throw "Test: the script wrote ($Method $u)."
            }
            if ($u -match '/organizations\?') {
                $global:SetCustomerOwnerTeamsTest_Events.Add('Get organizations')
                $settings = if ($global:SetCustomerOwnerTeamsTest_OrgReadQueue.Count -gt 0) { $global:SetCustomerOwnerTeamsTest_OrgReadQueue.Dequeue() } else { $global:SetCustomerOwnerTeamsTest_OrgSettings }
                return @{ value = @([pscustomobject]@{ organizationid = $global:SetCustomerOwnerTeamsTest_OrgId; name = 'test'; orgdborgsettings = $settings }) }
            }
            if ($u -match '/businessunits\?') {
                return @{ value = @(
                        [pscustomobject]@{ businessunitid = $unit.root; name = 'Spaarke'; _parentbusinessunitid_value = $null }
                        [pscustomobject]@{ businessunitid = $unit.secure; name = 'Secure Record'; _parentbusinessunitid_value = $(if ($global:SetCustomerOwnerTeamsTest_SecureUnderCustomer) { $unit.customer } else { $unit.root }) }
                        [pscustomobject]@{ businessunitid = $unit.customer; name = 'Customer One'; _parentbusinessunitid_value = $unit.root }
                        [pscustomobject]@{ businessunitid = $unit.child; name = 'Customer One Litigation'; _parentbusinessunitid_value = $unit.customer }
                        [pscustomobject]@{ businessunitid = $unit.other; name = 'Customer Two'; _parentbusinessunitid_value = $unit.root }
                        [pscustomobject]@{ businessunitid = $unit.otherchild; name = 'Customer Two Litigation'; _parentbusinessunitid_value = $unit.other }
                    )
                }
            }
            if ($u -match "EntityDefinitions\(LogicalName='sprk_policy'\)/Privileges") {
                return @{ value = @([pscustomobject]@{ PrivilegeType = 'Read'; Name = 'prvReadsprk_Policy'; PrivilegeId = [guid]$global:SetCustomerOwnerTeamsTest_PolicyReadId }) }
            }
            if ($u -match '/roles\?.*name eq') {
                if (-not $global:SetCustomerOwnerTeamsTest_RoleExists) { return @{ value = @() } }
                return @{ value = @([pscustomobject]@{ roleid = $copy.root; name = $roleName; isinherited = $global:SetCustomerOwnerTeamsTest_IsInherited; _parentrootroleid_value = $copy.root }) }
            }
            if ($u -match '/roles\?.*_parentrootroleid_value eq') {
                return @{ value = @(foreach ($k in 'root', 'secure', 'customer', 'child', 'other') {
                            $inh = if ($global:SetCustomerOwnerTeamsTest_CopyIsInherited.ContainsKey($k)) { $global:SetCustomerOwnerTeamsTest_CopyIsInherited[$k] } else { $global:SetCustomerOwnerTeamsTest_IsInherited }
                            [pscustomobject]@{ roleid = $copy[$k]; isinherited = $inh; _businessunitid_value = $unit[$k] }
                        })
                }
            }
            if ($u -match '/roles\(([^)]+)\)/teamroles_association') {
                $id = $Matches[1]; $i = 0
                return @{ value = @(foreach ($t in $global:SetCustomerOwnerTeamsTest_TeamHolders) {
                            $i++
                            if ($copy[$t.Copy] -eq $id) { [pscustomobject]@{ teamid = (UserId (500 + $i)); name = "team $i"; _businessunitid_value = $unit[$t.Copy] } }
                        })
                }
            }
            if ($u -match '/roles\(([^)]+)\)/systemuserroles_association') {
                $id = $Matches[1]; $i = 0
                return @{ value = @(foreach ($h in $global:SetCustomerOwnerTeamsTest_UserHolders) {
                            $i++
                            if ($copy[$h.Copy] -eq $id) { [pscustomobject]@{ systemuserid = $(if ($h.Id) { $h.Id } else { UserId $i }); fullname = "holder $i"; _businessunitid_value = $unit[$h.Home] } }
                        })
                }
            }
            if ($u -match '/systemusers\(([^)]+)\)/Microsoft\.Dynamics\.CRM\.RetrieveUserPrivileges') {
                $id = $Matches[1]
                if ($id -eq $global:SetCustomerOwnerTeamsTest_CandidateId) { return PrivilegesOf $global:SetCustomerOwnerTeamsTest_Candidate.Admin }
                $named = @($global:SetCustomerOwnerTeamsTest_UserHolders | Where-Object { $_.Id -and $_.Id -eq $id })
                if ($named.Count -gt 0) { return PrivilegesOf $named[0].Admin }  # a holder added by an assignment
                $i = [int]($id.Substring($id.Length - 12))
                return PrivilegesOf (@($global:SetCustomerOwnerTeamsTest_UserHolders)[$i - 1].Admin)
            }
            if ($u -match '/systemusers\(([^)]+)\)\?') {
                return [pscustomobject]@{ systemuserid = $Matches[1]; fullname = 'candidate'; _businessunitid_value = $unit[$global:SetCustomerOwnerTeamsTest_Candidate.Home] }
            }
            if ($u -match 'RetrieveRolePrivilegesRole') {
                return @{ RolePrivileges = @($global:SetCustomerOwnerTeamsTest_Held | ForEach-Object { [pscustomobject]@{ PrivilegeName = $_.Name; PrivilegeId = [guid]$_.Id; Depth = $_.Depth } }) }
            }
            throw "Test: unexpected read $u"
        }
    }

    It 'must not fire: the shipped shape verifies (org setting on, isinherited 0, Deep Read on sprk_policy only, one billing admin homed in a child unit holding the top unit''s copy)' {
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match 'VERIFY PASS'
        $r.Output | Should -Match "Org setting : EnableOwnershipAcrossBusinessUnits = 'true'"
        $r.Output | Should -Match 'isinherited=0'
        $r.Output | Should -Match 'Privilege   : prvReadsprk_Policy at Deep'
        $r.Output | Should -Match 'Holders     : 1 of the Customer One copy; team holders \(any copy\): 0; root-copy holders: 0'
        $r.Output | Should -Match "Holder      : 'holder 1' .* homed in 'Customer One Litigation' .*prvWritesprk_ruleset: yes"
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It 'must not fire: holders of ANOTHER customer top unit''s copy are information, checked by that unit''s run' {
        $global:SetCustomerOwnerTeamsTest_UserHolders += @(@{ Copy = 'other'; Home = 'other'; Admin = $true })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match "INFO        : 1 user\(s\) hold the 'Customer Two' copy"
    }

    It 'must fire: the org setting is off, or absent; -Verify fails and the dry run plans it as its own org-wide step' {
        foreach ($xml in '<OrgSettings><EnableOwnershipAcrossBusinessUnits>false</EnableOwnershipAcrossBusinessUnits></OrgSettings>', '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel></OrgSettings>', '') {
            $global:SetCustomerOwnerTeamsTest_OrgSettings = $xml
            $r = Invoke-Billing -Verify
            $r.ExitCode | Should -Be 1 -Because $xml
            $r.Output | Should -Match 'ORG SETTING: EnableOwnershipAcrossBusinessUnits is .*expected true'
            $dry = Invoke-Billing
            $dry.ExitCode | Should -Be 0 -Because $dry.Output
            $dry.Output | Should -Match '1\. ORG-WIDE WRITE, its own step: set EnableOwnershipAcrossBusinessUnits to true'
        }
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It '-Apply sets the org setting as its own logged step, changes that one element only, and reads it back' {
        $global:SetCustomerOwnerTeamsTest_OrgSettings = '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>false</EnableOwnershipAcrossBusinessUnits></OrgSettings>'
        $global:SetCustomerOwnerTeamsTest_AllowOrgPatch = $true
        $r = Invoke-Billing -Apply
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match "ORG SETTING : writing organization 'test' \($([regex]::Escape($global:SetCustomerOwnerTeamsTest_OrgId))\)\.orgdborgsettings: EnableOwnershipAcrossBusinessUnits 'false' -> 'true'"
        $r.Output | Should -Match 'no other orgdborgsettings element changed'
        @($global:SetCustomerOwnerTeamsTest_Writes) | Should -Be @("Patch https://example.crm.dynamics.com/api/data/v9.2/organizations($($global:SetCustomerOwnerTeamsTest_OrgId))")
        $global:SetCustomerOwnerTeamsTest_OrgSettings | Should -Be '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>'
    }

    It 'must fire (uac-r2 condition 5): isinherited 1 fails -Verify; the dry run plans setting it to 0' {
        $global:SetCustomerOwnerTeamsTest_IsInherited = 1
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "ROLE INHERITED: 'Billing Rule Reader' .* has isinherited '1', expected 0"
        $dry = Invoke-Billing
        $dry.ExitCode | Should -Be 0 -Because $dry.Output
        $dry.Output | Should -Match "set isinherited to 0 \(Team privileges only\) on 'Billing Rule Reader'"
    }

    It 'a missing role is planned with isinherited 0 in the root, and nothing in Secure Record' {
        $global:SetCustomerOwnerTeamsTest_RoleExists = $false
        $dry = Invoke-Billing
        $dry.ExitCode | Should -Be 0 -Because $dry.Output
        $dry.Output | Should -Match "create the role 'Billing Rule Reader' in the root business unit with isinherited 0"
        $dry.Output | Should -Match 'add Read at Deep to .*prvReadsprk_Policy'
        $dry.Output | Should -Match 'and nothing in Secure Record'
        $dry.Output | Should -Match 're-run with -AssignBillingRuleReader'
    }

    It 'must fire: the ROOT copy assigned to a user (Deep from the root reaches Secure Record)' {
        $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'root'; Home = 'child'; Admin = $true })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "ROLE ASSIGNED ELSEWHERE: user 'holder 1' .* holds the ROOT copy"
        $dry = Invoke-Billing
        $dry.ExitCode | Should -Be 1
        $dry.Output | Should -Match 'OWNER DECISION NEEDED'
    }

    It 'must fire: a child unit''s copy assigned (the admin-UI mistake; it reaches nothing above that unit)' {
        $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'child'; Home = 'child'; Admin = $true })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "ROLE ASSIGNED ELSEWHERE: user 'holder 1' .* holds the 'Customer One Litigation' copy .* below a customer top unit"
    }

    It 'must fire: the Secure Record copy assigned' {
        $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'secure'; Home = 'child'; Admin = $true })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "ROLE ASSIGNED ELSEWHERE: .* holds the 'Secure Record' copy .* in or below Secure Record"
    }

    It 'must fire: a team holds the role, whichever unit''s copy' {
        foreach ($k in 'customer', 'root', 'other') {
            $global:SetCustomerOwnerTeamsTest_TeamHolders = @(@{ Copy = $k })
            $r = Invoke-Billing -Verify
            $r.ExitCode | Should -Be 1 -Because $k
            $r.Output | Should -Match "ROLE HELD BY TEAM: team 'team 1' .* no team may hold it"
            $dry = Invoke-Billing
            $dry.ExitCode | Should -Be 1
            $dry.Output | Should -Match 'OWNER DECISION NEEDED'
        }
    }

    It 'must fire: a holder without the billing-admin marker' {
        $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'customer'; Home = 'child'; Admin = $true }, @{ Copy = 'customer'; Home = 'customer'; Admin = $false })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "HOLDER PRIVILEGE: .* holder 'holder 2' .* does not hold prvWritesprk_ruleset"
        $r.Output | Should -Not -Match "holder 'holder 1' .* does not hold"
    }

    It 'must fire: a holder homed outside the customer subtree (Secure Record, the root, another customer)' {
        foreach ($homeUnit in 'secure', 'root', 'other') {
            $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'customer'; Home = $homeUnit; Admin = $true })
            $r = Invoke-Billing -Verify
            $r.ExitCode | Should -Be 1 -Because $homeUnit
            $r.Output | Should -Match "HOLDER UNIT: .* holder 'holder 1' .* outside Customer One and the units below it"
        }
    }

    It 'must fire: the role grants Global depth, or an extra privilege' {
        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadsprk_Policy'; Id = $global:SetCustomerOwnerTeamsTest_PolicyReadId; Depth = 'Global' })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE DEPTH: prvReadsprk_Policy \(sprk_policy\) at Global, expected Deep'
        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadsprk_Policy'; Id = $global:SetCustomerOwnerTeamsTest_PolicyReadId; Depth = 'Deep' }, @{ Name = 'prvReadsprk_PolicyVersion'; Id = '00000000-0000-4000-8000-000000000015'; Depth = 'Deep' })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE OUTSIDE THE FILE: .* holds prvReadsprk_PolicyVersion at Deep'
    }

    It 'the assignment helper: a dry run for a billing admin homed in a child unit plans the top unit''s copy and writes nothing' {
        $r = Invoke-Billing -Assign $global:SetCustomerOwnerTeamsTest_CandidateId
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match "assign the Customer One copy of 'Billing Rule Reader' to user 'candidate' .* homed in 'Customer One Litigation', holding prvWritesprk_ruleset"
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It 'must fire: the assignment helper refuses a user homed outside the customer subtree, and -Apply writes nothing' {
        foreach ($homeUnit in 'other', 'secure', 'root') {
            $global:SetCustomerOwnerTeamsTest_Candidate = @{ Home = $homeUnit; Admin = $true }
            $r = Invoke-Billing -Assign $global:SetCustomerOwnerTeamsTest_CandidateId
            $r.ExitCode | Should -Be 1 -Because $homeUnit
            $r.Output | Should -Match "ASSIGN REFUSED: user 'candidate' .* outside Customer One and the units below it"
            $a = Invoke-Billing -Apply -Assign $global:SetCustomerOwnerTeamsTest_CandidateId
            $a.ExitCode | Should -Be 1
            $a.Output | Should -Match 'Refused: the drift above is an owner decision. Nothing was written.'
        }
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It 'must fire: the assignment helper refuses a user without the billing-admin marker, and -Apply writes nothing' {
        $global:SetCustomerOwnerTeamsTest_Candidate = @{ Home = 'child'; Admin = $false }
        $r = Invoke-Billing -Assign $global:SetCustomerOwnerTeamsTest_CandidateId
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "ASSIGN REFUSED: user 'candidate' .* does not hold prvWritesprk_ruleset"
        $a = Invoke-Billing -Apply -Assign $global:SetCustomerOwnerTeamsTest_CandidateId
        $a.ExitCode | Should -Be 1
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It '-Verify refuses -AssignBillingRuleReader (read-only mode)' {
        $r = Invoke-Billing -Verify -Assign $global:SetCustomerOwnerTeamsTest_CandidateId
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match '-Verify is read-only'
    }

    # ── pass-1 review (089d R): the writes that decide isolation, and the other customer's copy ─────────────────────────────────

    It 'F2 must fire: a holder of ANOTHER customer top unit''s copy who is homed outside THAT unit''s subtree (here: in this customer) is HOLDER UNIT drift and an owner decision' {
        foreach ($homeUnit in 'child', 'customer', 'root', 'secure') {
            $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'other'; Home = $homeUnit; Admin = $true })
            $r = Invoke-Billing -Verify
            $r.ExitCode | Should -Be 1 -Because $homeUnit
            $r.Output | Should -Match "HOLDER UNIT: user 'holder 1' .* holds the 'Customer Two' copy .* but is homed in '.*' .*, outside that unit and the units below it"
            $r.Output | Should -Not -Match 'INFO        : .* hold the .Customer Two. copy'
            $dry = Invoke-Billing
            $dry.ExitCode | Should -Be 1 -Because $homeUnit
            $dry.Output | Should -Match 'OWNER DECISION NEEDED'
        }
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It 'F2 must not fire: holders of another customer top unit''s copy homed in THAT unit or a child of it are INFO and -Verify passes' {
        $global:SetCustomerOwnerTeamsTest_UserHolders = @(@{ Copy = 'customer'; Home = 'child'; Admin = $true }, @{ Copy = 'other'; Home = 'otherchild'; Admin = $true }, @{ Copy = 'other'; Home = 'other'; Admin = $true })
        $r = Invoke-Billing -Verify
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match "INFO        : 2 user\(s\) hold the 'Customer Two' copy, homed in that unit or below"
        $r.Output | Should -Not -Match 'HOLDER UNIT'
    }

    It 'F3 -Apply -AssignBillingRuleReader: the one write assigns the CUSTOMER TOP UNIT''s copy (never the root''s), and the read-back shows the holder' {
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $true
        $cand = $global:SetCustomerOwnerTeamsTest_CandidateId
        $copyId = $global:SetCustomerOwnerTeamsTest_CopyId
        $copyId.customer | Should -Not -Be $copyId.root
        $r = Invoke-Billing -Apply -Assign $cand
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $r.Output | Should -Match 'APPLIED and read back: no drift'
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 1 -Because ($global:SetCustomerOwnerTeamsTest_Writes -join '; ')
        $global:SetCustomerOwnerTeamsTest_Writes[0] | Should -Be ('Post https://example.crm.dynamics.com/api/data/v9.2/systemusers(' + $cand + ')/systemuserroles_association/$ref')
        $body = $global:SetCustomerOwnerTeamsTest_WriteBodies[0] | ConvertFrom-Json
        $body.'@odata.id' | Should -BeLike ('*roles(' + $copyId.customer + ')')
        $body.'@odata.id' | Should -Not -BeLike ('*roles(' + $copyId.root + ')*')
        @($global:SetCustomerOwnerTeamsTest_UserHolders | Where-Object { $_.Id -eq $cand })[0].Copy | Should -Be 'customer'
        # The read-back (after the write) shows the new holder of the customer's copy, and the run did not end in drift.
        $r.Output | Should -Match 'Holders     : 2 of the Customer One copy'
        $r.Output | Should -Match "Holder      : 'holder 2' \($cand\) homed in 'Customer One Litigation'"
        $r.Output | Should -Not -Match 'does not hold the Customer One copy after the assignment'
    }

    It 'F3 -Apply with the role missing: creates it in the root with isinherited 0, adds Read at Deep on prvReadsprk_Policy, then takes off the privilege the platform added on create' {
        $global:SetCustomerOwnerTeamsTest_RoleExists = $false
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $true
        $r = Invoke-Billing -Apply
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $w = @($global:SetCustomerOwnerTeamsTest_Writes)
        $w.Count | Should -Be 3 -Because ($w -join '; ')
        $roles = 'https://example.crm.dynamics.com/api/data/v9.2/roles'
        $rootCopy = $global:SetCustomerOwnerTeamsTest_CopyId.root
        $w[0] | Should -Be "Post $roles"
        $create = $global:SetCustomerOwnerTeamsTest_WriteBodies[0] | ConvertFrom-Json
        $create.isinherited | Should -Be 0
        $create.name | Should -Be 'Billing Rule Reader'
        $create.'businessunitid@odata.bind' | Should -Be "/businessunits($($global:SetCustomerOwnerTeamsTest_RootId))"
        $w[1] | Should -Be "Post $roles($rootCopy)/Microsoft.Dynamics.CRM.AddPrivilegesRole"
        $add = $global:SetCustomerOwnerTeamsTest_WriteBodies[1] | ConvertFrom-Json
        @($add.Privileges).Count | Should -Be 1
        @($add.Privileges)[0].PrivilegeName | Should -Be 'prvReadsprk_Policy'
        @($add.Privileges)[0].PrivilegeId | Should -Be $global:SetCustomerOwnerTeamsTest_PolicyReadId
        @($add.Privileges)[0].Depth | Should -Be 'Deep'
        # The strip: the outside privilege the platform added on create comes off, after the add.
        $w[2] | Should -Be "Post $roles($rootCopy)/Microsoft.Dynamics.CRM.RemovePrivilegeRole"
        ($global:SetCustomerOwnerTeamsTest_WriteBodies[2] | ConvertFrom-Json).Privilege.privilegeid | Should -Be $global:SetCustomerOwnerTeamsTest_PlatformPrivilegeId
        @($global:SetCustomerOwnerTeamsTest_Held | ForEach-Object Name) | Should -Be @('prvReadsprk_Policy')
        @($global:SetCustomerOwnerTeamsTest_Held)[0].Depth | Should -Be 'Deep'
    }

    It 'F3 -Apply -AssignBillingRuleReader: an outside privilege on the role comes off BEFORE the user assignment write' {
        $global:SetCustomerOwnerTeamsTest_Held = @(@{ Name = 'prvReadsprk_Policy'; Id = $global:SetCustomerOwnerTeamsTest_PolicyReadId; Depth = 'Deep' }, @{ Name = 'prvReadSdkMessage'; Id = $global:SetCustomerOwnerTeamsTest_PlatformPrivilegeId; Depth = 'Basic' })
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $true
        $cand = $global:SetCustomerOwnerTeamsTest_CandidateId
        $r = Invoke-Billing -Apply -Assign $cand
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $w = @($global:SetCustomerOwnerTeamsTest_Writes)
        $w.Count | Should -Be 2 -Because ($w -join '; ')
        $w[0] | Should -Match 'roles\([^)]+\)/Microsoft\.Dynamics\.CRM\.RemovePrivilegeRole$'
        ($global:SetCustomerOwnerTeamsTest_WriteBodies[0] | ConvertFrom-Json).Privilege.privilegeid | Should -Be $global:SetCustomerOwnerTeamsTest_PlatformPrivilegeId
        $w[1] | Should -Match 'systemusers\([^)]+\)/systemuserroles_association/\$ref$'
        @($global:SetCustomerOwnerTeamsTest_Held | ForEach-Object Name) | Should -Be @('prvReadsprk_Policy')
    }

    It 'K1 must fire: Secure Record placed under the customer top unit refuses in every mode, before any write' {
        $global:SetCustomerOwnerTeamsTest_SecureUnderCustomer = $true
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $true   # if the script wrote, it would succeed: the test must catch it
        $global:SetCustomerOwnerTeamsTest_AllowOrgPatch = $true
        foreach ($mode in @{}, @{ Verify = $true }, @{ Apply = $true }, @{ Apply = $true; Assign = $global:SetCustomerOwnerTeamsTest_CandidateId }) {
            $r = Invoke-Billing @mode
            $r.ExitCode | Should -Be 1 -Because ($mode.Keys -join ',')
            $r.Output | Should -Match 'business-unit tree is not the expected shape'
            $r.Output | Should -Match 'Secure Record must be a direct child of the root and outside every customer subtree \(D-123\)'
            $r.Output | Should -Not -MatchExactly 'ADDED at|CREATED role|ASSIGNED the|REMOVED from|ORG SETTING : writing'
        }
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It 'F3 -Apply with the role''s isinherited 1: exactly one PATCH, setting it to 0' {
        $global:SetCustomerOwnerTeamsTest_IsInherited = 1
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $true
        $r = Invoke-Billing -Apply
        $r.ExitCode | Should -Be 0 -Because $r.Output
        @($global:SetCustomerOwnerTeamsTest_Writes) | Should -Be @("Patch https://example.crm.dynamics.com/api/data/v9.2/roles($($global:SetCustomerOwnerTeamsTest_CopyId.root))")
        ($global:SetCustomerOwnerTeamsTest_WriteBodies[0] | ConvertFrom-Json).isinherited | Should -Be 0
    }

    It 'K2: the plan and -Apply agree on isinherited: the root OR any copy differs means the root is patched; neither differs means no step and no write' {
        # A copy differs (the root is right): the plan lists the step AND -Apply patches the root (copies follow it).
        $global:SetCustomerOwnerTeamsTest_CopyIsInherited = @{ customer = 1 }
        $dry = Invoke-Billing
        $dry.ExitCode | Should -Be 0 -Because $dry.Output
        $dry.Output | Should -Match "set isinherited to 0 \(Team privileges only\) on 'Billing Rule Reader'"
        $global:SetCustomerOwnerTeamsTest_AllowRoleWrites = $true
        $r = Invoke-Billing -Apply
        $r.ExitCode | Should -Be 0 -Because $r.Output
        @($global:SetCustomerOwnerTeamsTest_Writes) | Should -Be @("Patch https://example.crm.dynamics.com/api/data/v9.2/roles($($global:SetCustomerOwnerTeamsTest_CopyId.root))")
        # Neither differs: no plan step, no write.
        $global:SetCustomerOwnerTeamsTest_Writes.Clear()
        $global:SetCustomerOwnerTeamsTest_CopyIsInherited = @{}
        $ok = Invoke-Billing -Apply
        $ok.ExitCode | Should -Be 0 -Because $ok.Output
        $ok.Output | Should -Not -Match 'set isinherited to 0'
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0
    }

    It 'F2 (stale read) must fire: orgdborgsettings read again just before the write differs from the first read -> no PATCH, a failure that says so' {
        $off = '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>false</EnableOwnershipAcrossBusinessUnits></OrgSettings>'
        $changed = @(
            '<OrgSettings><MaxRecordsForExportToExcel>5</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>false</EnableOwnershipAcrossBusinessUnits></OrgSettings>'  # someone changed another element
            '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>'   # someone turned the setting on
        )
        foreach ($second in $changed) {
            $global:SetCustomerOwnerTeamsTest_Writes.Clear()
            $global:SetCustomerOwnerTeamsTest_OrgSettings = $off
            $global:SetCustomerOwnerTeamsTest_AllowOrgPatch = $true   # if the script wrote, it would succeed: the test must catch it
            $global:SetCustomerOwnerTeamsTest_OrgReadQueue.Enqueue($off)
            $global:SetCustomerOwnerTeamsTest_OrgReadQueue.Enqueue($second)
            $r = Invoke-Billing -Apply
            $r.ExitCode | Should -Be 1 -Because $r.Output
            $r.Output | Should -Match 'orgdborgsettings changed since this run first read it'
            $r.Output | Should -Match 'Nothing was written'
            $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 0 -Because $second
            $global:SetCustomerOwnerTeamsTest_OrgReadQueue.Clear()
        }
    }

    It 'F2 (stale read) must not fire: identical reads and the setting off -> one PATCH, built from a read made immediately before it, changing only EnableOwnershipAcrossBusinessUnits' {
        $global:SetCustomerOwnerTeamsTest_OrgSettings = '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>false</EnableOwnershipAcrossBusinessUnits></OrgSettings>'
        $global:SetCustomerOwnerTeamsTest_AllowOrgPatch = $true
        $r = Invoke-Billing -Apply
        $r.ExitCode | Should -Be 0 -Because $r.Output
        $global:SetCustomerOwnerTeamsTest_Writes.Count | Should -Be 1
        $events = @($global:SetCustomerOwnerTeamsTest_Events)
        $patchAt = [array]::FindIndex($events, [Predicate[string]] { param($e) $e -like 'Patch *organizations(*' })
        $patchAt | Should -BeGreaterThan 1
        $events[$patchAt - 1] | Should -Be 'Get organizations'   # the re-read is the call right before the write
        @($events[0..($patchAt - 1)] | Where-Object { $_ -eq 'Get organizations' }).Count | Should -Be 2   # the first read, then the fresh one
        $global:SetCustomerOwnerTeamsTest_OrgSettings | Should -Be '<OrgSettings><MaxRecordsForExportToExcel>100000</MaxRecordsForExportToExcel><EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>'
    }

    It 'must fire: a config with a team, isinherited 1, Global depth, a member list or the root as its unit is refused before any read' {
        $bad = @(
            @{ Name = 'team'; Change = { param($c) $c | Add-Member -NotePropertyName teamName -NotePropertyValue 'Billing Rule Readers' }; Message = 'with a teamName' }
            @{ Name = 'inherited'; Change = { param($c) $c.isinherited = 1 }; Message = "isinherited '1'" }
            @{ Name = 'global'; Change = { param($c) $c.depth = 'Global' }; Message = "at depth 'Global'" }
            @{ Name = 'members'; Change = { param($c) $c | Add-Member -NotePropertyName membership -NotePropertyValue 'members' }; Message = "names 'membership'" }
            @{ Name = 'root'; Change = { param($c) $c.businessUnit = 'root' }; Message = "names business unit 'root'" }
            @{ Name = 'nosetting'; Change = { param($c) $c.requiresOrgSetting = $null }; Message = 'requiresOrgSetting' }
        )
        foreach ($b in $bad) {
            $r = Invoke-Billing -ConfigPath (New-BadConfig $b.Name $b.Change)
            $r.ExitCode | Should -Be 1 -Because $b.Name
            $r.Output | Should -Match ([regex]::Escape($b.Message))
            $r.Output | Should -Not -Match 'Environment :'
        }
    }
}
