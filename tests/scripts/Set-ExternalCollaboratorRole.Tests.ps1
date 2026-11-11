# Pester 5.7.1 (6.x does not load the containers here). Run: Invoke-Pester tests/scripts/Set-ExternalCollaboratorRole.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (ubuntu-latest).
# unified-access-control-r2 task 191d: scripts/Set-ExternalCollaboratorRole.ps1 over a mocked Dataverse built from the
# checked-in config/external-collaborator-role.json. Nothing talks to Dataverse. The dry run and -Verify must never write
# (any write is recorded and asserted empty); -Apply runs against a mock that creates the unit and role, re-injects the
# platform's privileges on every AddPrivilegesRole, and removes on RemovePrivilegeRole, so the end state is checked as
# -Verify checks it.
# Must-fire: -Apply with -Verify throws; each -Verify gap is named (BU MISSING, BU PARENT, BU OWNS ROWS, DEFAULT TEAM ROLE,
#   WRITER PROFILE, STORED FILE PROFILE, ROLE HOLDER, PRIVILEGE MISSING, PRIVILEGE DEPTH, PRIVILEGE OUTSIDE THE FILE,
#   NEVER-TABLE PRIVILEGE, WRITE USER); -Apply refuses a unit outside the root before any write; a platform depth that is not
#   the lowest the privilege supports refuses.
# Must-not-fire: a clean environment verifies (exit 0); the dry run writes nothing and plans the unit and the role; -Apply on an
#   empty environment ends with no gap and strips the injected privileges.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Set-ExternalCollaboratorRole.ps1'
    $script:ConfigFile = Join-Path $script:RepoRoot 'config' 'external-collaborator-role.json'
    $script:Url = 'https://example.crm.dynamics.com'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    $config = Get-Content -LiteralPath $script:ConfigFile -Raw | ConvertFrom-Json
    $global:ExtCollabTest_Config = $config
    $global:ExtCollabTest_RootId = '00000000-0000-4000-8000-0000000000a1'
    $global:ExtCollabTest_UnitId = '00000000-0000-4000-8000-0000000000a2'
    $global:ExtCollabTest_TeamId = '00000000-0000-4000-8000-0000000000a3'
    $global:ExtCollabTest_RoleId = '00000000-0000-4000-8000-0000000000a4'
    $global:ExtCollabTest_OtherUnitId = '00000000-0000-4000-8000-0000000000a5'

    # Metadata: every table of the file, with a stable id per privilege. The schema-name casing is not modelled: the script
    # reads names from metadata, so the mock's names are what it compares.
    $global:ExtCollabTest_Ids = @{}
    function global:ExtCollabTest_Id([string]$Name) {
        if (-not $global:ExtCollabTest_Ids.ContainsKey($Name)) { $global:ExtCollabTest_Ids[$Name] = [guid]::NewGuid().ToString() }
        return $global:ExtCollabTest_Ids[$Name]
    }
    $global:ExtCollabTest_Tables = @{}
    foreach ($e in @($config.lineage) + @($config.never)) { $global:ExtCollabTest_Tables[$e.logicalName] = 'UserOwned' }
    foreach ($e in @($config.catalogs)) { $global:ExtCollabTest_Tables[$e.logicalName] = 'OrganizationOwned' }

    # The privileges the platform injects on create and on every AddPrivilegesRole (setup guide 5.4): SharePoint Create/Write
    # are outside the file and must come off.
    $global:ExtCollabTest_Injected = @('prvReadSharePointData', 'prvCreateSharePointData', 'prvWriteSharePointData', 'prvReadSharePointDocument')

    # What the file declares: name -> depth (lineage Basic, catalogs Global, platform as declared).
    function global:ExtCollabTest_PrivName([string]$Table, [string]$Type) { "prv$Type$Table" }
    $global:ExtCollabTest_Wanted = [ordered]@{}
    foreach ($e in @($config.lineage)) { foreach ($a in @($e.access)) { $global:ExtCollabTest_Wanted[(ExtCollabTest_PrivName $e.logicalName $a)] = 'Basic' } }
    foreach ($e in @($config.catalogs)) { foreach ($a in @($e.access)) { $global:ExtCollabTest_Wanted[(ExtCollabTest_PrivName $e.logicalName $a)] = 'Global' } }
    foreach ($e in @($config.platform)) { $global:ExtCollabTest_Wanted[$e.privilegeName] = $e.depth }

    function global:ExtCollabTest_Held([string]$Name, [string]$Depth) {
        [pscustomobject]@{ PrivilegeId = (ExtCollabTest_Id $Name); PrivilegeName = $Name; Depth = $Depth }
    }

    function Reset-World {
        $global:ExtCollabTest_UnitExists = $false
        $global:ExtCollabTest_UnitParent = $global:ExtCollabTest_RootId
        $global:ExtCollabTest_TeamRoles = @()
        $global:ExtCollabTest_Profiles = @()
        $global:ExtCollabTest_Owned = @{}
        $global:ExtCollabTest_RoleExists = $false
        $global:ExtCollabTest_Held = [System.Collections.Generic.List[object]]::new()
        $global:ExtCollabTest_UserHolders = @()
        $global:ExtCollabTest_Writes = [System.Collections.Generic.List[string]]::new()
        $global:ExtCollabTest_AllowWrites = $false
    }

    # The unit and role exactly as the file declares them.
    function Use-CleanSetup {
        $global:ExtCollabTest_UnitExists = $true
        $global:ExtCollabTest_RoleExists = $true
        foreach ($k in $global:ExtCollabTest_Wanted.Keys) { $global:ExtCollabTest_Held.Add((ExtCollabTest_Held $k $global:ExtCollabTest_Wanted[$k])) }
    }

    function Invoke-Script([hashtable]$Arguments) {
        $output = & $script:Script @Arguments 6>&1 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    function Invoke-Verify { Invoke-Script @{ EnvironmentUrl = $script:Url; Verify = $true } }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'ExtCollabTest_*' -ErrorAction SilentlyContinue
    foreach ($f in 'ExtCollabTest_Id', 'ExtCollabTest_PrivName', 'ExtCollabTest_Held') { Remove-Item -Path "Function:\$f" -ErrorAction SilentlyContinue }
}

Describe 'Set-ExternalCollaboratorRole.ps1 (mocked Dataverse)' {
    BeforeEach {
        Reset-World
        $global:LASTEXITCODE = 0
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            $u = [string]$Uri
            $g = 'ExtCollabTest_'
            $root = $global:ExtCollabTest_RootId; $unit = $global:ExtCollabTest_UnitId; $roleId = $global:ExtCollabTest_RoleId
            if ($Method -ne 'Get') {
                $global:ExtCollabTest_Writes.Add("$Method $u")
                if (-not $global:ExtCollabTest_AllowWrites) { throw "Test: the script wrote ($Method $u)." }
                $body = if ($Body) { [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json } else { $null }
                if ($u -match '/businessunits$') { $global:ExtCollabTest_UnitExists = $true; return $null }
                if ($u -match '/roles$') {
                    $global:ExtCollabTest_RoleExists = $true
                    foreach ($n in $global:ExtCollabTest_Injected) { $global:ExtCollabTest_Held.Add((ExtCollabTest_Held $n 'Global')) }
                    return $null
                }
                if ($u -match 'AddPrivilegesRole$') {
                    foreach ($p in @($body.Privileges)) {
                        $existing = @($global:ExtCollabTest_Held | Where-Object { $_.PrivilegeId -eq $p.PrivilegeId })
                        foreach ($x in $existing) { [void]$global:ExtCollabTest_Held.Remove($x) }
                        $global:ExtCollabTest_Held.Add([pscustomobject]@{ PrivilegeId = $p.PrivilegeId; PrivilegeName = $p.PrivilegeName; Depth = $p.Depth })
                    }
                    foreach ($n in $global:ExtCollabTest_Injected) {
                        if (-not @($global:ExtCollabTest_Held | Where-Object { $_.PrivilegeName -eq $n })) { $global:ExtCollabTest_Held.Add((ExtCollabTest_Held $n 'Global')) }
                    }
                    return $null
                }
                if ($u -match 'RemovePrivilegeRole$') {
                    foreach ($x in @($global:ExtCollabTest_Held | Where-Object { $_.PrivilegeId -eq $body.Privilege.privilegeid })) { [void]$global:ExtCollabTest_Held.Remove($x) }
                    return $null
                }
                return $null
            }
            if ($u -match "EntityDefinitions\?.*LogicalName eq '([^']+)'") {
                $t = $Matches[1]
                if (-not $global:ExtCollabTest_Tables.ContainsKey($t) -or $t -eq 'sprk_affinity') { return @{ value = @() } }
                return @{ value = @([pscustomobject]@{ LogicalName = $t; EntitySetName = "${t}set"; OwnershipType = $global:ExtCollabTest_Tables[$t]; PrimaryIdAttribute = "${t}id" }) }
            }
            if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/Privileges") {
                $t = $Matches[1]
                $org = $global:ExtCollabTest_Tables[$t] -eq 'OrganizationOwned'
                return @{ value = @(foreach ($type in 'Create', 'Read', 'Write', 'Delete', 'Append', 'AppendTo', 'Assign', 'Share') {
                            $n = ExtCollabTest_PrivName $t $type
                            [pscustomobject]@{ PrivilegeType = $type; Name = $n; PrivilegeId = (ExtCollabTest_Id $n); CanBeBasic = -not $org; CanBeLocal = -not $org; CanBeDeep = -not $org; CanBeGlobal = $true }
                        })
                }
            }
            if ($u -match "privileges\?.*name eq 'prvWriteUser'") { return @{ value = @([pscustomobject]@{ privilegeid = (ExtCollabTest_Id 'prvWriteUser'); name = 'prvWriteUser' }) } }
            if ($u -match 'privileges\?.*In\(PropertyName') {
                return @{ value = @(foreach ($e in @($global:ExtCollabTest_Config.platform)) {
                            $d = $e.depth
                            [pscustomobject]@{ name = $e.privilegeName; privilegeid = (ExtCollabTest_Id $e.privilegeName); canbebasic = ($d -eq 'Basic'); canbelocal = ($d -in 'Basic', 'Local'); canbedeep = ($d -ne 'Global'); canbeglobal = $true }
                        })
                }
            }
            if ($u -match '_parentbusinessunitid_value eq null') { return @{ value = @([pscustomobject]@{ businessunitid = $root; name = 'Root' }) } }
            if ($u -match '/businessunits\?') {
                if (-not $global:ExtCollabTest_UnitExists) { return @{ value = @() } }
                return @{ value = @([pscustomobject]@{ businessunitid = $unit; name = 'External Collaborators'; _parentbusinessunitid_value = $global:ExtCollabTest_UnitParent; sprk_containerid = $null }) }
            }
            if ($u -match '/teams\?') { return @{ value = @([pscustomobject]@{ teamid = $global:ExtCollabTest_TeamId; name = 'External Collaborators'; isdefault = $true }) } }
            if ($u -match '/teams\([^)]+\)/teamroles_association') { return @{ value = @($global:ExtCollabTest_TeamRoles) } }
            if ($u -match '/teams\([^)]+\)/teamprofiles_association') {
                return @{ value = @($global:ExtCollabTest_Profiles | ForEach-Object { [pscustomobject]@{ fieldsecurityprofileid = $_.Id; name = $_.Name } }) }
            }
            if ($u -match "fieldpermissions\?.*_fieldsecurityprofileid_value eq ([0-9a-f-]+)") {
                $id = $Matches[1]
                return @{ value = @(@($global:ExtCollabTest_Profiles | Where-Object { $_.Id -eq $id }) | ForEach-Object { $_.Permissions }) }
            }
            if ($u -match '/([a-z_]+)set\?\$select=[a-z_]+id&\$filter=_owningbusinessunit_value') {
                $n = [int]$global:ExtCollabTest_Owned[$Matches[1]]
                return @{ value = @(); '@odata.count' = $n }
            }
            if ($u -match '/roles\?') {
                if (-not $global:ExtCollabTest_RoleExists) { return @{ value = @() } }
                return @{ value = @([pscustomobject]@{ roleid = $roleId; name = 'Spaarke External Collaborator'; isinherited = 1; _businessunitid_value = $unit; _parentrootroleid_value = $roleId }) }
            }
            if ($u -match '/roles\([^)]+\)/systemuserroles_association') { return @{ value = @($global:ExtCollabTest_UserHolders) } }
            if ($u -match '/roles\([^)]+\)/teamroles_association') { return @{ value = @() } }
            if ($u -match 'RetrieveRolePrivilegesRole') { return @{ RolePrivileges = @($global:ExtCollabTest_Held | ForEach-Object { $_ }) } }
            throw "Test: unexpected read $u"
        }
    }

    It 'throws when -Apply and -Verify are both passed' {
        { & $script:Script -EnvironmentUrl $script:Url -Apply -Verify } | Should -Throw '*separate modes*'
    }

    It 'dry run on an empty environment plans the unit and the role and writes nothing' {
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url }
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match "create the business unit 'External Collaborators' as a direct child of the root"
        $r.Output | Should -Match "create the role 'Spaarke External Collaborator' in 'External Collaborators'"
        $r.Output | Should -Match 'add prvReadsprk_matter at Basic'
        $r.Output | Should -Match 'add prvReadUser at Local'
        $r.Output | Should -Match 'DRY RUN: nothing was written'
        $global:ExtCollabTest_Writes.Count | Should -Be 0
    }

    It 'verifies a clean environment (exit 0) and writes nothing' {
        Use-CleanSetup
        $r = Invoke-Verify
        $r.Output | Should -Match 'VERIFY PASS'
        $r.ExitCode | Should -Be 0
        $global:ExtCollabTest_Writes.Count | Should -Be 0
    }

    It 'names BU MISSING and ROLE MISSING' {
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'BU MISSING'
        $r.Output | Should -Match 'ROLE MISSING'
    }

    It 'names a missing privilege' {
        Use-CleanSetup
        foreach ($x in @($global:ExtCollabTest_Held | Where-Object PrivilegeName -eq 'prvReadsprk_memo')) { [void]$global:ExtCollabTest_Held.Remove($x) }
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE MISSING: prvReadsprk_memo at Basic'
    }

    It 'names a privilege at the wrong depth' {
        Use-CleanSetup
        foreach ($x in @($global:ExtCollabTest_Held | Where-Object PrivilegeName -eq 'prvReadsprk_matter')) { $x.Depth = 'Deep' }
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE DEPTH: prvReadsprk_matter at Deep, expected Basic'
    }

    It 'names an extra privilege' {
        Use-CleanSetup
        $global:ExtCollabTest_Held.Add((ExtCollabTest_Held 'prvSharesprk_matter' 'Basic'))
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'PRIVILEGE OUTSIDE THE FILE: .* holds prvSharesprk_matter at Basic'
    }

    It 'names a never-table privilege' {
        Use-CleanSetup
        $global:ExtCollabTest_Held.Add((ExtCollabTest_Held 'prvReadsprk_invoice' 'Basic'))
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'NEVER-TABLE PRIVILEGE: .* holds prvReadsprk_invoice \(sprk_invoice\)'
    }

    It 'names prvWriteUser' {
        Use-CleanSetup
        $global:ExtCollabTest_Held.Add((ExtCollabTest_Held 'prvWriteUser' 'Local'))
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'WRITE USER'
    }

    It 'names rows owned in the unit' {
        Use-CleanSetup
        $global:ExtCollabTest_Owned['sprk_matter'] = 3
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'BU OWNS ROWS: 3 sprk_matter row'
    }

    It 'names a role on the default team' {
        Use-CleanSetup
        $global:ExtCollabTest_TeamRoles = @([pscustomobject]@{ roleid = [guid]::NewGuid().ToString(); name = 'Spaarke Basic User' })
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "DEFAULT TEAM ROLE: the default team holds role 'Spaarke Basic User'"
    }

    It 'names a writer profile on the default team' {
        Use-CleanSetup
        $global:ExtCollabTest_Profiles = @([pscustomobject]@{ Id = [guid]::NewGuid().ToString(); Name = 'Spaarke BFF-Managed Field Writers'
                Permissions = @([pscustomobject]@{ entityname = 'sprk_matter'; attributelogicalname = 'sprk_issecure'; canread = 4; cancreate = 4; canupdate = 4 }) })
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "WRITER PROFILE: the default team holds 'Spaarke BFF-Managed Field Writers'"
    }

    It 'names the Stored File reader profile on the default team' {
        Use-CleanSetup
        $global:ExtCollabTest_Profiles = @([pscustomobject]@{ Id = [guid]::NewGuid().ToString(); Name = 'Spaarke BFF-Managed Field Readers'
                Permissions = @([pscustomobject]@{ entityname = 'sprk_storedfile'; attributelogicalname = 'sprk_sha256'; canread = 4; cancreate = 0; canupdate = 0 }) })
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "STORED FILE PROFILE: the default team holds 'Spaarke BFF-Managed Field Readers'"
        $r.Output | Should -Not -Match 'WRITER PROFILE'
    }

    It 'names a holder homed outside the unit' {
        Use-CleanSetup
        $global:ExtCollabTest_UserHolders = @([pscustomobject]@{ systemuserid = [guid]::NewGuid().ToString(); _businessunitid_value = $global:ExtCollabTest_OtherUnitId })
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'ROLE HOLDER: user .* is homed in business unit'
    }

    It 'does not flag a holder homed in the unit' {
        Use-CleanSetup
        $global:ExtCollabTest_UserHolders = @([pscustomobject]@{ systemuserid = [guid]::NewGuid().ToString(); _businessunitid_value = $global:ExtCollabTest_UnitId })
        $r = Invoke-Verify
        $r.ExitCode | Should -Be 0
    }

    It 'names a unit outside the root, and -Apply refuses it before any write' {
        Use-CleanSetup
        $global:ExtCollabTest_UnitParent = $global:ExtCollabTest_OtherUnitId
        $v = Invoke-Verify
        $v.ExitCode | Should -Be 1
        $v.Output | Should -Match 'BU PARENT'
        $global:ExtCollabTest_AllowWrites = $true
        $a = Invoke-Script @{ EnvironmentUrl = $script:Url; Apply = $true }
        $a.ExitCode | Should -Be 1
        $a.Output | Should -Match 'Refused \(owner decision; nothing was written\)'
        $global:ExtCollabTest_Writes.Count | Should -Be 0
    }

    It 'refuses a platform privilege declared above the lowest depth it supports' {
        $c = Get-Content -LiteralPath $script:ConfigFile -Raw | ConvertFrom-Json
        ($c.platform | Where-Object privilegeName -eq 'prvReadUserQuery').depth = 'Global'
        $path = Join-Path $TestDrive 'bad.json'
        $c | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding utf8
        # The mock answers each platform privilege's lowest depth from the checked-in file (Basic for prvReadUserQuery).
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; Verify = $true; ConfigPath = $path }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match "prvReadUserQuery' is declared at Global; the lowest depth it supports is Basic"
    }

    It '-Apply on an empty environment creates the unit and role, strips the injected privileges and ends with no gap' {
        $global:ExtCollabTest_AllowWrites = $true
        $r = Invoke-Script @{ EnvironmentUrl = $script:Url; Apply = $true }
        $r.Output | Should -Match 'APPLY DONE:'
        $r.ExitCode | Should -Be 0
        ($global:ExtCollabTest_Writes | Where-Object { $_ -match 'Post .*/businessunits$' }).Count | Should -Be 1
        ($global:ExtCollabTest_Writes | Where-Object { $_ -match 'Post .*/roles$' }).Count | Should -Be 1
        @($global:ExtCollabTest_Held | Where-Object PrivilegeName -in 'prvCreateSharePointData', 'prvWriteSharePointData').Count | Should -Be 0
        @($global:ExtCollabTest_Held).Count | Should -Be $global:ExtCollabTest_Wanted.Count
        # Never a user, team, share or profile write.
        @($global:ExtCollabTest_Writes | Where-Object { $_ -match 'systemuser|teams|fieldsecurityprofile|GrantAccess|ModifyAccess' }).Count | Should -Be 0
    }
}
