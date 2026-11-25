#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Loads (or resets) the Spaarke demo dataset on a DEMO customer stamp. Operator script: never part of provisioning.

.DESCRIPTION
    Data plan Tier 4 (projects/customer-provisioning-orchestration-r1/notes/data-setup-plan.md, task D6). Writes the
    records in scripts/demo-data/demo-records.json (a coherent legal-department story: organizations, contacts, matters,
    projects, events, to-dos, communications and document records) into one stamp's Dataverse, so the product can be
    demonstrated, and removes them again with -Reset.

    SAFETY, in the order the script applies it:
      1. The operator's own az sign-in is used (NFR-11). A service principal is refused. No secret is read or written.
      2. DEMO GUARD. The target's row in the environment registry (sprk_dataverseenvironment on the admin environment)
         must be Environment Type = Demo and active. Anything else is refused unless -AllowNonDemo is given (with a
         warning). A Production row is refused even with -AllowNonDemo. -CustomerId resolves the stamp from that row;
         -DataverseUrl names it directly and is checked against the same registry.
      3. Everything the dataset references is resolved on the target BEFORE anything is written: the taxonomy rows by
         name (matter types, practice areas, project/contact/organization types), the event types by code, the record
         types, the choice labels, the schema, and the owner team. Every missing value is listed, and nothing is written.
      4. Every row is TAGGED (standard column importsequencenumber, see scripts/demo-data/DemoDataLib.ps1). Re-running
         matches by tag + natural key and creates nothing new. -Reset deletes only tagged rows, children first, and keeps
         a tagged parent that an untagged row points at.
      5. Rows are owned by the customer's business unit default team, so users of that unit (who read at Deep) see them.

    Documents are METADATA ONLY (no file): uploading needs the stamp's BFF with a user token. See
    scripts/demo-data/README.md ("Documents and files").

    What this script no longer does (the v1 script did): AI seed data, chart definitions, SPE uploads, AI Search
    indexing. AI content and chart definitions are product configuration (H12a / H12b seed them per stamp); the index
    sync pointed at the production search service. -Force is gone: run -Reset, then load again.

.PARAMETER CustomerId
    The customer id (registry column sprk_customerid), for example 'acme'. The Dataverse URL and the owner come from the
    customer's registry row. Use this or -DataverseUrl.

.PARAMETER DataverseUrl
    The stamp's Dataverse URL, host only (https://<org>.crm.dynamics.com). Alias: -EnvironmentUrl. Checked against the
    registry (the row with that URL must be a Demo row, or -AllowNonDemo is needed).

.PARAMETER RegistryUrl
    The admin environment that holds the registry. Default: environments.dev.dataverseUrl in config/environments.json.

.PARAMETER Reset
    Delete the tagged demo rows instead of loading. Honours -WhatIf.

.PARAMETER AllowNonDemo
    Operator override for a target the registry does not mark as an active Demo environment. Warns. Never overrides Production.

.PARAMETER OwnerBusinessUnitName
    Business unit whose default team owns the rows. Default: the registry row's sprk_businessunitname, else its sprk_name
    (the customer's display name, which H10 names the customer's unit with).

.PARAMETER OwnerTeamName
    An explicit owner team in that unit (default: the unit's default team).

.PARAMETER OwnerIsOperator
    Leave the operator as owner (the rows land in the operator's own business unit and are invisible to users of a child
    unit). Only for a stamp with no customer unit.

.PARAMETER SkipDocuments / SkipCommunications
    Leave a section out of a load or reset.

.PARAMETER DryRun
    Same as -WhatIf (kept from the v1 script). Reads, writes nothing.

.EXAMPLE
    .\Load-DemoSampleData.ps1 -CustomerId acme -WhatIf
    # Resolve ACME from the registry, check everything, print what would be created. Writes nothing.

.EXAMPLE
    .\Load-DemoSampleData.ps1 -CustomerId acme
    # Load. Re-running creates nothing new.

.EXAMPLE
    .\Load-DemoSampleData.ps1 -CustomerId acme -Reset
    # Remove the tagged demo rows (children first).
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$CustomerId,

    [Alias('EnvironmentUrl')]
    [string]$DataverseUrl,

    [string]$RegistryUrl,

    [string]$DataFile,

    [switch]$Reset,
    [switch]$AllowNonDemo,

    [string]$OwnerBusinessUnitName,
    [string]$OwnerTeamName,
    [switch]$OwnerIsOperator,

    [switch]$SkipDocuments,
    [switch]$SkipCommunications,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if ($DryRun) { $WhatIfPreference = $true }

. (Join-Path $PSScriptRoot 'demo-data' 'DemoDataLib.ps1')

if (-not $DataFile) { $DataFile = Join-Path $PSScriptRoot 'demo-data' 'demo-records.json' }

# ============================================================================
# HTTP (the only place that talks to Dataverse)
# ============================================================================

$script:Tokens = @{}

function Get-DvToken {
    param([Parameter(Mandatory)][string]$Url)
    if (-not $script:Tokens.ContainsKey($Url)) {
        $t = & az account get-access-token --resource $Url --query accessToken -o tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $t) { throw "Could not get an access token for $Url with the operator's az sign-in. Run 'az login' as yourself and check you can sign in to that environment." }
        $script:Tokens[$Url] = "$t".Trim()
    }
    return $script:Tokens[$Url]
}

function Format-DvError {
    param($ErrorRecord)
    $status = $null
    try { $status = [int]$ErrorRecord.Exception.Response.StatusCode } catch { $null = $_ }
    $msg = $ErrorRecord.Exception.Message
    try {
        $detail = $ErrorRecord.ErrorDetails.Message
        if ($detail) { $parsed = $detail | ConvertFrom-Json; if ($parsed.error.message) { $msg = $parsed.error.message } }
    }
    catch { $null = $_ }
    return "HTTP $status $msg"
}

function Invoke-Dv {
    <# One Dataverse Web API call with the operator's token; retries throttling (429) and 503/504. #>
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$PathAndQuery,
        [ValidateSet('GET', 'POST', 'DELETE')][string]$Method = 'GET',
        $Body,
        [switch]$ReturnHeaders
    )
    $uri = if ($PathAndQuery -match '^https://') { $PathAndQuery } else { "$Url/api/data/v9.2/$PathAndQuery" }
    $headers = @{
        Authorization      = "Bearer $(Get-DvToken $Url)"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Prefer             = 'odata.include-annotations="*"'
    }
    if ($Method -eq 'POST') { $headers['Prefer'] = 'return=minimal' }
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params['Body'] = ($Body | ConvertTo-Json -Depth 6 -Compress); $params['ContentType'] = 'application/json; charset=utf-8' }
    for ($attempt = 1; ; $attempt++) {
        try {
            if ($ReturnHeaders) { $null = Invoke-RestMethod @params -ResponseHeadersVariable rh; return $rh }
            return Invoke-RestMethod @params
        }
        catch {
            $status = $null
            try { $status = [int]$_.Exception.Response.StatusCode } catch { $null = $_ }
            if ($status -in 429, 503, 504 -and $attempt -lt 5) { Start-Sleep -Seconds ([math]::Min(30, 3 * $attempt)); continue }
            throw (Format-DvError $_)
        }
    }
}

function Invoke-DvWrite {
    <# POST or DELETE. Refuses under -WhatIf even if a caller forgot ShouldProcess: a dry run never writes. #>
    param([string]$Url, [string]$PathAndQuery, [ValidateSet('POST', 'DELETE')][string]$Method, $Body, [switch]$ReturnHeaders)
    if ($WhatIfPreference) { throw 'Internal error: a write was attempted under -WhatIf.' }
    return Invoke-Dv -Url $Url -PathAndQuery $PathAndQuery -Method $Method -Body $Body -ReturnHeaders:$ReturnHeaders
}

function Get-DvAll {
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$PathAndQuery)
    $all = New-Object System.Collections.Generic.List[object]
    $next = $PathAndQuery
    while ($next) {
        $r = Invoke-Dv -Url $Url -PathAndQuery $next
        foreach ($v in @($r.value)) { $all.Add($v) }
        $next = $r.'@odata.nextLink'
    }
    return $all.ToArray()
}

function ConvertTo-Query {
    param([Parameter(Mandatory)][string]$Set, [string]$Select, [string]$Filter)
    $q = $Set
    $sep = '?'
    if ($Select) { $q += $sep + '$select=' + $Select; $sep = '&' }
    if ($Filter) { $q += $sep + '$filter=' + [uri]::EscapeDataString($Filter) }
    return $q
}

# ============================================================================
# OPERATOR, REGISTRY, GUARD
# ============================================================================

function Get-DefaultRegistryUrl {
    $cfg = Join-Path $PSScriptRoot '..' 'config' 'environments.json'
    if (-not (Test-Path $cfg)) { throw "config/environments.json not found; pass -RegistryUrl." }
    $url = (Get-Content $cfg -Raw | ConvertFrom-Json).environments.dev.dataverseUrl
    if (-not $url) { throw "environments.dev.dataverseUrl is not set in config/environments.json; pass -RegistryUrl." }
    return $url
}

$registrySelect = 'sprk_dataverseenvironmentid,sprk_name,sprk_customerid,sprk_dataverseurl,sprk_environmenttype,sprk_isactive,sprk_setupstatus,sprk_businessunitname,sprk_teamname,sprk_appservicename'

function Get-RegistryRowByCustomer {
    param([string]$Registry, [string]$Customer)
    $rows = @(Get-DvAll -Url $Registry -PathAndQuery (ConvertTo-Query -Set 'sprk_dataverseenvironments' -Select $registrySelect -Filter "sprk_customerid eq '$(ConvertTo-DemoODataString $Customer)'"))
    if ($rows.Count -eq 0) { throw "No registry row has sprk_customerid '$Customer' on $Registry." }
    if ($rows.Count -gt 1) { throw "$($rows.Count) registry rows have sprk_customerid '$Customer'; refusing to guess." }
    return $rows[0]
}

function Get-RegistryRowByUrl {
    param([string]$Registry, [string]$Target)
    $rows = @(Get-DvAll -Url $Registry -PathAndQuery (ConvertTo-Query -Set 'sprk_dataverseenvironments' -Select $registrySelect))
    $match = @($rows | Where-Object {
            $u = $null
            if ($_.sprk_dataverseurl) { try { $u = Resolve-DemoDataverseUrl $_.sprk_dataverseurl } catch { $u = $null } }
            $u -eq $Target
        })
    $active = @($match | Where-Object { $_.sprk_isactive -eq $true })
    if ($active.Count -gt 1) { throw "$($active.Count) active registry rows have the URL $Target; refusing to guess." }
    if ($active.Count -eq 1) { return $active[0] }
    if ($match.Count -ge 1) { return $match[0] }
    return $null
}

# ============================================================================
# TARGET RESOLUTION (schema, reference values, owner)
# ============================================================================

function Get-TargetSchema {
    <# For each table the dataset writes: which columns exist, and each lookup column's navigation property. #>
    param([string]$Url, $Specs)
    $schema = @{}
    $problems = New-Object System.Collections.Generic.List[string]
    foreach ($spec in $Specs) {
        $entity = $spec.Logical
        try {
            $attrs = Invoke-Dv -Url $Url -PathAndQuery "EntityDefinitions(LogicalName='$entity')/Attributes?`$select=LogicalName"
            $rels = Invoke-Dv -Url $Url -PathAndQuery "EntityDefinitions(LogicalName='$entity')/ManyToOneRelationships?`$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName"
        }
        catch {
            $problems.Add("Table '$entity' is not readable on the target ($_). Is SpaarkeMaster imported?")
            continue
        }
        $have = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($a in $attrs.value) { $null = $have.Add($a.LogicalName) }
        $nav = @{}
        foreach ($r in $rels.value) { $nav[$r.ReferencingAttribute] = $r.ReferencingEntityNavigationPropertyName }
        $schema[$entity] = $nav
        $missing = @(Get-DemoSpecColumns $spec | Where-Object { -not $have.Contains($_) })
        if ($missing.Count -gt 0) { $problems.Add("Table '$entity' has no column(s): $($missing -join ', ').") }
        $noNav = @(Get-DemoLookupColumns $spec | Where-Object { $have.Contains($_) -and -not $nav.ContainsKey($_) })
        if ($noNav.Count -gt 0) { $problems.Add("Table '$entity': no navigation property for lookup column(s): $($noNav -join ', ').") }
    }
    return [pscustomobject]@{ Nav = $schema; Problems = $problems.ToArray() }
}

function Resolve-TargetReferences {
    <# Resolves every reference the dataset needs. Returns the id/value maps plus EVERY missing value. #>
    param([string]$Url, $Requirements)
    $refIds = @{}
    $optionValues = @{}
    $missing = New-Object System.Collections.Generic.List[string]
    $taxonomy = Get-DemoTaxonomyTables

    foreach ($group in ($Requirements | Where-Object { $_.Kind -eq 'taxonomy' } | Group-Object Table)) {
        $t = $taxonomy[$group.Name]
        $refIds[$group.Name] = @{}
        try { $rows = @(Get-DvAll -Url $Url -PathAndQuery (ConvertTo-Query -Set $t.EntitySet -Select "$($t.IdColumn),$($t.NameColumn)" -Filter 'statecode eq 0')) }
        catch { $missing.Add("$($group.Name): table not readable on the target ($_)"); continue }
        $byName = @{}
        foreach ($r in $rows) {
            $n = "$($r.($t.NameColumn))"
            if (-not $byName.ContainsKey($n)) { $byName[$n] = New-Object System.Collections.Generic.List[string] }
            $byName[$n].Add($r.($t.IdColumn))
        }
        foreach ($req in $group.Group) {
            if (-not $byName.ContainsKey($req.Name)) { $missing.Add("$($group.Name) ($($t.Label)): '$($req.Name)' not found") }
            elseif ($byName[$req.Name].Count -gt 1) { $missing.Add("$($group.Name) ($($t.Label)): '$($req.Name)' is ambiguous ($($byName[$req.Name].Count) active rows)") }
            else { $refIds[$group.Name][$req.Name] = $byName[$req.Name][0] }
        }
    }

    foreach ($group in ($Requirements | Where-Object { $_.Kind -eq 'option' } | Group-Object { "$($_.Entity).$($_.Column)" })) {
        $first = $group.Group[0]
        try {
            $o = Invoke-Dv -Url $Url -PathAndQuery "EntityDefinitions(LogicalName='$($first.Entity)')/Attributes(LogicalName='$($first.Column)')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
        }
        catch { $missing.Add("$($group.Name): not a readable choice column on the target ($_)"); continue }
        $map = @{}
        foreach ($opt in $o.OptionSet.Options) { $map["$($opt.Label.UserLocalizedLabel.Label)"] = [int]$opt.Value }
        $optionValues[$group.Name] = $map
        foreach ($req in $group.Group) { if (-not $map.ContainsKey($req.Name)) { $missing.Add("$($group.Name) (choice): label '$($req.Name)' not found") } }
    }
    return [pscustomobject]@{ RefIds = $refIds; OptionValues = $optionValues; Missing = $missing.ToArray() }
}

function Resolve-OwnerTeam {
    param([string]$Url, $Plan)
    if (-not $Plan.BusinessUnitName) { throw 'No owner business unit is known (the registry row names none). Pass -OwnerBusinessUnitName, or -OwnerIsOperator.' }
    if (Test-DemoReservedBusinessUnit $Plan.BusinessUnitName) { throw "'$($Plan.BusinessUnitName)' is a reserved business unit; demo rows are never owned there." }
    $bus = @(Get-DvAll -Url $Url -PathAndQuery (ConvertTo-Query -Set 'businessunits' -Select 'businessunitid,name,_parentbusinessunitid_value' -Filter "name eq '$(ConvertTo-DemoODataString $Plan.BusinessUnitName)' and isdisabled eq false"))
    if ($bus.Count -ne 1) { throw "Expected exactly one business unit named '$($Plan.BusinessUnitName)' on the target, found $($bus.Count)." }
    if (-not $bus[0]._parentbusinessunitid_value) { throw "'$($Plan.BusinessUnitName)' is the root business unit; demo rows belong in the customer's own unit." }
    $filter = "_businessunitid_value eq $($bus[0].businessunitid) and teamtype eq 0"
    if ($Plan.TeamName) { $filter += " and name eq '$(ConvertTo-DemoODataString $Plan.TeamName)'" } else { $filter += ' and isdefault eq true' }
    $teams = @(Get-DvAll -Url $Url -PathAndQuery (ConvertTo-Query -Set 'teams' -Select 'teamid,name' -Filter $filter))
    if ($teams.Count -ne 1) { throw "Expected exactly one owner team ($(if ($Plan.TeamName) { "named '$($Plan.TeamName)'" } else { 'the default team' })) in business unit '$($Plan.BusinessUnitName)', found $($teams.Count)." }
    return [pscustomobject]@{ TeamId = $teams[0].teamid; TeamName = $teams[0].name; BusinessUnitId = $bus[0].businessunitid; BusinessUnitName = $bus[0].name }
}

# ============================================================================
# RESET HELPERS
# ============================================================================

function Get-UntaggedReferences {
    <# Untagged rows that point at any of -Ids through any Spaarke lookup. Fails closed: an unreadable relationship is reported. #>
    param([string]$Url, [string]$Logical, [string[]]$Ids)
    $refs = @{}
    $unchecked = New-Object System.Collections.Generic.List[string]
    if ($Ids.Count -eq 0) { return [pscustomobject]@{ Refs = $refs; Unchecked = @() } }
    $rels = (Invoke-Dv -Url $Url -PathAndQuery "EntityDefinitions(LogicalName='$Logical')/OneToManyRelationships?`$select=ReferencingEntity,ReferencingAttribute").value |
        Where-Object { $_.ReferencingEntity -match '^(sprk_|contact$|account$)' }
    $setNames = @{}
    foreach ($rel in $rels) {
        $label = "$($rel.ReferencingEntity).$($rel.ReferencingAttribute)"
        try {
            if (-not $setNames.ContainsKey($rel.ReferencingEntity)) {
                $setNames[$rel.ReferencingEntity] = (Invoke-Dv -Url $Url -PathAndQuery "EntityDefinitions(LogicalName='$($rel.ReferencingEntity)')?`$select=EntitySetName").EntitySetName
            }
            $col = $rel.ReferencingAttribute
            $anyOf = ($Ids | ForEach-Object { "_$($col)_value eq $_" }) -join ' or '
            $filter = "($anyOf) and (importsequencenumber eq null or importsequencenumber ne $(Get-DemoImportSequenceNumber))"
            $rows = @(Get-DvAll -Url $Url -PathAndQuery (ConvertTo-Query -Set $setNames[$rel.ReferencingEntity] -Select "_$($col)_value" -Filter $filter))
            foreach ($r in $rows) {
                $parent = "$($r.("_$($col)_value"))".ToLowerInvariant()
                if (-not $refs.ContainsKey($parent)) { $refs[$parent] = New-Object System.Collections.Generic.List[string] }
                $refs[$parent].Add($label)
            }
        }
        catch { $unchecked.Add("$label ($_)") }
    }
    return [pscustomobject]@{ Refs = $refs; Unchecked = $unchecked.ToArray() }
}

# ============================================================================
# MAIN
# ============================================================================

$summary = [ordered]@{}
$problemsFound = $false

function Write-Section { param([string]$Text) Write-Host ''; Write-Host "--- $Text ---" -ForegroundColor Cyan }

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  Spaarke demo data loader (dataset v2)' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan

if ([bool]$CustomerId -eq [bool]$DataverseUrl) { throw 'Give exactly one of -CustomerId or -DataverseUrl.' }
if (-not (Test-Path $DataFile)) { throw "Data file not found: $DataFile" }

# 1. Dataset
$dataset = Get-Content $DataFile -Raw -Encoding UTF8 | ConvertFrom-Json
$datasetErrors = @(Test-DemoDataset -Dataset $dataset)
if ($datasetErrors.Count -gt 0) {
    Write-Host "The dataset is invalid ($($datasetErrors.Count) problem(s)):" -ForegroundColor Red
    $datasetErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Dataset:     $($dataset.datasetName) $($dataset.datasetVersion)"

# 2. Operator identity (own user, never a service principal)
$account = (& az account show 2>$null | ConvertFrom-Json)
if (-not $account) { throw "az is not signed in. Run 'az login' as yourself." }
$identity = Test-DemoOperatorIdentity -Account $account
if (-not $identity.Allowed) { throw $identity.Reason }
Write-Host "Operator:    $($account.user.name)"

# 3. Target + demo guard
if (-not $RegistryUrl) { $RegistryUrl = Get-DefaultRegistryUrl }
$RegistryUrl = Resolve-DemoDataverseUrl $RegistryUrl
$registryRow = $null
if ($CustomerId) {
    $registryRow = Get-RegistryRowByCustomer -Registry $RegistryUrl -Customer $CustomerId
    if (-not $registryRow.sprk_dataverseurl) { throw "Registry row '$($registryRow.sprk_name)' has no sprk_dataverseurl." }
    $targetUrl = Resolve-DemoDataverseUrl $registryRow.sprk_dataverseurl
}
else {
    $targetUrl = Resolve-DemoDataverseUrl $DataverseUrl
    try { $registryRow = Get-RegistryRowByUrl -Registry $RegistryUrl -Target $targetUrl }
    catch { Write-Host "Registry lookup failed: $_" -ForegroundColor Yellow }
}
Write-Host "Target:      $targetUrl"
if ($registryRow) {
    Write-Host ("Registry:    '{0}' customerId='{1}' type={2} active={3} setup={4}" -f $registryRow.sprk_name, $registryRow.sprk_customerid, (Get-DemoEnvironmentTypeName $registryRow.sprk_environmenttype), $registryRow.sprk_isactive, $registryRow.'sprk_setupstatus@OData.Community.Display.V1.FormattedValue')
}
else { Write-Host 'Registry:    (no row for this target)' -ForegroundColor Yellow }

$guard = Test-DemoTargetAllowed -RegistryRow $registryRow -AllowNonDemo:$AllowNonDemo
if (-not $guard.Allowed) {
    Write-Host ''
    Write-Host 'REFUSED: this is not a demo stamp.' -ForegroundColor Red
    $guard.Reasons | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host '  Mark the environment Demo in the registry, or pass -AllowNonDemo if you are sure (never for Production).' -ForegroundColor Red
    exit 1
}
foreach ($w in $guard.Warnings) { Write-Host "WARNING: $w" -ForegroundColor Yellow }

if ($WhatIfPreference) { Write-Host 'Mode:        WHATIF (reads only, writes nothing)' -ForegroundColor Yellow }
elseif ($Reset) { Write-Host 'Mode:        RESET (deletes tagged demo rows)' -ForegroundColor Yellow }
else { Write-Host 'Mode:        LOAD' -ForegroundColor Green }

# 4. Sections in scope
$skip = @()
if ($SkipDocuments) { $skip += 'documents' }
if ($SkipCommunications) { $skip += 'communications' }
$specs = @(Get-DemoTableSpecs | Where-Object { $_.Section -notin $skip })
if ($Reset) { $specs = @(Get-DemoResetOrder | Where-Object { $_ -notin $skip } | ForEach-Object { $s = $_; Get-DemoTableSpecs | Where-Object { $_.Section -eq $s } }) }

# 5. The target must have the product
try { $null = Invoke-Dv -Url $targetUrl -PathAndQuery 'sprk_matters?$top=1&$select=sprk_matterid' }
catch { throw "The target has no Spaarke tables ($_). Import SpaarkeMaster first (solution-authoring/Import-SpaarkeMasterPackage.ps1)." }

# ============================================================================
# RESET
# ============================================================================

if ($Reset) {
    $tag = Get-DemoImportSequenceNumber
    foreach ($spec in $specs) {
        Write-Section "Reset: $($spec.Section) ($($spec.EntitySet))"
        $rows = @(Get-DvAll -Url $targetUrl -PathAndQuery (ConvertTo-Query -Set $spec.EntitySet -Select "$($spec.IdColumn),$($spec.NaturalKey)" -Filter (Get-DemoTaggedFilter)) |
                ForEach-Object { [pscustomobject]@{ Id = $_.($spec.IdColumn); Name = "$($_.($spec.NaturalKey))" } })
        $counts = [ordered]@{ Deleted = 0; WouldDelete = 0; Kept = 0; Errors = 0 }
        $summary[$spec.Section] = $counts
        if ($rows.Count -eq 0) { Write-Host '  nothing tagged'; continue }

        $untagged = @{}
        # Every table: any untagged row that points at a demo row would lose its link (every lookup is RemoveLink on delete).
        $check = Get-UntaggedReferences -Url $targetUrl -Logical $spec.Logical -Ids @($rows | ForEach-Object { $_.Id })
        $untagged = $check.Refs
        if ($check.Unchecked.Count -gt 0) {
            Write-Host "  Could not check $($check.Unchecked.Count) relationship(s); keeping every row of this table (fail closed):" -ForegroundColor Yellow
            $check.Unchecked | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
            $untagged = @{}
            foreach ($r in $rows) { $untagged["$($r.Id)".ToLowerInvariant()] = @('unchecked relationships') }
        }
        $plan = Select-DemoDeletableRows -Rows $rows -UntaggedReferences $untagged
        foreach ($k in $plan.Kept) {
            $counts.Kept++
            Write-Host "  KEEP   '$($k.Name)': untagged rows point at it via $($k.Reasons -join ', ')" -ForegroundColor Yellow
        }
        foreach ($row in $plan.Deletable) {
            if ($PSCmdlet.ShouldProcess("$($spec.Logical) '$($row.Name)' ($($row.Id))", "Delete tagged demo row (importsequencenumber = $tag)")) {
                try {
                    Invoke-DvWrite -Url $targetUrl -PathAndQuery "$($spec.EntitySet)($($row.Id))" -Method DELETE
                    $counts.Deleted++
                    Write-Host "  DELETED '$($row.Name)'" -ForegroundColor Green
                }
                catch { $counts.Errors++; Write-Host "  ERROR  '$($row.Name)': $_" -ForegroundColor Red }
            }
            else { $counts.WouldDelete++ }
        }
    }
}

# ============================================================================
# LOAD
# ============================================================================

else {
    # Resolve everything first.
    Write-Section 'Resolving the target'
    $schema = Get-TargetSchema -Url $targetUrl -Specs $specs
    $work = [pscustomobject]@{}
    foreach ($p in $dataset.PSObject.Properties) { $work | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value }
    foreach ($s in $skip) { $work.$s = @() }
    $requirements = @(Get-DemoReferenceRequirements $work)
    $resolved = Resolve-TargetReferences -Url $targetUrl -Requirements $requirements

    $ownerTeamId = $null
    $ownerProblem = $null
    if ($OwnerIsOperator) {
        Write-Host 'WARNING: -OwnerIsOperator: rows are owned by the operator and sit in the operator''s business unit.' -ForegroundColor Yellow
    }
    else {
        $plan = Get-DemoOwnerPlan -RegistryRow $registryRow -OwnerBusinessUnitName $OwnerBusinessUnitName -OwnerTeamName $OwnerTeamName
        try {
            $owner = Resolve-OwnerTeam -Url $targetUrl -Plan $plan
            $ownerTeamId = $owner.TeamId
            Write-Host "Owner:       team '$($owner.TeamName)' in business unit '$($owner.BusinessUnitName)' (from $($plan.Source))"
        }
        catch { $ownerProblem = "Owner team: $_" }
    }

    $allProblems = @($schema.Problems) + @($resolved.Missing | ForEach-Object { "Missing reference value - $_" })
    if ($ownerProblem) { $allProblems += $ownerProblem }
    if ($allProblems.Count -gt 0) {
        $problemsFound = $true
        Write-Host ''
        Write-Host "The target cannot take the dataset yet: $($allProblems.Count) problem(s)." -ForegroundColor Red
        $byTable = $resolved.Missing | Group-Object { ($_ -split ' ')[0] }
        foreach ($p in $schema.Problems) { Write-Host "  $p" -ForegroundColor Red }
        foreach ($g in $byTable) {
            Write-Host "  $($g.Name) - $($g.Count) missing:" -ForegroundColor Red
            $g.Group | ForEach-Object { Write-Host "    $($_.Substring($g.Name.Length).Trim())" -ForegroundColor Red }
        }
        if ($ownerProblem) { Write-Host "  $ownerProblem" -ForegroundColor Red }
        Write-Host '  Seed the missing values (data plan D2/D4: ISS-039 event types, the legal-standard taxonomy pack) and re-run.' -ForegroundColor Red
        if (-not $WhatIfPreference) { Write-Host 'Nothing was written.' -ForegroundColor Red; exit 1 }
        Write-Host 'WHATIF: a real run would stop here and write nothing. The plan below shows what it would create once these are fixed.' -ForegroundColor Yellow
        # Placeholders so the plan can be printed in full (nothing below writes under -WhatIf).
        foreach ($req in ($requirements | Where-Object { $_.Kind -eq 'taxonomy' })) {
            if (-not $resolved.RefIds.ContainsKey($req.Table)) { $resolved.RefIds[$req.Table] = @{} }
            if (-not $resolved.RefIds[$req.Table].ContainsKey($req.Name)) { $resolved.RefIds[$req.Table][$req.Name] = [guid]::Empty.ToString() }
        }
        foreach ($req in ($requirements | Where-Object { $_.Kind -eq 'option' })) {
            $k = "$($req.Entity).$($req.Column)"
            if (-not $resolved.OptionValues.ContainsKey($k)) { $resolved.OptionValues[$k] = @{} }
            if (-not $resolved.OptionValues[$k].ContainsKey($req.Name)) { $resolved.OptionValues[$k][$req.Name] = 0 }
        }
        foreach ($spec in $specs) {
            if (-not $schema.Nav.ContainsKey($spec.Logical)) { $schema.Nav[$spec.Logical] = @{} }
            foreach ($c in (Get-DemoLookupColumns $spec)) { if (-not $schema.Nav[$spec.Logical].ContainsKey($c)) { $schema.Nav[$spec.Logical][$c] = "placeholder_$c" } }
        }
    }

    # Create, parents first.
    $ctx = @{
        Today        = (Get-Date).ToUniversalTime().Date
        OwnerTeamId  = $ownerTeamId
        Nav          = $schema.Nav
        RefIds       = $resolved.RefIds
        OptionValues = $resolved.OptionValues
        KeyIds       = @{}
        KeyInfo      = @{}
    }
    foreach ($spec in (Get-DemoTableSpecs)) {
        $ctx.KeyIds[$spec.Section] = @{}
        $ctx.KeyInfo[$spec.Section] = @{}
        foreach ($row in @($dataset.($spec.Section))) {
            $f = Get-DemoRowProperty $row 'fields'
            $ctx.KeyInfo[$spec.Section][$row.key] = @{ Name = $(if ($spec.DisplayName) { "$($f[$spec.DisplayName])" }); Number = $(if ($spec.DisplayNumber) { "$($f[$spec.DisplayNumber])" }) }
        }
    }

    foreach ($spec in $specs) {
        Write-Section "$($spec.Section) ($($spec.EntitySet))"
        $counts = [ordered]@{ Created = 0; Planned = 0; Existing = 0; Skipped = 0; Errors = 0 }
        $summary[$spec.Section] = $counts

        $existing = @{}
        try {
            foreach ($r in (Get-DvAll -Url $targetUrl -PathAndQuery (ConvertTo-Query -Set $spec.EntitySet -Select "$($spec.IdColumn),$($spec.NaturalKey)" -Filter (Get-DemoTaggedFilter)))) {
                $existing["$($r.($spec.NaturalKey))".ToLowerInvariant()] = $r.($spec.IdColumn)
            }
        }
        catch { Write-Host "  Could not read existing tagged rows ($_)" -ForegroundColor Red; $counts.Errors++; continue }

        foreach ($row in @($dataset.($spec.Section))) {
            $nk = Get-DemoNaturalKeyValue -Spec $spec -Row $row
            if ($existing.ContainsKey($nk.ToLowerInvariant())) {
                $ctx.KeyIds[$spec.Section][$row.key] = $existing[$nk.ToLowerInvariant()]
                $counts.Existing++
                Write-Host "  EXISTS   '$nk'" -ForegroundColor DarkGray
                continue
            }
            try { $payload = New-DemoPayload -Spec $spec -Row $row -Context $ctx }
            catch {
                $counts.Skipped++
                Write-Host "  SKIPPED  '$nk': $_" -ForegroundColor Yellow
                continue
            }
            Write-Verbose ("POST {0}: {1}" -f $spec.EntitySet, ($payload | ConvertTo-Json -Depth 4 -Compress))
            if ($PSCmdlet.ShouldProcess("$($spec.Logical) '$nk'", 'Create tagged demo row')) {
                try {
                    $h = Invoke-DvWrite -Url $targetUrl -PathAndQuery $spec.EntitySet -Method POST -Body $payload -ReturnHeaders
                    $entityId = "$($h['OData-EntityId'])"
                    if ($entityId -notmatch '\(([0-9a-fA-F-]{36})\)') { throw "Created, but the response named no row id ($entityId)." }
                    $ctx.KeyIds[$spec.Section][$row.key] = $Matches[1]
                    $counts.Created++
                    Write-Host "  CREATED  '$nk'" -ForegroundColor Green
                }
                catch {
                    $counts.Errors++
                    $hint = if ("$_" -match 'already exists|duplicate|0x80060892') { ' (an untagged row already uses this key; the loader never adopts or changes untagged rows)' } else { '' }
                    Write-Host "  ERROR    '$nk': $_$hint" -ForegroundColor Red
                }
            }
            else {
                $counts.Planned++
                $ctx.KeyIds[$spec.Section][$row.key] = [guid]::Empty.ToString()
            }
        }
    }

    $docsWithFiles = @($dataset.documents | Where-Object { $_.sampleFile })
    if ('documents' -notin $skip) {
        Write-Section 'Documents and files'
        Write-Host "  Document rows are metadata only (sprk_hasfile = false). $($docsWithFiles.Count) of $(@($dataset.documents).Count) name a sample file under scripts/demo-data/sample-documents/ for a future upload step."
        Write-Host '  TODO: file upload needs the stamp BFF with a user token; see scripts/demo-data/README.md.'
    }
}

# ============================================================================
# SUMMARY
# ============================================================================

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  Summary' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
$totals = @{}
foreach ($name in $summary.Keys) {
    $line = ($summary[$name].GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)"; $totals[$_.Key] = [int]$totals[$_.Key] + [int]$_.Value }) -join '  '
    Write-Host ("  {0,-15} {1}" -f $name, $line)
}
Write-Host ("  {0,-15} {1}" -f 'TOTAL', (($totals.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '  '))
if ($WhatIfPreference) { Write-Host '  (WhatIf: Planned = would be created; nothing was written.)' -ForegroundColor Yellow }

if ($problemsFound -or $totals['Errors'] -gt 0) { exit 1 }
exit 0
