<#
.SYNOPSIS
    Test fixture for tests/scripts/Add-BillingExportToMatterManagementApp.Tests.ps1 (ebilling-r1, task 153 follow-up): runs
    scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1 in THIS process against a fake Dataverse. No network call is made.

.DESCRIPTION
    Invoke-RestMethod (and `az`, Start-Sleep) are replaced by global functions, so the module's own Invoke-Dv reaches the fake. A GET is
    answered from a small state that mirrors the Matter Management app on dev; any GET the fake does not know throws, so a new read in
    the script fails the test loudly instead of being answered with a guess. Every write is recorded AND applied to the fake state, so
    the read-back and the second publish of the script work against what it wrote.

    Scenarios (what dev looks like):
      Healthy  the table is an app component and the sitemap has the designer's subarea (subarea_8f8f4243, group_review)   [dev, 2026-11-03]
      OtherId  as Healthy, but the subarea has another Id and group (subarea_zz_other, group_work): matching must be by Entity
      Missing  the state before the designer add: no table component, no subarea
      Lag      as Missing, but AddAppComponents rows become visible only after a publish of the app module
      NoOp     as Missing, but AddAppComponents answers 204 and never adds anything (the known environment hazard, notes/029 7A)

    Prints the script's output, then "RUNNER-EXIT: n", "RUNNER-WRITES: n", one "RUNNER-WRITE: ..." line per write (PATCH lines carry
    If-Match), "RUNNER-SUBAREAS: n" (subareas for sprk_billingexport in the fake sitemap at the end) and "RUNNER-TABLE: yes|no".
#>
param(
    [Parameter(Mandatory)][string]$ScriptPath,
    [Parameter(Mandatory)][string]$SitemapFixture,
    [Parameter(Mandatory)][string]$BackupDir,
    [ValidateSet('Healthy', 'OtherId', 'Missing', 'Lag', 'NoOp')][string]$Scenario = 'Healthy',
    [ValidateSet('WhatIf', 'Verify', 'Apply', 'ApplyNoApproval', 'None')][string]$Mode = 'WhatIf',
    [switch]$ExplicitFormsAndViews
)

$global:FakeWrites = [System.Collections.Generic.List[string]]::new()
$global:FakeScenario = $Scenario

function global:az { $global:LASTEXITCODE = 0; 'fake-token' }
function global:Start-Sleep { }

$global:FkAppId = '729afe6d-ca73-f011-b4cb-6045bdd8b757'
$global:FkSitemapId = '57410a70-ca73-f011-b4cb-6045bdd6a665'
$global:FkIds = @{
    sprk_billingexport = '11111111-1111-4111-8111-111111111111'
    sprk_invoice       = '22222222-2222-4222-8222-222222222222'
    sprk_communication = '33333333-3333-4333-8333-333333333333'
    sprk_matter        = '44444444-4444-4444-8444-444444444444'
}
$global:FkMainForm = 'bd0020ec-1912-4f2d-b3e6-a6315f8b9e48'
$global:FkSourceView = 'ec28e244-5a7e-44ce-99c6-2c294e44a861'      # one of the source views of sprk_billingexport

# ---- sitemap: the dev sitemap before the designer add, plus (Healthy / OtherId) a subarea for the table ------------------------------
$xml = [System.IO.File]::ReadAllText($SitemapFixture)
if ($Scenario -in 'Healthy', 'OtherId') {
    $template = '<SubArea Id="subarea_8f8f4243" Icon="/_imgs/imagestrips/transparent_spacer.gif" Entity="sprk_billingexport" Client="All,Outlook,OutlookLaptopClient,OutlookWorkstationClient,Web" AvailableOffline="true" PassParams="false" Sku="All,OnPremise,Live,SPLA" />'
    if ($Scenario -eq 'OtherId') {
        $m = [regex]::Match($xml, '<SubArea Id="subarea_fc9da905"[^<>]*/>')
        $xml = $xml.Insert($m.Index + $m.Length, $template.Replace('subarea_8f8f4243', 'subarea_zz_other'))
    } else {
        $g = $xml.IndexOf('<Group Id="group_review"', [System.StringComparison]::Ordinal)
        $close = $xml.IndexOf('</Group>', $g, [System.StringComparison]::Ordinal)
        $xml = $xml.Insert($close, $template)
    }
}
$global:FakeSitemapXml = $xml
$global:FakeEtag = 'W/"1000"'
$global:FakeSitemapModified = [datetime]::Parse('2026-11-03T22:13:29Z').ToUniversalTime()

# ---- app components -----------------------------------------------------------------------------------------------------------------
$global:FakeRows = [System.Collections.Generic.List[object]]::new()
$global:FakePending = [System.Collections.Generic.List[object]]::new()
function Add-FakeRow($List, [int]$Type, [string]$Id) { $List.Add([pscustomobject]@{ componenttype = $Type; objectid = $Id }) }
foreach ($t in 'sprk_invoice', 'sprk_communication', 'sprk_matter') { Add-FakeRow $global:FakeRows 1 $global:FkIds[$t] }
if ($Scenario -in 'Healthy', 'OtherId') { Add-FakeRow $global:FakeRows 1 $global:FkIds['sprk_billingexport'] }
Add-FakeRow $global:FakeRows 60 'aaaaaaaa-0000-4000-8000-000000000001'      # one explicit sprk_invoice form
Add-FakeRow $global:FakeRows 62 $global:FkSitemapId

function New-FakeNotFound { $e = [System.Exception]::new('Not found (fake)'); $e.Data['Status'] = 404; throw $e }

function global:Invoke-RestMethod {
    [CmdletBinding()]
    param([string]$Uri, [string]$Method = 'GET', $Headers, $Body, [string]$ContentType)
    $p = $Uri -replace '^https://[^/]+/api/data/v9\.2/', ''

    if ($Method -ne 'GET') {
        $json = if ($Body) { $Body | ConvertFrom-Json -Depth 30 } else { $null }
        if ($Method -eq 'POST' -and $p -eq 'AddAppComponents') {
            $global:FakeWrites.Add("POST AddAppComponents " + (($json.Components | ForEach-Object { ($_.'@odata.type' -replace '^Microsoft\.Dynamics\.CRM\.', '') + ':' + ($_.entityid + $_.formid + $_.savedqueryid) }) -join ','))
            if ($global:FakeScenario -ne 'NoOp') {
                $target = $global:FakeRows
                if ($global:FakeScenario -eq 'Lag') { $target = $global:FakePending }
                foreach ($c in $json.Components) {
                    $type = switch ($c.'@odata.type') { 'Microsoft.Dynamics.CRM.entity' { 1 } 'Microsoft.Dynamics.CRM.systemform' { 60 } default { 26 } }
                    Add-FakeRow $target $type ($c.entityid + $c.formid + $c.savedqueryid)
                }
            }
            return $null
        }
        if ($Method -eq 'POST' -and $p -eq 'PublishXml') {
            $global:FakeWrites.Add("POST PublishXml $($json.ParameterXml)")
            if ($json.ParameterXml -match '<appmodule>') { foreach ($r in $global:FakePending) { $global:FakeRows.Add($r) }; $global:FakePending.Clear() }
            return $null
        }
        if ($Method -eq 'PATCH' -and $p -match '^sitemaps\(') {
            $global:FakeWrites.Add("PATCH $p if-match=$($Headers['If-Match']) keys=$((@($json.PSObject.Properties.Name)) -join ',')")
            $global:FakeSitemapXml = [string]$json.sitemapxml
            $global:FakeEtag = 'W/"1001"'
            return $null
        }
        $global:FakeWrites.Add("$Method $p")
        return $null
    }

    if ($p -match "^appmodules\?.*uniquename eq 'sprk_MatterManagement'") {
        return [pscustomobject]@{ value = @([pscustomobject]@{ appmoduleid = $global:FkAppId; appmoduleidunique = 'u1'; uniquename = 'sprk_MatterManagement'; name = 'Matter Management'
                    modifiedon = [datetime]::Parse('2026-11-03T22:13:29Z').ToUniversalTime(); publishedon = [datetime]::Parse('2026-11-03T22:13:30Z').ToUniversalTime() }) }
    }
    if ($p -match "^EntityDefinitions\(LogicalName='(\w+)'\)\?\`$select=MetadataId$") { return [pscustomobject]@{ MetadataId = $global:FkIds[$Matches[1]] } }
    if ($p -eq "RetrieveAppComponents(AppModuleId=$global:FkAppId)") { return [pscustomobject]@{ value = @($global:FakeRows) } }
    if ($p -match '^sitemaps\?.*sprk_MatterManagement') { return [pscustomobject]@{ value = @([pscustomobject]@{ sitemapid = $global:FkSitemapId; sitemapnameunique = 'sprk_MatterManagement'; modifiedon = $global:FakeSitemapModified }) } }
    if ($p -match "^sitemaps\($global:FkSitemapId\)") {
        return [pscustomobject]@{ sitemapxml = $global:FakeSitemapXml; modifiedon = $global:FakeSitemapModified; '@odata.etag' = $global:FakeEtag }
    }
    if ($p -match "^appmodules\($global:FkAppId\)/Microsoft\.Dynamics\.CRM\.RetrieveUnpublished\(\)") { return [pscustomobject]@{ modifiedon = [datetime]::Parse('2026-11-03T22:13:29Z').ToUniversalTime() } }
    if ($p -match "^appmodules\($global:FkAppId\)\?") { return [pscustomobject]@{ modifiedon = [datetime]::Parse('2026-11-03T22:13:29Z').ToUniversalTime() } }
    if ($p -match '^appsettings') { return [pscustomobject]@{ value = @() } }
    if ($p -eq "ValidateApp(AppModuleId=$global:FkAppId)") {
        return [pscustomobject]@{ AppValidationResponse = [pscustomobject]@{ ValidationSuccess = $true; ValidationIssueList = @(
                    [pscustomobject]@{ ErrorType = 'Warning'; Message = "Spend Signal doesn$([char]0x2019)t reference a form or view. App users will see all forms and views."; DisplayName = $null; ComponentType = 0; RequiredComponents = @() }
                    [pscustomobject]@{ ErrorType = 'Warning'; Message = "Billing Export doesn$([char]0x2019)t reference a form or view. App users will see all forms and views."; DisplayName = $null; ComponentType = 0; RequiredComponents = @() }
                    [pscustomobject]@{ ErrorType = 'Warning'; Message = $null; DisplayName = 'Site Map'; ComponentType = 62; RequiredComponents = @([pscustomobject]@{ DisplayName = 'AI Persona'; SchemaName = 'sprk_aipersona'; ComponentType = 1 }) }) } }
    }
    if ($p -match '^systemforms\?.*objecttypecode eq ''(\w+)''') {
        $t = $Matches[1]
        if ($t -eq 'sprk_billingexport') { return [pscustomobject]@{ value = @([pscustomobject]@{ formid = $global:FkMainForm; name = 'Information'; type = 2; isdefault = $false; formactivationstate = 1 }) } }
        return [pscustomobject]@{ value = @([pscustomobject]@{ formid = 'aaaaaaaa-0000-4000-8000-000000000001'; name = "$t main"; type = 2; isdefault = $false; formactivationstate = 1 }) }
    }
    if ($p -match "^systemforms\($global:FkMainForm\)\?.*formxml") {
        return [pscustomobject]@{ name = 'Information'; formxml = '<form><tabs><tab><columns><column><sections><section><rows><row><cell><control id="subgrid_apemails"><parameters><TargetEntityType>sprk_communication</TargetEntityType><RelationshipName>sprk_communication_billingexport_billingexport</RelationshipName><ViewId>{b0b03a6c-a438-468d-8a62-55744c392fd1}</ViewId></parameters></control></cell></row></rows></section></sections></column></columns></tab></tabs></form>' }
    }
    if ($p -match "^systemforms\($global:FkMainForm\)\?") { return [pscustomobject]@{ formid = $global:FkMainForm; name = 'Information'; type = 2; objecttypecode = 'sprk_billingexport'; formactivationstate = 1 } }
    if ($p -match "^savedqueries\?.*returnedtypecode eq 'sprk_billingexport'") {
        return [pscustomobject]@{ value = @([pscustomobject]@{ savedqueryid = $global:FkSourceView; name = 'Active Billing Exports'; querytype = 0 }, [pscustomobject]@{ savedqueryid = 'eeeeeeee-0000-4000-8000-00000000000e'; name = 'A view that is not in source'; querytype = 0 }) }
    }
    if ($p -match '^savedqueries\(') { return [pscustomobject]@{ name = 'All Outgoing Email' } }
    if ($p -match "^EntityDefinitions\(LogicalName='sprk_communication'\)/ManyToOneRelationships") {
        return [pscustomobject]@{ value = @([pscustomobject]@{ SchemaName = 'sprk_communication_billingexport_billingexport'; ReferencingAttribute = 'sprk_billingexport' }) }
    }
    if ($p -match '^RetrieveEntityRibbon') { throw 'FAKE: the ribbon is not modelled' }
    throw "FAKE: unexpected GET $p"
}

$splat = @{ BackupDir = $BackupDir }
if ($ExplicitFormsAndViews) { $splat.ExplicitFormsAndViews = $true }
switch ($Mode) {
    'WhatIf' { $splat.WhatIf = $true }
    'Verify' { $splat.Verify = $true }
    'Apply' { $splat.Apply = $true; $splat.OwnerApproval = $true }
    'ApplyNoApproval' { $splat.Apply = $true }
}
& $ScriptPath @splat
"RUNNER-EXIT: $LASTEXITCODE"
"RUNNER-WRITES: $($global:FakeWrites.Count)"
$global:FakeWrites | ForEach-Object { "RUNNER-WRITE: $_" }
"RUNNER-SUBAREAS: " + ([regex]::Matches($global:FakeSitemapXml, '<SubArea\b[^<>]*\bEntity="sprk_billingexport"')).Count
"RUNNER-TABLE: " + $(if (@($global:FakeRows | Where-Object { $_.componenttype -eq 1 -and $_.objectid -eq $global:FkIds['sprk_billingexport'] }).Count) { 'yes' } else { 'no' })
