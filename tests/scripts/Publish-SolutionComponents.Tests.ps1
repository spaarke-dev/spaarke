# Pester 5. Run: Invoke-Pester tests/scripts/Publish-SolutionComponents.Tests.ps1
# Task 130 (D-83): the scoped-publish module's pure logic, plus the lint that keeps tenant-wide publish out of the repo.
# CI: .github/workflows/scoped-publish-lint.yml

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    . (Join-Path $script:RepoRoot 'scripts' 'lib' 'Publish-SolutionComponents.ps1')
    # The banned forms. Case-insensitive, PCRE. Every form has a bypass sample below that must match and a benign sample that must not.
    $script:BannedPatterns = @(
        'PublishAll'                                  # the all-customizations action, also when split: "PublishAll" + "Xml"
        'publish[-_]all'
        'pac(\.exe|\.cmd)?\s+solution\s+publish'      # also with doubled spaces
        'pac(\.exe|\.cmd)?\s+org\s+publish'
        '--publish-changes'
        'solution\s+import\b.*\s-pc\b'                # short flag on the same line
        '^\s*-pc\s*[`\\]?\s*$'                        # short flag on a continuation line
        'Publish-CrmAllCustomization'
        'powerplatform-actions/publish-solution|uses:\s*\S*publish-solution(?!\w)'   # GitHub Action
        'PowerPlatformPublishCustomizations'          # Azure DevOps task
        'publish\s+all\s+customi[sz]ations'           # manual steps
        'save\s*(\+|and)\s*publish\s+all'
    )
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
                'ribbon' { 'sprk_todo' } 'appsetting' { if ($id -eq 'orphan') { $null } else { '{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}' } } 'control' { @('{w1}', '{w2}') } 'controlhosts' { @('sprk_host') }
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

Describe 'app settings (type 10075)' {
    It 'publishes the parent app module and reads the setting back' {
        $lookup = { param($kind, $id) if ($kind -eq 'appsetting') { '{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}' } }
        $p = Resolve-PublishPlan -Lookup $lookup -Components @([pscustomobject]@{ componenttype = 10075; objectid = 's1' })
        @($p.AppModules) | Should -Be @('{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}')
        @($p.AppSettings) | Should -Be @('s1')
        @($p.Unmapped).Count | Should -Be 0
    }
    It 'is unmapped when the setting has no parent app module' {
        $lookup = { param($kind, $id) $null }
        $p = Resolve-PublishPlan -Lookup $lookup -Components @([pscustomobject]@{ componenttype = 10075; objectid = 's1' })
        @($p.Unmapped).Count | Should -Be 1
    }
}

Describe 'application ribbon, whole path (plan to ParameterXml)' {
    It 'a plan holding only an application ribbon produces the ribbons element and does not throw' {
        $p = Resolve-PublishPlan -Lookup { param($kind, $id) $null } -Components @([pscustomobject]@{ componenttype = 50; objectid = 'r' })
        New-PublishParameterXmlFromPlan -Plan $p | Should -Be '<importexportxml><ribbons><ribbon /></ribbons></importexportxml>'
    }
    It 'Publish-SolutionComponents posts it for a solution that holds only an application ribbon' {
        $script:Calls = @()
        Mock Invoke-RestMethod {
            $script:Calls += [pscustomobject]@{ Method = $Method; Uri = "$Uri"; Body = $Body }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 50; objectid = 'r1' }) } }
                '*/ribboncustomizations(*' { return [pscustomobject]@{ entity = $null } }
                default { return $null }
            }
        }
        { Publish-SolutionComponents -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -SolutionUniqueName S -SkipCollateralCheck | Out-Null } | Should -Not -Throw
        $post = @($script:Calls | Where-Object { $_.Method -eq 'Post' })
        $post.Count | Should -Be 1
        $post[0].Body | Should -Match 'ribbons'
    }
}

Describe 'Split-PublishPlan (chunking)' {
    It 'keeps every request at or under the chunk size and loses nothing' {
        $ents = 1..60 | ForEach-Object { "sprk_e$_" }
        $webs = 1..30 | ForEach-Object { [guid]::NewGuid().ToString() }
        $chunks = @(Split-PublishPlan -Entities $ents -WebResources $webs -AppModules @([guid]::NewGuid().ToString()) -ChunkSize 25)
        $chunks.Count | Should -Be 4
        foreach ($c in $chunks) { (@($c.Entities).Count + @($c.WebResources).Count + @($c.AppModules).Count) | Should -BeLessOrEqual 25 }
        (($chunks | ForEach-Object { @($_.Entities).Count } | Measure-Object -Sum).Sum) | Should -Be 60
        (($chunks | ForEach-Object { @($_.WebResources).Count } | Measure-Object -Sum).Sum) | Should -Be 30
    }
    It 'puts the application ribbon in a chunk and attaches app settings to the last chunk' {
        $chunks = @(Split-PublishPlan -Entities @('sprk_a') -AppSettings @('s1') -ApplicationRibbon -ChunkSize 25)
        $chunks.Count | Should -Be 1
        $chunks[0].ApplicationRibbon | Should -BeTrue
        @($chunks[0].AppSettings) | Should -Be @('s1')
    }
    It 'returns no chunks for an empty plan' { @(Split-PublishPlan).Count | Should -Be 0 }
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


Describe 'dot-sourcing the module does not change the caller (review F1)' {
    It 'leaves strict mode off: reading a missing property returns $null instead of throwing' {
        $mod = Join-Path $script:RepoRoot 'scripts' 'lib' 'Publish-SolutionComponents.ps1'
        $out = & pwsh -NoProfile -Command ". '$mod'; `$o = [pscustomobject]@{ value = 1 }; if (`$null -eq `$o.'@odata.nextLink') { 'ok' }" 2>&1
        ($out -join '') | Should -Be 'ok'
    }
}

Describe 'application ribbon (type 50 without an entity)' {
    It 'is published as an empty ribbon element in a ribbons section' {
        $lookup = { param($kind, $id) $null }
        $p = Resolve-PublishPlan -Lookup $lookup -Components @([pscustomobject]@{ componenttype = 50; objectid = 'r' })
        $p.ApplicationRibbon | Should -BeTrue
        New-PublishParameterXml -ApplicationRibbon | Should -Be '<importexportxml><ribbons><ribbon /></ribbons></importexportxml>'
    }
}

Describe 'component type knowledge' {
    It 'knows the SpaarkeMaster types (70, 10075, 10139, 10141, 10314) and the mapped ones' {
        foreach ($t in 1, 2, 9, 10, 26, 50, 59, 60, 61, 62, 66, 80, 10075, 20, 46, 70, 71, 380, 10139, 10141, 10314) { Test-ComponentTypeKnown $t | Should -BeTrue }
    }
    It 'does not know an arbitrary type' { Test-ComponentTypeKnown 99999 | Should -BeFalse }
    It 'keeps types without a 404 proof unmapped (workflows/flows, SLA family, connectors)' {
        foreach ($t in 29, 150, 151, 152, 153, 154, 371, 372) { Test-ComponentTypeKnown $t | Should -BeFalse }
    }
}

Describe 'allow-list' {
    It 'contains exactly the three skill paths (change this test deliberately to add or remove one)' {
        $lines = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'publish-lint-allowlist.txt') | Where-Object { $_ -and $_ -notmatch '^\s*#' } | ForEach-Object { ($_ -split "`t")[0].Trim() })
        $lines | Sort-Object | Should -Be @('.claude/skills/dataverse-deploy/SKILL.md', '.claude/skills/pcf-deploy/SKILL.md', '.claude/skills/ribbon-edit/SKILL.md')
    }
}

Describe 'Get-ZipSolutionInfo (customizations.xml and folders)' {
    BeforeAll {
        function New-TestZip([string]$Customizations, [string[]]$Folders) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zp = Join-Path ([IO.Path]::GetTempPath()) ("pscz-" + [guid]::NewGuid() + ".zip")
            $z = [IO.Compression.ZipFile]::Open($zp, 'Create')
            try {
                $w = New-Object IO.StreamWriter($z.CreateEntry('solution.xml').Open()); $w.Write('<ImportExportXml><SolutionManifest><UniqueName>S</UniqueName><Managed>0</Managed><RootComponents><RootComponent type="1" schemaName="x" /></RootComponents></SolutionManifest></ImportExportXml>'); $w.Dispose()
                $w = New-Object IO.StreamWriter($z.CreateEntry('customizations.xml').Open()); $w.Write($Customizations); $w.Dispose()
                foreach ($f in $Folders) { $w = New-Object IO.StreamWriter($z.CreateEntry("$f/a.xml").Open()); $w.Write('<a/>'); $w.Dispose() }
            } finally { $z.Dispose() }
            return $zp
        }
    }
    It 'adds subcomponent types from customizations.xml elements and top-level folders' {
        $zp = New-TestZip '<ImportExportXml><Roles><Role /></Roles><Languages><Language>1033</Language></Languages><Workflows /></ImportExportXml>' @('environmentvariabledefinitions', 'appsettings')
        try {
            $i = Get-ZipSolutionInfo -ZipPath $zp
            @($i.AllTypes) | Should -Be @(1, 20, 380, 10075)
            @($i.UnknownParts).Count | Should -Be 0
        } finally { Remove-Item $zp -Force }
    }
    It 'reports a non-empty element or folder it cannot map, and Invoke-ImportPreflight refuses before importing' {
        $zp = New-TestZip '<ImportExportXml><Templates><Template /></Templates></ImportExportXml>' @('mysterydata')
        try {
            $i = Get-ZipSolutionInfo -ZipPath $zp
            @($i.UnknownParts) | Should -Be @('customizations.xml/Templates', 'folder/mysterydata')
            Mock Get-SolutionComponentRows { $null }
            { Invoke-ImportPreflight -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -ZipPath $zp -SolutionUniqueName S } | Should -Throw '*Nothing was imported*'
        } finally { Remove-Item $zp -Force }
    }
}

Describe 'Get-ZipSolutionInfo' {
    It 'reads managed flag, root types and root entities from solution.xml' {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zipPath = Join-Path ([IO.Path]::GetTempPath()) ("pscz-" + [guid]::NewGuid() + ".zip")
        $xml = '<ImportExportXml><SolutionManifest><UniqueName>S1</UniqueName><Managed>1</Managed><RootComponents>' +
            '<RootComponent type="1" schemaName="Sprk_Event" behavior="0" /><RootComponent type="10075" schemaName="x" behavior="0" /></RootComponents></SolutionManifest></ImportExportXml>'
        $z = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
        try { $w = New-Object IO.StreamWriter($z.CreateEntry('solution.xml').Open()); $w.Write($xml); $w.Dispose() } finally { $z.Dispose() }
        try {
            $i = Get-ZipSolutionInfo -ZipPath $zipPath
            $i.UniqueName | Should -Be 'S1'
            $i.Managed | Should -BeTrue
            @($i.RootTypes) | Should -Be @(1, 10075)
            @($i.RootEntities) | Should -Be @('sprk_event')
        } finally { Remove-Item $zipPath -Force -ErrorAction SilentlyContinue }
    }
}

Describe 'Publish-SolutionComponents (mocked Dataverse)' {
    BeforeEach { $script:Calls = @() }
    It 'posts exactly one PublishXml for the solution components plus extras, and nothing broader' {
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
                '*savedqueryvisualizations*' { return [pscustomobject]@{ value = @() } }
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
        ($script:Calls.Uri -join ' ') | Should -Not -Match 'PublishAll'
    }
    It 'stops (no publish at all) when a component type is unmapped' {
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
    It 'throws when the read-back still finds unpublished content (mutation M4)' {
        Mock Invoke-RestMethod {
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 61; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                '*RetrieveUnpublished*' { return [pscustomobject]@{ content = 'NEW' } }
                '*/webresourceset(*' { return [pscustomobject]@{ content = 'OLD' } }
                default { return $null }
            }
        }
        { Publish-SolutionComponents -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -SolutionUniqueName S -SkipCollateralCheck } | Should -Throw '*still unpublished*'
    }
}

Describe 'read-back covers entities and site maps (round-4 fix-now)' {
    It 'reports an entity whose view or form is still unpublished after the publish' {
        Mock Invoke-RestMethod {
            if ("$Uri" -match 'systemforms.*RetrieveUnpublishedMultiple') {
                return [pscustomobject]@{ value = @([pscustomobject]@{ formid = 'F1'; name = 'Main'; modifiedon = '9' }) }
            }
            return [pscustomobject]@{ value = @() }
        }
        $r = @(Test-PublishedReadBack -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Plan ([pscustomobject]@{ Entities = @('sprk_event') }))
        $r.Count | Should -Be 1
        $r[0] | Should -Match 'entity sprk_event'
    }
    It 'compares charts (savedqueryvisualization) as well as views and forms' {
        Mock Invoke-RestMethod {
            if ("$Uri" -match 'savedqueryvisualizations.*RetrieveUnpublishedMultiple') {
                return [pscustomobject]@{ value = @([pscustomobject]@{ savedqueryvisualizationid = 'C1'; name = 'Chart'; modifiedon = '9' }) }
            }
            return [pscustomobject]@{ value = @() }
        }
        $r = @(Get-EntityPublishCollateral -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Entity sprk_event)
        $r.Count | Should -Be 1
        $r[0] | Should -Match 'savedqueryvisualizations'
    }
    It 'reports nothing for an entity with no pending items' {
        Mock Invoke-RestMethod { [pscustomobject]@{ value = @() } }
        @(Test-PublishedReadBack -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Plan ([pscustomobject]@{ Entities = @('sprk_event') })).Count | Should -Be 0
    }
    It 'reports a site map whose published copy differs from the unpublished one' {
        Mock Invoke-RestMethod {
            if ("$Uri" -match 'RetrieveUnpublished') { return [pscustomobject]@{ sitemapxml = '<new/>'; modifiedon = '2' } }
            return [pscustomobject]@{ sitemapxml = '<old/>'; modifiedon = '1' }
        }
        $r = @(Test-PublishedReadBack -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Plan ([pscustomobject]@{ SiteMaps = @('{11111111-1111-1111-1111-111111111111}') }))
        $r | Should -Be @('sitemap 11111111-1111-1111-1111-111111111111')
    }
    It 'reports nothing for a published site map' {
        Mock Invoke-RestMethod { [pscustomobject]@{ sitemapxml = '<same/>'; modifiedon = '1' } }
        @(Test-PublishedReadBack -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Plan ([pscustomobject]@{ SiteMaps = @('{11111111-1111-1111-1111-111111111111}') })).Count | Should -Be 0
    }
    It 'Publish-SolutionComponents fails when a site map is still unpublished after the request' {
        Mock Invoke-RestMethod {
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 62; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                '*RetrieveUnpublished*' { return [pscustomobject]@{ sitemapxml = '<new/>'; modifiedon = '2' } }
                '*/sitemaps(*' { return [pscustomobject]@{ sitemapxml = '<old/>'; modifiedon = '1' } }
                default { return $null }
            }
        }
        { Publish-SolutionComponents -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -SolutionUniqueName S -SkipCollateralCheck } | Should -Throw '*sitemap*'
    }
}

Describe 'dashboards read-back (round-5 K1)' {
    It 'reports a dashboard whose published copy differs from the unpublished one' {
        Mock Invoke-RestMethod {
            if ("$Uri" -match 'RetrieveUnpublished') { return [pscustomobject]@{ formxml = '<new/>'; modifiedon = '2' } }
            return [pscustomobject]@{ formxml = '<old/>'; modifiedon = '1' }
        }
        $r = @(Test-PublishedReadBack -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Plan ([pscustomobject]@{ Dashboards = @('{11111111-1111-1111-1111-111111111111}') }))
        $r | Should -Be @('dashboard 11111111-1111-1111-1111-111111111111')
    }
    It 'reports nothing for a published dashboard' {
        Mock Invoke-RestMethod { [pscustomobject]@{ formxml = '<same/>'; modifiedon = '1' } }
        @(Test-PublishedReadBack -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Plan ([pscustomobject]@{ Dashboards = @('{11111111-1111-1111-1111-111111111111}') })).Count | Should -Be 0
    }
    It 'Publish-SolutionComponents fails when a dashboard is still unpublished after the request' {
        Mock Invoke-RestMethod {
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 60; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                '*/systemforms(*RetrieveUnpublished*' { return [pscustomobject]@{ formxml = '<new/>'; modifiedon = '2'; objecttypecode = 'none'; type = 0 } }
                '*/systemforms(*' { return [pscustomobject]@{ formxml = '<old/>'; modifiedon = '1'; objecttypecode = 'none'; type = 0 } }
                default { return $null }
            }
        }
        { Publish-SolutionComponents -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -SolutionUniqueName S -SkipCollateralCheck } | Should -Throw '*dashboard*'
    }
}

Describe 'resume command carries components published outside the solution (round-5 K2)' {
    It 'names them and passes them as -ExtraWebResources / -ExtraEntities' {
        Mock Invoke-RestMethod {
            if ("$Method" -eq 'Post') { throw 'boom' }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 61; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                default { return $null }
            }
        }
        $err = $null
        try {
            Publish-SolutionComponents -Context @{ Api = 'https://org.crm.dynamics.com/api/data/v9.2'; Headers = @{} } -SolutionUniqueName MySol -SkipCollateralCheck `
                -ExtraWebResources @('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111') -ExtraEntities @('sprk_event')
        } catch { $err = $_.Exception.Message }
        $err | Should -Match "-ExtraWebResources '22222222-2222-2222-2222-222222222222'"
        $err | Should -Not -Match "-ExtraWebResources '[^ ]*11111111-1111"
        $err | Should -Match "-ExtraEntities 'sprk_event'"
        $err | Should -Match 'web resources outside the solution: 22222222'
    }
    It 'adds nothing when every extra is already in the solution' {
        Mock Invoke-RestMethod {
            if ("$Method" -eq 'Post') { throw 'boom' }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 61; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                default { return $null }
            }
        }
        $err = $null
        try { Publish-SolutionComponents -Context @{ Api = 'https://org.crm.dynamics.com/api/data/v9.2'; Headers = @{} } -SolutionUniqueName MySol -SkipCollateralCheck -ExtraWebResources @('11111111-1111-1111-1111-111111111111') } catch { $err = $_.Exception.Message }
        $err | Should -Not -Match '-ExtraWebResources'
    }
}

Describe 'extras: one printed form, validated on entry (round-6 F4/K5)' {
    BeforeAll {
        # Emulate bash quote removal on the printed command: split on spaces outside single quotes, drop the quotes.
        function Split-LikeBash([string]$line) {
            $tok = @(); $cur = ''; $q = $false; $has = $false
            foreach ($ch in $line.ToCharArray()) {
                if ($ch -eq "'") { $q = -not $q; $has = $true; continue }
                if ($ch -eq ' ' -and -not $q) { if ($has -or $cur) { $tok += $cur }; $cur = ''; $has = $false; continue }
                $cur += $ch; $has = $true
            }
            if ($has -or $cur) { $tok += $cur }
            return $tok
        }
        $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'Import-SolutionScoped.ps1'
    }
    It 'the printed resume command, run through the real script parameter binding, yields every extra (two or more)' {
        Mock Invoke-RestMethod {
            if ("$Method" -eq 'Post') { throw 'boom' }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 61; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                default { return $null }
            }
        }
        $err = $null
        try {
            Publish-SolutionComponents -Context @{ Api = 'https://org.crm.dynamics.com/api/data/v9.2'; Headers = @{} } -SolutionUniqueName MySol -SkipCollateralCheck `
                -ExtraWebResources @('22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333') -ExtraEntities @('sprk_event', 'sprk_todo')
        } catch { $err = $_.Exception.Message }
        $cmd = [regex]::Match($err, 'pwsh scripts/Import-SolutionScoped\.ps1[^()]*?(?= \(this run| *$)').Value
        $cmd | Should -Match "-ExtraWebResources '22222222-2222-2222-2222-222222222222,33333333-3333-3333-3333-333333333333'"
        $cmd | Should -Match "-ExtraEntities 'sprk_event,sprk_todo'"
        $args2 = @(Split-LikeBash $cmd | Select-Object -Skip 2) + '-CheckArguments'
        $out = & pwsh -NoProfile -File $script:ScriptPath @args2 2>&1
        $LASTEXITCODE | Should -Be 0
        $r = ($out -join '') | ConvertFrom-Json
        @($r.ExtraWebResources) | Should -Be @('22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333')
        @($r.ExtraEntities) | Should -Be @('sprk_event', 'sprk_todo')
    }
    It 'accepts the comma list, the PowerShell array form, spaces and empty entries' {
        $out = & pwsh -NoProfile -File $script:ScriptPath -EnvironmentUrl https://x -SolutionUniqueName S -ExtraEntities ' sprk_a , ,sprk_b' -CheckArguments 2>&1
        $LASTEXITCODE | Should -Be 0
        @((($out -join '') | ConvertFrom-Json).ExtraEntities) | Should -Be @('sprk_a', 'sprk_b')
    }
    It 'rejects a quote-containing or malformed value before any import or publish, with a clear error' {
        $out = & pwsh -NoProfile -File $script:ScriptPath -EnvironmentUrl https://x -SolutionUniqueName S -ExtraWebResources "11111111-1111-1111-1111-111111111111'; calc" -PublishOnly 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($out -join ' ') | Should -Match 'Invalid web resource id'
        $out = & pwsh -NoProfile -File $script:ScriptPath -EnvironmentUrl https://x -SolutionUniqueName S -ExtraEntities "Sprk_Event','x" -CheckArguments 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($out -join ' ') | Should -Match 'Invalid entity logical name'
    }
    It 'Publish-SolutionComponents validates too, so a bad value never reaches a REST call or the resume command' {
        $script:Rest = 0
        Mock Invoke-RestMethod { $script:Rest++ ; $null }
        $err = $null
        try { Publish-SolutionComponents -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -SolutionUniqueName S -ExtraWebResources "a'; evil" } catch { $err = $_.Exception.Message }
        $err | Should -Match 'Invalid web resource id'
        $err | Should -Not -Match 'resume'
        $script:Rest | Should -Be 0
    }
    It 'the full-import path (-ZipPath) passes both extras on to Invoke-ScopedSolutionImport (K6)' {
        # Run a COPY of the real script next to a stub lib: the stub loads the real module, then replaces the two functions that
        # reach Dataverse/pac with recorders. The script text under test is unchanged.
        $dir = Join-Path ([IO.Path]::GetTempPath()) ("iss-" + [guid]::NewGuid())
        New-Item -ItemType Directory -Path (Join-Path $dir 'lib') | Out-Null
        try {
            Copy-Item -LiteralPath $script:ScriptPath -Destination (Join-Path $dir 'Import-SolutionScoped.ps1')
            $real = Join-Path $script:RepoRoot 'scripts' 'lib' 'Publish-SolutionComponents.ps1'
            Set-Content -LiteralPath (Join-Path $dir 'lib' 'Publish-SolutionComponents.ps1') -Value @(
                ". '$real'",
                'function Get-DataverseApiContext { @{ Api = ''https://x/api/data/v9.2''; Headers = @{} } }',
                'function Invoke-ScopedSolutionImport { param($EnvironmentUrl, $ZipPath, $SolutionUniqueName, $PacExe, $ImportArgs, $Context, $ExtraWebResources, $ExtraEntities, [switch]$IncludeControlHostEntities, [switch]$AllowWorkflows)',
                '  [pscustomobject]@{ Zip = $ZipPath; Webs = @($ExtraWebResources); Ents = @($ExtraEntities) } | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:ISS_RECORD }')
            $zip = Join-Path $dir 'a.zip'; Set-Content -LiteralPath $zip -Value 'x'
            $env:ISS_RECORD = Join-Path $dir 'recorded.json'
            $out = & pwsh -NoProfile -File (Join-Path $dir 'Import-SolutionScoped.ps1') -EnvironmentUrl https://x -SolutionUniqueName S -ZipPath $zip `
                -ExtraWebResources '22222222-2222-2222-2222-222222222222,33333333-3333-3333-3333-333333333333' -ExtraEntities 'sprk_event,sprk_todo' 2>&1
            $LASTEXITCODE | Should -Be 0
            Test-Path -LiteralPath $env:ISS_RECORD | Should -BeTrue
            $r = Get-Content -LiteralPath $env:ISS_RECORD -Raw | ConvertFrom-Json
            @($r.Webs) | Should -Be @('22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333')
            @($r.Ents) | Should -Be @('sprk_event', 'sprk_todo')
        } finally { Remove-Item Env:\ISS_RECORD -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'ConvertTo-ExtraList normalises braces and case and removes duplicates' {
        @(ConvertTo-ExtraList -Values @('{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa') -Kind WebResource) | Should -Be @('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    }
}

Describe 'request order and resume command' {
    It 'puts option sets and web resources first, entities next, app modules last' {
        $chunks = @(Split-PublishPlan -Entities @('sprk_a', 'sprk_b') -WebResources @('11111111-1111-1111-1111-111111111111') -OptionSets @('sprk_o') `
                -SiteMaps @('22222222-2222-2222-2222-222222222222') -AppModules @('33333333-3333-3333-3333-333333333333') -ChunkSize 2)
        $flat = @(foreach ($c in $chunks) { foreach ($k in 'OptionSets', 'WebResources', 'Entities', 'SiteMaps', 'AppModules') { foreach ($v in @($c[$k])) { $k } } })
        $flat | Should -Be @('OptionSets', 'WebResources', 'Entities', 'Entities', 'SiteMaps', 'AppModules')
    }
    It 'prints the exact resume command (no re-import) when a request fails' {
        Mock Invoke-RestMethod {
            if ("$Method" -eq 'Post') { throw 'request timed out' }
            switch -Wildcard ("$Uri") {
                '*/solutions?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ solutionid = 's1' }) } }
                '*/solutioncomponents?*' { return [pscustomobject]@{ value = @([pscustomobject]@{ componenttype = 61; objectid = '11111111-1111-1111-1111-111111111111' }) } }
                default { return $null }
            }
        }
        $err = $null
        try { Publish-SolutionComponents -Context @{ Api = 'https://org.crm.dynamics.com/api/data/v9.2'; Headers = @{} } -SolutionUniqueName MySol -SkipCollateralCheck } catch { $err = $_.Exception.Message }
        $err | Should -Match 'request 1 of 1'
        $err | Should -Match 'pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://org.crm.dynamics.com -SolutionUniqueName MySol -PublishOnly'
        $err | Should -Match 'WITHOUT re-importing'
    }
}

Describe 'workflows (type 29)' {
    It 'are refused by default and accepted only with -AllowWorkflows' {
        Test-ComponentTypeKnown 29 | Should -BeFalse
        Test-ComponentTypeKnown 29 -AllowWorkflows | Should -BeTrue
        $lookup = { param($kind, $id) $null }
        @((Resolve-PublishPlan -Lookup $lookup -Components @([pscustomobject]@{ componenttype = 29; objectid = 'w1' })).Unmapped).Count | Should -Be 1
        $p = Resolve-PublishPlan -Lookup $lookup -Components @([pscustomobject]@{ componenttype = 29; objectid = 'w1' }) -AllowWorkflows
        @($p.Unmapped).Count | Should -Be 0
        @($p.Workflows) | Should -Be @('w1')
    }
    It 'Test-WorkflowsActivated returns the workflows whose statecode is not 1' {
        Mock Invoke-RestMethod { if ("$Uri" -match 'wf-ok') { [pscustomobject]@{ statecode = 1; name = 'ok' } } else { [pscustomobject]@{ statecode = 0; name = 'draft' } } }
        $r = @(Test-WorkflowsActivated -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -WorkflowIds @('wf-ok', 'wf-bad'))
        $r.Count | Should -Be 1
        $r[0] | Should -Match 'wf-bad'
    }
    It 'the import adds --activate-plugins only with -AllowWorkflows' {
        Mock Invoke-ImportPreflight { }
        $script:PacArgs = $null
        function global:pac-fake { $script:PacArgs = $args; $global:LASTEXITCODE = 0 }
        Mock Publish-SolutionComponents { [pscustomobject]@{} }
        Invoke-ScopedSolutionImport -EnvironmentUrl 'https://x' -ZipPath 'a.zip' -SolutionUniqueName S -PacExe 'pac-fake' -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -AllowWorkflows | Out-Null
        ($script:PacArgs -join ' ') | Should -Match '--activate-plugins'
        Invoke-ScopedSolutionImport -EnvironmentUrl 'https://x' -ZipPath 'a.zip' -SolutionUniqueName S -PacExe 'pac-fake' -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } | Out-Null
        ($script:PacArgs -join ' ') | Should -Not -Match '--activate-plugins'
        Remove-Item function:global:pac-fake
    }
}

Describe 'app setting parent outside the solution (pre-flight warning)' {
    It 'lists the parent app''s pending changes and says it is outside the solution' {
        Mock Get-ZipSolutionInfo { [pscustomobject]@{ UniqueName = 'S'; Managed = $false; RootTypes = @(10075); AllTypes = @(10075); UnknownParts = @(); RootEntities = @(); AppSettingParents = @() } }
        Mock Get-SolutionComponentRows { @([pscustomobject]@{ componenttype = 10075; objectid = 's1' }) }
        Mock Get-AppSettingParent { '{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}' }
        Mock Get-AppPendingChanges { @('app setting theirs (x)') }
        $w = $null
        Invoke-ImportPreflight -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -ZipPath 'a.zip' -SolutionUniqueName S -WarningVariable w -WarningAction SilentlyContinue | Out-Null
        ($w -join ' ') | Should -Match 'OUTSIDE the solution'
        ($w -join ' ') | Should -Match 'app setting theirs'
    }
    It 'Get-AppPendingChanges compares the app module and its settings against RetrieveUnpublished' {
        Mock Invoke-RestMethod {
            switch -Wildcard ("$Uri") {
                '*appmodules(*RetrieveUnpublished*' { return [pscustomobject]@{ name = 'App'; modifiedon = '2' } }
                '*appmodules(*' { return [pscustomobject]@{ name = 'App'; modifiedon = '1' } }
                '*appsettings/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple*' { return [pscustomobject]@{ value = @([pscustomobject]@{ appsettingid = 'S9'; displayname = 'Theme'; modifiedon = '5' }) } }
                default { return [pscustomobject]@{ value = @() } }
            }
        }
        $r = @(Get-AppPendingChanges -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -AppId '{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}')
        $r.Count | Should -Be 2
        ($r -join ' ') | Should -Match 'app module App'
        ($r -join ' ') | Should -Match 'app setting Theme'
    }
}

Describe 'Get-EntityPublishCollateral' {
    It 'leaves out the solution''s own pending items' {
        Mock Invoke-RestMethod {
            if ("$Uri" -match 'savedqueries.*RetrieveUnpublishedMultiple') {
                return [pscustomobject]@{ value = @(
                        [pscustomobject]@{ savedqueryid = 'AAA'; name = 'mine'; modifiedon = '2' },
                        [pscustomobject]@{ savedqueryid = 'BBB'; name = 'theirs'; modifiedon = '2' }) }
            }
            return [pscustomobject]@{ value = @() }
        }
        $r = @(Get-EntityPublishCollateral -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } -Entity sprk_event -ExcludeIds @('{aaa}'))
        $r.Count | Should -Be 1
        $r[0] | Should -Match 'theirs'
    }
}

Describe 'Invoke-ScopedSolutionImport guards' {
    It 'rejects --publish-changes and the -pc short flag before running anything' {
        { Invoke-ScopedSolutionImport -EnvironmentUrl 'https://x' -ZipPath 'a.zip' -SolutionUniqueName S -ImportArgs @('--publish-changes') -PacExe 'pac' -Context @{} } | Should -Throw '*tenant-wide publish*'
        { Invoke-ScopedSolutionImport -EnvironmentUrl 'https://x' -ZipPath 'a.zip' -SolutionUniqueName S -ImportArgs @('-pc') -PacExe 'pac' -Context @{} } | Should -Throw '*tenant-wide publish*'
    }
    It 'refuses an unmapped component type BEFORE importing (nothing imported)' {
        Mock Get-ZipSolutionInfo { [pscustomobject]@{ UniqueName = 'S'; Managed = $false; RootTypes = @(1, 424242); RootEntities = @() } }
        Mock Get-SolutionComponentRows { $null }
        { Invoke-ScopedSolutionImport -EnvironmentUrl 'https://x' -ZipPath 'a.zip' -SolutionUniqueName S -PacExe 'pac-must-not-run' -Context @{ Api = 'https://x/api/data/v9.2'; Headers = @{} } } |
            Should -Throw '*Nothing was imported*'
    }
}

Describe 'banned-pattern coverage' {
    It 'matches every known bypass form' {
        $samples = @(
            'Invoke-RestMethod -Uri "$api/PublishAllXml"', '"PublishAll" + "Xml"', 'pac solution publish-all', 'pac  solution  publish',
            'pac org publish --async', 'pac solution import --path x --publish-changes', 'pac solution import --path x -pc',
            '  -pc  `', 'Publish-CrmAllCustomization -Conn $c', 'uses: microsoft/powerplatform-actions/publish-solution@v1',
            'task: PowerPlatformPublishCustomizations@2', '5. Publish all customizations', 'Save + Publish All')
        foreach ($sm in $samples) {
            $hit = $false
            foreach ($pt in $script:BannedPatterns) { if ($sm -match "(?i)$pt") { $hit = $true } }
            if (-not $hit) { throw "bypass not caught: $sm" }
        }
    }
    It 'does not match the allowed scoped forms' {
        $ok = @('POST PublishXml with ParameterXml', 'Publish-SolutionComponents -Context $c', 'scripts/Import-SolutionScoped.ps1 -PlanOnly', 'pac solution import --path x --force-overwrite', 'pac solution list', 'Invoke-PublishXml -Context $ctx')
        foreach ($sm in $ok) { foreach ($pt in $script:BannedPatterns) { if ($sm -match "(?i)$pt") { throw "false positive on '$sm' by '$pt'" } } }
    }
}

Describe 'no tenant-wide publish anywhere in the repo (D-83)' {
    BeforeAll {
        # Allow-list: "path<TAB>reason" per line. Narrow and explicit; delete a line when its reason is gone.
        $allowFile = Join-Path $PSScriptRoot 'publish-lint-allowlist.txt'
        $allow = @(Get-Content -LiteralPath $allowFile | Where-Object { $_ -and $_ -notmatch '^\s*#' } | ForEach-Object { ($_ -split "`t")[0].Trim() })
        # The module and this test must name what they ban; docs/adr is ADR history; projects/ notes are history except the skill-amendment note.
        $excl = @(':(exclude)scripts/lib/Publish-SolutionComponents.ps1', ':(exclude)tests/scripts/Publish-SolutionComponents.Tests.ps1', ':(exclude)tests/scripts/publish-lint-allowlist.txt', ':(exclude)docs/adr', ':(exclude)*package-lock.json')
        $excl += @($allow | ForEach-Object { ":(exclude)$_" })
        $roots = @('scripts', '.github', 'docs', 'infrastructure', 'src', 'tests', '.claude')
        $script:Hits = @()
        $script:GitExit = @{}
        foreach ($pt in $script:BannedPatterns) {
            $out = & git -C $script:RepoRoot grep -n -i -I -P -e $pt -- @($roots + $excl) 2>&1
            $script:GitExit[$pt] = $LASTEXITCODE
            $script:Hits += @($out | Where-Object { $_ } | ForEach-Object { "[$pt] $_" })
        }
    }
    It 'ran git grep successfully for every pattern (0 = hits, 1 = none; anything else is a failure, never a pass)' {
        foreach ($k in $script:GitExit.Keys) { $script:GitExit[$k] | Should -BeIn @(0, 1) }
    }
    It 'has zero hits for any banned form' {
        ($script:Hits -join [Environment]::NewLine) | Should -BeNullOrEmpty
    }
}
