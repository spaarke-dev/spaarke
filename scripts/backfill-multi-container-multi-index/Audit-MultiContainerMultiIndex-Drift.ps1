<#
.SYNOPSIS
    Drift audit (READ-ONLY) of `sprk_containerid` + `sprk_searchindexname` values across
    Spaarke parent entities (Matter / Project / Invoice / WorkAssignment / Event) and
    `sprk_document` records. Implements FR-BF-03 per design.md §5.4.

.DESCRIPTION
    *********************************************************************************
    *                                                                               *
    *  READ-ONLY  —  THIS SCRIPT NEVER WRITES TO DATAVERSE.                         *
    *                                                                               *
    *  Informational audit only. Do NOT promote this script to a write path.        *
    *  For mutations, use the dedicated backfill scripts:                           *
    *    - Backfill-MultiContainerMultiIndex-ParentRecords.ps1 (FR-BF-01)           *
    *    - Backfill-MultiContainerMultiIndex-Documents.ps1     (FR-BF-02)           *
    *                                                                               *
    *  This contract is enforced by:                                                *
    *    (a) using only HTTP GET against the Dataverse Web API                      *
    *    (b) zero references to Set-/Update-/New-/Remove-/Invoke- CRM cmdlets       *
    *    (c) zero PATCH/POST/PUT/DELETE Invoke-RestMethod calls                     *
    *  Reviewers MUST verify (b) and (c) via grep before approving any change.      *
    *                                                                               *
    *********************************************************************************

    For each parent record (sprk_matter / sprk_project / sprk_invoice /
    sprk_workassignment / sprk_event) and each sprk_document, the script:

      1. Reads stored `sprk_containerid` + `sprk_searchindexname` from the record.
      2. Computes the chain-derived value(s) using the SAME logic as the two
         write-path backfill scripts (so this audit is a true mirror):
           - Parent record:
               * if >=1 child Documents -> mode of child Documents' sprk_graphdriveid
               * if  0 child Documents  -> owner BU's current sprk_containerid
               * sprk_searchindexname derived by mapping the container id via §5.1
           - Document:
               * sprk_searchindexname derived by mapping its own sprk_graphdriveid via §5.1
      3. Compares stored vs derived. Classifies the row:

         - "override"  : stored value differs from BU's value AND the record's
                         modifiedon predates the BU's modifiedon for the
                         container/index fields. Heuristic indicator of an
                         intentional explicit override (e.g. Protected Matter).
                         Recommendation: no action — intentional (per INV-5).

         - "drift"     : stored value differs from BU's value AND the record's
                         modifiedon is OLDER than the BU's modifiedon. Per INV-3,
                         BU change does NOT propagate; coexistence is the design.
                         Recommendation: no action — BU changed post-create.

         - "anomaly"   : container is NOT present in the §5.1 map, OR a parent's
                         sprk_containerid does NOT match the mode of its child
                         Documents' sprk_graphdriveid, OR a Document's stored
                         sprk_searchindexname does not match the §5.1 mapping of
                         its sprk_graphdriveid. Recommendation: operator review.

    Output: dual format — CSV + Markdown — written to caller-supplied paths
    (default: `./audit-drift-{timestamp}.csv` + `./audit-drift-{timestamp}.md`).
    CSV is the spreadsheet pivot-table input; Markdown is the human-readable scan.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER OutputCsvPath
    Path for the CSV report. Defaults to `./audit-drift-{yyyyMMdd-HHmmss}.csv`.

.PARAMETER OutputMarkdownPath
    Path for the Markdown report. Defaults to `./audit-drift-{yyyyMMdd-HHmmss}.md`.

.PARAMETER EntityFilter
    Optional comma-separated entity logical-name filter (spot-check mode).
    Allowed values: sprk_matter, sprk_project, sprk_invoice, sprk_workassignment,
    sprk_event, sprk_document. If omitted, all six are audited.

.PARAMETER BatchSize
    Page size for Dataverse paging. Default 500.

.EXAMPLE
    # Full audit across all six entity types
    .\Audit-MultiContainerMultiIndex-Drift.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.EXAMPLE
    # Spot-check Matters only
    .\Audit-MultiContainerMultiIndex-Drift.ps1 `
        -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -EntityFilter "sprk_matter"

.EXAMPLE
    # Custom output paths
    .\Audit-MultiContainerMultiIndex-Drift.ps1 `
        -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
        -OutputCsvPath "./reports/drift-2026-06-07.csv" `
        -OutputMarkdownPath "./reports/drift-2026-06-07.md"

.NOTES
    Project: spaarke-multi-container-multi-index-r1
    Task:    052
    Spec:    spec.md FR-BF-03
    Design:  design.md §5.4 (drift audit), §5.1 (container -> index map),
             INV-3 (no BU fan-out), INV-5 (explicit overrides sacred)

    Conservative classification policy (per task notes):
    When in doubt between `override` and `drift`, classify as `anomaly` and let the
    operator decide. False positives in `anomaly` are recoverable (operator marks
    them no-action); false negatives that mask a real issue are NOT.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [string]$OutputCsvPath,

    [string]$OutputMarkdownPath,

    [string]$EntityFilter,

    [int]$BatchSize = 500
)

# -----------------------------------------------------------------------------
# Hard guard: this script is READ-ONLY. The next line is the canonical assertion.
# -----------------------------------------------------------------------------
$Script:READ_ONLY_CONTRACT = $true
$Script:WRITE_METHODS_FORBIDDEN = @('PATCH', 'POST', 'PUT', 'DELETE')

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

# Resolve default output paths with timestamp
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($OutputCsvPath))       { $OutputCsvPath      = "./audit-drift-$timestamp.csv" }
if ([string]::IsNullOrWhiteSpace($OutputMarkdownPath))  { $OutputMarkdownPath = "./audit-drift-$timestamp.md"  }

# -----------------------------------------------------------------------------
# §5.1 hardcoded container -> index map (design.md §5.1)
# -----------------------------------------------------------------------------
$ContainerIndexMap = @{
    'b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50' = 'spaarke-knowledge-index-v2'
    'b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh' = 'spaarke-file-index'
}

# -----------------------------------------------------------------------------
# Parent entity catalog. Each entry: logical name, plural collection,
# primary-id attribute, primary-name attribute, child-Document lookup attribute.
# -----------------------------------------------------------------------------
$ParentEntities = @(
    [pscustomobject]@{
        Logical    = 'sprk_matter'
        Collection = 'sprk_matters'
        IdAttr     = 'sprk_matterid'
        NameAttr   = 'sprk_name'
        DocLookup  = '_sprk_matter_value'
    }
    [pscustomobject]@{
        Logical    = 'sprk_project'
        Collection = 'sprk_projects'
        IdAttr     = 'sprk_projectid'
        NameAttr   = 'sprk_name'
        DocLookup  = '_sprk_project_value'
    }
    [pscustomobject]@{
        Logical    = 'sprk_invoice'
        Collection = 'sprk_invoices'
        IdAttr     = 'sprk_invoiceid'
        NameAttr   = 'sprk_name'
        DocLookup  = '_sprk_invoice_value'
    }
    [pscustomobject]@{
        Logical    = 'sprk_workassignment'
        Collection = 'sprk_workassignments'
        IdAttr     = 'sprk_workassignmentid'
        NameAttr   = 'sprk_name'
        DocLookup  = '_sprk_workassignment_value'
    }
    [pscustomobject]@{
        Logical    = 'sprk_event'
        Collection = 'sprk_events'
        IdAttr     = 'sprk_eventid'
        NameAttr   = 'sprk_name'
        DocLookup  = '_sprk_event_value'
    }
)

$DocumentEntity = [pscustomobject]@{
    Logical    = 'sprk_document'
    Collection = 'sprk_documents'
    IdAttr     = 'sprk_documentid'
    NameAttr   = 'sprk_documentname'
}

# -----------------------------------------------------------------------------
# Apply optional EntityFilter
# -----------------------------------------------------------------------------
$AuditParents   = $true
$AuditDocuments = $true

if (-not [string]::IsNullOrWhiteSpace($EntityFilter)) {
    $requested = $EntityFilter.Split(',') | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ }
    $ParentEntities = $ParentEntities | Where-Object { $requested -contains $_.Logical }
    $AuditParents   = $ParentEntities.Count -gt 0
    $AuditDocuments = $requested -contains 'sprk_document'
}

# -----------------------------------------------------------------------------
# Banner
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Multi-Container/Multi-Index Drift Audit (READ-ONLY) ===" -ForegroundColor Cyan
Write-Host "  Environment        : $EnvironmentUrl"
Write-Host "  Output (CSV)       : $OutputCsvPath"
Write-Host "  Output (Markdown)  : $OutputMarkdownPath"
Write-Host "  Entity filter      : $(if ($EntityFilter) { $EntityFilter } else { '(all six)' })"
Write-Host "  Audit parents      : $AuditParents"
Write-Host "  Audit documents    : $AuditDocuments"
Write-Host "  Batch size         : $BatchSize"
Write-Host "  Contract           : READ-ONLY (no Set/Update/New/Remove/PATCH/POST/PUT/DELETE)" -ForegroundColor Yellow
Write-Host ""

# -----------------------------------------------------------------------------
# Acquire Dataverse access token
# -----------------------------------------------------------------------------
Write-Host "Acquiring Dataverse access token..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to acquire token for $EnvironmentUrl. Try 'az login' or check 'az account show'."
}

$ReadHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = 'odata.include-annotations="*"'
}

# -----------------------------------------------------------------------------
# Helpers
# -----------------------------------------------------------------------------

function Invoke-DataverseRead {
    <#
        Wrapper around Invoke-RestMethod that ENFORCES the read-only contract by
        only ever issuing HTTP GET. Any caller passing a different method causes
        a hard failure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [string]$Method = 'GET'
    )
    if ($Method -ne 'GET') {
        throw "Audit script invariant violated: attempted HTTP $Method against '$Uri'. This script is READ-ONLY."
    }
    return Invoke-RestMethod -Uri $Uri -Headers $ReadHeaders -Method GET
}

function Get-DataversePaged {
    <#
        Returns all rows from a Dataverse OData query, following @odata.nextLink.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Uri
    )
    $all = New-Object System.Collections.Generic.List[object]
    $nextLink = $Uri
    while ($nextLink) {
        $page = Invoke-DataverseRead -Uri $nextLink
        if ($null -ne $page.value) {
            foreach ($row in $page.value) { [void]$all.Add($row) }
        }
        $nextLink = $page.'@odata.nextLink'
    }
    return $all
}

function Get-BusinessUnit {
    <#
        Returns a hashtable: { businessunitid -> [pscustomobject]@{ ContainerId, IndexName, ModifiedOn } }
        Loaded once at startup; small footprint (typically <10 BUs).
    #>
    Write-Host "Loading Business Units (containerid + searchindexname + modifiedon)..." -ForegroundColor DarkCyan
    $uri = "$EnvironmentUrl/api/data/v9.2/businessunits?`$select=businessunitid,name,sprk_containerid,sprk_searchindexname,modifiedon&`$top=500"
    $rows = Get-DataversePaged -Uri $uri
    $map = @{}
    foreach ($r in $rows) {
        $map[$r.businessunitid] = [pscustomobject]@{
            Name        = $r.name
            ContainerId = $r.sprk_containerid
            IndexName   = $r.sprk_searchindexname
            ModifiedOn  = if ($r.modifiedon) { [datetime]$r.modifiedon } else { $null }
        }
    }
    Write-Host ("  Loaded " + $map.Count + " business units") -ForegroundColor DarkGray
    return $map
}

function Get-ModeContainer {
    <#
        Given a list of child Documents, returns the most common sprk_graphdriveid
        (mode). Tie-broken alphabetically (deterministic). Returns $null if no
        children carry a non-empty drive id.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Children)
    $withDrive = $Children | Where-Object { $_.sprk_graphdriveid }
    if (-not $withDrive -or $withDrive.Count -eq 0) { return $null }
    $groups = $withDrive | Group-Object -Property sprk_graphdriveid
    $maxCount = ($groups | Measure-Object -Property Count -Maximum).Maximum
    $top = $groups | Where-Object { $_.Count -eq $maxCount } | Sort-Object Name
    return $top[0].Name
}

function Resolve-IndexFromContainer {
    <#
        Looks up an index name in $ContainerIndexMap. Returns $null if unknown
        (caller classifies as anomaly).
    #>
    [CmdletBinding()]
    param([string]$ContainerId)
    if ([string]::IsNullOrWhiteSpace($ContainerId)) { return $null }
    if ($ContainerIndexMap.ContainsKey($ContainerId)) { return $ContainerIndexMap[$ContainerId] }
    return $null
}

function Classify-DriftRow {
    <#
        Classification heuristic (design.md §5.4 + INV-3 + INV-5):

          - If derived value is unmapped (not in §5.1)            -> anomaly
          - If parent's stored container != mode of children      -> anomaly
          - If document's stored index   != map(stored drive id)  -> anomaly
          - If stored != BU value AND record.modifiedon  > BU.modifiedon -> override
          - If stored != BU value AND record.modifiedon <= BU.modifiedon -> drift
          - If stored == derived value                            -> match (omit)

        Conservative tie-break: when timestamps are missing or ambiguous, classify
        as `anomaly` (per task notes: false positives recoverable, false negatives
        hide problems).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Entity,
        [Parameter(Mandatory)]$Row,
        $RecordModifiedOn,
        $BuContainerId,
        $BuIndexName,
        $BuModifiedOn,
        [string]$StoredContainer,
        [string]$StoredIndex,
        [string]$DerivedContainer,
        [string]$DerivedIndex,
        [bool]$ParentDocMismatch = $false,
        [bool]$IndexMappingMismatch = $false
    )

    # 1. Unmapped container in stored value -> anomaly
    if ($StoredContainer -and -not (Resolve-IndexFromContainer -ContainerId $StoredContainer) `
        -and ($StoredContainer -notmatch '^\s*$')) {
        return [pscustomobject]@{
            Classification = 'anomaly'
            Recommendation = "Stored container '$StoredContainer' is not in §5.1 map. Operator must either extend the map (add (container -> index) pair in design.md §5.1 + backfill scripts) or correct the record."
        }
    }

    # 2. Parent has stored container that differs from mode of child Documents
    if ($ParentDocMismatch) {
        return [pscustomobject]@{
            Classification = 'anomaly'
            Recommendation = "Parent's stored sprk_containerid ('$StoredContainer') does NOT match the mode of its child Documents' sprk_graphdriveid ('$DerivedContainer'). Operator must investigate: either the parent is misrouted or its children were split across containers."
        }
    }

    # 3. Document's stored index does not match §5.1 mapping of its stored drive id
    if ($IndexMappingMismatch) {
        return [pscustomobject]@{
            Classification = 'anomaly'
            Recommendation = "Document's stored sprk_searchindexname ('$StoredIndex') does NOT match the §5.1 mapping ('$DerivedIndex') of its sprk_graphdriveid ('$StoredContainer'). Operator must investigate."
        }
    }

    # 4. Stored matches derived -> not a drift row; caller will skip
    if ($StoredContainer -eq $DerivedContainer -and $StoredIndex -eq $DerivedIndex) {
        return $null
    }

    # 5. Differs from BU -> distinguish override (record newer than BU) from drift (BU newer than record)
    $differsFromBu =
        ($BuContainerId -and $StoredContainer -ne $BuContainerId) -or
        ($BuIndexName   -and $StoredIndex     -ne $BuIndexName)

    if ($differsFromBu -and $RecordModifiedOn -and $BuModifiedOn) {
        if ($RecordModifiedOn -gt $BuModifiedOn) {
            return [pscustomobject]@{
                Classification = 'override'
                Recommendation = "No action — record was modified AFTER the BU's container/index fields. Treated as an explicit override (e.g., Protected Matter). Sacred per INV-5."
            }
        }
        elseif ($RecordModifiedOn -lt $BuModifiedOn) {
            return [pscustomobject]@{
                Classification = 'drift'
                Recommendation = "No action — BU's container/index changed AFTER this record was last modified. Coexistence is the design per INV-3 (BU changes do not propagate)."
            }
        }
        else {
            # Equal timestamps — ambiguous; conservative classification as anomaly
            return [pscustomobject]@{
                Classification = 'anomaly'
                Recommendation = "Record and BU container/index were last modified at the same instant; classification ambiguous. Operator must inspect manually."
            }
        }
    }

    # 6. Differs from derived but missing timestamps -> anomaly (conservative)
    return [pscustomobject]@{
        Classification = 'anomaly'
        Recommendation = "Stored value differs from chain-derived value but classification heuristic could not run (missing record.modifiedon or BU.modifiedon, or BU container/index null). Operator must inspect manually."
    }
}

# -----------------------------------------------------------------------------
# Main audit
# -----------------------------------------------------------------------------
$Findings = New-Object System.Collections.Generic.List[object]
$BuMap = Get-BusinessUnit

# ----- Parent entities -----
if ($AuditParents) {
    foreach ($ent in $ParentEntities) {
        Write-Host ("Auditing " + $ent.Logical + " ...") -ForegroundColor Yellow

        $select = @($ent.IdAttr, $ent.NameAttr, 'sprk_containerid', 'sprk_searchindexname', 'modifiedon', '_owningbusinessunit_value') -join ','
        $filter = 'sprk_containerid ne null or sprk_searchindexname ne null'
        $uri    = "$EnvironmentUrl/api/data/v9.2/$($ent.Collection)?`$select=$select&`$filter=$filter&`$top=$BatchSize"

        $records = Get-DataversePaged -Uri $uri
        Write-Host ("  Loaded " + $records.Count + " " + $ent.Logical + " records with container/index set") -ForegroundColor DarkGray

        foreach ($r in $records) {
            $recId      = $r.($ent.IdAttr)
            $recName    = $r.($ent.NameAttr)
            $stContId   = $r.sprk_containerid
            $stIdxName  = $r.sprk_searchindexname
            $recModOn   = if ($r.modifiedon) { [datetime]$r.modifiedon } else { $null }
            $buId       = $r.'_owningbusinessunit_value'

            $bu = if ($buId -and $BuMap.ContainsKey($buId)) { $BuMap[$buId] } else { $null }

            # Compute chain-derived value: mode of child Documents' sprk_graphdriveid,
            # falling back to owner BU's sprk_containerid.
            $childUri = "$EnvironmentUrl/api/data/v9.2/sprk_documents?`$select=sprk_documentid,sprk_graphdriveid&`$filter=$($ent.DocLookup) eq $recId&`$top=$BatchSize"
            $children = Get-DataversePaged -Uri $childUri
            $modeContainer = Get-ModeContainer -Children $children

            $derivedContainer = if ($modeContainer) { $modeContainer } else { if ($bu) { $bu.ContainerId } else { $null } }
            $derivedIndex     = Resolve-IndexFromContainer -ContainerId $derivedContainer

            # Detect parent/Document mismatch (anomaly source)
            $parentDocMismatch = $false
            if ($modeContainer -and $stContId -and $modeContainer -ne $stContId) {
                $parentDocMismatch = $true
            }

            $cls = Classify-DriftRow `
                -Entity              $ent.Logical `
                -Row                 $r `
                -RecordModifiedOn    $recModOn `
                -BuContainerId       $(if ($bu) { $bu.ContainerId } else { $null }) `
                -BuIndexName         $(if ($bu) { $bu.IndexName }   else { $null }) `
                -BuModifiedOn        $(if ($bu) { $bu.ModifiedOn }  else { $null }) `
                -StoredContainer     $stContId `
                -StoredIndex         $stIdxName `
                -DerivedContainer    $derivedContainer `
                -DerivedIndex        $derivedIndex `
                -ParentDocMismatch   $parentDocMismatch `
                -IndexMappingMismatch $false

            if ($null -ne $cls) {
                [void]$Findings.Add([pscustomobject]@{
                    entity            = $ent.Logical
                    recordId          = $recId
                    recordName        = $recName
                    currentContainer  = $stContId
                    currentIndex      = $stIdxName
                    derivedContainer  = $derivedContainer
                    derivedIndex      = $derivedIndex
                    classification    = $cls.Classification
                    recommendation    = $cls.Recommendation
                })
            }
        }
    }
}

# ----- Documents -----
if ($AuditDocuments) {
    Write-Host "Auditing sprk_document ..." -ForegroundColor Yellow

    $select = 'sprk_documentid,sprk_documentname,sprk_graphdriveid,sprk_searchindexname,modifiedon,_owningbusinessunit_value'
    $filter = 'sprk_graphdriveid ne null or sprk_searchindexname ne null'
    $uri    = "$EnvironmentUrl/api/data/v9.2/sprk_documents?`$select=$select&`$filter=$filter&`$top=$BatchSize"

    $records = Get-DataversePaged -Uri $uri
    Write-Host ("  Loaded " + $records.Count + " sprk_document records with drive/index set") -ForegroundColor DarkGray

    foreach ($d in $records) {
        $recId     = $d.sprk_documentid
        $recName   = $d.sprk_documentname
        $stDrive   = $d.sprk_graphdriveid
        $stIdxName = $d.sprk_searchindexname
        $recModOn  = if ($d.modifiedon) { [datetime]$d.modifiedon } else { $null }
        $buId      = $d.'_owningbusinessunit_value'

        $bu = if ($buId -and $BuMap.ContainsKey($buId)) { $BuMap[$buId] } else { $null }

        # Derived index = §5.1 map of the Document's own sprk_graphdriveid
        $derivedIndex = Resolve-IndexFromContainer -ContainerId $stDrive

        # Index mapping mismatch = stored index disagrees with the §5.1 mapping
        $indexMappingMismatch = $false
        if ($stDrive -and $derivedIndex -and $stIdxName -and ($stIdxName -ne $derivedIndex)) {
            $indexMappingMismatch = $true
        }

        $cls = Classify-DriftRow `
            -Entity              'sprk_document' `
            -Row                 $d `
            -RecordModifiedOn    $recModOn `
            -BuContainerId       $(if ($bu) { $bu.ContainerId } else { $null }) `
            -BuIndexName         $(if ($bu) { $bu.IndexName }   else { $null }) `
            -BuModifiedOn        $(if ($bu) { $bu.ModifiedOn }  else { $null }) `
            -StoredContainer     $stDrive `
            -StoredIndex         $stIdxName `
            -DerivedContainer    $stDrive `
            -DerivedIndex        $derivedIndex `
            -ParentDocMismatch   $false `
            -IndexMappingMismatch $indexMappingMismatch

        if ($null -ne $cls) {
            [void]$Findings.Add([pscustomobject]@{
                entity            = 'sprk_document'
                recordId          = $recId
                recordName        = $recName
                currentContainer  = $stDrive
                currentIndex      = $stIdxName
                derivedContainer  = $stDrive
                derivedIndex      = $derivedIndex
                classification    = $cls.Classification
                recommendation    = $cls.Recommendation
            })
        }
    }
}

# -----------------------------------------------------------------------------
# Write CSV output
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host ("Writing CSV report to " + $OutputCsvPath + " ...") -ForegroundColor Cyan
$Findings | Select-Object entity, recordId, recordName, currentContainer, currentIndex, derivedContainer, derivedIndex, classification, recommendation `
    | Export-Csv -Path $OutputCsvPath -NoTypeInformation -Encoding UTF8

# -----------------------------------------------------------------------------
# Write Markdown output (grouped by classification with summary)
# -----------------------------------------------------------------------------
Write-Host ("Writing Markdown report to " + $OutputMarkdownPath + " ...") -ForegroundColor Cyan

$total      = $Findings.Count
$nOverride  = ($Findings | Where-Object { $_.classification -eq 'override' }).Count
$nDrift     = ($Findings | Where-Object { $_.classification -eq 'drift' }).Count
$nAnomaly   = ($Findings | Where-Object { $_.classification -eq 'anomaly' }).Count

$entityTotals = $Findings | Group-Object entity | Sort-Object Count -Descending

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Multi-Container / Multi-Index Drift Audit Report")
[void]$md.AppendLine()
[void]$md.AppendLine("> **Generated**: " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "  ")
[void]$md.AppendLine("> **Environment**: ``$EnvironmentUrl``  ")
[void]$md.AppendLine("> **Mode**: READ-ONLY (no record mutations)  ")
[void]$md.AppendLine("> **Spec**: FR-BF-03  ")
[void]$md.AppendLine("> **Design**: design.md §5.4 (drift audit), §5.1 (container -> index map), INV-3, INV-5  ")
[void]$md.AppendLine()
[void]$md.AppendLine("## Summary")
[void]$md.AppendLine()
[void]$md.AppendLine("| Classification | Count | Meaning | Action |")
[void]$md.AppendLine("|---|---:|---|---|")
[void]$md.AppendLine("| **override** | $nOverride | Record modified AFTER BU change; intentional override | None — sacred per INV-5 |")
[void]$md.AppendLine("| **drift**    | $nDrift    | BU changed AFTER record; coexistence is the design     | None — by design per INV-3 |")
[void]$md.AppendLine("| **anomaly**  | $nAnomaly  | Unmapped container, parent/child mismatch, or ambiguous classification | Operator review required |")
[void]$md.AppendLine("| **TOTAL**    | $total     | (rows where stored differs from chain-derived value)   |  |")
[void]$md.AppendLine()
[void]$md.AppendLine("## Findings by entity")
[void]$md.AppendLine()
[void]$md.AppendLine("| Entity | Drift rows |")
[void]$md.AppendLine("|---|---:|")
foreach ($g in $entityTotals) {
    [void]$md.AppendLine("| ``$($g.Name)`` | $($g.Count) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Recommended next actions")
[void]$md.AppendLine()
if ($nAnomaly -gt 0) {
    [void]$md.AppendLine("- **Anomalies present (count: $nAnomaly)** — operator review required. Open the CSV, filter `classification = anomaly`, and inspect each `recommendation`. Common causes: a new SPE container was added without updating the §5.1 map in design.md + backfill scripts, or a parent record was migrated across containers without re-routing its children.")
}
if ($nOverride -gt 0) {
    [void]$md.AppendLine("- **Overrides present (count: $nOverride)** — informational. These are intentional explicit settings (e.g., Protected Matters). No action.")
}
if ($nDrift -gt 0) {
    [void]$md.AppendLine("- **Drift rows present (count: $nDrift)** — informational. BU's container/index changed AFTER these records were created. INV-3 guarantees BU changes do NOT propagate; coexistence is the design.")
}
if ($total -eq 0) {
    [void]$md.AppendLine("- **No drift detected.** All audited records match the chain-derived value.")
}
[void]$md.AppendLine()

foreach ($classification in @('anomaly', 'override', 'drift')) {
    $rows = $Findings | Where-Object { $_.classification -eq $classification }
    if (-not $rows -or $rows.Count -eq 0) { continue }

    [void]$md.AppendLine("## " + $classification.Substring(0,1).ToUpper() + $classification.Substring(1) + " ($($rows.Count))")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| entity | recordId | recordName | currentContainer | currentIndex | derivedContainer | derivedIndex | recommendation |")
    [void]$md.AppendLine("|---|---|---|---|---|---|---|---|")
    foreach ($row in $rows) {
        # Markdown-safe: pipe and backtick escape
        $safeName = if ($row.recordName) { $row.recordName -replace '\|', '\|' } else { '' }
        $safeRec  = if ($row.recommendation) { $row.recommendation -replace '\|', '\|' -replace '\r?\n', ' ' } else { '' }
        $line = "| ``$($row.entity)`` | ``$($row.recordId)`` | $safeName | ``$($row.currentContainer)`` | ``$($row.currentIndex)`` | ``$($row.derivedContainer)`` | ``$($row.derivedIndex)`` | $safeRec |"
        [void]$md.AppendLine($line)
    }
    [void]$md.AppendLine()
}

[void]$md.AppendLine("---")
[void]$md.AppendLine()
[void]$md.AppendLine("*Generated by `Audit-MultiContainerMultiIndex-Drift.ps1` — READ-ONLY. To mutate records, use the dedicated backfill scripts.*")

Set-Content -Path $OutputMarkdownPath -Value $md.ToString() -Encoding UTF8

# -----------------------------------------------------------------------------
# Final summary to console
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Drift Audit Complete ===" -ForegroundColor Green
Write-Host ("  Total drift rows : {0}" -f $total)
Write-Host ("  Override         : {0}" -f $nOverride) -ForegroundColor DarkGreen
Write-Host ("  Drift            : {0}" -f $nDrift)    -ForegroundColor DarkGreen
Write-Host ("  Anomaly          : {0}" -f $nAnomaly)  -ForegroundColor $(if ($nAnomaly -gt 0) { 'Red' } else { 'DarkGreen' })
Write-Host ""
Write-Host ("  CSV report       : {0}" -f $OutputCsvPath)
Write-Host ("  Markdown report  : {0}" -f $OutputMarkdownPath)
Write-Host ""

if ($nAnomaly -gt 0) {
    Write-Host "ANOMALIES FOUND — operator review required. See Markdown report for details." -ForegroundColor Yellow
}
