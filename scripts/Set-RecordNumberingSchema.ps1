#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Applies the INTERIM record numbering of spaarkeai-word-add-in-r1 task 076 to one Dataverse environment:
    Matters get MAT-######, Projects PRJ-######, sequential, from Dataverse's platform autonumber. Dry run by
    default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    sprk_matter.sprk_matternumber and sprk_project.sprk_projectnumber are their tables' PRIMARY NAME columns, so a
    record created without a number shows a BLANK name in every lookup, subgrid and regarding field. Owner decision
    2026-10-02 (task 076): until the planned numbering function exists, the number comes from the platform's own
    autonumber — "the dataverse auto numbering is just an interim solution until we build the numbering function".
    The platform fills the column on create ONLY when the caller leaves it empty (probed live 2026-10-02: a supplied
    value is kept, an omitted or empty one is generated), so the Create Matter wizard's own {typeCode}-{random6}
    numbers are unchanged, and every other path that sends no number — the Office pane, the Create Project wizard,
    model-driven forms, the AI create tool — gets one. Per table, in this order:

      (a) PLAN       Reads every row (every state). Blank numbers are listed in creation order. The highest number
                     already in the target format (e.g. MAT-000123) is found, so nothing below it is ever issued.
                     Duplicate values STOP all writes for that table (the key could not be created; half-applied
                     numbering is worse than none) — resolve them, then re-run.
      (b) FORMAT     AutoNumberFormat = MAT-{SEQNUM:6} / PRJ-{SEQNUM:6} on the existing column (compared case-
                     sensitively). A DIFFERENT format already there is never overwritten (the numbering function may
                     own it) — reported FAIL. A MANAGED column is never customised directly (ADR-027): its format
                     must arrive with the SpaarkeCore import.
      (c) SEED       Numbers come from the DATA: the next free number is one above the highest MAT-###### value any
                     row holds, after the blank rows (d) take theirs. The sequence is moved only when:
                       * it has issued nothing yet — the format was just set, or it still sits at the platform
                         default (seed 1000, as a solution import leaves it): it starts above the data, so a new
                         environment's first number is MAT-000001, not MAT-001000;
                       * blank rows need numbers (their range is reserved by moving the seed past it);
                       * a row already holds a number at or above the next one (typed in ahead of the sequence).
                     Once numbers have been issued it never moves BACK, so a deleted record's number is not re-issued.
                     The seed is per environment and is NOT carried by a solution import (Microsoft Learn, "Create
                     autonumber columns"), so every environment needs this script.
      (d) BACKFILL   Each blank row gets the next reserved number, oldest first (owner decision 2026-10-02: "fill
                     them on first run"), written against the row's ETag — a row changed since the plan is skipped,
                     never renumbered. A row that already has a number is never touched. Each write updates the row's
                     modifiedon (and anything that reacts to it).
      (e) UNIQUE     Alternate key on the number column (sprk_MatterNumber; sprk_ProjectNumber already existed in
                     dev). The sequence is unique only against itself — Dataverse does not check typed-in values — so
                     the key turns a collision into a refused write (the BFF retries it, RecordCreationService)
                     instead of a silent duplicate. NULLs are not enforced, so the key never blocks a blank row. Not
                     created on a MANAGED table (ADR-027) — it arrives with the import.
      (f) SOLUTION   Confirms -SolutionUniqueName (default SpaarkeCore) carries each table with all its subcomponents,
                     so the format and the keys travel with the solution; otherwise adds the column and key. Skipped
                     where the solution is managed. The SEED does not travel: run this script after every import into
                     a new environment.
      (g) PUBLISH    sprk_matter, sprk_project — only after a metadata write.

    -Verify fails (exit 1) on: a missing or different format, a sequence still at the platform default, a key that is
    missing or not Active, a component not in the solution, any duplicate value, any blank row, and a row holding a
    number at or above the next one.

    Known limits (interim):
      * GetNextAutoNumberValue reads one high until the first number is issued after a seed (measured live). A row
        holding EXACTLY the seed is therefore indistinguishable from the first issued number; a hand-typed value equal
        to the seed is not flagged (the key refuses the first create that draws it, and the BFF retries).
      * A record created between the plan and the format write (first run only) stays blank — -Verify reports it and
        a re-run numbers it. Run the first -Apply in a quiet window.
      * A matter type whose code is "MAT" would make the Create Matter wizard's {typeCode}-{random6} values look like
        sequence numbers. None exists today (LITG, CMRCL, PAT, TMRK, EMPL).

    ⚠️ Production: the owner checks existing matter numbers are unique before -Apply (answer (c), 2026-10-02); the
    plan step lists any duplicates and stops that table.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The solution that carries the tables. Default SpaarkeCore. Letters, digits and underscores only.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-RecordNumberingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: every step's state and every number it WOULD write; zero writes.

.EXAMPLE
    .\Set-RecordNumberingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-RecordNumberingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    spaarkeai-word-add-in-r1 task 076. Record: projects/spaarkeai-word-add-in-r1/notes/076-record-numbering.md.
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
    Retiring it (when the numbering function ships): clear AutoNumberFormat on both columns; keep the keys.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [ValidatePattern('^[A-Za-z0-9_]+$')][string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (RecordCreationService reads the same column names) ─────────────────────────────────────────────
$Digits = 6
$PlatformDefaultSeed = [long]1000   # where Dataverse starts a new sequence (Microsoft Learn; UEA-001000 probe)
$Tables = @(
    @{ Entity = 'sprk_matter'; Set = 'sprk_matters'; Id = 'sprk_matterid'; Column = 'sprk_matternumber'; Prefix = 'MAT'
       KeySchema = 'sprk_MatterNumber'; KeyLabel = 'Matter Number (unique)' },
    @{ Entity = 'sprk_project'; Set = 'sprk_projects'; Id = 'sprk_projectid'; Column = 'sprk_projectnumber'; Prefix = 'PRJ'
       KeySchema = 'sprk_ProjectNumber'; KeyLabel = 'Project Number (unique)' }
)

# ── Auth + helpers ──────────────────────────────────────────────────────────────────────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
# Every call through Invoke-DvWrite changes the environment; it runs only on -Apply paths.
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
# Read-only platform ACTIONS (POST by protocol, no side effect) — safe in dry run and -Verify.
function Invoke-DvAction([string]$Name, [hashtable]$Body) {
    Invoke-RestMethod -Uri "$Api/$Name" -Headers $headers -Method Post -Body ($Body | ConvertTo-Json -Compress)
}
function New-Label([string]$Text) {
    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 }) }
}
function Get-AllPages([string]$Path) {
    $rows = @(); $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers ($headers + @{ Prefer = 'odata.maxpagesize=5000' }) -Method Get
        $rows += @($page.value); $next = $page.'@odata.nextLink'
    }
    return $rows
}
function Format-Number([string]$Prefix, [long]$Value) { '{0}-{1}' -f $Prefix, $Value.ToString().PadLeft($Digits, '0') }
function Get-PlatformNext([hashtable]$T) {
    # GetNextAutoNumberValue is an ACTION (POST + body), not a function — a GET with parameters is refused
    # 0x80060888 (live, 2026-10-02). It returns the RAW next sequence number (e.g. "1001"), not the formatted value;
    # the formatted shape is accepted too in case a later platform version changes that.
    $v = [string](Invoke-DvAction 'GetNextAutoNumberValue' @{ EntityName = $T.Entity; AttributeName = $T.Column }).NextAutoNumberValue
    if ($v -match '^\d+$') { return [long]$v }
    if ($v -match "^$($T.Prefix)-(\d+)$") { return [long]$Matches[1] }
    throw "GetNextAutoNumberValue for $($T.Entity).$($T.Column) returned '$v', not a sequence number."
}
function Get-Seed([hashtable]$T) {
    [long](Invoke-DvAction 'GetAutoNumberSeed' @{ EntityName = $T.Entity; AttributeName = $T.Column }).AutoNumberSeedValue
}
# Where the sequence stands. Measured live 2026-10-02: after SetAutoNumberSeed(1002) the next matter got MAT-001002,
# while GetNextAutoNumberValue read 1003 until that create — it over-reads by ONE until the first number is issued
# after a (re)seed, and reads true after that. So a reading other than seed+1 is the truth. A reading of exactly
# seed+1 is the truth if a row holds a number at or above the seed (one was issued); otherwise nothing has been issued
# since the seed (or one was, and its row deleted — indistinguishable): the next number is the seed, the lower value,
# which is the conservative side for the collision check.
function Get-SequenceState([hashtable]$T, [long[]]$InFormat) {
    $seed = Get-Seed $T
    $platform = Get-PlatformNext $T
    $nothingIssued = ($platform -eq $seed + 1) -and (@($InFormat | Where-Object { $_ -ge $seed }).Count -eq 0)
    @{
        Seed     = $seed
        Next     = $(if ($nothingIssued) { $seed } else { $platform })
        Note     = $(if ($nothingIssued) { " (or $(Format-Number $T.Prefix $platform) if one number was issued and its row deleted)" } else { '' })
        # Still where a new sequence (or a solution import) leaves it: the platform default, nothing issued from it.
        Unseeded = $nothingIssued -and ($seed -eq $PlatformDefaultSeed)
    }
}

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$metadataWritten = $false
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"

$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename,ismanaged&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1

foreach ($T in $Tables) {
    $expected = "$($T.Prefix)-{SEQNUM:$Digits}"
    $attrPath = "EntityDefinitions(LogicalName='$($T.Entity)')/Attributes(LogicalName='$($T.Column)')"
    Write-Host "`n== $($T.Entity).$($T.Column) → $expected"

    # ── (a) PLAN ──────────────────────────────────────────────────────────────────────────────────────────
    $rows = @(Get-AllPages "$($T.Set)?`$select=$($T.Id),$($T.Column),createdon&`$orderby=createdon asc,$($T.Id) asc")
    $blank = @($rows | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.($T.Column)) })
    $values = @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.($T.Column)) } | ForEach-Object { ([string]$_.($T.Column)).Trim() })
    # Dataverse's key index compares case-insensitively, so duplicates are counted the same way.
    $dupes = @($values | Group-Object { $_.ToUpperInvariant() } | Where-Object Count -gt 1)
    [long[]]$inFormat = @($values | Where-Object { $_ -cmatch "^$($T.Prefix)-\d{$Digits,}$" } | ForEach-Object { [long]($_.Substring($T.Prefix.Length + 1)) })
    # [long], never the [double] Measure-Object returns: the seed below is sent as an Edm.Int64.
    $highest = [long]0
    if ($inFormat.Count -gt 0) { $highest = [long](($inFormat | Measure-Object -Maximum).Maximum) }
    Report 'INFO' ("{0} rows; {1} blank; {2} already {3}-###### (highest {4}); {5} duplicate value(s)" -f `
        $rows.Count, $blank.Count, $inFormat.Count, $T.Prefix, $(if ($highest) { Format-Number $T.Prefix $highest } else { 'none' }), $dupes.Count)
    foreach ($d in $dupes) { Report 'FAIL' "duplicate number '$($d.Group[0])' on $($d.Count) rows" }
    if ($dupes.Count -gt 0 -and -not $Verify) {
        Report 'FAIL' "no writes for $($T.Entity) until the duplicates are resolved (the key could not be created)"
        continue
    }

    $entityMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')?`$select=MetadataId,IsManaged"
    $attr = Invoke-DvGet "$attrPath/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=AutoNumberFormat,MetadataId,IsManaged"

    # ── (b) FORMAT ────────────────────────────────────────────────────────────────────────────────────────
    $current = [string]$attr.AutoNumberFormat
    $formatReady = $false
    if ($current -ceq $expected) { Report 'OK' "AutoNumberFormat is $expected"; $formatReady = $true }
    elseif ($current) { Report 'FAIL' "AutoNumberFormat is '$current', not $expected — another numbering format owns this column; not overwritten"; continue }
    elseif ($Verify) { Report 'MISSING' "AutoNumberFormat $expected (the column has none, so a record created without a number has a blank name)" }
    elseif ($attr.IsManaged) { Report 'FAIL' "$($T.Column) is MANAGED here: its format must arrive with the $SolutionUniqueName import (ADR-027 — no direct customisation of a managed component)"; continue }
    elseif ($IsDryRun) { Report 'WOULD' "set AutoNumberFormat = $expected" }
    else {
        $typed = Invoke-DvGet "$attrPath/Microsoft.Dynamics.CRM.StringAttributeMetadata"
        $typed.PSObject.Properties.Remove('@odata.context')
        $typed | Add-Member -NotePropertyName '@odata.type' -NotePropertyValue 'Microsoft.Dynamics.CRM.StringAttributeMetadata' -Force
        $typed.AutoNumberFormat = $expected
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $metadataWritten = $true
        Report 'DONE' "set AutoNumberFormat = $expected"
        $formatReady = $true
    }

    # ── (c) SEED ──────────────────────────────────────────────────────────────────────────────────────────
    $state = if ($current -ceq $expected) { Get-SequenceState $T $inFormat } else { $null }   # null: no sequence before this run
    $start = if ($null -eq $state -or $state.Unseeded) { $highest + 1 } else { [math]::Max($highest + 1, $state.Next) }
    [long]$seed = $start + $blank.Count
    $collision = ($null -ne $state) -and ($state.Next -le $highest)
    if ($collision) {
        Report $(if ($Verify) { 'FAIL' } else { 'INFO' }) "a row already holds $(Format-Number $T.Prefix $highest), at or above the next number $(Format-Number $T.Prefix $state.Next) — the sequence would reach it; the seed must move past it"
    }
    $needSeed = ($null -eq $state) -or ($blank.Count -gt 0) -or $collision -or ($state.Unseeded -and $seed -ne $state.Seed)
    if (-not $needSeed) { Report 'OK' "next number is $(Format-Number $T.Prefix $state.Next)$($state.Note)" }
    elseif ($Verify) {
        if ($null -eq $state) { Report 'MISSING' 'seed (no autonumber format yet)' }
        elseif ($state.Unseeded -and $seed -ne $state.Seed) { Report 'FAIL' "the sequence is still at the platform default (next $(Format-Number $T.Prefix $state.Next)); -Apply starts it at $(Format-Number $T.Prefix $seed)" }
    }
    elseif ($IsDryRun -or -not $formatReady) { Report 'WOULD' "set the seed so the next new record is $(Format-Number $T.Prefix $seed)" }
    else {
        # Metadata writes propagate asynchronously: just after the format is set, SetAutoNumberSeed can still refuse
        # 0x80060884 "is not an Auto Number attribute" (live, 2026-10-02). Retry that one error; anything else throws.
        for ($attempt = 1; ; $attempt++) {
            try { Invoke-DvWrite POST 'SetAutoNumberSeed' @{ EntityName = $T.Entity; AttributeName = $T.Column; Value = $seed } | Out-Null; break }
            catch {
                if ("$($_.ErrorDetails.Message) $($_.Exception.Message)" -notmatch '0x80060884' -or $attempt -ge 12) { throw }
                Write-Host "    the new format is not visible to the seed service yet; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        $after = Get-Seed $T
        if ($after -ne $seed) { Report 'FAIL' "SetAutoNumberSeed($seed) but GetAutoNumberSeed reads $after" }
        else { Report 'DONE' "seeded: the next new record is $(Format-Number $T.Prefix $seed)" }
    }

    # ── (d) BACKFILL — blank rows only, oldest first, from the reserved range ────────────────────────────────
    $n = $start
    foreach ($row in $blank) {
        $value = Format-Number $T.Prefix $n; $n++
        $rowId = $row.($T.Id)
        if ($Verify) { Report 'FAIL' "row $rowId (created $($row.createdon)) has no number — a blank name"; continue }
        if ($IsDryRun -or -not $formatReady) { Report 'WOULD' "number row $rowId (created $($row.createdon)) as $value"; continue }
        try {
            # The row's ETag from the plan: a row changed since then (numbered by someone else) is refused, not overwritten.
            Invoke-DvWrite PATCH "$($T.Set)($rowId)" @{ $T.Column = $value } @{ 'If-Match' = $row.'@odata.etag' } | Out-Null
            Report 'DONE' "numbered row $rowId as $value"
        } catch {
            if ([int]$_.Exception.Response.StatusCode -ne 412) { throw }
            Report 'INFO' "row $rowId changed since the plan — skipped ($value stays unused); re-run if it is still blank"
        }
    }

    # ── (e) UNIQUE ────────────────────────────────────────────────────────────────────────────────────────
    $keys = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus,MetadataId").value)
    $key = $keys | Where-Object { @($_.KeyAttributes) -join ',' -eq $T.Column } | Select-Object -First 1
    if ($key) {
        if ($key.EntityKeyIndexStatus -eq 'Active') { Report 'OK' "alternate key $($key.SchemaName) is Active" }
        else { Report 'FAIL' "alternate key $($key.SchemaName) index status is $($key.EntityKeyIndexStatus) (not Active)" }
    } elseif ($Verify) { Report 'MISSING' "alternate key on $($T.Entity)($($T.Column))" }
    elseif ($entityMeta.IsManaged) { Report 'FAIL' "$($T.Entity) is MANAGED here: the key must arrive with the $SolutionUniqueName import (ADR-027)" }
    elseif ($IsDryRun) { Report 'WOULD' "create alternate key $($T.KeySchema) on $($T.Entity)($($T.Column))" }
    else {
        Invoke-DvWrite POST "EntityDefinitions(LogicalName='$($T.Entity)')/Keys" @{
            SchemaName    = $T.KeySchema
            DisplayName   = (New-Label $T.KeyLabel)
            KeyAttributes = @($T.Column)
        } | Out-Null
        $metadataWritten = $true
        $status = $null
        for ($i = 1; $i -le 18; $i++) {
            Start-Sleep -Seconds 10
            $k = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus").value) | Where-Object { @($_.KeyAttributes) -join ',' -eq $T.Column } | Select-Object -First 1
            $status = $k.EntityKeyIndexStatus
            if ($status -in 'Active', 'Failed') { break }
            Write-Host "    waiting for the $($T.KeySchema) index ($status, $i/18)..."
        }
        if ($status -eq 'Active') { Report 'DONE' "created $($T.KeySchema) (Active)" }
        else { Report 'FAIL' "created $($T.KeySchema) but its index is $status — re-run -Verify; Failed means duplicate values" }
    }

    # ── (f) SOLUTION ──────────────────────────────────────────────────────────────────────────────────────
    if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found"; continue }
    if ($solution.ismanaged) { Report 'INFO' "$SolutionUniqueName is managed here — the format and key travel with its import"; continue }
    # A table added with ALL its subcomponents (rootcomponentbehavior 0) already carries every column and key — the
    # case for both tables in SpaarkeCore (dev, 2026-10-02); the shared helper answers 'ViaTable' for them.
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    $components = @(@{ Id = $attr.MetadataId; Type = 2; Label = "$($T.Entity).$($T.Column)"; TableId = $entityMeta.MetadataId })
    $k = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')/Keys?`$select=MetadataId,KeyAttributes").value) | Where-Object { @($_.KeyAttributes) -join ',' -eq $T.Column } | Select-Object -First 1
    if ($k) { $components += @{ Id = $k.MetadataId; Type = 14; Label = "key on $($T.Entity).$($T.Column)"; TableId = $entityMeta.MetadataId } }
    foreach ($c in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $SolutionUniqueName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $SolutionUniqueName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        $metadataWritten = $true
        Report 'DONE' "added $($c.Label) to $SolutionUniqueName"
    }
}

# ── (g) PUBLISH — only after a metadata write ─────────────────────────────────────────────────────────────────
if ($Apply -and $metadataWritten) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = '<importexportxml><entities><entity>sprk_matter</entity><entity>sprk_project</entity></entities></importexportxml>' } | Out-Null
    Write-Host "`nPublished sprk_matter and sprk_project." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: record numbering is complete." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($gaps.Count -gt 0) {
    Write-Host "`n$($gaps.Count) problem(s) above need attention." -ForegroundColor Red
    exit 1
}
