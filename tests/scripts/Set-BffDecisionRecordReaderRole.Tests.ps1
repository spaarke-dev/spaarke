# Pester 5.7.1 (6.x does not load the containers here). Run: Invoke-Pester tests/scripts/Set-BffDecisionRecordReaderRole.Tests.ps1
# ebilling-r1 DEF-059: scripts/ebilling/Set-BffDecisionRecordReaderRole.ps1 over a mocked Dataverse. Pins: -Verify exits 0 only when
# every BFF application user present holds the role copy of its own business unit, the role holds exactly prvReadsprk_DecisionRecord
# at Global, and nobody else holds it; -Apply assigns only what is missing (idempotent) and refuses a drifted role.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Set-BffDecisionRecordReaderRole.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    $global:DrrTest_RootBu = '00000000-0000-4000-8000-000000000001'
    $global:DrrTest_CustomerBu = '00000000-0000-4000-8000-000000000002'
    $global:DrrTest_RootRole = '00000000-0000-4000-8000-0000000000a1'
    $global:DrrTest_CustomerCopy = '00000000-0000-4000-8000-0000000000a2'
    $global:DrrTest_AppA = '00000000-0000-4000-8000-0000000000b1'   # the BFF app registration (customer unit)
    $global:DrrTest_AppB = '00000000-0000-4000-8000-0000000000b2'   # the BFF managed identity (customer unit)
    $global:DrrTest_UserA = '00000000-0000-4000-8000-0000000000c1'
    $global:DrrTest_UserB = '00000000-0000-4000-8000-0000000000c2'

    function Invoke-Script([hashtable]$Arguments) {
        $output = & $script:Script @Arguments 6>&1 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    function Base([switch]$Apply, [switch]$Verify) {
        $a = @{ EnvironmentUrl = $script:Url; BffApplicationIds = @("$global:DrrTest_AppA,$global:DrrTest_AppB") }
        if ($Apply) { $a.Apply = $true }
        if ($Verify) { $a.Verify = $true }
        $a
    }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'DrrTest_*' -ErrorAction SilentlyContinue
}

Describe 'Set-BffDecisionRecordReaderRole.ps1 (mocked Dataverse)' {
    BeforeEach {
        $global:LASTEXITCODE = 0
        $global:DrrTest_RoleExists = $true
        $global:DrrTest_Privileges = @([pscustomobject]@{ PrivilegeName = 'prvReadsprk_DecisionRecord'; Depth = 'Global' })
        $global:DrrTest_PresentApps = @($global:DrrTest_AppA, $global:DrrTest_AppB)
        $global:DrrTest_Assigned = @{ $global:DrrTest_UserA = $true; $global:DrrTest_UserB = $true }
        $global:DrrTest_OtherHolders = @()
        $global:DrrTest_Teams = @()
        $global:DrrTest_Posts = [System.Collections.Generic.List[string]]::new()
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            $u = [string]$Uri
            if ($Method -eq 'Post') {
                if ($u -match 'systemusers\(([^)]+)\)/systemuserroles_association/\$ref$') {
                    $global:DrrTest_Posts.Add("$($Matches[1]) <- " + ([Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json).'@odata.id')
                    $global:DrrTest_Assigned[$Matches[1]] = $true
                    return $null
                }
                throw "Test: unexpected write ($Method $u)."
            }
            if ($Method -ne 'Get') { throw "Test: unexpected write ($Method $u)." }
            if ($u -match '/businessunits\?') { return @{ value = @([pscustomobject]@{ businessunitid = $global:DrrTest_RootBu; name = 'Spaarke' }) } }
            if ($u -match '/roles\?.*name eq') {
                return @{ value = @(if ($global:DrrTest_RoleExists) { [pscustomobject]@{ roleid = $global:DrrTest_RootRole; name = 'Spaarke Decision Record Reader' } }) }
            }
            if ($u -match 'RetrieveRolePrivilegesRole') { return @{ RolePrivileges = $global:DrrTest_Privileges } }
            if ($u -match '/roles\?.*_parentrootroleid_value') {
                return @{ value = @(
                        [pscustomobject]@{ roleid = $global:DrrTest_RootRole; _businessunitid_value = $global:DrrTest_RootBu }
                        [pscustomobject]@{ roleid = $global:DrrTest_CustomerCopy; _businessunitid_value = $global:DrrTest_CustomerBu }
                    )
                }
            }
            if ($u -match '/systemusers\?\$filter=applicationid eq ([0-9a-f-]+)') {
                $app = $Matches[1]
                if ($global:DrrTest_PresentApps -notcontains $app) { return @{ value = @() } }
                $id = if ($app -eq $global:DrrTest_AppA) { $global:DrrTest_UserA } else { $global:DrrTest_UserB }
                return @{ value = @([pscustomobject]@{ systemuserid = $id; fullname = "bff $app"; isdisabled = $false; _businessunitid_value = $global:DrrTest_CustomerBu }) }
            }
            if ($u -match '/systemusers\(([^)]+)\)/systemuserroles_association\?') {
                return @{ value = @(if ($global:DrrTest_Assigned[$Matches[1]]) { [pscustomobject]@{ roleid = $global:DrrTest_CustomerCopy } }) }
            }
            if ($u -match '/roles\(([^)]+)\)/systemuserroles_association\?') {
                if ($Matches[1] -ne $global:DrrTest_CustomerCopy) { return @{ value = @() } }
                $rows = @(foreach ($pair in @(@($global:DrrTest_UserA, $global:DrrTest_AppA), @($global:DrrTest_UserB, $global:DrrTest_AppB))) {
                        if ($global:DrrTest_Assigned[$pair[0]]) { [pscustomobject]@{ systemuserid = $pair[0]; fullname = "bff $($pair[1])"; applicationid = $pair[1] } }
                    })
                return @{ value = @($rows + $global:DrrTest_OtherHolders) }
            }
            if ($u -match '/roles\(([^)]+)\)/teamroles_association\?') {
                return @{ value = @(if ($Matches[1] -eq $global:DrrTest_CustomerCopy) { $global:DrrTest_Teams }) }
            }
            throw "Test: unexpected read $u"
        }
    }

    It 'must-not-fire: -Verify passes when both BFF users hold the copy of their unit, the role holds only the pinned read, and nobody else holds it' {
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'VERIFY PASS'
        $global:DrrTest_Posts.Count | Should -Be 0
    }

    It 'must-fire: -Verify fails when a BFF user does not hold the role' {
        $global:DrrTest_Assigned = @{ $global:DrrTest_UserA = $true }
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match ([regex]::Escape("($global:DrrTest_AppB) does not hold the role"))
        $global:DrrTest_Posts.Count | Should -Be 0
    }

    It 'must-fire: -Verify fails on an added privilege, on another depth, and on another table' {
        foreach ($set in @(
                @([pscustomobject]@{ PrivilegeName = 'prvReadsprk_DecisionRecord'; Depth = 'Global' }, [pscustomobject]@{ PrivilegeName = 'prvReadUser'; Depth = 'Basic' }),
                @([pscustomobject]@{ PrivilegeName = 'prvReadsprk_DecisionRecord'; Depth = 'Deep' }),
                @([pscustomobject]@{ PrivilegeName = 'prvReadsprk_Signal'; Depth = 'Global' }))) {
            $global:DrrTest_Privileges = $set
            $r = Invoke-Script (Base -Verify)
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'outside its one-privilege set|does not hold prvReadsprk_DecisionRecord at Global'
        }
    }

    It 'must-fire: -Verify fails when a person, another application user or a team holds a copy (and removes nothing)' {
        $global:DrrTest_OtherHolders = @([pscustomobject]@{ systemuserid = [guid]::NewGuid(); fullname = 'Some Person'; applicationid = $null })
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'a PERSON'

        $global:DrrTest_OtherHolders = @([pscustomobject]@{ systemuserid = [guid]::NewGuid(); fullname = 'writer'; applicationid = '00000000-0000-4000-8000-0000000000d1' })
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'not in -BffApplicationIds'

        $global:DrrTest_OtherHolders = @()
        $global:DrrTest_Teams = @([pscustomobject]@{ teamid = [guid]::NewGuid(); name = 'Some Team' })
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'held by team Some Team'
        $global:DrrTest_Posts.Count | Should -Be 0
    }

    It 'must-fire: -Verify fails when the role is missing, and when no BFF user is in the environment' {
        $global:DrrTest_RoleExists = $false
        (Invoke-Script (Base -Verify)).ExitCode | Should -Be 1
        $global:DrrTest_RoleExists = $true
        $global:DrrTest_PresentApps = @()
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'none of -BffApplicationIds'
    }

    It 'a BFF user absent from the environment is skipped; the one present is enough' {
        $global:DrrTest_PresentApps = @($global:DrrTest_AppA)
        $global:DrrTest_Assigned = @{ $global:DrrTest_UserA = $true }
        $r = Invoke-Script (Base -Verify)
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'not in this environment \(skipped\)'
    }

    It '-Apply assigns only the missing user, to the copy of its own business unit, then holds; a second -Apply writes nothing (idempotent)' {
        $global:DrrTest_Assigned = @{ $global:DrrTest_UserA = $true }
        $r = Invoke-Script (Base -Apply)
        $r.ExitCode | Should -Be 0
        $global:DrrTest_Posts.Count | Should -Be 1
        $global:DrrTest_Posts[0] | Should -Be "$global:DrrTest_UserB <- $script:Url/api/data/v9.2/roles($global:DrrTest_CustomerCopy)"

        $global:DrrTest_Posts.Clear()
        $r = Invoke-Script (Base -Apply)
        $r.ExitCode | Should -Be 0
        $global:DrrTest_Posts.Count | Should -Be 0
    }

    It 'platform defaults are drift by default; -AllowPlatformDefaults lists them without failing, and never excuses another privilege' {
        $global:DrrTest_Privileges = @(
            [pscustomobject]@{ PrivilegeName = 'prvReadsprk_DecisionRecord'; Depth = 'Global' }
            [pscustomobject]@{ PrivilegeName = 'prvReadPluginAssembly'; Depth = 'Global' })
        (Invoke-Script (Base -Verify)).ExitCode | Should -Be 1
        $a = Base -Verify; $a.AllowPlatformDefaults = $true
        $r = Invoke-Script $a
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'platform default prvReadPluginAssembly'

        $global:DrrTest_Privileges += [pscustomobject]@{ PrivilegeName = 'prvCreatesprk_DecisionRecord'; Depth = 'Global' }
        (Invoke-Script $a).ExitCode | Should -Be 1
    }

    It '-Apply refuses a drifted role and assigns nothing' {
        $global:DrrTest_Assigned = @{}
        $global:DrrTest_Privileges = @([pscustomobject]@{ PrivilegeName = 'prvReadsprk_DecisionRecord'; Depth = 'Global' }, [pscustomobject]@{ PrivilegeName = 'prvCreatesprk_DecisionRecord'; Depth = 'Global' })
        $r = Invoke-Script (Base -Apply)
        $r.ExitCode | Should -Be 2
        $r.Output | Should -Match 'REFUSED'
        $global:DrrTest_Posts.Count | Should -Be 0
    }

    It 'the dry run writes nothing; it exits 0 when -Apply can fix the drift and 1 when it cannot' {
        $global:DrrTest_Assigned = @{}
        $r = Invoke-Script (Base)
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'PLAN'
        $global:DrrTest_Posts.Count | Should -Be 0

        $global:DrrTest_Teams = @([pscustomobject]@{ teamid = [guid]::NewGuid(); name = 'Some Team' })
        (Invoke-Script (Base)).ExitCode | Should -Be 1
        $global:DrrTest_Posts.Count | Should -Be 0
    }
}
