# Pester 3.4.0 (legacy `Should Be` syntax; the Pester 5/6 modules reject it). Run from the repo root:
#   pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 3.4.0; Invoke-Pester tests/scripts/Load-DemoSampleData.Tests.ps1"
# A bare `Should Throw` always fails under 3.4: every throw test below passes the expected message.
#
# Data plan D6 (projects/customer-provisioning-orchestration-r1/notes/data-setup-plan.md): demo dataset v2 and the
# refreshed loader. Offline only - no Dataverse, no az. Covers:
#   * the dataset (scripts/demo-data/demo-records.json) against the reference snapshot (reference-names.json) and the
#     table specs: every lookup reference exists, every required reference is present, no duplicate natural keys,
#     every row would be tagged and owned;
#   * the validator itself (each rule must fire on a bad copy - a validator that cannot fail proves nothing);
#   * the loader's pure functions (scripts/demo-data/DemoDataLib.ps1): tag, payload, reset order, what a reset may
#     delete, the demo guard, operator identity, owner plan, URL and date handling;
#   * source guards on scripts/Load-DemoSampleData.ps1: the only Web API call is inside Invoke-Dv, the only writes go
#     through Invoke-DvWrite, no method other than GET/POST/DELETE, no secret or az context changes.

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:Loader = Join-Path $script:RepoRoot 'scripts' 'Load-DemoSampleData.ps1'
$script:Lib = Join-Path $script:RepoRoot 'scripts' 'demo-data' 'DemoDataLib.ps1'
$script:DataFile = Join-Path $script:RepoRoot 'scripts' 'demo-data' 'demo-records.json'
$script:RefFile = Join-Path $script:RepoRoot 'scripts' 'demo-data' 'reference-names.json'
$script:EventTypeSeed = Join-Path $script:RepoRoot 'scripts' 'seed-data' 'reference' 'sprk_eventtype_ref.json'

. $script:Lib

function Get-FreshDataset { return (Get-Content $script:DataFile -Raw -Encoding UTF8 | ConvertFrom-Json) }
function Get-Reference { return (Get-Content $script:RefFile -Raw -Encoding UTF8 | ConvertFrom-Json) }

function New-FakeContext {
    <# A resolved context with made-up ids: what the loader would hold after resolving a healthy stamp. #>
    param($Dataset, [string]$OwnerTeamId = '11111111-1111-4111-8111-111111111111')
    $refIds = @{}
    $optionValues = @{}
    $n = 100000000
    foreach ($r in (Get-DemoReferenceRequirements $Dataset)) {
        if ($r.Kind -eq 'taxonomy') {
            if (-not $refIds.ContainsKey($r.Table)) { $refIds[$r.Table] = @{} }
            $refIds[$r.Table][$r.Name] = [guid]::NewGuid().ToString()
        }
        else {
            $k = "$($r.Entity).$($r.Column)"
            if (-not $optionValues.ContainsKey($k)) { $optionValues[$k] = @{} }
            $optionValues[$k][$r.Name] = $n++
        }
    }
    $nav = @{}
    $keyIds = @{}
    $keyInfo = @{}
    foreach ($spec in (Get-DemoTableSpecs)) {
        $nav[$spec.Logical] = @{}
        foreach ($c in (Get-DemoLookupColumns $spec)) { $nav[$spec.Logical][$c] = "nav_$c" }
        $keyIds[$spec.Section] = @{}
        $keyInfo[$spec.Section] = @{}
        foreach ($row in @($Dataset.($spec.Section))) {
            $keyIds[$spec.Section][$row.key] = [guid]::NewGuid().ToString()
            $f = Get-DemoRowProperty $row 'fields'
            $keyInfo[$spec.Section][$row.key] = @{ Name = $(if ($spec.DisplayName) { "$($f[$spec.DisplayName])" }); Number = $(if ($spec.DisplayNumber) { "$($f[$spec.DisplayNumber])" }) }
        }
    }
    return @{ Today = [datetime]'2026-11-25'; OwnerTeamId = $OwnerTeamId; Nav = $nav; RefIds = $refIds; OptionValues = $optionValues; KeyIds = $keyIds; KeyInfo = $keyInfo }
}

Describe 'Demo dataset v2 (scripts/demo-data/demo-records.json)' {
    $dataset = Get-FreshDataset
    $reference = Get-Reference

    It 'is valid against the table specs and the reference snapshot (0 errors)' {
        $errors = @(Test-DemoDataset -Dataset $dataset -Reference $reference)
        ($errors -join "`n") | Should Be ''
        $errors.Count | Should Be 0
    }

    It 'is versioned' {
        $dataset.datasetVersion | Should Match '^\d+\.\d+\.\d+$'
    }

    It 'has rows in every section and the plan''s record kinds (counts)' {
        foreach ($section in 'organizations', 'contacts', 'matters', 'projects', 'events', 'todos', 'communications', 'documents') {
            (@($dataset.$section).Count -gt 0) | Should Be $true
        }
        @($dataset.matters).Count | Should Be 10
        @($dataset.events).Count | Should Be 29
    }

    It 'types every organization, every contact, every project and every matter (type + practice area)' {
        foreach ($o in @($dataset.organizations)) { [string]::IsNullOrEmpty($o.reference.organizationType) | Should Be $false }
        foreach ($c in @($dataset.contacts)) { [string]::IsNullOrEmpty($c.reference.contactType) | Should Be $false }
        foreach ($p in @($dataset.projects)) { [string]::IsNullOrEmpty($p.reference.projectType) | Should Be $false }
        foreach ($m in @($dataset.matters)) {
            [string]::IsNullOrEmpty($m.reference.matterType) | Should Be $false
            [string]::IsNullOrEmpty($m.reference.practiceArea) | Should Be $false
        }
    }

    It 'types every event with one of the 17 product event types, by code, and covers 16 of them' {
        $codes = @($dataset.events | ForEach-Object { $_.reference.eventTypeCode } | Select-Object -Unique)
        foreach ($c in $codes) { (@($reference.eventTypes | Where-Object { $_.code -ceq $c }).Count) | Should Be 1 }
        $codes.Count | Should Be 16
        @($reference.eventTypes).Count | Should Be 17
    }

    It 'gives every row of every section a unique key and a unique natural key within its section' {
        foreach ($spec in (Get-DemoTableSpecs)) {
            $nks = @($dataset.($spec.Section) | ForEach-Object { (Get-DemoNaturalKeyValue -Spec $spec -Row $_).ToLowerInvariant() })
            @($nks | Select-Object -Unique).Count | Should Be $nks.Count
        }
        $keys = @(foreach ($spec in (Get-DemoTableSpecs)) { @($dataset.($spec.Section)) | ForEach-Object { $_.key } })
        @($keys | Select-Object -Unique).Count | Should Be $keys.Count
    }

    It 'files every event, to-do and communication under exactly one matter or project' {
        foreach ($section in 'events', 'todos', 'communications') {
            foreach ($row in @($dataset.$section)) {
                $n = @($row.links.PSObject.Properties | Where-Object { $_.Name -in 'regardingMatter', 'regardingProject' }).Count
                $n | Should Be 1
            }
        }
    }

    It 'files every document under a matter or a project' {
        foreach ($row in @($dataset.documents)) {
            $n = @($row.links.PSObject.Properties | Where-Object { $_.Name -in 'matter', 'project' }).Count
            $n | Should Be 1
        }
    }

    It 'builds a TAGGED, team-owned payload for every row (every row tagged)' {
        $ctx = New-FakeContext -Dataset $dataset
        $built = 0
        foreach ($spec in (Get-DemoTableSpecs)) {
            foreach ($row in @($dataset.($spec.Section))) {
                $p = New-DemoPayload -Spec $spec -Row $row -Context $ctx
                $p['importsequencenumber'] | Should Be (Get-DemoImportSequenceNumber)
                $p['ownerid@odata.bind'] | Should Be '/teams(11111111-1111-4111-8111-111111111111)'
                $built++
            }
        }
        $built | Should Be 123
    }

    It 'writes document rows as metadata only: sprk_hasfile false and no file pointer column' {
        $ctx = New-FakeContext -Dataset $dataset
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'documents' }
        foreach ($row in @($dataset.documents)) {
            $p = New-DemoPayload -Spec $spec -Row $row -Context $ctx
            $p['sprk_hasfile'] | Should Be $false
            foreach ($col in 'sprk_graphitemid', 'sprk_graphdriveid', 'sprk_driveitemid', 'sprk_graphitemidbound', 'sprk_filename', 'sprk_filepath', 'sprk_containerid') {
                $p.Contains($col) | Should Be $false
            }
        }
    }

    It 'does not write eBilling rows (owner decision F: invoices, budgets, billing events are out of v2)' {
        $sections = @(Get-DemoSectionOrder)
        foreach ($s in 'invoices', 'budgets', 'billingEvents') { ($sections -contains $s) | Should Be $false }
        $logicals = @(Get-DemoTableSpecs | ForEach-Object { $_.Logical })
        foreach ($l in 'sprk_invoice', 'sprk_budget', 'sprk_billingevent', 'sprk_invoicesubmission') { ($logicals -contains $l) | Should Be $false }
        [string]::IsNullOrEmpty($dataset.extensionPoints.ebilling) | Should Be $false
    }

    It 'only names sample files that exist' {
        foreach ($d in @($dataset.documents | Where-Object { $_.sampleFile })) {
            (Test-Path (Join-Path $script:RepoRoot 'scripts' 'demo-data' 'sample-documents' $d.sampleFile)) | Should Be $true
        }
    }

    It 'reference snapshot event types equal the ISS-039 seed file once that file is on master' {
        if (-not (Test-Path $script:EventTypeSeed)) { return }   # not on master yet: nothing to pin against
        $seed = Get-Content $script:EventTypeSeed -Raw | ConvertFrom-Json
        foreach ($r in @($seed.rows)) {
            $mine = @($reference.eventTypes | Where-Object { $_.code -ceq $r.sprk_eventtypecode })
            $mine.Count | Should Be 1
            $mine[0].id | Should Be $r.sprk_eventtype_refid
            [bool]$mine[0].requiresDueDate | Should Be ([bool]$r.sprk_requiredduedate)
            [bool]$mine[0].requiresBaseDate | Should Be ([bool]$r.sprk_requiresbasedate)
        }
        @($seed.rows).Count | Should Be @($reference.eventTypes).Count
    }
}

Describe 'Test-DemoDataset fires on bad data (a validator that cannot fail proves nothing)' {
    $reference = Get-Reference

    It 'flags a duplicate natural key' {
        $d = Get-FreshDataset
        $d.matters[1].fields.sprk_matternumber = $d.matters[0].fields.sprk_matternumber
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match 'natural key .* duplicates'
    }
    It 'flags a duplicate row key' {
        $d = Get-FreshDataset
        $d.contacts[1].key = $d.contacts[0].key
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match 'duplicate key'
    }
    It 'flags a matter type that is not in the taxonomy' {
        $d = Get-FreshDataset
        $d.matters[0].reference.matterType = 'Nonexistent Matter Type'
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "matterType 'Nonexistent Matter Type' is not a sprk_mattertype_ref name"
    }
    It 'flags an event type code that is not one of the 17' {
        $d = Get-FreshDataset
        $d.events[0].reference.eventTypeCode = 'ZZZZ'
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "event type code 'ZZZZ'"
    }
    It 'flags an unknown choice label' {
        $d = Get-FreshDataset
        $d.documents[0].options.documentType = 'Napkin'
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "documentType 'Napkin' is not an option label of sprk_document.sprk_documenttype"
    }
    It 'flags a link to a row that does not exist' {
        $d = Get-FreshDataset
        $d.matters[0].links.assignedLawFirm1 = 'org-nope'
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "org-nope' is not a row key in section 'organizations'"
    }
    It 'flags a matter without a matter type or practice area' {
        $d = Get-FreshDataset
        $d.matters[0].reference.PSObject.Properties.Remove('practiceArea')
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "'practiceArea' is required"
    }
    It 'flags an event that is filed under nothing' {
        $d = Get-FreshDataset
        $d.events[0].links.PSObject.Properties.Remove('regardingMatter')
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match 'exactly one matter or project'
    }
    It 'flags a TASK event with no due date' {
        $d = Get-FreshDataset
        $d.events[0].dates.PSObject.Properties.Remove('sprk_duedate')
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match 'requires a due date'
    }
    It 'flags a column the loader owns (the tag, the owner, a file pointer)' {
        $d = Get-FreshDataset
        $d.matters[0].fields | Add-Member -NotePropertyName importsequencenumber -NotePropertyValue 1
        $d.documents[0].fields | Add-Member -NotePropertyName sprk_graphitemid -NotePropertyValue 'x'
        $text = (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n")
        $text | Should Match 'fields.importsequencenumber is set by the loader'
        $text | Should Match 'fields.sprk_graphitemid is set by the loader'
    }
    It 'flags a malformed date offset' {
        $d = Get-FreshDataset
        $d.events[0].dates.sprk_duedate = 'next tuesday'
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "Date offset 'next tuesday'"
    }
    It 'flags a missing or malformed datasetVersion' {
        $d = Get-FreshDataset
        $d.datasetVersion = 'two'
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match 'datasetVersion'
    }
    It 'flags an empty section' {
        $d = Get-FreshDataset
        $d.todos = @()
        (@(Test-DemoDataset -Dataset $d -Reference $reference) -join "`n") | Should Match "Section 'todos' is missing or empty"
    }
}

Describe 'The tag' {
    It 'is the pinned constant (changing it orphans rows already loaded: do not)' {
        Get-DemoImportSequenceNumber | Should Be 2026112501
    }
    It 'is the only thing reset filters on' {
        Get-DemoTaggedFilter | Should Be 'importsequencenumber eq 2026112501'
    }
    It 'finds untagged rows by tag absence or difference' {
        Get-DemoUntaggedFilter -LookupColumn 'sprk_regardingmatter' | Should Be '_sprk_regardingmatter_value ne null and (importsequencenumber eq null or importsequencenumber ne 2026112501)'
    }
    It 'is carried by a payload built with no owner too' {
        $d = Get-FreshDataset
        $ctx = New-FakeContext -Dataset $d -OwnerTeamId $null
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'organizations' }
        $p = New-DemoPayload -Spec $spec -Row $d.organizations[0] -Context $ctx
        $p['importsequencenumber'] | Should Be 2026112501
        $p.Contains('ownerid@odata.bind') | Should Be $false
    }
}

Describe 'New-DemoPayload' {
    $dataset = Get-FreshDataset

    It 'binds the typed regarding lookup and stamps the regarding columns for an event' {
        $ctx = New-FakeContext -Dataset $dataset
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'events' }
        $row = $dataset.events[0]
        $p = New-DemoPayload -Spec $spec -Row $row -Context $ctx
        $matterId = $ctx.KeyIds['matters'][$row.links.regardingMatter]
        $p['nav_sprk_regardingmatter@odata.bind'] | Should Be "/sprk_matters($matterId)"
        $p['sprk_regardingrecordid'] | Should Be $matterId
        $p['sprk_regardingrecordnumber'] | Should Be 'MTR-2026-001'
        ($p['sprk_regardingrecordname'] -like 'Meridian Corp v. Pinnacle*') | Should Be $true
        $p['sprk_eventtypecode'] | Should Be 'TASK'
        $p['sprk_duedate'] | Should Be '2026-12-04'
        $p['nav_sprk_regardingrecordtype@odata.bind'] | Should Match '^/sprk_recordtype_refs\('
    }
    It 'writes a choice as the value the target reports for the label' {
        $ctx = New-FakeContext -Dataset $dataset
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'events' }
        $p = New-DemoPayload -Spec $spec -Row $dataset.events[0] -Context $ctx
        $p['sprk_eventstatus'] | Should Be $ctx.OptionValues['sprk_event.sprk_eventstatus']['Open']
    }
    It 'refuses to build a row whose parent has no id (a failed parent never leaves a dangling child)' {
        $ctx = New-FakeContext -Dataset $dataset
        $ctx.KeyIds['matters'].Remove('mt-pinnacle')
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'events' }
        { New-DemoPayload -Spec $spec -Row $dataset.events[0] -Context $ctx } | Should Throw "has no row id (the parent was not created)"
    }
    It 'refuses to build a row whose reference value was not resolved' {
        $ctx = New-FakeContext -Dataset $dataset
        $ctx.RefIds['sprk_mattertype_ref'].Remove('Litigation')
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'matters' }
        { New-DemoPayload -Spec $spec -Row $dataset.matters[0] -Context $ctx } | Should Throw "sprk_mattertype_ref 'Litigation' was not resolved"
    }
}

Describe 'Dependency order and reset' {
    It 'creates parents before children: every link points at an earlier section' {
        $order = @(Get-DemoSectionOrder)
        foreach ($spec in (Get-DemoTableSpecs)) {
            foreach ($l in $spec.Links.Values) { ($order.IndexOf($l.Section) -lt $order.IndexOf($spec.Section)) | Should Be $true }
        }
    }
    It 'resets in the reverse order: documents first, organizations last' {
        $reset = @(Get-DemoResetOrder)
        $reset[0] | Should Be 'documents'
        $reset[$reset.Count - 1] | Should Be 'organizations'
        ($reset -join ',') | Should Be 'documents,communications,todos,events,projects,matters,contacts,organizations'
    }
    It 'resets every child before the parents it points at' {
        $reset = @(Get-DemoResetOrder)
        foreach ($spec in (Get-DemoTableSpecs)) {
            foreach ($l in $spec.Links.Values) { ($reset.IndexOf($spec.Section) -lt $reset.IndexOf($l.Section)) | Should Be $true }
        }
    }
    It 'resetting never reorders the create order in place' {
        $before = (Get-DemoSectionOrder) -join ','
        $null = Get-DemoResetOrder
        ((Get-DemoSectionOrder) -join ',') | Should Be $before
    }

    It 'deletes a tagged row nothing untagged points at' {
        $rows = @([pscustomobject]@{ Id = 'AAAAAAAA-0000-4000-8000-000000000001'; Name = 'MTR-2026-001' })
        $plan = Select-DemoDeletableRows -Rows $rows -UntaggedReferences @{}
        @($plan.Deletable).Count | Should Be 1
        @($plan.Kept).Count | Should Be 0
    }
    It 'keeps a tagged row an untagged row points at, and says why (case-insensitive ids)' {
        $rows = @(
            [pscustomobject]@{ Id = 'AAAAAAAA-0000-4000-8000-000000000001'; Name = 'MTR-2026-001' },
            [pscustomobject]@{ Id = 'AAAAAAAA-0000-4000-8000-000000000002'; Name = 'MTR-2026-002' })
        $refs = @{ 'aaaaaaaa-0000-4000-8000-000000000001' = @('sprk_event.sprk_regardingmatter', 'sprk_event.sprk_regardingmatter', 'sprk_todo.sprk_regardingmatter') }
        $plan = Select-DemoDeletableRows -Rows $rows -UntaggedReferences $refs
        @($plan.Deletable).Count | Should Be 1
        $plan.Deletable[0].Name | Should Be 'MTR-2026-002'
        @($plan.Kept).Count | Should Be 1
        $plan.Kept[0].Name | Should Be 'MTR-2026-001'
        (@($plan.Kept[0].Reasons) -join ',') | Should Be 'sprk_event.sprk_regardingmatter,sprk_todo.sprk_regardingmatter'
    }
    It 'handles no rows at all' {
        $plan = Select-DemoDeletableRows -Rows @() -UntaggedReferences @{}
        @($plan.Deletable).Count | Should Be 0
        @($plan.Kept).Count | Should Be 0
    }
}

Describe 'Demo guard (Test-DemoTargetAllowed)' {
    function Row($type, $active = $true, $name = 'Test Customer') { [pscustomobject]@{ sprk_name = $name; sprk_environmenttype = $type; sprk_isactive = $active } }

    It 'allows an active Demo environment' {
        $r = Test-DemoTargetAllowed -RegistryRow (Row 1)
        $r.Allowed | Should Be $true
        @($r.Warnings).Count | Should Be 0
    }
    It 'refuses a Development, Sandbox, Trial, Partner or Training environment' {
        foreach ($t in 0, 2, 3, 4, 5) {
            $r = Test-DemoTargetAllowed -RegistryRow (Row $t)
            $r.Allowed | Should Be $false
            (@($r.Reasons) -join ' ') | Should Match 'not Demo'
        }
    }
    It 'refuses a row with no environment type (ACME-style rows that were never marked)' {
        $r = Test-DemoTargetAllowed -RegistryRow (Row $null)
        $r.Allowed | Should Be $false
        (@($r.Reasons) -join ' ') | Should Match '\(not set\)'
    }
    It 'refuses an inactive Demo environment' {
        $r = Test-DemoTargetAllowed -RegistryRow (Row 1 $false)
        $r.Allowed | Should Be $false
        (@($r.Reasons) -join ' ') | Should Match 'not active'
    }
    It 'refuses a target with no registry row' {
        $r = Test-DemoTargetAllowed -RegistryRow $null
        $r.Allowed | Should Be $false
        (@($r.Reasons) -join ' ') | Should Match 'no environment registry row'
    }
    It '-AllowNonDemo accepts a non-demo row and a missing row, with a warning each' {
        $r = Test-DemoTargetAllowed -RegistryRow (Row 0) -AllowNonDemo
        $r.Allowed | Should Be $true
        (@($r.Warnings) -join ' ') | Should Match '-AllowNonDemo'
        $r2 = Test-DemoTargetAllowed -RegistryRow $null -AllowNonDemo
        $r2.Allowed | Should Be $true
        @($r2.Warnings).Count | Should Be 1
    }
    It 'refuses Production even with -AllowNonDemo' {
        $r = Test-DemoTargetAllowed -RegistryRow (Row 6) -AllowNonDemo
        $r.Allowed | Should Be $false
        (@($r.Reasons) -join ' ') | Should Match 'Production'
    }
    It 'names the environment type in words' {
        Get-DemoEnvironmentTypeName 1 | Should Be 'Demo'
        Get-DemoEnvironmentTypeName 6 | Should Be 'Production'
        Get-DemoEnvironmentTypeName $null | Should Be '(not set)'
    }
}

Describe 'Target URL, operator identity, owner plan' {
    It 'normalises a Dataverse URL (trailing slash, case)' {
        Resolve-DemoDataverseUrl 'https://Spaarke-Acme.crm.dynamics.com/' | Should Be 'https://spaarke-acme.crm.dynamics.com'
        Resolve-DemoDataverseUrl 'https://org.crm4.dynamics.com' | Should Be 'https://org.crm4.dynamics.com'
    }
    It 'refuses a URL that is not a Dataverse org host' {
        { Resolve-DemoDataverseUrl 'http://spaarke-acme.crm.dynamics.com' } | Should Throw "is not a Dataverse environment URL"
        { Resolve-DemoDataverseUrl 'https://spaarke-acme.crm.dynamics.com/api/data' } | Should Throw "is not a Dataverse environment URL"
        { Resolve-DemoDataverseUrl 'https://example.com' } | Should Throw "is not a Dataverse environment URL"
    }
    It 'accepts a signed-in user and refuses a service principal or no user (NFR-11)' {
        (Test-DemoOperatorIdentity -Account ([pscustomobject]@{ user = [pscustomobject]@{ name = 'a@b.com'; type = 'user' } })).Allowed | Should Be $true
        $sp = Test-DemoOperatorIdentity -Account ([pscustomobject]@{ user = [pscustomobject]@{ name = 'app-id'; type = 'servicePrincipal' } })
        $sp.Allowed | Should Be $false
        $sp.Reason | Should Match 'service principal'
        (Test-DemoOperatorIdentity -Account ([pscustomobject]@{ user = [pscustomobject]@{ name = $null; type = 'user' } })).Allowed | Should Be $false
    }
    It 'owner plan: the customer unit is named after the registry row (H10 convention), default team' {
        $plan = Get-DemoOwnerPlan -RegistryRow ([pscustomobject]@{ sprk_name = 'ACME Corporation'; sprk_businessunitname = $null; sprk_teamname = $null })
        $plan.BusinessUnitName | Should Be 'ACME Corporation'
        $plan.TeamName | Should Be $null
    }
    It 'owner plan: an explicit unit and team on the registry row win over the customer name' {
        $plan = Get-DemoOwnerPlan -RegistryRow ([pscustomobject]@{ sprk_name = 'Dev'; sprk_businessunitname = 'Spaarke Demo'; sprk_teamname = 'Spaarke Demo Team' })
        $plan.BusinessUnitName | Should Be 'Spaarke Demo'
        $plan.TeamName | Should Be 'Spaarke Demo Team'
    }
    It 'owner plan: parameters win over the registry' {
        $plan = Get-DemoOwnerPlan -RegistryRow ([pscustomobject]@{ sprk_name = 'X' }) -OwnerBusinessUnitName 'Other Unit' -OwnerTeamName 'Other Team'
        $plan.BusinessUnitName | Should Be 'Other Unit'
        $plan.TeamName | Should Be 'Other Team'
        $plan.Source | Should Be 'parameter'
    }
    It 'owner plan: no row and no parameter gives no unit' {
        (Get-DemoOwnerPlan -RegistryRow $null).BusinessUnitName | Should Be $null
    }
    It 'never owns demo rows in the Secure Record or ontology writer units' {
        Test-DemoReservedBusinessUnit 'Secure Record' | Should Be $true
        Test-DemoReservedBusinessUnit 'secure record' | Should Be $true   # Dataverse names are case-insensitive
        Test-DemoReservedBusinessUnit 'Spaarke Ontology Writer' | Should Be $true
        Test-DemoReservedBusinessUnit 'ACME Corporation' | Should Be $false
    }
}

Describe 'Dates and OData strings' {
    It 'turns a day offset into a date, or a UTC time with an HH:mm part' {
        $today = [datetime]'2026-11-25 17:45:00'
        ConvertTo-DemoDateValue -Offset '+14d' -Today $today | Should Be '2026-12-09'
        ConvertTo-DemoDateValue -Offset '14d' -Today $today | Should Be '2026-12-09'
        ConvertTo-DemoDateValue -Offset '-3d' -Today $today | Should Be '2026-11-22'
        ConvertTo-DemoDateValue -Offset '0d' -Today $today | Should Be '2026-11-25'
        ConvertTo-DemoDateValue -Offset '+3d 16:00' -Today $today | Should Be '2026-11-28T16:00:00Z'
        ConvertTo-DemoDateValue -Offset '-390d 09:05' -Today $today | Should Be '2025-10-31T09:05:00Z'
    }
    It 'refuses anything else, naming the value' {
        { ConvertTo-DemoDateValue -Offset 'tomorrow' -Today ([datetime]'2026-11-25') } | Should Throw "Date offset 'tomorrow'"
        { ConvertTo-DemoDateValue -Offset '3d 25:00' -Today ([datetime]'2026-11-25') } | Should Throw "Date offset '3d 25:00'"
    }
    It 'doubles single quotes for an OData string literal' {
        ConvertTo-DemoODataString "Pinnacle's" | Should Be "Pinnacle''s"
    }
}

Describe 'Reference requirements (what the loader resolves before it writes)' {
    $dataset = Get-FreshDataset
    $reqs = @(Get-DemoReferenceRequirements $dataset)

    It 'lists every taxonomy name, event type code, record type and choice label the dataset uses, once each' {
        $keys = @($reqs | ForEach-Object { "$($_.Kind)|$($_.Table)|$($_.Entity)|$($_.Column)|$($_.Name)" })
        @($keys | Select-Object -Unique).Count | Should Be $keys.Count
        (@($reqs | Where-Object { $_.Table -eq 'sprk_eventtype_ref' }).Count) | Should Be 16
        (@($reqs | Where-Object { $_.Table -eq 'sprk_recordtype_ref' } | ForEach-Object { $_.Name } | Sort-Object) -join ',') | Should Be 'sprk_matter,sprk_project'
        (@($reqs | Where-Object { $_.Table -eq 'sprk_mattertype_ref' }).Count) | Should Be 10
    }
    It 'every lookup column the loader binds is a column it writes (schema check covers it)' {
        foreach ($spec in (Get-DemoTableSpecs)) {
            $cols = @(Get-DemoSpecColumns $spec)
            foreach ($c in (Get-DemoLookupColumns $spec)) { ($cols -contains $c) | Should Be $true }
            ($cols -contains 'importsequencenumber') | Should Be $true
            ($cols -contains 'ownerid') | Should Be $true
        }
    }
}

Describe 'Loader source guards (scripts/Load-DemoSampleData.ps1)' {
    $parseErrors = $null
    $tokens = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:Loader, [ref]$tokens, [ref]$parseErrors)
    $text = Get-Content $script:Loader -Raw

    function Get-EnclosingFunctionName($node) {
        $p = $node.Parent
        while ($p) { if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $p.Name }; $p = $p.Parent }
        return $null
    }

    It 'parses without errors' {
        @($parseErrors).Count | Should Be 0
    }
    It 'supports -WhatIf (SupportsShouldProcess) and asks before every create and delete' {
        $text | Should Match 'SupportsShouldProcess'
        ([regex]::Matches($text, '\$PSCmdlet\.ShouldProcess\(')).Count | Should Be 2
    }
    It 'keeps -DryRun as the same thing as -WhatIf' {
        $text | Should Match 'if \(\$DryRun\) \{ \$WhatIfPreference = \$true \}'
    }
    It 'makes every Web API call from inside Invoke-Dv' {
        $calls = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -in 'Invoke-RestMethod', 'Invoke-WebRequest', 'Invoke-Expression', 'curl' }, $true)
        @($calls).Count | Should Be 2
        foreach ($c in $calls) { (Get-EnclosingFunctionName $c) | Should Be 'Invoke-Dv' }
    }
    It 'uses no method but GET, POST and DELETE, and writes only through Invoke-DvWrite' {
        $dvCalls = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-Dv' }, $true)
        foreach ($c in $dvCalls) {
            $s = $c.Extent.Text
            if ($s -match '-Method\s+(POST|DELETE)') { (Get-EnclosingFunctionName $c) | Should Be 'Invoke-DvWrite' }
        }
        $text | Should Not Match '-Method\s+(PATCH|PUT|MERGE)'
        $text | Should Not Match "ValidateSet\('GET', 'POST', 'DELETE', '"
    }
    It 'refuses a write under -WhatIf even if a caller forgot to ask' {
        $text | Should Match "if \(\`$WhatIfPreference\) \{ throw 'Internal error: a write was attempted under -WhatIf.' \}"
    }
    It 'never switches the az subscription, reads a secret or a key vault, or signs in as anything else' {
        $text | Should Not Match 'az account set'
        # 'az login' appears only inside operator-facing error messages; it must never be a command the script runs.
        $text | Should Not Match '(?m)^\s*(&\s*)?az\s+login'
        $text | Should Match "Run 'az login' as yourself"
        $text | Should Not Match 'keyvault'
        $text | Should Not Match '(?i)client_?secret'
        $text | Should Not Match '--service-principal'
    }
    It 'deletes only by the tag filter and never offers -Force' {
        ([regex]::Matches($text, 'Get-DemoTaggedFilter')).Count | Should Be 2
        $text | Should Not Match '\[switch\]\$Force'
    }
    It 'no longer touches AI seed data, SPE, chart definitions or AI Search (product configuration, not demo data)' {
        $text | Should Not Match 'Deploy-All-AI-SeedData'
        $text | Should Not Match 'Sync-RecordsToIndex'
        $text | Should Not Match 'spaarke-search-prod'
        $text | Should Not Match 'sprk_chartdefinitions'
    }
}
