#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Task 101 (unified-access-control-r2, FR-33): creates the two estate-wide "external shares by expiration" public views
    on sprk_externalrecordaccess. DRY RUN by default.

.DESCRIPTION
    THE TWO VIEWS (owner 2026-09-10; design notes/decisions/external-grant-expiry-mandatory.md section 10.3)
      1. "Active External Shares by Expiration" - every row with statecode = 0, soonest-expiring first.
      2. "External Shares Expiring in 30 Days"   - the same rows, narrowed to an expiry ON OR BEFORE today + 30.
    Both carry: the shared record (project / matter / work assignment - the three roots the access evaluator reads),
    the contact, the organization, the access level, the expiry, Granted By and Created By.

    View 2's filter is RELATIVE, so it never goes stale. FetchXML has no relative "on or before", so it is the union of
    three relative operators, whose boundaries were measured on spaarkedev1 on 2026-10-07 (task 101 notes section 3):
        olderthan-x-days 1   ->  expiry <  today - 1
        last-x-days 1        ->  today - 1 <= expiry <= today
        next-x-days 30       ->  today <  expiry <= today + 30      (next-x-days EXCLUDES today)
    Together: every dated expiry on or before today + 30, with no gap. Undated rows match none of them.

    MODES
      (default)  Dry run. Checks every column against live metadata, runs each query it would write live and checks its
                 rows, and prints what -Apply would do. Zero writes.
      -Apply     Creates each missing view in the solution; rewrites a same-named view whose definition (published OR
                 pending) differs, after saving its current definition to scripts/logs/; adds a view that is not in the
                 solution; publishes the table whenever it wrote anything or a view has unpublished changes; then runs
                 -Verify.
      -Verify    Read-only. Exit 0 when each view exists once, its PUBLISHED definition is right, it has no unpublished
                 changes, it is in the solution, and its query returns the right rows; exit 1 naming each gap.

    The comparison is SEMANTIC - the normalised filter tree, the sort, the needed attributes, the columns - not textual,
    so a view built by hand in the maker portal (task 101 notes section 6) is verified the same way.

    Solution membership is decided by scripts/common/DataverseSolutionMembership.ps1 (Test-DvInSolution), never by a
    solutioncomponents read of this script's own.

    OPERATOR-RUN ONLY (binding directive 2026-09-04, FAILURE-MODES AP-13). Run order:
      1. pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1            (dry run)
      2. pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Apply
      3. pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Verify

.PARAMETER EnvironmentUrl
    Dataverse environment URL.

.PARAMETER SolutionUniqueName
    The unmanaged solution the views belong to. sprk_externalrecordaccess lives in SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code.

.PARAMETER PageSize
    Rows per FetchXML page (the Web API maximum is 5000). Lower it only to exercise the paging loop on a small table.
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify,
    [string]$SnapshotDir = (Join-Path $PSScriptRoot 'logs'),
    [ValidateRange(1, 5000)][int]$PageSize = 5000
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$BaseUrl/api/data/v9.2"
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; -Apply already verifies after writing.' }

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')

$Table = 'sprk_externalrecordaccess'
$EntitySet = 'sprk_externalrecordaccesses'
$ExpiryColumn = 'sprk_expiresdate'
$WindowDays = 30

# The columns, in display order. Every name is checked against live metadata before anything else runs.
$Columns = [ordered]@{
    'sprk_project'        = 160
    'sprk_matter'         = 160
    'sprk_workassignment' = 160
    'sprk_contact'        = 170
    'sprk_organization'   = 170
    'sprk_accesslevel'    = 110
    'sprk_expiresdate'    = 110
    'sprk_grantedby'      = 150
    'createdby'           = 150
}
$ActiveCondition = '<condition attribute="statecode" operator="eq" value="0" />'
$WindowFilter = '<filter type="or">' +
    "<condition attribute=""$ExpiryColumn"" operator=""olderthan-x-days"" value=""1"" />" +
    "<condition attribute=""$ExpiryColumn"" operator=""last-x-days"" value=""1"" />" +
    "<condition attribute=""$ExpiryColumn"" operator=""next-x-days"" value=""$WindowDays"" />" +
    '</filter>'

$Views = @(
    [pscustomobject]@{
        Key         = 'all'
        Name        = 'Active External Shares by Expiration'
        Description = 'Every active external share (statecode Active), soonest expiry first. Rows at the top with a past or empty expiry confer no access (FR-33, task 107): renew or end them. Task 101.'
        Filter      = $ActiveCondition
    },
    [pscustomobject]@{
        Key         = 'window'
        Name        = "External Shares Expiring in $WindowDays Days"
        Description = "Active external shares whose expiry is on or before today + $WindowDays (relative, never stale), soonest first. Includes shares already past their expiry that are still Active. Task 101."
        Filter      = $ActiveCondition + $WindowFilter
    }
)

# ---------------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------------

$token = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $BaseUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Accept             = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
}

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = 'GET', [object]$Body = $null, [hashtable]$ExtraHeaders = @{})
    $h = $headers.Clone()
    $h['Prefer'] = 'odata.maxpagesize=5000'
    foreach ($k in $ExtraHeaders.Keys) { if ($null -eq $ExtraHeaders[$k]) { $h.Remove($k) } else { $h[$k] = $ExtraHeaders[$k] } }   # $null removes a header
    $uri = if ($Endpoint -like 'https://*') { $Endpoint } else { "$Api/$Endpoint" }
    $params = @{ Uri = $uri; Method = $Method; Headers = $h }
    if ($null -ne $Body) { $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10)) }
    try { return Invoke-RestMethod @params }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

# Every page of an OData query ($filter / $select), which pages with @odata.nextLink.
function Get-ODataRows([string]$Endpoint) {
    $rows = [Collections.Generic.List[object]]::new()
    $next = $Endpoint
    while ($next) {
        $page = Invoke-Dv $next
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $link = $page.PSObject.Properties['@odata.nextLink']
        $next = if ($link) { $link.Value } else { $null }
    }
    return , $rows
}

# Every page of a FetchXML query. The Web API does NOT page ?fetchXml= with @odata.nextLink: it returns at most
# `count` rows plus @Microsoft.Dynamics.CRM.morerecords and a paging cookie (URL-encoded twice), and the next page is
# the same fetch with page + 1 and that cookie. A saved view is read through its own fetchxml here rather than
# ?savedQuery=, so one loop pages both.
function Get-FetchRows([string]$FetchXml) {
    $doc = [xml]$FetchXml
    $fetch = $doc.DocumentElement
    $fetch.RemoveAttribute('savedqueryid')        # stamped by the server into a saved view's fetchxml
    $fetch.RemoveAttribute('top')
    $fetch.SetAttribute('count', "$PageSize")
    $prefer = @{ Prefer = 'odata.include-annotations="Microsoft.Dynamics.CRM.fetchxmlpagingcookie,Microsoft.Dynamics.CRM.morerecords"' }
    $rows = [Collections.Generic.List[object]]::new()
    for ($page = 1; ; $page++) {
        if ($page -gt 100000) { throw 'FetchXML paging did not end after 100000 pages.' }
        $fetch.SetAttribute('page', "$page")
        $resp = Invoke-Dv "$EntitySet`?fetchXml=$([uri]::EscapeDataString($fetch.OuterXml))" -ExtraHeaders $prefer
        foreach ($r in @($resp.value)) { $rows.Add($r) }
        $more = $resp.PSObject.Properties['@Microsoft.Dynamics.CRM.morerecords']
        if (-not ($more -and $more.Value)) { break }
        $cookieProp = $resp.PSObject.Properties['@Microsoft.Dynamics.CRM.fetchxmlpagingcookie']
        if ($cookieProp -and $cookieProp.Value) {
            $cookie = ([xml]$cookieProp.Value).DocumentElement.GetAttribute('pagingcookie')
            if ($cookie) { $fetch.SetAttribute('paging-cookie', [uri]::UnescapeDataString([uri]::UnescapeDataString($cookie))) }
        }
    }
    return , $rows
}

function Get-FetchXml([string]$Filter) {
    $attrs = (@('sprk_externalrecordaccessid') + @($Columns.Keys) | ForEach-Object { "<attribute name=""$_"" />" }) -join ''
    return '<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">' +
        "<entity name=""$Table"">$attrs<order attribute=""$ExpiryColumn"" descending=""false"" />" +
        "<filter type=""and"">$Filter</filter></entity></fetch>"
}

function Get-LayoutXml([int]$ObjectTypeCode, [string]$PrimaryName) {
    $cells = ($Columns.Keys | ForEach-Object { "<cell name=""$_"" width=""$($Columns[$_])"" />" }) -join ''
    return "<grid name=""resultset"" object=""$ObjectTypeCode"" jump=""$PrimaryName"" select=""1"" icon=""1"" preview=""1"">" +
        "<row name=""result"" id=""sprk_externalrecordaccessid"">$cells</row></grid>"
}

# A filter's children in canonical form. A nested filter of the SAME type, and a filter with one child, are spliced
# into their parent (neither changes the meaning); any other nested filter stays a group, so (A or B) and C is not
# A or B or C. Siblings are sorted: their order does not change the meaning either.
function Get-FilterParts([Xml.XmlElement]$Node, [string]$Type) {
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($child in $Node.ChildNodes) {
        if ($child -isnot [Xml.XmlElement]) { continue }
        if ($child.LocalName -eq 'condition') {
            $values = @($child.SelectNodes('value') | ForEach-Object { $_.InnerText } | Sort-Object) -join ';'
            $parts.Add(('{0}|{1}|{2}{3}' -f $child.GetAttribute('attribute'), $child.GetAttribute('operator'), $child.GetAttribute('value'), $values))
        } elseif ($child.LocalName -eq 'filter') {
            $t = $child.GetAttribute('type'); if (-not $t) { $t = 'and' }   # FetchXML's default
            $sub = Get-FilterParts $child $t
            if ($t -eq $Type -or $sub.Count -eq 1) { foreach ($s in $sub) { $parts.Add($s) } }
            elseif ($sub.Count -gt 1) { $parts.Add("$t(" + (@($sub | Sort-Object) -join ',') + ')') }
        }
    }
    return , $parts
}

# The meaning of a view: entity, the normalised filter tree, link-entities, the sort, the attributes it needs, the
# columns. Attribute order, whitespace, the savedqueryid the server stamps into fetchxml, and any EXTRA fetch attribute
# (the maker portal adds the primary name) do not change it; a MISSING column does.
function Get-ViewSignature([string]$FetchXml, [string]$LayoutXml) {
    $entity = ([xml]$FetchXml).SelectSingleNode('/fetch/entity')
    $parts = Get-FilterParts $entity 'and'     # assign first: piping the call would sort the wrapped list as ONE object
    $filter = 'and(' + (@($parts | Sort-Object) -join ',') + ')'
    $links = @($entity.SelectNodes('.//link-entity')).Count
    $orders = @($entity.SelectNodes('order') | ForEach-Object {
            $desc = $_.GetAttribute('descending'); if (-not $desc) { $desc = 'false' }        # FetchXML's default
            '{0}|{1}' -f $_.GetAttribute('attribute'), $desc
        })
    $needed = @('sprk_externalrecordaccessid') + @($Columns.Keys)
    $attrs = @($entity.SelectNodes('attribute') | ForEach-Object { $_.GetAttribute('name') } | Where-Object { $needed -contains $_ } | Sort-Object -Unique)
    $cells = @(([xml]$LayoutXml).SelectNodes('//cell') | ForEach-Object { $_.GetAttribute('name') })
    return "entity=$($entity.GetAttribute('name')); filter=$filter; links=$links; order=$($orders -join ','); " +
        "attributes=$($attrs -join ','); cells=$($cells -join ',')"
}

# The expiry as a calendar day. The Web API sends a date-only value as midnight UTC ("2026-12-10T00:00:00Z"); taking
# the UTC date keeps it that day whether PowerShell hands it over as a UTC DateTime or as a string.
function Get-ExpiryDay([object]$Row) {
    $v = $Row.$ExpiryColumn
    if ($null -eq $v -or "$v" -eq '') { return $null }
    return ([datetime]$v).ToUniversalTime().Date
}

# Rows a view returns must be active, sorted by expiry ascending (empty first, as Dataverse sorts NULL), and - for the
# window view - dated on or before today + 30. One day of slack each side: the relative operators use the VIEWING
# user's time zone, this machine may use another.
function Test-ViewRows([string]$Key, [object[]]$Rows, [object[]]$ActiveRows) {
    $gaps = [Collections.Generic.List[string]]::new()
    $activeIds = @{}
    foreach ($a in $ActiveRows) { $activeIds[$a.sprk_externalrecordaccessid] = $a }
    $dates = @($Rows | ForEach-Object { $d = Get-ExpiryDay $_; if ($d) { $d } else { [datetime]::MinValue } })
    for ($i = 1; $i -lt $dates.Count; $i++) {
        if ($dates[$i] -lt $dates[$i - 1]) { $gaps.Add("row $i is not in expiry order ($($dates[$i - 1].ToString('yyyy-MM-dd')) then $($dates[$i].ToString('yyyy-MM-dd')))"); break }
    }
    $notActive = @($Rows | Where-Object { -not $activeIds.ContainsKey($_.sprk_externalrecordaccessid) })
    if ($notActive.Count) { $gaps.Add("$($notActive.Count) row(s) are not Active") }
    $today = (Get-Date).Date
    if ($Key -eq 'all') {
        if ($Rows.Count -ne $ActiveRows.Count) { $gaps.Add("returns $($Rows.Count) rows; $($ActiveRows.Count) rows are Active") }
    } else {
        $late = @($Rows | Where-Object { $d = Get-ExpiryDay $_; -not $d -or $d -gt $today.AddDays($WindowDays + 1) })
        if ($late.Count) { $gaps.Add("$($late.Count) row(s) are undated or expire after today + $WindowDays") }
        $ids = @{}
        foreach ($r in $Rows) { $ids[$r.sprk_externalrecordaccessid] = $true }
        $missed = @($ActiveRows | Where-Object { $d = Get-ExpiryDay $_; $d -and $d -le $today.AddDays($WindowDays - 1) -and -not $ids.ContainsKey($_.sprk_externalrecordaccessid) })
        if ($missed.Count) { $gaps.Add("$($missed.Count) Active row(s) dated on or before today + $($WindowDays - 1) are missing") }
    }
    return , $gaps
}

function Write-Gap([string]$Text) { Write-Host "  GAP  $Text" -ForegroundColor Red; $script:gapCount++ }
function Write-Plan([string]$Text) { Write-Host "  PLAN $Text" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------------------------------------------------
# Metadata: every name the views use must exist on the table, and the expiry must be a date-only column
# ---------------------------------------------------------------------------------------------------------------------

$orgName = (Invoke-Dv 'organizations?$select=name').value[0].name
Write-Host "Environment : $BaseUrl (org '$orgName')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { "APPLY (solution $SolutionUniqueName)" } else { 'DRY RUN (no writes)' }))

$meta = Invoke-Dv "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId,ObjectTypeCode,PrimaryNameAttribute,PrimaryIdAttribute"
$liveAttrs = @{}
foreach ($a in (Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes?`$select=LogicalName,AttributeType").value) { $liveAttrs[$a.LogicalName] = $a.AttributeType }
$missingAttrs = @(@($Columns.Keys) + @('statecode', $meta.PrimaryIdAttribute) | Where-Object { -not $liveAttrs.ContainsKey($_) })
if ($missingAttrs.Count) { throw "Not on $Table in this environment: $($missingAttrs -join ', '). Nothing was written." }
$expiryMeta = Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$ExpiryColumn')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format"
if ($expiryMeta.Format -ne 'DateOnly') { throw "$ExpiryColumn has format '$($expiryMeta.Format)', expected DateOnly; the window boundaries were measured on a date-only column. Nothing was written." }
Write-Host "Metadata    : $($Columns.Count) columns + statecode present; $ExpiryColumn is DateOnly; object type code $($meta.ObjectTypeCode)"

$solutionName = $SolutionUniqueName -replace "'", "''"
$solution = @((Invoke-Dv "solutions?`$select=solutionid,ismanaged&`$filter=uniquename eq '$solutionName'").value)
if ($solution.Count -ne 1 -or $solution[0].ismanaged) { throw "Solution '$SolutionUniqueName' is missing or managed. Nothing was written." }
$membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution[0].solutionid
Write-Host "Solution    : $SolutionUniqueName (table $(if (Test-DvInSolution -Membership $membership -ComponentId $meta.MetadataId) { 'in it' } else { 'NOT in it' }))"

$layout = Get-LayoutXml $meta.ObjectTypeCode $meta.PrimaryNameAttribute
$activeRows = Get-ODataRows "$EntitySet`?`$select=sprk_externalrecordaccessid,$ExpiryColumn&`$filter=statecode eq 0"
Write-Host "Active rows : $($activeRows.Count) (undated: $(@($activeRows | Where-Object { -not $_.$ExpiryColumn }).Count))"

# ---------------------------------------------------------------------------------------------------------------------
# Plan
# ---------------------------------------------------------------------------------------------------------------------

$script:gapCount = 0
$plan = [Collections.Generic.List[object]]::new()
$needsPublish = $false

foreach ($v in $Views) {
    Write-Host "`n[$($v.Name)]"
    $fetch = Get-FetchXml $v.Filter
    $want = Get-ViewSignature $fetch $layout
    $escaped = $v.Name -replace "'", "''"
    $existing = @((Invoke-Dv "savedqueries?`$select=savedqueryid,name,fetchxml,layoutxml&`$filter=returnedtypecode eq '$Table' and querytype eq 0 and name eq '$escaped'").value)

    if ($existing.Count -gt 1) {
        if ($Verify) { Write-Gap "$($existing.Count) public views carry this name"; continue }
        throw "$($existing.Count) public views on $Table are named '$($v.Name)'. Remove the duplicates by hand; nothing was written."
    }

    $item = [pscustomobject]@{ View = $v; Fetch = $fetch; Id = $null; Current = $null; Create = $false; Rewrite = $false; AddToSolution = $false }

    if ($existing.Count -eq 0) {
        if ($Verify) { Write-Gap 'view does not exist'; continue }
        $item.Create = $true
    } else {
        $current = $existing[0]
        $item.Id = $current.savedqueryid
        $item.Current = $current
        $published = Get-ViewSignature $current.fetchxml $current.layoutxml
        # A function, not a collection: it refuses a paging Prefer ("top/paging expression is not supported").
        $pendingRow = Invoke-Dv "savedqueries($($current.savedqueryid))/Microsoft.Dynamics.CRM.RetrieveUnpublished()" -ExtraHeaders @{ Prefer = $null }
        $pending = Get-ViewSignature $pendingRow.fetchxml $pendingRow.layoutxml
        $inSolution = Test-DvInSolution -Membership $membership -ComponentId $current.savedqueryid -TableMetadataId $meta.MetadataId

        if ($Verify) {
            if ($published -ne $want) { Write-Gap "published definition differs`n         have: $published`n         want: $want" }
            if ($pending -ne $published) { Write-Gap "has unpublished changes (pending: $pending)" }
            if (-not $inSolution) { Write-Gap "view $($current.savedqueryid) is not in solution $SolutionUniqueName" }
        } else {
            # The PENDING definition is what a publish would make live, so it is what must match.
            if ($pending -ne $want) { $item.Rewrite = $true; Write-Plan "rewrite $($current.savedqueryid)`n         have: $pending`n         want: $want" }
            elseif ($pending -ne $published) { $needsPublish = $true; Write-Plan 'publish: the pending definition is right but unpublished' }
            if (-not $inSolution) { $item.AddToSolution = $true; Write-Plan "add $($current.savedqueryid) to solution $SolutionUniqueName (component type 26)" }
        }

        if ($published -eq $want) {
            # What users see: run the published definition and check its rows.
            $rows = Get-FetchRows $current.fetchxml
            $rowGaps = Test-ViewRows $v.Key $rows $activeRows
            if ($rowGaps.Count) { foreach ($g in $rowGaps) { Write-Gap $g } }
            elseif ($Verify -or -not ($item.Rewrite -or $item.AddToSolution)) { Write-Host "  OK   $($current.savedqueryid): published definition matches; returns $($rows.Count) rows, all checks pass" -ForegroundColor Green }
        }
    }

    if ($item.Create -or $item.Rewrite) {
        # Run the query about to be written BEFORE writing it.
        $preview = Get-FetchRows $fetch
        $rowGaps = Test-ViewRows $v.Key $preview $activeRows
        if ($rowGaps.Count) { throw "The query for '$($v.Name)' returns the wrong rows: $($rowGaps -join '; '). Nothing was written." }
        if ($item.Create) { Write-Plan "create in $SolutionUniqueName (query runs: $($preview.Count) rows, all checks pass)" }
        else { Write-Host "         new query runs: $($preview.Count) rows, all checks pass" -ForegroundColor Cyan }
    }
    if ($item.Create -or $item.Rewrite -or $item.AddToSolution) { $plan.Add($item) }
}

if ($Verify) {
    if ($script:gapCount -eq 0) { Write-Host "`nVERIFY PASS: both views exist on $Table, are published, are in $SolutionUniqueName and return the right rows." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: $($script:gapCount) gap(s) above." -ForegroundColor Red
    exit 1
}
if ($script:gapCount -gt 0) { Write-Host "`n$($script:gapCount) gap(s) above in a published view whose definition already matches. Nothing was written." -ForegroundColor Red; exit 1 }
if ($plan.Count -eq 0 -and -not $needsPublish) { Write-Host "`nNothing to change." -ForegroundColor Green; exit 0 }
if (-not $Apply) { Write-Host "`nDRY RUN: $($plan.Count) view(s) to write$(if ($needsPublish) { ', a publish pending' }). Re-run with -Apply." -ForegroundColor Cyan; exit 0 }

# ---------------------------------------------------------------------------------------------------------------------
# Apply
# ---------------------------------------------------------------------------------------------------------------------

$toRewrite = @($plan | Where-Object Rewrite)
if ($toRewrite.Count) {
    New-Item -ItemType Directory -Force -Path $SnapshotDir | Out-Null
    $snapPath = Join-Path $SnapshotDir ("external-share-expiry-views-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
    $toRewrite | ForEach-Object { $_.Current | Select-Object savedqueryid, name, fetchxml, layoutxml } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $snapPath -Encoding UTF8
    Write-Host "Snapshot    : $snapPath"
}

$createdIds = [Collections.Generic.List[string]]::new()
foreach ($p in $plan) {
    $body = @{ name = $p.View.Name; description = $p.View.Description; fetchxml = $p.Fetch; layoutxml = $layout }
    if ($p.Create) {
        $body.returnedtypecode = $Table
        $body.querytype = 0
        $body.isdefault = $false
        $body.isquickfindquery = $false
        $created = Invoke-Dv 'savedqueries' 'POST' $body @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName; Prefer = 'return=representation' }
        $createdIds.Add("$($created.savedqueryid)")
        Write-Host "  created '$($p.View.Name)' ($($created.savedqueryid)) in $SolutionUniqueName" -ForegroundColor Green
        continue
    }
    if ($p.Rewrite) {
        Invoke-Dv "savedqueries($($p.Id))" 'PATCH' $body @{ 'If-Match' = '*' } | Out-Null    # update only, never an upsert
        Write-Host "  rewrote '$($p.View.Name)' ($($p.Id))" -ForegroundColor Green
    }
    if ($p.AddToSolution) {
        Invoke-Dv 'AddSolutionComponent' 'POST' @{ ComponentId = $p.Id; ComponentType = 26; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Write-Host "  added '$($p.View.Name)' ($($p.Id)) to $SolutionUniqueName" -ForegroundColor Green
    }
}

try {
    Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
} catch {
    # A rewrite left unpublished is found by a re-run (pending != published) and published. A view CREATED here is
    # expected to be returned by savedqueries before publish (a new record, not an update of a published one), so a
    # re-run finds it rather than creating it twice - but that was not observed live (no write was allowed while this
    # script was written), so the ids are printed and -Verify names any view that is still missing or unpublished.
    throw ("Wrote the views but the publish failed: $($_.Exception.Message). Created: $(if ($createdIds.Count) { $createdIds -join ', ' } else { 'none' }). " +
        "Re-run -Apply (it publishes pending changes), or publish the table in the maker portal, then run -Verify.")
}
Write-Host "Published $Table. Verifying..."

& $PSCommandPath -EnvironmentUrl $BaseUrl -SolutionUniqueName $SolutionUniqueName -Verify
exit $LASTEXITCODE
