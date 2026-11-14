# Pester 5.7.1 (6.x does not load the containers here). Run: Invoke-Pester tests/scripts/Set-InvoiceFamilyFieldSecurity.Tests.ps1
# #2799 (unified-access-control-r2): scripts/Set-InvoiceFamilyFieldSecurity.ps1 -> scripts/Set-DocumentPointerFieldSecurity.ps1
# -Target InvoiceFamilyLinks over a mocked Dataverse. Pins: the 24 columns come from scripts/common/InvoiceFamilyLockedColumns.psd1;
# a deployed web resource that writes a locked column ON A FAMILY TABLE makes -Apply refuse with nothing written (fail closed),
# and a dry run reports the same finding without refusing; the same column written on sprk_event does not block; -Apply
# needs -BffWritesInvoiceFamilyLinksAsApp; on a clean environment -Apply secures every column, grants both profiles and
# publishes the six tables; -Verify fails on an unsecured column and on a business unit default team without the reader.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:EntryPoint = Join-Path $script:RepoRoot 'scripts' 'Set-InvoiceFamilyFieldSecurity.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    $list = Import-PowerShellDataFile -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'common' 'InvoiceFamilyLockedColumns.psd1')
    $script:Pairs = @($list.Keys | Sort-Object | ForEach-Object { $t = $_; $list[$t].Columns | ForEach-Object { "$t.$_" } })
    $script:Tables = @($list.Keys | Sort-Object)
    $script:BffAppId = 'aaaaaaaa-0000-4000-8000-000000000001'

    $script:StaleBundle = 'await t.updateRecord("sprk_invoice",o,{"sprk_Matter@odata.bind":`/sprk_matters(${i})`})'
    $script:EventWrite = 'await t.updateRecord("sprk_event",o,{"sprk_Matter@odata.bind":`/sprk_matters(${i})`})'

    function Reset-World([ValidateSet('Before', 'Locked')][string]$State, [string[]]$WebResources = @()) {
        $ids = @{
            Root = '00000000-0000-4000-8000-000000000001'; Unit1 = '00000000-0000-4000-8000-000000000002'
            RootTeam = '00000000-0000-4000-8000-000000000011'; Unit1Team = '00000000-0000-4000-8000-000000000012'
            BffUser = '00000000-0000-4000-8000-000000000021'; Reader = '00000000-0000-4000-8000-000000000031'
            Writer = '00000000-0000-4000-8000-000000000032'; Solution = '00000000-0000-4000-8000-000000000041'
        }
        $locked = $State -eq 'Locked'
        $world = @{
            Ids = $ids; Pairs = $script:Pairs; BffAppId = $script:BffAppId
            ReaderTeams = [System.Collections.Generic.List[string]]@($ids.RootTeam, $ids.Unit1Team)
            Secured = @{}; Perms = @{}
            AllowWrites = $false
            Writes = [System.Collections.Generic.List[string]]::new()
            PublishXml = $null
            WebResources = @($WebResources)
        }
        foreach ($p in $script:Pairs) {
            $world.Secured[$p] = $locked
            if ($locked) {
                $world.Perms["$($ids.Reader)|$p"] = @{ canread = 4; cancreate = 0; canupdate = 0 }
                $world.Perms["$($ids.Writer)|$p"] = @{ canread = 4; cancreate = 4; canupdate = 4 }
            }
        }
        $global:InvoiceFamilyLockTestWorld = $world
    }

    function Invoke-Script([hashtable]$Extra = @{}) {
        $arguments = @{ EnvironmentUrl = $script:Url; BffApplicationIds = @($script:BffAppId) }
        foreach ($k in $Extra.Keys) { $arguments[$k] = $Extra[$k] }
        $global:LASTEXITCODE = 0
        try {
            $output = & $script:EntryPoint @arguments 6>&1 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Error = $null }
        } catch {
            [pscustomobject]@{ ExitCode = -1; Output = ''; Error = $_.Exception.Message }
        }
    }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'InvoiceFamilyLockTestWorld' -ErrorAction SilentlyContinue
}

Describe 'Set-InvoiceFamilyFieldSecurity.ps1 (mocked Dataverse)' {
    BeforeEach {
        Reset-World 'Before'
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            $w = $global:InvoiceFamilyLockTestWorld
            $ids = $w.Ids
            $u = [uri]::UnescapeDataString([string]$Uri)
            $teamRow = {
                param($id)
                $name = switch ($id) { $ids.RootTeam { 'Spaarke' } $ids.Unit1Team { 'Unit 1' } }
                [pscustomobject]@{ teamid = $id; name = $name; isdefault = $true }
            }

            if ($Method -ne 'Get') {
                if (-not $w.AllowWrites) { throw "Test: the script wrote ($Method $u)." }
                $path = $u -replace '^https://[^/]+/api/data/v9\.2/', ''
                $w.Writes.Add("$Method $path")
                $b = if ($Body) { $Body | ConvertFrom-Json -AsHashtable } else { @{} }
                if ($Method -eq 'Put' -and $path -match "EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)") {
                    $w.Secured["$($Matches[1]).$($Matches[2])"] = [bool]$b.IsSecured; return
                }
                if ($path -eq 'fieldpermissions') {
                    $profileId = $b['fieldsecurityprofileid@odata.bind'] -replace '.*\(([^)]+)\)$', '$1'
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
                if ($name -eq 'Spaarke BFF-Managed Field Writers') {
                    return @{ value = @([pscustomobject]@{ fieldsecurityprofileid = $ids.Writer; name = $name; teamprofiles_association = @()
                                systemuserprofiles_association = @([pscustomobject]@{ systemuserid = $ids.BffUser; fullname = 'BFF'; applicationid = $w.BffAppId }) }) }
                }
                return @{ value = @() }
            }
            if ($u -match '/solutions\?') { return @{ value = @([pscustomobject]@{ solutionid = $ids.Solution }) } }
            if ($u -match '/solutioncomponents\?') {
                $rows = @(
                    [pscustomobject]@{ objectid = $ids.Reader; componenttype = 70; rootcomponentbehavior = $null }
                    [pscustomobject]@{ objectid = $ids.Writer; componenttype = 70; rootcomponentbehavior = $null }
                )
                foreach ($t in @($w.Pairs | ForEach-Object { ($_ -split '\.')[0] } | Select-Object -Unique)) { $rows += [pscustomobject]@{ objectid = "meta-$t"; componenttype = 1; rootcomponentbehavior = 0 } }
                return @{ value = $rows }
            }
            if ($u -match '/teams\?') {
                return @{ value = @(
                        [pscustomobject]@{ teamid = $ids.RootTeam; name = 'Spaarke'; _businessunitid_value = $ids.Root; businessunitid = [pscustomobject]@{ name = 'Spaarke' } }
                        [pscustomobject]@{ teamid = $ids.Unit1Team; name = 'Unit 1'; _businessunitid_value = $ids.Unit1; businessunitid = [pscustomobject]@{ name = 'Unit 1' } }
                    ) }
            }
            if ($u -match '/businessunits\?') { return @{ value = @() } }
            if ($u -match '/systemusers\?.*applicationid eq ([0-9a-f-]+)') {
                if ($Matches[1] -eq $w.BffAppId) { return @{ value = @([pscustomobject]@{ systemuserid = $ids.BffUser; fullname = 'BFF' }) } }
                return @{ value = @() }
            }
            if ($u -match '/roles\?') { return @{ value = @() } }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)/ManyToOneRelationships\?") {
                $t = $Matches[1]
                return @{ value = @($w.Pairs | Where-Object { $_ -like "$t.*" } | ForEach-Object {
                            $c = ($_ -split '\.')[1]; [pscustomobject]@{ ReferencingAttribute = $c; ReferencingEntityNavigationPropertyName = $c } }) }
            }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)(\?\`$select=(\w+))?$") {
                $key = "$($Matches[1]).$($Matches[2])"
                if ($key -notin $w.Pairs) { throw "Test: no such column $key" }
                if ($Matches[4] -eq 'IsSecured') { return [pscustomobject]@{ IsSecured = $w.Secured[$key] } }
                if ($Matches[4] -eq 'LogicalName') { return [pscustomobject]@{ LogicalName = $Matches[2] } }
                return [pscustomobject]@{ '@odata.type' = '#Microsoft.Dynamics.CRM.LookupAttributeMetadata'; LogicalName = $Matches[2]; IsSecured = $w.Secured[$key] }
            }
            if ($u -match "/EntityDefinitions\(LogicalName='([^']+)'\)\?\`$select=MetadataId") { return [pscustomobject]@{ MetadataId = "meta-$($Matches[1])" } }
            if ($u -match '/webresourceset\?') {
                $i = 0
                return @{ value = @($w.WebResources | ForEach-Object {
                            $i++; [pscustomobject]@{ name = "sprk_wr$i"; webresourcetype = 3; content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_)) } }) }
            }
            if ($u -match "/fieldpermissions\?.*\`$expand=fieldsecurityprofileid.*entityname eq '([^']+)' and attributelogicalname eq '([^']+)'") { return @{ value = @() } }
            if ($u -match "/fieldpermissions\?.*_fieldsecurityprofileid_value eq ([0-9a-f-]+) and entityname eq '([^']+)' and attributelogicalname eq '([^']+)'") {
                $perm = $w.Perms["$($Matches[1])|$($Matches[2]).$($Matches[3])"]
                return @{ value = @(if ($perm) { [pscustomobject]@{ fieldpermissionid = 'x'; canread = $perm.canread; cancreate = $perm.cancreate; canupdate = $perm.canupdate } }) }
            }
            throw "Test: unexpected read $u"
        }
    }

    It 'locks exactly the 24 columns of the one list' {
        $script:Pairs.Count | Should -Be 24
        $script:Tables.Count | Should -Be 6
    }

    Context 'dry run (default)' {
        It 'reports a deployed family-table write as a FAIL without refusing, and writes nothing' {
            Reset-World 'Before' -WebResources @($script:StaleBundle, 'var x = 1;')
            $r = Invoke-Script
            $r.Error | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'Target\s+: InvoiceFamilyLinks'
            $r.Output | Should -Match "FAIL\s+\(p4a\) web resource 'sprk_wr1' WRITES sprk_invoice \(updateRecord: sets"
            $r.Output | Should -Not -Match "web resource 'sprk_wr2'"
            foreach ($p in $script:Pairs) { $r.Output | Should -Match ('WOULD\s+secure ' + [regex]::Escape($p) + ',') }
            $r.Output | Should -Match 'DRY RUN complete'
            $global:InvoiceFamilyLockTestWorld.Writes.Count | Should -Be 0
        }
    }

    Context '-Apply' {
        It 'REFUSES, writing nothing, while a deployed web resource writes a locked column on a family table (must fire)' {
            Reset-World 'Before' -WebResources @('var x = 1;', $script:StaleBundle)
            $global:InvoiceFamilyLockTestWorld.AllowWrites = $true
            $r = Invoke-Script @{ Apply = $true; BffWritesInvoiceFamilyLinksAsApp = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED: -Apply needs every precondition to pass'
            $r.Output | Should -Match "\(p4a\) web resource 'sprk_wr2' WRITES sprk_invoice"
            $global:InvoiceFamilyLockTestWorld.Writes.Count | Should -Be 0
        }

        It 'REFUSES without -BffWritesInvoiceFamilyLinksAsApp' {
            Reset-World 'Before' -WebResources @('var x = 1;')
            $global:InvoiceFamilyLockTestWorld.AllowWrites = $true
            $r = Invoke-Script @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'FAIL\s+\(p4b\) -BffWritesInvoiceFamilyLinksAsApp not passed'
            $global:InvoiceFamilyLockTestWorld.Writes.Count | Should -Be 0
        }

        It 'secures every column, grants both profiles and publishes the six tables when the only client write is on sprk_event (must not fire)' {
            Reset-World 'Before' -WebResources @($script:EventWrite)
            $global:InvoiceFamilyLockTestWorld.AllowWrites = $true
            $r = Invoke-Script @{ Apply = $true; BffWritesInvoiceFamilyLinksAsApp = $true }
            $r.Error | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'OK\s+\(p4a\) none of the 1 deployed JavaScript / HTML web resources writes a locked invoice-family column'
            $w = $global:InvoiceFamilyLockTestWorld
            foreach ($p in $script:Pairs) {
                $w.Secured[$p] | Should -BeTrue
                $w.Perms["$($w.Ids.Reader)|$p"].canread | Should -Be 4
                $w.Perms["$($w.Ids.Reader)|$p"].canupdate | Should -Be 0
                $w.Perms["$($w.Ids.Writer)|$p"].canupdate | Should -Be 4
            }
            foreach ($t in $script:Tables) { $w.PublishXml | Should -Match "<entity>$t</entity>" }
            $r.Output | Should -Match 'PASS: '
        }
    }

    Context '-Verify' {
        It 'fails on an environment where nothing is locked yet' {
            Reset-World 'Before'
            $r = Invoke-Script @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'MISSING\s+sprk_invoice\.sprk_matter is NOT field-secured'
        }

        It 'passes on a fully locked environment with no client write' {
            Reset-World 'Locked' -WebResources @($script:EventWrite)
            $r = Invoke-Script @{ Verify = $true }
            $r.Error | Should -BeNullOrEmpty
            $r.Output | Should -Match 'PASS: '
            $r.ExitCode | Should -Be 0
        }

        It 'fails when a business unit default team does not hold the reader profile' {
            Reset-World 'Locked'
            [void]$global:InvoiceFamilyLockTestWorld.ReaderTeams.Remove($global:InvoiceFamilyLockTestWorld.Ids.Unit1Team)
            $r = Invoke-Script @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "FAIL\s+\(p2\) default team 'Unit 1' \(BU 'Unit 1'\) is NOT on the reader profile"
        }
    }
}
