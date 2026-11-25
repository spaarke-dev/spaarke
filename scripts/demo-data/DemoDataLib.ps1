# DemoDataLib.ps1 - the pure (no network, no az) functions behind scripts/Load-DemoSampleData.ps1.
#
# Dot-sourced by the loader and by tests/scripts/Load-DemoSampleData.Tests.ps1. Everything here takes its inputs as
# parameters and returns data, so the rules that protect a stamp (the tag, the demo guard, the reset order, what a reset
# may delete) are unit-tested without a Dataverse. The loader owns every HTTP call.
#
# THE TAG. Every row the loader creates carries the standard column importsequencenumber = Get-DemoImportSequenceNumber.
# Why this column: it exists on every user-owned table (standard and custom), takes a value on create, is filterable,
# is not on any form or view (a user cannot clear or rename it by accident, unlike a name prefix), and no Spaarke code
# reads or writes it. A real data import numbers its batches 1, 2, 3 ..., so the large constant below cannot collide.
# Reset deletes only rows that carry it. A row without it is never deleted, never updated.

function Get-DemoImportSequenceNumber {
    # yyyyMMdd + 01: the day the tag was fixed (2026-11-25). Never change it: rows already loaded carry it.
    return [int]2026112501
}

function Get-DemoTaxonomyTables {
    <# Reference tables the dataset names by NAME (or code), with how to find a row. #>
    return [ordered]@{
        sprk_mattertype_ref       = @{ EntitySet = 'sprk_mattertype_refs'; IdColumn = 'sprk_mattertype_refid'; NameColumn = 'sprk_mattertypename'; Label = 'matter type' }
        sprk_practicearea_ref     = @{ EntitySet = 'sprk_practicearea_refs'; IdColumn = 'sprk_practicearea_refid'; NameColumn = 'sprk_practiceareaname'; Label = 'practice area' }
        sprk_projecttype_ref      = @{ EntitySet = 'sprk_projecttype_refs'; IdColumn = 'sprk_projecttype_refid'; NameColumn = 'sprk_name'; Label = 'project type' }
        sprk_contacttype_ref      = @{ EntitySet = 'sprk_contacttype_refs'; IdColumn = 'sprk_contacttype_refid'; NameColumn = 'sprk_contacttypename'; Label = 'contact type' }
        sprk_organizationtype_ref = @{ EntitySet = 'sprk_organizationtype_refs'; IdColumn = 'sprk_organizationtype_refid'; NameColumn = 'sprk_name'; Label = 'organization type' }
        sprk_eventtype_ref        = @{ EntitySet = 'sprk_eventtype_refs'; IdColumn = 'sprk_eventtype_refid'; NameColumn = 'sprk_eventtypecode'; Label = 'event type (by code)' }
        sprk_recordtype_ref       = @{ EntitySet = 'sprk_recordtype_refs'; IdColumn = 'sprk_recordtype_refid'; NameColumn = 'sprk_recordlogicalname'; Label = 'record type (by table)' }
    }
}

function Get-DemoTableSpecs {
    <#
      The tables the dataset writes, IN CREATE ORDER (parents first). Reset walks it backwards.
      Fields     - literal columns a dataset row may set under "fields" (anything else is a validation error).
      Dates      - columns a row may set under "dates" as a day offset ("-3d", "+14d", "-3d 14:30").
      Reference  - friendly name -> lookup column + the reference table that names the target (see Get-DemoTaxonomyTables).
      Options    - friendly name -> picklist column (the dataset gives the LABEL; the loader reads the value from metadata).
      Links      - friendly name -> lookup column + the dataset section the target row lives in.
      Required   - friendly names every row must give (Reference, Options or Links).
      Regarding  - links that file the row under a matter or project; the row then also gets the regarding stamp columns.
    #>
    $regardingStamp = @{ RecordType = 'sprk_regardingrecordtype'; Id = 'sprk_regardingrecordid'; Name = 'sprk_regardingrecordname'; Number = 'sprk_regardingrecordnumber' }
    return @(
        @{
            Section = 'organizations'; Logical = 'sprk_organization'; EntitySet = 'sprk_organizations'; IdColumn = 'sprk_organizationid'
            NaturalKey = 'sprk_organizationname'; DisplayName = 'sprk_organizationname'; DisplayNumber = $null
            Fields = @('sprk_organizationname', 'sprk_domain'); Dates = @()
            Reference = @{ organizationType = @{ Column = 'sprk_organizationtype'; Table = 'sprk_organizationtype_ref' } }
            Options = @{}; Links = @{}; Required = @('organizationType'); Regarding = @()
        }
        @{
            Section = 'contacts'; Logical = 'contact'; EntitySet = 'contacts'; IdColumn = 'contactid'
            NaturalKey = 'emailaddress1'; DisplayName = $null; DisplayNumber = $null
            Fields = @('firstname', 'lastname', 'emailaddress1', 'telephone1', 'mobilephone', 'jobtitle'); Dates = @()
            Reference = @{ contactType = @{ Column = 'sprk_contacttype'; Table = 'sprk_contacttype_ref' } }
            Options = @{}
            Links = @{ organization = @{ Column = 'sprk_organization'; Section = 'organizations' } }
            Required = @('contactType', 'organization'); Regarding = @()
        }
        @{
            Section = 'matters'; Logical = 'sprk_matter'; EntitySet = 'sprk_matters'; IdColumn = 'sprk_matterid'
            NaturalKey = 'sprk_matternumber'; DisplayName = 'sprk_mattername'; DisplayNumber = 'sprk_matternumber'
            Fields = @('sprk_matternumber', 'sprk_mattername', 'sprk_matterdescription', 'sprk_highpriority'); Dates = @('sprk_openeddate', 'sprk_nextreviewdate')
            Reference = @{
                matterType   = @{ Column = 'sprk_mattertype'; Table = 'sprk_mattertype_ref' }
                practiceArea = @{ Column = 'sprk_practicearea'; Table = 'sprk_practicearea_ref' }
            }
            Options = @{}
            Links = @{
                assignedLawFirm1   = @{ Column = 'sprk_assignedlawfirm1'; Section = 'organizations' }
                assignedAttorney1  = @{ Column = 'sprk_assignedattorney1'; Section = 'contacts' }
                assignedAttorney2  = @{ Column = 'sprk_assignedattorney2'; Section = 'contacts' }
                assignedParalegal1 = @{ Column = 'sprk_assignedparalegal1'; Section = 'contacts' }
                assignedToInternal = @{ Column = 'sprk_assignedtointernal'; Section = 'contacts' }
            }
            Required = @('matterType', 'practiceArea'); Regarding = @()
        }
        @{
            Section = 'projects'; Logical = 'sprk_project'; EntitySet = 'sprk_projects'; IdColumn = 'sprk_projectid'
            NaturalKey = 'sprk_projectnumber'; DisplayName = 'sprk_projectname'; DisplayNumber = 'sprk_projectnumber'
            Fields = @('sprk_projectnumber', 'sprk_projectname', 'sprk_projectdescription', 'sprk_highpriority'); Dates = @('sprk_openeddate', 'sprk_nextreviewdate')
            Reference = @{
                projectType  = @{ Column = 'sprk_projecttype_ref'; Table = 'sprk_projecttype_ref' }
                practiceArea = @{ Column = 'sprk_practicearea'; Table = 'sprk_practicearea_ref' }
            }
            Options = @{}
            Links = @{
                assignedLawFirm1   = @{ Column = 'sprk_assignedlawfirm1'; Section = 'organizations' }
                assignedAttorney1  = @{ Column = 'sprk_assignedattorney1'; Section = 'contacts' }
                assignedToInternal = @{ Column = 'sprk_assignedtointernal'; Section = 'contacts' }
            }
            Required = @('projectType'); Regarding = @()
        }
        @{
            Section = 'events'; Logical = 'sprk_event'; EntitySet = 'sprk_events'; IdColumn = 'sprk_eventid'
            NaturalKey = 'sprk_eventname'; DisplayName = 'sprk_eventname'; DisplayNumber = $null
            Fields = @('sprk_eventname', 'sprk_description', 'sprk_highpriority'); Dates = @('sprk_duedate', 'sprk_completeddate', 'sprk_plannedstart', 'sprk_plannedend')
            Reference = @{ eventTypeCode = @{ Column = 'sprk_eventtype_ref'; Table = 'sprk_eventtype_ref'; CodeColumn = 'sprk_eventtypecode' } }
            Options = @{
                status   = @{ Column = 'sprk_eventstatus' }
                priority = @{ Column = 'sprk_priority' }
            }
            Links = @{
                regardingMatter  = @{ Column = 'sprk_regardingmatter'; Section = 'matters'; Regarding = 'sprk_matter' }
                regardingProject = @{ Column = 'sprk_regardingproject'; Section = 'projects'; Regarding = 'sprk_project' }
                assignedTo       = @{ Column = 'sprk_assignedto'; Section = 'contacts' }
            }
            Required = @('eventTypeCode', 'status', 'priority'); Regarding = $regardingStamp
        }
        @{
            Section = 'todos'; Logical = 'sprk_todo'; EntitySet = 'sprk_todos'; IdColumn = 'sprk_todoid'
            NaturalKey = 'sprk_name'; DisplayName = 'sprk_name'; DisplayNumber = $null
            Fields = @('sprk_name', 'sprk_description', 'sprk_highpriority'); Dates = @('sprk_duedate')
            Reference = @{}
            Options = @{ priority = @{ Column = 'sprk_priority' } }
            Links = @{
                regardingMatter  = @{ Column = 'sprk_regardingmatter'; Section = 'matters'; Regarding = 'sprk_matter' }
                regardingProject = @{ Column = 'sprk_regardingproject'; Section = 'projects'; Regarding = 'sprk_project' }
                assignedTo       = @{ Column = 'sprk_assignedto'; Section = 'contacts' }
            }
            Required = @('priority'); Regarding = $regardingStamp
        }
        @{
            Section = 'communications'; Logical = 'sprk_communication'; EntitySet = 'sprk_communications'; IdColumn = 'sprk_communicationid'
            NaturalKey = 'sprk_name'; DisplayName = 'sprk_name'; DisplayNumber = $null
            Fields = @('sprk_name', 'sprk_subject', 'sprk_body', 'sprk_from', 'sprk_to', 'sprk_cc'); Dates = @('sprk_sentat', 'sprk_receiveddate')
            Reference = @{}
            Options = @{
                direction         = @{ Column = 'sprk_direction' }
                communicationType = @{ Column = 'sprk_communicationtype' }
                bodyFormat        = @{ Column = 'sprk_bodyformat' }
            }
            Links = @{
                regardingMatter  = @{ Column = 'sprk_regardingmatter'; Section = 'matters'; Regarding = 'sprk_matter' }
                regardingProject = @{ Column = 'sprk_regardingproject'; Section = 'projects'; Regarding = 'sprk_project' }
            }
            Required = @('direction', 'communicationType', 'bodyFormat'); Regarding = $regardingStamp
        }
        @{
            Section = 'documents'; Logical = 'sprk_document'; EntitySet = 'sprk_documents'; IdColumn = 'sprk_documentid'
            NaturalKey = 'sprk_documentname'; DisplayName = 'sprk_documentname'; DisplayNumber = $null
            Fields = @('sprk_documentname', 'sprk_documentdescription'); Dates = @()
            Reference = @{}
            Options = @{
                documentType   = @{ Column = 'sprk_documenttype' }
                documentStatus = @{ Column = 'sprk_documentstatus' }
            }
            Links = @{
                matter  = @{ Column = 'sprk_matter'; Section = 'matters' }
                project = @{ Column = 'sprk_project'; Section = 'projects' }
            }
            Required = @('documentType', 'documentStatus'); Regarding = @()
            # Metadata only: sprk_hasfile is written false and no pointer column (driveitemid, graphitemid ...) is ever written.
            # See scripts/demo-data/README.md "Documents and files".
        }
    )
}

function Get-DemoSectionOrder {
    return @(Get-DemoTableSpecs | ForEach-Object { $_.Section })
}

function Get-DemoResetOrder {
    <# Reset deletes children before parents: the create order, reversed. #>
    $order = @(Get-DemoSectionOrder)
    [array]::Reverse($order)
    return $order
}

function Get-DemoReservedColumns {
    <# Columns the loader owns: a dataset row may not set them. #>
    return @(
        'importsequencenumber', 'ownerid', 'owningbusinessunit', 'owninguser', 'owningteam', 'createdon', 'modifiedon', 'createdby', 'modifiedby',
        'overriddencreatedon', 'statecode', 'statuscode', 'sprk_hasfile', 'sprk_graphitemid', 'sprk_graphdriveid', 'sprk_driveitemid',
        'sprk_graphitemidbound', 'sprk_containerid', 'sprk_searchindexname', 'sprk_ai_search_index'
    )
}

function Get-DemoSpecColumns {
    <# Every column the loader writes on a table: used to check the target's schema before anything is written. #>
    param([Parameter(Mandatory)][hashtable]$Spec)
    $cols = New-Object System.Collections.Generic.List[string]
    $cols.Add('importsequencenumber'); $cols.Add('ownerid')
    foreach ($c in $Spec.Fields) { $cols.Add($c) }
    foreach ($c in $Spec.Dates) { $cols.Add($c) }
    foreach ($d in $Spec.Reference.Values) { $cols.Add($d.Column); if ($d.Contains('CodeColumn')) { $cols.Add($d.CodeColumn) } }
    foreach ($d in $Spec.Options.Values) { $cols.Add($d.Column) }
    foreach ($d in $Spec.Links.Values) { $cols.Add($d.Column) }
    if ($Spec.Regarding -is [hashtable] -and $Spec.Regarding.Count -gt 0) { foreach ($c in $Spec.Regarding.Values) { $cols.Add($c) } }
    if ($Spec.Section -eq 'documents') { $cols.Add('sprk_hasfile') }
    return @($cols | Select-Object -Unique)
}

function Get-DemoLookupColumns {
    <# The lookup columns the loader binds on a table (their navigation property names are read from the target). #>
    param([Parameter(Mandatory)][hashtable]$Spec)
    $cols = New-Object System.Collections.Generic.List[string]
    foreach ($d in $Spec.Reference.Values) { $cols.Add($d.Column) }
    foreach ($d in $Spec.Links.Values) { $cols.Add($d.Column) }
    if ($Spec.Regarding -is [hashtable] -and $Spec.Regarding.Count -gt 0) { $cols.Add($Spec.Regarding.RecordType) }
    return @($cols | Select-Object -Unique)
}

function ConvertTo-DemoODataString {
    <# Single quotes doubled, the OData string-literal rule. The caller URL-encodes the whole $filter. #>
    param([Parameter(Mandatory)][string]$Value)
    return $Value.Replace("'", "''")
}

function ConvertTo-DemoQuery {
    <# 'set?$select=...&$filter=...' with the filter URL-encoded. The one place a Web API path is assembled. #>
    param([Parameter(Mandatory)][string]$Set, [string]$Select, [string]$Filter)
    $q = $Set
    $sep = '?'
    if ($Select) { $q += $sep + '$select=' + $Select; $sep = '&' }
    if ($Filter) { $q += $sep + '$filter=' + [uri]::EscapeDataString($Filter) }
    return $q
}

function Get-DemoOptionMap {
    <#
      A picklist's options as English label -> value. Labels are matched on the 1033 (English) entry of LocalizedLabels,
      NOT UserLocalizedLabel: that one follows the signed-in operator's language, so a French-speaking operator would
      see no 'Open' and the load would stop. Input is the Options array of OptionSet metadata. An option with no 1033
      label is left out; a label two options share is reported as ambiguous (value -1) rather than guessed.
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Options)
    $map = @{}
    foreach ($opt in $Options) {
        $en = @($opt.Label.LocalizedLabels | Where-Object { $_.LanguageCode -eq 1033 }) | Select-Object -First 1
        if ($null -eq $en -or [string]::IsNullOrEmpty("$($en.Label)")) { continue }
        $label = "$($en.Label)"
        if ($map.ContainsKey($label)) { $map[$label] = -1 } else { $map[$label] = [int]$opt.Value }
    }
    return $map
}

function ConvertTo-DemoDateValue {
    <#
      "-3d" / "+14d" / "14d" -> 'yyyy-MM-dd'; "-3d 14:30" -> 'yyyy-MM-ddTHH:mm:00Z' (UTC). Offsets are from -Today, so a
      dataset loaded in a year still looks current. Throws on any other shape.
    #>
    param([Parameter(Mandatory)][string]$Offset, [Parameter(Mandatory)][datetime]$Today)
    if ($Offset -notmatch '^(?<days>[+-]?\d{1,4})d(?: (?<h>[01]\d|2[0-3]):(?<m>[0-5]\d))?$') {
        throw "Date offset '$Offset' is not '<n>d' or '<n>d HH:mm' (for example '-3d' or '+14d 09:30')."
    }
    $day = $Today.Date.AddDays([int]$Matches['days'])
    if ($Matches['h']) {
        return $day.AddHours([int]$Matches['h']).AddMinutes([int]$Matches['m']).ToString("yyyy-MM-dd'T'HH:mm:00'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
    }
    return $day.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-DemoRowProperty {
    <# A dataset row's optional object property as a hashtable (empty when absent). #>
    param($Row, [Parameter(Mandatory)][string]$Name)
    $result = [ordered]@{}
    if ($null -eq $Row) { return $result }
    $prop = $Row.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return $result }
    foreach ($p in $prop.Value.PSObject.Properties) { $result[$p.Name] = $p.Value }
    return $result
}

function Get-DemoSeedReference {
    <#
      The reference values the dataset may name, READ FROM THE SEED FILES THEMSELVES so they cannot drift:
        taxonomy names  - scripts/seed-data/reference/packs/legal-standard/<table>.json (the D4 pack; the name column
                          each pack file declares in "nameColumn"),
        event types     - scripts/seed-data/reference/sprk_eventtype_ref.json (code, name, id, requires*Date),
        choice labels and record types - scripts/demo-data/reference-names.json (a metadata snapshot: no seed file
                          carries them).
      Returns an object shaped like the old snapshot (taxonomy, eventTypes, options, recordTypes) for Test-DemoDataset.
      Throws when a seed file is missing: a missing file is a failure, never an empty list.
    #>
    param(
        [string]$ScriptsRoot = (Join-Path $PSScriptRoot '..'),
        [string]$OptionsFile = (Join-Path $PSScriptRoot 'reference-names.json')
    )
    $packDir = Join-Path $ScriptsRoot 'seed-data' 'reference' 'packs' 'legal-standard'
    $eventSeed = Join-Path $ScriptsRoot 'seed-data' 'reference' 'sprk_eventtype_ref.json'
    foreach ($f in $packDir, $eventSeed, $OptionsFile) {
        if (-not (Test-Path $f)) { throw "Reference source not found: $f (the demo dataset is checked against the D4 taxonomy pack, the event-type seed and reference-names.json)." }
    }

    $taxonomy = [ordered]@{}
    foreach ($table in 'sprk_mattertype_ref', 'sprk_practicearea_ref', 'sprk_projecttype_ref', 'sprk_contacttype_ref', 'sprk_organizationtype_ref') {
        $file = Join-Path $packDir "$table.json"
        if (-not (Test-Path $file)) { throw "Taxonomy pack file not found: $file" }
        $pack = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
        $nameColumn = "$($pack.nameColumn)"
        if (-not $nameColumn) { throw "Taxonomy pack file $file declares no nameColumn." }
        $taxonomy[$table] = @($pack.rows | ForEach-Object { "$($_.$nameColumn)" })
    }

    $seed = Get-Content $eventSeed -Raw -Encoding UTF8 | ConvertFrom-Json
    $eventTypes = @($seed.rows | ForEach-Object {
            [pscustomobject]@{
                id               = "$($_.sprk_eventtype_refid)"
                code             = "$($_.sprk_eventtypecode)"
                name             = "$($_.sprk_name)"
                requiresDueDate  = [bool]$_.sprk_requiredduedate
                requiresBaseDate = [bool]$_.sprk_requiresbasedate
            }
        })

    $opts = Get-Content $OptionsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    return [pscustomobject]@{
        taxonomy    = [pscustomobject]$taxonomy
        eventTypes  = $eventTypes
        options     = $opts.options
        recordTypes = @($opts.recordTypes)
    }
}

function Test-DemoDataset {
    <#
      Validates the dataset against the table specs and, when -Reference (Get-DemoSeedReference) is given, against
      the reference values. Returns an array of error strings (empty = valid). Never throws on bad data.
    #>
    param([Parameter(Mandatory)]$Dataset, $Reference)

    $errors = New-Object System.Collections.Generic.List[string]
    $specs = @(Get-DemoTableSpecs)

    if (-not $Dataset.datasetVersion -or "$($Dataset.datasetVersion)" -notmatch '^\d+\.\d+\.\d+$') {
        $errors.Add("datasetVersion is missing or not 'major.minor.patch'.")
    }

    $allKeys = @{}
    $sectionKeys = @{}
    foreach ($spec in $specs) { $sectionKeys[$spec.Section] = @{} }

    # Pass 1: shape, keys, natural keys.
    foreach ($spec in $specs) {
        $rows = @($Dataset.($spec.Section))
        if ($rows.Count -eq 0 -or $null -eq $rows[0]) { $errors.Add("Section '$($spec.Section)' is missing or empty."); continue }
        $naturalKeys = @{}
        foreach ($row in $rows) {
            $key = "$($row.key)"
            if (-not $key) { $errors.Add("$($spec.Section): a row has no 'key'."); continue }
            $where = "$($spec.Section)/$key"
            if ($allKeys.ContainsKey($key)) { $errors.Add("$where : duplicate key (first used in $($allKeys[$key]))."); continue }
            $allKeys[$key] = $spec.Section
            $sectionKeys[$spec.Section][$key] = $row

            $fields = Get-DemoRowProperty $row 'fields'
            $nk = $fields[$spec.NaturalKey]
            if ([string]::IsNullOrWhiteSpace("$nk")) { $errors.Add("$where : fields.$($spec.NaturalKey) (the natural key) is missing.") }
            else {
                $nkLower = "$nk".ToLowerInvariant()
                if ($naturalKeys.ContainsKey($nkLower)) { $errors.Add("$where : natural key '$nk' ($($spec.NaturalKey)) duplicates $($naturalKeys[$nkLower]).") }
                else { $naturalKeys[$nkLower] = $key }
            }
            foreach ($f in $fields.Keys) {
                if ($f -in (Get-DemoReservedColumns)) { $errors.Add("$where : fields.$f is set by the loader and must not appear in the dataset.") }
                elseif ($f -notin $spec.Fields) { $errors.Add("$where : fields.$f is not a column the loader writes for $($spec.Section) (allowed: $($spec.Fields -join ', ')).") }
            }
        }
    }

    # Pass 2: references, options, links, dates, per-type rules.
    foreach ($spec in $specs) {
        foreach ($row in @($Dataset.($spec.Section))) {
            if ($null -eq $row -or -not $row.key) { continue }
            $where = "$($spec.Section)/$($row.key)"
            $rowRefs = Get-DemoRowProperty $row 'reference'
            $options = Get-DemoRowProperty $row 'options'
            $links = Get-DemoRowProperty $row 'links'
            $dates = Get-DemoRowProperty $row 'dates'

            foreach ($name in $spec.Required) {
                if (-not ($rowRefs.Contains($name) -or $options.Contains($name) -or $links.Contains($name))) { $errors.Add("$where : '$name' is required.") }
            }
            foreach ($name in $rowRefs.Keys) {
                if (-not $spec.Reference.Contains($name)) { $errors.Add("$where : reference.$name is not valid for $($spec.Section)."); continue }
                if ($Reference) {
                    $table = $spec.Reference[$name].Table
                    if ($table -eq 'sprk_eventtype_ref') {
                        if (-not (@($Reference.eventTypes) | Where-Object { $_.code -ceq "$($rowRefs[$name])" })) { $errors.Add("$where : event type code '$($rowRefs[$name])' is not an event type in scripts/seed-data/reference/sprk_eventtype_ref.json.") }
                    }
                    elseif ($Reference.taxonomy.PSObject.Properties.Name -notcontains $table -or @($Reference.taxonomy.$table) -cnotcontains "$($rowRefs[$name])") {
                        $errors.Add("$where : $name '$($rowRefs[$name])' is not a $table name in the D4 pack (scripts/seed-data/reference/packs/legal-standard/$table.json).")
                    }
                }
            }
            foreach ($name in $options.Keys) {
                if (-not $spec.Options.Contains($name)) { $errors.Add("$where : options.$name is not valid for $($spec.Section)."); continue }
                if ($Reference) {
                    $optKey = "$($spec.Logical).$($spec.Options[$name].Column)"
                    $labels = if ($Reference.options.PSObject.Properties.Name -contains $optKey) { @($Reference.options.$optKey) } else { @() }
                    if ($labels -cnotcontains "$($options[$name])") { $errors.Add("$where : $name '$($options[$name])' is not an option label of $optKey.") }
                }
            }
            $regardingCount = 0
            foreach ($name in $links.Keys) {
                if (-not $spec.Links.Contains($name)) { $errors.Add("$where : links.$name is not valid for $($spec.Section)."); continue }
                $target = $spec.Links[$name]
                if (-not $sectionKeys[$target.Section].ContainsKey("$($links[$name])")) { $errors.Add("$where : links.$name '$($links[$name])' is not a row key in section '$($target.Section)'.") }
                if ($target.Contains('Regarding')) { $regardingCount++ }
            }
            if ($spec.Regarding -is [hashtable] -and $spec.Regarding.Count -gt 0 -and $regardingCount -ne 1) {
                $errors.Add("$where : must be filed under exactly one matter or project (links.regardingMatter or links.regardingProject); it has $regardingCount.")
            }
            foreach ($col in $dates.Keys) {
                if ($col -notin $spec.Dates) { $errors.Add("$where : dates.$col is not a date column the loader writes for $($spec.Section) (allowed: $($spec.Dates -join ', ')).") }
                try { $null = ConvertTo-DemoDateValue -Offset "$($dates[$col])" -Today ([datetime]'2026-01-01') } catch { $errors.Add("$where : dates.$col - $($_.Exception.Message)") }
            }
            if ($spec.Section -eq 'events' -and $Reference -and $rowRefs.Contains('eventTypeCode')) {
                $et = @($Reference.eventTypes) | Where-Object { $_.code -ceq "$($rowRefs['eventTypeCode'])" } | Select-Object -First 1
                if ($et -and $et.requiresDueDate -and -not $dates.Contains('sprk_duedate')) { $errors.Add("$where : event type $($et.code) requires a due date (dates.sprk_duedate).") }
                if ($et -and $et.requiresBaseDate) { $errors.Add("$where : event type $($et.code) requires a base date, which the loader does not write.") }
            }
        }
    }
    return $errors.ToArray()
}

function Get-DemoReferenceRequirements {
    <#
      Every reference value the dataset needs from the TARGET stamp, de-duplicated: taxonomy names, event type codes,
      record types (by table), option labels. Used by the loader to resolve everything before it writes.
    #>
    param([Parameter(Mandatory)]$Dataset)
    $found = [ordered]@{}
    foreach ($spec in (Get-DemoTableSpecs)) {
        $anyRegarding = $false
        foreach ($row in @($Dataset.($spec.Section))) {
            if ($null -eq $row) { continue }
            $rowRefs = Get-DemoRowProperty $row 'reference'
            $options = Get-DemoRowProperty $row 'options'
            $links = Get-DemoRowProperty $row 'links'
            foreach ($name in $rowRefs.Keys) {
                if (-not $spec.Reference.Contains($name)) { continue }
                $table = $spec.Reference[$name].Table
                $found["T|$table|$($rowRefs[$name])"] = [pscustomobject]@{ Kind = 'taxonomy'; Table = $table; Entity = $null; Column = $null; Name = "$($rowRefs[$name])" }
            }
            foreach ($name in $options.Keys) {
                if (-not $spec.Options.Contains($name)) { continue }
                $col = $spec.Options[$name].Column
                $found["O|$($spec.Logical)|$col|$($options[$name])"] = [pscustomobject]@{ Kind = 'option'; Table = $null; Entity = $spec.Logical; Column = $col; Name = "$($options[$name])" }
            }
            foreach ($name in $links.Keys) {
                if ($spec.Links.Contains($name) -and $spec.Links[$name].Contains('Regarding')) {
                    $logical = $spec.Links[$name].Regarding
                    $found["T|sprk_recordtype_ref|$logical"] = [pscustomobject]@{ Kind = 'taxonomy'; Table = 'sprk_recordtype_ref'; Entity = $null; Column = $null; Name = $logical }
                }
            }
        }
    }
    return @($found.Values)
}

function New-DemoPayload {
    <#
      Builds the POST body for one dataset row. Pure: the Context carries everything resolved from the target.
        Context.Today        [datetime]   the day date offsets count from
        Context.OwnerTeamId  [string]     the team that owns the row (null = leave the operator as owner; the loader warns)
        Context.Nav          hashtable    entity logical name -> (lookup column -> navigation property name)
        Context.RefIds       hashtable    reference table -> (name or code -> id)
        Context.OptionValues hashtable    'entity.column' -> (label -> int value)
        Context.KeyIds       hashtable    section -> (row key -> id of the created or existing row)
        Context.KeyInfo      hashtable    section -> (row key -> @{ Name; Number }), for the regarding stamp
      The payload always carries the tag, and always carries a team owner when one is given.
    #>
    param(
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)]$Row,
        [Parameter(Mandatory)][hashtable]$Context
    )
    $payload = [ordered]@{}
    $payload['importsequencenumber'] = Get-DemoImportSequenceNumber

    foreach ($kv in (Get-DemoRowProperty $Row 'fields').GetEnumerator()) { $payload[$kv.Key] = $kv.Value }
    foreach ($kv in (Get-DemoRowProperty $Row 'dates').GetEnumerator()) { $payload[$kv.Key] = ConvertTo-DemoDateValue -Offset "$($kv.Value)" -Today $Context.Today }

    function Add-Bind([string]$Column, [string]$TargetSet, [string]$Id) {
        $nav = $Context.Nav[$Spec.Logical][$Column]
        if (-not $nav) { throw "No navigation property known for $($Spec.Logical).$Column on the target; the schema check should have caught this." }
        $payload["$nav@odata.bind"] = "/$TargetSet($Id)"
    }

    $taxonomy = Get-DemoTaxonomyTables
    foreach ($kv in (Get-DemoRowProperty $Row 'reference').GetEnumerator()) {
        $def = $Spec.Reference[$kv.Key]
        $id = $Context.RefIds[$def.Table]["$($kv.Value)"]
        if (-not $id) { throw "$($Spec.Section): $($def.Table) '$($kv.Value)' was not resolved." }
        Add-Bind $def.Column $taxonomy[$def.Table].EntitySet $id
        if ($def.Contains('CodeColumn')) { $payload[$def.CodeColumn] = "$($kv.Value)" }
    }
    foreach ($kv in (Get-DemoRowProperty $Row 'options').GetEnumerator()) {
        $def = $Spec.Options[$kv.Key]
        $map = $Context.OptionValues["$($Spec.Logical).$($def.Column)"]
        if ($null -eq $map -or -not $map.ContainsKey("$($kv.Value)")) { throw "$($Spec.Section): option '$($kv.Value)' of $($Spec.Logical).$($def.Column) was not resolved." }
        $payload[$def.Column] = [int]$map["$($kv.Value)"]
    }

    $allSpecs = @{}
    foreach ($s in (Get-DemoTableSpecs)) { $allSpecs[$s.Section] = $s }
    foreach ($kv in (Get-DemoRowProperty $Row 'links').GetEnumerator()) {
        $def = $Spec.Links[$kv.Key]
        $id = $Context.KeyIds[$def.Section]["$($kv.Value)"]
        if (-not $id) { throw "$($Spec.Section): link '$($kv.Key)' -> '$($kv.Value)' has no row id (the parent was not created)." }
        Add-Bind $def.Column $allSpecs[$def.Section].EntitySet $id
        if ($def.Contains('Regarding') -and $Spec.Regarding -is [hashtable] -and $Spec.Regarding.Count -gt 0) {
            $info = $Context.KeyInfo[$def.Section]["$($kv.Value)"]
            $recordTypeId = $Context.RefIds['sprk_recordtype_ref'][$def.Regarding]
            if (-not $recordTypeId) { throw "$($Spec.Section): record type for table '$($def.Regarding)' was not resolved." }
            Add-Bind $Spec.Regarding.RecordType 'sprk_recordtype_refs' $recordTypeId
            $payload[$Spec.Regarding.Id] = "$id"
            if ($info.Name) { $payload[$Spec.Regarding.Name] = $info.Name }
            if ($info.Number) { $payload[$Spec.Regarding.Number] = $info.Number }
        }
    }

    if ($Spec.Section -eq 'documents') { $payload['sprk_hasfile'] = $false }

    if ($Context.OwnerTeamId) { $payload['ownerid@odata.bind'] = "/teams($($Context.OwnerTeamId))" }
    return $payload
}

function Get-DemoNaturalKeyValue {
    param([Parameter(Mandatory)][hashtable]$Spec, [Parameter(Mandatory)]$Row)
    return "$((Get-DemoRowProperty $Row 'fields')[$Spec.NaturalKey])"
}

# --- The guard ---------------------------------------------------------------------------------------------------------

function Resolve-DemoDataverseUrl {
    <# 'https://x.crm.dynamics.com/' -> 'https://x.crm.dynamics.com'. Refuses anything that is not a Dataverse org URL. #>
    param([Parameter(Mandatory)][string]$Url)
    $u = $Url.Trim().TrimEnd('/')
    if ($u -notmatch '^https://[a-z0-9][a-z0-9-]*\.crm\d*\.dynamics\.com$') {
        throw "'$Url' is not a Dataverse environment URL (expected https://<org>.crm.dynamics.com, host only)."
    }
    return $u.ToLowerInvariant()
}

function Get-DemoEnvironmentTypeName {
    param($Value)
    $names = @{ 0 = 'Development'; 1 = 'Demo'; 2 = 'Sandbox'; 3 = 'Trial'; 4 = 'Partner'; 5 = 'Training'; 6 = 'Production' }
    if ($null -eq $Value -or "$Value" -eq '') { return '(not set)' }
    if ($names.ContainsKey([int]$Value)) { return $names[[int]$Value] }
    return "unknown($Value)"
}

function Test-DemoTargetAllowed {
    <#
      The demo guard (data plan Tier 4: never a production customer). The registry row (sprk_dataverseenvironment) of the
      target must be marked Environment Type = Demo (1) and be active. -AllowNonDemo is the operator's explicit override
      for any other row, or for a URL with no row, and always comes with a warning. A Production row (6) is refused even
      with the override: demo records on a paying customer's stamp are never the intent.
      Returns Allowed, Reasons (why refused) and Warnings (what the override accepted).
    #>
    param($RegistryRow, [switch]$AllowNonDemo)
    $reasons = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]

    if ($null -eq $RegistryRow) {
        # Only reached after a SUCCESSFUL registry read (a failed read throws in Get-DemoRegistryRowForUrl and never gets here).
        $reasons.Add('The registry was read successfully but has no row (sprk_dataverseenvironment) for this URL, so the target cannot be shown to be a demo stamp.')
    }
    else {
        $type = $RegistryRow.sprk_environmenttype
        $typeName = Get-DemoEnvironmentTypeName $type
        if ($null -ne $type -and "$type" -ne '' -and [int]$type -eq 6) {
            return [pscustomobject]@{ Allowed = $false; Reasons = @("The registry marks '$($RegistryRow.sprk_name)' as Production. Demo data is never loaded into a production environment, with or without -AllowNonDemo."); Warnings = @() }
        }
        if ($null -eq $type -or "$type" -eq '' -or [int]$type -ne 1) { $reasons.Add("The registry marks '$($RegistryRow.sprk_name)' as Environment Type '$typeName', not Demo.") }
        if ($RegistryRow.sprk_isactive -ne $true) { $reasons.Add("The registry row '$($RegistryRow.sprk_name)' is not active.") }
    }

    if ($reasons.Count -eq 0) { return [pscustomobject]@{ Allowed = $true; Reasons = @(); Warnings = @() } }
    if ($AllowNonDemo) {
        foreach ($r in $reasons) { $warnings.Add("-AllowNonDemo: $r") }
        return [pscustomobject]@{ Allowed = $true; Reasons = @(); Warnings = $warnings.ToArray() }
    }
    return [pscustomobject]@{ Allowed = $false; Reasons = $reasons.ToArray(); Warnings = @() }
}

function ConvertTo-DemoComparableHost {
    <#
      The host of a registry or target URL in one canonical form: 'https://X.crm.dynamics.com./main.aspx?a=1' ->
      'x.crm.dynamics.com'. Lower case, a trailing dot (a fully qualified name) removed, and the Web API alias host
      'x.api.crm.dynamics.com' mapped to the organization host 'x.crm.dynamics.com' (same environment). Used on BOTH sides
      of a registry comparison, so a trailing slash, a path, upper case, a port, a trailing dot or the api host never
      makes a Production row "not match". $null when no host can be read.
    #>
    param([string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url)) { return $null }
    if ($Url.Trim() -match '^(?i:(?:https?://)?)([a-z0-9][a-z0-9.-]*)(?::\d+)?(?:[/?#].*)?$') { return (ConvertTo-DemoCanonicalHost $Matches[1]) }
    return $null
}

function ConvertTo-DemoCanonicalHost {
    <# Lower case, no trailing dot, '<org>.api.crm<N>.dynamics.com' -> '<org>.crm<N>.dynamics.com'. #>
    param([Parameter(Mandatory)][string]$HostName)
    $h = $HostName.Trim().ToLowerInvariant().TrimEnd('.')
    return ($h -replace '^([a-z0-9][a-z0-9-]*)\.api\.(crm\d*\.dynamics\.com)$', '$1.$2')
}

function Test-DemoRegistryUrlMatch {
    <# Does a registry row's sprk_dataverseurl name the target? A value whose shape cannot be read still matches when it mentions the target host (fail towards refusing). #>
    param([string]$RowUrl, [Parameter(Mandatory)][string]$Target)
    $t = ConvertTo-DemoComparableHost $Target
    if (-not $t) { throw "Target '$Target' has no readable host." }
    $h = ConvertTo-DemoComparableHost $RowUrl
    if ($h) { return ($h -eq $t) }
    # Unreadable shape: look for the host inside the text, with the same normalisation (api alias, trailing dot) as above.
    $text = $RowUrl.ToLowerInvariant() -replace '\.api\.(crm\d*\.dynamics\.com)', '.$1' -replace '(dynamics\.com)\.(?=[^a-z0-9-]|$)', '$1'
    return $text.Contains($t)
}

function Get-DemoRegistryRowForUrl {
    <#
      The registry row for a target URL. -ReadRows returns every registry row (the loader's reader calls Dataverse).
      A FAILED READ THROWS: it is never "no row", because "no row" lets -AllowNonDemo through. Precedence among rows that
      name the URL: a Production row (always, active or not) > the one active row (two active rows throw) > the first
      inactive row. $null only when the registry was read and no row names the URL.
    #>
    param([Parameter(Mandatory)][scriptblock]$ReadRows, [Parameter(Mandatory)][string]$Target)
    try { $rows = @(& $ReadRows) }
    catch { throw "The environment registry could not be read ($_). Refusing: a failed read is never treated as 'no registry row'." }
    $match = @($rows | Where-Object { $_.sprk_dataverseurl -and (Test-DemoRegistryUrlMatch -RowUrl "$($_.sprk_dataverseurl)" -Target $Target) })
    $production = @($match | Where-Object { $null -ne $_.sprk_environmenttype -and "$($_.sprk_environmenttype)" -ne '' -and [int]$_.sprk_environmenttype -eq 6 })
    if ($production.Count -ge 1) { return $production[0] }
    $active = @($match | Where-Object { $_.sprk_isactive -eq $true })
    if ($active.Count -gt 1) { throw "$($active.Count) active registry rows name the URL $Target; refusing to guess." }
    if ($active.Count -eq 1) { return $active[0] }
    if ($match.Count -ge 1) { return $match[0] }
    return $null
}

function Resolve-DemoTarget {
    <#
      The guard wiring: registry row + guard verdict for one target. -CustomerRow is the row found by -CustomerId (the
      URL lookup still runs, and a Production row anywhere that names the same URL wins). Throws when the registry
      cannot be read; returns Row and Guard (Test-DemoTargetAllowed) otherwise.
    #>
    param([Parameter(Mandatory)][scriptblock]$ReadRows, [Parameter(Mandatory)][string]$Target, $CustomerRow, [switch]$AllowNonDemo)
    $urlRow = Get-DemoRegistryRowForUrl -ReadRows $ReadRows -Target $Target
    $row = if ($CustomerRow) { $CustomerRow } else { $urlRow }
    if ($CustomerRow -and $urlRow -and "$($urlRow.sprk_environmenttype)" -ne '' -and [int]$urlRow.sprk_environmenttype -eq 6) { $row = $urlRow }
    $guard = Test-DemoTargetAllowed -RegistryRow $row -AllowNonDemo:$AllowNonDemo
    return [pscustomobject]@{ Row = $row; Guard = $guard }
}

function Assert-DemoTargetAllowed {
    <#
      The refusal itself. The loader calls this unconditionally, before anything else touches the target: it THROWS when the
      guard says no (a Production row, a non-Demo or inactive row, no row without -AllowNonDemo) and when the registry
      cannot be read, and returns Row and Guard (with the warnings an -AllowNonDemo override accepted) otherwise. Because
      the refusal is a throw inside this function, a caller cannot "forget" to test a flag: there is no flag to test.
    #>
    param([Parameter(Mandatory)][scriptblock]$ReadRows, [Parameter(Mandatory)][string]$Target, $CustomerRow, [switch]$AllowNonDemo)
    $resolved = Resolve-DemoTarget -ReadRows $ReadRows -Target $Target -CustomerRow $CustomerRow -AllowNonDemo:$AllowNonDemo
    if (-not $resolved.Guard.Allowed) {
        throw ("REFUSED: '$Target' is not a demo stamp. $(@($resolved.Guard.Reasons) -join ' ') " +
            'Mark the environment Demo in the registry, or pass -AllowNonDemo if you are sure (never for Production). Nothing was read from or written to the target.')
    }
    return $resolved
}

function Test-DemoOperatorSeesAllRows {
    <#
      Dataverse silently hides rows the caller cannot read: a reset run by a user who cannot read every row would not see
      an untagged row that points at a demo row, and would delete the demo row anyway. So the operator must be a System
      Administrator on the target. -Get is the loader's reader (path -> rows). Roles come from the user's own role
      assignments and from the roles of the teams the user belongs to. Returns IsSystemAdministrator, Roles and, when the
      roles could not be read, Problem (never "verified" on a failed read).
    #>
    param([Parameter(Mandatory)][scriptblock]$Get, [Parameter(Mandatory)][string]$UserId)
    if ($UserId -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { throw "WhoAmI returned '$UserId', which is not a user id." }
    $roles = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($r in @(& $Get "systemusers($UserId)/systemuserroles_association?`$select=name")) { $roles.Add("$($r.name)") }
        foreach ($t in @(& $Get "systemusers($UserId)/teammembership_association?`$select=teamid")) {
            foreach ($r in @(& $Get "teams($($t.teamid))/teamroles_association?`$select=name")) { $roles.Add("$($r.name)") }
        }
    }
    catch { return [pscustomobject]@{ IsSystemAdministrator = $false; Roles = @(); Problem = "the operator's security roles could not be read ($_)" } }
    return [pscustomobject]@{ IsSystemAdministrator = [bool]($roles -contains 'System Administrator'); Roles = @($roles | Select-Object -Unique); Problem = $null }
}

function Test-DemoOperatorIdentity {
    <# The operator signs in as themselves (NFR-11): a service principal is refused. Input is `az account show` as JSON. #>
    param([Parameter(Mandatory)]$Account)
    $type = "$($Account.user.type)"
    if ($type -ieq 'servicePrincipal') { return [pscustomobject]@{ Allowed = $false; Reason = "az is signed in as a service principal ($($Account.user.name)). The demo loader runs under the operator's own user identity (NFR-11)." } }
    if (-not $Account.user.name) { return [pscustomobject]@{ Allowed = $false; Reason = 'az reports no signed-in user. Run az login.' } }
    return [pscustomobject]@{ Allowed = $true; Reason = $null }
}

function Get-DemoOwnerPlan {
    <#
      Which business unit and team own the demo rows. Convention (H10, CustomerBusinessUnitIntake): the customer's business
      unit is named after the customer's display name = the registry row's sprk_name; its default Owner team is the team of
      the same name. A registry row may name a unit or team explicitly (sprk_businessunitname, sprk_teamname); parameters
      override the row. Returns BusinessUnitName, TeamName (null = the unit's default team) and Source.
    #>
    param($RegistryRow, [string]$OwnerBusinessUnitName, [string]$OwnerTeamName)
    $bu = $null; $team = $null; $source = $null
    if ($OwnerBusinessUnitName) { $bu = $OwnerBusinessUnitName; $team = $OwnerTeamName; $source = 'parameter' }
    elseif ($RegistryRow) {
        if ($RegistryRow.sprk_businessunitname) {
            # -OwnerTeamName alone overrides the registry's team (the unit still comes from the registry).
            $bu = "$($RegistryRow.sprk_businessunitname)"; $source = 'registry (sprk_businessunitname)'
            if ($OwnerTeamName) { $team = $OwnerTeamName; $source += ' + -OwnerTeamName' } else { $team = "$($RegistryRow.sprk_teamname)" }
        }
        elseif ($RegistryRow.sprk_name) { $bu = "$($RegistryRow.sprk_name)"; $team = $OwnerTeamName; $source = 'registry (sprk_name = customer display name)' }
    }
    if ($team -eq '') { $team = $null }
    return [pscustomobject]@{ BusinessUnitName = $bu; TeamName = $team; Source = $source }
}

function Test-DemoReservedBusinessUnit {
    <# A customer's demo rows never go to the Secure Record unit or the ontology writer's unit (the same list H10 refuses). #>
    param([Parameter(Mandatory)][string]$Name)
    return @('Secure Record', 'Spaarke Ontology Writer') -contains $Name.Trim()
}

# --- Reset -------------------------------------------------------------------------------------------------------------

function Select-DemoDeletableRows {
    <#
      Reset deletes a tagged row only when no UNTAGGED row points at it: every Spaarke lookup is RemoveLink on delete, so
      deleting a demo matter would silently null the regarding column of a row a user filed under it, which is an edit of
      an untagged row. Rows are @{ Id; Name }. UntaggedReferences maps a parent id (lower case) to the 'table.column'
      strings of untagged rows that point at it. Returns Deletable and Kept (each Kept row carries Reasons).
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Rows, [hashtable]$UntaggedReferences = @{})
    $deletable = New-Object System.Collections.Generic.List[object]
    $kept = New-Object System.Collections.Generic.List[object]
    foreach ($row in $Rows) {
        $refs = $UntaggedReferences["$($row.Id)".ToLowerInvariant()]
        if ($refs -and @($refs).Count -gt 0) { $kept.Add([pscustomobject]@{ Id = $row.Id; Name = $row.Name; Reasons = @($refs | Select-Object -Unique) }) }
        else { $deletable.Add($row) }
    }
    return [pscustomobject]@{ Deletable = $deletable.ToArray(); Kept = $kept.ToArray() }
}

function Get-DemoTaggedFilter {
    <# The only filter reset ever deletes by. #>
    return "importsequencenumber eq $(Get-DemoImportSequenceNumber)"
}

function Get-DemoTaggedRows {
    <# The tagged rows of one table as @{ Id; Name } (Name = the natural key). -Get takes a Web API path and returns every row (paged). A read error throws. #>
    param([Parameter(Mandatory)][scriptblock]$Get, [Parameter(Mandatory)][hashtable]$Spec)
    $path = ConvertTo-DemoQuery -Set $Spec.EntitySet -Select "$($Spec.IdColumn),$($Spec.NaturalKey)" -Filter (Get-DemoTaggedFilter)
    return @(@(& $Get $path) | ForEach-Object { [pscustomobject]@{ Id = "$($_.($Spec.IdColumn))"; Name = "$($_.($Spec.NaturalKey))" } })
}

function Get-DemoBlockingReferences {
    <#
      What stops a tagged row being deleted. A row blocks when it still exists after this run's deletes and points at the
      tagged row through ANY 1:N relationship (every Spaarke lookup is RemoveLink, so the delete would silently edit that
      row; a cascade-delete relationship, e.g. a note or an activity regarding a contact, would silently DELETE it), or
      through a many-to-many association whose other side is not a tagged row.
        -Get   reads a Web API path and returns every row (the loader's reader pages through nextLink). Injectable so the
               tests can fake Dataverse.
        -Gone  lower-case ids of tagged rows this run has deleted or, under -WhatIf, would delete. Those do not block:
               children are deleted before parents, so by the time a parent is checked its tagged children are gone.
               A tagged child that was KEPT is not in the set and does block (its parent is kept too).
      EVERY 1:N relationship into the table is checked except the system plumbing columns (created/modified by,
      owner*): the old filter looked only at sprk_/contact/account tables and missed email, systemuser, notes and the like.
      FAILS CLOSED: a relationship that cannot be read is returned in Unchecked, and the caller keeps every row. So is a
      metadata read that returns NO 1:N relationship at all (every table has some, so zero means the caller cannot see them).
      The one exception is an explicit allow-list of referencing system tables that the platform never lets anyone read (below).
      Returns Refs (parent id lower case -> reason strings), Unchecked and NotRetrievable (strings).
    #>
    param(
        [Parameter(Mandatory)][scriptblock]$Get,
        [Parameter(Mandatory)][string]$Logical,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Ids,
        [System.Collections.Generic.HashSet[string]]$Gone
    )
    if ($null -eq $Gone) { $Gone = New-Object 'System.Collections.Generic.HashSet[string]' }
    $refs = @{}
    $unchecked = New-Object System.Collections.Generic.List[string]
    $parents = @($Ids | ForEach-Object { "$_".ToLowerInvariant() })
    if ($parents.Count -eq 0) { return [pscustomobject]@{ Refs = $refs; Unchecked = @() } }
    $plumbing = @('createdby', 'modifiedby', 'createdonbehalfby', 'modifiedonbehalfby', 'owninguser', 'owningteam', 'owningbusinessunit', 'ownerid')
    $notRetrievable = New-Object System.Collections.Generic.List[string]
    # The ONE read error that is not a failure, and only for these referencing tables: postrole and
    # subscriptionmanuallytrackedobject answer every Web API read with "RetrieveMultiple does not support entities of type"
    # (seen live on the ACME stamp) and hold platform plumbing a user never files (a wall's role grants, auto-follow
    # subscriptions). ANY OTHER table that returns that error is Unchecked, so a table nobody has looked at fails closed.
    # postregarding is deliberately NOT here: it answers the same error, but it is the only way a post on a contact or
    # account wall points at that row ('post' is not a 1:N relationship of contact; checked on ACME), so being unable to
    # read it means a user's post may exist. That keeps contacts and organizations on reset (see scripts/demo-data/README.md).
    $notRetrievablePattern = 'method does not support entities of type'
    $notRetrievableTables = @('postrole', 'subscriptionmanuallytrackedobject')
    $chunkSize = 20
    $infoCache = @{}
    $taggedCache = @{}
    $logicals = @(Get-DemoTableSpecs | ForEach-Object { $_.Logical })

    $entityInfo = {
        param([string]$Name)
        if (-not $infoCache.ContainsKey($Name)) {
            $found = @(& $Get (ConvertTo-DemoQuery -Set 'EntityDefinitions' -Select 'LogicalName,EntitySetName,PrimaryIdAttribute' -Filter "LogicalName eq '$(ConvertTo-DemoODataString $Name)'"))
            if ($found.Count -ne 1 -or -not $found[0].EntitySetName) { throw "metadata for table '$Name' is not readable" }
            $infoCache[$Name] = @{ EntitySet = "$($found[0].EntitySetName)"; PrimaryId = "$($found[0].PrimaryIdAttribute)" }
        }
        return $infoCache[$Name]
    }
    $addRef = {
        param([string]$Parent, [string]$Label)
        if (-not $refs.ContainsKey($Parent)) { $refs[$Parent] = New-Object System.Collections.Generic.List[string] }
        $refs[$Parent].Add($Label)
    }

    # 1:N - every referencing table.
    $oneToMany = @()
    try {
        $oneToMany = @(& $Get (ConvertTo-DemoQuery -Set "EntityDefinitions(LogicalName='$Logical')/OneToManyRelationships" -Select 'SchemaName,ReferencingEntity,ReferencingAttribute'))
    }
    catch { $unchecked.Add("1:N relationships of $Logical ($_)") }
    # Every table has 1:N relationships (at least the created/modified/owner columns). None at all means the metadata was
    # hidden from this caller or the read was cut short: nothing is known about what points at these rows.
    if ($oneToMany.Count -eq 0 -and $unchecked.Count -eq 0) { $unchecked.Add("1:N relationships of $Logical (the metadata read returned none; every table has some, so the operator may not be able to see them)") }
    foreach ($rel in $oneToMany) {
        if ("$($rel.ReferencingAttribute)" -in $plumbing) { continue }
        $label = "$($rel.ReferencingEntity).$($rel.ReferencingAttribute)"
        try {
            $info = & $entityInfo "$($rel.ReferencingEntity)"
            if (-not $info.PrimaryId) { throw "no primary id column for '$($rel.ReferencingEntity)'" }
            $col = "$($rel.ReferencingAttribute)"
            for ($i = 0; $i -lt $parents.Count; $i += $chunkSize) {
                $chunk = @($parents[$i..([math]::Min($i + $chunkSize - 1, $parents.Count - 1))])
                $anyOf = ($chunk | ForEach-Object { "_$($col)_value eq $_" }) -join ' or '
                $rows = @(& $Get (ConvertTo-DemoQuery -Set $info.EntitySet -Select "_$($col)_value,$($info.PrimaryId)" -Filter $anyOf))
                foreach ($r in $rows) {
                    if ($Gone.Contains("$($r.($info.PrimaryId))".ToLowerInvariant())) { continue }
                    $parentId = ("$($r.("_$($col)_value"))").ToLowerInvariant()
                    & $addRef $parentId $label
                }
            }
        }
        catch { if ("$_" -match $notRetrievablePattern -and "$($rel.ReferencingEntity)" -in $notRetrievableTables) { $notRetrievable.Add($label) } else { $unchecked.Add("$label ($_)") } }
    }

    # M:N - every association of the table.
    $manyToMany = @()
    try {
        $manyToMany = @(& $Get (ConvertTo-DemoQuery -Set "EntityDefinitions(LogicalName='$Logical')/ManyToManyRelationships" -Select 'SchemaName,IntersectEntityName,Entity1LogicalName,Entity1IntersectAttribute,Entity2LogicalName,Entity2IntersectAttribute'))
    }
    catch { $unchecked.Add("M:N relationships of $Logical ($_)") }
    foreach ($rel in $manyToMany) {
        $sides = @()
        if ("$($rel.Entity1LogicalName)" -eq $Logical) { $sides += @{ Mine = "$($rel.Entity1IntersectAttribute)"; Other = "$($rel.Entity2IntersectAttribute)"; OtherEntity = "$($rel.Entity2LogicalName)" } }
        if ("$($rel.Entity2LogicalName)" -eq $Logical) { $sides += @{ Mine = "$($rel.Entity2IntersectAttribute)"; Other = "$($rel.Entity1IntersectAttribute)"; OtherEntity = "$($rel.Entity1LogicalName)" } }
        foreach ($side in $sides) {
            $label = "m:n $($rel.SchemaName)"
            try {
                $info = & $entityInfo "$($rel.IntersectEntityName)"
                for ($i = 0; $i -lt $parents.Count; $i += $chunkSize) {
                    $chunk = @($parents[$i..([math]::Min($i + $chunkSize - 1, $parents.Count - 1))])
                    $anyOf = ($chunk | ForEach-Object { "$($side.Mine) eq $_" }) -join ' or '
                    $rows = @(& $Get (ConvertTo-DemoQuery -Set $info.EntitySet -Select "$($side.Mine),$($side.Other)" -Filter $anyOf))
                    if ($rows.Count -eq 0) { continue }
                    # The other side may be a tagged demo row (it goes in this reset or is a demo row anyway): then the
                    # association is demo-to-demo and does not block. Anything else is someone else's link.
                    if (-not $taggedCache.ContainsKey($side.OtherEntity)) {
                        $set = New-Object 'System.Collections.Generic.HashSet[string]'
                        if ($side.OtherEntity -in $logicals) {
                            $oi = & $entityInfo $side.OtherEntity
                            foreach ($t in @(& $Get (ConvertTo-DemoQuery -Set $oi.EntitySet -Select $oi.PrimaryId -Filter (Get-DemoTaggedFilter)))) { $null = $set.Add("$($t.($oi.PrimaryId))".ToLowerInvariant()) }
                        }
                        $taggedCache[$side.OtherEntity] = $set
                    }
                    foreach ($r in $rows) {
                        $other = "$($r.($side.Other))".ToLowerInvariant()
                        if ($Gone.Contains($other) -or $taggedCache[$side.OtherEntity].Contains($other)) { continue }
                        $parentId = ("$($r.($side.Mine))").ToLowerInvariant()
                        & $addRef $parentId $label
                    }
                }
            }
            catch { $unchecked.Add("$label ($_)") }   # no intersect table is on the allow-list: an unreadable association is never skipped
        }
    }
    return [pscustomobject]@{ Refs = $refs; Unchecked = $unchecked.ToArray(); NotRetrievable = $notRetrievable.ToArray() }
}

function Invoke-DemoResetSection {
    <#
      Resets ONE table: reads its tagged rows, keeps every row something else still points at (Get-DemoBlockingReferences;
      fails closed), deletes the rest one by one. Rows are only ever found by the tag filter, so nothing untagged is
      deleted. Injectable so the tests run it without Dataverse:
        -Get      path -> every row.            -Delete   { param($Spec, $Row) } performs one DELETE.
        -Approve  { param($Target, $Action) } returns $false under -WhatIf (the row is counted WouldDelete, never deleted).
        -Gone     shared set of ids deleted or would-be-deleted so far in this run (see Get-DemoBlockingReferences).
        -Say      { param($Text, $Color) } progress output.
      Returns Counts (Deleted / WouldDelete / Kept / Errors) and Kept (Section, Name, Reasons).
    #>
    param(
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)][scriptblock]$Get,
        [Parameter(Mandatory)][scriptblock]$Delete,
        [Parameter(Mandatory)][scriptblock]$Approve,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$Gone,
        [scriptblock]$Say = { }
    )
    $counts = [ordered]@{ Deleted = 0; WouldDelete = 0; Kept = 0; Errors = 0 }
    $keptReport = New-Object System.Collections.Generic.List[object]
    $rows = @(Get-DemoTaggedRows -Get $Get -Spec $Spec)
    if ($rows.Count -eq 0) {
        & $Say '  nothing tagged' 'Gray'
        return [pscustomobject]@{ Counts = $counts; Kept = @() }
    }

    $check = Get-DemoBlockingReferences -Get $Get -Logical $Spec.Logical -Ids @($rows | ForEach-Object { $_.Id }) -Gone $Gone
    $blocking = $check.Refs
    if (@($check.NotRetrievable).Count -gt 0) { & $Say "  Not checked (the platform does not allow reading them): $(@($check.NotRetrievable) -join ', ')" 'DarkGray' }
    if ($check.Unchecked.Count -gt 0) {
        & $Say "  Could not check $($check.Unchecked.Count) relationship(s); keeping every row of this table (fail closed):" 'Yellow'
        foreach ($u in $check.Unchecked) { & $Say "    $u" 'Yellow' }
        $blocking = @{}
        foreach ($r in $rows) { $blocking[$r.Id.ToLowerInvariant()] = @("a relationship could not be read ($($check.Unchecked.Count) unreadable)") }
    }
    $plan = Select-DemoDeletableRows -Rows $rows -UntaggedReferences $blocking
    foreach ($k in $plan.Kept) {
        $counts.Kept++
        $keptReport.Add([pscustomobject]@{ Section = $Spec.Section; Name = $k.Name; Reasons = @($k.Reasons) })
        & $Say "  KEEP   '$($k.Name)': still referenced by rows this reset does not delete: $($k.Reasons -join ', ')" 'Yellow'
    }
    foreach ($row in $plan.Deletable) {
        if ([bool](& $Approve "$($Spec.Logical) '$($row.Name)' ($($row.Id))" "Delete tagged demo row (importsequencenumber = $(Get-DemoImportSequenceNumber))")) {
            try {
                & $Delete $Spec $row
                $counts.Deleted++
                $null = $Gone.Add($row.Id.ToLowerInvariant())
                & $Say "  DELETED '$($row.Name)'" 'Green'
            }
            catch { $counts.Errors++; & $Say "  ERROR  '$($row.Name)': $_" 'Red' }
        }
        else {
            $counts.WouldDelete++
            $null = $Gone.Add($row.Id.ToLowerInvariant())
        }
    }
    return [pscustomobject]@{ Counts = $counts; Kept = $keptReport.ToArray() }
}

