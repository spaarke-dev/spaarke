# tests/scripts/SpaarkePackageScope.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 task 218c: the package scope rule (scripts/solution-authoring/
# SpaarkePackageScope.psm1). Proves: every sprk_ web resource is in scope whatever solution it sits in (paged
# results included); managed, intersect and non-prefixed components are not; OOB tables contribute only their sprk_
# columns, unmanaged views and forms; roles come from the ROOT business unit only (copies skipped) by pattern or name;
# unmanaged field security profiles are in scope; exclusions need a reason and a date; the comparison reports
# missing, shell-packaged, excluded-but-packaged, outside-the-rule, stale exclusions and unmatched extra names; the
# env-var value guard catches a values file in a folder and in a zip.
#
# Technique: every module function takes the Dataverse GET as a scriptblock. The fake serves canned pages AND
# asserts each query's $filter (review F2-1: a fake that ignored the filter let a broken query pass).
# Pester 3.4 syntax (as Set-AiSpendLimit.Tests.ps1).
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Import-Module (Join-Path $repoRoot 'scripts/solution-authoring/SpaarkePackageScope.psm1') -Force

function New-FakeGet {
    # $Pages: endpoint prefix -> @{ Page = <response>; Filter = <substrings the endpoint must contain> }
    param([hashtable]$Pages)
    return {
        param($endpoint)
        foreach ($k in $Pages.Keys) {
            if ($endpoint.StartsWith($k)) {
                foreach ($needle in @($Pages[$k].Filter)) {
                    if ($needle -and -not $endpoint.Contains($needle)) { throw "GET $endpoint lacks the expected filter '$needle'" }
                }
                return $Pages[$k].Page
            }
        }
        throw "unexpected GET $endpoint"
    }.GetNewClosure()
}

function New-Scope([object[]]$Exclusions = @(), [string[]]$AlsoIncluded = @('Spaarke Extra Role')) {
    [PSCustomObject]@{
        prefix                = 'sprk_'
        roleNamePattern       = '(?i)^spaarke '
        roleNamesAlsoIncluded = $AlsoIncluded
        exclusions            = $Exclusions
    }
}

function Page([object[]]$Value, [string]$Next = $null) {
    $p = [PSCustomObject]@{ value = $Value }
    if ($Next) { $p | Add-Member -NotePropertyName '@odata.nextLink' -NotePropertyValue $Next }
    $p
}

$script:UnmanagedPrefix = @('startswith(', "'sprk_')", 'ismanaged eq false')

function Get-DevPages {
    @{
        'EntityDefinitions'              = @{ Page = (Page @(
                    [PSCustomObject]@{ LogicalName = 'sprk_matter'; MetadataId = 'E1'; IsManaged = $false; IsIntersect = $false; Attributes = @() }
                    [PSCustomObject]@{ LogicalName = 'sprk_matter_contact'; MetadataId = 'E2'; IsManaged = $false; IsIntersect = $true; Attributes = @() }
                    [PSCustomObject]@{ LogicalName = 'sprk_vendorthing'; MetadataId = 'E4'; IsManaged = $true; IsIntersect = $false; Attributes = @() }
                    [PSCustomObject]@{ LogicalName = 'contact'; MetadataId = 'E3'; IsManaged = $true; IsIntersect = $false; Attributes = @(
                            [PSCustomObject]@{ LogicalName = 'sprk_customerid'; MetadataId = 'A1'; IsCustomAttribute = $true; IsManaged = $false; AttributeOf = $null }
                            [PSCustomObject]@{ LogicalName = 'sprk_customeridname'; MetadataId = 'A2'; IsCustomAttribute = $true; IsManaged = $false; AttributeOf = 'sprk_customerid' }
                            [PSCustomObject]@{ LogicalName = 'firstname'; MetadataId = 'A3'; IsCustomAttribute = $false; IsManaged = $true; AttributeOf = $null }
                            [PSCustomObject]@{ LogicalName = 'sprk_vendorcol'; MetadataId = 'A4'; IsCustomAttribute = $true; IsManaged = $true; AttributeOf = $null }
                        ) }
                )) }
        'GlobalOptionSetDefinitions'     = @{ Page = (Page @(
                    [PSCustomObject]@{ Name = 'sprk_status'; MetadataId = 'O1'; IsManaged = $false }
                    [PSCustomObject]@{ Name = 'msdyn_x'; MetadataId = 'O2'; IsManaged = $true }
                )) }
        'webresourceset'                 = @{ Filter = $script:UnmanagedPrefix; Page = (Page @([PSCustomObject]@{ webresourceid = 'W1'; name = 'sprk_spaarkeai' }) 'https://dev/api/data/v9.2/webresourceset?page2') }
        'https://dev/api/data/v9.2/webresourceset?page2' = @{ Page = (Page @([PSCustomObject]@{ webresourceid = 'W2'; name = 'sprk_dailyupdate' })) }
        'customcontrols'                 = @{ Filter = $script:UnmanagedPrefix; Page = (Page @(
                    [PSCustomObject]@{ customcontrolid = 'C1'; name = 'sprk_Spaarke.Records.MatterHeader' }
                    [PSCustomObject]@{ customcontrolid = 'C2'; name = 'sprk_Spaarke.Controls.DueDatesWidget' }
                )) }
        'appmodules'                     = @{ Filter = $script:UnmanagedPrefix; Page = (Page @([PSCustomObject]@{ appmoduleid = 'M1'; uniquename = 'sprk_MatterManagement' })) }
        'sitemaps'                       = @{ Filter = $script:UnmanagedPrefix; Page = (Page @()) }
        'environmentvariabledefinitions' = @{ Filter = $script:UnmanagedPrefix; Page = (Page @([PSCustomObject]@{ environmentvariabledefinitionid = 'V1'; schemaname = 'sprk_BffApiBaseUrl' })) }
        'savedqueries'                   = @{ Filter = 'ismanaged eq false'; Page = (Page @(
                    [PSCustomObject]@{ savedqueryid = 'Q1'; name = 'Contacts with Identity Collisions'; returnedtypecode = 'contact' }
                    [PSCustomObject]@{ savedqueryid = 'Q2'; name = 'Active Matters'; returnedtypecode = 'sprk_matter' }
                )) }
        'systemforms'                    = @{ Filter = 'ismanaged eq false'; Page = (Page @([PSCustomObject]@{ formid = 'F1'; name = 'Documents'; objecttypecode = 'none' })) }
        'fieldsecurityprofiles'          = @{ Filter = 'ismanaged eq false'; Page = (Page @([PSCustomObject]@{ fieldsecurityprofileid = 'P1'; name = 'Identity Link Readers' })) }
        'businessunits'                  = @{ Filter = '_parentbusinessunitid_value eq null'; Page = (Page @([PSCustomObject]@{ businessunitid = 'B0' })) }
        'roles'                          = @{ Filter = @('ismanaged eq false', '_businessunitid_value eq B0'); Page = (Page @(
                    [PSCustomObject]@{ roleid = 'R1'; name = 'Spaarke Core User'; _parentrootroleid_value = 'R1' }
                    [PSCustomObject]@{ roleid = 'R1b'; name = 'Spaarke Core User'; _parentrootroleid_value = 'R1' }
                    [PSCustomObject]@{ roleid = 'R2'; name = 'Spaarke Extra Role'; _parentrootroleid_value = 'R2' }
                    [PSCustomObject]@{ roleid = 'R3'; name = 'System Customizer'; _parentrootroleid_value = 'R3' }
                )) }
    }
}

Describe 'Get-PackageRuleComponents (T218c)' {
    $rule = @(Get-PackageRuleComponents -Get (New-FakeGet (Get-DevPages)) -Scope (New-Scope))
    $names = @($rule | ForEach-Object Name)

    It 'puts every sprk_ web resource in scope, across pages' {
        ($names -contains 'sprk_spaarkeai') | Should Be $true
        ($names -contains 'sprk_dailyupdate') | Should Be $true
    }

    It 'takes unmanaged custom tables whole; skips intersect and managed tables; OOB tables give only unmanaged sprk_ columns' {
        ($names -contains 'sprk_matter') | Should Be $true
        ($names -contains 'sprk_matter_contact') | Should Be $false
        ($names -contains 'sprk_vendorthing') | Should Be $false
        ($names -contains 'contact.sprk_customerid') | Should Be $true
        ($names -contains 'contact.sprk_customeridname') | Should Be $false
        ($names -contains 'contact.firstname') | Should Be $false
        ($names -contains 'contact.sprk_vendorcol') | Should Be $false
        ($rule | Where-Object Name -eq 'contact.sprk_customerid').ParentEntityId | Should Be 'e3'
    }

    It 'skips managed and non-prefixed option sets' {
        ($names -contains 'msdyn_x') | Should Be $false
        ($names -contains 'sprk_status') | Should Be $true
    }

    It 'takes unmanaged views on OOB tables, not those on sprk_ tables (they ship with the table), with the OOB table as parent' {
        ($names -contains 'contact: Contacts with Identity Collisions') | Should Be $true
        ($names -contains 'sprk_matter: Active Matters') | Should Be $false
        ($rule | Where-Object Name -eq 'contact: Contacts with Identity Collisions').ParentEntityId | Should Be 'e3'
    }

    It 'takes unmanaged dashboards, labelled as such, with no parent table' {
        $d = $rule | Where-Object Name -eq 'dashboard: Documents'
        $d.TypeName | Should Be 'SystemForm'
        $d.ParentEntityId | Should Be $null
    }

    It 'takes unmanaged field security profiles' {
        ($names -contains 'Identity Link Readers') | Should Be $true
    }

    It 'takes roles of the root business unit only (a child-unit role cannot be packaged), by pattern or name - never a copy, never another role' {
        @($rule | Where-Object { $_.TypeName -eq 'Role' } | ForEach-Object ObjectId) | Should Be @('r1', 'r2')
    }

    It 'adds nothing for a type with no components (empty page)' {
        @($rule | Where-Object TypeName -eq 'SiteMap').Count | Should Be 0
    }

    It 'lowercases object ids and carries the component type code' {
        $wr = $rule | Where-Object Name -eq 'sprk_spaarkeai'
        $wr.ObjectId | Should Be 'w1'
        $wr.ComponentType | Should Be 61
    }

    It 'fails when a query loses its unmanaged/prefix filter (the fake checks the filter text)' {
        $pages = Get-DevPages
        $pages['customcontrols'].Filter = @('ismanaged eq true')
        { Get-PackageRuleComponents -Get (New-FakeGet $pages) -Scope (New-Scope) } | Should Throw 'lacks the expected filter'
    }
}

Describe 'Read-PackageScope + exclusions (T218c)' {
    It 'marks an excluded component with its reason' {
        $scope = New-Scope @([PSCustomObject]@{ type = 'CustomControl'; name = 'sprk_Spaarke.Controls.DueDatesWidget'; reason = 'not on a form'; date = '2026-08-21' })
        $rule = @(Get-PackageRuleComponents -Get (New-FakeGet (Get-DevPages)) -Scope $scope)
        $hit = $rule | Where-Object Name -eq 'sprk_Spaarke.Controls.DueDatesWidget'
        $hit.Excluded | Should Be $true
        $hit.Reason | Should Be 'not on a form'
    }

    It 'refuses an exclusion without a reason' {
        $path = Join-Path $TestDrive 'scope.json'
        '{"prefix":"sprk_","roleNamePattern":"x","roleNamesAlsoIncluded":[],"exclusions":[{"type":"Role","name":"A","date":"2026-10-07"}]}' | Set-Content $path
        { Read-PackageScope -Path $path } | Should Throw 'reason'
    }

    It 'refuses an unknown type and a malformed date' {
        $path = Join-Path $TestDrive 'scope2.json'
        '{"prefix":"sprk_","roleNamePattern":"x","roleNamesAlsoIncluded":[],"exclusions":[{"type":"Plugin","name":"A","reason":"r","date":"2026-10-07"}]}' | Set-Content $path
        { Read-PackageScope -Path $path } | Should Throw 'Unknown package component type'
        '{"prefix":"sprk_","roleNamePattern":"x","roleNamesAlsoIncluded":[],"exclusions":[{"type":"Role","name":"A","reason":"r","date":"7 Oct"}]}' | Set-Content $path
        { Read-PackageScope -Path $path } | Should Throw 'yyyy-MM-dd'
    }

    It 'accepts the committed docs/data-model/package-scope.json' {
        $scope = Read-PackageScope -Path (Join-Path $repoRoot 'docs/data-model/package-scope.json')
        $scope.prefix | Should Be 'sprk_'
        @($scope.exclusions).Count -gt 0 | Should Be $true
    }
}

Describe 'Compare-PackageScope (T218c)' {
    $scope = New-Scope -Exclusions @(
        [PSCustomObject]@{ type = 'CustomControl'; name = 'sprk_Spaarke.Controls.DueDatesWidget'; reason = 'r'; date = '2026-08-21' }
        [PSCustomObject]@{ type = 'Role'; name = 'Gone Role'; reason = 'r'; date = '2026-08-21' }
    ) -AlsoIncluded @('Spaarke Extra Role', 'No Such Role')
    $rule = @(Get-PackageRuleComponents -Get (New-FakeGet (Get-DevPages)) -Scope $scope)
    $membership = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in '2|a1', '9|o1', '61|w2', '66|c1', '66|c2', '80|m1', '380|v1', '20|r1', '20|r2', '26|q1', '70|p1', '60|f1') { $membership[$k] = 0 }
    $membership['1|e1'] = 2
    $membership['1|e3'] = 1
    $membership['1|ms99'] = 0
    $membership['381|val1'] = 0
    $membership['10075|x'] = 0
    $result = Compare-PackageScope -RuleComponents $rule -MembershipKeys $membership -Scope $scope

    It 'reports the code page missing from the package' {
        @($result.MissingFromPackage | ForEach-Object Name) | Should Be @('sprk_spaarkeai')
    }

    It 'reports an in-scope table packaged as a shell' {
        @($result.PackagedAsShell | ForEach-Object Name) | Should Be @('sprk_matter')
    }

    It 'reports an excluded component that is packaged anyway' {
        @($result.ExcludedButInPackage | ForEach-Object Name) | Should Be @('sprk_Spaarke.Controls.DueDatesWidget')
    }

    It 'treats the OOB table row that comes with an in-scope view as explained' {
        $m = [System.Collections.Generic.Dictionary[string, int]]::new($membership, [StringComparer]::OrdinalIgnoreCase)
        $m.Remove('2|a1') | Out-Null; $m['1|e3'] = 2   # only the view explains contact now
        $r = Compare-PackageScope -RuleComponents $rule -MembershipKeys $m -Scope $scope
        @($r.OutsideRule | Where-Object { $_.ObjectId -eq 'e3' }).Count | Should Be 0
    }

    It 'reports packaged components the rule does not explain (dragged-in tables, env-var values), not OOB parents' {
        @($result.OutsideRule | ForEach-Object { "$($_.ComponentType)|$($_.ObjectId)" }) | Should Be @('1|ms99', '381|val1')
    }

    It 'reports a stale exclusion and an unmatched extra role name' {
        @($result.UnmatchedExclusions | ForEach-Object name) | Should Be @('Gone Role')
        @($result.UnmatchedAlsoIncluded) | Should Be @('No Such Role')
    }
}

Describe 'Find-LeakyDependencies (T218c F12 guard)' {
    It 'reports a missing dependency on solution=Active and ignores first-party ones' {
        $path = Join-Path $TestDrive 'Solution.xml'
        @'
<ImportExportXml><SolutionManifest><MissingDependencies>
  <MissingDependency><Required type="61" schemaName="sprk_/scripts/x.js" solution="Active" /><Dependent type="60" schemaName="sprk_matter main" /></MissingDependency>
  <MissingDependency><Required type="1" schemaName="msdyn_x" solution="msdynce_AppCommon (9.0)" /><Dependent type="1" schemaName="sprk_matter" /></MissingDependency>
</MissingDependencies></SolutionManifest></ImportExportXml>
'@ | Set-Content $path
        $leaks = @(Find-LeakyDependencies -SolutionXmlPath $path)
        $leaks.Count | Should Be 1
        $leaks[0] | Should Match 'sprk_/scripts/x.js'
    }

    It 'passes a manifest without leaks' {
        $path = Join-Path $TestDrive 'Clean.xml'
        '<ImportExportXml><SolutionManifest><MissingDependencies /></SolutionManifest></ImportExportXml>' | Set-Content $path
        @(Find-LeakyDependencies -SolutionXmlPath $path).Count | Should Be 0
    }
}

Describe 'Get-NextPackageVersion (T218e)' {
    It 'bumps each part as four parts (the inline bump produced 1.0.0.1.0)' {
        Get-NextPackageVersion -Current '1.0.0.0' -Kind Build | Should Be '1.0.1.0'
        Get-NextPackageVersion -Current '1.0.3.7' -Kind Minor | Should Be '1.1.0.0'
        Get-NextPackageVersion -Current '1.4.3.7' -Kind Major | Should Be '2.0.0.0'
        Get-NextPackageVersion -Current '1.0.0.0' -Kind Revision | Should Be '1.0.0.1'
        Get-NextPackageVersion -Current '1.2' -Kind Build | Should Be '1.2.1.0'
    }

    It 'sets an explicit version only when it is higher' {
        Get-NextPackageVersion -Current '1.0.0.0' -Version '1.2.0.0' | Should Be '1.2.0.0'
        Get-NextPackageVersion -Current '1.0.0.0' -Version '1.2' | Should Be '1.2.0.0'
        { Get-NextPackageVersion -Current '1.2.0.0' -Version '1.2' } | Should Throw 'not higher'
        { Get-NextPackageVersion -Current '1.2.0.0' -Version '1.1.9.9' } | Should Throw 'not higher'
        { Get-NextPackageVersion -Current '1.0.0.0' -Version '1.x' } | Should Throw 'Not a version'
    }
}

Describe 'New-SpaarkeMasterManifest (T218d — the shape H6 parses)' {
    $sample = Get-Content (Join-Path $repoRoot 'src/server/services/Sprk.Provisioning.ControlPlane.Tests/Fixtures/spaarkemaster-manifest.sample.json') -Raw | ConvertFrom-Json
    $made = New-SpaarkeMasterManifest -Version '1.2.0.0' -ManagedBlobName 'm.zip' -UnmanagedBlobName 'u.zip' `
        -ManagedSha256 'a' -UnmanagedSha256 'b' -BuildId 'x' -SourceSha 'y' | ConvertFrom-Json

    It 'has the same top-level and SpaarkeMaster keys as the sample the C# parser test reads' {
        (@($made.PSObject.Properties.Name) -join ',') | Should Be (@($sample.PSObject.Properties.Name) -join ',')
        (@($made.solutions.SpaarkeMaster.PSObject.Properties.Name) -join ',') |
            Should Be (@($sample.solutions.SpaarkeMaster.PSObject.Properties.Name) -join ',')
    }

    It 'writes the values it is given under solutions.SpaarkeMaster' {
        $made.solutions.SpaarkeMaster.version | Should Be '1.2.0.0'
        $made.solutions.SpaarkeMaster.managedBlobName | Should Be 'm.zip'
        $made.solutions.SpaarkeMaster.unmanagedBlobName | Should Be 'u.zip'
    }
}

Describe 'Get-PackedSolutionInfo (T218d)' {
    It 'reads name, version and the managed flag from a zip, and refuses a zip without solution.xml' {
        $src = Join-Path $TestDrive 'pkg'
        New-Item -ItemType Directory -Path $src -Force | Out-Null
        '<ImportExportXml><SolutionManifest><UniqueName>SpaarkeMaster</UniqueName><Version>1.2.0.0</Version><Managed>1</Managed></SolutionManifest></ImportExportXml>' |
            Set-Content (Join-Path $src 'solution.xml')
        $zip = Join-Path $TestDrive 'm.zip'
        Compress-Archive -Path (Join-Path $src '*') -DestinationPath $zip -Force
        $info = Get-PackedSolutionInfo -ZipPath $zip
        $info.UniqueName | Should Be 'SpaarkeMaster'
        $info.Version | Should Be '1.2.0.0'
        $info.Managed | Should Be $true

        $empty = Join-Path $TestDrive 'e'
        New-Item -ItemType Directory -Path $empty -Force | Out-Null
        'x' | Set-Content (Join-Path $empty 'other.txt')
        Compress-Archive -Path (Join-Path $empty '*') -DestinationPath (Join-Path $TestDrive 'e.zip') -Force
        { Get-PackedSolutionInfo -ZipPath (Join-Path $TestDrive 'e.zip') } | Should Throw 'No solution.xml'
    }
}

Describe 'Resolve-PackageImportPlan (T218f — H6 rules for Spaarke environments)' {
    $inst = { param($v, $m) [PSCustomObject]@{ Version = $v; Managed = $m } }

    It 'installs into an environment without SpaarkeMaster' {
        $p = Resolve-PackageImportPlan -Installed $null -PackageVersion '1.2.0.0' -Managed $true
        $p.Action | Should Be 'Install'; $p.StageAndUpgrade | Should Be $false
    }
    It 'stages and upgrades an older managed package; updates an older unmanaged one with a plain import' {
        $m = Resolve-PackageImportPlan -Installed (& $inst '1.1.0.0' $true) -PackageVersion '1.2.0.0' -Managed $true
        $m.Action | Should Be 'Upgrade'; $m.StageAndUpgrade | Should Be $true
        $u = Resolve-PackageImportPlan -Installed (& $inst '1.0.0.0' $false) -PackageVersion '1.2.0.0' -Managed $false
        $u.Action | Should Be 'Update'; $u.StageAndUpgrade | Should Be $false
    }
    It 'does nothing when the same version is installed (missing parts count as 0)' {
        (Resolve-PackageImportPlan -Installed (& $inst '1.2' $false) -PackageVersion '1.2.0.0' -Managed $false).Action | Should Be 'AlreadyCurrent'
    }
    It 'refuses a managed/unmanaged switch in either direction' {
        $a = Resolve-PackageImportPlan -Installed (& $inst '1.0.0.0' $false) -PackageVersion '1.2.0.0' -Managed $true
        $a.Action | Should Be 'Refuse'; $a.Refusal | Should Be 'package-type-mismatch'
        (Resolve-PackageImportPlan -Installed (& $inst '1.0.0.0' $true) -PackageVersion '1.2.0.0' -Managed $false).Refusal | Should Be 'package-type-mismatch'
    }
    It 'refuses an unreadable package version even when nothing is installed (as H6 does)' {
        { Resolve-PackageImportPlan -Installed $null -PackageVersion 'latest' -Managed $true } | Should Throw 'Not a version'
    }
    It 'refuses a downgrade' {
        $d = Resolve-PackageImportPlan -Installed (& $inst '1.10.0.0' $true) -PackageVersion '1.9.0.0' -Managed $true
        $d.Action | Should Be 'Refuse'; $d.Refusal | Should Be 'downgrade-refused'
    }
}

Describe 'Compare-PackageVersion + Get-InstalledPackage (T218f)' {
    It 'compares numerically, not as text' {
        Compare-PackageVersion -A '1.10.0.0' -B '1.9.0.0' | Should Be 1
        Compare-PackageVersion -A '1.2' -B '1.2.0.0' | Should Be 0
        Compare-PackageVersion -A '1.2.0.0' -B '1.2.0.1' | Should Be -1
        { Compare-PackageVersion -A '1.x' -B '1.0' } | Should Throw 'Not a version'
    }
    It 'reads the installed package by unique name, and returns $null when absent' {
        $one = New-FakeGet @{ 'solutions' = @{ Filter = "uniquename eq 'SpaarkeMaster'"; Page = (Page @([PSCustomObject]@{ version = '1.0.0.0'; ismanaged = $false })) } }
        $p = Get-InstalledPackage -Get $one
        $p.Version | Should Be '1.0.0.0'; $p.Managed | Should Be $false
        $none = New-FakeGet @{ 'solutions' = @{ Filter = "uniquename eq 'SpaarkeMaster'"; Page = (Page @()) } }
        Get-InstalledPackage -Get $none | Should Be $null
    }
    It 'lets a failed read throw instead of reporting "not installed"' {
        $broken = { param($endpoint) throw 'HTTP 503' }
        { Get-InstalledPackage -Get $broken } | Should Throw 'HTTP 503'
    }
}

Describe 'Find-EnvironmentVariableValues (T218c value guard)' {
    It 'finds a values file in an unpacked folder and passes a clean one' {
        $dirty = Join-Path $TestDrive 'dirty/environmentvariabledefinitions/sprk_X'
        New-Item -ItemType Directory -Path $dirty -Force | Out-Null
        '{}' | Set-Content (Join-Path $dirty 'environmentvariablevalues.json')
        'x' | Set-Content (Join-Path $dirty 'environmentvariabledefinition.xml')
        @(Find-EnvironmentVariableValues -Path (Join-Path $TestDrive 'dirty')).Count | Should Be 1

        Remove-Item (Join-Path $dirty 'environmentvariablevalues.json')
        @(Find-EnvironmentVariableValues -Path (Join-Path $TestDrive 'dirty')).Count | Should Be 0
    }

    It 'the committed SpaarkeMaster source carries no env-var values (none yet before the first export)' {
        @(Find-EnvironmentVariableValues -Path (Join-Path $repoRoot 'src/dataverse/solutions/SpaarkeMaster')).Count | Should Be 0
    }

    It 'finds a values entry inside a solution zip' {
        $src = Join-Path $TestDrive 'zipsrc/environmentvariabledefinitions/sprk_X'
        New-Item -ItemType Directory -Path $src -Force | Out-Null
        '{}' | Set-Content (Join-Path $src 'environmentvariablevalues.json')
        $zip = Join-Path $TestDrive 'pkg.zip'
        Compress-Archive -Path (Join-Path $TestDrive 'zipsrc/*') -DestinationPath $zip -Force
        @(Find-EnvironmentVariableValues -Path $zip).Count | Should Be 1
    }
}
