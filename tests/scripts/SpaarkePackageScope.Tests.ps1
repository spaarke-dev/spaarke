# tests/scripts/SpaarkePackageScope.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 task 218c: the package scope rule (scripts/solution-authoring/
# SpaarkePackageScope.psm1). Proves: every sprk_ web resource is in scope whatever solution it sits in (paged
# results included); intersect tables, managed and non-prefixed components are not; OOB tables contribute only
# their sprk_ columns; roles come from the root business unit by pattern or name; exclusions need a reason and a
# date; the comparison reports what is missing from the package, what is excluded but packaged, and stale
# exclusions; the env-var value guard catches a values file in a folder and in a zip.
#
# Technique: every module function takes the Dataverse GET as a scriptblock, so a fake serves canned pages.
# Pester 3.4 syntax (as Set-AiSpendLimit.Tests.ps1).
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Import-Module (Join-Path $repoRoot 'scripts/solution-authoring/SpaarkePackageScope.psm1') -Force

$script:RootBu = '00000000-0000-0000-0000-0000000000b0'

function New-FakeGet {
    param([hashtable]$Pages)
    # Serves the first page whose key is a prefix of the endpoint.
    return {
        param($endpoint)
        foreach ($k in $Pages.Keys) { if ($endpoint.StartsWith($k)) { return $Pages[$k] } }
        throw "unexpected GET $endpoint"
    }.GetNewClosure()
}

function New-Scope([object[]]$Exclusions = @()) {
    [PSCustomObject]@{
        prefix                = 'sprk_'
        roleNamePattern       = '(?i)^spaarke '
        roleNamesAlsoIncluded = @('Secure Record Owner')
        exclusions            = $Exclusions
    }
}

function Get-DevPages {
    @{
        'EntityDefinitions'              = [PSCustomObject]@{ value = @(
                [PSCustomObject]@{ LogicalName = 'sprk_matter'; MetadataId = 'E1'; IsManaged = $false; IsIntersect = $false; Attributes = @() }
                [PSCustomObject]@{ LogicalName = 'sprk_matter_contact'; MetadataId = 'E2'; IsManaged = $false; IsIntersect = $true; Attributes = @() }
                [PSCustomObject]@{ LogicalName = 'contact'; MetadataId = 'E3'; IsManaged = $true; IsIntersect = $false; Attributes = @(
                        [PSCustomObject]@{ LogicalName = 'sprk_customerid'; MetadataId = 'A1'; IsCustomAttribute = $true; IsManaged = $false; AttributeOf = $null }
                        [PSCustomObject]@{ LogicalName = 'sprk_customeridname'; MetadataId = 'A2'; IsCustomAttribute = $true; IsManaged = $false; AttributeOf = 'sprk_customerid' }
                        [PSCustomObject]@{ LogicalName = 'firstname'; MetadataId = 'A3'; IsCustomAttribute = $false; IsManaged = $true; AttributeOf = $null }
                    ) }
            ) }
        'GlobalOptionSetDefinitions'     = [PSCustomObject]@{ value = @(
                [PSCustomObject]@{ Name = 'sprk_status'; MetadataId = 'O1'; IsManaged = $false }
                [PSCustomObject]@{ Name = 'msdyn_x'; MetadataId = 'O2'; IsManaged = $true }
            ) }
        'webresourceset'                 = [PSCustomObject]@{ value = @(
                [PSCustomObject]@{ webresourceid = 'W1'; name = 'sprk_spaarkeai' }
            ); '@odata.nextLink' = 'https://dev/api/data/v9.2/webresourceset?page2' }
        'https://dev/api/data/v9.2/webresourceset?page2' = [PSCustomObject]@{ value = @(
                [PSCustomObject]@{ webresourceid = 'W2'; name = 'sprk_dailyupdate' }
            ) }
        'customcontrols'                 = [PSCustomObject]@{ value = @(
                [PSCustomObject]@{ customcontrolid = 'C1'; name = 'sprk_Spaarke.Records.MatterHeader' }
                [PSCustomObject]@{ customcontrolid = 'C2'; name = 'sprk_Spaarke.Controls.DueDatesWidget' }
            ) }
        'appmodules'                     = [PSCustomObject]@{ value = @([PSCustomObject]@{ appmoduleid = 'M1'; uniquename = 'sprk_MatterManagement' }) }
        'sitemaps'                       = [PSCustomObject]@{ value = @() }
        'environmentvariabledefinitions' = [PSCustomObject]@{ value = @([PSCustomObject]@{ environmentvariabledefinitionid = 'V1'; schemaname = 'sprk_BffApiBaseUrl' }) }
        'businessunits'                  = [PSCustomObject]@{ value = @([PSCustomObject]@{ businessunitid = $script:RootBu }) }
        'roles'                          = [PSCustomObject]@{ value = @(
                [PSCustomObject]@{ roleid = 'R1'; name = 'Spaarke Core User' }
                [PSCustomObject]@{ roleid = 'R2'; name = 'Secure Record Owner' }
                [PSCustomObject]@{ roleid = 'R3'; name = 'System Customizer' }
            ) }
    }
}

Describe 'Get-PackageRuleComponents (T218c)' {
    $rule = Get-PackageRuleComponents -Get (New-FakeGet (Get-DevPages)) -Scope (New-Scope)
    $names = @($rule | ForEach-Object Name)

    It 'puts every sprk_ web resource in scope, across pages — including code pages in no Spaarke solution' {
        ($names -contains 'sprk_spaarkeai') | Should Be $true
        ($names -contains 'sprk_dailyupdate') | Should Be $true
    }

    It 'takes custom tables whole, skips intersect tables, and takes only the sprk_ columns of OOB tables' {
        ($names -contains 'sprk_matter') | Should Be $true
        ($names -contains 'sprk_matter_contact') | Should Be $false
        ($names -contains 'contact.sprk_customerid') | Should Be $true
        ($names -contains 'contact.sprk_customeridname') | Should Be $false
        ($names -contains 'contact.firstname') | Should Be $false
        ($rule | Where-Object Name -eq 'contact.sprk_customerid').ParentEntityId | Should Be 'e3'
    }

    It 'skips managed and non-prefixed components' {
        ($names -contains 'msdyn_x') | Should Be $false
        ($names -contains 'sprk_status') | Should Be $true
    }

    It 'takes root-unit roles by pattern or by name, nothing else' {
        ($names -contains 'Spaarke Core User') | Should Be $true
        ($names -contains 'Secure Record Owner') | Should Be $true
        ($names -contains 'System Customizer') | Should Be $false
    }

    It 'adds nothing for a type with no components (empty page)' {
        @($rule | Where-Object TypeName -eq 'SiteMap').Count | Should Be 0
    }

    It 'lowercases object ids and carries the component type code' {
        $wr = $rule | Where-Object Name -eq 'sprk_spaarkeai'
        $wr.ObjectId | Should Be 'w1'
        $wr.ComponentType | Should Be 61
    }
}

Describe 'Read-PackageScope + exclusions (T218c)' {
    It 'marks an excluded component with its reason' {
        $scope = New-Scope @([PSCustomObject]@{ type = 'CustomControl'; name = 'sprk_Spaarke.Controls.DueDatesWidget'; reason = 'not on a form'; date = '2026-08-21' })
        $rule = Get-PackageRuleComponents -Get (New-FakeGet (Get-DevPages)) -Scope $scope
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
    $scope = New-Scope @(
        [PSCustomObject]@{ type = 'CustomControl'; name = 'sprk_Spaarke.Controls.DueDatesWidget'; reason = 'r'; date = '2026-08-21' }
        [PSCustomObject]@{ type = 'Role'; name = 'Gone Role'; reason = 'r'; date = '2026-08-21' }
    )
    $rule = Get-PackageRuleComponents -Get (New-FakeGet (Get-DevPages)) -Scope $scope
    $membership = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in '1|e1', '2|a1', '9|o1', '61|w2', '66|c1', '66|c2', '80|m1', '380|v1', '20|r1', '20|r2') { [void]$membership.Add($k) }
    $result = Compare-PackageScope -RuleComponents $rule -MembershipKeys $membership -Scope $scope

    It 'reports the code page missing from the package' {
        @($result.MissingFromPackage | ForEach-Object Name) | Should Be @('sprk_spaarkeai')
    }

    It 'reports an excluded component that is packaged anyway' {
        @($result.ExcludedButInPackage | ForEach-Object Name) | Should Be @('sprk_Spaarke.Controls.DueDatesWidget')
    }

    It 'reports an exclusion that matches nothing (stale)' {
        @($result.UnmatchedExclusions | ForEach-Object name) | Should Be @('Gone Role')
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
