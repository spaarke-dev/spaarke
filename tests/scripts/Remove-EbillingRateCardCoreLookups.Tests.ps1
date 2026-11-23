# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Remove-EbillingRateCardCoreLookups.Tests.ps1
# ebilling-r1 task 193. Remove-EbillingRateCardCoreLookups.ps1 retires sprk_ratecard's three core-record lookups and their relationships.
# It begins with `using module` and runs against an environment, so its functions and the constants they read are lifted out of its own
# syntax tree (as Remove-EbillingSourceDocumentColumns.Tests.ps1 does) and run against a stateful test double of the Dataverse calls: no
# environment, no network. The deploy attestation checks run against a throw-away git repository.
#   T1  the targets and the pure transforms (form, view);
#   T2  the census reads only, and a column that is already gone is not read for rows;
#   T3  the refusals: a rate card holding a value, an unknown dependent, a personal view, a form that cannot be cleaned;
#   T4  -Apply: refuses before the first write; -WhatIf writes nothing; the happy path writes in order; a second -Apply is a no-op pass;
#   T5  -Verify fails while an attribute, relationship, form or view remains;
#   T6  the pre-flight (approval, attestations, environment) and the attestation scan (must-fire and must-not-fire, throw-away repo);
#   T7  structure: read-only by construction, writes only behind Confirm-Write, no direct REST call (Consistency: Strong stays in the module);
#   T8  the schema script, the forms script and the solution source no longer name the lookups.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Remove-EbillingRateCardCoreLookups.ps1'

    $tokens = $null; $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'DevHost', 'Table', 'EntitySet', 'IdColumn', 'Targets', 'CoreLookupPattern', 'RelationshipNamesRegex', 'RateCardMentionRegex', 'CoreColumnMentionRegex', 'BffScanPaths', 'SpaScanPaths'
    $wantedFunctions = 'Get-ReaderProblems', 'Get-PreflightRefusals', 'Test-MentionsCoreLookup', 'Get-MentionCount', 'ConvertTo-FormWithoutCoreLookups', 'ConvertTo-ViewWithoutCoreLookups',
        'Read-Dv', 'Read-DvOrNull', 'Get-ComponentKind', 'Get-DependencyList', 'Get-State', 'Write-State', 'Test-AlreadyRetired', 'Get-ApplyBlockers', 'Get-VerifyProblems',
        'Edit-FormsAndViews', 'Get-PublishContext', 'Get-PendingCollateral', 'Publish-RateCard', 'Remove-Relationships', 'Wait-ColumnsGone', 'Invoke-Retirement'

    $pieces = [System.Collections.Generic.List[string]]::new()
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($statement in $script:Ast.EndBlock.Statements) {
        if ($statement -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $statement.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $statement.Left.VariablePath.UserPath -in $wantedVariables) {
            $pieces.Add($statement.Extent.Text); $found.Add($statement.Left.VariablePath.UserPath)
        }
        elseif ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $statement.Name -in $wantedFunctions) {
            $pieces.Add($statement.Extent.Text); $found.Add($statement.Name)
        }
    }
    $missing = @($wantedVariables + $wantedFunctions | Where-Object { $_ -notin $found })
    if ($missing.Count -gt 0) { throw "Not found in the script: $($missing -join ', ')" }

    # The caller-can-read-every-row check lives in the shared module (the source-document script uses it too): lifted from the module's own
    # syntax tree and run against the same test double of Invoke-Dv.
    $script:ModulePath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'EbillingSchema.psm1'
    $moduleAst = [System.Management.Automation.Language.Parser]::ParseFile($script:ModulePath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The module does not parse: $($errors[0].Message)" }
    $gapFunction = @($moduleAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ReadAllRowsGap' }, $true))
    if ($gapFunction.Count -ne 1) { throw 'Get-ReadAllRowsGap not found in EbillingSchema.psm1' }
    $pieces.Add($gapFunction[0].Extent.Text)

    # The module's exception class is not exported by Import-Module: a test double with the same name and shape.
    class EscalationException : System.Exception {
        EscalationException([string]$message) : base($message) {}
    }

    # ---- Test doubles of the module's calls, over a stateful fake of dev. $script:Dev is set per test. ----
    function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }
    function Invoke-WhilePropagating { param([scriptblock]$Action) & $Action }
    # -WhatIf is a double that answers no; $script:Allow = $false makes every write refuse.
    function Confirm-Write { param([string]$Target, [string]$Action) $script:Dev.Asked.Add("$Action | $Target"); return [bool]$script:Allow }

    # The shared D-83 procedure (scripts/lib/Publish-SolutionComponents.ps1): doubles that apply -ExcludeIds like the real one and record
    # every call. $script:Dev.Collateral rows are @{ Id; Text }; CollateralAfterEdit rows appear once a form or view was edited.
    $EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com'
    function Get-DataverseApiContext { param([string]$EnvironmentUrl) $script:Dev.ContextUrls.Add($EnvironmentUrl); return @{ Api = 'https://dev.invalid/api/data/v9.2'; Headers = @{} } }
    function Get-EntityPublishCollateral {
        param([hashtable]$Context, [string]$Entity, [string[]]$ExcludeIds = @())
        $d = $script:Dev
        $d.CollateralCalls.Add([pscustomobject]@{ Entity = $Entity; ExcludeIds = @($ExcludeIds) })
        $rows = @($d.Collateral)
        if (@($d.Writes | Where-Object { $_ -like 'PATCH *' }).Count -gt 0) { $rows += @($d.CollateralAfterEdit) }
        return @($rows | Where-Object { $ExcludeIds -notcontains $_.Id } | ForEach-Object { $_.Text })
    }
    function Invoke-PublishXml {
        param([hashtable]$Context, [string]$ParameterXml)
        if ($ParameterXml -notmatch '^<importexportxml>.+</importexportxml>$') { throw 'ParameterXml must be an <importexportxml> document.' }
        $script:Dev.Writes.Add('POST PublishXml'); $script:Dev.Published.Add($ParameterXml)
    }

    function Test-DevMentions($Text, [string]$Column) { return ($Text -match "(?i)(?<![A-Za-z0-9])$Column(?![A-Za-z0-9])") }

    function Get-DevRelationship([string]$SchemaName) {
        $d = $script:Dev
        $col = @{ sprk_matter_ratecard_matter = 'sprk_matter'; sprk_project_ratecard_project = 'sprk_project'; sprk_workassignment_ratecard_workassignment = 'sprk_workassignment' }[$SchemaName]
        if (-not $col -or -not $d.Rel[$SchemaName]) { return $null }
        return [pscustomobject]@{ SchemaName = $SchemaName; MetadataId = "rel-$col"; ReferencingEntity = 'sprk_ratecard'; ReferencingAttribute = $col; ReferencedEntity = $col }
    }

    function Invoke-Dv {
        param([string]$Path, [string]$Method = 'GET', $Body = $null, [hashtable]$ExtraHeaders = @{}, [switch]$NoRetry)
        $d = $script:Dev
        if ($Method -ne 'GET') {
            $d.Writes.Add("$Method $Path")
            if ($Path -match '^systemforms\((?<id>.+)\)$') { ($d.Forms | Where-Object { $_.formid -eq $Matches.id }).formxml = $Body.formxml }
            elseif ($Path -match '^savedqueries\((?<id>.+)\)$') {
                $v = $d.Views | Where-Object { $_.savedqueryid -eq $Matches.id }
                $v.fetchxml = $Body.fetchxml
                if ($Body.ContainsKey('layoutxml')) { $v.layoutxml = $Body.layoutxml }
            }
            elseif ($Path -match '^RelationshipDefinitions\((?<id>.+)\)$') {
                $col = $Matches.id -replace '^rel-', ''
                foreach ($k in @($d.Rel.Keys)) { if ((Get-DevRelationship $k).MetadataId -eq $Matches.id) { $d.Rel[$k] = $false } }
                $d.Attr[$col] = $false
            }
            return $null
        }
        $d.Gets.Add($Path)
        # The caller's identity and rights (Get-ReadAllRowsGap). $d.Who: Admin, Depth ($null = holds no Read privilege), Fault (a message = every call throws).
        $w = $d.Who
        if ($Path -eq 'WhoAmI' -or $Path -match '^systemusers\(|^privileges\?') {
            if ($w.Fault) { throw $w.Fault }
            if ($Path -eq 'WhoAmI') { return [pscustomobject]@{ UserId = 'u-1' } }
            # The caller's roles are never asked any more (no role-name shortcut); the double still answers, so a regression to the shortcut shows:
            # RolesNoValue = an answer with no `value` property at all (a malformed one), Admin = a role named System Administrator.
            if ($Path -match 'systemuserroles_association') {
                if ($w.RolesNoValue) { return [pscustomobject]@{ note = 'no value property' } }
                return [pscustomobject]@{ value = @(if ($w.Admin) { [pscustomobject]@{ name = 'System Administrator' } }) }
            }
            if ($Path -match '^privileges\?') { return $(if ($w.PrivilegeAnswer) { $w.PrivilegeAnswer } else { [pscustomobject]@{ value = @([pscustomobject]@{ privilegeid = 'P-READ' }) } }) }
            if ($Path -match 'RetrieveUserPrivileges') {
                if ($w.HeldAnswer) { return $w.HeldAnswer }
                $held = @(if ($w.Depth) { [pscustomobject]@{ PrivilegeId = 'p-read'; Depth = $w.Depth } }; [pscustomobject]@{ PrivilegeId = 'p-other'; Depth = 'Global' })
                return [pscustomobject]@{ RolePrivileges = $held }
            }
        }
        if ($Path -match '^systemforms\?') { return [pscustomobject]@{ value = @($d.Forms | ForEach-Object { [pscustomobject]$_ }) } }
        if ($Path -match '^savedqueries\?') { return [pscustomobject]@{ value = @($d.Views | ForEach-Object { [pscustomobject]$_ }) } }
        if ($Path -match '^userqueries\?') { return [pscustomobject]@{ value = @($d.Personal | ForEach-Object { [pscustomobject]$_ }) } }
        if ($Path -match '^RetrieveDependenciesForDelete\(ObjectId=(?<id>[^,]+),ComponentType=(?<t>\d+)\)') {
            $deps = [System.Collections.Generic.List[object]]::new()
            if ([int]$Matches.t -eq 2) {
                $col = $Matches.id -replace '^attr-', ''
                foreach ($f in $d.Forms) { if (Test-DevMentions $f.formxml $col) { $deps.Add([pscustomobject]@{ dependentcomponenttype = 60; dependentcomponentobjectid = $f.formid }) } }
                foreach ($v in $d.Views) { if ((Test-DevMentions $v.fetchxml $col) -or (Test-DevMentions $v.layoutxml $col)) { $deps.Add([pscustomobject]@{ dependentcomponenttype = 26; dependentcomponentobjectid = $v.savedqueryid }) } }
                foreach ($x in $d.ExtraDeps) { $deps.Add([pscustomobject]$x) }
                if (@($d.Writes | Where-Object { $_ -like 'PATCH *' }).Count -gt 0) { foreach ($x in $d.ExtraDepsAfterEdit) { $deps.Add([pscustomobject]$x) } }
            }
            else { foreach ($x in $d.ExtraRelDeps) { $deps.Add([pscustomobject]$x) } }
            return [pscustomobject]@{ value = $deps.ToArray() }
        }
        return [pscustomobject]@{ value = @() }
    }
    function Get-DvOrNull {
        param([string]$Path, [int]$Retries = 0)
        $d = $script:Dev
        $d.Gets.Add($Path)
        if ($Path -match "^RelationshipDefinitions\(SchemaName='(?<n>[^']+)'\)") { return Get-DevRelationship $Matches.n }
        if ($Path -match '^sprk_gridconfigurations\?') { return [pscustomobject]@{ value = @($d.Grids | ForEach-Object { [pscustomobject]$_ }) } }
        return $null
    }
    function Get-Attr {
        param([string]$Table, [string]$Column, [string]$Select)
        $script:Dev.Gets.Add("attr $Table.$Column")
        if ($script:Dev.Attr[$Column]) { return [pscustomobject]@{ MetadataId = "attr-$Column"; AttributeType = $script:Dev.AttrType } }
        return $null
    }
    function Get-RowCountExact {
        param([string]$EntitySet, [string]$IdColumn, [string]$Filter = '')
        $script:Dev.Gets.Add("count $EntitySet $Filter")
        if ($Filter -match '^_(?<c>sprk_\w+?)_value ne null$') { return $script:Dev.NonNull[$Matches.c] }
        return 0
    }

    . ([scriptblock]::Create($pieces -join "`n"))

    # ---- Fixtures ----
    $script:FormXml = @'
<form><tabs><tab><columns><column><sections><section name="general"><rows>
<row><cell id="{1}"><labels><label description="Name" languagecode="1033" /></labels><control id="sprk_name" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" datafieldname="sprk_name" /></cell><cell id="{2}"><control id="sprk_firm" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="sprk_firm" /></cell></row>
<row><cell id="{3}"><control id="sprk_matter" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="sprk_matter" /></cell><cell id="{4}"><control id="sprk_project" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="sprk_project" /></cell></row>
<row><cell id="{5}"><control id="sprk_workassignment" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="sprk_workassignment" /></cell><cell id="{6}"><control id="sprk_sourcedocument" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="sprk_sourcedocument" /></cell></row>
<row><cell id="{7}"><control id="ownerid" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="ownerid" /></cell></row>
</rows></section></sections></column></columns></tab></tabs></form>
'@
    $script:FetchXml = '<fetch version="1.0" mapping="logical"><entity name="sprk_ratecard"><attribute name="sprk_ratecardid" /><attribute name="sprk_name" /><attribute name="sprk_firm" /><attribute name="sprk_matter" /><attribute name="sprk_project" /><attribute name="sprk_workassignment" /><attribute name="sprk_effectivefrom" /><order attribute="sprk_name" descending="false" /><filter type="and"><condition attribute="statecode" operator="eq" value="0" /></filter></entity></fetch>'
    $script:LayoutXml = '<grid name="resultset" jump="sprk_name" select="1" icon="1" preview="1"><row name="result" id="sprk_ratecardid"><cell name="sprk_name" width="220" /><cell name="sprk_firm" width="150" /><cell name="sprk_matter" width="150" /><cell name="sprk_project" width="150" /><cell name="sprk_workassignment" width="150" /><cell name="sprk_effectivefrom" width="150" /></row></grid>'

    function New-Dev {
        return @{
            Attr      = @{ sprk_matter = $true; sprk_project = $true; sprk_workassignment = $true }
            AttrType  = 'Lookup'
            Rel       = @{ sprk_matter_ratecard_matter = $true; sprk_project_ratecard_project = $true; sprk_workassignment_ratecard_workassignment = $true }
            NonNull   = @{ sprk_matter = 0; sprk_project = 0; sprk_workassignment = 0 }
            Forms     = @(@{ formid = 'f-1'; name = 'Information'; type = 2; formxml = $script:FormXml })
            Views     = @(@{ savedqueryid = 'v-1'; name = 'Active Rate Cards'; fetchxml = $script:FetchXml; layoutxml = $script:LayoutXml })
            Personal  = @()
            Grids     = @()
            ExtraDeps = @()
            ExtraRelDeps = @()
            Writes    = [System.Collections.Generic.List[string]]::new()
            Gets      = [System.Collections.Generic.List[string]]::new()
            Asked     = [System.Collections.Generic.List[string]]::new()
            Published = [System.Collections.Generic.List[string]]::new()
            Collateral = @()
            CollateralAfterEdit = @()
            CollateralCalls = [System.Collections.Generic.List[object]]::new()
            ContextUrls = [System.Collections.Generic.List[string]]::new()
            ExtraDepsAfterEdit = @()
            Who = @{ Admin = $true; Depth = 'Global'; Fault = $null }
        }
    }

    function New-StateObj([hashtable]$Over = @{}) {
        $cols = foreach ($t in $Targets) {
            [pscustomobject]@{
                Column = $t.Column; Relationship = $t.Relationship; Referenced = $t.Referenced
                AttributePresent = $true; AttributeType = 'Lookup'; AttributeId = "attr-$($t.Column)"; NonNull = 0; Dependencies = @()
                RelationshipPresent = $true; RelationshipId = "rel-$($t.Column)"; RelReferencing = 'sprk_ratecard'; RelReferencingAttribute = $t.Column; RelReferenced = $t.Referenced; RelationshipDependencies = @()
            }
        }
        $s = [pscustomobject]@{ Columns = @($cols); Forms = @(); Views = @(); PersonalViews = @(); GridConfigs = @(); ReadGap = $null }
        foreach ($k in $Over.Keys) { $s.$k = $Over[$k] }
        return $s
    }

    function Get-Refusal([scriptblock]$Action) {
        try { & $Action } catch { return $_.Exception }
        return $null
    }
}

Describe 'the targets (T1)' {
    It 'name exactly the three columns of sprk_ratecard and the one relationship each sits behind' {
        $Table | Should -Be 'sprk_ratecard'
        @($Targets | ForEach-Object { $_.Column }) | Should -Be @('sprk_matter', 'sprk_project', 'sprk_workassignment')
        @($Targets | ForEach-Object { $_.Relationship }) | Should -Be @('sprk_matter_ratecard_matter', 'sprk_project_ratecard_project', 'sprk_workassignment_ratecard_workassignment')
        foreach ($t in $Targets) { $t.Referenced | Should -Be $t.Column }
    }

    It 'never names sprk_firm, sprk_procedureversion, sprk_sourcedocument or a rule set relationship (they stay)' {
        $text = ($Targets | ForEach-Object { "$($_.Column) $($_.Relationship) $($_.Referenced)" }) -join ' '
        foreach ($stay in 'sprk_firm', 'sprk_procedureversion', 'sprk_sourcedocument', 'ruleset') { $text | Should -Not -Match $stay }
    }
}

Describe 'Test-MentionsCoreLookup (T1)' {
    It 'fires on the bare names, the PhysicalName spelling and the _value filter spelling' {
        Test-MentionsCoreLookup 'datafieldname="sprk_matter"' | Should -BeTrue
        Test-MentionsCoreLookup 'PhysicalName="sprk_Matter"' | Should -BeTrue
        Test-MentionsCoreLookup '_sprk_workassignment_value eq null' | Should -BeTrue
        Test-MentionsCoreLookup '<attribute name="sprk_project" />' | Should -BeTrue
    }
    It 'does not fire on longer names or other columns' {
        Test-MentionsCoreLookup 'sprk_matterid' | Should -BeFalse
        Test-MentionsCoreLookup 'sprk_projectstatus' | Should -BeFalse
        Test-MentionsCoreLookup 'sprk_procedureversion sprk_firm sprk_sourcedocument' | Should -BeFalse
        Test-MentionsCoreLookup '' | Should -BeFalse
        Test-MentionsCoreLookup $null | Should -BeFalse
    }
}

Describe 'ConvertTo-FormWithoutCoreLookups (T1)' {
    It 'removes the three controls and the row they leave empty, and keeps the rest' {
        $r = ConvertTo-FormWithoutCoreLookups $script:FormXml
        $r.Remaining | Should -Be 0
        $r.Removed | Should -Contain 'control sprk_matter'
        $r.Removed | Should -Contain 'control sprk_project'
        $r.Removed | Should -Contain 'control sprk_workassignment'
        $r.Removed | Should -Contain 'an emptied row'
        $doc = [xml]$r.Xml
        @($doc.SelectNodes('//control') | ForEach-Object { $_.GetAttribute('datafieldname') }) | Should -Be @('sprk_name', 'sprk_firm', 'sprk_sourcedocument', 'ownerid')
        $doc.SelectNodes('//row').Count | Should -Be 3
        foreach ($row in $doc.SelectNodes('//row')) { $row.SelectNodes('cell').Count | Should -BeGreaterThan 0 }
    }

    It 'leaves a form that names none of them unchanged in meaning (nothing removed)' {
        $clean = (ConvertTo-FormWithoutCoreLookups $script:FormXml).Xml
        $r = ConvertTo-FormWithoutCoreLookups $clean
        $r.Removed.Count | Should -Be 0
        $r.Remaining | Should -Be 0
    }

    It 'reports a mention it cannot remove (an event handler) so the caller refuses' {
        $withEvent = $script:FormXml -replace '</form>', '<events><event name="onchange" attribute="sprk_matter"><Handlers /></event></events></form>'
        $r = ConvertTo-FormWithoutCoreLookups $withEvent
        $r.Remaining | Should -BeGreaterThan 0
    }
}

Describe 'ConvertTo-ViewWithoutCoreLookups (T1)' {
    It 'removes the fetch attributes and the layout cells and keeps the rest' {
        $r = ConvertTo-ViewWithoutCoreLookups $script:FetchXml $script:LayoutXml
        $r.Remaining | Should -Be 0
        $r.Changes.Count | Should -Be 6
        @(([xml]$r.FetchXml).SelectNodes('//attribute') | ForEach-Object { $_.GetAttribute('name') }) | Should -Be @('sprk_ratecardid', 'sprk_name', 'sprk_firm', 'sprk_effectivefrom')
        @(([xml]$r.LayoutXml).SelectNodes('//cell') | ForEach-Object { $_.GetAttribute('name') }) | Should -Be @('sprk_name', 'sprk_firm', 'sprk_effectivefrom')
        $r.FetchXml | Should -Match 'statecode'
    }

    It 'leaves a filter or order on a column in place and reports it (removing it would change the rows the view returns)' {
        $filtered = $script:FetchXml -replace '</filter>', '<condition attribute="sprk_project" operator="null" /></filter>'
        $r = ConvertTo-ViewWithoutCoreLookups $filtered $script:LayoutXml
        $r.Remaining | Should -Be 1
        $r.FetchXml | Should -Match 'condition attribute="sprk_project"'
    }

    It 'accepts a view without layout xml' {
        $r = ConvertTo-ViewWithoutCoreLookups $script:FetchXml $null
        $r.Remaining | Should -Be 0
        $r.LayoutXml | Should -BeNullOrEmpty
    }
}

Describe 'Get-State (T2)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'reads a clean dev: three columns with zero values, their relationships, the form and the view that name them; and only GETs' {
        $s = Get-State
        @($s.Columns | Where-Object AttributePresent).Count | Should -Be 3
        @($s.Columns | Where-Object RelationshipPresent).Count | Should -Be 3
        @($s.Columns | ForEach-Object { $_.NonNull }) | Should -Be @(0, 0, 0)
        @($s.Forms).Count | Should -Be 1
        $s.Forms[0].Remaining | Should -Be 0
        @($s.Views).Count | Should -Be 1
        @($s.Columns[0].Dependencies | ForEach-Object { $_.ObjectId }) | Should -Be @('f-1', 'v-1')
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'counts rows of every state with a plain not-null filter on the lookup (no statecode filter)' {
        $null = Get-State
        $counts = @($script:Dev.Gets | Where-Object { $_ -like 'count *' })
        $counts.Count | Should -Be 3
        $counts[0] | Should -Be 'count sprk_ratecards _sprk_matter_value ne null'
        ($counts -join ' ') | Should -Not -Match 'statecode'
    }

    It 'reads a column that is already gone as not present and reads no rows for it' {
        $script:Dev.Attr['sprk_project'] = $false
        $script:Dev.Rel['sprk_project_ratecard_project'] = $false
        $s = Get-State
        ($s.Columns | Where-Object Column -eq 'sprk_project').AttributePresent | Should -BeFalse
        ($s.Columns | Where-Object Column -eq 'sprk_project').RelationshipPresent | Should -BeFalse
        @($script:Dev.Gets | Where-Object { $_ -like 'count *sprk_project*' }).Count | Should -Be 0
    }

    It 'keeps a failed count as unknown (null), never zero' {
        $script:Dev.NonNull['sprk_matter'] = $null
        (Get-State).Columns[0].NonNull | Should -BeNullOrEmpty
    }

    It 'names a personal view and a grid configuration that mention a column' {
        $script:Dev.Personal = @(@{ name = 'My cards'; userqueryid = 'u-1'; fetchxml = $script:FetchXml; layoutxml = $null })
        $script:Dev.Grids = @(@{ sprk_name = 'Cards grid'; sprk_configjson = '{"columns":["sprk_matter"]}' }, @{ sprk_name = 'Other grid'; sprk_configjson = '{"columns":["sprk_firm"]}' })
        $s = Get-State
        @($s.PersonalViews | ForEach-Object { $_.Name }) | Should -Be @('My cards')
        @($s.GridConfigs | ForEach-Object { $_.Name }) | Should -Be @('Cards grid')
    }
}

Describe 'the caller must be shown to read every sprk_ratecard row before any count is believed (review pass 1, F1)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'Get-ReadAllRowsGap: organization (Global) Read reads every row, whatever case the privilege id comes in (a System Administrator holds it)' {
        $script:Dev.Who.Depth = 'Global'
        Get-ReadAllRowsGap 'sprk_ratecard' | Should -BeNullOrEmpty
    }

    It 'Get-ReadAllRowsGap: there is no role-name shortcut. A role named System Administrator without organization Read is a gap, and roles are never asked (fix round 2)' {
        $script:Dev.Who.Admin = $true; $script:Dev.Who.Depth = 'Local'
        Get-ReadAllRowsGap 'sprk_ratecard' | Should -BeLike "*Read depth on sprk_ratecard is 'Local'*"
        @($script:Dev.Gets | Where-Object { $_ -like '*systemuserroles_association*' }).Count | Should -Be 0
    }

    It 'Get-ReadAllRowsGap: a malformed role answer (no value property) can never pass a caller below organization depth (fix round 2)' {
        $script:Dev.Who.Admin = $false; $script:Dev.Who.RolesNoValue = $true; $script:Dev.Who.Depth = 'Local'
        $gap = Get-ReadAllRowsGap 'sprk_ratecard'
        $gap | Should -BeLike 'cannot confirm every sprk_ratecard row can be read*'
    }

    It 'Get-ReadAllRowsGap: a privilege lookup with no value list, a null value list, or a null entry in it is a gap, never a pass (fix round 2)' -ForEach @(
        @{ Answer = [pscustomobject]@{ note = 'no value property' }; Expect = '*malformed*' }
        @{ Answer = [pscustomobject]@{ value = $null };              Expect = '*Read privilege of sprk_ratecard was not found*' }
        @{ Answer = [pscustomobject]@{ value = @($null) };           Expect = '*Read privilege of sprk_ratecard was not found*' }
    ) {
        $script:Dev.Who.PrivilegeAnswer = $Answer
        Get-ReadAllRowsGap 'sprk_ratecard' | Should -BeLike $Expect
    }

    It 'Get-ReadAllRowsGap: a caller privilege list with no RolePrivileges is a gap, not "none held" and not a pass (fix round 2)' {
        $script:Dev.Who.HeldAnswer = [pscustomobject]@{ note = 'no RolePrivileges property' }
        Get-ReadAllRowsGap 'sprk_ratecard' | Should -BeLike '*RolePrivileges*malformed*'
    }

    It 'Get-ReadAllRowsGap: a null entry among the caller privileges is skipped, the Global grant after it still counts' {
        $script:Dev.Who.HeldAnswer = [pscustomobject]@{ RolePrivileges = @($null, [pscustomobject]@{ PrivilegeId = 'P-READ'; Depth = 'Global' }) }
        Get-ReadAllRowsGap 'sprk_ratecard' | Should -BeNullOrEmpty
    }

    It 'Get-ReadAllRowsGap: -Wording replaces only the clause after "cannot confirm"' {
        $script:Dev.Who.Depth = 'Basic'
        Get-ReadAllRowsGap 'sprk_ratecard' 'every version was read' | Should -BeLike "cannot confirm every version was read: the caller's Read depth on sprk_ratecard is 'Basic'*"
    }

    It 'Get-ReadAllRowsGap: a non-admin below organization depth is a gap naming the depth (<Depth>)' -ForEach @(
        @{ Depth = 'Deep' }, @{ Depth = 'Local' }, @{ Depth = 'Basic' }
    ) {
        $script:Dev.Who.Admin = $false; $script:Dev.Who.Depth = $Depth
        $gap = Get-ReadAllRowsGap 'sprk_ratecard'
        $gap | Should -BeLike "*Read depth on sprk_ratecard is '$Depth'*"
    }

    It 'Get-ReadAllRowsGap: a non-admin who holds no Read privilege on the table is a gap' {
        $script:Dev.Who.Admin = $false; $script:Dev.Who.Depth = $null
        Get-ReadAllRowsGap 'sprk_ratecard' | Should -BeLike "*'none found'*"
    }

    It 'Get-ReadAllRowsGap: a fault in the check is a gap, never a pass' {
        $script:Dev.Who.Fault = '403 Forbidden'
        $gap = Get-ReadAllRowsGap 'sprk_ratecard'
        $gap | Should -BeLike '*the check failed (403 Forbidden)*'
    }

    It 'Get-State counts nothing when the caller cannot be shown to read every row: NonNull stays unknown and the gap is carried' {
        $script:Dev.Who.Admin = $false; $script:Dev.Who.Depth = 'Local'
        $s = Get-State
        $s.ReadGap | Should -BeLike '*Local*'
        @($s.Columns | ForEach-Object { $_.NonNull } | Where-Object { $null -ne $_ }).Count | Should -Be 0
        @($script:Dev.Gets | Where-Object { $_ -like 'count *' }).Count | Should -Be 0
    }

    It 'Get-State counts as before for an admin, and asks the check once, not once per column' {
        $s = Get-State
        $s.ReadGap | Should -BeNullOrEmpty
        @($script:Dev.Gets | Where-Object { $_ -like 'count *' }).Count | Should -Be 3
        @($script:Dev.Gets | Where-Object { $_ -eq 'WhoAmI' }).Count | Should -Be 1
    }

    It 'Get-State does not check read rights when every column is already gone (nothing to count)' {
        foreach ($k in @($script:Dev.Attr.Keys)) { $script:Dev.Attr[$k] = $false }
        $script:Dev.Who.Fault = 'would fail'
        (Get-State).ReadGap | Should -BeNullOrEmpty
        @($script:Dev.Gets | Where-Object { $_ -eq 'WhoAmI' }).Count | Should -Be 0
    }

    It 'Get-ApplyBlockers: the gap is one blocker, and the per-column "could not be counted" noise is not repeated' {
        $s = New-StateObj @{ ReadGap = 'cannot confirm every sprk_ratecard row can be read: test' }
        foreach ($c in $s.Columns) { $c.NonNull = $null }
        $b = @(Get-ApplyBlockers $s)
        $b.Count | Should -Be 1
        $b[0] | Should -BeLike 'sprk_ratecard: cannot confirm every sprk_ratecard row can be read*'
    }

    It '-Apply refuses (exit-2 exception, no count, no write) for a caller below organization depth' {
        $script:Dev.Who.Admin = $false; $script:Dev.Who.Depth = 'Deep'
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Be 'EscalationException'
        $e.Message | Should -BeLike '*cannot confirm every sprk_ratecard row can be read*'
        $e.Message | Should -BeLike '*Nothing was changed*'
        $script:Dev.Writes.Count | Should -Be 0
        @($script:Dev.Gets | Where-Object { $_ -like 'count *' }).Count | Should -Be 0
    }

    It '-Apply refuses when the rights check itself faults (a failed check refuses)' {
        $script:Dev.Who.Fault = 'timeout'
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e.GetType().Name | Should -Be 'EscalationException'
        $e.Message | Should -BeLike '*the check failed (timeout)*'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It '-Apply proceeds for a caller whose Read is organization-wide, whatever role names it holds' {
        Invoke-Retirement $true $false $false 'owner OK'
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }).Count | Should -Be 3
        $script:Dev = New-Dev; $script:Dev.Who.Admin = $false; $script:Dev.Who.Depth = 'Global'
        Invoke-Retirement $true $false $false 'owner OK'
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }).Count | Should -Be 3
    }

    It 'the report mode shows the gap and does not throw or write' {
        $script:Dev.Who.Admin = $false; $script:Dev.Who.Depth = 'Local'
        { Invoke-Retirement $false $false $false '' } | Should -Not -Throw
        $script:Dev.Writes.Count | Should -Be 0
    }
}

Describe 'Get-ApplyBlockers (T3)' {
    It 'is empty for the clean state with the form and view this script cleans as the only dependents' {
        $s = New-StateObj @{
            Forms = @([pscustomobject]@{ Id = 'f-1'; Name = 'Information'; Remaining = 0 })
            Views = @([pscustomobject]@{ Id = 'v-1'; Name = 'Active Rate Cards'; Remaining = 0 })
        }
        foreach ($c in $s.Columns) { $c.Dependencies = @([pscustomobject]@{ Kind = 'system form'; ObjectId = 'f-1' }, [pscustomobject]@{ Kind = 'view'; ObjectId = 'v-1' }) }
        @(Get-ApplyBlockers $s).Count | Should -Be 0
    }

    It 'blocks on a rate card that holds a value, naming the column and the count, for each column' -ForEach @(
        @{ Index = 0; Column = 'sprk_matter' }, @{ Index = 1; Column = 'sprk_project' }, @{ Index = 2; Column = 'sprk_workassignment' }
    ) {
        $s = New-StateObj
        $s.Columns[$Index].NonNull = 3
        $b = @(Get-ApplyBlockers $s)
        $b.Count | Should -Be 1
        $b[0] | Should -Be "3 sprk_ratecard row(s) hold a value in $Column"
    }

    It 'blocks when the rows holding a value could not be counted' {
        $s = New-StateObj; $s.Columns[1].NonNull = $null
        @(Get-ApplyBlockers $s).Count | Should -Be 1
    }

    It 'blocks on a dependent that this script does not clean, on the attribute and on the relationship' {
        $s = New-StateObj
        $s.Columns[0].Dependencies = @([pscustomobject]@{ Kind = 'chart'; ObjectId = 'c-1' })
        $s.Columns[2].RelationshipDependencies = @([pscustomobject]@{ Kind = 'system form'; ObjectId = 'f-core' })
        $b = @(Get-ApplyBlockers $s)
        $b.Count | Should -Be 2
        ($b -join '|') | Should -Match 'sprk_matter: dependent chart c-1'
        ($b -join '|') | Should -Match 'relationship sprk_workassignment_ratecard_workassignment: dependent system form f-core'
    }

    It 'blocks on a relationship that is not exactly sprk_ratecard.(column) to the same-named core table, or is missing' {
        $s = New-StateObj; $s.Columns[0].RelReferencing = 'sprk_ruleset'
        @(Get-ApplyBlockers $s).Count | Should -Be 1
        $s = New-StateObj; $s.Columns[1].RelReferenced = 'sprk_matter'
        @(Get-ApplyBlockers $s).Count | Should -Be 1
        $s = New-StateObj; $s.Columns[2].RelationshipPresent = $false
        @(Get-ApplyBlockers $s).Count | Should -Be 1
    }

    It 'blocks on a column that is not a lookup, and on a relationship left without its column' {
        $s = New-StateObj; $s.Columns[0].AttributeType = 'String'
        @(Get-ApplyBlockers $s).Count | Should -Be 1
        $s = New-StateObj; $s.Columns[0].AttributePresent = $false
        @(Get-ApplyBlockers $s).Count | Should -Be 1
    }

    It 'blocks on a form or view that would still name a column, a personal view and a grid configuration, each named' {
        $s = New-StateObj @{
            Forms = @([pscustomobject]@{ Id = 'f-1'; Name = 'Information'; Remaining = 1 })
            Views = @([pscustomobject]@{ Id = 'v-1'; Name = 'Active Rate Cards'; Remaining = 2 })
            PersonalViews = @([pscustomobject]@{ Name = 'Mine' })
            GridConfigs = @([pscustomobject]@{ Name = 'Cards grid' })
        }
        $b = @(Get-ApplyBlockers $s)
        $b.Count | Should -Be 4
        ($b -join '|') | Should -Match "form 'Information'"
        ($b -join '|') | Should -Match "view 'Active Rate Cards'"
        ($b -join '|') | Should -Match "personal view 'Mine'"
        ($b -join '|') | Should -Match "grid configuration 'Cards grid'"
    }

    It 'is empty for a state that is already retired' {
        $s = New-StateObj
        foreach ($c in $s.Columns) { $c.AttributePresent = $false; $c.RelationshipPresent = $false }
        @(Get-ApplyBlockers $s).Count | Should -Be 0
        Test-AlreadyRetired $s | Should -BeTrue
    }
}

Describe 'Test-AlreadyRetired and Get-VerifyProblems (T5)' {
    BeforeEach { $script:Retired = New-StateObj; foreach ($c in $script:Retired.Columns) { $c.AttributePresent = $false; $c.RelationshipPresent = $false } }

    It 'a retired state has no verify problems' {
        @(Get-VerifyProblems $script:Retired).Count | Should -Be 0
        Test-AlreadyRetired $script:Retired | Should -BeTrue
    }

    It 'verify fails while an attribute remains' {
        $script:Retired.Columns[1].AttributePresent = $true
        $p = @(Get-VerifyProblems $script:Retired)
        $p.Count | Should -Be 1
        $p[0] | Should -Be 'sprk_ratecard.sprk_project still exists'
        Test-AlreadyRetired $script:Retired | Should -BeFalse
    }

    It 'verify fails while a relationship remains' {
        $script:Retired.Columns[2].RelationshipPresent = $true
        @(Get-VerifyProblems $script:Retired) | Should -Be @('relationship sprk_workassignment_ratecard_workassignment still exists')
    }

    It 'verify fails while a form or a view still names a column' {
        $script:Retired.Forms = @([pscustomobject]@{ Name = 'Information' })
        $script:Retired.Views = @([pscustomobject]@{ Name = 'Active Rate Cards' })
        @(Get-VerifyProblems $script:Retired).Count | Should -Be 2
        Test-AlreadyRetired $script:Retired | Should -BeFalse
    }
}

Describe 'Invoke-Retirement (T3, T4, T5)' {
    BeforeEach { $script:Dev = New-Dev; $script:Allow = $true }

    It 'with neither -Apply nor -Verify it only reports: no write, no publish' {
        Invoke-Retirement $false $false $false ''
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 0
    }

    It '-Apply -WhatIf writes nothing: every write is asked for and refused, and nothing is verified' {
        $script:Allow = $false
        Invoke-Retirement $true $false $true ''
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -BeGreaterThan 5
        $script:Dev.Attr.Values | Where-Object { -not $_ } | Should -BeNullOrEmpty
        $script:Dev.Rel.Values | Where-Object { -not $_ } | Should -BeNullOrEmpty
    }

    It 'refuses a rate card that holds a value in <Column>: exit-2 exception, no write at all' -ForEach @(
        @{ Column = 'sprk_matter' }, @{ Column = 'sprk_project' }, @{ Column = 'sprk_workassignment' }
    ) {
        $script:Dev.NonNull[$Column] = 2
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Be 'EscalationException'
        $e.Message | Should -BeLike "*2 sprk_ratecard row(s) hold a value in $Column*"
        $e.Message | Should -BeLike '*Nothing was changed*'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses when the count of rate cards holding a value cannot be had' {
        $script:Dev.NonNull['sprk_project'] = $null
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e.GetType().Name | Should -Be 'EscalationException'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses a dependent that is not the form or view it cleans (a chart), before any write' {
        $script:Dev.ExtraDeps = @(@{ dependentcomponenttype = 59; dependentcomponentobjectid = 'chart-1' })
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e.GetType().Name | Should -Be 'EscalationException'
        $e.Message | Should -BeLike '*dependent chart chart-1*'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses a personal view that names a column, before any write' {
        $script:Dev.Personal = @(@{ name = 'Mine'; userqueryid = 'u-1'; fetchxml = $script:FetchXml; layoutxml = $null })
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e.GetType().Name | Should -Be 'EscalationException'
        $e.Message | Should -BeLike "*personal view 'Mine'*"
        $script:Dev.Writes.Count | Should -Be 0
    }

    It 'refuses a view whose filter names a column, before any write' {
        $script:Dev.Views[0].fetchxml = $script:FetchXml -replace '</filter>', '<condition attribute="sprk_matter" operator="null" /></filter>'
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e.GetType().Name | Should -Be 'EscalationException'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It '-Apply retires: edits the form and view, publishes sprk_ratecard, deletes the three relationships, publishes sprk_ratecard again, verifies' {
        Invoke-Retirement $true $false $false 'owner OK'
        $w = @($script:Dev.Writes)
        $w | Should -Be @(
            'PATCH systemforms(f-1)'
            'PATCH savedqueries(v-1)'
            'POST PublishXml'
            'DELETE RelationshipDefinitions(rel-sprk_matter)'
            'DELETE RelationshipDefinitions(rel-sprk_project)'
            'DELETE RelationshipDefinitions(rel-sprk_workassignment)'
            'POST PublishXml'
        )
        $script:Dev.Published.Count | Should -Be 2
        $script:Dev.Published[0] | Should -Be '<importexportxml><entities><entity>sprk_ratecard</entity></entities></importexportxml>'
        $script:Dev.Published[1] | Should -Be '<importexportxml><entities><entity>sprk_ratecard</entity></entities></importexportxml>'
        (Test-MentionsCoreLookup $script:Dev.Forms[0].formxml) | Should -BeFalse
        (Test-MentionsCoreLookup $script:Dev.Views[0].fetchxml) | Should -BeFalse
        (Test-MentionsCoreLookup $script:Dev.Views[0].layoutxml) | Should -BeFalse
        $script:Dev.Attr.Values | Where-Object { $_ } | Should -BeNullOrEmpty
        $script:Dev.Rel.Values | Where-Object { $_ } | Should -BeNullOrEmpty
    }

    It 'never publishes sprk_matter, sprk_project or sprk_workassignment: deleting a relationship needs no publish of the core tables (F1)' {
        Invoke-Retirement $true $false $false 'owner OK'
        $script:Dev.Published.Count | Should -BeGreaterThan 0
        foreach ($body in $script:Dev.Published) {
            $body | Should -Not -Match 'sprk_matter|sprk_project|sprk_workassignment'
            @([regex]::Matches($body, '<entity>([^<]+)</entity>') | ForEach-Object { $_.Groups[1].Value }) | Should -Be @('sprk_ratecard')
        }
        # and the shared procedure is the only publish path: its collateral read ran for sprk_ratecard only
        foreach ($call in $script:Dev.CollateralCalls) { $call.Entity | Should -Be 'sprk_ratecard' }
    }

    It 'checks pending collateral through the shared procedure before the first write, excluding exactly the form and view it edits (F1)' {
        $script:Dev.Collateral = @(@{ Id = 'f-1'; Text = 'systemforms: Information (f-1)' }, @{ Id = 'v-1'; Text = 'savedqueries: Active Rate Cards (v-1)' })
        Invoke-Retirement $true $false $false 'owner OK'
        $script:Dev.CollateralCalls.Count | Should -BeGreaterThan 0
        $first = $script:Dev.CollateralCalls[0]
        @($first.ExcludeIds | Sort-Object) | Should -Be @('f-1', 'v-1')
        $script:Dev.Writes[0] | Should -Be 'PATCH systemforms(f-1)'
        $script:Dev.Writes.Count | Should -Be 7
    }

    It 'refuses when another pending form or view of sprk_ratecard would go live with the publish: exit-2 exception, nothing written or published (F1)' {
        $script:Dev.Collateral = @(@{ Id = 'f-1'; Text = 'systemforms: Information (f-1)' }, @{ Id = 'v-other'; Text = 'savedqueries: Someone Else''s View (v-other)' })
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Be 'EscalationException'
        $e.Message | Should -BeLike "*Someone Else's View (v-other)*"
        $e.Message | Should -BeLike '*-AllowPendingCollateral*'
        $e.Message | Should -BeLike '*Nothing was changed*'
        $e.Message | Should -Not -BeLike '*Information (f-1)*'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
    }

    It 'with -AllowPendingCollateral the same pending item does not stop the run (F1)' {
        $script:Dev.Collateral = @(@{ Id = 'v-other'; Text = 'savedqueries: Someone Else''s View (v-other)' })
        Invoke-Retirement $true $false $false 'owner OK' $true
        $script:Dev.Published.Count | Should -Be 2
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }).Count | Should -Be 3
    }

    It 'a pending item that shows up after the edit stops the publish with a plain error (exit 1, partly applied), no relationship deleted (F1)' {
        $script:Dev.CollateralAfterEdit = @(@{ Id = 'v-late'; Text = 'savedqueries: Late View (v-late)' })
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Not -Be 'EscalationException'
        $e.Message | Should -BeLike '*Late View (v-late)*'
        $e.Message | Should -BeLike '*partly applied*'
        $script:Dev.Published.Count | Should -Be 0
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }).Count | Should -Be 0
    }

    It '-Apply -WhatIf reads the collateral (read only), refuses the same way, and publishes nothing (F1)' {
        $script:Allow = $false
        $script:Dev.Collateral = @(@{ Id = 'v-other'; Text = 'savedqueries: Someone Else''s View (v-other)' })
        $e = Get-Refusal { Invoke-Retirement $true $false $true '' }
        $e.GetType().Name | Should -Be 'EscalationException'
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Published.Count | Should -Be 0
    }

    It 'a dependent that appears after the form and view edit stops the run with a plain error (exit 1, partly applied), not exit 2 (K1)' {
        $script:Dev.ExtraDepsAfterEdit = @(@{ dependentcomponenttype = 59; dependentcomponentobjectid = 'chart-late' })
        $e = Get-Refusal { Invoke-Retirement $true $false $false 'owner OK' }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Not -Be 'EscalationException'
        $e.Message | Should -BeLike '*chart chart-late*'
        $e.Message | Should -BeLike '*partly applied*'
        $e.Message | Should -BeLike '*No relationship was deleted*'
        @($script:Dev.Writes | Where-Object { $_ -like 'PATCH *' }).Count | Should -Be 2
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }).Count | Should -Be 0
    }

    It 'does not touch any other table: the only deletes are the three named relationships' {
        Invoke-Retirement $true $false $false 'owner OK'
        $deletes = @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' })
        $deletes.Count | Should -Be 3
        foreach ($d in $deletes) { $d | Should -Match '^DELETE RelationshipDefinitions\(rel-sprk_(matter|project|workassignment)\)$' }
    }

    It 'a second -Apply after success does nothing and passes' {
        Invoke-Retirement $true $false $false 'owner OK'
        $script:Dev.Writes.Clear(); $script:Dev.Asked.Clear()
        { Invoke-Retirement $true $false $false 'owner OK' } | Should -Not -Throw
        $script:Dev.Writes.Count | Should -Be 0
        $script:Dev.Asked.Count | Should -Be 0
    }

    It 'continues a run that stopped after the form and view edit (nothing left on the form, columns still there)' {
        $script:Dev.Forms[0].formxml = (ConvertTo-FormWithoutCoreLookups $script:FormXml).Xml
        $r = ConvertTo-ViewWithoutCoreLookups $script:FetchXml $script:LayoutXml
        $script:Dev.Views[0].fetchxml = $r.FetchXml; $script:Dev.Views[0].layoutxml = $r.LayoutXml
        Invoke-Retirement $true $false $false 'owner OK'
        @($script:Dev.Writes | Where-Object { $_ -like 'PATCH *' }).Count | Should -Be 0
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }).Count | Should -Be 3
        $script:Dev.Attr.Values | Where-Object { $_ } | Should -BeNullOrEmpty
    }

    It 'skips a relationship that is already gone and deletes the rest' {
        $script:Dev.Rel['sprk_project_ratecard_project'] = $false
        $script:Dev.Attr['sprk_project'] = $false
        Invoke-Retirement $true $false $false 'owner OK'
        @($script:Dev.Writes | Where-Object { $_ -like 'DELETE *' }) | Should -Be @('DELETE RelationshipDefinitions(rel-sprk_matter)', 'DELETE RelationshipDefinitions(rel-sprk_workassignment)')
    }

    It '-Verify on a retired environment passes and writes nothing' {
        Invoke-Retirement $true $false $false 'owner OK'
        $script:Dev.Writes.Clear()
        { Invoke-Retirement $false $true $false '' } | Should -Not -Throw
        $script:Dev.Writes.Count | Should -Be 0
    }

    It '-Verify fails (a plain error, exit 1) while an attribute remains' {
        $e = Get-Refusal { Invoke-Retirement $false $true $false '' }
        $e | Should -Not -BeNullOrEmpty
        $e.GetType().Name | Should -Not -Be 'EscalationException'
        $e.Message | Should -BeLike 'Verification failed:*'
        $e.Message | Should -BeLike '*sprk_ratecard.sprk_matter still exists*'
        $script:Dev.Writes.Count | Should -Be 0
    }

    It '-Verify fails while only a form still names a column (the columns are gone)' {
        foreach ($k in @($script:Dev.Attr.Keys)) { $script:Dev.Attr[$k] = $false }
        foreach ($k in @($script:Dev.Rel.Keys)) { $script:Dev.Rel[$k] = $false }
        $script:Dev.Views[0].fetchxml = '<fetch><entity name="sprk_ratecard"><attribute name="sprk_name" /></entity></fetch>'
        $script:Dev.Views[0].layoutxml = '<grid><row><cell name="sprk_name" /></row></grid>'
        $e = Get-Refusal { Invoke-Retirement $false $true $false '' }
        $e.Message | Should -BeLike "*form 'Information' still names a core-record lookup*"
    }
}

Describe 'the pre-flight (T6)' {
    It 'asks nothing when -Apply is not given' {
        @(Get-PreflightRefusals $false $false '' '' '' 'https://other.crm.dynamics.com').Count | Should -Be 0
    }

    It 'refuses -Apply without the owner approval (blank, whitespace or missing)' {
        foreach ($a in @('', '   ', $null)) {
            $r = @(Get-PreflightRefusals $true $false $a 'abc1234' 'abc1234' 'https://spaarkedev1.crm.dynamics.com')
            $r.Count | Should -Be 1
            $r[0] | Should -BeLike '*-OwnerApproval*'
        }
    }

    It 'does not need the owner approval for -Apply -WhatIf, but still needs both attestations' {
        $r = @(Get-PreflightRefusals $true $true '' '' '' 'https://spaarkedev1.crm.dynamics.com')
        $r.Count | Should -Be 2
        ($r -join '|') | Should -Match 'BffDeployAttestation'
        ($r -join '|') | Should -Match 'SpaDeployAttestation'
        ($r -join '|') | Should -Not -Match 'OwnerApproval'
    }

    It 'refuses an environment other than dev, and names every reason at once' {
        $r = @(Get-PreflightRefusals $true $false '' '' '' 'https://acme.crm.dynamics.com')
        $r.Count | Should -Be 4
        ($r -join '|') | Should -Match 'dev only'
    }
}

Describe 'the attestation scan (T6): the deployed source must not select the three rate-card columns' {
    BeforeAll {
        $script:Repo = Join-Path $TestDrive 'attest-repo'
        New-Item -ItemType Directory -Path $script:Repo | Out-Null
        function Invoke-TestGit { & git -C $script:Repo -c user.name=t -c user.email=t@example.invalid -c commit.gpgsign=false -c core.autocrlf=false @args 2>&1 | Out-Null; if ($LASTEXITCODE -ne 0) { throw "git $args failed" } }
        function Write-RepoFile([string]$RelativePath, [string]$Text) {
            $p = Join-Path $script:Repo $RelativePath
            New-Item -ItemType Directory -Path (Split-Path $p) -Force | Out-Null
            Set-Content -LiteralPath $p -Value $Text -NoNewline
        }
        function New-Commit([hashtable]$Files) {
            Get-ChildItem -LiteralPath $script:Repo -Exclude '.git' | Remove-Item -Recurse -Force
            foreach ($k in $Files.Keys) { Write-RepoFile $k $Files[$k] }
            Invoke-TestGit add -A
            Invoke-TestGit commit -q --allow-empty -m 'fixture'
            return (& git -C $script:Repo rev-parse HEAD).Trim()
        }
        Invoke-TestGit init -q
        $script:Wr = @{ 'src/dataverse/webresources/a.js' = 'var a = 1;'; 'src/dataverse/solutions/SpaarkeMaster/WebResources/b.js' = 'var b = 2;' }
        $script:Spa = @{ 'src/client/x.ts' = 'export const a = 1;'; 'src/solutions/y.ts' = 'export const b = 2;' } + $script:Wr

        # A BFF whose code touches the table and the matter table in different files, as the real one does: must not fire.
        $script:Clean = New-Commit ($script:Spa + @{
            'src/server/RateCardImport.cs'  = 'internal const string RateCardEntity = "sprk_ratecard"; var c = new Entity("sprk_ratecardrate"); c["sprk_firm"] = f; c["sprk_procedureversion"] = v;'
            'src/server/MatterReader.cs'    = 'var q = new QueryExpression("sprk_matter"); q.ColumnSet = new ColumnSet("sprk_project", "sprk_workassignment");'
            'src/server/RateRows.cs'        = 'var r = "sprk_ratecardrate"; var m = "sprk_matterid"; var link = "sprk_ratecard_ratecardrate_ratecard";'
        })
        $script:Reads = New-Commit ($script:Spa + @{
            'src/server/RateCardImport.cs'  = 'internal const string RateCardEntity = "sprk_ratecard"; var cols = new ColumnSet("sprk_name", "sprk_matter");'
        })
        $script:ReadsViaConstant = New-Commit ($script:Spa + @{
            'src/server/Other.cs'           = 'var q = new QueryExpression(InvoiceResolutionService.RateCardEntity); q.Criteria.AddCondition("_sprk_project_value", ConditionOperator.Null);'
        })
        $script:ReadsRelationship = New-Commit ($script:Spa + @{
            'src/server/Nav.cs'             = 'var rel = "sprk_workassignment_ratecard_workassignment";'
            'src/server/Ok.cs'              = 'var x = 1;'
        })
        $script:SpaReads = New-Commit (@{
            'src/server/Ok.cs'             = 'var x = 1;'
            'src/client/RateCards.tsx'      = 'fetch("/sprk_ratecards?$select=sprk_name,_sprk_matter_value");'
            'src/solutions/y.ts'            = 'export const b = 2;'
        } + $script:Wr)
        $script:WebResourceReads = New-Commit ($script:Spa + @{
            'src/dataverse/webresources/sprk_RateCardForm.js' = 'Xrm.WebApi.retrieveMultipleRecords("sprk_ratecard", "?$select=sprk_name,_sprk_project_value");'
        })
        $script:NoSolutions = New-Commit @{ 'src/server/Ok.cs' = 'var x = 1;'; 'src/client/x.ts' = 'export const a = 1;' }
    }

    It 'passes a source where no file names the rate card table together with one of the three columns (must not fire)' {
        $RepoRoot = $script:Repo
        @(Get-ReaderProblems $script:Clean $BffScanPaths '-BffDeployAttestation').Count | Should -Be 0
        @(Get-ReaderProblems $script:Clean $SpaScanPaths '-SpaDeployAttestation').Count | Should -Be 0
    }

    It 'refuses a file that names the rate card table and sprk_matter (must fire), naming the file' {
        $RepoRoot = $script:Repo
        $p = @(Get-ReaderProblems $script:Reads $BffScanPaths '-BffDeployAttestation')
        $p.Count | Should -Be 1
        $p[0] | Should -BeLike '*src/server/RateCardImport.cs*'
        $p[0] | Should -BeLike '*name the rate card table*'
    }

    It 'refuses a reader that reaches the table through the RateCardEntity constant and the _value spelling (must fire)' {
        $RepoRoot = $script:Repo
        $p = @(Get-ReaderProblems $script:ReadsViaConstant $BffScanPaths '-BffDeployAttestation')
        $p.Count | Should -Be 1
        $p[0] | Should -BeLike '*src/server/Other.cs*'
    }

    It 'refuses a file that names one of the three relationships (must fire)' {
        $RepoRoot = $script:Repo
        $p = @(Get-ReaderProblems $script:ReadsRelationship $BffScanPaths '-BffDeployAttestation')
        $p.Count | Should -Be 1
        $p[0] | Should -BeLike '*relationships*src/server/Nav.cs*'
    }

    It 'applies the SPA attestation to src/client and src/solutions, not to src/server (must fire there, must not fire on the BFF paths)' {
        $RepoRoot = $script:Repo
        @(Get-ReaderProblems $script:SpaReads $SpaScanPaths '-SpaDeployAttestation').Count | Should -Be 1
        @(Get-ReaderProblems $script:SpaReads $BffScanPaths '-BffDeployAttestation').Count | Should -Be 0
    }

    It 'scans the web resources under src/dataverse too: a form script that selects the table and a column is refused (K2), a clean one is not' {
        $RepoRoot = $script:Repo
        $p = @(Get-ReaderProblems $script:WebResourceReads $SpaScanPaths '-SpaDeployAttestation')
        $p.Count | Should -Be 1
        $p[0] | Should -BeLike '*src/dataverse/webresources/sprk_RateCardForm.js*'
        @(Get-ReaderProblems $script:Clean $SpaScanPaths '-SpaDeployAttestation').Count | Should -Be 0
        $SpaScanPaths | Should -Contain 'src/dataverse/webresources'
        $SpaScanPaths | Should -Contain 'src/dataverse/solutions/SpaarkeMaster/WebResources'
    }

    It 'refuses a path that does not exist at the commit, so the scan cannot pass vacuously' {
        $RepoRoot = $script:Repo
        $p = @(Get-ReaderProblems $script:NoSolutions $SpaScanPaths '-SpaDeployAttestation')
        $p.Count | Should -Be 3
        ($p -join '|') | Should -BeLike '*src/solutions does not exist at that commit*'
        ($p -join '|') | Should -BeLike '*src/dataverse/webresources does not exist at that commit*'
        ($p -join '|') | Should -BeLike '*SpaarkeMaster/WebResources does not exist at that commit*'
    }

    It 'refuses an unknown sha and a malformed one' {
        $RepoRoot = $script:Repo
        @(Get-ReaderProblems ('0' * 40) $BffScanPaths '-BffDeployAttestation')[0] | Should -BeLike '*is not a commit in this repository*'
        @(Get-ReaderProblems 'not-a-sha' $BffScanPaths '-BffDeployAttestation')[0] | Should -BeLike '*is not a commit sha*'
    }

    It 'a pre-flight with the dirty commit attested refuses before any Dataverse call' {
        $RepoRoot = $script:Repo
        $r = @(Get-PreflightRefusals $true $false 'owner OK' $script:Reads $script:Clean 'https://spaarkedev1.crm.dynamics.com')
        $r.Count | Should -Be 1
        $r[0] | Should -BeLike '-BffDeployAttestation*'
        @(Get-PreflightRefusals $true $false 'owner OK' $script:Clean $script:Clean 'https://spaarkedev1.crm.dynamics.com').Count | Should -Be 0
    }
}

Describe 'structure (T7)' {
    It 'Read-Dv, Read-DvOrNull, Get-DependencyList and Get-State use no -Method at all (the census only GETs)' {
        foreach ($name in 'Read-Dv', 'Read-DvOrNull', 'Get-DependencyList', 'Get-State') {
            $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)[0]
            $methods = $fn.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandParameterAst] -and $n.ParameterName -eq 'Method' }, $true)
            $methods.Count | Should -Be 0 -Because "$name must only GET"
        }
    }

    It 'the only -Method uses are PATCH x2 in Edit-FormsAndViews and DELETE in Remove-Relationships; the publish goes through the shared D-83 procedure, not a hand-rolled POST (F1)' {
        $uses = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandParameterAst] -and $n.ParameterName -eq 'Method' }, $true)
        $seen = foreach ($u in $uses) {
            $cmd = $u.Parent
            $fn = $cmd
            while ($fn -and $fn -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $fn = $fn.Parent }
            $fn | Should -Not -BeNullOrEmpty
            "$($fn.Name):$($cmd.CommandElements[[array]::IndexOf($cmd.CommandElements, $u) + 1].Extent.Text)"
        }
        @($seen | Sort-Object) | Should -Be @('Edit-FormsAndViews:PATCH', 'Edit-FormsAndViews:PATCH', 'Remove-Relationships:DELETE')
    }

    It 'every writing function asks Confirm-Write before its first -Method (so -WhatIf writes nothing)' {
        foreach ($name in 'Edit-FormsAndViews', 'Remove-Relationships') {
            $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)[0]
            $fn.Extent.Text.IndexOf('Confirm-Write') | Should -BeGreaterOrEqual 0
            $fn.Extent.Text.IndexOf('Confirm-Write') | Should -BeLessThan $fn.Extent.Text.IndexOf('-Method')
        }
    }

    It 'Publish-RateCard publishes only sprk_ratecard, through Invoke-PublishXml, after Confirm-Write and the pending-collateral check; the script loads the shared procedure and names no hand-rolled PublishXml (F1)' {
        $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Publish-RateCard' }, $true)[0].Extent.Text
        $fn.IndexOf('Confirm-Write') | Should -BeGreaterOrEqual 0
        $fn.IndexOf('Get-PendingCollateral') | Should -BeGreaterThan $fn.IndexOf('Confirm-Write')
        $fn.IndexOf('Invoke-PublishXml') | Should -BeGreaterThan $fn.IndexOf('Get-PendingCollateral')
        $fn | Should -Match '<entity>\$Table</entity>'
        $fn | Should -Not -Match 'Referenced|\$Targets'
        $text = $script:Ast.Extent.Text
        $text | Should -Match "(?m)^\. \(Join-Path \`$PSScriptRoot '\.\.' 'lib' 'Publish-SolutionComponents\.ps1'\)"
        $text | Should -Not -Match "Path 'PublishXml'|Publish-Tables"
    }

    It 'Get-State checks that the caller reads every row before it counts (F1), and the module exports the check' {
        $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-State' }, $true)[0].Extent.Text
        $fn.IndexOf('Get-ReadAllRowsGap') | Should -BeGreaterOrEqual 0
        $fn.IndexOf('Get-RowCountExact') | Should -BeGreaterThan $fn.IndexOf('Get-ReadAllRowsGap')
        (Get-Content -Raw $script:ModulePath) | Should -Match "(?s)Export-ModuleMember.*'Get-ReadAllRowsGap'"
    }

    It 'Confirm-Write is ShouldProcess, so -WhatIf reaches it' {
        $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Confirm-Write' }, $true)[0]
        $fn.Extent.Text | Should -Match 'PSCmdlet\.ShouldProcess'
        $script:Ast.Extent.Text | Should -Match 'SupportsShouldProcess = \$true'
    }

    It 'the apply path refuses (throws) before the first write, and the verdict is computed before any write' {
        $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-Retirement' }, $true)[0].Extent.Text
        $fn.IndexOf('Get-ApplyBlockers $state') | Should -BeGreaterThan 0
        $fn.IndexOf('throw [EscalationException]') | Should -BeGreaterThan $fn.IndexOf('Get-ApplyBlockers $state')
        $fn.IndexOf('Get-PendingCollateral') | Should -BeGreaterThan $fn.IndexOf('throw [EscalationException]')
        $fn.IndexOf('Edit-FormsAndViews') | Should -BeGreaterThan $fn.IndexOf('Get-PendingCollateral')
        $fn.IndexOf('Remove-Relationships') | Should -BeGreaterThan $fn.IndexOf('Edit-FormsAndViews')
    }

    It 'the pre-flight (approval, attestations, environment) runs before Initialize-EbillingSchema, i.e. before any Dataverse call' {
        $text = $script:Ast.Extent.Text
        $text.LastIndexOf('Get-PreflightRefusals (') | Should -BeGreaterThan 0
        $text.LastIndexOf('Initialize-EbillingSchema -EnvironmentUrl') | Should -BeGreaterThan $text.LastIndexOf('Get-PreflightRefusals (')
    }

    It 'sends every Dataverse call through EbillingSchema.psm1 (Consistency: Strong): no direct REST call, the module is loaded' {
        $text = $script:Ast.Extent.Text
        $text | Should -Match '(?m)^using module \.\\EbillingSchema\.psm1'
        $text | Should -Not -Match 'Invoke-RestMethod|Invoke-WebRequest|Invoke-Dv[^\r\n]*-Headers|\$headers'
        $module = Get-Content -Raw (Join-Path $script:RepoRoot 'scripts' 'ebilling' 'EbillingSchema.psm1')
        $module | Should -Match "Consistency\s*=\s*'Strong'"
    }
}

Describe 'the schema script, the forms script and the solution source no longer name the lookups (T8)' {
    BeforeAll {
        $script:Retired = @('sprk_matter', 'sprk_project', 'sprk_workassignment')
        $script:RetiredRels = @('sprk_matter_ratecard_matter', 'sprk_project_ratecard_project', 'sprk_workassignment_ratecard_workassignment')
        $script:Master = Join-Path $script:RepoRoot 'src' 'dataverse' 'solutions' 'SpaarkeMaster'
    }

    It 'Deploy-EbillingTimekeeperRateCardSchema.ps1 adds no Add-Lookup for them and no longer needs the core tables' {
        $path = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Deploy-EbillingTimekeeperRateCardSchema.ps1'
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        $errors.Count | Should -Be 0
        $calls = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Add-Lookup' }, $true)
        $columns = @(foreach ($c in $calls) {
            $i = 0; $els = $c.CommandElements
            for ($i = 0; $i -lt $els.Count; $i++) { if ($els[$i] -is [System.Management.Automation.Language.CommandParameterAst] -and $els[$i].ParameterName -eq 'Column') { $els[$i + 1].Value } }
        })
        # Must-fire control: the scan does see the lookups that stay.
        $columns | Should -Contain 'sprk_firm'
        $columns | Should -Contain 'sprk_sourcedocument'
        foreach ($r in $script:Retired) { $columns | Should -Not -Contain $r }
        $text = Get-Content -Raw $path
        foreach ($rel in $script:RetiredRels) { $text | Should -Not -Match $rel }
        $text | Should -Not -Match "'sprk_matter'|'sprk_project'|'sprk_workassignment'|<entity>sprk_matter</entity>"
    }

    It 'Deploy-EbillingFormsViews.ps1 puts none of them on the sprk_ratecard form or view' {
        $text = Get-Content -Raw (Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Deploy-EbillingFormsViews.ps1')
        $m = [regex]::Match($text, "(?s)sprk_ratecard\s*=\s*@\{.*?\r?\n    \}")
        $m.Success | Should -BeTrue
        foreach ($r in $script:Retired) { $m.Value | Should -Not -Match $r }
        $m.Value | Should -Match 'sprk_firm'
    }

    It 'SpaarkeMaster: the sprk_ratecard Entity.xml, main forms and view hold none of the three attributes (and keep sprk_firm, sprk_procedureversion)' {
        $dir = Join-Path $script:Master 'Entities' 'sprk_ratecard'
        $entity = Get-Content -Raw (Join-Path $dir 'Entity.xml')
        $entity | Should -Match '<LogicalName>sprk_firm</LogicalName>'
        $entity | Should -Match '<LogicalName>sprk_procedureversion</LogicalName>'
        foreach ($r in $script:Retired) { $entity | Should -Not -Match "<LogicalName>$r</LogicalName>" }
        $files = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.xml')
        $files.Count | Should -BeGreaterThan 5
        foreach ($f in $files) {
            $t = (Get-Content -Raw -LiteralPath $f.FullName).TrimStart([char]0xFEFF)
            [void][xml]$t     # still well-formed after the removal
            foreach ($r in $script:Retired) {
                $pattern = '(?i)(datafieldname="|attribute name="|cell name="|control id="|<LogicalName>|<Name>|PhysicalName=")' + $r + '("|<)'
                $t | Should -Not -Match $pattern -Because "$($f.Name) must not name $r"
            }
        }
    }

    It 'SpaarkeMaster: no relationship file names the three relationships, and the other relationships next to them are still there' {
        $rels = Join-Path $script:Master 'Other'
        $all = @(Get-ChildItem -LiteralPath $rels -Recurse -File -Filter '*.xml')
        $all.Count | Should -BeGreaterThan 5
        foreach ($f in $all) {
            $t = Get-Content -Raw -LiteralPath $f.FullName
            foreach ($rel in $script:RetiredRels) { $t | Should -Not -Match $rel -Because "$($f.Name) must not name $rel" }
        }
        $matter = Get-Content -Raw (Join-Path $rels 'Relationships' 'sprk_Matter.xml')
        $matter | Should -Match 'sprk_matter_ruleset_matter'
        [void][xml]$matter.TrimStart([char]0xFEFF)
    }
}
