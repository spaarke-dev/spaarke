<#
.SYNOPSIS
    eBilling R1 task 193 - retires the three core-record lookups of sprk_ratecard (sprk_matter, sprk_project, sprk_workassignment) and
    their three relationships. Guarded, idempotent, dev only.

.DESCRIPTION
    Since task 085 (EB-D-148, D-120) a rate card belongs to a Procedure Version; no code reads or writes the card's three core-record
    lookups. They still exist on dev as RemoveLink relationships (DEF-027), so the core-record delete planner (unified-access-control-r2)
    reaches cards through them. The owner decided on 2026-11-23 to remove them as a separate schema task. The three lookups and their
    relationships:

        sprk_ratecard.sprk_matter          sprk_matter_ratecard_matter
        sprk_ratecard.sprk_project         sprk_project_ratecard_project
        sprk_ratecard.sprk_workassignment  sprk_workassignment_ratecard_workassignment

    Nothing else is touched: sprk_ratecard.sprk_firm, .sprk_procedureversion and .sprk_sourcedocument stay, and so do the columns of the
    same name on sprk_ruleset and every other table (they sit behind other relationships, which the script never names).

    MODES
      (none)           READ-ONLY report: per column, whether it and its relationship exist, how many rate cards hold a value (a count,
                       no ids), what depends on it; the forms, views, personal views and grid configurations of sprk_ratecard that name
                       it; then the verdict (READY, BLOCKED with the reasons, or ALREADY RETIRED). This is the -WhatIf default: nothing
                       is written without -Apply.
      -Apply           The retirement (needs -OwnerApproval, -BffDeployAttestation, -SpaDeployAttestation; see below). Every refusal
                       happens before the first write (exit 2, nothing changed):
                         (a) any sprk_ratecard row (any state) holds a value in any of the three columns, or the count cannot be had,
                             or the caller cannot be shown to read EVERY sprk_ratecard row (the table is user-owned, so a count taken
                             at a lower read depth is silently short; needs organization-wide (Global) Read on the table, which a System
                             Administrator holds; there is no role-name shortcut);
                         (b) the deployed BFF / SPA source selects the columns (the attestation checks, below);
                         (c) anything else depends on a column or relationship (RetrieveDependenciesForDelete), a personal view or
                             grid configuration names a column, a form or view cannot be cleaned, or a relationship is not exactly
                             the expected sprk_ratecard -> core-record one;
                         (d) another pending (unpublished) form or view of sprk_ratecard would go live with the publish (D-83), unless
                             -AllowPendingCollateral is given;
                         (e) a form or view this script edits has UNPUBLISHED changes that are not exactly the edit it would write
                             (publishing would overwrite someone's work; -AllowPendingCollateral does NOT override this).
                       Then: take the three controls off the sprk_ratecard forms and the three cells / attributes off its views, publish
                       sprk_ratecard (entity-scoped, through the shared scripts/lib/Publish-SolutionComponents.ps1 procedure, never
                       tenant-wide); re-read RetrieveDependenciesForDelete until nothing depends on a column; delete the three
                       relationships by schema name (that removes the lookup columns and their name companions; deleting a relationship
                       needs no publish of the core tables, which are never published); publish sprk_ratecard again; verify.
      -Apply -WhatIf   The dry run of the above: the same refusals and the same census (read-only GETs), and "What if:" lines for every
                       write; nothing is written.
      -Verify          READ-ONLY. Confirms the three attributes and the three relationships are gone and no sprk_ratecard form or view
                       names them. Exit 0 = retired, exit 1 = something remains. -Apply ends with the same check.

    IDEMPOTENT. A second -Apply after success finds everything gone, does nothing, verifies and exits 0. A run interrupted part way is
    re-run to continue: every step skips work already done.

    RESUME AFTER A STOP BEFORE THE PUBLISH. A run that edited the form and view but stopped before it published leaves those edits
    unpublished (pending). The plain read of systemforms / savedqueries is the PUBLISHED layer, so the re-run reads the published text,
    which still names the lookups, builds the same edit again, and finds the pending edit on the form and view: it is taken as a resume
    ONLY when the unpublished text (RetrieveUnpublished, XML-normalised) equals the text this run would write.

    UNPUBLISHED EDITS ARE NEVER OVERWRITTEN (F2). The edits are built from the published text. The pending check lists everything
    unpublished on sprk_ratecard WITHOUT excluding the forms and views this script edits; for each of those with pending changes the
    unpublished text is read and compared with what this run would write. Anything that differs (someone else's unpublished edit) refuses
    (exit 2, before the first write) and -AllowPendingCollateral does NOT override it; the report and -Apply -WhatIf show the same
    line. The check runs again right before each publish (a change that appears during the run is a plain error, exit 1). The comparisons
    are case-STRICT (a case-only change is someone else's edit).

    IF DATAVERSE NORMALISES THE XML ON SAVE. After each PATCH the script reads the unpublished text back and uses THAT as the text the
    pre-publish re-check expects, so the first live -Apply is not stopped (exit 1, after the writes) by a harmless normalisation. The
    pre-WRITE check of a re-run still compares the pending text with the text this run would write (the script cannot know how Dataverse
    would normalise it): after a stop before the publish, a pending text that Dataverse normalised differently is refused (exit 2,
    "has unpublished changes that are not the edit this script would write"). That mismatch means the pending text is not byte-for-byte
    this script's edit; inspect the form or view, and publish the pending edit in the designer, which clears it, then re-run.
    EXIT CODES. 0 = done, report or verified. 1 = an error, or a stop AFTER the first write (the retirement is partly applied: re-run to
    resume). 2 = refused BEFORE the first write (nothing changed): a blocker, a missing approval or attestation, the wrong environment, a
    pending-collateral refusal, or a caller who cannot read every row.

    ATTESTATIONS. The dev BFF and external SPA are deployed from other branches and this script cannot see what is live. The operator
    attests the commit each was built from; the script checks THAT commit's source by content (project CLAUDE.md section 6: a squash
    merge breaks ancestry checks). 015's check ("git grep the column name") does not fit here: sprk_matter, sprk_project and
    sprk_workassignment are columns of many tables and appear all over the source. The check is therefore specific to the rate card:
      1. no file under the scanned paths names one of the three relationships, and
      2. no file that names the rate card table (sprk_ratecard / sprk_ratecards / RateCardEntity) also names one of the three columns
         (sprk_matter, sprk_project, sprk_workassignment, or their _..._value filter spelling).
    Scanned paths: -BffDeployAttestation src/server; -SpaDeployAttestation src/client and src/solutions (the external SPA, the shared UI
    library and the code pages) and the two web resource folders under src/dataverse (webresources and
    solutions/SpaarkeMaster/WebResources: form scripts and HTML web resources that Dataverse serves). Limits, stated plainly:
      - Rule 2 is a file-level heuristic: it can refuse a file that names both for an unrelated reason (the refusal names the file; fix
        or reword it);
      - it cannot see a reader that reaches the table or a column through a name it never spells in one file (a constant defined in
        another file, a name assembled at run time);
      - a code page or web resource deployed from an older commit than the attested one is not covered; the owner confirms every deployed
        surface is at or after the attested commit.
    A path that does not exist at the commit is refused (a missing path would pass the scan vacuously).

    Web API calls: the census, the form and view edits and the relationship deletes go through EbillingSchema.psm1 (Consistency: Strong,
    #2833). The pending-collateral reads and PublishXml go through the shared scripts/lib/Publish-SolutionComponents.ps1
    (Get-EntityPublishCollateral, Invoke-PublishXml), whose context carries no Consistency header of its own: Get-PublishContext sets
    Consistency: Strong on it (F4). Invoke-PublishXml has no retry of its own, so this script wraps it in the module's bounded transient
    retry (Invoke-WithTransientRetry: up to 4 attempts, 5 s / 10 s / 20 s backoff; any other error fails at once).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else dev. -Apply refuses anything but spaarkedev1.crm.dynamics.com.

.PARAMETER Apply
    Perform the retirement. Without it the script only reports.

.PARAMETER Verify
    Check that the retirement is complete (read-only; exit 1 when something remains).

.PARAMETER AllowPendingCollateral
    With -Apply: go on although other pending (unpublished) forms or views of sprk_ratecard would be published with it (D-83). Give it only
    after the owner of those changes agrees, or after checking that they are this script's own half-finished edit.

.PARAMETER OwnerApproval
    Required with -Apply (not with -Apply -WhatIf): free text recorded in the output, e.g. the date and who gave the owner's OK. Project
    CLAUDE.md section 5: destructive dev changes need the owner's explicit OK first.

.PARAMETER BffDeployAttestation
    Required with -Apply: a commit sha, the commit the BFF deployed to dev was built from.

.PARAMETER SpaDeployAttestation
    Required with -Apply: a commit sha, the commit the external SPA (and the code pages) deployed to dev was built from.

.EXAMPLE
    pwsh scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1
    pwsh scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1 -Apply -WhatIf -OwnerApproval 'dry run' -BffDeployAttestation <bff-sha> -SpaDeployAttestation <spa-sha>
    pwsh scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1 -Apply -OwnerApproval '<date and who>' -BffDeployAttestation <bff-sha> -SpaDeployAttestation <spa-sha> -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 193. Exit codes: 0 = done / report / verified, 1 = error, verification failed, or stopped after the first
    write (partly applied: re-run), 2 = refused before the first write (blocker, missing approval or attestation, wrong environment,
    pending collateral). Other environments lose the columns only through a SpaarkeMaster upgrade
    import (an update import leaves managed columns behind): see notes/193-retire-ratecard-core-lookups.md.
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$Apply,
    [switch]$Verify,
    [switch]$AllowPendingCollateral,
    [string]$OwnerApproval,
    [string]$BffDeployAttestation,
    [string]$SpaDeployAttestation
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
# The shared D-83 procedure: Get-EntityPublishCollateral, Invoke-PublishXml (the only publish call the repo allows), Get-DataverseApiContext.
. (Join-Path $PSScriptRoot '..' 'lib' 'Publish-SolutionComponents.ps1')
$DryRun = [bool]$WhatIfPreference
$DevHost = 'spaarkedev1.crm.dynamics.com'
$script:WriteStarted = $false

$Table = 'sprk_ratecard'
$EntitySet = 'sprk_ratecards'
$IdColumn = 'sprk_ratecardid'

# The three lookups and the one relationship each sits behind. Nothing outside this table is ever named for a delete.
$Targets = @(
    @{ Column = 'sprk_matter';          Relationship = 'sprk_matter_ratecard_matter';                        Referenced = 'sprk_matter' }
    @{ Column = 'sprk_project';         Relationship = 'sprk_project_ratecard_project';                      Referenced = 'sprk_project' }
    @{ Column = 'sprk_workassignment';  Relationship = 'sprk_workassignment_ratecard_workassignment';        Referenced = 'sprk_workassignment' }
)

# A mention of one of the three columns in form / view / grid text: the bare name, or the _..._value filter spelling. The name must not
# continue into a longer one (sprk_matterid is another column). Case-insensitive (PhysicalName spells sprk_Matter).
$CoreLookupPattern = '(?i)(?<![A-Za-z0-9])sprk_(?:matter|project|workassignment)(?![A-Za-z0-9])'

# The attestation scans (git grep -E, case-insensitive). See the help for why the rate card is the anchor.
$RelationshipNamesRegex = 'sprk_matter_ratecard_matter|sprk_project_ratecard_project|sprk_workassignment_ratecard_workassignment'
$RateCardMentionRegex = 'sprk_ratecards?([^A-Za-z0-9_]|$)|RateCardEntity'
$CoreColumnMentionRegex = '(^|[^A-Za-z0-9])sprk_(matter|project|workassignment)([^A-Za-z0-9]|$)'
$BffScanPaths = @('src/server')
$SpaScanPaths = @('src/client', 'src/solutions', 'src/dataverse/webresources', 'src/dataverse/solutions/SpaarkeMaster/WebResources')
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# ---------------------------------------------------------------------------------------------------------------
# Pre-flight (pure apart from git): the refusals that need no Dataverse call
# ---------------------------------------------------------------------------------------------------------------

# The problems with one attested commit, empty = the source at that commit does not select the three rate-card columns. Git only.
function Get-ReaderProblems([string]$Sha, [string[]]$Paths, [string]$Parameter) {
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($Sha -notmatch '^[0-9a-fA-F]{7,40}$') { $problems.Add("$Parameter '$Sha' is not a commit sha (7 to 40 hex characters)"); return $problems.ToArray() }
    $null = & git -C $RepoRoot cat-file -e "${Sha}^{commit}" 2>&1
    if ($LASTEXITCODE -ne 0) { $problems.Add("$Parameter '$Sha' is not a commit in this repository (git cat-file -e failed): fetch it, or pass the right sha"); return $problems.ToArray() }
    # Non-vacuity: a scan of a path that is not in the commit finds nothing and would pass.
    foreach ($p in $Paths) {
        $null = & git -C $RepoRoot cat-file -e "${Sha}:${p}" 2>&1
        if ($LASTEXITCODE -ne 0) { $problems.Add("$Parameter ${Sha}: $p does not exist at that commit, so the source scan would pass without reading anything") }
    }
    if ($problems.Count -gt 0) { return $problems.ToArray() }

    # 1. The relationship names, anywhere.
    $hits = @(& git -C $RepoRoot grep -l -i -E $RelationshipNamesRegex $Sha -- @Paths 2>&1)
    if ($LASTEXITCODE -gt 1) { $problems.Add("$Parameter ${Sha}: git grep failed ($($hits -join ' '))"); return $problems.ToArray() }
    if ($LASTEXITCODE -eq 0) {
        $names = @($hits | ForEach-Object { ([string]$_) -replace '^[^:\s]+:', '' })
        $problems.Add("$Parameter ${Sha}: $($names.Count) file(s) name one of the three rate-card relationships at that commit: $($names -join ', ')")
    }
    # 2. A file that names the rate card table and also one of the three columns.
    $hits = @(& git -C $RepoRoot grep -l --all-match -i -E -e $RateCardMentionRegex -e $CoreColumnMentionRegex $Sha -- @Paths 2>&1)
    if ($LASTEXITCODE -gt 1) { $problems.Add("$Parameter ${Sha}: git grep failed ($($hits -join ' '))"); return $problems.ToArray() }
    if ($LASTEXITCODE -eq 0) {
        $names = @($hits | ForEach-Object { ([string]$_) -replace '^[^:\s]+:', '' })
        $problems.Add("$Parameter ${Sha}: $($names.Count) file(s) name the rate card table and one of sprk_matter / sprk_project / sprk_workassignment at that commit (a build from it may select the columns): $($names -join ', ')")
    }
    return $problems.ToArray()
}

# Everything that makes -Apply refuse before any Dataverse call. Empty = go on.
function Get-PreflightRefusals([bool]$ApplyRequested, [bool]$DryRunMode, [string]$Approval, [string]$Bff, [string]$Spa, [string]$Url) {
    $r = [System.Collections.Generic.List[string]]::new()
    if (-not $ApplyRequested) { return $r.ToArray() }
    if (-not $DryRunMode -and [string]::IsNullOrWhiteSpace($Approval)) { $r.Add("-OwnerApproval '<date and who>' is required: destructive dev changes need the owner's explicit OK first (project CLAUDE.md section 5)") }
    if ([string]::IsNullOrWhiteSpace($Bff)) { $r.Add('-BffDeployAttestation <commit sha> is required: the commit the BFF deployed to dev was built from') }
    if ([string]::IsNullOrWhiteSpace($Spa)) { $r.Add('-SpaDeployAttestation <commit sha> is required: the commit the external SPA (and the code pages) deployed to dev was built from') }
    $hostName = try { ([uri]$Url).Host } catch { '' }
    if ($hostName -ne $DevHost) { $r.Add("-Apply is for dev only ($DevHost), not '$Url'") }
    if ($r.Count -gt 0) { return $r.ToArray() }
    foreach ($x in (Get-ReaderProblems $Bff $BffScanPaths '-BffDeployAttestation')) { $r.Add($x) }
    foreach ($x in (Get-ReaderProblems $Spa $SpaScanPaths '-SpaDeployAttestation')) { $r.Add($x) }
    return $r.ToArray()
}

# ---------------------------------------------------------------------------------------------------------------
# Pure transforms (XML text in, text out)
# ---------------------------------------------------------------------------------------------------------------

function Test-MentionsCoreLookup([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return $false }
    return [regex]::IsMatch($Text, $CoreLookupPattern)
}

function Get-MentionCount([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return 0 }
    return [regex]::Matches($Text, $CoreLookupPattern).Count
}

# Form: removes each cell whose control is bound to one of the columns (and its row when the row is left empty). Remaining = mentions of
# the columns left in the result (events, header cells, anything else): the caller refuses on > 0.
function ConvertTo-FormWithoutCoreLookups([string]$FormXml) {
    $doc = [xml]$FormXml
    $removed = [System.Collections.Generic.List[string]]::new()
    $xpath = '//control[' + (($Targets | ForEach-Object { "@datafieldname='$($_.Column)'" }) -join ' or ') + ']'
    foreach ($ctl in @($doc.SelectNodes($xpath))) {
        $cell = $ctl.SelectSingleNode('ancestor::cell[1]')
        if (-not $cell -or -not $cell.ParentNode) { continue }
        $row = $cell.ParentNode
        [void]$row.RemoveChild($cell)
        $removed.Add("control $($ctl.GetAttribute('datafieldname'))")
        if ($row.LocalName -eq 'row' -and $row.SelectNodes('cell').Count -eq 0 -and $row.ParentNode) {
            [void]$row.ParentNode.RemoveChild($row)
            $removed.Add('an emptied row')
        }
    }
    $xml = $doc.OuterXml
    return [pscustomobject]@{ Xml = $xml; Removed = $removed.ToArray(); Remaining = (Get-MentionCount $xml) }
}

# View: removes the columns' fetch attributes and layout cells. A filter, order or link on a column is NOT removed (it would change what
# the view returns): it stays in Remaining and the caller refuses.
function ConvertTo-ViewWithoutCoreLookups([string]$FetchXml, [string]$LayoutXml) {
    $changes = [System.Collections.Generic.List[string]]::new()
    $fetchOut = $FetchXml
    if (-not [string]::IsNullOrWhiteSpace($FetchXml)) {
        $fetch = [xml]$FetchXml
        foreach ($t in $Targets) {
            foreach ($a in @($fetch.SelectNodes("//attribute[@name='$($t.Column)']"))) { [void]$a.ParentNode.RemoveChild($a); $changes.Add("fetch attribute $($t.Column)") }
        }
        $fetchOut = $fetch.OuterXml
    }
    $layoutOut = $LayoutXml
    if (-not [string]::IsNullOrWhiteSpace($LayoutXml)) {
        $layout = [xml]$LayoutXml
        foreach ($t in $Targets) {
            foreach ($c in @($layout.SelectNodes("//cell[@name='$($t.Column)']"))) { [void]$c.ParentNode.RemoveChild($c); $changes.Add("layout cell $($t.Column)") }
        }
        $layoutOut = $layout.OuterXml
    }
    return [pscustomobject]@{ FetchXml = $fetchOut; LayoutXml = $layoutOut; Changes = $changes.ToArray(); Remaining = ((Get-MentionCount $fetchOut) + (Get-MentionCount $layoutOut)) }
}

# ---------------------------------------------------------------------------------------------------------------
# Census (read-only: everything here only GETs)
# ---------------------------------------------------------------------------------------------------------------

function Read-Dv([string]$Path) { return Invoke-Dv -Path $Path }
function Read-DvOrNull([string]$Path) { return Get-DvOrNull -Path $Path }

# Short names for the component types RetrieveDependenciesForDelete reports.
function Get-ComponentKind([int]$Type) {
    switch ($Type) {
        1 { 'entity' }
        2 { 'attribute' }
        10 { 'relationship' }
        26 { 'view' }
        59 { 'chart' }
        60 { 'system form' }
        80 { 'app module' }
        default { "component type $Type" }
    }
}

function Get-DependencyList([string]$ObjectId, [int]$ComponentType) {
    return @(foreach ($d in @((Read-Dv "RetrieveDependenciesForDelete(ObjectId=$ObjectId,ComponentType=$ComponentType)").value)) {
        [pscustomobject]@{ Kind = (Get-ComponentKind ([int]$d.dependentcomponenttype)); ObjectId = [string]$d.dependentcomponentobjectid }
    })
}

function Get-State([hashtable]$ReadCheck = @{}) {
    # sprk_ratecard is user-owned: a count taken as a caller who cannot read every row is silently short. Checked once, before the first
    # count, and only while a column still exists; when the caller is not shown to read every row, nothing is counted (NonNull stays
    # $null = unknown) and Get-ApplyBlockers refuses. $ReadCheck is one run's cache (Invoke-Retirement passes the same one to every pass of
    # its loops): a PASS is remembered and not asked again (two Web API calls and a privilege list per pass otherwise); a GAP is never
    # remembered, so it is checked again and still refuses. A standalone call gets a fresh cache and checks.
    $readGap = $null; $readChecked = $false
    $columns = @(foreach ($t in $Targets) {
        $c = [ordered]@{
            Column = $t.Column; Relationship = $t.Relationship; Referenced = $t.Referenced
            AttributePresent = $false; AttributeType = $null; AttributeId = $null; NonNull = $null; Dependencies = @()
            RelationshipPresent = $false; RelationshipId = $null; RelReferencing = $null; RelReferencingAttribute = $null; RelReferenced = $null; RelationshipDependencies = @()
        }
        $attr = Get-Attr $Table $t.Column 'LogicalName,AttributeType,MetadataId'
        if ($attr) {
            $c.AttributePresent = $true; $c.AttributeType = $attr.AttributeType; $c.AttributeId = $attr.MetadataId
            if (-not $readChecked) {
                if (-not $ReadCheck.Passed) { $readGap = Get-ReadAllRowsGap $Table; if (-not $readGap) { $ReadCheck.Passed = $true } }
                $readChecked = $true
            }
            # Rows of every state (no statecode filter). A count only; ids are never read.
            if (-not $readGap) { $c.NonNull = Get-RowCountExact $EntitySet $IdColumn "_$($t.Column)_value ne null" }
            $c.Dependencies = @(Get-DependencyList $attr.MetadataId 2)
        }
        $rel = Read-DvOrNull "RelationshipDefinitions(SchemaName='$($t.Relationship)')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,MetadataId,ReferencingEntity,ReferencingAttribute,ReferencedEntity"
        if ($rel) {
            $c.RelationshipPresent = $true; $c.RelationshipId = $rel.MetadataId
            $c.RelReferencing = $rel.ReferencingEntity; $c.RelReferencingAttribute = $rel.ReferencingAttribute; $c.RelReferenced = $rel.ReferencedEntity
            $c.RelationshipDependencies = @(Get-DependencyList $rel.MetadataId 10)
        }
        [pscustomobject]$c
    })

    # Forms and views of sprk_ratecard that name a column, each with the cleaned text it would be written back as.
    $forms = @(foreach ($f in @((Read-Dv "systemforms?`$select=name,formid,type,formxml&`$filter=objecttypecode eq '$Table'").value)) {
        if (-not (Test-MentionsCoreLookup $f.formxml)) { continue }
        $res = ConvertTo-FormWithoutCoreLookups $f.formxml
        [pscustomobject]@{ Id = $f.formid; Name = $f.name; Type = $f.type; NewXml = $res.Xml; Removed = $res.Removed; Remaining = $res.Remaining }
    })
    $views = @(foreach ($v in @((Read-Dv "savedqueries?`$select=name,savedqueryid,fetchxml,layoutxml&`$filter=returnedtypecode eq '$Table'").value)) {
        if (-not ((Test-MentionsCoreLookup $v.fetchxml) -or (Test-MentionsCoreLookup $v.layoutxml))) { continue }
        $res = ConvertTo-ViewWithoutCoreLookups $v.fetchxml $v.layoutxml
        [pscustomobject]@{ Id = $v.savedqueryid; Name = $v.name; HasLayout = (-not [string]::IsNullOrWhiteSpace($v.layoutxml)); NewFetch = $res.FetchXml; NewLayout = $res.LayoutXml; Changes = $res.Changes; Remaining = $res.Remaining }
    })
    $personal = @(foreach ($u in @((Read-Dv "userqueries?`$select=name,userqueryid,fetchxml,layoutxml&`$filter=returnedtypecode eq '$Table'").value)) {
        if ((Test-MentionsCoreLookup $u.fetchxml) -or (Test-MentionsCoreLookup $u.layoutxml)) { [pscustomobject]@{ Name = $u.name } }
    })
    $gridPage = Read-DvOrNull "sprk_gridconfigurations?`$select=sprk_name,sprk_configjson&`$filter=contains(sprk_configjson,'$Table')"
    $grids = @(foreach ($g in @($(if ($gridPage) { $gridPage.value }))) {
        if (Test-MentionsCoreLookup $g.sprk_configjson) { [pscustomobject]@{ Name = $g.sprk_name } }
    })

    return [pscustomobject]@{ Columns = $columns; Forms = $forms; Views = $views; PersonalViews = $personal; GridConfigs = $grids; ReadGap = $readGap }
}

function Write-State($State) {
    if ($State.ReadGap) { Write-Host "read access     : $($State.ReadGap)" -ForegroundColor Red }
    foreach ($c in $State.Columns) {
        Write-Host "$Table.$($c.Column)" -ForegroundColor Cyan
        if (-not $c.AttributePresent) { Write-Host '  column         : not found (already removed)' -ForegroundColor Yellow }
        else {
            Write-Host "  column         : $($c.AttributeType) (MetadataId $($c.AttributeId))"
            Write-Host "  rows with value: $(if ($null -eq $c.NonNull) { 'unknown' } else { $c.NonNull })" -ForegroundColor $(if ($c.NonNull -eq 0) { 'Green' } else { 'Red' })
            Write-Host "  dependents     : $(@($c.Dependencies).Count)"
            foreach ($d in @($c.Dependencies)) { Write-Host "    $($d.Kind) $($d.ObjectId)" }
        }
        if (-not $c.RelationshipPresent) { Write-Host "  relationship   : $($c.Relationship) not found (already removed)" -ForegroundColor Yellow }
        else {
            Write-Host "  relationship   : $($c.Relationship) ($($c.RelReferencing).$($c.RelReferencingAttribute) -> $($c.RelReferenced); MetadataId $($c.RelationshipId))"
            Write-Host "  rel. dependents: $(@($c.RelationshipDependencies).Count)"
            foreach ($d in @($c.RelationshipDependencies)) { Write-Host "    $($d.Kind) $($d.ObjectId)" }
        }
    }
    Write-Host "$Table forms naming a column     : $(@($State.Forms).Count)"
    foreach ($f in @($State.Forms)) { Write-Host "  form '$($f.Name)' ($($f.Id), type $($f.Type)): removes $($f.Removed -join ', '); mentions left $($f.Remaining)" }
    Write-Host "$Table views naming a column     : $(@($State.Views).Count)"
    foreach ($v in @($State.Views)) { Write-Host "  view '$($v.Name)' ($($v.Id)): removes $($v.Changes -join ', '); mentions left $($v.Remaining)" }
    Write-Host "$Table personal views naming one : $(@($State.PersonalViews).Count)"
    foreach ($p in @($State.PersonalViews)) { Write-Host "  personal view '$($p.Name)'" }
    Write-Host "grid configurations naming one   : $(@($State.GridConfigs).Count)"
    foreach ($g in @($State.GridConfigs)) { Write-Host "  grid configuration '$($g.Name)'" }
}

# ---------------------------------------------------------------------------------------------------------------
# Verdicts (pure: a state in, strings out)
# ---------------------------------------------------------------------------------------------------------------

# True when nothing is left to do: no attribute, no relationship, and no form, view, personal view or grid configuration names a column.
function Test-AlreadyRetired($State) {
    foreach ($c in $State.Columns) { if ($c.AttributePresent -or $c.RelationshipPresent) { return $false } }
    return (@($State.Forms).Count + @($State.Views).Count + @($State.PersonalViews).Count + @($State.GridConfigs).Count) -eq 0
}

# The reasons -Apply must refuse. Empty = READY. Counts only: no row id is ever named.
function Get-ApplyBlockers($State, $Pending = $null, [bool]$AllowPending = $false) {
    $b = [System.Collections.Generic.List[string]]::new()
    # The forms and views this script cleans are expected dependents of a column; anything else is not.
    $known = @(@($State.Forms | ForEach-Object { $_.Id }) + @($State.Views | ForEach-Object { $_.Id }))
    # Before any count is believed: the caller must be shown to read every row (nothing was counted when it is not).
    if ($State.ReadGap) { $b.Add("${Table}: $($State.ReadGap)") }
    foreach ($c in $State.Columns) {
        $col = $c.Column
        if ($c.AttributePresent) {
            if ($c.AttributeType -ne 'Lookup') { $b.Add("$Table.$col is $($c.AttributeType), not a Lookup") }
            if ($null -eq $c.NonNull) { if (-not $State.ReadGap) { $b.Add("$Table.${col}: the rate cards holding a value could not be counted") } }   # with a read gap nothing was counted, and the gap is already a blocker
            elseif ($c.NonNull -gt 0) { $b.Add("$($c.NonNull) $Table row(s) hold a value in $col") }
            if (-not $c.RelationshipPresent) { $b.Add("$Table.$col exists but the relationship $($c.Relationship) was not found, so the column cannot be removed through it") }
            foreach ($d in @($c.Dependencies)) {
                if ($known -notcontains $d.ObjectId -and $d.ObjectId -ne $c.RelationshipId) { $b.Add("$Table.${col}: dependent $($d.Kind) $($d.ObjectId)") }
            }
        }
        elseif ($c.RelationshipPresent) { $b.Add("the relationship $($c.Relationship) exists although $Table.$col does not") }
        if ($c.RelationshipPresent) {
            if ($c.RelReferencing -ne $Table -or $c.RelReferencingAttribute -ne $col -or $c.RelReferenced -ne $c.Referenced) {
                $b.Add("the relationship $($c.Relationship) is not $Table.$col -> $($c.Referenced) (found $($c.RelReferencing).$($c.RelReferencingAttribute) -> $($c.RelReferenced))")
            }
            foreach ($d in @($c.RelationshipDependencies)) {
                if ($known -notcontains $d.ObjectId -and $d.ObjectId -ne $c.AttributeId) { $b.Add("relationship $($c.Relationship): dependent $($d.Kind) $($d.ObjectId)") }
            }
        }
    }
    foreach ($f in @($State.Forms)) { if ($f.Remaining -gt 0) { $b.Add("form '$($f.Name)' would still name a core-record lookup after the clean-up ($($f.Remaining) mention(s)): edit it by hand") } }
    foreach ($v in @($State.Views)) { if ($v.Remaining -gt 0) { $b.Add("view '$($v.Name)' would still name a core-record lookup after the clean-up ($($v.Remaining) mention(s): a filter, order or link): edit it by hand") } }
    foreach ($p in @($State.PersonalViews)) { $b.Add("personal view '$($p.Name)' names a core-record lookup") }
    foreach ($g in @($State.GridConfigs)) { $b.Add("grid configuration '$($g.Name)' names a core-record lookup") }
    # D-83 and the unpublished edits of the forms and views this script edits (Get-PendingReport).
    if ($Pending) {
        foreach ($x in @($Pending.Own)) { $b.Add($x) }
        if (-not $AllowPending) { foreach ($x in @($Pending.Foreign)) { $b.Add("pending (unpublished) item would go live with the publish (D-83; -AllowPendingCollateral after the owner agrees): $x") } }
    }
    return $b.ToArray()
}

# What -Verify (and the end of -Apply) reports as still there. Empty = retired.
function Get-VerifyProblems($State) {
    $p = [System.Collections.Generic.List[string]]::new()
    foreach ($c in $State.Columns) {
        if ($c.AttributePresent) { $p.Add("$Table.$($c.Column) still exists") }
        if ($c.RelationshipPresent) { $p.Add("relationship $($c.Relationship) still exists") }
    }
    foreach ($f in @($State.Forms)) { $p.Add("form '$($f.Name)' still names a core-record lookup") }
    foreach ($v in @($State.Views)) { $p.Add("view '$($v.Name)' still names a core-record lookup") }
    foreach ($x in @($State.PersonalViews)) { $p.Add("personal view '$($x.Name)' still names a core-record lookup") }
    foreach ($g in @($State.GridConfigs)) { $p.Add("grid configuration '$($g.Name)' still names a core-record lookup") }
    return $p.ToArray()
}

# ---------------------------------------------------------------------------------------------------------------
# Writes (only reached through -Apply after the refusals are empty; each asks Confirm-Write first, so -WhatIf writes nothing)
# ---------------------------------------------------------------------------------------------------------------

function Confirm-Write([string]$Target, [string]$Action) { return $PSCmdlet.ShouldProcess($Target, $Action) }

function Edit-FormsAndViews($State) {
    $edited = 0
    foreach ($f in @($State.Forms)) {
        if (-not (Confirm-Write "form '$($f.Name)' ($($f.Id))" "PATCH formxml: remove $($f.Removed -join ', ')")) { continue }
        $script:WriteStarted = $true
        Invoke-Dv -Path "systemforms($($f.Id))" -Method PATCH -Body @{ formxml = $f.NewXml } | Out-Null
        $edited++; Write-Host "  EDITED   form '$($f.Name)': $($f.Removed -join ', ')" -ForegroundColor Green
        # K2: read the unpublished text back. It is what the pre-publish re-check expects, so a Dataverse that normalises form XML on
        # save does not stop the first run (exit 1) after the writes.
        $back = Read-Dv "systemforms($($f.Id))/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=formxml"
        if (-not [string]::IsNullOrWhiteSpace($back.formxml)) { $f | Add-Member -NotePropertyName WrittenXml -NotePropertyValue $back.formxml -Force }
    }
    foreach ($v in @($State.Views)) {
        if (-not (Confirm-Write "view '$($v.Name)' ($($v.Id))" "PATCH fetchxml / layoutxml: remove $($v.Changes -join ', ')")) { continue }
        $body = @{ fetchxml = $v.NewFetch }
        if ($v.HasLayout) { $body.layoutxml = $v.NewLayout }
        $script:WriteStarted = $true
        Invoke-Dv -Path "savedqueries($($v.Id))" -Method PATCH -Body $body | Out-Null
        $edited++; Write-Host "  EDITED   view '$($v.Name)': $($v.Changes -join ', ')" -ForegroundColor Green
        # K2: the same read-back for the view (fetchxml and layoutxml).
        $back = Read-Dv "savedqueries($($v.Id))/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=fetchxml,layoutxml"
        if (-not [string]::IsNullOrWhiteSpace($back.fetchxml)) { $v | Add-Member -NotePropertyName WrittenFetch -NotePropertyValue $back.fetchxml -Force }
        if ($v.HasLayout -and -not [string]::IsNullOrWhiteSpace($back.layoutxml)) { $v | Add-Member -NotePropertyName WrittenLayout -NotePropertyValue $back.layoutxml -Force }
    }
    if (@($State.Forms).Count + @($State.Views).Count -eq 0) { Write-Host '  nothing to edit: no form or view names a column' -ForegroundColor Yellow }
    return $edited
}

# The token context of the shared procedure (az CLI identity), made on first use.
function Get-PublishContext {
    if (-not $script:PublishContext) {
        $script:PublishContext = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl
        # The shared context carries no Consistency header of its own: set here, so the pending-collateral reads and the PublishXml POST
        # are strongly consistent too (F4, #2833).
        $script:PublishContext.Headers['Consistency'] = 'Strong'
    }
    return $script:PublishContext
}

# D-83: an entity publish carries EVERY pending form and view of the table, not only the ones this script edited. Read only.
function ConvertTo-CanonicalXml([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    try { return ([xml]$Text).OuterXml } catch { return $Text }
}

# The id inside the trailing parentheses of a collateral line ("systemforms: name (id)").
function Get-CollateralItemId([string]$Line) {
    if ($Line -match '\(([^()]+)\)\s*$') { return $Matches[1].Trim('{', '}').ToLowerInvariant() }
    return ''
}

# Every pending item of sprk_ratecard, the forms and views this script edits NOT excluded:
#   Own     = a form or view this script edits that has unpublished changes which are NOT exactly the text this run writes (the unpublished
#             text is read and compared after XML normalisation). Publishing would overwrite them: never overridable.
#   Foreign = everything else that is pending: it would go live with the publish. -AllowPendingCollateral may accept these.
# An item whose pending text equals this run's text is a resume (or the edit this run just made): not reported.
function Get-PendingReport($State) {
    $own = [System.Collections.Generic.List[string]]::new()
    $foreign = [System.Collections.Generic.List[string]]::new()
    $targets = @{}
    foreach ($f in @($State.Forms)) { $targets[([string]$f.Id).Trim('{', '}').ToLowerInvariant()] = @{ Kind = 'form'; Item = $f } }
    foreach ($v in @($State.Views)) { $targets[([string]$v.Id).Trim('{', '}').ToLowerInvariant()] = @{ Kind = 'view'; Item = $v } }
    foreach ($line in @(Get-EntityPublishCollateral -Context (Get-PublishContext) -Entity $Table)) {
        $id = Get-CollateralItemId $line
        if (-not $targets.ContainsKey($id)) { $foreign.Add($line); continue }
        $t = $targets[$id]; $item = $t.Item
        # What this run expects to find pending: the text read back right after its own PATCH when there is one (K2: Dataverse may
        # normalise the XML on save), else the text it would write. Every comparison is case-STRICT (-ceq): a case-only change, e.g. of a
        # label, is someone else's edit, not this run's text.
        if ($t.Kind -eq 'form') {
            $u = Read-Dv "systemforms($($item.Id))/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=formxml"
            $expected = if ($item.PSObject.Properties['WrittenXml']) { $item.WrittenXml } else { $item.NewXml }
            $same = (ConvertTo-CanonicalXml $u.formxml) -ceq (ConvertTo-CanonicalXml $expected)
        }
        else {
            $u = Read-Dv "savedqueries($($item.Id))/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=fetchxml,layoutxml"
            $expectedFetch = if ($item.PSObject.Properties['WrittenFetch']) { $item.WrittenFetch } else { $item.NewFetch }
            $expectedLayout = if ($item.PSObject.Properties['WrittenLayout']) { $item.WrittenLayout } else { $item.NewLayout }
            $same = ((ConvertTo-CanonicalXml $u.fetchxml) -ceq (ConvertTo-CanonicalXml $expectedFetch)) -and
                    ((-not $item.HasLayout) -or ((ConvertTo-CanonicalXml $u.layoutxml) -ceq (ConvertTo-CanonicalXml $expectedLayout)))
        }
        if (-not $same) { $own.Add("$($t.Kind) '$($item.Name)' ($($item.Id)) has unpublished changes that are not the edit this script would write (publishing would overwrite them): $line") }
    }
    return [pscustomobject]@{ Own = $own.ToArray(); Foreign = $foreign.ToArray() }
}

function Write-PendingReport($Pending) {
    $n = @($Pending.Own).Count + @($Pending.Foreign).Count
    Write-Host "pending (unpublished) items on ${Table}: $(if ($n -eq 0) { 'none' } else { $n })" -ForegroundColor $(if ($n -eq 0) { 'Cyan' } else { 'Yellow' })
    foreach ($x in @($Pending.Own)) { Write-Host "  OVERWRITE RISK  $x" -ForegroundColor Red }
    foreach ($x in @($Pending.Foreign)) { Write-Host "  would go live   $x" -ForegroundColor Yellow }
}

# The only publish: sprk_ratecard, entity-scoped, through the shared procedure. The core tables are never published (deleting a
# relationship needs no publish of them). Pending items are re-read right before the publish; they were checked up front, so one found
# here appeared during the run: a plain error (exit 1, partly applied), never an exit-2 "nothing changed". The script's own edit (its
# forms and views pending with exactly this run's text) is not reported.
function Publish-RateCard($State, [bool]$AllowPending) {
    if (-not (Confirm-Write $Table 'PublishXml (entity-scoped, through scripts/lib/Publish-SolutionComponents.ps1, never tenant-wide)')) { return }
    $pending = Get-PendingReport $State
    if (@($pending.Own).Count -gt 0) {
        throw "Refusing to publish ${Table}: a form or view this script edits changed during the run and publishing would overwrite it: $(@($pending.Own) -join '; '). Nothing was published by this step; the retirement is partly applied, so check it and re-run to resume."
    }
    if (@($pending.Foreign).Count -gt 0 -and -not $AllowPending) {
        throw "Refusing to publish ${Table}: $(@($pending.Foreign).Count) other pending form(s) / view(s) appeared and would go live too: $(@($pending.Foreign) -join '; '). Nothing was published by this step; the retirement is partly applied, so re-run to resume after the owner of those changes agrees (-AllowPendingCollateral)."
    }
    # The shared Invoke-PublishXml has no retry of its own: a publish lock or throttling is retried with the module's transient test and
    # backoff (bounded); any other error fails at once.
    Invoke-WithTransientRetry { Invoke-PublishXml -Context (Get-PublishContext) -ParameterXml "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }
    Write-Host "  published $Table" -ForegroundColor Green
}
function Remove-Relationships($State) {
    $deleted = 0
    foreach ($c in @($State.Columns)) {
        if (-not $c.RelationshipPresent) { Write-Host "  skipped  $($c.Relationship) already gone" -ForegroundColor Yellow; continue }
        if (-not (Confirm-Write "relationship $($c.Relationship) ($Table.$($c.Column))" 'DELETE the relationship (the lookup column and its name companion go with it)')) { continue }
        $script:WriteStarted = $true
        Invoke-WhilePropagating { Invoke-Dv -Path "RelationshipDefinitions($($c.RelationshipId))" -Method DELETE | Out-Null } | Out-Null
        $deleted++; Write-Host "  DELETED  relationship $($c.Relationship)" -ForegroundColor Green
    }
    return $deleted
}

# Reads again until the columns are gone (a metadata delete lags a few seconds). Throws when one is still readable.
function Wait-ColumnsGone($State) {
    foreach ($c in @($State.Columns | Where-Object { $_.AttributePresent })) {
        $gone = $false
        for ($i = 0; $i -lt 18 -and -not $gone; $i++) {
            $gone = -not (Get-Attr $Table $c.Column)
            if (-not $gone) { Start-Sleep -Seconds 5 }
        }
        if (-not $gone) { throw "$Table.$($c.Column) is still readable after the relationship delete." }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run (a function so the Pester tests can drive it against a test double of the Dataverse calls)
# ---------------------------------------------------------------------------------------------------------------

function Invoke-Retirement([bool]$ApplyRequested, [bool]$VerifyRequested, [bool]$DryRunMode, [string]$Approval, [bool]$AllowPending = $false) {
    $readCheck = @{}   # one run's cache of a passed read-all-rows check (see Get-State)
    $state = Get-State $readCheck
    Write-State $state
    $retired = Test-AlreadyRetired $state
    # The pending items (the forms and views this script edits are NOT excluded; read only), shown in every mode.
    $pending = [pscustomobject]@{ Own = @(); Foreign = @() }
    if (-not $retired) { $pending = Get-PendingReport $state; Write-PendingReport $pending }
    $blockers = @(Get-ApplyBlockers $state $pending $AllowPending)
    Write-Host ''
    if ($retired) { Write-Host "VERDICT: ALREADY RETIRED - no column, no relationship, no form or view names them" -ForegroundColor Yellow }
    elseif ($blockers.Count -eq 0) { Write-Host 'VERDICT: READY' -ForegroundColor Green }
    else { Write-Host 'VERDICT: BLOCKED' -ForegroundColor Red; foreach ($x in $blockers) { Write-Host "  - $x" -ForegroundColor Red } }

    if (-not $ApplyRequested -and -not $VerifyRequested) {
        Write-Host ''
        Write-Host 'Report only. Nothing was changed. To retire (after the owner''s OK and the deploy attestations): -Apply -OwnerApproval ''<date and who>'' -BffDeployAttestation <sha> -SpaDeployAttestation <sha>.' -ForegroundColor Cyan
        return
    }

    if ($ApplyRequested) {
        Write-Host ''
        Write-Host "Apply (owner approval: $(if ([string]::IsNullOrWhiteSpace($Approval)) { 'none, -WhatIf' } else { $Approval }))" -ForegroundColor Cyan
        if ($retired) { Write-Host '  nothing to do: already retired' -ForegroundColor Yellow }
        else {
            if ($blockers.Count -gt 0) { throw [EscalationException]::new("Refusing to apply: $($blockers.Count) reason(s): $($blockers -join '; '). Nothing was changed.") }

            # D-83, before the first write (the blockers above include the pending items): the publish carries every pending form and view
            # of the table.
            if (@($pending.Foreign).Count -gt 0) { Write-Host "  -AllowPendingCollateral: publishing $(@($pending.Foreign).Count) other pending item(s) of $Table too" -ForegroundColor Yellow }

            Write-Host '1. Forms and views of sprk_ratecard' -ForegroundColor Cyan
            $null = Edit-FormsAndViews $state
            # Always publish (an earlier run may have edited and stopped before publishing).
            Publish-RateCard $state $AllowPending

            Write-Host '2. Nothing may depend on a column or relationship any more' -ForegroundColor Cyan
            $after = $null; $left = @()
            for ($i = 0; $i -lt 6; $i++) {
                $after = Get-State $readCheck
                $left = @(Get-ApplyBlockers $after)
                if ($left.Count -eq 0) { break }
                Start-Sleep -Seconds 5
            }
            # A plain error, not an EscalationException: the form and view were already edited and published, so this is exit 1 (partly
            # applied; re-run to resume), not the exit-2 "nothing changed".
            if ($left.Count -gt 0) { throw "After the form and view edit $($left.Count) reason(s) remain: $($left -join '; '). No relationship was deleted. The retirement is partly applied (the form and view are edited and published): fix the reason(s), then re-run to resume." }
            Write-Host '  no dependents remain' -ForegroundColor Green

            Write-Host '3. Delete the relationships (the lookup columns go with them)' -ForegroundColor Cyan
            $deleted = Remove-Relationships $after
            if ($deleted -gt 0) { Wait-ColumnsGone $after }
            Publish-RateCard $state $AllowPending
        }
    }

    if ($VerifyRequested -or ($ApplyRequested -and -not $DryRunMode)) {
        Write-Host ''
        Write-Host 'Verify' -ForegroundColor Cyan
        $problems = @()
        for ($i = 0; $i -lt 6; $i++) {
            $problems = @(Get-VerifyProblems (Get-State $readCheck))
            if ($problems.Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        if ($problems.Count -gt 0) { throw "Verification failed: $($problems -join '; ')" }
        Write-Host '  the three attributes and relationships are gone; no sprk_ratecard form or view names them' -ForegroundColor Green
    }
}

# What the error handler tells the operator: after the first write the run is partly applied (re-run to resume); before it, nothing was
# written (an error in a read, e.g. the pending read, changes nothing). $script:WriteStarted is set right before each write.
function Get-ErrorTrailer {
    if ($script:WriteStarted) { return 'Steps already done stay done; re-run to continue (every step skips work already done).' }
    return 'Nothing was written (the error happened before the first write).'
}

# ---------------------------------------------------------------------------------------------------------------
# Entry
# ---------------------------------------------------------------------------------------------------------------

Write-Host ''
Write-Host "eBilling task 193 - retire the core-record lookups of $Table" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if ($Apply -and $DryRun) { 'APPLY -WhatIf (no writes; read-only GETs still run)' } elseif ($Apply) { 'APPLY' } elseif ($Verify) { 'VERIFY (read-only)' } else { 'REPORT (read-only)' })"
Write-Host ''

try {
    $refusals = @(Get-PreflightRefusals ([bool]$Apply) $DryRun $OwnerApproval $BffDeployAttestation $SpaDeployAttestation $EnvironmentUrl)
    if ($refusals.Count -gt 0) { throw [EscalationException]::new("Refusing to apply: $($refusals.Count) reason(s): $($refusals -join '; '). Nothing was changed.") }
    if ($Apply) { Write-Host "Pre-flight (git only, before any Dataverse call) passed: -BffDeployAttestation $BffDeployAttestation (src/server), -SpaDeployAttestation $SpaDeployAttestation (src/client, src/solutions, the web resources under src/dataverse)" -ForegroundColor Green }

    $null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun -Entity $Table
    Invoke-Retirement ([bool]$Apply) ([bool]$Verify) $DryRun $OwnerApproval ([bool]$AllowPendingCollateral)
}
catch [EscalationException] {
    Write-Host ''
    Write-Host "ESCALATION - REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host (Get-ErrorTrailer) -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($Apply -and $DryRun) { Write-Host 'WhatIf run: nothing was written (read-only GETs against Dataverse did run, after the git pre-flight).' -ForegroundColor Cyan }
exit 0
