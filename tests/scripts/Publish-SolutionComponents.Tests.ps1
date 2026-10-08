# Pester 5. Run: Invoke-Pester tests/scripts/Publish-SolutionComponents.Tests.ps1
# Task 130 (D-83): the scoped-publish module's pure logic, plus the lint that keeps tenant-wide publish out of the repo.
# CI: .github/workflows/scoped-publish-lint.yml

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    . (Join-Path $script:RepoRoot 'scripts' 'lib' 'Publish-SolutionComponents.ps1')
}

Describe 'New-PublishParameterXml' {
    It 'emits entities, web resources, option sets, site maps, dashboards and app modules' {
        $xml = New-PublishParameterXml -Entities sprk_b, sprk_a -WebResources '11111111-1111-1111-1111-111111111111' `
            -OptionSets sprk_choice -SiteMaps '22222222-2222-2222-2222-222222222222' `
            -Dashboards '33333333-3333-3333-3333-333333333333' -AppModules '44444444-4444-4444-4444-444444444444'
        $xml | Should -Be ('<importexportxml><entities><entity>sprk_a</entity><entity>sprk_b</entity></entities>' +
            '<webresources><webresource>{11111111-1111-1111-1111-111111111111}</webresource></webresources>' +
            '<optionsets><optionset>sprk_choice</optionset></optionsets>' +
            '<sitemaps><sitemap>{22222222-2222-2222-2222-222222222222}</sitemap></sitemaps>' +
            '<dashboards><dashboard>{33333333-3333-3333-3333-333333333333}</dashboard></dashboards>' +
            '<appmodules><appmodule>{44444444-4444-4444-4444-444444444444}</appmodule></appmodules></importexportxml>')
    }
    It 'omits empty sections and de-duplicates' {
        New-PublishParameterXml -Entities sprk_a, sprk_a | Should -Be '<importexportxml><entities><entity>sprk_a</entity></entities></importexportxml>'
    }
    It 'normalises guids with or without braces to {guid}' {
        New-PublishParameterXml -AppModules '{44444444-4444-4444-4444-444444444444}' | Should -Match '<appmodule>\{44444444-'
    }
    It 'refuses an empty set (never widens into a publish-all)' {
        { New-PublishParameterXml } | Should -Throw '*Nothing to publish*'
        { New-PublishParameterXml -Entities @('', ' ') } | Should -Throw '*Nothing to publish*'
    }
    It 'rejects a malformed entity name and a malformed guid (no XML injection)' {
        { New-PublishParameterXml -Entities 'a</entity><x>' } | Should -Throw '*Invalid entity name*'
        { New-PublishParameterXml -WebResources 'not-a-guid' } | Should -Throw '*Not a GUID*'
    }
}

Describe 'Resolve-PublishPlan' {
    BeforeAll {
        $script:Lookup = {
            param($kind, $id)
            switch ($kind) {
                'entity' { "ent_$id" } 'optionset' { "opt_$id" } 'view' { 'sprk_event' } 'chart' { 'sprk_event' }
                'form' { if ($id -eq 'dash') { $null } else { 'sprk_matter' } }
                'ribbon' { 'sprk_todo' } 'control' { @('{w1}', '{w2}') } 'controlhosts' { @('sprk_host') }
            }
        }
        function C($t, $id) { [pscustomobject]@{ componenttype = $t; objectid = $id } }
    }
    It 'maps each component type to its bucket' {
        $p = Resolve-PublishPlan -Lookup $script:Lookup -Components @(
            (C 1 'e1'), (C 9 'o1'), (C 26 'v1'), (C 60 'f1'), (C 60 'dash'), (C 50 'r1'), (C 61 'wr'), (C 62 'sm'), (C 80 'app'), (C 2 'attr'), (C 20 'role'))
        @($p.Entities) | Should -Be @('ent_e1', 'sprk_event', 'sprk_matter', 'sprk_todo')
        @($p.OptionSets) | Should -Be @('opt_o1')
        @($p.WebResources) | Should -Be @('wr')
        @($p.SiteMaps) | Should -Be @('sm')
        @($p.Dashboards) | Should -Be @('dash')
        @($p.AppModules) | Should -Be @('app')
        @($p.Unmapped).Count | Should -Be 0
        @($p.Skipped).Count | Should -Be 1
    }
    It 'publishes a custom control through its bundle web resources and NOT its host entities by default' {
        $p = Resolve-PublishPlan -Lookup $script:Lookup -Components @((C 66 'cc'))
        @($p.WebResources) | Should -Be @('{w1}', '{w2}')
        @($p.Entities).Count | Should -Be 0
    }
    It 'adds the host entities of a custom control only when asked' {
        $p = Resolve-PublishPlan -Lookup $script:Lookup -Components @((C 66 'cc')) -IncludeControlHostEntities
        @($p.Entities) | Should -Be @('sprk_host')
    }
    It 'reports an unknown component type instead of ignoring it' {
        $p = Resolve-PublishPlan -Lookup $script:Lookup -Components @((C 9999 'x'))
        @($p.Unmapped) | Should -Be @('9999/x')
    }
}

Describe 'Get-ControlWebResourcePrefix' {
    It 'drops the publisher prefix and adds cc_ (observed on spaarkedev1)' {
        Get-ControlWebResourcePrefix 'sprk_Spaarke.Visuals.VisualHost' | Should -Be 'cc_Spaarke.Visuals.VisualHost/'
    }
}

Describe 'Compare-UnpublishedArtifacts' {
    It 'returns rows that are new or changed in the unpublished copy' {
        $pub = @(@{ id = 'a'; modifiedon = '1'; name = 'A' }, @{ id = 'b'; modifiedon = '1'; name = 'B' })
        $unp = @(@{ id = 'a'; modifiedon = '1'; name = 'A' }, @{ id = 'b'; modifiedon = '2'; name = 'B' }, @{ id = 'c'; modifiedon = '1'; name = 'C' })
        @(Compare-UnpublishedArtifacts -Published $pub -Unpublished $unp | ForEach-Object { $_.id }) | Should -Be @('b', 'c')
    }
    It 'returns nothing when nothing is pending' {
        @(Compare-UnpublishedArtifacts -Published @(@{ id = 'a'; modifiedon = '1' }) -Unpublished @(@{ id = 'a'; modifiedon = '1' })).Count | Should -Be 0
    }
}

Describe 'Publish-SolutionComponents (mocked Dataverse)' {
    It 'posts exactly one PublishXml for the solution components plus extras, and nothing broader' {
        $script:Calls = @()
        Mock Invoke-RestMethod {
            $script:Calls += [pscustomobject]@{ Method = $Method; Uri = "$Uri"; Body = $Body }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @(
                            [pscustomobject]@{ componenttype = 61; objectid = '11111111-1111-1111-1111-111111111111' },
                            [pscustomobject]@{ componenttype = 1; objectid = 'e1' }) } }
                '*/EntityDefinitions(*' { return [pscustomobject]@{ LogicalName = 'sprk_event' } }
                '*savedqueries*' { return [pscustomobject]@{ value = @() } }
                '*systemforms*' { return [pscustomobject]@{ value = @() } }
                '*/webresourceset(*' { return [pscustomobject]@{ content = 'same' } }
                default { return $null }
            }
        }
        $ctx = @{ Api = 'https://x/api/data/v9.2'; Headers = @{} }
        Publish-SolutionComponents -Context $ctx -SolutionUniqueName S -ExtraWebResources '22222222-2222-2222-2222-222222222222' | Out-Null
        $posts = @($script:Calls | Where-Object { $_.Method -eq 'Post' })
        $posts.Count | Should -Be 1
        $posts[0].Uri | Should -Match '/PublishXml$'
        $posts[0].Body | Should -Match 'sprk_event'
        $posts[0].Body | Should -Match '11111111-1111-1111-1111-111111111111'
        $posts[0].Body | Should -Match '22222222-2222-2222-2222-222222222222'
        ($script:Calls.Uri -join ' ') | Should -Not -Match 'PublishAllXml'
    }
    It 'stops (no publish at all) when a component type is unmapped' {
        $script:Calls = @()
        Mock Invoke-RestMethod {
            $script:Calls += [pscustomobject]@{ Method = $Method; Uri = "$Uri" }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 9999; objectid = 'x' }) } }
            }
        }
        { Publish-SolutionComponents -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -SolutionUniqueName S } | Should -Throw '*unmapped*'
        @($script:Calls | Where-Object { $_.Method -eq 'Post' }).Count | Should -Be 0
    }
}

Describe 'Invoke-ScopedSolutionImport guard' {
    It 'rejects --publish-changes before running anything' {
        { Invoke-ScopedSolutionImport -EnvironmentUrl 'https://x' -ZipPath 'a.zip' -SolutionUniqueName S -ImportArgs @('--publish-changes') -PacExe 'pac' -Context @{} } |
            Should -Throw '*tenant-wide publish*'
    }
}

Describe 'no tenant-wide publish anywhere in the repo (D-83)' {
    BeforeAll {
        $banned = 'PublishAllXml|publish-changes|publish-all|pac solution publish'
        # The module and this test must name what they ban; docs/adr is ADR history.
        $out = & git -C $script:RepoRoot grep -n -i -I -E $banned -- scripts .github docs infrastructure src tests `
            ':(exclude)scripts/lib/Publish-SolutionComponents.ps1' ':(exclude)tests/scripts/Publish-SolutionComponents.Tests.ps1' `
            ':(exclude)docs/adr' ':(exclude)*package-lock.json' 2>&1
        $script:Hits = @($out | Where-Object { $_ -and $_ -notmatch '^(warning|fatal):' })
    }
    It 'has zero hits for PublishAllXml, --publish-changes, publish-all, pac solution publish' {
        ($script:Hits -join [Environment]::NewLine) | Should -BeNullOrEmpty
    }
    It 'detects a reintroduced publish-all (the pattern can fail)' {
        'Invoke-RestMethod -Uri "$api/PublishAllXml"' -match 'PublishAllXml|publish-changes|publish-all|pac solution publish' | Should -BeTrue
        'pac solution import --path x --publish-changes' -match 'PublishAllXml|publish-changes|publish-all|pac solution publish' | Should -BeTrue
    }
}
