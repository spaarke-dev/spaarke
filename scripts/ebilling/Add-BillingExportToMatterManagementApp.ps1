<#
.SYNOPSIS
    eBilling R1 (task 153 follow-up) - verify / drift tool, and a targeted ADDITIVE-ONLY fix, for the sprk_billingexport table in the
    Matter Management model-driven app on dev: app component + sitemap subarea. Dev only.

.DESCRIPTION
    HISTORY. On 2026-11-03 sprk_billingexport was not a component of any model-driven app on spaarkedev1 (RetrieveAppComponents for Matter
    Management, appmoduleid 729afe6d-ca73-f011-b4cb-6045bdd8b757, uniquename sprk_MatterManagement, had no row for it) and the sitemap had
    no subarea for it, although source had both (task 029, ac920d4184). Dev lost them when every app component row was rewritten at once
    on 2026-10-21 02:29:29Z. At 22:13:29Z the same day the table was added through the app designer (this script was still being written
    and had written nothing): the designer added the entity row for sprk_billingexport, sprk_ebillingsetting and sprk_ebillingoverride and
    one sitemap subarea each, under area_finance. Source was then synced to dev (AppModule.xml, AppModuleSiteMap.xml and their _managed
    copies): see projects/ebilling-r1/notes/153-wp6-deploy.md.

    WHAT IT IS NOW.
      -Verify    reads only; four checks (below); also prints the batch form facts and the source-vs-dev drift for this table.
      -WhatIf    (default) reads only; prints what -Apply would do. On a healthy app that is NOTHING.
      -Apply     only if something is missing again, and only additively. With the table and any subarea for it present it plans and
                 writes nothing (and publishes nothing).

    WHY TARGETED, NOT AN IMPORT. Other projects change this app on dev. Importing our AppModule.xml or sitemap wholesale could remove
    their components. The script therefore only ADDS, and refuses if it cannot prove that:
      1  Components   AddAppComponents (AppId + Components) with the sprk_billingexport entity row. sprk_communication (the AP emails
                      subgrid target) is added too if it is not a component. Components already in the app are skipped.
                      Forms and views: the app lists forms explicitly for some tables (sprk_invoice, sprk_communication) but no view row for
                      any table, and the ebilling tables added in the same wave (sprk_invoicesubmission, sprk_invoicecorrection), the
                      designer's own add of sprk_billingexport and source are entity-row only (ValidateApp: "App users will see all forms
                      and views"). An explicit form row LIMITS the table to the listed forms, so the main form bd0020ec and the source
                      views are listed in the plan but added as rows only with -ExplicitFormsAndViews.
      2  Sitemap      Matched by ENTITY, never by subarea Id: if the sitemap has any subarea for sprk_billingexport, whatever its Id or
                      group, nothing is inserted, so a second Billing Exports entry is never added. Only when none exists, ONE SubArea is
                      inserted, copied exactly from source (the SubArea with Entity sprk_billingexport in AppModuleSiteMap.xml), into the
                      same Group as source after the same predecessor; if that group is absent on dev, next to the Invoices subarea.
                      The edit is a text splice, then asserted twice before anything is written: (a) byte level - removing the inserted
                      text gives back the before XML exactly; (b) node level - the before and after XML are parsed and compared node by
                      node, and the only difference allowed is that one added SubArea. Anything else stops the run (exit 2).
                      The before XML is saved byte for byte under a unique timestamped name (rollback copy).
      3  Publish      PublishXml for the app module and the sitemap only (never the tenant-wide publish); the ParameterXml comes from
                      the repo's one builder, New-PublishParameterXml (scripts/lib/Publish-SolutionComponents.ps1).
      4  ValidateApp  Run after the publish; its result is printed and saved.

    KNOWN HAZARD. AddAppComponents has answered 204 and added nothing on this environment before (notes/029 section 7A, uac-r2
    batch5-live-gates-2026-10-08). So the script reads the result back (RetrieveAppComponents), publishes the app module once if the
    row is not visible yet, reads again, and STOPS BEFORE THE SITEMAP is touched if the table is still missing (exit 3, with the manual
    app-designer step). Nothing else was changed in that case.

    SAFETY GATES (all before the first write): one mode only; -Apply needs -OwnerApproval; dev only; the app and its sitemap are the
    expected records; no pending (unpublished) change on the app module or its settings (publishing the app would publish them too);
    the sitemap edit passes both assertions; the component set and the sitemap are re-read just before each write and must be unchanged
    since the plan was made; the sitemap PATCH carries If-Match with the etag that was read. Every write goes through one helper that
    allows only AddAppComponents, the scoped PublishXml and the PATCH of one sitemap record.

.PARAMETER WhatIf
    The default when no mode is given. Reads only: prints the planned components, the sitemap diff and the publish plan, saves the
    before-XML copy locally, runs ValidateApp for a baseline. Writes nothing to dev.
.PARAMETER Apply
    Writes. Refused unless -OwnerApproval is also given.
.PARAMETER OwnerApproval
    Required with -Apply. The owner has approved this change on dev.
.PARAMETER Verify
    Reads only. Exit 0 when (1) RetrieveAppComponents includes sprk_billingexport, (2) sprk_invoice is still a component (control),
    (3) ValidateApp has no error and no Billing Export issue (result printed), (4) the published sitemap has a subarea for the table.
.PARAMETER ExplicitFormsAndViews
    Also add the main form bd0020ec and the table's views that exist on dev and in source as explicit app components. Default: not
    added (entity row only, like the sibling ebilling tables and source); an explicit form row limits the table to the listed forms.
.PARAMETER BackupDir
    Where the before-XML, its sidecar and the ValidateApp output are saved. Default scripts/logs (gitignored).

.EXAMPLE
    pwsh scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1                  # WhatIf (default)
    pwsh scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1 -Verify
    pwsh scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1 -Apply -OwnerApproval

.NOTES
    Project: ebilling-r1. Exit codes: 0 = ok / verified, 1 = error or Verify failed, 2 = refused (nothing written),
    3 = applied but the table is still not an app component (manual step; sitemap not written).
    Pattern: scripts/ebilling/Deploy-EbillingFormsViews.ps1 (Sync-AppTables, Sync-SiteMap) and EbillingSchema.psm1 (az token, Invoke-Dv).
    Tests: tests/scripts/Add-BillingExportToMatterManagementApp.Tests.ps1 (offline; the decision functions are read out of this file).
#>
using module .\EbillingSchema.psm1

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$WhatIf,
    [switch]$Apply,
    [switch]$OwnerApproval,
    [switch]$Verify,
    [switch]$ExplicitFormsAndViews,
    [string]$BackupDir = (Join-Path $PSScriptRoot '..' 'logs')
)

$ErrorActionPreference = 'Stop'

# The repo's one scoped-publish builder (task 130, D-83). Only New-PublishParameterXml is used here.
. (Join-Path $PSScriptRoot '..' 'lib' 'Publish-SolutionComponents.ps1')

# ---------------------------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------------------------
$AppUniqueName = 'sprk_MatterManagement'
$AppId = '729afe6d-ca73-f011-b4cb-6045bdd8b757'
$Table = 'sprk_billingexport'
$LinkedTable = 'sprk_communication'      # the AP emails subgrid's target
$ControlTable = 'sprk_invoice'           # must still be an app component after the change
$MainFormId = 'bd0020ec-1912-4f2d-b3e6-a6315f8b9e48'
$InvoiceEntity = 'sprk_invoice'
$SubscriptionRelationship = 'sprk_communication_billingexport_billingexport'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$SourceSiteMapPath = Join-Path $RepoRoot 'src/dataverse/solutions/SpaarkeMaster/AppModuleSiteMaps/sprk_MatterManagement/AppModuleSiteMap.xml'
$SourceAppModulePath = Join-Path $RepoRoot 'src/dataverse/solutions/SpaarkeMaster/AppModules/sprk_MatterManagement/AppModule.xml'
$SourceViewsDir = Join-Path $RepoRoot 'src/dataverse/solutions/SpaarkeMaster/Entities/sprk_BillingExport/SavedQueries'

# ---------------------------------------------------------------------------------------------------------------------------------
# Pure decision functions (no Dataverse access; read out of this file by the Pester tests)
# ---------------------------------------------------------------------------------------------------------------------------------

# One mode only; -Apply needs -OwnerApproval. Returns @{ Mode; Error }.
function Resolve-InvocationMode {
    param([bool]$WhatIfSwitch, [bool]$ApplySwitch, [bool]$VerifySwitch, [bool]$OwnerApprovalSwitch)
    $chosen = @(@($WhatIfSwitch, $ApplySwitch, $VerifySwitch) | Where-Object { $_ }).Count
    if ($chosen -gt 1) { return @{ Mode = $null; Error = 'Choose ONE of -WhatIf, -Apply, -Verify.' } }
    if ($ApplySwitch -and -not $OwnerApprovalSwitch) {
        return @{ Mode = $null; Error = '-Apply refuses to run without -OwnerApproval. Nothing was read or written.' }
    }
    if ($ApplySwitch) { return @{ Mode = 'APPLY'; Error = $null } }
    if ($VerifySwitch) { return @{ Mode = 'VERIFY'; Error = $null } }
    return @{ Mode = 'WHATIF'; Error = $null }
}

# Index of the end of a <SubArea Id="..."> element in raw XML text (quote-aware scan; the element is self-closing or has children).
function Find-SubAreaRawEnd {
    param([string]$Xml, [string]$SubAreaId)
    $needle = '<SubArea Id="' + $SubAreaId + '"'
    $start = $Xml.IndexOf($needle, [System.StringComparison]::Ordinal)
    if ($start -lt 0) { throw "SubArea '$SubAreaId' was not found in the sitemap text (it is not serialised as <SubArea Id=`"...`"). The script does not re-serialise the sitemap." }
    if ($Xml.IndexOf($needle, $start + 1, [System.StringComparison]::Ordinal) -ge 0) { throw "SubArea '$SubAreaId' appears more than once in the sitemap text." }
    $quote = [char]0
    $gt = -1
    for ($i = $start; $i -lt $Xml.Length; $i++) {
        $c = $Xml[$i]
        if ($quote -ne [char]0) { if ($c -eq $quote) { $quote = [char]0 }; continue }
        if ($c -eq '"' -or $c -eq "'") { $quote = $c; continue }
        if ($c -eq '>') { $gt = $i; break }
    }
    if ($gt -lt 0) { throw "SubArea '$SubAreaId' has no closing '>' in the sitemap text." }
    if ($Xml[$gt - 1] -eq '/') { return $gt + 1 }
    $close = $Xml.IndexOf('</SubArea>', $gt, [System.StringComparison]::Ordinal)
    if ($close -lt 0) { throw "SubArea '$SubAreaId' has no </SubArea> in the sitemap text." }
    return $close + '</SubArea>'.Length
}

# Index of the closing tag of a <Group Id="..."> element (Groups do not nest).
function Find-GroupRawClose {
    param([string]$Xml, [string]$GroupId)
    $needle = '<Group Id="' + $GroupId + '"'
    $start = $Xml.IndexOf($needle, [System.StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Group '$GroupId' was not found in the sitemap text." }
    $close = $Xml.IndexOf('</Group>', $start, [System.StringComparison]::Ordinal)
    if ($close -lt 0) { throw "Group '$GroupId' has no </Group> in the sitemap text." }
    return $close
}

function Read-XmlPreserving([string]$Text) {
    $d = [System.Xml.XmlDocument]::new()
    $d.PreserveWhitespace = $true
    $d.LoadXml($Text.TrimStart([char]0xFEFF))
    return $d
}

# Every subarea of the sitemap for one entity, whatever its Id or group: @{ Id; AreaId; GroupId; Xml }.
function Get-EntitySubAreas {
    param([string]$Xml, [string]$Entity)
    $doc = Read-XmlPreserving $Xml
    return @($doc.SelectNodes("//SubArea[@Entity='$Entity']") | ForEach-Object {
            [pscustomobject]@{ Id = $_.GetAttribute('Id'); AreaId = $_.ParentNode.ParentNode.GetAttribute('Id'); GroupId = $_.ParentNode.GetAttribute('Id'); Xml = $_.OuterXml }
        })
}

# The subarea for an entity in the SOURCE sitemap file: exactly one is expected. Returns Found, Id, AreaId, GroupId, PredecessorId (the
# SubArea before it in its group, or $null), Xml (the element's own text, copied from the file), Line, Detail.
function Get-SourceSubArea {
    param([string]$Path, [string]$Entity)
    if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]@{ Found = $false; Detail = "source file not found: $Path" } }
    $text = [System.IO.File]::ReadAllText($Path)
    $doc = Read-XmlPreserving $text
    $nodes = @($doc.SelectNodes("//SubArea[@Entity='$Entity']"))
    if ($nodes.Count -ne 1) { return [pscustomobject]@{ Found = $false; Detail = "source sitemap has $($nodes.Count) SubArea elements for $Entity (expected exactly 1)" } }
    $n = $nodes[0]
    $m = [regex]::Matches($text, '<SubArea\b[^<>]*\bEntity="' + [regex]::Escape($Entity) + '"[^<>]*/>')
    if ($m.Count -ne 1) { return [pscustomobject]@{ Found = $false; Detail = "the source SubArea for $Entity is not a single self-closing element in the file text" } }
    $pred = $null
    for ($s = $n.PreviousSibling; $s; $s = $s.PreviousSibling) { if ($s.NodeType -eq 'Element' -and $s.LocalName -eq 'SubArea') { $pred = $s.GetAttribute('Id'); break } }
    $line = ($text.Substring(0, $m[0].Index) -split "`n").Count
    return [pscustomobject]@{ Found = $true; Id = $n.GetAttribute('Id'); AreaId = $n.ParentNode.ParentNode.GetAttribute('Id'); GroupId = $n.ParentNode.GetAttribute('Id')
        PredecessorId = $pred; Xml = $m[0].Value; Line = $line; Detail = "$(Split-Path -Leaf $Path):$line, SubArea $($n.GetAttribute('Id')) in $($n.ParentNode.ParentNode.GetAttribute('Id'))/$($n.ParentNode.GetAttribute('Id'))" }
}

# Does the source AppModule.xml list the entity as a component (type 1)? Returns Found, Line.
function Get-SourceAppModuleEntry {
    param([string]$Path, [string]$Entity)
    if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]@{ Found = $false; Line = 0 } }
    $text = [System.IO.File]::ReadAllText($Path)
    $m = [regex]::Match($text, '<AppModuleComponent type="1" schemaName="' + [regex]::Escape($Entity) + '" />')
    return [pscustomobject]@{ Found = $m.Success; Line = $(if ($m.Success) { ($text.Substring(0, $m.Index) -split "`n").Count } else { 0 }) }
}

# Plans the sitemap change. Matched by ENTITY: if any subarea for the entity exists (any Id, any group) nothing is planned (Present).
# Otherwise builds the after-XML by a TEXT splice of the source element into the source group after the source predecessor (else after
# the Invoices subarea of that group, else at the end of the group); if the group is absent, next to the Invoices subarea.
# Returns Present, Existing, Conflict (text when no safe plan exists), AfterXml, InsertIndex, GroupId, Anchor, Placement.
function New-SiteMapPlan {
    param(
        [Parameter(Mandatory)][string]$BeforeXml,
        [Parameter(Mandatory)][string]$SubAreaXml,
        [Parameter(Mandatory)][string]$SubAreaId,
        [Parameter(Mandatory)][string]$Entity,
        [Parameter(Mandatory)][string]$SourceGroupId,
        [string]$SourcePredecessorId,
        [Parameter(Mandatory)][string]$InvoiceEntity
    )
    $none = [pscustomobject]@{ Present = $false; Existing = @(); Conflict = $null; AfterXml = $null; InsertIndex = -1; GroupId = $null; Anchor = $null; Placement = $null }
    $existing = @(Get-EntitySubAreas -Xml $BeforeXml -Entity $Entity)
    if ($existing.Count -gt 0) { $none.Present = $true; $none.Existing = $existing; $none.GroupId = $existing[0].GroupId; return $none }
    $doc = Read-XmlPreserving $BeforeXml
    $clash = $doc.SelectSingleNode("//SubArea[@Id='$SubAreaId']")
    if ($clash) { $none.Conflict = "SubArea Id '$SubAreaId' is already used on dev by a subarea for '$($clash.GetAttribute('Entity'))$($clash.GetAttribute('Url'))', not for $Entity. Owner decision needed."; return $none }
    $group = $doc.SelectSingleNode("//Group[@Id='$SourceGroupId']")
    if ($group) {
        $anchorNode = $null
        if ($SourcePredecessorId) { $anchorNode = $group.SelectSingleNode("SubArea[@Id='$SourcePredecessorId']") }
        if (-not $anchorNode) { $anchorNode = $group.SelectSingleNode("SubArea[@Entity='$InvoiceEntity']") }
        if ($anchorNode) {
            $idx = Find-SubAreaRawEnd -Xml $BeforeXml -SubAreaId $anchorNode.GetAttribute('Id')
            $placement = "group $SourceGroupId (as in source), right after SubArea $($anchorNode.GetAttribute('Id'))"
            $anchor = $anchorNode.GetAttribute('Id')
        } else {
            $idx = Find-GroupRawClose -Xml $BeforeXml -GroupId $SourceGroupId
            $placement = "group $SourceGroupId (as in source), at the end of the group (neither the source predecessor nor an Invoices subarea is in it)"
            $anchor = $null
        }
        $gid = $SourceGroupId
    } else {
        $inv = $doc.SelectSingleNode("//SubArea[@Entity='$InvoiceEntity']")
        if (-not $inv) { $none.Conflict = "Neither group '$SourceGroupId' nor a SubArea for $InvoiceEntity exists in the sitemap: no safe place for the new subarea. Owner decision needed."; return $none }
        $idx = Find-SubAreaRawEnd -Xml $BeforeXml -SubAreaId $inv.GetAttribute('Id')
        $placement = "group $SourceGroupId is absent on dev: next to the Invoices subarea $($inv.GetAttribute('Id')) in group $($inv.ParentNode.GetAttribute('Id'))"
        $anchor = $inv.GetAttribute('Id')
        $gid = $inv.ParentNode.GetAttribute('Id')
    }
    $none.AfterXml = $BeforeXml.Insert($idx, $SubAreaXml); $none.InsertIndex = $idx; $none.GroupId = $gid; $none.Anchor = $anchor; $none.Placement = $placement
    return $none
}

# One canonical line per element, document order: path (ids where there are ids) | sorted attributes | direct text.
function Add-NodeLines {
    param($Node, [string]$Path, $Lines)
    $name = $Node.LocalName
    $id = $Node.GetAttribute('Id')
    if ($id) { $seg = "$name[$id]" }
    else {
        $k = 0
        for ($s = $Node.PreviousSibling; $s; $s = $s.PreviousSibling) { if ($s.NodeType -eq 'Element' -and $s.LocalName -eq $name) { $k++ } }
        $seg = "$name#$k"
    }
    $p = "$Path/$seg"
    $attrs = @($Node.Attributes | Sort-Object -Property Name -CaseSensitive | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ';'
    $text = (@($Node.ChildNodes | Where-Object { $_.NodeType -in 'Text', 'CDATA' } | ForEach-Object { $_.Value }) -join '') -replace '\s+', ' '
    $Lines.Add("$p | $attrs | $($text.Trim())")
    foreach ($c in $Node.ChildNodes) { if ($c.NodeType -eq 'Element') { Add-NodeLines -Node $c -Path $p -Lines $Lines } }
}

function Get-SiteMapNodeLines([string]$Xml) {
    $doc = Read-XmlPreserving $Xml
    $lines = [System.Collections.Generic.List[string]]::new()
    Add-NodeLines -Node $doc.DocumentElement -Path '' -Lines $lines
    return , $lines.ToArray()
}

# Node-by-node comparison. The only allowed difference: ONE added line whose path contains SubArea[<AddedSubAreaId>].
function Compare-SiteMapNodes {
    param([string[]]$BeforeLines, [string[]]$AfterLines, [string]$AddedSubAreaId)
    $marker = "SubArea[$AddedSubAreaId] |"
    $i = 0; $j = 0; $added = @(); $unexpected = @()
    while ($i -lt $BeforeLines.Count -or $j -lt $AfterLines.Count) {
        if ($i -lt $BeforeLines.Count -and $j -lt $AfterLines.Count -and $BeforeLines[$i] -ceq $AfterLines[$j]) { $i++; $j++; continue }
        if ($j -lt $AfterLines.Count -and $added.Count -eq 0 -and $AfterLines[$j].Contains($marker)) { $added += $AfterLines[$j]; $j++; continue }
        $unexpected += [pscustomobject]@{
            Before = $(if ($i -lt $BeforeLines.Count) { $BeforeLines[$i] } else { '<end of before>' })
            After  = $(if ($j -lt $AfterLines.Count) { $AfterLines[$j] } else { '<end of after>' })
        }
        break
    }
    return [pscustomobject]@{ BeforeCount = $BeforeLines.Count; AfterCount = $AfterLines.Count; Added = $added; Unexpected = $unexpected }
}

# The additive-only proof. Ok only when (a) the after text is the before text plus the one inserted element, and (b) the parsed
# before / after documents differ by exactly that one added SubArea. Problems lists every reason it is not.
function Assert-SiteMapAdditiveOnly {
    param([string]$BeforeXml, [string]$AfterXml, [string]$SubAreaXml, [string]$SubAreaId, [int]$InsertIndex)
    $problems = [System.Collections.Generic.List[string]]::new()
    $len = $SubAreaXml.Length
    if ($InsertIndex -lt 0 -or $InsertIndex + $len -gt $AfterXml.Length) { $problems.Add("insert index $InsertIndex is outside the after text") }
    elseif ($AfterXml.Length -ne $BeforeXml.Length + $len) { $problems.Add("after text is $($AfterXml.Length) chars, expected $($BeforeXml.Length + $len)") }
    else {
        if ($AfterXml.Substring($InsertIndex, $len) -cne $SubAreaXml) { $problems.Add('the text at the insert index is not the SubArea element') }
        if ($AfterXml.Remove($InsertIndex, $len) -cne $BeforeXml) { $problems.Add('removing the inserted element does not give back the before XML byte for byte') }
    }
    $cmp = $null
    try {
        $cmp = Compare-SiteMapNodes -BeforeLines (Get-SiteMapNodeLines $BeforeXml) -AfterLines (Get-SiteMapNodeLines $AfterXml) -AddedSubAreaId $SubAreaId
        if ($cmp.Unexpected.Count -gt 0) { $problems.Add("node difference other than the added SubArea: before [$($cmp.Unexpected[0].Before)] vs after [$($cmp.Unexpected[0].After)]") }
        if ($cmp.Added.Count -ne 1) { $problems.Add("expected exactly 1 added node, found $($cmp.Added.Count)") }
        if ($cmp.AfterCount -ne $cmp.BeforeCount + 1) { $problems.Add("node count $($cmp.BeforeCount) -> $($cmp.AfterCount), expected +1") }
    } catch { $problems.Add("the XML could not be compared node by node: $($_.Exception.Message)") }
    return [pscustomobject]@{ Ok = ($problems.Count -eq 0); Problems = @($problems); Compare = $cmp }
}

# Components from RetrieveAppComponents rows -> lower-case id sets per component type (1 table, 60 form, 26 view, 62 sitemap).
function Group-AppComponents($Rows) {
    $sets = @{}
    foreach ($r in @($Rows)) {
        $t = [int]$r.componenttype
        if (-not $sets.ContainsKey($t)) { $sets[$t] = [System.Collections.Generic.HashSet[string]]::new() }
        [void]$sets[$t].Add(("$($r.objectid)").ToLowerInvariant())
    }
    return $sets
}

function Test-HasComponent($Sets, [int]$Type, [string]$Id) {
    return $Sets.ContainsKey($Type) -and $Sets[$Type].Contains($Id.ToLowerInvariant())
}

# ValidateApp response -> summary. Clean = ValidationSuccess, no Error issue, and no Billing Export issue other than the informational
# "doesn't reference a form or view" (the platform's own wording for an entity row without form / view rows).
function Get-ValidateAppSummary($Response) {
    $v = $Response.AppValidationResponse
    $issues = @($v.ValidationIssueList)
    $text = { param($i) "$($i.DisplayName) $($i.Message) " + ((@($i.RequiredComponents) | ForEach-Object { "$($_.SchemaName) $($_.DisplayName)" }) -join ' ') }
    $info = @($issues | Where-Object { "$($_.Message)" -match 'reference a form or view' })
    $errors = @($issues | Where-Object { "$($_.ErrorType)" -eq 'Error' })
    $billing = @($issues | Where-Object { ((& $text $_) -match 'billingexport|Billing Export') -and ("$($_.Message)" -notmatch 'reference a form or view') })
    $other = @($issues | Where-Object { "$($_.Message)" -notmatch 'reference a form or view' })
    return [pscustomobject]@{
        Success = [bool]$v.ValidationSuccess; Total = $issues.Count; Errors = $errors; InfoNoFormView = $info; Other = $other; BillingIssues = $billing
        Clean = ([bool]$v.ValidationSuccess -and $errors.Count -eq 0 -and $billing.Count -eq 0)
    }
}

# The set of issue keys, to tell NEW issues from the ones that were already there (baseline taken before the change).
function Get-IssueKeys($Response) {
    return @(@($Response.AppValidationResponse.ValidationIssueList) | ForEach-Object { "$($_.ErrorType)|$($_.ComponentType)|$($_.DisplayName)|$($_.Message)" })
}

# A scoped PublishXml parameter: the app module, and the sitemap when one is given. Built by the repo's one builder
# (New-PublishParameterXml, scripts/lib/Publish-SolutionComponents.ps1), which validates the ids and never builds an empty document.
function New-ScopedPublishXml {
    param([Parameter(Mandatory)][string]$AppId, [string]$SitemapId)
    if ($SitemapId) { return New-PublishParameterXml -AppModules $AppId -SiteMaps $SitemapId }
    return New-PublishParameterXml -AppModules $AppId
}

# The only writes this script may make: AddAppComponents, PublishXml scoped to app modules / sitemaps, PATCH of one sitemap record.
function Test-AllowedWrite {
    param([string]$Path, [string]$Method, $Body)
    if ($Method -eq 'POST' -and $Path -eq 'AddAppComponents') { return $true }
    if ($Method -eq 'POST' -and $Path -eq 'PublishXml') {
        return ("$($Body.ParameterXml)" -match '^<importexportxml>(<sitemaps>(<sitemap>\{[0-9a-fA-F-]{36}\}</sitemap>)+</sitemaps>)?(<appmodules>(<appmodule>\{[0-9a-fA-F-]{36}\}</appmodule>)+</appmodules>)?</importexportxml>$') -and
        ("$($Body.ParameterXml)" -match '<appmodule>|<sitemap>')
    }
    if ($Method -eq 'PATCH' -and $Path -match '^sitemaps\([0-9a-fA-F-]{36}\)$') { return (@($Body.Keys) -join ',') -eq 'sitemapxml' }
    return $false
}

# A byte-for-byte copy of the before XML under a unique timestamped name, plus a sidecar that records how to restore it.
function Save-SiteMapBackup {
    param([string]$Dir, [string]$UniqueName, [string]$SitemapId, [string]$Etag, [string]$ModifiedOn, [string]$Xml, [string]$Environment)
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + 'Z'
    $suffix = ([guid]::NewGuid().ToString('N')).Substring(0, 8)
    $path = Join-Path (Resolve-Path $Dir).Path "sitemap-$UniqueName-before-$stamp-$suffix.xml"
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Xml)
    $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)   # CreateNew: never overwrites
    try { $fs.Write($bytes, 0, $bytes.Length) } finally { $fs.Dispose() }
    $sha = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($bytes)).Replace('-', '').ToLowerInvariant()
    $back = [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($path))
    if ([System.BitConverter]::ToString($back).Replace('-', '').ToLowerInvariant() -ne $sha) { throw "The saved before-XML copy $path does not read back identical." }
    $side = [ordered]@{ environment = $Environment; sitemapnameunique = $UniqueName; sitemapid = $SitemapId; etag = $Etag; modifiedon = $ModifiedOn
        savedAtUtc = [DateTime]::UtcNow.ToString('o'); sha256 = $sha; bytes = $bytes.Length; encoding = 'UTF-8 without BOM'
        restore = "PATCH sitemaps($SitemapId) { sitemapxml: <contents of the .xml file> }, then PublishXml for the app module and this sitemap only" }
    ($side | ConvertTo-Json) | Set-Content -LiteralPath ($path + '.json') -Encoding UTF8
    return [pscustomobject]@{ Path = $path; Sidecar = $path + '.json'; Sha256 = $sha; Bytes = $bytes.Length }
}

# Everything the script writes to Dataverse passes through this ONE helper, which refuses outside -Apply -OwnerApproval and refuses
# any call that is not one of the three writes above.
function Invoke-DvWrite {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][ValidateSet('POST', 'PATCH')][string]$Method, [Parameter(Mandatory)]$Body, [hashtable]$Headers = @{})
    if ($script:Mode -ne 'APPLY' -or -not $script:OwnerApprovalGiven) { throw "Write refused: $Method $Path outside -Apply with -OwnerApproval." }
    if (-not (Test-AllowedWrite -Path $Path -Method $Method -Body $Body)) { throw "Write refused: $Method $Path is not one of the writes this script is allowed to make." }
    return Invoke-Dv -Path $Path -Method $Method -Body $Body -ExtraHeaders $Headers
}

# The guard comes before any Dataverse access: a refused invocation reads nothing and writes nothing.
$inv = Resolve-InvocationMode -WhatIfSwitch ([bool]$WhatIf) -ApplySwitch ([bool]$Apply) -VerifySwitch ([bool]$Verify) -OwnerApprovalSwitch ([bool]$OwnerApproval)
if ($inv.Error) { Write-Host "REFUSED: $($inv.Error)" -ForegroundColor Red; exit 2 }
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($EnvironmentUrl -notmatch 'spaarkedev1') { Write-Host "REFUSED: dev only in R1 ($EnvironmentUrl)." -ForegroundColor Red; exit 2 }
$script:Mode = $inv.Mode
$script:OwnerApprovalGiven = [bool]$OwnerApproval
Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:($script:Mode -ne 'APPLY') | Out-Null

# ---------------------------------------------------------------------------------------------------------------------------------
# Read helpers (Dataverse, read only)
# ---------------------------------------------------------------------------------------------------------------------------------
function Write-Section([string]$Text) { Write-Host ""; Write-Host $Text -ForegroundColor Cyan }
function Write-Ascii([string]$Text) { return ($Text -replace "[$([char]0x2018)$([char]0x2019)]", "'" -replace "[$([char]0x201C)$([char]0x201D)]", '"' -replace [char]0x2013, '-') }

function Get-AppComponentRows { return @((Invoke-Dv -Path "RetrieveAppComponents(AppModuleId=$AppId)").value) }

function Get-AppSiteMap {
    $r = @((Invoke-Dv -Path "sitemaps?`$select=sitemapid,sitemapnameunique,modifiedon&`$filter=sitemapnameunique eq '$AppUniqueName'").value)
    if ($r.Count -ne 1) { throw [EscalationException]::new("Expected exactly one sitemap with sitemapnameunique '$AppUniqueName', found $($r.Count).") }
    # The etag is only on a single-record read.
    $one = Invoke-Dv -Path "sitemaps($($r[0].sitemapid))?`$select=sitemapxml,modifiedon"
    return [pscustomobject]@{ Id = $r[0].sitemapid; Xml = [string]$one.sitemapxml; Etag = $one.'@odata.etag'; ModifiedOn = $one.modifiedon.ToString('o') }
}

# Pending (unpublished) changes on the app module and its settings. Publishing the app publishes ALL of them.
function Get-AppPending {
    $out = @()
    $pub = Invoke-Dv -Path "appmodules($AppId)?`$select=modifiedon"
    $unp = Invoke-Dv -Path "appmodules($AppId)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=modifiedon"
    if ($pub.modifiedon -ne $unp.modifiedon) { $out += "app module (published modifiedon $($pub.modifiedon.ToString('o')), unpublished $($unp.modifiedon.ToString('o')))" }
    $q = "`$select=appsettingid,displayname,modifiedon&`$filter=_parentappmoduleid_value eq $AppId"
    $p = @{}; foreach ($s in @((Invoke-Dv -Path "appsettings?$q").value)) { $p["$($s.appsettingid)"] = $s.modifiedon }
    foreach ($s in @((Invoke-Dv -Path "appsettings/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()?$q").value)) {
        if (-not $p.ContainsKey("$($s.appsettingid)") -or $p["$($s.appsettingid)"] -ne $s.modifiedon) { $out += "app setting $($s.displayname) ($($s.appsettingid))" }
    }
    return $out
}

function Get-TableMetadataId([string]$LogicalName) { return (Invoke-Dv -Path "EntityDefinitions(LogicalName='$LogicalName')?`$select=MetadataId").MetadataId }

function Write-ValidateAppSummary($Response, $Summary, [string]$RawPath) {
    Write-Host ("  ValidateApp: ValidationSuccess={0}; issues={1} (errors {2}; informational 'does not reference a form or view' {3}; others {4})" -f $Summary.Success, $Summary.Total, $Summary.Errors.Count, $Summary.InfoNoFormView.Count, $Summary.Other.Count)
    foreach ($i in @($Summary.Errors)) {
        Write-Host ("    ERROR   {0} | type {1} | {2}" -f $i.DisplayName, $i.ComponentType, (Write-Ascii "$($i.Message)")) -ForegroundColor Red
    }
    foreach ($i in @($Summary.Other)) {
        $req = @($i.RequiredComponents | ForEach-Object { if ($_.SchemaName) { "$($_.SchemaName)" } elseif ($_.DisplayName) { "$($_.DisplayName)" } else { "type $($_.ComponentType)" } })
        $reqText = if ($req.Count -gt 3) { ($req[0..2] -join ', ') + " (+$($req.Count - 3) more)" } else { $req -join ', ' }
        Write-Host ("    {0,-7} {1} | type {2}{3}{4}" -f $i.ErrorType, $(if ($i.DisplayName) { Write-Ascii $i.DisplayName } else { '-' }), $i.ComponentType,
            $(if ($i.Message) { " | " + (Write-Ascii "$($i.Message)") } else { '' }), $(if ($reqText) { " | requires: $reqText" } else { '' })) -ForegroundColor DarkYellow
    }
    $names = @($Summary.InfoNoFormView | ForEach-Object { ("$($_.Message)" -replace "\s+doesn.t reference.*$", '') } | Where-Object { $_ -match 'Billing Export|Invoice|Communication$' })
    Write-Host ("    informational: {0} tables are entity rows without form / view rows (app users see all forms and views); relevant: {1}" -f $Summary.InfoNoFormView.Count, $(if ($names) { ($names -join ', ') } else { 'none' }))
    Write-Host ("    Billing Export issues (other than the informational line): {0}" -f $Summary.BillingIssues.Count)
    foreach ($i in @($Summary.BillingIssues)) { Write-Host ("      {0} | {1} | {2}" -f $i.ErrorType, $i.DisplayName, (Write-Ascii "$($i.Message)")) -ForegroundColor Red }
    if ($RawPath) { Write-Host "    full ValidateApp response saved: $RawPath" }
    Write-Host ("  ValidateApp clean (success, 0 errors, 0 Billing Export issues): {0}" -f $Summary.Clean) -ForegroundColor $(if ($Summary.Clean) { 'Green' } else { 'Red' })
}

function Invoke-ValidateApp([string]$Tag) {
    $resp = Invoke-Dv -Path "ValidateApp(AppModuleId=$AppId)"
    $raw = $null
    try {
        New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
        $raw = Join-Path (Resolve-Path $BackupDir).Path ("validateapp-$Tag-" + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + 'Z-' + ([guid]::NewGuid().ToString('N')).Substring(0, 8) + '.json')
        ($resp | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $raw -Encoding UTF8
    } catch { $raw = $null }
    return [pscustomobject]@{ Response = $resp; Summary = (Get-ValidateAppSummary $resp); RawPath = $raw }
}

# Read-only facts about the batch form, for the report: default form, the AP emails subgrid, and the Email to AP command-bar button.
function Write-BatchFormFacts {
    Write-Section "Batch form check on dev (read only, informational)"
    try {
        $forms = @((Invoke-Dv -Path "systemforms?`$select=formid,name,type,isdefault,formactivationstate&`$filter=objecttypecode eq '$Table'").value)
        $mains = @($forms | Where-Object { $_.type -eq 2 -and $_.formactivationstate -eq 1 })
        Write-Host ("  forms of {0}: {1}" -f $Table, (@($forms | ForEach-Object { "$($_.formid) '$($_.name)' type $($_.type)" }) -join '; '))
        if ($mains.Count -eq 1) { Write-Host ("  default form: {0} '{1}' - the only active main form (systemform.isdefault is {2} on every form here; the platform does not use it)" -f $mains[0].formid, $mains[0].name, $mains[0].isdefault) -ForegroundColor Green }
        else { Write-Host ("  default form: {0} active main forms ({1}); the default depends on form order / role, not decided here" -f $mains.Count, (($mains | ForEach-Object { $_.formid }) -join ', ')) -ForegroundColor Yellow }
        $f = Invoke-Dv -Path "systemforms($MainFormId)?`$select=formxml,name"
        $x = [xml]$f.formxml
        $grid = $x.SelectSingleNode("//control[parameters/TargetEntityType='$LinkedTable']")
        if ($grid) {
            $viewId = ("$($grid.SelectSingleNode('parameters/ViewId').InnerText)").Trim('{', '}')
            $viewName = try { (Invoke-Dv -Path "savedqueries($viewId)?`$select=name").name } catch { '?' }
            Write-Host ("  AP emails subgrid on the main form ({0}): YES - control '{1}', target {2}, relationship {3}, view {4} '{5}'" -f $MainFormId, $grid.id, $LinkedTable, $grid.SelectSingleNode('parameters/RelationshipName').InnerText, $viewId, $viewName) -ForegroundColor Green
        } else { Write-Host "  AP emails subgrid on the main form ($MainFormId): NO control targeting $LinkedTable" -ForegroundColor Red }
        $rel = @((Invoke-Dv -Path "EntityDefinitions(LogicalName='$LinkedTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute").value | Where-Object { $_.SchemaName -eq $SubscriptionRelationship })
        Write-Host ("  relationship {0}: {1}" -f $SubscriptionRelationship, $(if ($rel.Count -gt 0) { "exists (column $($rel[0].ReferencingAttribute))" } else { 'MISSING' }))
    } catch { Write-Host "  could not read the form: $($_.Exception.Message)" -ForegroundColor Yellow }
    try {
        $r = Invoke-Dv -Path "RetrieveEntityRibbon(EntityName='$Table',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')"
        $ms = [System.IO.MemoryStream]::new([Convert]::FromBase64String($r.CompressedEntityXml))
        $zip = [System.IO.Compression.ZipArchive]::new($ms)
        $entry = $zip.Entries | Where-Object { $_.Name -eq 'RibbonXml.xml' } | Select-Object -First 1
        $sr = [System.IO.StreamReader]::new($entry.Open()); $ribbon = $sr.ReadToEnd(); $sr.Close()
        $btn = $ribbon -match 'Button Id="sprk\.ApExport\.BillingExport\.Form\.EmailToAp\.Button"'
        $priv = $ribbon -match 'EntityPrivilegeRule EntityName="sprk_billingexport" PrivilegeType="Write"'
        Write-Host ("  Email to AP button (command bar, published ribbon, not part of the form XML): {0}; shows for an existing record with Write on sprk_billingexport{1}" -f $(if ($btn) { 'PRESENT' } else { 'ABSENT' }), $(if ($priv) { ' (Write display rule found)' } else { '' })) -ForegroundColor $(if ($btn) { 'Green' } else { 'Red' })
    } catch { Write-Host "  could not read the published ribbon: $($_.Exception.Message)" -ForegroundColor Yellow }
}

# Source (the checked-in solution) against dev for this table. Informational: it never changes the exit code.
function Write-SourceDriftFacts {
    Write-Section "Source vs dev for $Table (read only, informational)"
    try {
        $tableId = Get-TableMetadataId $Table
        $devHas = Test-HasComponent (Group-AppComponents (Get-AppComponentRows)) 1 $tableId
        $am = Get-SourceAppModuleEntry -Path $SourceAppModulePath -Entity $Table
        $amState = if ($am.Found -and $devHas) { 'in sync' } else { 'DRIFT' }
        Write-Host ("  AppModule.xml entity component: source {0}{1}; dev {2} -> {3}" -f $(if ($am.Found) { 'yes' } else { 'NO' }), $(if ($am.Found) { " (line $($am.Line))" } else { '' }), $(if ($devHas) { 'yes' } else { 'NO' }), $amState) -ForegroundColor $(if ($amState -eq 'DRIFT') { 'Yellow' } else { 'Green' })
        $dev = @(Get-EntitySubAreas -Xml (Get-AppSiteMap).Xml -Entity $Table)
        $src = Get-SourceSubArea -Path $SourceSiteMapPath -Entity $Table
        $devText = if ($dev.Count) { ($dev | ForEach-Object { "$($_.Id) in $($_.AreaId)/$($_.GroupId)" }) -join '; ' } else { 'none' }
        if (-not $src.Found) { Write-Host ("  sitemap subarea: source: {0}; dev: {1} -> DRIFT" -f $src.Detail, $devText) -ForegroundColor Yellow }
        else {
            $same = ($dev.Count -eq 1) -and ($dev[0].Xml -ceq $src.Xml) -and ($dev[0].AreaId -eq $src.AreaId) -and ($dev[0].GroupId -eq $src.GroupId)
            Write-Host ("  sitemap subarea: source {0} in {1}/{2}; dev {3} -> {4}" -f $src.Id, $src.AreaId, $src.GroupId, $devText, $(if ($same) { 'in sync (same Id, group, attributes)' } else { 'DRIFT' })) -ForegroundColor $(if ($same) { 'Green' } else { 'Yellow' })
        }
        foreach ($pair in @(@('AppModule.xml', 'AppModule_managed.xml', $SourceAppModulePath), @('AppModuleSiteMap.xml', 'AppModuleSiteMap_managed.xml', $SourceSiteMapPath))) {
            $managed = Join-Path (Split-Path -Parent $pair[2]) $pair[1]
            if (Test-Path -LiteralPath $managed) {
                $same = (Get-FileHash -LiteralPath $pair[2] -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $managed -Algorithm SHA256).Hash
                Write-Host ("  {0} and {1} are {2}" -f $pair[0], $pair[1], $(if ($same) { 'identical' } else { 'DIFFERENT (sync both)' })) -ForegroundColor $(if ($same) { 'Green' } else { 'Yellow' })
            }
        }
    } catch { Write-Host "  could not compare source and dev: $($_.Exception.Message)" -ForegroundColor Yellow }
}

function Write-RollbackSteps($SitemapId, [string]$BackupPath) {
    Write-Section "Rollback (only needed after an -Apply that wrote something)"
    Write-Host "  Sitemap (the before XML is the byte copy below; restoring it removes only the subarea this script inserted):"
    Write-Host "    1. `$xml = [IO.File]::ReadAllText('$BackupPath', [Text.UTF8Encoding]::new(`$false))"
    Write-Host "    2. PATCH $EnvironmentUrl/api/data/v9.2/sitemaps($SitemapId)   body { sitemapxml: `$xml }   (az account get-access-token --resource $EnvironmentUrl)"
    Write-Host "    3. PublishXml: $(New-ScopedPublishXml -AppId $AppId -SitemapId $SitemapId)   (scoped; never the tenant-wide publish)"
    Write-Host "  App components: RemoveAppComponents (AppId $AppId, the same components) - on dev it can answer 204 and remove nothing (CLAUDE.md, Spaarke Platform note);"
    Write-Host "    then remove the table in the app designer (make.powerapps.com, Matter Management, Save + Publish). A leftover entity component is harmless."
}

# The four checks. Prints each; returns Passed.
function Invoke-VerifyChecks([string]$Tag) {
    $tableId = Get-TableMetadataId $Table
    $controlId = Get-TableMetadataId $ControlTable
    $linkedId = Get-TableMetadataId $LinkedTable
    $sets = Group-AppComponents (Get-AppComponentRows)
    $c1 = Test-HasComponent $sets 1 $tableId
    Write-Host ("  [{0}] 1. RetrieveAppComponents({1}) includes {2} ({3})" -f $(if ($c1) { 'PASS' } else { 'FAIL' }), $AppId.Substring(0, 8), $Table, $tableId) -ForegroundColor $(if ($c1) { 'Green' } else { 'Red' })
    $c2 = Test-HasComponent $sets 1 $controlId
    Write-Host ("  [{0}] 2. control: {1} is still an app component ({2})" -f $(if ($c2) { 'PASS' } else { 'FAIL' }), $ControlTable, $controlId) -ForegroundColor $(if ($c2) { 'Green' } else { 'Red' })
    $va = Invoke-ValidateApp $Tag
    Write-Host ("  [{0}] 3. ValidateApp is clean" -f $(if ($va.Summary.Clean) { 'PASS' } else { 'FAIL' })) -ForegroundColor $(if ($va.Summary.Clean) { 'Green' } else { 'Red' })
    Write-ValidateAppSummary $va.Response $va.Summary $va.RawPath
    $sm = Get-AppSiteMap
    $subs = @(Get-EntitySubAreas -Xml $sm.Xml -Entity $Table)
    $c4 = $subs.Count -ge 1
    $where = ($subs | ForEach-Object { "$($_.Id) in $($_.AreaId)/$($_.GroupId)" }) -join '; '
    Write-Host ("  [{0}] 4. the published sitemap {1} has a subarea for {2} (matched by Entity){3}" -f $(if ($c4) { 'PASS' } else { 'FAIL' }), $AppUniqueName, $Table, $(if ($c4) { ": $where; sitemap modifiedon $($sm.ModifiedOn)" } else { '' })) -ForegroundColor $(if ($c4) { 'Green' } else { 'Red' })
    if ($subs.Count -gt 1) { Write-Host "        WARN: $($subs.Count) subareas for $Table - users see the entry more than once." -ForegroundColor Yellow }
    Write-Host ("        note: a sitemap has no unpublished copy to compare (RetrieveUnpublished refuses 'sitemap', 0x80040800): the record read here is the one PublishXml publishes.")
    Write-Host ("  info: {0} app component: {1}; main form {2} listed as a form component: {3}" -f $LinkedTable, $(if (Test-HasComponent $sets 1 $linkedId) { 'yes' } else { 'NO' }), $MainFormId, $(if (Test-HasComponent $sets 60 $MainFormId) { 'yes' } else { 'no (entity row only: all forms and views)' }))
    return ($c1 -and $c2 -and $va.Summary.Clean -and $c4)
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "eBilling - $Table in the Matter Management app on dev (verify / additive fix)" -ForegroundColor Cyan
Write-Host ("Environment: {0}   App: {1} ({2})   Mode: {3}" -f $EnvironmentUrl, $AppUniqueName, $AppId, $(switch ($script:Mode) { 'WHATIF' { 'WHATIF (reads only; nothing is written to dev)' } 'VERIFY' { 'VERIFY (reads only)' } default { 'APPLY (writes; owner approval given)' } }))

$stage = 'start'
try {
    $app = @((Invoke-Dv -Path "appmodules?`$select=appmoduleid,appmoduleidunique,uniquename,name,modifiedon,publishedon&`$filter=uniquename eq '$AppUniqueName'").value)
    if ($app.Count -ne 1 -or $app[0].appmoduleid -ne $AppId) {
        throw [EscalationException]::new("The app '$AppUniqueName' on this environment is not the record this script was written for ($AppId). Found: $(@($app | ForEach-Object { $_.appmoduleid }) -join ', ').")
    }
    $tableId = Get-TableMetadataId $Table

    if ($script:Mode -eq 'VERIFY') {
        Write-Section "Verify (read only)"
        $passed = Invoke-VerifyChecks 'verify'
        Write-BatchFormFacts
        Write-SourceDriftFacts
        Write-Host ""
        Write-Host $(if ($passed) { 'VERIFY PASSED' } else { 'VERIFY FAILED' }) -ForegroundColor $(if ($passed) { 'Green' } else { 'Red' })
        exit $(if ($passed) { 0 } else { 1 })
    }

    # ---- 1. current components --------------------------------------------------------------------------------------------
    Write-Section "1. Current app components (RetrieveAppComponents, read only)"
    $rows = Get-AppComponentRows
    $sets = Group-AppComponents $rows
    $byType = @($rows | Group-Object componenttype | Sort-Object { [int]$_.Name } | ForEach-Object { "type $($_.Name): $($_.Count)" }) -join ', '
    Write-Host ("  {0} rows ({1}); app modifiedon {2}, last published {3}" -f $rows.Count, $byType, $app[0].modifiedon.ToString('o'), $app[0].publishedon.ToString('o'))
    $controlId = Get-TableMetadataId $ControlTable
    $linkedId = Get-TableMetadataId $LinkedTable
    $hasTable = Test-HasComponent $sets 1 $tableId
    $hasLinked = Test-HasComponent $sets 1 $linkedId
    $hasForm = Test-HasComponent $sets 60 $MainFormId
    Write-Host ("  {0,-20} {1}  app component: {2}" -f $Table, $tableId, $(if ($hasTable) { 'yes' } else { 'NO' })) -ForegroundColor $(if ($hasTable) { 'Green' } else { 'Yellow' })
    Write-Host ("  {0,-20} {1}  app component: {2}   (control; must stay)" -f $ControlTable, $controlId, $(if (Test-HasComponent $sets 1 $controlId) { 'yes' } else { 'NO' }))
    Write-Host ("  {0,-20} {1}  app component: {2}   (target of the AP emails subgrid)" -f $LinkedTable, $linkedId, $(if ($hasLinked) { 'yes' } else { 'NO - it will be added too' })) -ForegroundColor $(if ($hasLinked) { 'Green' } else { 'Yellow' })
    if (-not (Test-HasComponent $sets 1 $controlId)) { throw [EscalationException]::new("$ControlTable is not an app component: the app is not in the state this script expects. Nothing was changed.") }

    Write-Host "  How the app lists forms and views (pattern):"
    $viewRows = if ($sets.ContainsKey(26)) { $sets[26].Count } else { 0 }
    Write-Host ("    view rows (type 26) in the app: {0} of {1} -> views are not listed per table; a table's views come with its entity row" -f $viewRows, $rows.Count)
    foreach ($t in $ControlTable, $LinkedTable, 'sprk_invoicesubmission', 'sprk_invoicecorrection') {
        $fs = @((Invoke-Dv -Path "systemforms?`$select=formid,name,type&`$filter=objecttypecode eq '$t'").value)
        $listed = @($fs | Where-Object { Test-HasComponent $sets 60 $_.formid })
        Write-Host ("    {0,-24} {1} of {2} forms listed explicitly{3}" -f $t, $listed.Count, $fs.Count, $(if ($listed.Count -gt 0) { ': ' + (($listed | ForEach-Object { "$($_.formid.Substring(0, 8)) '$($_.name)' t$($_.type)" }) -join '; ') } else { ' (entity row only: all forms and views)' }))
    }

    # ---- 2. components to add ---------------------------------------------------------------------------------------------
    Write-Section "2. Components the script would add (AddAppComponents)"
    $mainForm = Invoke-Dv -Path "systemforms($MainFormId)?`$select=formid,name,type,objecttypecode,formactivationstate"
    if ($mainForm.objecttypecode -ne $Table -or $mainForm.type -ne 2) { throw [EscalationException]::new("Form $MainFormId is not a main form of ${Table}: objecttypecode '$($mainForm.objecttypecode)', type $($mainForm.type).") }
    $components = [System.Collections.Generic.List[hashtable]]::new()
    $planLines = [System.Collections.Generic.List[string]]::new()
    if (-not $hasTable) {
        $components.Add(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.entity'; entityid = $tableId })
        $planLines.Add("+ table  $Table  entityid $tableId")
    } else { $planLines.Add("= table  $Table  already a component - skipped") }
    if (-not $hasLinked) {
        $components.Add(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.entity'; entityid = $linkedId })
        $planLines.Add("+ table  $LinkedTable  entityid $linkedId  (the AP emails subgrid target was NOT an app component)")
    } else { $planLines.Add("= table  $LinkedTable  already a component (AP emails subgrid target) - not added") }
    $notRow = 'not added as a row (default; -ExplicitFormsAndViews would add it and then LIMIT the table to the listed forms / views) - available through the entity row'
    if ($hasForm) { $planLines.Add("= form   $MainFormId  '$($mainForm.name)' (main form, type 2)  already a component - skipped") }
    elseif ($ExplicitFormsAndViews) {
        $components.Add(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.systemform'; formid = $MainFormId })
        $planLines.Add("+ form   $MainFormId  '$($mainForm.name)' (main form, type 2)")
    } else { $planLines.Add("  form   $MainFormId  '$($mainForm.name)' (main form, type 2; the table's only main form = its default)  - $notRow") }

    $views = @((Invoke-Dv -Path "savedqueries?`$select=savedqueryid,name,querytype&`$filter=returnedtypecode eq '$Table' and statecode eq 0").value | Sort-Object { [int]$_.querytype }, name)
    $srcViewIds = if (Test-Path -LiteralPath $SourceViewsDir) { @(Get-ChildItem -LiteralPath $SourceViewsDir -Filter '*.xml' | ForEach-Object { $_.BaseName.Trim('{', '}').ToLowerInvariant() }) } else { @() }
    foreach ($v in $views) {
        $inSrc = $srcViewIds -contains "$($v.savedqueryid)".ToLowerInvariant()
        $have = Test-HasComponent $sets 26 $v.savedqueryid
        if ($ExplicitFormsAndViews -and $inSrc -and -not $have) {
            $components.Add(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.savedquery'; savedqueryid = $v.savedqueryid })
            $planLines.Add("+ view   $($v.savedqueryid)  '$($v.name)' (querytype $($v.querytype))")
        } else {
            $why = if ($have) { 'already a component' } elseif (-not $inSrc) { 'not in source' } else { $notRow }
            $planLines.Add("  view   $($v.savedqueryid)  '$($v.name)' (querytype $($v.querytype))  - $why")
        }
    }
    $others = @((Invoke-Dv -Path "systemforms?`$select=formid,name,type&`$filter=objecttypecode eq '$Table'").value | Where-Object { $_.formid -ne $MainFormId })
    foreach ($o in $others) { $planLines.Add("  form   $($o.formid)  '$($o.name)' (type $($o.type))  - not added (source lists no form row for this table either)") }
    $planLines | ForEach-Object { Write-Host "  $_" -ForegroundColor $(if ($_.StartsWith('+')) { 'Yellow' } else { 'DarkGray' }) }
    Write-Host ("  => {0} component(s) to add{1}" -f $components.Count, $(if ($components.Count -gt 0) { '; AddAppComponents body: ' + ((@{ AppId = $AppId; Components = @($components) } | ConvertTo-Json -Depth 6 -Compress)) } else { '' }))

    # ---- 3. sitemap -------------------------------------------------------------------------------------------------------
    Write-Section "3. Sitemap $AppUniqueName (read only)"
    $sm = Get-AppSiteMap
    $rowSm = @($rows | Where-Object { $_.componenttype -eq 62 })
    if ($rowSm.Count -eq 1 -and "$($rowSm[0].objectid)".ToLowerInvariant() -ne "$($sm.Id)".ToLowerInvariant()) {
        throw [EscalationException]::new("The sitemap named '$AppUniqueName' ($($sm.Id)) is not the sitemap component of the app ($($rowSm[0].objectid)).")
    }
    Write-Host ("  sitemapid {0}; modifiedon {1}; etag {2}; {3} chars" -f $sm.Id, $sm.ModifiedOn, $sm.Etag, $sm.Xml.Length)
    $backup = Save-SiteMapBackup -Dir $BackupDir -UniqueName $AppUniqueName -SitemapId $sm.Id -Etag $sm.Etag -ModifiedOn $sm.ModifiedOn -Xml $sm.Xml -Environment $EnvironmentUrl
    Write-Host ("  before-XML saved byte for byte: {0}  ({1} bytes, sha256 {2}); sidecar {3}" -f $backup.Path, $backup.Bytes, $backup.Sha256, (Split-Path -Leaf $backup.Sidecar))

    $devSubs = @(Get-EntitySubAreas -Xml $sm.Xml -Entity $Table)
    $src = Get-SourceSubArea -Path $SourceSiteMapPath -Entity $Table
    Write-Host ("  source subarea for {0}: {1}" -f $Table, $(if ($src.Found) { $src.Detail } else { "none usable - $($src.Detail)" })) -ForegroundColor $(if ($src.Found) { 'Green' } else { 'Yellow' })
    $plan = $null
    $sitemapProblem = $null
    $proof = $null
    if ($devSubs.Count -gt 0) {
        Write-Host ("  subarea for {0} already in the sitemap (matched by Entity, not by Id): {1} - no sitemap change" -f $Table, (($devSubs | ForEach-Object { "$($_.Id) in $($_.AreaId)/$($_.GroupId)" }) -join '; ')) -ForegroundColor Green
        if ($devSubs.Count -gt 1) { Write-Host "  WARN: $($devSubs.Count) subareas for $Table - users see the entry more than once (this script never removes one)." -ForegroundColor Yellow }
    } elseif (-not $src.Found) {
        $sitemapProblem = "no subarea for $Table on dev and none in source to copy ($($src.Detail))"
        Write-Host "  subarea MISSING on dev and $sitemapProblem - cannot plan an insertion" -ForegroundColor Red
    } else {
        $candidate = New-SiteMapPlan -BeforeXml $sm.Xml -SubAreaXml $src.Xml -SubAreaId $src.Id -Entity $Table -SourceGroupId $src.GroupId -SourcePredecessorId $src.PredecessorId -InvoiceEntity $InvoiceEntity
        if ($candidate.Conflict) {
            $sitemapProblem = $candidate.Conflict
            Write-Host "  subarea MISSING on dev; cannot plan: $sitemapProblem" -ForegroundColor Red
        } else {
            $plan = $candidate
            Write-Host "  subarea for $Table is MISSING; placement: $($plan.Placement)" -ForegroundColor Yellow
            Write-Host "  element (copied from source): $($src.Xml)"
            $proof = Assert-SiteMapAdditiveOnly -BeforeXml $sm.Xml -AfterXml $plan.AfterXml -SubAreaXml $src.Xml -SubAreaId $src.Id -InsertIndex $plan.InsertIndex
            $newNode = (Read-XmlPreserving $plan.AfterXml).SelectSingleNode("//SubArea[@Id='$($src.Id)']")
            $sibs = @($newNode.ParentNode.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.LocalName -eq 'SubArea' })
            $pos = [array]::IndexOf($sibs, $newNode)
            Write-Host ("  sitemap diff (area {0} / group {1}; only the + line is new):" -f $newNode.ParentNode.ParentNode.GetAttribute('Id'), $newNode.ParentNode.GetAttribute('Id'))
            for ($k = [Math]::Max(0, $pos - 2); $k -le [Math]::Min($sibs.Count - 1, $pos + 2); $k++) {
                $s = $sibs[$k]
                $what = if ($s.GetAttribute('Entity')) { "Entity $($s.GetAttribute('Entity'))" } else { "Url $($s.GetAttribute('Url'))" }
                if ($k -eq $pos) { Write-Host ("    + SubArea {0}  {1}" -f $s.GetAttribute('Id'), $what) -ForegroundColor Green }
                else { Write-Host ("      SubArea {0}  {1}" -f $s.GetAttribute('Id'), $what) -ForegroundColor DarkGray }
            }
            if ($proof.Compare) {
                Write-Host ("  node-by-node comparison: {0} nodes before, {1} after; added: {2}; unexpected differences: {3}" -f $proof.Compare.BeforeCount, $proof.Compare.AfterCount, $proof.Compare.Added.Count, $proof.Compare.Unexpected.Count)
            }
            Write-Host ("  byte-level: after = before + {0} inserted chars at index {1}; removing them gives back the before XML: {2}" -f $src.Xml.Length, $plan.InsertIndex, $(if ($plan.AfterXml.Remove($plan.InsertIndex, $src.Xml.Length) -ceq $sm.Xml) { 'identical' } else { 'DIFFERENT' }))
            if (-not $proof.Ok) { throw [EscalationException]::new("The sitemap edit is not additive-only, nothing was written: $($proof.Problems -join '; ')") }
            Write-Host "  ADDITIVE-ONLY ASSERTION: passed (exactly one SubArea added, nothing else differs)" -ForegroundColor Green
        }
    }

    # ---- 4. publish plan, pending changes, baseline -----------------------------------------------------------------------
    Write-Section "4. Publish plan, pending changes, ValidateApp baseline (read only)"
    $publishXml = New-ScopedPublishXml -AppId $AppId -SitemapId $sm.Id
    $willPublish = ($components.Count -gt 0) -or [bool]$plan
    Write-Host ("  PublishXml ParameterXml (only if something is written): {0}" -f $publishXml)
    Write-Host ("  publish needed now: {0}; the tenant-wide publish is never used" -f $(if ($willPublish) { 'yes' } else { 'no (nothing to write)' }))
    $pending = @(Get-AppPending)
    if ($pending.Count -eq 0) { Write-Host "  pending (unpublished) changes on the app module and its settings: none" -ForegroundColor Green }
    else {
        Write-Host "  pending (unpublished) changes that publishing the app module would publish too:" -ForegroundColor Red
        $pending | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    }
    $baseline = Invoke-ValidateApp 'baseline'
    Write-ValidateAppSummary $baseline.Response $baseline.Summary $baseline.RawPath
    $baselineKeys = Get-IssueKeys $baseline.Response

    Write-BatchFormFacts
    Write-SourceDriftFacts

    if ($script:Mode -eq 'WHATIF') {
        if ($willPublish) { Write-RollbackSteps $sm.Id $backup.Path }
        Write-Host ""
        Write-Host "================ Summary (WHATIF) ================" -ForegroundColor Cyan
        Write-Host ("components to add : {0}" -f $components.Count)
        Write-Host ("sitemap           : {0}" -f $(if ($plan) { "add the source subarea $($src.Id) (1 node)" } elseif ($sitemapProblem) { "CANNOT PLAN - $sitemapProblem" } else { 'no change' }))
        Write-Host ("pending changes   : {0}" -f $pending.Count)
        Write-Host "WhatIf run: nothing was written to dev (only the local before-XML copy and the ValidateApp response)." -ForegroundColor Cyan
        exit 0
    }

    # ================================== APPLY ==================================================================================
    if ($sitemapProblem) { throw [EscalationException]::new("The sitemap cannot be planned safely: $sitemapProblem. Nothing was written.") }
    if (-not $willPublish) {
        Write-Host ""
        Write-Host "Nothing to add: the table and a subarea for it are in place. No write, no publish; verifying." -ForegroundColor Green
        $passed = Invoke-VerifyChecks 'apply-noop'
        exit $(if ($passed) { 0 } else { 1 })
    }
    if ($pending.Count -gt 0) { throw [EscalationException]::new("Pending changes exist on the app module / settings; publishing would publish them too. Their owner publishes or discards them first. Nothing was written.") }
    if ($components.Count -gt 0) {
        Write-Section "A1. AddAppComponents"
        $rows2 = Get-AppComponentRows
        $sets2 = Group-AppComponents $rows2
        $lost = @($rows | Where-Object { -not (Test-HasComponent $sets2 ([int]$_.componenttype) "$($_.objectid)") }).Count
        if ($lost -gt 0 -or $rows2.Count -ne $rows.Count) { throw [EscalationException]::new("The app's component set changed since the plan was made ($($rows.Count) -> $($rows2.Count) rows). Someone else is editing the app; re-run the WhatIf. Nothing was written.") }
        Invoke-DvWrite -Path 'AddAppComponents' -Method POST -Body @{ AppId = $AppId; Components = @($components) } | Out-Null
        $stage = 'components-added'
        Write-Host "  AddAppComponents answered (HTTP success); reading back"
        function Get-Missing {
            $s = Group-AppComponents (Get-AppComponentRows)
            return @($components | Where-Object {
                    $t = switch ($_['@odata.type']) { 'Microsoft.Dynamics.CRM.entity' { 1 } 'Microsoft.Dynamics.CRM.systemform' { 60 } default { 26 } }
                    $id = if ($_.ContainsKey('entityid')) { $_.entityid } elseif ($_.ContainsKey('formid')) { $_.formid } else { $_.savedqueryid }
                    -not (Test-HasComponent $s $t $id) })
        }
        $missing = @(Get-Missing)
        if ($missing.Count -gt 0) {
            Write-Host "  not visible yet ($($missing.Count) missing): publishing the app module only, then reading again" -ForegroundColor Yellow
            Invoke-DvWrite -Path 'PublishXml' -Method POST -Body @{ ParameterXml = (New-ScopedPublishXml -AppId $AppId) } | Out-Null
            $missing = @(Get-Missing)
        }
        if ($missing.Count -gt 0) {
            Write-Host ""
            Write-Host "STOPPED: AddAppComponents answered but $($missing.Count) component(s) are still not in the app after a publish (the known no-op on this environment, notes/029 section 7A)." -ForegroundColor Red
            Write-Host "The sitemap was NOT written. Manual step: make.powerapps.com > Matter Management > Edit > Add page > Table > Billing Export (all forms and views, or the main form) > Save > Publish; then re-run -Apply (it skips what is there) or -Verify." -ForegroundColor Red
            exit 3
        }
        Write-Host "  all $($components.Count) component(s) read back in the app" -ForegroundColor Green
    }

    if ($plan) {
        Write-Section "A2. Sitemap PATCH"
        $now = Get-AppSiteMap
        if ($now.Xml -cne $sm.Xml) { throw [EscalationException]::new("The sitemap changed since the plan was made (modifiedon $($sm.ModifiedOn) -> $($now.ModifiedOn)). Nothing was written to it; re-run the WhatIf.") }
        if (@(Get-EntitySubAreas -Xml $now.Xml -Entity $Table).Count -gt 0) { throw [EscalationException]::new("A subarea for $Table appeared in the sitemap since the plan was made. Nothing was written to it.") }
        Invoke-DvWrite -Path "sitemaps($($sm.Id))" -Method PATCH -Body @{ sitemapxml = $plan.AfterXml } -Headers @{ 'If-Match' = $sm.Etag } | Out-Null
        $stage = 'sitemap-written'
        $read = Get-AppSiteMap
        if ($read.Xml -ceq $plan.AfterXml) { Write-Host "  sitemap written and read back byte for byte" -ForegroundColor Green }
        else {
            $nodeOnly = Compare-SiteMapNodes -BeforeLines (Get-SiteMapNodeLines $sm.Xml) -AfterLines (Get-SiteMapNodeLines $read.Xml) -AddedSubAreaId $src.Id
            if ($nodeOnly.Unexpected.Count -eq 0 -and $nodeOnly.Added.Count -eq 1) { Write-Host "  WARN: the server re-serialised the sitemap text; node-by-node it is before + the one SubArea" -ForegroundColor Yellow }
            else { throw "The sitemap read back after the PATCH differs from what was written beyond the added SubArea. Roll back with the saved copy: $($backup.Path)" }
        }
    }

    Write-Section "A3. Publish (app module + sitemap only)"
    Invoke-DvWrite -Path 'PublishXml' -Method POST -Body @{ ParameterXml = $publishXml } | Out-Null
    $stage = 'published'
    Write-Host "  published: $publishXml" -ForegroundColor Green

    Write-Section "A4. Verify and ValidateApp after the change"
    $passed = Invoke-VerifyChecks 'after-apply'
    $post = Invoke-ValidateApp 'after-apply-compare'
    $new = @(Get-IssueKeys $post.Response | Where-Object { $baselineKeys -notcontains $_ })
    Write-Host ("  issues that are new compared with the baseline: {0}" -f $new.Count) -ForegroundColor $(if ($new.Count -eq 0) { 'Green' } else { 'Yellow' })
    $new | ForEach-Object { Write-Host "    $(Write-Ascii $_)" -ForegroundColor Yellow }
    Write-RollbackSteps $sm.Id $backup.Path
    Write-Host ""
    Write-Host $(if ($passed) { 'APPLIED AND VERIFIED' } else { 'APPLIED, BUT A CHECK FAILED (see above)' }) -ForegroundColor $(if ($passed) { 'Green' } else { 'Red' })
    exit $(if ($passed) { 0 } else { 1 })
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "REFUSED / ESCALATION - $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($stage -ne 'start') { Write-Host "Stage reached: $stage. The script is re-runnable (it skips what is already there); see the rollback steps if you want to undo." -ForegroundColor Red }
    exit 1
}
