# Pester 3.4.0 (legacy `Should Be` syntax; the Pester 5/6 modules reject it). Run from the repo root:
#   pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 3.4.0; Invoke-Pester tests/scripts/Load-DemoSampleData.Tests.ps1"
# A bare `Should Throw` always fails under 3.4: every throw test below passes the expected message.
#
# Data plan D6 (projects/customer-provisioning-orchestration-r1/notes/data-setup-plan.md): demo dataset v2 and the
# refreshed loader. Offline only - no Dataverse, no az. Covers:
#   * the dataset (scripts/demo-data/demo-records.json) against the seed files themselves (D4 taxonomy pack, event-type seed) and the
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
$script:PackDir = Join-Path $script:RepoRoot 'scripts' 'seed-data' 'reference' 'packs' 'legal-standard'
$script:PackTables = @{ sprk_mattertype_ref = 1; sprk_practicearea_ref = 1; sprk_projecttype_ref = 1; sprk_contacttype_ref = 1; sprk_organizationtype_ref = 1 }

. $script:Lib

function Get-FreshDataset { return (Get-Content $script:DataFile -Raw -Encoding UTF8 | ConvertFrom-Json) }
function Get-Reference { return (Get-DemoSeedReference -ScriptsRoot (Join-Path $script:RepoRoot 'scripts')) }

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

    # The dataset is pinned to the SEED FILES, read here directly (not through the loader's own reader, which would make
    # the check circular). A missing file FAILS: there is no "not on master yet" skip any more.
    It 'finds the D4 pack files and the event-type seed (a missing file fails, it is not skipped)' {
        (Test-Path $script:PackDir) | Should Be $true
        (Test-Path $script:EventTypeSeed) | Should Be $true
        foreach ($t in $script:PackTables.Keys) { (Test-Path (Join-Path $script:PackDir "$t.json")) | Should Be $true }
    }
    It 'uses a name that exists in the D4 pack for every taxonomy lookup (exact: Patent, Trademark, hyphen in Intellectual Property - Patent)' {
        $specs = @{}
        foreach ($s in (Get-DemoTableSpecs)) { $specs[$s.Section] = $s }
        $checked = 0
        foreach ($section in 'organizations', 'contacts', 'matters', 'projects') {
            foreach ($row in @($dataset.$section)) {
                foreach ($p in $row.reference.PSObject.Properties) {
                    $table = $specs[$section].Reference[$p.Name].Table
                    $pack = Get-Content (Join-Path $script:PackDir "$table.json") -Raw -Encoding UTF8 | ConvertFrom-Json
                    $names = @($pack.rows | ForEach-Object { $_.($pack.nameColumn) })
                    ($names -ccontains $p.Value) | Should Be $true
                    $checked++
                }
            }
        }
        $checked | Should BeGreaterThan 40
        foreach ($n in @($dataset.matters | ForEach-Object { $_.reference.matterType }) + @($dataset.projects | ForEach-Object { $_.reference.projectType })) { $n.Contains([string][char]0x2013) | Should Be $false }
        (@($dataset.projects | ForEach-Object { $_.reference.projectType }) -contains 'Intellectual Property - Patent') | Should Be $true
    }
    It 'uses an event type code that exists in the event-type seed for every event' {
        $seed = Get-Content $script:EventTypeSeed -Raw -Encoding UTF8 | ConvertFrom-Json
        $codes = @($seed.rows | ForEach-Object { $_.sprk_eventtypecode })
        foreach ($e in @($dataset.events)) { ($codes -ccontains $e.reference.eventTypeCode) | Should Be $true }
    }
    It 'no longer carries a copy of the taxonomy or event types in reference-names.json (nothing to drift)' {
        $ref = Get-Content $script:RefFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $ref.PSObject.Properties.Name -contains 'taxonomy' | Should Be $false
        $ref.PSObject.Properties.Name -contains 'eventTypes' | Should Be $false
    }
    It 'reads a taxonomy rename from the pack: the seed reference follows the pack file, not a copy' {
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ("demo-seed-" + [guid]::NewGuid())
        try {
            $dest = Join-Path $tmp 'seed-data' 'reference'
            New-Item -ItemType Directory -Path $dest -Force | Out-Null
            Copy-Item (Join-Path $script:RepoRoot 'scripts' 'seed-data' 'reference' 'packs') $dest -Recurse
            Copy-Item $script:EventTypeSeed $dest
            $mt = Join-Path $dest 'packs' 'legal-standard' 'sprk_mattertype_ref.json'
            (Get-Content $mt -Raw -Encoding UTF8).Replace('"Patent"', '"Patent Law"') | Set-Content $mt -Encoding UTF8
            $ref = Get-DemoSeedReference -ScriptsRoot $tmp
            (@($ref.taxonomy.sprk_mattertype_ref) -ccontains 'Patent Law') | Should Be $true
            $errors = @(Test-DemoDataset -Dataset $dataset -Reference $ref)
            ($errors -join "`n") | Should Match "matterType 'Patent' is not a sprk_mattertype_ref name"
        }
        finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'fails (does not return an empty list) when a seed file is missing' {
        { Get-DemoSeedReference -ScriptsRoot (Join-Path ([IO.Path]::GetTempPath()) 'no-such-scripts-root') } | Should Throw 'Reference source not found'
    }
    It 'numbers matters and projects so they cannot collide with the v1 dataset (MTR-2026-001..005, PRJ-2026-001..003)' {
        foreach ($m in @($dataset.matters)) {
            $m.fields.sprk_matternumber | Should Match '^MTR-DEMO-\d{3}$'
            $m.fields.sprk_matternumber | Should Not Match '^MTR-2026-00[1-5]$'
        }
        foreach ($p in @($dataset.projects)) {
            $p.fields.sprk_projectnumber | Should Match '^PRJ-DEMO-\d{3}$'
            $p.fields.sprk_projectnumber | Should Not Match '^PRJ-2026-00[1-3]$'
        }
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
        $p['sprk_regardingrecordnumber'] | Should Be 'MTR-DEMO-001'
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
        $rows = @([pscustomobject]@{ Id = 'AAAAAAAA-0000-4000-8000-000000000001'; Name = 'MTR-DEMO-001' })
        $plan = Select-DemoDeletableRows -Rows $rows -UntaggedReferences @{}
        @($plan.Deletable).Count | Should Be 1
        @($plan.Kept).Count | Should Be 0
    }
    It 'keeps a tagged row an untagged row points at, and says why (case-insensitive ids)' {
        $rows = @(
            [pscustomobject]@{ Id = 'AAAAAAAA-0000-4000-8000-000000000001'; Name = 'MTR-DEMO-001' },
            [pscustomobject]@{ Id = 'AAAAAAAA-0000-4000-8000-000000000002'; Name = 'MTR-DEMO-002' })
        $refs = @{ 'aaaaaaaa-0000-4000-8000-000000000001' = @('sprk_event.sprk_regardingmatter', 'sprk_event.sprk_regardingmatter', 'sprk_todo.sprk_regardingmatter') }
        $plan = Select-DemoDeletableRows -Rows $rows -UntaggedReferences $refs
        @($plan.Deletable).Count | Should Be 1
        $plan.Deletable[0].Name | Should Be 'MTR-DEMO-002'
        @($plan.Kept).Count | Should Be 1
        $plan.Kept[0].Name | Should Be 'MTR-DEMO-001'
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
        (@($r.Reasons) -join ' ') | Should Match 'has no row'
    }
    It '-AllowNonDemo accepts a non-demo row and a missing row, with a warning each' {
        $r = Test-DemoTargetAllowed -RegistryRow (Row 0) -AllowNonDemo
        $r.Allowed | Should Be $true
        (@($r.Warnings) -join ' ') | Should Match '-AllowNonDemo'
        $r2 = Test-DemoTargetAllowed -RegistryRow $null -AllowNonDemo
        $r2.Allowed | Should Be $true
        @($r2.Warnings).Count | Should Be 1
        # The warning says exactly what was accepted: the registry was READ and had no row.
        $r2.Warnings[0] | Should Match 'registry was read successfully but has no row'
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
    It 'supports -WhatIf (SupportsShouldProcess) and every Invoke-DvWrite sits behind ShouldProcess (create) or the reset section''s -Approve callback (delete)' {
        $text | Should Match 'SupportsShouldProcess'
        $writes = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-DvWrite' }, $true)
        @($writes).Count | Should Be 2
        foreach ($w in $writes) {
            $gated = $false
            for ($p = $w.Parent; $p; $p = $p.Parent) {
                if ($p -is [System.Management.Automation.Language.IfStatementAst] -and ($p.Clauses | Where-Object { $_.Item1.Extent.Text -match 'ShouldProcess\(' })) { $gated = $true }
                # The delete runs inside the -Delete callback; Invoke-DemoResetSection calls it only after -Approve (tested behaviourally below).
                if ($p -is [System.Management.Automation.Language.AssignmentStatementAst] -and $p.Left.Extent.Text -eq '$deleter') { $gated = $true }
            }
            $gated | Should Be $true
        }
        $text | Should Match '\$confirmer = \{ param\(\$Target, \$Action\) \$PSCmdlet\.ShouldProcess\(\$Target, \$Action\) \}'
        $text | Should Match '-Approve \$confirmer'
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
    It 'never offers -Force, and finds the rows it deletes only through Get-DemoTaggedRows (behaviour: see the reset tests)' {
        $text | Should Not Match '\[switch\]\$Force'
        $text | Should Not Match 'Get-DemoUntaggedFilter'
        $text | Should Not Match 'Get-UntaggedReferences'
        $text | Should Match 'Invoke-DemoResetSection'
        $text | Should Match 'Get-DemoTaggedRows'
    }
    It 'wires the registry guard through Resolve-DemoTarget with no try/catch that turns a failed read into "no row"' {
        $text | Should Match 'Resolve-DemoTarget'
        $text | Should Not Match 'Registry lookup failed'
        $text | Should Not Match 'Get-RegistryRowByUrl'
        $call = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Resolve-DemoTarget' }, $true) | Select-Object -First 1
        $call | Should Not BeNullOrEmpty
        for ($p = $call.Parent; $p; $p = $p.Parent) { ($p -is [System.Management.Automation.Language.TryStatementAst]) | Should Be $false }
    }
    It 'reads choice labels through Get-DemoOptionMap (1033), never UserLocalizedLabel' {
        $text | Should Match 'Get-DemoOptionMap'
        $text | Should Not Match 'UserLocalizedLabel'
    }
    It 'no longer touches AI seed data, SPE, chart definitions or AI Search (product configuration, not demo data)' {
        $text | Should Not Match 'Deploy-All-AI-SeedData'
        $text | Should Not Match 'Sync-RecordsToIndex'
        $text | Should Not Match 'spaarke-search-prod'
        $text | Should Not Match 'sprk_chartdefinitions'
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# A fake Dataverse for the reset and guard tests. It answers the Web API paths the library builds (metadata, relationships,
# filtered row reads) from a hashtable "world", records every path it was asked, and can be told to fail a path. Each test
# that proves a protection also has the opposite case (a must-fire and a must-not-fire), so the protection cannot pass by
# refusing everything.
# ---------------------------------------------------------------------------------------------------------------------

function New-FakeDataverse {
    param([Parameter(Mandatory)][hashtable]$World)
    $state = @{ World = $World; Paths = (New-Object System.Collections.Generic.List[string]) }
    $get = {
        param($Path)
        $p = [uri]::UnescapeDataString($Path)
        $state.Paths.Add($p)
        foreach ($f in @($state.World.Fail)) { if ($f -and $p -match $f) { throw "HTTP 403 (fake) $p" } }
        foreach ($f in @($state.World.NotSupported)) { if ($f -and $p -match $f) { throw "HTTP 500 The 'RetrieveMultiple' method does not support entities of type 'x'. (fake)" } }
        if ($p -match "^EntityDefinitions\(LogicalName='([^']+)'\)/OneToManyRelationships") {
            return @(@($state.World.OneToMany[$Matches[1]]) | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ SchemaName = 'rel'; ReferencingEntity = $_.Entity; ReferencingAttribute = $_.Attr } })
        }
        if ($p -match "^EntityDefinitions\(LogicalName='([^']+)'\)/ManyToManyRelationships") {
            return @(@($state.World.ManyToMany[$Matches[1]]) | Where-Object { $_ } | ForEach-Object {
                    [pscustomobject]@{ SchemaName = $_.Schema; IntersectEntityName = $_.Intersect; Entity1LogicalName = $_.E1; Entity1IntersectAttribute = $_.A1; Entity2LogicalName = $_.E2; Entity2IntersectAttribute = $_.A2 } })
        }
        if ($p -match "^EntityDefinitions\?.*LogicalName eq '([^']+)'") {
            $e = $state.World.Entities[$Matches[1]]
            if (-not $e) { return @() }
            return @([pscustomobject]@{ LogicalName = $Matches[1]; EntitySetName = $e.Set; PrimaryIdAttribute = $e.Pk })
        }
        if ($p -match '^([a-z0-9_]+)\?\$select=([^&]*)(?:&\$filter=(.*))?$') {
            $set = $Matches[1]; $filter = $Matches[3]
            $rows = @($state.World.Rows[$set])
            $out = New-Object System.Collections.Generic.List[object]
            foreach ($row in $rows) {
                if (-not $row) { continue }
                $hit = $true
                if ($filter) {
                    $hit = $false
                    foreach ($term in ($filter -split ' or ')) {
                        if ($term.Trim().Trim('(', ')') -match '^(\w+) eq (\S+)$') {
                            if ("$($row[$Matches[1]])".ToLowerInvariant() -eq $Matches[2].ToLowerInvariant()) { $hit = $true }
                        }
                    }
                }
                if ($hit) { $out.Add([pscustomobject]$row) }
            }
            return $out.ToArray()
        }
        throw "fake Dataverse: unexpected path $p"
    }.GetNewClosure()
    return @{ Get = $get; Paths = $state.Paths }
}

function Invoke-FakeReset {
    <# Runs the reset the way the loader does (reverse create order, shared Gone set). Returns what was deleted, kept and counted. #>
    param([Parameter(Mandatory)][hashtable]$Fake, [bool]$Approve = $true)
    $deleted = New-Object System.Collections.Generic.List[string]
    $kept = New-Object System.Collections.Generic.List[object]
    $counts = @{}
    $gone = New-Object 'System.Collections.Generic.HashSet[string]'
    $delete = { param($Spec, $Row) $deleted.Add("$($Spec.Section):$($Row.Name)") }.GetNewClosure()
    $approveSb = { param($Target, $Action) $Approve }.GetNewClosure()
    foreach ($section in (Get-DemoResetOrder)) {
        $spec = Get-DemoTableSpecs | Where-Object { $_.Section -eq $section }
        $r = Invoke-DemoResetSection -Spec $spec -Get $Fake.Get -Delete $delete -Approve $approveSb -Gone $gone
        $counts[$section] = $r.Counts
        foreach ($k in $r.Kept) { $kept.Add($k) }
    }
    return @{ Deleted = $deleted.ToArray(); Kept = $kept.ToArray(); Counts = $counts }
}

function New-ResetWorld {
    <#
      Matters M1..M6 (all tagged): M1 is kept through a TAGGED event E1 that is itself kept (an untagged email points at E1);
      M2 by an untagged EMAIL (a table outside sprk_/contact/account); M3 by an untagged sprk_event; M4 by an association to an
      untagged contact; M5 and M6 are clean (M5 is associated with tagged contact C3, M6 owns the tagged event E6).
      Contacts C1..C3 tagged: C1 has an untagged NOTE (cascade-delete child, a table with no tag column); C2, C3 clean.
    #>
    $tag = Get-DemoImportSequenceNumber
    $id = { param($n) '00000000-0000-4000-8000-{0:D12}' -f $n }
    $m = 1..6 | ForEach-Object { & $id $_ }
    $c = 11..13 | ForEach-Object { & $id $_ }
    return @{
        Fail     = @()
        Entities = @{
            sprk_matter         = @{ Set = 'sprk_matters'; Pk = 'sprk_matterid' }
            sprk_event          = @{ Set = 'sprk_events'; Pk = 'sprk_eventid' }
            contact             = @{ Set = 'contacts'; Pk = 'contactid' }
            email               = @{ Set = 'emails'; Pk = 'activityid' }
            annotation          = @{ Set = 'annotations'; Pk = 'annotationid' }
            sprk_matter_contact = @{ Set = 'sprk_matter_contactset'; Pk = $null }
        }
        OneToMany = @{
            sprk_matter = @(
                @{ Entity = 'sprk_event'; Attr = 'sprk_regardingmatter' },
                @{ Entity = 'email'; Attr = 'regardingobjectid' },
                @{ Entity = 'systemuser'; Attr = 'createdby' }   # plumbing: never queried (systemuser is not even readable here)
            )
            sprk_event  = @( @{ Entity = 'email'; Attr = 'regardingobjectid' } )
            contact     = @( @{ Entity = 'annotation'; Attr = 'objectid' } )
        }
        ManyToMany = @{
            sprk_matter = @( @{ Schema = 'sprk_matter_contact'; Intersect = 'sprk_matter_contact'; E1 = 'sprk_matter'; A1 = 'sprk_matterid'; E2 = 'contact'; A2 = 'contactid' } )
            contact     = @( @{ Schema = 'sprk_matter_contact'; Intersect = 'sprk_matter_contact'; E1 = 'sprk_matter'; A1 = 'sprk_matterid'; E2 = 'contact'; A2 = 'contactid' } )
        }
        Rows     = @{
            sprk_matters           = @(1..6 | ForEach-Object { @{ sprk_matterid = $m[$_ - 1]; sprk_matternumber = "M$_"; importsequencenumber = $tag } })
            sprk_events            = @(
                @{ sprk_eventid = (& $id 101); sprk_eventname = 'E1'; importsequencenumber = $tag; _sprk_regardingmatter_value = $m[0] },
                @{ sprk_eventid = (& $id 106); sprk_eventname = 'E6'; importsequencenumber = $tag; _sprk_regardingmatter_value = $m[5] },
                @{ sprk_eventid = (& $id 103); sprk_eventname = 'E3 (user)'; importsequencenumber = $null; _sprk_regardingmatter_value = $m[2] })
            emails                 = @(
                @{ activityid = (& $id 201); importsequencenumber = $null; _regardingobjectid_value = $m[1] },
                @{ activityid = (& $id 202); importsequencenumber = $null; _regardingobjectid_value = (& $id 101) })
            contacts               = @(
                @{ contactid = $c[0]; emailaddress1 = 'c1@example.com'; importsequencenumber = $tag },
                @{ contactid = $c[1]; emailaddress1 = 'c2@example.com'; importsequencenumber = $tag },
                @{ contactid = $c[2]; emailaddress1 = 'c3@example.com'; importsequencenumber = $tag },
                @{ contactid = (& $id 14); emailaddress1 = 'user@example.com'; importsequencenumber = $null })
            annotations            = @( @{ annotationid = (& $id 301); _objectid_value = $c[0] } )
            sprk_matter_contactset = @(
                @{ sprk_matterid = $m[3]; contactid = (& $id 14) },
                @{ sprk_matterid = $m[4]; contactid = $c[2] })
        }
    }
}

Describe 'Reset keeps what something else still points at (finding: every relationship, M:N, cascade children, fail closed)' {
    It 'deletes exactly the clean tagged rows and keeps every referenced one, in dependency order' {
        $fake = New-FakeDataverse -World (New-ResetWorld)
        $r = Invoke-FakeReset -Fake $fake
        ((@($r.Deleted) | Sort-Object) -join ',') | Should Be 'contacts:c2@example.com,contacts:c3@example.com,events:E6,matters:M5,matters:M6'
        ((@($r.Kept | ForEach-Object { $_.Name }) | Sort-Object) -join ',') | Should Be 'c1@example.com,E1,M1,M2,M3,M4'
        $r.Counts['matters'].Deleted | Should Be 2
        $r.Counts['matters'].Kept | Should Be 4
        $r.Counts['contacts'].Deleted | Should Be 2
    }
    It 'MUST FIRE: an untagged EMAIL regarding a matter keeps the matter (a table the old sprk_/contact/account filter never looked at)' {
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        $m2 = @($r.Kept | Where-Object { $_.Name -eq 'M2' })
        $m2.Count | Should Be 1
        (@($m2[0].Reasons) -join ',') | Should Be 'email.regardingobjectid'
        ($r.Deleted -contains 'matters:M2') | Should Be $false
    }
    It 'MUST FIRE: an untagged NOTE on a contact (cascade-delete child, a table with no tag column) keeps the contact' {
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        ($r.Deleted -contains 'contacts:c1@example.com') | Should Be $false
        (@($r.Kept | Where-Object { $_.Name -eq 'c1@example.com' })[0].Reasons -join ',') | Should Be 'annotation.objectid'
    }
    It 'MUST FIRE: a many-to-many association to an untagged contact keeps the matter' {
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        (@($r.Kept | Where-Object { $_.Name -eq 'M4' })[0].Reasons -join ',') | Should Be 'm:n sprk_matter_contact'
        ($r.Deleted -contains 'matters:M4') | Should Be $false
    }
    It 'MUST NOT FIRE: an association between two tagged demo rows does not keep either (M5 and tagged contact C3 are both deleted)' {
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        ($r.Deleted -contains 'matters:M5') | Should Be $true
        ($r.Deleted -contains 'contacts:c3@example.com') | Should Be $true
    }
    It 'MUST FIRE: a tagged child that is itself kept keeps its parent (E1 stays because of an untagged email, so M1 stays)' {
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        (@($r.Kept | Where-Object { $_.Name -eq 'M1' })[0].Reasons -join ',') | Should Be 'sprk_event.sprk_regardingmatter'
    }
    It 'MUST NOT FIRE: a tagged child that this run deletes does not keep its parent (E6 goes first, then M6)' {
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        ($r.Deleted -contains 'events:E6') | Should Be $true
        ($r.Deleted -contains 'matters:M6') | Should Be $true
    }
    It 'MUST NOT FIRE: a plumbing relationship (createdby) is not queried and does not make the table unchecked' {
        $fake = New-FakeDataverse -World (New-ResetWorld)
        $null = Invoke-FakeReset -Fake $fake
        (@($fake.Paths | Where-Object { $_ -match 'systemuser' }).Count) | Should Be 0
    }
    It 'FAILS CLOSED: a relationship that cannot be read keeps every row of that table and deletes none of it' {
        $world = New-ResetWorld
        $world.Fail = @('^annotations\?')
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World $world)
        $r.Counts['contacts'].Deleted | Should Be 0
        $r.Counts['contacts'].Kept | Should Be 3
        ((@($r.Kept | Where-Object { $_.Name -like 'c*@example.com' } | ForEach-Object { @($_.Reasons) }) -join ' ')) | Should Match 'could not be read'
        # ...and the other tables are unaffected.
        ($r.Deleted -contains 'matters:M5') | Should Be $true
    }
    It 'MUST NOT FIRE: a system table the platform refuses to retrieve ("RetrieveMultiple does not support entities of type") is noted, not treated as unreadable' {
        $world = New-ResetWorld
        $world.NotSupported = @('^annotations\?')
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World $world)
        $r.Counts['contacts'].Deleted | Should Be 3
        $check = Get-DemoBlockingReferences -Get (New-FakeDataverse -World $world).Get -Logical 'contact' -Ids @('00000000-0000-4000-8000-000000000011')
        @($check.Unchecked).Count | Should Be 0
        (@($check.NotRetrievable) -join ',') | Should Be 'annotation.objectid'
    }
    It 'FAILS CLOSED: unreadable relationship metadata keeps every row' {
        $world = New-ResetWorld
        $world.Fail = @('OneToManyRelationships')
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World $world)
        @($r.Deleted).Count | Should Be 0
        $r.Counts['matters'].Errors | Should Be 0
    }
    It 'FAILS CLOSED: an unreadable many-to-many association keeps the table' {
        $world = New-ResetWorld
        $world.Fail = @('^sprk_matter_contactset\?')
        $r = Invoke-FakeReset -Fake (New-FakeDataverse -World $world)
        ($r.Deleted -contains 'matters:M5') | Should Be $false
    }
    It 'a failed READ of the tagged rows is fatal (throws), not "nothing to delete"' {
        $world = New-ResetWorld
        $world.Fail = @('^sprk_matters\?')
        { Invoke-FakeReset -Fake (New-FakeDataverse -World $world) } | Should Throw 'HTTP 403'
    }
    It 'under -WhatIf (Approve says no) nothing is deleted, and the plan matches the real run (children planned first)' {
        $real = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld))
        $dry = Invoke-FakeReset -Fake (New-FakeDataverse -World (New-ResetWorld)) -Approve $false
        @($dry.Deleted).Count | Should Be 0
        (($dry.Counts.Values | ForEach-Object { $_.WouldDelete } | Measure-Object -Sum).Sum) | Should Be @($real.Deleted).Count
        ((@($dry.Kept | ForEach-Object { $_.Name }) | Sort-Object) -join ',') | Should Be ((@($real.Kept | ForEach-Object { $_.Name }) | Sort-Object) -join ',')
    }
    It 'only ever reads rows by the tag filter, and never deletes an untagged row' {
        $fake = New-FakeDataverse -World (New-ResetWorld)
        $r = Invoke-FakeReset -Fake $fake
        foreach ($d in $r.Deleted) { $d | Should Not Match '(user)|user@example.com' }
        $tagged = @($fake.Paths | Where-Object { $_ -match 'importsequencenumber eq 2026112501' })
        ($tagged.Count -ge 3) | Should Be $true
    }
    It 'counts a failed delete as an error and keeps the row''s parents (the failed row is not "gone")' {
        $world = New-ResetWorld
        $fake = New-FakeDataverse -World $world
        $gone = New-Object 'System.Collections.Generic.HashSet[string]'
        $events = Get-DemoTableSpecs | Where-Object { $_.Section -eq 'events' }
        $res = Invoke-DemoResetSection -Spec $events -Get $fake.Get -Delete { throw 'HTTP 500 (fake)' } -Approve { $true } -Gone $gone
        $res.Counts.Errors | Should Be 1
        ($gone.Contains('00000000-0000-4000-8000-000000000106')) | Should Be $false
    }
}

Describe 'Get-DemoBlockingReferences (the check itself)' {
    It 'returns nothing and reads nothing when there are no ids' {
        $fake = New-FakeDataverse -World (New-ResetWorld)
        $r = Get-DemoBlockingReferences -Get $fake.Get -Logical 'sprk_matter' -Ids @()
        $r.Refs.Count | Should Be 0
        @($r.Unchecked).Count | Should Be 0
        $fake.Paths.Count | Should Be 0
    }
    It 'lists every relationship it could not read, by name' {
        $world = New-ResetWorld
        $world.Fail = @('^emails\?')
        $fake = New-FakeDataverse -World $world
        $r = Get-DemoBlockingReferences -Get $fake.Get -Logical 'sprk_matter' -Ids @('00000000-0000-4000-8000-000000000001')
        (@($r.Unchecked) -join ' ') | Should Match 'email.regardingobjectid'
    }
    It 'splits a long id list into several reads (no oversized filter)' {
        $world = New-ResetWorld
        $fake = New-FakeDataverse -World $world
        $ids = @(1..45 | ForEach-Object { '00000000-0000-4000-8000-{0:D12}' -f (900 + $_) })
        $null = Get-DemoBlockingReferences -Get $fake.Get -Logical 'contact' -Ids $ids
        (@($fake.Paths | Where-Object { $_ -match '^annotations\?' }).Count) | Should Be 3
    }
}

Describe 'The demo guard is fed by the registry read and fails closed (finding: a failed read became "no row")' {
    function Reg($url, $type, $active = $true, $name = 'Row') { [pscustomobject]@{ sprk_name = $name; sprk_dataverseurl = $url; sprk_environmenttype = $type; sprk_isactive = $active } }
    $target = 'https://spaarke-acme.crm.dynamics.com'

    It 'MUST FIRE: a registry read that throws is fatal, with or without -AllowNonDemo (it is never "no row")' {
        { Resolve-DemoTarget -ReadRows { throw 'token failure' } -Target $target } | Should Throw 'could not be read'
        { Resolve-DemoTarget -ReadRows { throw 'token failure' } -Target $target -AllowNonDemo } | Should Throw 'never treated as'
    }
    It 'MUST FIRE: a Production row is refused even with -AllowNonDemo, whatever shape its URL has' {
        foreach ($u in 'https://spaarke-acme.crm.dynamics.com', 'https://SPAARKE-ACME.crm.dynamics.com/', 'https://spaarke-acme.crm.dynamics.com/main.aspx?appid=1', 'http://spaarke-acme.crm.dynamics.com', 'spaarke-acme.crm.dynamics.com', ' https://spaarke-acme.crm.dynamics.com:443/ ', 'see https://spaarke-acme.crm.dynamics.com (prod)') {
            $res = Resolve-DemoTarget -ReadRows { @((Reg $u 6 $true 'Prod Customer')) } -Target $target -AllowNonDemo
            $res.Guard.Allowed | Should Be $false
            (@($res.Guard.Reasons) -join ' ') | Should Match 'Production'
        }
    }
    It 'MUST FIRE: an INACTIVE Production row still refuses, even when an active Demo row has the same URL' {
        $rows = @((Reg $target 1 $true 'Demo'), (Reg $target 6 $false 'Prod'))
        $res = Resolve-DemoTarget -ReadRows { $rows } -Target $target -AllowNonDemo
        $res.Guard.Allowed | Should Be $false
    }
    It 'MUST FIRE: -CustomerId resolved a Demo row but another row names the same URL as Production: refused' {
        $rows = @((Reg $target 1 $true 'Demo'), (Reg $target 6 $true 'Prod'))
        $res = Resolve-DemoTarget -ReadRows { $rows } -Target $target -CustomerRow $rows[0] -AllowNonDemo
        $res.Guard.Allowed | Should Be $false
    }
    It 'MUST FIRE: two active rows with the URL is an error, not a guess' {
        $rows = @((Reg $target 1 $true 'A'), (Reg $target 1 $true 'B'))
        { Resolve-DemoTarget -ReadRows { $rows } -Target $target } | Should Throw 'active registry rows'
    }
    It 'MUST NOT FIRE: an active Demo row is allowed with no warning (also by customer row, URL in another shape)' {
        $res = Resolve-DemoTarget -ReadRows { @((Reg 'https://Spaarke-Acme.crm.dynamics.com/' 1)) } -Target $target
        $res.Guard.Allowed | Should Be $true
        @($res.Guard.Warnings).Count | Should Be 0
        $cust = Reg $target 1 $true 'ACME'
        $res2 = Resolve-DemoTarget -ReadRows { @((Reg 'https://spaarke-acme.crm.dynamics.com' 1 $true 'ACME')) } -Target $target -CustomerRow $cust
        $res2.Guard.Allowed | Should Be $true
    }
    It 'MUST NOT FIRE: other customers'' rows, even with an unreadable URL, do not block or match' {
        $rows = @((Reg 'https://someone-else.crm.dynamics.com' 6), (Reg 'not a url at all' 6), (Reg $null 6), (Reg $target 1))
        $res = Resolve-DemoTarget -ReadRows { $rows } -Target $target
        $res.Guard.Allowed | Should Be $true
        $res.Row.sprk_environmenttype | Should Be 1
    }
    It 'a successful read with no row for the URL: refused, or allowed with -AllowNonDemo and a warning that says so' {
        $none = @((Reg 'https://someone-else.crm.dynamics.com' 1))
        $refused = Resolve-DemoTarget -ReadRows { $none } -Target $target
        $refused.Guard.Allowed | Should Be $false
        $res = Resolve-DemoTarget -ReadRows { $none } -Target $target -AllowNonDemo
        $res.Guard.Allowed | Should Be $true
        (@($res.Guard.Warnings) -join ' ') | Should Match 'read successfully but has no row'
        $res.Row | Should BeNullOrEmpty
    }
    It 'compares URLs by host on both sides' {
        ConvertTo-DemoComparableHost 'https://X.crm.dynamics.com/main.aspx?a=b' | Should Be 'x.crm.dynamics.com'
        ConvertTo-DemoComparableHost 'x.crm.dynamics.com' | Should Be 'x.crm.dynamics.com'
        ConvertTo-DemoComparableHost 'https://x.crm.dynamics.com:443/' | Should Be 'x.crm.dynamics.com'
        ConvertTo-DemoComparableHost '' | Should BeNullOrEmpty
    }
}

Describe 'Owner plan: -OwnerTeamName alone overrides the registry team' {
    $row = [pscustomobject]@{ sprk_name = 'Dev'; sprk_businessunitname = 'Spaarke Demo'; sprk_teamname = 'Registry Team' }
    It 'uses the registry unit with the team the operator named' {
        $plan = Get-DemoOwnerPlan -RegistryRow $row -OwnerTeamName 'Operator Team'
        $plan.BusinessUnitName | Should Be 'Spaarke Demo'
        $plan.TeamName | Should Be 'Operator Team'
    }
    It 'still uses the registry team when no team is named' {
        (Get-DemoOwnerPlan -RegistryRow $row).TeamName | Should Be 'Registry Team'
    }
    It 'a blank registry team and no parameter leaves the default team (null)' {
        $r = [pscustomobject]@{ sprk_name = 'Dev'; sprk_businessunitname = 'Unit'; sprk_teamname = $null }
        (Get-DemoOwnerPlan -RegistryRow $r).TeamName | Should BeNullOrEmpty
    }
}

Describe 'Choice labels are matched on the English (1033) label (Get-DemoOptionMap)' {
    function Opt($value, $labels) {
        [pscustomobject]@{
            Value = $value
            Label = [pscustomobject]@{
                LocalizedLabels     = @($labels.GetEnumerator() | ForEach-Object { [pscustomobject]@{ LanguageCode = $_.Key; Label = $_.Value } })
                UserLocalizedLabel  = [pscustomobject]@{ LanguageCode = 1036; Label = 'FR-USER-LABEL' }
            }
        }
    }
    It 'reads the 1033 label even when the operator''s own language (UserLocalizedLabel) is French' {
        $map = Get-DemoOptionMap -Options @((Opt 1 @{ 1033 = 'Open'; 1036 = 'Ouvert' }), (Opt 2 @{ 1033 = 'Closed'; 1036 = 'Fermé' }))
        $map['Open'] | Should Be 1
        $map['Closed'] | Should Be 2
        $map.ContainsKey('Ouvert') | Should Be $false
        $map.ContainsKey('FR-USER-LABEL') | Should Be $false
    }
    It 'leaves out an option with no English label, and marks a label two options share as ambiguous (-1)' {
        $map = Get-DemoOptionMap -Options @((Opt 1 @{ 1036 = 'Seulement' }), (Opt 2 @{ 1033 = 'Dup' }), (Opt 3 @{ 1033 = 'Dup' }))
        $map.Count | Should Be 1
        $map['Dup'] | Should Be (-1)
    }
    It 'handles an empty option list' {
        (Get-DemoOptionMap -Options @()).Count | Should Be 0
    }
}
