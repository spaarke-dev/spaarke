# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1 -Force; Invoke-Pester tests/scripts/Set-PrivateContextSchema.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (ubuntu-latest).
# field-mapping-server-write-path-r1 task 056b (design note, condition C4): scripts/Set-PrivateContextSchema.ps1 over a mocked
# Dataverse. Every write in -Verify and in a dry run is a test failure; -Apply writes are recorded and checked.
# Must fire (-Verify exits 1): each of Assign, Share, Unshare, Reparent set to Cascade; Delete not RemoveLink (Cascade, NoCascade,
#   Restrict); Merge not NoCascade; the menu shown; a missing column; a missing relationship; a wrong lookup target; a component
#   missing from the solution. The same cascade fault is reported (not written) by a dry run and corrected only by -Apply.
# Must not fire: the correct shape on all three columns exits 0 and writes nothing; -Apply on it writes only the publish.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Set-PrivateContextSchema.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    # Mock needs the command to exist; a runner without the az CLI gets a stub (the mock replaces it in every test).
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    $script:Specs = @(
        @{ Column = 'sprk_privatecontextmatter'; Target = 'sprk_matter'; Relationship = 'sprk_matter_sprk_communication_privatecontext'; N = 1 }
        @{ Column = 'sprk_privatecontextproject'; Target = 'sprk_project'; Relationship = 'sprk_project_sprk_communication_privatecontext'; N = 2 }
        @{ Column = 'sprk_privatecontextworkassignment'; Target = 'sprk_workassignment'; Relationship = 'sprk_workassignment_sprk_communication_privatecontext'; N = 3 }
    )

    function New-Guid4([int]$Kind, [int]$N) { '{0:x8}-0000-4000-8000-{1:x12}' -f $Kind, $N }

    function Reset-World {
        $global:SetPrivateContextTest_Attrs = @{}
        $global:SetPrivateContextTest_Rels = @{}
        $global:SetPrivateContextTest_InSolution = [System.Collections.Generic.List[string]]::new()
        $global:SetPrivateContextTest_Writes = [System.Collections.Generic.List[object]]::new()
        foreach ($s in $script:Specs) {
            $global:SetPrivateContextTest_Attrs[$s.Column] = [pscustomobject]@{ LogicalName = $s.Column; SchemaName = 'x'; Targets = @($s.Target); MetadataId = (New-Guid4 1 $s.N) }
            $global:SetPrivateContextTest_Rels[$s.Relationship] = [pscustomobject]@{
                SchemaName = $s.Relationship; ReferencedEntity = $s.Target; ReferencingEntity = 'sprk_communication'; ReferencingAttribute = $s.Column
                ReferencingEntityNavigationPropertyName = $s.Column; MetadataId = (New-Guid4 2 $s.N)
                CascadeConfiguration = [pscustomobject]@{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink'; RollupView = 'NoCascade' }
                AssociatedMenuConfiguration = [pscustomobject]@{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
            }
            $global:SetPrivateContextTest_InSolution.Add((New-Guid4 1 $s.N))
            $global:SetPrivateContextTest_InSolution.Add((New-Guid4 2 $s.N))
        }
    }

    function Invoke-Script([hashtable]$Extra = @{}) {
        $arguments = @{ EnvironmentUrl = $script:Url }
        foreach ($k in $Extra.Keys) { $arguments[$k] = $Extra[$k] }
        $global:LASTEXITCODE = 0
        $thrown = $null
        try { $output = & $script:Script @arguments 6>&1 2>&1 | Out-String }
        catch { $thrown = $_.Exception.Message; $output = '' }
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Thrown = $thrown }
    }

    # The mocked Dataverse: reads answer from the world; writes are recorded and, for the ones the script makes, applied to it.
    function Invoke-FakeDataverse($Method, $Uri, $Body) {
        $u = [string]$Uri
        if ($Method -eq 'Get') {
            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ name = 'test' }) } }
            if ($u -match "/Attributes\(LogicalName='([^']+)'\)/Microsoft.Dynamics.CRM.LookupAttributeMetadata") {
                $a = $global:SetPrivateContextTest_Attrs[$Matches[1]]
                if ($a) { return $a }
                throw 'Test: 404 attribute'
            }
            if ($u -match "RelationshipDefinitions\(SchemaName='([^']+)'\)/") {
                $r = $global:SetPrivateContextTest_Rels[$Matches[1]]
                if ($r) { return $r }
                throw 'Test: 404 relationship'
            }
            if ($u -match 'RelationshipDefinitions\(([0-9a-f-]{36})\)/') {
                $id = $Matches[1]
                return ($global:SetPrivateContextTest_Rels.Values | Where-Object { $_.MetadataId -eq $id } | ConvertTo-Json -Depth 10 | ConvertFrom-Json)
            }
            if ($u -match '/solutions\?') { return @{ value = @([pscustomobject]@{ solutionid = (New-Guid4 9 1); uniquename = 'SpaarkeCore' }) } }
            if ($u -match "EntityDefinitions\(LogicalName='sprk_communication'\)\?\`$select=MetadataId") { return [pscustomobject]@{ MetadataId = (New-Guid4 8 1) } }
            if ($u -match "EntityDefinitions\(LogicalName='sprk_communication'\)\?\`$select=EntitySetName") { return [pscustomobject]@{ EntitySetName = 'sprk_communications' } }
            if ($u -match '/solutioncomponents\?') {
                return @{ value = @($global:SetPrivateContextTest_InSolution | ForEach-Object { [pscustomobject]@{ objectid = $_; componenttype = 2; rootcomponentbehavior = 1 } }) }
            }
            if ($u -match '/sprk_communications\?') { return @{ value = @() } }
            throw "Test: unexpected read $u"
        }

        $global:SetPrivateContextTest_Writes.Add([pscustomobject]@{ Method = $Method; Uri = $u; Body = $Body })
        if ($u -match '/RelationshipDefinitions$' -and $Method -eq 'Post') {
            $b = $Body | ConvertFrom-Json
            $spec = $script:Specs | Where-Object { $_.Column -eq $b.Lookup.SchemaName.ToLowerInvariant() }
            $global:SetPrivateContextTest_Attrs[$spec.Column] = [pscustomobject]@{ LogicalName = $spec.Column; SchemaName = $b.Lookup.SchemaName; Targets = @($b.ReferencedEntity); MetadataId = (New-Guid4 1 $spec.N) }
            $global:SetPrivateContextTest_Rels[$b.SchemaName] = [pscustomobject]@{
                SchemaName = $b.SchemaName; ReferencedEntity = $b.ReferencedEntity; ReferencingEntity = $b.ReferencingEntity; ReferencingAttribute = $spec.Column
                ReferencingEntityNavigationPropertyName = $spec.Column; MetadataId = (New-Guid4 2 $spec.N)
                CascadeConfiguration = $b.CascadeConfiguration; AssociatedMenuConfiguration = $b.AssociatedMenuConfiguration
            }
            $global:SetPrivateContextTest_InSolution.Add((New-Guid4 1 $spec.N))
            $global:SetPrivateContextTest_InSolution.Add((New-Guid4 2 $spec.N))
        }
        if ($u -match 'RelationshipDefinitions\(([0-9a-f-]{36})\)$' -and $Method -eq 'Put') {
            $id = $Matches[1]; $b = $Body | ConvertFrom-Json
            $name = ($global:SetPrivateContextTest_Rels.Keys | Where-Object { $global:SetPrivateContextTest_Rels[$_].MetadataId -eq $id })
            $global:SetPrivateContextTest_Rels[$name].CascadeConfiguration = $b.CascadeConfiguration
            $global:SetPrivateContextTest_Rels[$name].AssociatedMenuConfiguration = $b.AssociatedMenuConfiguration
        }
        return $null
    }

    function Get-Rel([string]$Column) { $global:SetPrivateContextTest_Rels[($script:Specs | Where-Object { $_.Column -eq $Column }).Relationship] }
    function Get-NonPublishWrites { @($global:SetPrivateContextTest_Writes | Where-Object { $_.Uri -notmatch 'PublishXml$' }) }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'SetPrivateContextTest_*' -ErrorAction SilentlyContinue
}

Describe 'Set-PrivateContextSchema.ps1 -Verify (mocked Dataverse; a write is a failure)' {
    BeforeEach {
        Reset-World
        Mock az { 'test-token' }
        Mock Start-Sleep { }
        Mock Invoke-RestMethod {
            if ($Method -ne 'Get') { throw "Test: -Verify wrote ($Method $Uri)." }
            Invoke-FakeDataverse $Method $Uri $Body
        }
    }

    It 'must not fire: the correct shape on all three columns passes with exit 0' {
        $r = Invoke-Script @{ Verify = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'VERIFY PASS'
        foreach ($s in $script:Specs) { $r.Output | Should -Match ([regex]::Escape("$($s.Relationship) cascade")) }
    }

    It 'must fire: <Key>=Cascade on <Column> fails -Verify, naming the cascade and the relationship' -ForEach @(
        foreach ($c in @('sprk_privatecontextmatter', 'sprk_privatecontextproject', 'sprk_privatecontextworkassignment')) {
            foreach ($k in @('Assign', 'Share', 'Unshare', 'Reparent')) { @{ Column = $c; Key = $k } }
        }
    ) {
        (Get-Rel $Column).CascadeConfiguration.$Key = 'Cascade'

        $r = Invoke-Script @{ Verify = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match ([regex]::Escape("$Key=Cascade (expected NoCascade)"))
        $r.Output | Should -Match 'VERIFY FAIL'
    }

    It 'must fire: Delete=<Value> (not RemoveLink) on <Column> fails -Verify' -ForEach @(
        foreach ($c in @('sprk_privatecontextmatter', 'sprk_privatecontextproject', 'sprk_privatecontextworkassignment')) {
            foreach ($v in @('Cascade', 'NoCascade', 'Restrict')) { @{ Column = $c; Value = $v } }
        }
    ) {
        (Get-Rel $Column).CascadeConfiguration.Delete = $Value

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match ([regex]::Escape("Delete=$Value (expected RemoveLink)"))
    }

    It 'must fire: Merge=Cascade fails -Verify' {
        (Get-Rel 'sprk_privatecontextproject').CascadeConfiguration.Merge = 'Cascade'

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match ([regex]::Escape('Merge=Cascade (expected NoCascade)'))
    }

    It 'must fire: a relationship shown on the record forms fails -Verify' {
        (Get-Rel 'sprk_privatecontextmatter').AssociatedMenuConfiguration.Behavior = 'UseCollectionName'

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match ([regex]::Escape('AssociatedMenu=UseCollectionName (expected DoNotDisplay)'))
    }

    It 'must fire: several faults on one relationship are all named' {
        $cfg = (Get-Rel 'sprk_privatecontextworkassignment').CascadeConfiguration
        $cfg.Share = 'Cascade'; $cfg.Reparent = 'Cascade'

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'Share=Cascade'
        $r.Output | Should -Match 'Reparent=Cascade'
    }

    It 'must fire: a missing column fails -Verify, names it, and writes nothing' {
        $global:SetPrivateContextTest_Attrs.Remove('sprk_privatecontextproject')
        $global:SetPrivateContextTest_Rels.Remove('sprk_project_sprk_communication_privatecontext')

        $r = Invoke-Script @{ Verify = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'MISSING\s+sprk_communication\.sprk_privatecontextproject'
        $r.Output | Should -Match 'MISSING\s+relationship sprk_project_sprk_communication_privatecontext'
    }

    It 'must fire: a column with no relationship fails -Verify' {
        $global:SetPrivateContextTest_Rels.Remove('sprk_matter_sprk_communication_privatecontext')

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'relationship sprk_matter_sprk_communication_privatecontext was not found'
    }

    It 'must fire: a lookup that targets the wrong table fails -Verify' {
        $global:SetPrivateContextTest_Attrs['sprk_privatecontextmatter'].Targets = @('sprk_matter', 'sprk_project')

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'targets \[sprk_matter, sprk_project\]; expected exactly sprk_matter'
    }

    It 'must fire: a component outside the solution fails -Verify' {
        $global:SetPrivateContextTest_InSolution.Remove((New-Guid4 2 3)) | Out-Null

        $r = Invoke-Script @{ Verify = $true }

        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'relationship sprk_workassignment_sprk_communication_privatecontext in SpaarkeCore'
    }
}

Describe 'Set-PrivateContextSchema.ps1 dry run and -Apply (mocked Dataverse)' {
    BeforeEach {
        Reset-World
        Mock az { 'test-token' }
        Mock Start-Sleep { }
        Mock Invoke-RestMethod { Invoke-FakeDataverse $Method $Uri $Body }
    }

    It 'must not fire: -Apply on a correct shape writes nothing but the publish and exits 0' {
        $r = Invoke-Script @{ Apply = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 0
        Get-NonPublishWrites | Should -HaveCount 0
        @($global:SetPrivateContextTest_Writes | Where-Object { $_.Uri -match 'PublishXml$' }) | Should -HaveCount 1
    }

    It 'a dry run reports a wrong cascade and writes nothing' {
        (Get-Rel 'sprk_privatecontextmatter').CascadeConfiguration.Share = 'Cascade'

        $r = Invoke-Script @{}

        $r.Thrown | Should -BeNullOrEmpty
        $r.Output | Should -Match ([regex]::Escape('Share=Cascade (expected NoCascade)'))
        $r.Output | Should -Match 'WOULD\s+correct sprk_matter_sprk_communication_privatecontext'
        $global:SetPrivateContextTest_Writes | Should -HaveCount 0
        (Get-Rel 'sprk_privatecontextmatter').CascadeConfiguration.Share | Should -Be 'Cascade'
    }

    It 'a dry run on an empty environment would create all three and writes nothing' {
        $global:SetPrivateContextTest_Attrs.Clear(); $global:SetPrivateContextTest_Rels.Clear(); $global:SetPrivateContextTest_InSolution.Clear()

        $r = Invoke-Script @{}

        $r.ExitCode | Should -Be 0
        @([regex]::Matches($r.Output, 'WOULD\s+create sprk_communication\.sprk_privatecontext')) | Should -HaveCount 3
        $global:SetPrivateContextTest_Writes | Should -HaveCount 0
    }

    It '-Apply corrects a wrong cascade through a metadata update of that relationship only, then passes' {
        $cfg = (Get-Rel 'sprk_privatecontextproject').CascadeConfiguration
        $cfg.Assign = 'Cascade'; $cfg.Reparent = 'Cascade'; $cfg.Delete = 'Cascade'

        $r = Invoke-Script @{ Apply = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 0
        $puts = @(Get-NonPublishWrites)
        $puts | Should -HaveCount 1
        $puts[0].Method | Should -Be 'Put'
        $puts[0].Uri | Should -Match 'RelationshipDefinitions\(00000002-0000-4000-8000-000000000002\)$'
        (Get-Rel 'sprk_privatecontextproject').CascadeConfiguration.Assign | Should -Be 'NoCascade'
        (Get-Rel 'sprk_privatecontextproject').CascadeConfiguration.Reparent | Should -Be 'NoCascade'
        (Get-Rel 'sprk_privatecontextproject').CascadeConfiguration.Delete | Should -Be 'RemoveLink'
        (Get-Rel 'sprk_privatecontextmatter').CascadeConfiguration.Assign | Should -Be 'NoCascade'
    }

    It '-Apply creates all three columns with a referential cascade and a hidden menu, then passes' {
        $global:SetPrivateContextTest_Attrs.Clear(); $global:SetPrivateContextTest_Rels.Clear(); $global:SetPrivateContextTest_InSolution.Clear()

        $r = Invoke-Script @{ Apply = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 0
        $creates = @($global:SetPrivateContextTest_Writes | Where-Object { $_.Uri -match '/RelationshipDefinitions$' })
        $creates | Should -HaveCount 3
        foreach ($c in $creates) {
            $b = $c.Body | ConvertFrom-Json
            foreach ($k in 'Assign', 'Share', 'Unshare', 'Reparent', 'Merge') { $b.CascadeConfiguration.$k | Should -Be 'NoCascade' }
            $b.CascadeConfiguration.Delete | Should -Be 'RemoveLink'
            $b.AssociatedMenuConfiguration.Behavior | Should -Be 'DoNotDisplay'
            $b.Lookup.RequiredLevel.Value | Should -Be 'None'
        }
        @($creates | ForEach-Object { ($_.Body | ConvertFrom-Json).ReferencedEntity } | Sort-Object) | Should -Be @('sprk_matter', 'sprk_project', 'sprk_workassignment')
    }
}
