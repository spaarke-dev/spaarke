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

function Test-DemoDataset {
    <#
      Validates the dataset against the table specs and, when -Reference (reference-names.json) is given, against the
      reference snapshot. Returns an array of error strings (empty = valid). Never throws on bad data.
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
                        if (-not (@($Reference.eventTypes) | Where-Object { $_.code -ceq "$($rowRefs[$name])" })) { $errors.Add("$where : event type code '$($rowRefs[$name])' is not one of the 17 product event types.") }
                    }
                    elseif ($Reference.taxonomy.PSObject.Properties.Name -notcontains $table -or @($Reference.taxonomy.$table) -cnotcontains "$($rowRefs[$name])") {
                        $errors.Add("$where : $name '$($rowRefs[$name])' is not a $table name in reference-names.json (data plan section 6).")
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
        $reasons.Add('The target has no environment registry row (sprk_dataverseenvironment), so it cannot be shown to be a demo stamp.')
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
        if ($RegistryRow.sprk_businessunitname) { $bu = "$($RegistryRow.sprk_businessunitname)"; $team = "$($RegistryRow.sprk_teamname)"; $source = 'registry (sprk_businessunitname)' }
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

function Get-DemoUntaggedFilter {
    param([Parameter(Mandatory)][string]$LookupColumn)
    $tag = Get-DemoImportSequenceNumber
    return "_$($LookupColumn)_value ne null and (importsequencenumber eq null or importsequencenumber ne $tag)"
}
