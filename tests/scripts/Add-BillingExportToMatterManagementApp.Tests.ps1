# Pester 5. Run: Invoke-Pester tests/scripts/Add-BillingExportToMatterManagementApp.Tests.ps1
# eBilling R1 (task 153 follow-up): scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1 adds sprk_billingexport to the Matter
# Management app on dev - ADDITIVELY and only when it is missing. Dev, edited in the app designer on 2026-11-03, already has the table and
# a subarea under another Id; the script must then plan and write NOTHING, and it must never create a second Billing Exports entry.
#
# The script talks to a live environment on load, so it is never run against one here. Two kinds of test:
#   1. The decision functions are read out of the script's own AST (the code under test is the shipped code, not a copy).
#   2. The whole script is run in a child process against a fake Dataverse (fixtures/Invoke-BillingExportAppAgainstFake.ps1): reads are
#      answered from a state that mirrors dev, every write is recorded and applied to the fake state. No network call is made.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Add-BillingExportToMatterManagementApp.ps1'
    $script:ScriptText = [System.IO.File]::ReadAllText($script:ScriptPath)
    $script:Fixture = Join-Path $PSScriptRoot 'fixtures' 'Invoke-BillingExportAppAgainstFake.ps1'
    $script:SitemapFixture = Join-Path $PSScriptRoot 'fixtures' 'sitemap-matter-management-before-billingexport.xml'
    $script:SourceSiteMap = Join-Path $script:RepoRoot 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'AppModuleSiteMaps' 'sprk_MatterManagement' 'AppModuleSiteMap.xml'
    $script:SourceAppModule = Join-Path $script:RepoRoot 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'AppModules' 'sprk_MatterManagement' 'AppModule.xml'

    . (Join-Path $script:RepoRoot 'scripts' 'lib' 'Publish-SolutionComponents.ps1')

    $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }
    foreach ($name in 'Resolve-InvocationMode', 'Find-SubAreaRawEnd', 'Find-GroupRawClose', 'Read-XmlPreserving', 'Get-EntitySubAreas', 'Get-SourceSubArea',
        'Get-SourceAppModuleEntry', 'New-SiteMapPlan', 'Add-NodeLines', 'Get-SiteMapNodeLines', 'Compare-SiteMapNodes', 'Assert-SiteMapAdditiveOnly',
        'Group-AppComponents', 'Test-HasComponent', 'Get-ValidateAppSummary', 'Get-IssueKeys', 'New-ScopedPublishXml', 'Test-AllowedWrite',
        'Save-SiteMapBackup', 'Invoke-DvWrite') {
        $fn = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
        if (-not $fn) { throw "The script no longer defines $name" }
        . ([scriptblock]::Create($fn.Extent.Text))
    }

    # Invoke-DvWrite calls Invoke-Dv (the module's); here it only records the call.
    function Invoke-Dv { param([string]$Path, [string]$Method = 'GET', $Body = $null, [hashtable]$ExtraHeaders = @{}) $script:DvCalls.Add("$Method $Path"); return $null }

    $script:SitemapBefore = [System.IO.File]::ReadAllText($script:SitemapFixture)

    # The source element task 029 added (before the source sync of 2026-11-03): group_work, after subarea_eb_submission.
    $script:OrigTemplate = '<SubArea Id="subarea_eb_export" Icon="/_imgs/imagestrips/transparent_spacer.gif" Entity="sprk_billingexport" Client="All,Outlook,OutlookLaptopClient,OutlookWorkstationClient,Web" AvailableOffline="true" PassParams="false" Sku="All,OnPremise,Live,SPLA" />'
    $script:Small = '<SiteMap><Area Id="a1"><Group Id="g_work"><SubArea Id="s_inv" Entity="sprk_invoice" /><SubArea Id="s_x" Url="u"><Titles><Title LCID="1033" Title="X &gt; Y" /></Titles></SubArea></Group><Group Id="g_other"><SubArea Id="s_o" Entity="sprk_matter" /></Group></Area></SiteMap>'
    $script:NewSub = '<SubArea Id="s_new" Entity="sprk_billingexport" />'

    function New-Plan([string]$Xml, [string]$Sub, [string]$Id, [string]$Group, [string]$Pred) {
        New-SiteMapPlan -BeforeXml $Xml -SubAreaXml $Sub -SubAreaId $Id -Entity 'sprk_billingexport' -SourceGroupId $Group -SourcePredecessorId $Pred -InvoiceEntity 'sprk_invoice'
    }

    # Runs the whole script against the fake Dataverse in a child process.
    function Invoke-Fake {
        param([string]$Scenario, [string]$Mode, [string]$BackupDir, [switch]$ExplicitFormsAndViews)
        $a = @('-NoProfile', '-File', $script:Fixture, '-ScriptPath', $script:ScriptPath, '-SitemapFixture', $script:SitemapFixture, '-BackupDir', $BackupDir, '-Scenario', $Scenario, '-Mode', $Mode)
        if ($ExplicitFormsAndViews) { $a += '-ExplicitFormsAndViews' }
        $saved = $env:DATAVERSE_URL
        $env:DATAVERSE_URL = 'https://spaarkedev1.crm.dynamics.com'
        try { $text = (& pwsh @a 2>&1 | Out-String -Width 400) } finally { $env:DATAVERSE_URL = $saved }
        $lines = @($text -split "`r?`n")
        [pscustomobject]@{
            Text      = $text
            Exit      = [int](($lines | Where-Object { $_ -like 'RUNNER-EXIT: *' }) -replace 'RUNNER-EXIT: ', '')
            Writes    = @($lines | Where-Object { $_ -like 'RUNNER-WRITE: *' } | ForEach-Object { $_ -replace '^RUNNER-WRITE: ', '' })
            Subareas  = [int](($lines | Where-Object { $_ -like 'RUNNER-SUBAREAS: *' }) -replace 'RUNNER-SUBAREAS: ', '')
            Table     = (($lines | Where-Object { $_ -like 'RUNNER-TABLE: *' }) -replace 'RUNNER-TABLE: ', '')
            BackupDir = $BackupDir
        }
    }
}

Describe 'Resolve-InvocationMode' {
    It 'is WhatIf by default' { (Resolve-InvocationMode $false $false $false $false).Mode | Should -Be 'WHATIF' }
    It '-WhatIf alone is WhatIf' { (Resolve-InvocationMode $true $false $false $false).Mode | Should -Be 'WHATIF' }
    It '-Verify alone is Verify' { (Resolve-InvocationMode $false $false $true $false).Mode | Should -Be 'VERIFY' }
    It '-Apply without -OwnerApproval is refused' {
        $r = Resolve-InvocationMode $false $true $false $false
        $r.Mode | Should -BeNullOrEmpty
        $r.Error | Should -Match 'OwnerApproval'
    }
    It '-Apply with -OwnerApproval is the only way to APPLY' { (Resolve-InvocationMode $false $true $false $true).Mode | Should -Be 'APPLY' }
    It '-OwnerApproval alone does not write' { (Resolve-InvocationMode $false $false $false $true).Mode | Should -Be 'WHATIF' }
    It 'two modes at once are refused' {
        (Resolve-InvocationMode $true $true $false $true).Error | Should -Match 'ONE of'
        (Resolve-InvocationMode $false $true $true $true).Error | Should -Match 'ONE of'
        (Resolve-InvocationMode $true $false $true $false).Error | Should -Match 'ONE of'
    }
}

Describe 'Find-SubAreaRawEnd' {
    It 'finds the end of a self-closing element' {
        $i = Find-SubAreaRawEnd -Xml $script:Small -SubAreaId 's_inv'
        $script:Small.Substring(0, $i) | Should -Match '<SubArea Id="s_inv" Entity="sprk_invoice" />$'
    }
    It 'finds the end of an element with children (not the first > inside it)' {
        $i = Find-SubAreaRawEnd -Xml $script:Small -SubAreaId 's_x'
        $script:Small.Substring($i) | Should -Match '^</Group><Group Id="g_other">'
    }
    It 'is not fooled by > inside an attribute value' {
        $xml = '<SiteMap><Group Id="g"><SubArea Id="s_q" Url="a>b" /><SubArea Id="s_r" Entity="e" /></Group></SiteMap>'
        $i = Find-SubAreaRawEnd -Xml $xml -SubAreaId 's_q'
        $xml.Substring($i) | Should -Match '^<SubArea Id="s_r"'
    }
    It 'throws for an Id that is not there, and for an Id that appears twice' {
        { Find-SubAreaRawEnd -Xml $script:Small -SubAreaId 'nope' } | Should -Throw '*was not found*'
        { Find-SubAreaRawEnd -Xml ($script:Small + $script:Small) -SubAreaId 's_inv' } | Should -Throw '*more than once*'
    }
    It 'Find-GroupRawClose returns the index of the closing tag of that group' {
        $i = Find-GroupRawClose -Xml $script:Small -GroupId 'g_other'
        $script:Small.Substring($i) | Should -Be '</Group></Area></SiteMap>'
    }
}

Describe 'Get-EntitySubAreas / Get-SourceSubArea' {
    It 'matches by the Entity attribute only (a Url that names the table is not a subarea for it)' {
        $xml = $script:Small.Replace('</Group></Area>', '<SubArea Id="s_u" Url="$webresource:sprk_billingexport" /></Group></Area>')
        @(Get-EntitySubAreas -Xml $xml -Entity 'sprk_billingexport').Count | Should -Be 0
    }
    It 'returns the Id, area and group of every subarea for the entity' {
        $xml = $script:Small.Replace('<SubArea Id="s_o"', '<SubArea Id="s_dup" Entity="sprk_billingexport" /><SubArea Id="s_o"')
        $r = @(Get-EntitySubAreas -Xml $xml -Entity 'sprk_billingexport')
        $r.Count | Should -Be 1
        $r[0].Id | Should -Be 's_dup'; $r[0].AreaId | Should -Be 'a1'; $r[0].GroupId | Should -Be 'g_other'
    }
    It 'the pre-designer dev sitemap has none; the synced source sitemap has exactly one' {
        @(Get-EntitySubAreas -Xml $script:SitemapBefore -Entity 'sprk_billingexport').Count | Should -Be 0
        @(Get-EntitySubAreas -Xml ([System.IO.File]::ReadAllText($script:SourceSiteMap)) -Entity 'sprk_billingexport').Count | Should -Be 1
    }
    It 'reads the source subarea as dev has it after the designer add (Id, group, attributes)' {
        $s = Get-SourceSubArea -Path $script:SourceSiteMap -Entity 'sprk_billingexport'
        $s.Found | Should -BeTrue
        $s.Id | Should -Be 'subarea_8f8f4243'
        $s.AreaId | Should -Be 'area_finance'
        $s.GroupId | Should -Be 'group_review'
        $s.Xml | Should -Match '^<SubArea Id="subarea_8f8f4243" .* Entity="sprk_billingexport" .*/>$'
        ([System.IO.File]::ReadAllText($script:SourceSiteMap)).Contains($s.Xml) | Should -BeTrue
    }
    It 'reports Found = false (never throws) for a missing file and for an entity that has no subarea' {
        (Get-SourceSubArea -Path (Join-Path $TestDrive 'nope.xml') -Entity 'sprk_billingexport').Found | Should -BeFalse
        (Get-SourceSubArea -Path $script:SourceSiteMap -Entity 'sprk_notthere').Found | Should -BeFalse
    }
    It 'the source AppModule.xml lists the table as an entity component' {
        (Get-SourceAppModuleEntry -Path $script:SourceAppModule -Entity 'sprk_billingexport').Found | Should -BeTrue
        (Get-SourceAppModuleEntry -Path $script:SourceAppModule -Entity 'sprk_notthere').Found | Should -BeFalse
    }
}

Describe 'New-SiteMapPlan - matched by Entity, never by subarea Id' {
    It 'plans NOTHING when a subarea for the entity exists under another Id and in another group' {
        $xml = $script:Small.Replace('<SubArea Id="s_o"', '<SubArea Id="subarea_8f8f4243" Entity="sprk_billingexport" /><SubArea Id="s_o"')
        $plan = New-Plan $xml $script:NewSub 's_new' 'g_work' 's_inv'
        $plan.Present | Should -BeTrue
        $plan.AfterXml | Should -BeNullOrEmpty
        $plan.Conflict | Should -BeNullOrEmpty
        $plan.Existing[0].Id | Should -Be 'subarea_8f8f4243'
    }
    It 'plans nothing against the synced source sitemap (the designer''s entry) even for the old source Id' {
        $plan = New-Plan ([System.IO.File]::ReadAllText($script:SourceSiteMap)) $script:OrigTemplate 'subarea_eb_export' 'group_work' 'subarea_eb_submission'
        $plan.Present | Should -BeTrue
        $plan.AfterXml | Should -BeNullOrEmpty
    }
    It 'plans nothing when there are two (no third is added, nothing is removed)' {
        $xml = $script:Small.Replace('<SubArea Id="s_o"', '<SubArea Id="d1" Entity="sprk_billingexport" /><SubArea Id="d2" Entity="sprk_billingexport" /><SubArea Id="s_o"')
        $plan = New-Plan $xml $script:NewSub 's_new' 'g_work' 's_inv'
        $plan.Present | Should -BeTrue
        $plan.Existing.Count | Should -Be 2
    }
    It 'does not treat a Url subarea that names the table as present' {
        $xml = $script:Small.Replace('<SubArea Id="s_o"', '<SubArea Id="s_u" Url="$webresource:sprk_billingexport" /><SubArea Id="s_o"')
        (New-Plan $xml $script:NewSub 's_new' 'g_work' 's_inv').Present | Should -BeFalse
    }
    It 'inserts right after the source predecessor, in the source group' {
        $plan = New-Plan $script:Small $script:NewSub 's_new' 'g_work' 's_inv'
        $plan.Present | Should -BeFalse
        $plan.GroupId | Should -Be 'g_work'
        $plan.AfterXml | Should -Match 'Entity="sprk_invoice" /><SubArea Id="s_new" Entity="sprk_billingexport" /><SubArea Id="s_x"'
    }
    It 'falls back to the Invoices subarea of the source group when the predecessor is not on dev' {
        $plan = New-Plan $script:Small $script:NewSub 's_new' 'g_work' 'subarea_eb_submission'
        $plan.Anchor | Should -Be 's_inv'
        $plan.AfterXml | Should -Match 'Entity="sprk_invoice" /><SubArea Id="s_new"'
    }
    It 'goes to the end of the source group when neither the predecessor nor an Invoices subarea is in it' {
        $plan = New-Plan $script:Small $script:NewSub 's_new' 'g_other' $null
        $plan.GroupId | Should -Be 'g_other'
        $plan.AfterXml | Should -Match 'Entity="sprk_matter" /><SubArea Id="s_new" Entity="sprk_billingexport" /></Group>'
    }
    It 'goes next to the Invoices subarea when the source group does not exist on dev' {
        $plan = New-Plan $script:Small $script:NewSub 's_new' 'group_gone' $null
        $plan.Placement | Should -Match 'absent on dev'
        $plan.GroupId | Should -Be 'g_work'
        $plan.AfterXml | Should -Match 'Entity="sprk_invoice" /><SubArea Id="s_new"'
    }
    It 'reports a Conflict (no plan) when the source group and any Invoices subarea are both missing' {
        $xml = '<SiteMap><Area Id="a1"><Group Id="g1"><SubArea Id="s1" Entity="sprk_matter" /></Group></Area></SiteMap>'
        $plan = New-Plan $xml $script:NewSub 's_new' 'group_gone' $null
        $plan.Conflict | Should -Match 'no safe place'
        $plan.AfterXml | Should -BeNullOrEmpty
    }
    It 'reports a Conflict when the Id is already used by a subarea of another entity (no second element with that Id)' {
        $plan = New-Plan $script:Small $script:NewSub 's_inv' 'g_work' $null
        $plan.Conflict | Should -Match 'already used'
        $plan.AfterXml | Should -BeNullOrEmpty
    }
}

Describe 'Assert-SiteMapAdditiveOnly (real pre-designer dev sitemap)' {
    BeforeAll {
        $script:Plan = New-Plan $script:SitemapBefore $script:OrigTemplate 'subarea_eb_export' 'group_work' 'subarea_eb_submission'
        function Test-Proof([string]$After, [string]$Sub = $script:OrigTemplate, [string]$Id = 'subarea_eb_export', [int]$At = $script:Plan.InsertIndex) {
            Assert-SiteMapAdditiveOnly -BeforeXml $script:SitemapBefore -AfterXml $After -SubAreaXml $Sub -SubAreaId $Id -InsertIndex $At
        }
    }
    It 'plans the old source element next to the Invoices subarea of group_work and proves it additive-only' {
        $script:Plan.Present | Should -BeFalse
        $script:Plan.Anchor | Should -Be 'subarea_fc9da905'
        $script:Plan.GroupId | Should -Be 'group_work'
        $p = Test-Proof $script:Plan.AfterXml
        $p.Ok | Should -BeTrue
        $p.Problems.Count | Should -Be 0
        $p.Compare.AfterCount | Should -Be ($p.Compare.BeforeCount + 1)
        $p.Compare.Added.Count | Should -Be 1
        $p.Compare.Added[0] | Should -Match 'SubArea\[subarea_eb_export\] \|'
        $p.Compare.Unexpected.Count | Should -Be 0
    }
    It 'removing the inserted text gives back the before XML byte for byte' {
        $script:Plan.AfterXml.Remove($script:Plan.InsertIndex, $script:OrigTemplate.Length) | Should -BeExactly $script:SitemapBefore
    }
    It 'plans the current source element (group_review, as the designer put it) additive-only too' {
        $s = Get-SourceSubArea -Path $script:SourceSiteMap -Entity 'sprk_billingexport'
        $plan = New-Plan $script:SitemapBefore $s.Xml $s.Id $s.GroupId $s.PredecessorId
        $plan.Present | Should -BeFalse
        $plan.GroupId | Should -Be 'group_review'
        (Test-Proof $plan.AfterXml $s.Xml $s.Id $plan.InsertIndex).Ok | Should -BeTrue
    }
    It 'refuses a change elsewhere in the document even when the length is unchanged' {
        $tampered = $script:Plan.AfterXml.Replace('Title="SPAARKE"', 'Title="SPAARKX"')
        $tampered.Length | Should -Be $script:Plan.AfterXml.Length
        $p = Test-Proof $tampered
        $p.Ok | Should -BeFalse
        ($p.Problems -join ' | ') | Should -Match 'byte for byte'
        ($p.Problems -join ' | ') | Should -Match 'node difference'
    }
    It 'refuses two inserted elements' {
        $two = $script:Plan.AfterXml.Insert($script:Plan.InsertIndex, $script:OrigTemplate)
        (Test-Proof $two).Ok | Should -BeFalse
    }
    It 'refuses an insertion that also drops another node' {
        $victim = [regex]::Match($script:Plan.AfterXml, '<SubArea Id="(?!subarea_eb_export)[^"]*"[^<>]*/>').Value
        $victim | Should -Not -BeNullOrEmpty
        $dropped = $script:Plan.AfterXml.Replace($victim, '')
        $p = Test-Proof $dropped
        $p.Ok | Should -BeFalse
    }
    It 'refuses when the inserted text is not the element it claims to be' {
        $other = $script:OrigTemplate.Replace('transparent_spacer.gif', 'transparent_spacer.gix')
        $other.Length | Should -Be $script:OrigTemplate.Length
        $after = $script:SitemapBefore.Insert($script:Plan.InsertIndex, $other)
        $p = Test-Proof $after
        $p.Ok | Should -BeFalse
        ($p.Problems -join ' | ') | Should -Match 'not the SubArea element'
    }
    It 'refuses an element with a different Id than the one claimed' {
        $renamed = $script:OrigTemplate.Replace('subarea_eb_export', 'subarea_other')
        $after = $script:SitemapBefore.Insert($script:Plan.InsertIndex, $renamed)
        (Assert-SiteMapAdditiveOnly -BeforeXml $script:SitemapBefore -AfterXml $after -SubAreaXml $renamed -SubAreaId 'subarea_eb_export' -InsertIndex $script:Plan.InsertIndex).Ok | Should -BeFalse
    }
    It 'refuses an after text that is not well-formed XML (reports it, does not throw)' {
        $p = Test-Proof ($script:Plan.AfterXml.Replace('</SiteMap>', '</SiteMa>'))
        $p.Ok | Should -BeFalse
        ($p.Problems -join ' | ') | Should -Match 'could not be compared'
    }
}

Describe 'components and ValidateApp' {
    It 'Group-AppComponents / Test-HasComponent compare GUIDs case-insensitively and by component type' {
        $sets = Group-AppComponents @([pscustomobject]@{ componenttype = 1; objectid = 'ABCDEF00-0000-4000-8000-000000000001' }, [pscustomobject]@{ componenttype = 60; objectid = 'ffffffff-0000-4000-8000-000000000002' })
        Test-HasComponent $sets 1 'abcdef00-0000-4000-8000-000000000001' | Should -BeTrue
        Test-HasComponent $sets 60 'abcdef00-0000-4000-8000-000000000001' | Should -BeFalse
        Test-HasComponent $sets 26 'ffffffff-0000-4000-8000-000000000002' | Should -BeFalse
    }
    It 'treats the platform''s informational "does not reference a form or view" lines as clean' {
        $apos = [string][char]0x2019
        $r = [pscustomobject]@{ AppValidationResponse = [pscustomobject]@{ ValidationSuccess = $true; ValidationIssueList = @(
                    [pscustomobject]@{ ErrorType = 'Warning'; Message = "Billing Export doesn${apos}t reference a form or view. App users will see all forms and views."; DisplayName = $null; RequiredComponents = @() }
                    [pscustomobject]@{ ErrorType = 'Warning'; Message = $null; DisplayName = 'Site Map'; RequiredComponents = @([pscustomobject]@{ SchemaName = 'sprk_aipersona'; DisplayName = 'AI Persona' }) }) } }
        $s = Get-ValidateAppSummary $r
        $s.Clean | Should -BeTrue
        $s.InfoNoFormView.Count | Should -Be 1
        $s.Other.Count | Should -Be 1
    }
    It 'is not clean for an Error, for a failed validation, or for a Billing Export issue' {
        $mk = { param($ok, $issues) [pscustomobject]@{ AppValidationResponse = [pscustomobject]@{ ValidationSuccess = $ok; ValidationIssueList = @($issues) } } }
        (Get-ValidateAppSummary (& $mk $true @([pscustomobject]@{ ErrorType = 'Error'; Message = 'x'; DisplayName = 'y'; RequiredComponents = @() }))).Clean | Should -BeFalse
        (Get-ValidateAppSummary (& $mk $false @())).Clean | Should -BeFalse
        $billing = [pscustomobject]@{ ErrorType = 'Warning'; Message = 'The form is missing'; DisplayName = 'Billing Export'; RequiredComponents = @() }
        (Get-ValidateAppSummary (& $mk $true @($billing))).Clean | Should -BeFalse
        $viaRequired = [pscustomobject]@{ ErrorType = 'Warning'; Message = $null; DisplayName = 'Site Map'; RequiredComponents = @([pscustomobject]@{ SchemaName = 'sprk_billingexport'; DisplayName = 'x' }) }
        (Get-ValidateAppSummary (& $mk $true @($viaRequired))).Clean | Should -BeFalse
    }
    It 'Get-IssueKeys makes a stable key per issue (new issues are told from the baseline)' {
        $r = [pscustomobject]@{ AppValidationResponse = [pscustomobject]@{ ValidationIssueList = @([pscustomobject]@{ ErrorType = 'Warning'; ComponentType = 62; DisplayName = 'Site Map'; Message = $null }) } }
        @(Get-IssueKeys $r) | Should -Be @('Warning|62|Site Map|')
    }
}

Describe 'writes: only AddAppComponents, the scoped publish and one sitemap PATCH are possible' {
    BeforeAll {
        $script:AppG = '729afe6d-ca73-f011-b4cb-6045bdd8b757'
        $script:SmG = '57410a70-ca73-f011-b4cb-6045bdd6a665'
    }
    It 'New-ScopedPublishXml names the app module and the sitemap, nothing else' {
        New-ScopedPublishXml -AppId $script:AppG -SitemapId $script:SmG | Should -BeExactly "<importexportxml><sitemaps><sitemap>{$($script:SmG)}</sitemap></sitemaps><appmodules><appmodule>{$($script:AppG)}</appmodule></appmodules></importexportxml>"
        New-ScopedPublishXml -AppId $script:AppG | Should -BeExactly "<importexportxml><appmodules><appmodule>{$($script:AppG)}</appmodule></appmodules></importexportxml>"
    }
    It 'New-ScopedPublishXml refuses a value that is not a GUID' {
        { New-ScopedPublishXml -AppId 'x</appmodule>' -SitemapId $script:SmG } | Should -Throw '*Not a GUID*'
    }
    It 'Test-AllowedWrite allows the three writes' {
        Test-AllowedWrite -Path 'AddAppComponents' -Method 'POST' -Body @{ AppId = $script:AppG } | Should -BeTrue
        Test-AllowedWrite -Path 'PublishXml' -Method 'POST' -Body @{ ParameterXml = (New-ScopedPublishXml -AppId $script:AppG -SitemapId $script:SmG) } | Should -BeTrue
        Test-AllowedWrite -Path 'PublishXml' -Method 'POST' -Body @{ ParameterXml = (New-ScopedPublishXml -AppId $script:AppG) } | Should -BeTrue
        Test-AllowedWrite -Path "sitemaps($($script:SmG))" -Method 'PATCH' -Body @{ sitemapxml = '<SiteMap />' } | Should -BeTrue
    }
    It 'Test-AllowedWrite refuses an unscoped or widened publish' {
        $bad = '<importexportxml></importexportxml>', '<importexportxml><entities><entity>sprk_invoice</entity></entities></importexportxml>',
        "<importexportxml><appmodules><appmodule>{$($script:AppG)}</appmodule></appmodules><entities><entity>sprk_invoice</entity></entities></importexportxml>",
        "<importexportxml><webresources><webresource>{$($script:AppG)}</webresource></webresources></importexportxml>", ''
        foreach ($x in $bad) { Test-AllowedWrite -Path 'PublishXml' -Method 'POST' -Body @{ ParameterXml = $x } | Should -BeFalse -Because $x }
    }
    It 'Test-AllowedWrite refuses any other path, record or body' {
        Test-AllowedWrite -Path 'PublishXml' -Method 'PATCH' -Body @{ ParameterXml = '<importexportxml><appmodules><appmodule>{a}</appmodule></appmodules></importexportxml>' } | Should -BeFalse
        Test-AllowedWrite -Path "appmodules($($script:AppG))" -Method 'PATCH' -Body @{ name = 'x' } | Should -BeFalse
        Test-AllowedWrite -Path "sitemaps($($script:SmG))" -Method 'PATCH' -Body @{ sitemapxml = 'x'; sitemapname = 'y' } | Should -BeFalse
        Test-AllowedWrite -Path "sitemaps($($script:SmG))" -Method 'PATCH' -Body @{ sitemapname = 'y' } | Should -BeFalse
        Test-AllowedWrite -Path 'sitemaps' -Method 'POST' -Body @{ sitemapxml = 'x' } | Should -BeFalse
        Test-AllowedWrite -Path 'RemoveAppComponents' -Method 'POST' -Body @{} | Should -BeFalse
        Test-AllowedWrite -Path "EntityDefinitions(LogicalName='sprk_invoice')" -Method 'POST' -Body @{} | Should -BeFalse
    }
    Context 'Invoke-DvWrite' {
        BeforeEach { $script:DvCalls = [System.Collections.Generic.List[string]]::new() }
        It 'refuses every write outside -Apply' {
            foreach ($m in 'WHATIF', 'VERIFY', $null) {
                $script:Mode = $m; $script:OwnerApprovalGiven = $true
                { Invoke-DvWrite -Path 'AddAppComponents' -Method POST -Body @{ AppId = $script:AppG } } | Should -Throw '*Write refused*'
            }
            $script:DvCalls.Count | Should -Be 0
        }
        It 'refuses a write under -Apply without -OwnerApproval' {
            $script:Mode = 'APPLY'; $script:OwnerApprovalGiven = $false
            { Invoke-DvWrite -Path 'AddAppComponents' -Method POST -Body @{ AppId = $script:AppG } } | Should -Throw '*Write refused*'
            $script:DvCalls.Count | Should -Be 0
        }
        It 'refuses a call that is not one of the three allowed writes, even under -Apply with approval' {
            $script:Mode = 'APPLY'; $script:OwnerApprovalGiven = $true
            { Invoke-DvWrite -Path "appmodules($($script:AppG))" -Method PATCH -Body @{ name = 'x' } } | Should -Throw '*not one of the writes*'
            { Invoke-DvWrite -Path 'PublishXml' -Method POST -Body @{ ParameterXml = '<importexportxml></importexportxml>' } } | Should -Throw '*not one of the writes*'
            $script:DvCalls.Count | Should -Be 0
        }
        It 'passes an allowed write through under -Apply with approval' {
            $script:Mode = 'APPLY'; $script:OwnerApprovalGiven = $true
            Invoke-DvWrite -Path 'AddAppComponents' -Method POST -Body @{ AppId = $script:AppG } | Out-Null
            @($script:DvCalls) | Should -Be @('POST AddAppComponents')
        }
    }
}

Describe 'Save-SiteMapBackup' {
    It 'keeps a byte-for-byte copy with a sidecar, under a name that is unique per call' {
        $xml = $script:SitemapBefore
        $a = Save-SiteMapBackup -Dir (Join-Path $TestDrive 'bk') -UniqueName 'sprk_MatterManagement' -SitemapId 'sid' -Etag 'W/"1"' -ModifiedOn '2026-11-03T22:13:29Z' -Xml $xml -Environment 'https://x'
        $b = Save-SiteMapBackup -Dir (Join-Path $TestDrive 'bk') -UniqueName 'sprk_MatterManagement' -SitemapId 'sid' -Etag 'W/"1"' -ModifiedOn '2026-11-03T22:13:29Z' -Xml $xml -Environment 'https://x'
        $a.Path | Should -Not -Be $b.Path
        [System.IO.File]::ReadAllBytes($a.Path) | Should -Be ([System.Text.UTF8Encoding]::new($false).GetBytes($xml))
        [System.IO.File]::ReadAllText($a.Path) | Should -BeExactly $xml
        $side = Get-Content -LiteralPath $a.Sidecar -Raw | ConvertFrom-Json
        $side.sitemapid | Should -Be 'sid'
        $side.etag | Should -Be 'W/"1"'
        $side.sha256 | Should -Be $a.Sha256
        $side.restore | Should -Match 'PATCH sitemaps\(sid\)'
        Split-Path -Leaf $a.Path | Should -Match '^sitemap-sprk_MatterManagement-before-\d{8}T\d{9}Z-[0-9a-f]{8}\.xml$'
    }
}

Describe 'the script source' {
    It 'every Dataverse write goes through Invoke-DvWrite (no Invoke-Dv call with -Method anywhere else)' {
        $writeFn = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-DvWrite' }, $true)
        $calls = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-Dv' }, $true)
        $calls.Count | Should -BeGreaterThan 5
        $withMethod = @($calls | Where-Object { @($_.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'Method' }).Count -gt 0 })
        $withMethod.Count | Should -Be 1
        $withMethod[0].Extent.StartOffset | Should -BeGreaterThan $writeFn.Extent.StartOffset
        $withMethod[0].Extent.EndOffset | Should -BeLessThan $writeFn.Extent.EndOffset
    }
    It 'Invoke-DvWrite is only called for AddAppComponents, PublishXml and the sitemap PATCH' {
        $calls = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-DvWrite' }, $true)
        $calls.Count | Should -Be 4
        $paths = @($calls | ForEach-Object {
                $els = @($_.CommandElements)
                for ($i = 0; $i -lt $els.Count - 1; $i++) { if ($els[$i] -is [System.Management.Automation.Language.CommandParameterAst] -and $els[$i].ParameterName -eq 'Path') { $els[$i + 1].Extent.Text } }
            })
        $paths.Count | Should -Be 4
        foreach ($p in $paths) { $p | Should -BeIn @("'AddAppComponents'", "'PublishXml'", '"sitemaps($($sm.Id))"') }
    }
    It 'never uses the tenant-wide publish (the repo lint bans it too) and is plain ASCII' {
        $banned = 'Publish' + 'All'
        $script:ScriptText | Should -Not -Match "(?i)$banned|publish[-_ ]all"
        ($script:ScriptText.ToCharArray() | Where-Object { [int]$_ -gt 127 }).Count | Should -Be 0
    }
    It 'matches the sitemap by Entity: the old Id-based option is gone and no subarea Id is hard-coded' {
        $script:ScriptText | Should -Not -Match 'SourcePlacement|IdOnly'
        $script:ScriptText | Should -Not -Match '\$SubAreaId\s*='
        $script:ScriptText | Should -Not -Match "subarea_eb_export"
    }
    It 'is dev only and refuses a non-dev URL before reading anything' {
        $script:ScriptText | Should -Match "spaarkedev1"
        $script:ScriptText.IndexOf('Resolve-InvocationMode -WhatIfSwitch') | Should -BeLessThan $script:ScriptText.IndexOf('Initialize-EbillingSchema -EnvironmentUrl')
    }
}

Describe 'the whole script against a fake Dataverse' {
    Context 'dev as it is now (Healthy: the table is a component, the designer''s subarea is there)' {
        BeforeAll {
            $script:H = @{}
            foreach ($m in 'None', 'WhatIf', 'Verify', 'Apply') { $script:H[$m] = Invoke-Fake -Scenario Healthy -Mode $m -BackupDir (Join-Path $TestDrive "h-$m") }
            $script:HRefused = Invoke-Fake -Scenario Healthy -Mode ApplyNoApproval -BackupDir (Join-Path $TestDrive 'h-refused')
        }
        It '-Verify passes all four checks and writes nothing' {
            $r = $script:H['Verify']
            $r.Exit | Should -Be 0
            ([regex]::Matches($r.Text, '\[PASS\]')).Count | Should -Be 4
            $r.Text | Should -Match 'VERIFY PASSED'
            $r.Writes.Count | Should -Be 0
        }
        It 'no mode given is the same as -WhatIf: reads only, plans nothing' {
            foreach ($m in 'None', 'WhatIf') {
                $r = $script:H[$m]
                $r.Exit | Should -Be 0
                $r.Text | Should -Match 'Mode: WHATIF'
                $r.Text | Should -Match '=> 0 component\(s\) to add'
                $r.Text | Should -Match 'no sitemap change'
                $r.Text | Should -Match 'publish needed now: no'
                $r.Writes.Count | Should -Be 0
            }
        }
        It '-Apply -OwnerApproval with the table and a subarea present writes nothing and publishes nothing' {
            $r = $script:H['Apply']
            $r.Exit | Should -Be 0
            $r.Text | Should -Match 'Nothing to add'
            $r.Writes.Count | Should -Be 0
            $r.Subareas | Should -Be 1
        }
        It 'reports source and dev as in sync for this table' {
            $script:H['Verify'].Text | Should -Match 'sitemap subarea: source subarea_8f8f4243 in area_finance/group_review; dev subarea_8f8f4243 in area_finance/group_review -> in sync'
            $script:H['Verify'].Text | Should -Match 'AppModule.xml entity component: source yes \(line \d+\); dev yes -> in sync'
        }
        It 'reports the batch form facts: default form, AP emails subgrid' {
            $script:H['Verify'].Text | Should -Match 'default form: bd0020ec-1912-4f2d-b3e6-a6315f8b9e48'
            $script:H['Verify'].Text | Should -Match 'AP emails subgrid on the main form .*: YES'
        }
        It '-Apply without -OwnerApproval is refused before anything is read' {
            $script:HRefused.Exit | Should -Be 2
            $script:HRefused.Text | Should -Match 'REFUSED: -Apply refuses to run without -OwnerApproval'
            $script:HRefused.Text | Should -Not -Match 'Environment:'
            $script:HRefused.Writes.Count | Should -Be 0
        }
    }

    Context 'the subarea exists under another Id and group (OtherId): matching is by Entity' {
        BeforeAll { $script:O = Invoke-Fake -Scenario OtherId -Mode Apply -BackupDir (Join-Path $TestDrive 'o') }
        It '-Apply plans nothing and never adds a second Billing Exports entry' {
            $script:O.Exit | Should -Be 0
            $script:O.Writes.Count | Should -Be 0
            $script:O.Subareas | Should -Be 1
            $script:O.Text | Should -Match 'already in the sitemap \(matched by Entity, not by Id\): subarea_zz_other in area_\w+/group_work'
        }
    }

    Context 'the state before the designer add (Missing)' {
        BeforeAll {
            $script:MW = Invoke-Fake -Scenario Missing -Mode WhatIf -BackupDir (Join-Path $TestDrive 'mw')
            $script:MV = Invoke-Fake -Scenario Missing -Mode Verify -BackupDir (Join-Path $TestDrive 'mv')
            $script:MX = Invoke-Fake -Scenario Missing -Mode WhatIf -BackupDir (Join-Path $TestDrive 'mx') -ExplicitFormsAndViews
            $script:MA = Invoke-Fake -Scenario Missing -Mode Apply -BackupDir (Join-Path $TestDrive 'ma')
        }
        It '-WhatIf lists exactly the table, proves the sitemap edit additive-only, and writes nothing' {
            $r = $script:MW
            $r.Exit | Should -Be 0
            $r.Text | Should -Match '\+ table  sprk_billingexport  entityid 11111111'
            $r.Text | Should -Match '=> 1 component\(s\) to add'
            $r.Text | Should -Match 'sprk_communication  already a component'
            $r.Text | Should -Match 'ADDITIVE-ONLY ASSERTION: passed'
            $r.Text | Should -Match 'removing them gives back the before XML: identical'
            $r.Text | Should -Match 'unexpected differences: 0'
            $r.Writes.Count | Should -Be 0
            $r.Subareas | Should -Be 0
            $r.Table | Should -Be 'no'
        }
        It '-WhatIf saves the before XML byte for byte' {
            $f = @(Get-ChildItem -LiteralPath $script:MW.BackupDir -Filter 'sitemap-*-before-*.xml')
            $f.Count | Should -Be 1
            [System.IO.File]::ReadAllText($f[0].FullName) | Should -BeExactly $script:SitemapBefore
        }
        It 'the main form and the source views are rows only with -ExplicitFormsAndViews (and only the views that are in source)' {
            $script:MW.Text | Should -Match 'form   bd0020ec.*- not added as a row'
            $script:MX.Text | Should -Match '=> 3 component\(s\) to add'
            $script:MX.Text | Should -Match '\+ form   bd0020ec-1912-4f2d-b3e6-a6315f8b9e48'
            $script:MX.Text | Should -Match '\+ view   ec28e244-5a7e-44ce-99c6-2c294e44a861'
            $script:MX.Text | Should -Match 'view   eeeeeeee-0000-4000-8000-00000000000e.*not in source'
        }
        It '-Verify fails (exit 1) on the missing table and subarea' {
            $script:MV.Exit | Should -Be 1
            $script:MV.Text | Should -Match '\[FAIL\] 1\. '
            $script:MV.Text | Should -Match '\[FAIL\] 4\. '
            $script:MV.Text | Should -Match 'VERIFY FAILED'
            $script:MV.Writes.Count | Should -Be 0
        }
        It '-Apply writes exactly: the component, the sitemap (If-Match, one key), the scoped publish - in that order - and verifies' {
            $r = $script:MA
            $r.Exit | Should -Be 0
            $r.Writes.Count | Should -Be 3
            $r.Writes[0] | Should -Be 'POST AddAppComponents entity:11111111-1111-4111-8111-111111111111'
            $r.Writes[1] | Should -Match '^PATCH sitemaps\(57410a70-ca73-f011-b4cb-6045bdd6a665\) if-match=W/"1000" keys=sitemapxml$'
            $r.Writes[2] | Should -Be 'POST PublishXml <importexportxml><sitemaps><sitemap>{57410a70-ca73-f011-b4cb-6045bdd6a665}</sitemap></sitemaps><appmodules><appmodule>{729afe6d-ca73-f011-b4cb-6045bdd8b757}</appmodule></appmodules></importexportxml>'
            $r.Subareas | Should -Be 1
            $r.Table | Should -Be 'yes'
            $r.Text | Should -Match 'APPLIED AND VERIFIED'
            $r.Text | Should -Match 'sitemap written and read back byte for byte'
            $r.Text | Should -Match 'issues that are new compared with the baseline: 0'
            @(Get-ChildItem -LiteralPath $r.BackupDir -Filter 'sitemap-*-before-*.xml').Count | Should -Be 1
        }
    }

    Context 'AddAppComponents is slow to show (Lag) or answers 204 and adds nothing (NoOp, the known environment hazard)' {
        BeforeAll {
            $script:LA = Invoke-Fake -Scenario Lag -Mode Apply -BackupDir (Join-Path $TestDrive 'la')
            $script:NA = Invoke-Fake -Scenario NoOp -Mode Apply -BackupDir (Join-Path $TestDrive 'na')
        }
        It 'Lag: publishes the app module only, reads again, then goes on to the sitemap' {
            $r = $script:LA
            $r.Exit | Should -Be 0
            $r.Writes.Count | Should -Be 4
            $r.Writes[1] | Should -Be 'POST PublishXml <importexportxml><appmodules><appmodule>{729afe6d-ca73-f011-b4cb-6045bdd8b757}</appmodule></appmodules></importexportxml>'
            $r.Writes[2] | Should -Match '^PATCH sitemaps\('
            $r.Subareas | Should -Be 1
        }
        It 'NoOp: stops before the sitemap is written (exit 3), with the manual step' {
            $r = $script:NA
            $r.Exit | Should -Be 3
            $r.Text | Should -Match 'STOPPED: AddAppComponents answered but 1 component\(s\) are still not in the app'
            $r.Text | Should -Match 'The sitemap was NOT written'
            @($r.Writes | Where-Object { $_ -like 'PATCH *' }).Count | Should -Be 0
            $r.Writes.Count | Should -Be 2
            $r.Subareas | Should -Be 0
        }
    }
}
