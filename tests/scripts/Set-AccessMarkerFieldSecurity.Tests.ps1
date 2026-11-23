# Pester 5.7.1 (6.x does not load the containers here). Run: Invoke-Pester tests/scripts/Set-AccessMarkerFieldSecurity.Tests.ps1
# unified-access-control-r2 task 191f: scripts/Set-AccessMarkerFieldSecurity.ps1 -> scripts/Set-DocumentPointerFieldSecurity.ps1
# -Target AccessMarkerColumns over a mocked Dataverse. Pins: a dry run writes nothing and lists every (table, column) pair
# and the writer profile's planned members; -Verify names each gap (an unsecured column, a default team without the
# reader, the External Collaborators team holding the writer, a BFF user without the writer); -Apply refuses, writing
# nothing, while a default team lacks the reader (or the External unit is missing); -Apply adds the writer profile and its
# members BEFORE it secures any column and never adds the External team; sprk_isexternal is never a target column.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:EntryPoint = Join-Path $script:RepoRoot 'scripts' 'Set-AccessMarkerFieldSecurity.ps1'
    $script:Engine = Join-Path $script:RepoRoot 'scripts' 'Set-DocumentPointerFieldSecurity.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    $script:Pairs = @(
        'sprk_matter.sprk_accesspermission', 'sprk_project.sprk_accesspermission', 'sprk_workassignment.sprk_accesspermission',
        'sprk_document.sprk_accesspermission', 'sprk_communication.sprk_accesspermission', 'sprk_event.sprk_accesspermission',
        'sprk_todo.sprk_accesspermission', 'sprk_agreement.sprk_accesspermission',
        'sprk_communication.sprk_isinternalonly', 'sprk_communicationthread.sprk_privacystate')
    $script:Tables = @($script:Pairs | ForEach-Object { ($_ -split '\.')[0] } | Select-Object -Unique)
    $script:BffAppId = 'aaaaaaaa-0000-4000-8000-000000000001'

    # The world the mocked Web API answers from (global: the mock bodies run inside the script under test).
    function Reset-World([ValidateSet('Before', 'Locked')][string]$State) {
        $ids = @{
            Root = '00000000-0000-4000-8000-000000000001'; Secure = '00000000-0000-4000-8000-000000000002'; External = '00000000-0000-4000-8000-000000000003'
            RootTeam = '00000000-0000-4000-8000-000000000011'; SecureTeam = '00000000-0000-4000-8000-000000000012'; ExternalTeam = '00000000-0000-4000-8000-000000000013'
            BffUser = '00000000-0000-4000-8000-000000000021'; Reader = '00000000-0000-4000-8000-000000000031'
            Writer = '60b9344e-f253-49cd-af1a-4be2cf52c3a1'; Solution = '00000000-0000-4000-8000-000000000041'
        }
        $locked = $State -eq 'Locked'
        $world = @{
            Ids = $ids; Pairs = $script:Pairs; BffAppId = $script:BffAppId
            ExternalUnitExists = $true; SysAdmin = $true
            Carrying = @($script:Pairs | Where-Object { $_ -like '*.sprk_accesspermission' } | ForEach-Object { ($_ -split '\.')[0] })
            ReaderTeams = [System.Collections.Generic.List[string]]@($ids.RootTeam, $ids.SecureTeam, $ids.ExternalTeam)
            WriterExists = $locked
            WriterTeams = [System.Collections.Generic.List[string]]::new()
            WriterUsers = [System.Collections.Generic.List[string]]::new()
            Secured = @{}; Perms = @{}
            AllowWrites = $false; DropRefs = $false; FailGrantFor = $null
            BooleanColumns = @('sprk_communication.sprk_isinternalonly'); BooleanRetries = 0
            StrayGrants = @{}
            Writes = [System.Collections.Generic.List[string]]::new()
            PublishXml = $null
        }
        if ($locked) { $world.WriterTeams.Add($ids.RootTeam); $world.WriterTeams.Add($ids.SecureTeam); $world.WriterUsers.Add($ids.BffUser) }
        foreach ($p in $script:Pairs) {
            $world.Secured[$p] = $locked
            if ($locked) {
                $world.Perms["$($ids.Reader)|$p"] = @{ canread = 4; cancreate = 0; canupdate = 0 }
                $world.Perms["$($ids.Writer)|$p"] = @{ canread = 4; cancreate = 4; canupdate = 4 }
            }
        }
        $global:AccessMarkerTestWorld = $world
    }

    function Invoke-Script([string]$Path, [hashtable]$Extra = @{}) {
        $arguments = @{ EnvironmentUrl = $script:Url; BffApplicationIds = @($script:BffAppId) }
        if ((Split-Path $Path -Leaf) -eq 'Set-DocumentPointerFieldSecurity.ps1') { $arguments.Target = 'AccessMarkerColumns' }
        if (-not $Extra.ContainsKey('ExternalCollaboratorConfigPath')) { $arguments.ExternalBusinessUnitName = 'External Collaborators' }
        foreach ($k in $Extra.Keys) { $arguments[$k] = $Extra[$k] }
        $global:LASTEXITCODE = 0
        try {
            $output = & $Path @arguments 6>&1 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Error = $null }
        } catch {
            [pscustomobject]@{ ExitCode = -1; Output = ''; Error = $_.Exception.Message }
        }
    }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'AccessMarkerTestWorld' -ErrorAction SilentlyContinue
}

Describe 'Set-AccessMarkerFieldSecurity.ps1 (mocked Dataverse)' {
    BeforeEach {
        Reset-World 'Before'
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            $w = $global:AccessMarkerTestWorld
            $ids = $w.Ids
            $u = [uri]::UnescapeDataString([string]$Uri)
            $teamRow = {
                param($id)
                $name = switch ($id) { $ids.RootTeam { 'Spaarke' } $ids.SecureTeam { 'Secure Record' } $ids.ExternalTeam { 'External Collaborators' } }
                [pscustomobject]@{ teamid = $id; name = $name; isdefault = $true }
            }

            if ($Method -ne 'Get') {
                if (-not $w.AllowWrites) { throw "Test: the script wrote ($Method $u)." }
                $path = $u -replace '^https://[^/]+/api/data/v9\.2/', ''
                $w.Writes.Add("$Method $path")
                $b = if ($Body) { $Body | ConvertFrom-Json -AsHashtable } else { @{} }
                if ($Method -eq 'Post' -and $path -eq 'fieldsecurityprofiles') { $w.WriterExists = $true; return }
                if ($path -match 'systemuserprofiles_association/\$ref$') { if (-not $w.DropRefs) { $w.WriterUsers.Add(($b['@odata.id'] -replace '.*\(([^)]+)\)$', '$1')) }; return }
                if ($path -match 'teamprofiles_association/\$ref$') { if (-not $w.DropRefs) { $w.WriterTeams.Add(($b['@odata.id'] -replace '.*\(([^)]+)\)$', '$1')) }; return }
                if ($Method -eq 'Put' -and $path -match "EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)") {
                    $key = "$($Matches[1]).$($Matches[2])"
                    # A Boolean definition PUT without its OptionSet is refused (the shape Set-SecureFlagFieldSecurity retries).
                    if ($key -in $w.BooleanColumns -and -not $b.ContainsKey('OptionSet')) { throw "Test: $key PUT refused without its OptionSet" }
                    if ($key -in $w.BooleanColumns) { $w.BooleanRetries++ }
                    $w.Secured[$key] = [bool]$b.IsSecured; return
                }
                if ($path -eq 'fieldpermissions') {
                    $profileId = $b['fieldsecurityprofileid@odata.bind'] -replace '.*\(([^)]+)\)$', '$1'
                    if ($w.FailGrantFor -eq "$profileId|$($b.entityname).$($b.attributelogicalname)") { throw 'Test: the grant failed (a permanent refusal, not the propagation code)' }
                    $w.Perms["$profileId|$($b.entityname).$($b.attributelogicalname)"] = @{ canread = $b.canread; cancreate = $b.cancreate; canupdate = $b.canupdate }
                    return
                }
                if ($path -eq 'PublishXml') { $w.PublishXml = $b.ParameterXml; return }
                throw "Test: unexpected write ($Method $path)."
            }

            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ name = 'test' }) } }
            if ($u -match "/fieldsecurityprofiles\?.*name eq '([^']+)'") {
                $name = $Matches[1]
                if ($name -eq 'Spaarke BFF-Managed Field Readers') {
                    return @{ value = @([pscustomobject]@{ fieldsecurityprofileid = $ids.Reader; name = $name
                                teamprofiles_association = @($w.ReaderTeams | ForEach-Object { & $teamRow $_ }); systemuserprofiles_association = @() }) }
                }
                if ($name -eq 'Spaarke Access Marker Writers' -and $w.WriterExists) {
                    return @{ value = @([pscustomobject]@{ fieldsecurityprofileid = $ids.Writer; name = $name
                                teamprofiles_association = @($w.WriterTeams | ForEach-Object { & $teamRow $_ })
                                systemuserprofiles_association = @($w.WriterUsers | ForEach-Object { [pscustomobject]@{ systemuserid = $_; fullname = 'BFF'; applicationid = $w.BffAppId } }) }) }
                }
                return @{ value = @() }
            }
            if ($u -match '/solutions\?') { return @{ value = @([pscustomobject]@{ solutionid = $ids.Solution }) } }
            if ($u -match '/solutioncomponents\?') {
                $rows = @([pscustomobject]@{ objectid = $ids.Reader; componenttype = 70; rootcomponentbehavior = $null })
                if ($w.WriterExists) { $rows += [pscustomobject]@{ objectid = $ids.Writer; componenttype = 70; rootcomponentbehavior = $null } }
                foreach ($t in @($w.Pairs | ForEach-Object { ($_ -split '\.')[0] } | Select-Object -Unique)) {
                    $rows += [pscustomobject]@{ objectid = "meta-$t"; componenttype = 1; rootcomponentbehavior = 0 }
                }
                return @{ value = $rows }
            }
            if ($u -match '/teams\?') {
                return @{ value = @(
                        [pscustomobject]@{ teamid = $ids.RootTeam; name = 'Spaarke'; _businessunitid_value = $ids.Root; businessunitid = [pscustomobject]@{ name = 'Spaarke' } }
                        [pscustomobject]@{ teamid = $ids.SecureTeam; name = 'Secure Record'; _businessunitid_value = $ids.Secure; businessunitid = [pscustomobject]@{ name = 'Secure Record' } }
                        if ($w.ExternalUnitExists) { [pscustomobject]@{ teamid = $ids.ExternalTeam; name = 'External Collaborators'; _businessunitid_value = $ids.External; businessunitid = [pscustomobject]@{ name = 'External Collaborators' } } }
                    ) }
            }
            if ($u -match "/businessunits\?.*name eq '([^']+)'") {
                if ($Matches[1] -eq 'External Collaborators' -and $w.ExternalUnitExists) { return @{ value = @([pscustomobject]@{ businessunitid = $ids.External; name = 'External Collaborators' }) } }
                return @{ value = @() }
            }
            if ($u -match '/systemusers\([^)]+\)/systemuserroles_association') {
                return @{ value = @(if ($w.SysAdmin) { [pscustomobject]@{ name = 'System Administrator'; _roletemplateid_value = '627090ff-40a3-4053-8790-584edc5be201' } }) }
            }
            if ($u -match '/systemusers\([^)]+\)/teammembership_association') { return @{ value = @() } }
            if ($u -match '/systemusers\?.*applicationid eq ([0-9a-f-]+)') {
                if ($Matches[1] -eq $w.BffAppId) { return @{ value = @([pscustomobject]@{ systemuserid = $ids.BffUser; fullname = 'BFF' }) } }
                return @{ value = @() }
            }
            if ($u -match '/systemusers\?.*sprk_isexternal') { return @{ value = @() } }
            if ($u -match '/roles\?') { return @{ value = @() } }
            if ($u -match '/EntityDefinitions\?\$select=LogicalName&\$expand=Attributes') {
                return @{ value = @(
                        @($w.Carrying | ForEach-Object { [pscustomobject]@{ LogicalName = $_; Attributes = @([pscustomobject]@{ LogicalName = 'sprk_accesspermission' }) } })
                        [pscustomobject]@{ LogicalName = 'account'; Attributes = @() }
                    ) }
            }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)/Microsoft\.Dynamics\.CRM\.BooleanAttributeMetadata\?\`$expand=OptionSet$") {
                $key = "$($Matches[1]).$($Matches[2])"
                return [pscustomobject]@{ '@odata.type' = '#Microsoft.Dynamics.CRM.BooleanAttributeMetadata'; LogicalName = $Matches[2]; IsSecured = $w.Secured[$key]; OptionSet = @{ TrueOption = @{ Value = 1 } } }
            }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)(\?\`$select=(\w+))?$") {
                $key = "$($Matches[1]).$($Matches[2])"
                if ($key -notin $w.Pairs) { throw "Test: no such column $key" }
                if ($Matches[4] -eq 'IsSecured') { return [pscustomobject]@{ IsSecured = $w.Secured[$key] } }
                if ($Matches[4] -eq 'LogicalName') { return [pscustomobject]@{ LogicalName = $Matches[2] } }
                $type = if ($key -in $w.BooleanColumns) { '#Microsoft.Dynamics.CRM.BooleanAttributeMetadata' } else { '#Microsoft.Dynamics.CRM.PicklistAttributeMetadata' }
                return [pscustomobject]@{ '@odata.type' = $type; LogicalName = $Matches[2]; IsSecured = $w.Secured[$key] }
            }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)\?\`$select=MetadataId") { return [pscustomobject]@{ MetadataId = "meta-$($Matches[1])" } }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)\?\`$select=EntitySetName") { return [pscustomobject]@{ EntitySetName = "$($Matches[1])s" } }
            if ($u -match '/(sprk_fieldmappingrules|sprk_aitopicregistrys|sprk_emailupdatefields)\?') { return @{ value = @() } }
            if ($u -match "/fieldpermissions\?.*\`$expand=fieldsecurityprofileid.*entityname eq '([^']+)' and attributelogicalname eq '([^']+)'") {
                $key = "$($Matches[1]).$($Matches[2])"
                return @{ value = @($w.StrayGrants[$key] | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ canupdate = 4; cancreate = 0; fieldsecurityprofileid = [pscustomobject]@{ name = $_ } } }) }
            }
            if ($u -match "/fieldpermissions\?.*_fieldsecurityprofileid_value eq ([0-9a-f-]+) and entityname eq '([^']+)' and attributelogicalname eq '([^']+)'") {
                $perm = $w.Perms["$($Matches[1])|$($Matches[2]).$($Matches[3])"]
                return @{ value = @(if ($perm) { [pscustomobject]@{ fieldpermissionid = 'x'; canread = $perm.canread; cancreate = $perm.cancreate; canupdate = $perm.canupdate } }) }
            }
            if ($u -match '/fieldpermissions\?.*_fieldsecurityprofileid_value eq ([0-9a-f-]+)$') {
                $profileId = $Matches[1]
                return @{ value = @($w.Perms.Keys | Where-Object { $_ -like "$profileId|*" } | ForEach-Object {
                            $tc = ($_ -split '\|')[1] -split '\.'; [pscustomobject]@{ entityname = $tc[0]; attributelogicalname = $tc[1] } }) }
            }
            throw "Test: unexpected read $u"
        }
    }

    Context 'dry run (default)' {
        It 'writes nothing and lists every pair and the writer profile members it would add' {
            $r = Invoke-Script $script:EntryPoint
            $r.Error | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'DRY RUN \(no writes\)'
            $r.Output | Should -Match 'Target\s+: AccessMarkerColumns'
            foreach ($p in $script:Pairs) {
                $r.Output | Should -Match ("WOULD\s+secure " + [regex]::Escape($p) + ',')
                $r.Output | Should -Match ("WOULD\s+grant Spaarke Access Marker Writers on " + [regex]::Escape($p))
            }
            $r.Output | Should -Match "WOULD\s+\(p1\) create profile 'Spaarke Access Marker Writers' \(60b9344e-f253-49cd-af1a-4be2cf52c3a1\) in SpaarkeMaster"
            $r.Output | Should -Match "WOULD\s+\(p3\) add internal default team 'Spaarke'"
            $r.Output | Should -Match "WOULD\s+\(p3\) add internal default team 'Secure Record'"
            $r.Output | Should -Match "WOULD\s+\(p3\) add BFF app user 'BFF'"
            $r.Output | Should -Not -Match "add internal default team 'External Collaborators'"
            $r.Output | Should -Match 'DRY RUN complete'
        }
    }

    Context '-Verify' {
        It 'passes on a fully locked environment' {
            Reset-World 'Locked'
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true }
            $r.Error | Should -BeNullOrEmpty
            $r.Output | Should -Match 'PASS: '
            $r.ExitCode | Should -Be 0
        }

        It 'names an unsecured column' {
            Reset-World 'Locked'
            $global:AccessMarkerTestWorld.Secured['sprk_communication.sprk_isinternalonly'] = $false
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'MISSING\s+sprk_communication\.sprk_isinternalonly is NOT field-secured'
        }

        It 'names a business unit default team without the reader' {
            Reset-World 'Locked'
            [void]$global:AccessMarkerTestWorld.ReaderTeams.Remove($global:AccessMarkerTestWorld.Ids.ExternalTeam)
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "FAIL\s+\(p2\) default team 'External Collaborators' \(BU 'External Collaborators'\) is NOT on the reader profile"
        }

        It 'names the External Collaborators team holding the writer' {
            Reset-World 'Locked'
            $global:AccessMarkerTestWorld.WriterTeams.Add($global:AccessMarkerTestWorld.Ids.ExternalTeam)
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "FAIL\s+\(p3\) the 'External Collaborators' unit's default team 'External Collaborators' holds the WRITER profile"
        }

        It 'names a BFF app user without the writer' {
            Reset-World 'Locked'
            $global:AccessMarkerTestWorld.WriterUsers.Clear()
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "MISSING\s+\(p3\) BFF app user 'BFF' is NOT on the writer profile"
        }

        It 'names a table that carries sprk_accesspermission outside the target' {
            Reset-World 'Locked'
            $global:AccessMarkerTestWorld.Carrying += 'sprk_invoice'
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'FAIL\s+\(p9\) sprk_invoice carries sprk_accesspermission but is not in the target'
        }

        It 'reads the External Collaborators unit name from the 191d definition file' {
            Reset-World 'Locked'
            $config = Join-Path $TestDrive 'external-collaborator-role.json'
            '{ "businessUnit": { "name": "External Collaborators", "parent": "root" } }' | Set-Content -LiteralPath $config
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true; ExternalCollaboratorConfigPath = $config }
            $r.Error | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
        }

        It 'refuses to start without the External Collaborators definition' {
            $r = Invoke-Script $script:EntryPoint @{ Verify = $true; ExternalCollaboratorConfigPath = (Join-Path $TestDrive 'absent.json') }
            $r.Error | Should -Match 'External Collaborators definition .* is missing'
        }
    }

    Context '-Apply refusals (nothing written)' {
        It 'refuses while a business unit default team lacks the reader' {
            [void]$global:AccessMarkerTestWorld.ReaderTeams.Remove($global:AccessMarkerTestWorld.Ids.SecureTeam)
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.Error | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED: -Apply needs every precondition to pass'
            $r.Output | Should -Match "\(p2\) default team 'Secure Record'"
            $global:AccessMarkerTestWorld.Writes.Count | Should -Be 0
        }

        It 'refuses while the External Collaborators unit does not exist' {
            $global:AccessMarkerTestWorld.ExternalUnitExists = $false
            [void]$global:AccessMarkerTestWorld.ReaderTeams.Remove($global:AccessMarkerTestWorld.Ids.ExternalTeam)
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "REFUSED"
            $r.Output | Should -Match "\(p8\) no business unit named 'External Collaborators'"
            $global:AccessMarkerTestWorld.Writes.Count | Should -Be 0
        }

        It 'refuses without the operator confirmation that the BFF no longer names a marker on caller creates' {
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'FAIL\s+\(p4\) -BffOmitsMarkersFromCallerCreates not passed'
            $global:AccessMarkerTestWorld.Writes.Count | Should -Be 0
        }

        It 'refuses while another profile already grants update on a marker (the shared reader included)' {
            $global:AccessMarkerTestWorld.StrayGrants['sprk_matter.sprk_accesspermission'] = @('Spaarke BFF-Managed Field Readers')
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "FAIL\s+\(p11\) profile 'Spaarke BFF-Managed Field Readers' already grants create/update on sprk_matter\.sprk_accesspermission"
            $global:AccessMarkerTestWorld.Writes.Count | Should -Be 0
        }

        It 'refuses while a BFF app user does not hold System Administrator' {
            $global:AccessMarkerTestWorld.SysAdmin = $false
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "FAIL\s+\(p10\) BFF app user 'BFF' does not hold System Administrator"
            $global:AccessMarkerTestWorld.Writes.Count | Should -Be 0
        }
    }

    Context '-Apply' {
        It 'adds the writer profile and its members before securing any column, never the External team, then verifies' {
            $global:AccessMarkerTestWorld.AllowWrites = $true
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.Error | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'PASS: '

            $writes = @($global:AccessMarkerTestWorld.Writes)
            $writes[0] | Should -Be 'Post fieldsecurityprofiles'
            $firstPut = [array]::FindIndex([string[]]$writes, [Predicate[string]] { param($x) $x -like 'Put *' })
            $lastRef = [array]::FindLastIndex([string[]]$writes, [Predicate[string]] { param($x) $x -like '*$ref' })
            $firstPut | Should -BeGreaterThan $lastRef
            @($writes | Where-Object { $_ -like 'Put *' } | Select-Object -Unique).Count | Should -Be $script:Pairs.Count
            # The Boolean internal-only flag: its plain PUT is refused, the one retry with its OptionSet lands.
            $global:AccessMarkerTestWorld.BooleanRetries | Should -Be 1
            $r.Output | Should -Match 'PUT of sprk_communication\.sprk_isinternalonly refused without its OptionSet'

            $ids = $global:AccessMarkerTestWorld.Ids
            $global:AccessMarkerTestWorld.WriterTeams | Should -Not -Contain $ids.ExternalTeam
            $global:AccessMarkerTestWorld.WriterTeams | Should -Contain $ids.RootTeam
            $global:AccessMarkerTestWorld.WriterTeams | Should -Contain $ids.SecureTeam
            $global:AccessMarkerTestWorld.WriterUsers | Should -Contain $ids.BffUser
            foreach ($p in $script:Pairs) {
                $global:AccessMarkerTestWorld.Secured[$p] | Should -BeTrue
                $global:AccessMarkerTestWorld.Perms["$($ids.Reader)|$p"].canupdate | Should -Be 0
                $global:AccessMarkerTestWorld.Perms["$($ids.Writer)|$p"].canupdate | Should -Be 4
            }
            foreach ($t in $script:Tables) { $global:AccessMarkerTestWorld.PublishXml | Should -Match "<entity>$t</entity>" }
        }

        It 'stops with nothing secured when a writer member does not land (read-back)' {
            $global:AccessMarkerTestWorld.AllowWrites = $true
            $global:AccessMarkerTestWorld.DropRefs = $true
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'STOPPED before securing anything'
            @($global:AccessMarkerTestWorld.Writes | Where-Object { $_ -like 'Put *' -or $_ -eq 'Post fieldpermissions' }).Count | Should -Be 0
            foreach ($p in $script:Pairs) { $global:AccessMarkerTestWorld.Secured[$p] | Should -BeFalse }
        }

        It 'reverts a column outside sprk_document when its grant fails, and publishes that table' {
            $global:AccessMarkerTestWorld.AllowWrites = $true
            $ids = $global:AccessMarkerTestWorld.Ids
            $global:AccessMarkerTestWorld.FailGrantFor = "$($ids.Writer)|sprk_communicationthread.sprk_privacystate"
            $r = Invoke-Script $script:EntryPoint @{ Apply = $true; BffOmitsMarkersFromCallerCreates = $true }
            $r.Error | Should -Match 'the grant failed'
            $global:AccessMarkerTestWorld.Secured['sprk_communicationthread.sprk_privacystate'] | Should -BeFalse
            $global:AccessMarkerTestWorld.PublishXml | Should -Be '<importexportxml><entities><entity>sprk_communicationthread</entity></entities></importexportxml>'
            $global:AccessMarkerTestWorld.Writes | Should -Contain "Put EntityDefinitions(LogicalName='sprk_communicationthread')/Attributes(LogicalName='sprk_privacystate')"
        }
    }
}

Describe 'sprk_isexternal is never a target column' {
    It 'is named by no target of the engine' {
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:Engine, [ref]$null, [ref]$null)
        $targets = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$Targets' }, $true)
        $targets | Should -Not -BeNullOrEmpty
        $strings = @($targets.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true) | ForEach-Object { $_.Value })
        $strings | Should -Contain 'sprk_communication.sprk_isinternalonly'   # the scan reads the real target list
        @($strings | Where-Object { $_ -match 'sprk_isexternal' }) | Should -BeNullOrEmpty
    }

    It 'stops the engine before any read when a target names it (must-fire)' {
        $copy = Join-Path $TestDrive 'scripts'
        New-Item -ItemType Directory -Path $copy -Force | Out-Null
        Copy-Item -Recurse -Path (Join-Path $script:RepoRoot 'scripts' 'common') -Destination $copy
        $text = (Get-Content -LiteralPath $script:Engine -Raw).Replace("'sprk_communication.sprk_isinternalonly',", "'sprk_communication.sprk_isinternalonly', 'systemuser.sprk_isexternal',")
        $text | Should -Match 'systemuser\.sprk_isexternal'
        $engineCopy = Join-Path $copy 'Set-DocumentPointerFieldSecurity.ps1'
        Set-Content -LiteralPath $engineCopy -Value $text
        Mock az { throw 'Test: the guard must stop the engine before it asks for a token.' }
        $r = Invoke-Script $engineCopy
        $r.Error | Should -Match 'sprk_isexternal, which is never field-secured'
    }
}
