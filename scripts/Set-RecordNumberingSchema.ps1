#!/usr/bin/env pwsh
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
      (b) FORMAT     AutoNumberFormat = MAT-{SEQNUM:6} / PRJ-{SEQNUM:6} on the existing column. A DIFFERENT format
                     already there is never overwritten (the numbering function may own it) — reported FAIL.
      (c) SEED       Numbers come from the DATA: the next free number is one above the highest MAT-###### value any
                     row holds, after the blank rows (d) take theirs. Re-seeded only for blank rows, for a next
                     number a row already holds, or on first application (start at 1). The seed is per environment
                     and is NOT carried by a solution import (Microsoft Learn, "Create autonumber columns"), so every
                     environment needs this script — without it the platform starts at 1000 (MAT-001000).
                     ⚠️ GetNextAutoNumberValue over-reads by one until the first number is issued after a seed
                     (measured live 2026-10-02); the script reads GetAutoNumberSeed in that state.
      (d) BACKFILL   Each blank row gets the next reserved number, oldest first (owner decision 2026-10-02: "fill
                     them on first run"). A row that already has a number is never renumbered.
      (e) UNIQUE     Alternate key on the number column (sprk_MatterNumber is created; sprk_ProjectNumber already
                     exists in dev). Refused while two rows share a value. The sequence is unique only against
                     itself — Dataverse does not check typed-in values — so the key turns a collision into a
                     refused write (the BFF retries it, RecordCreationService) instead of a silent duplicate. NULLs
                     are not enforced, so the key never blocks a blank row.
      (f) SOLUTION   Adds the number columns and keys to -SolutionUniqueName (default SpaarkeCore), so the format and
                     the keys travel with the solution. The SEED does not travel: run this script after every import
                     into a new environment.
      (g) PUBLISH    sprk_matter, sprk_project.

    -Verify fails (exit 1) on: a missing or different format, a key that is missing or not Active, a component not
    in the solution, any duplicate value, any blank row, and a platform next value that is already in use.

    ⚠️ Production: the owner checks existing matter numbers are unique before -Apply (answer (c), 2026-10-02); the
    key step refuses and lists the duplicates if they are not.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

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
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (RecordCreationService reads the same column names) ─────────────────────────────────────────────
$Digits = 6
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
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
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
    $r = Invoke-DvWrite POST 'GetNextAutoNumberValue' @{ EntityName = $T.Entity; AttributeName = $T.Column }
    $v = [string]$r.NextAutoNumberValue
    if ($v -match '^\d+$') { return [long]$v }
    if ($v -match "^$($T.Prefix)-(\d+)$") { return [long]$Matches[1] }
    throw "GetNextAutoNumberValue for $($T.Entity).$($T.Column) returned '$v', not a sequence number."
}
function Get-Seed([hashtable]$T) {
    [long](Invoke-DvWrite POST 'GetAutoNumberSeed' @{ EntityName = $T.Entity; AttributeName = $T.Column }).AutoNumberSeedValue
}
# The number the NEXT create will actually receive. Measured live 2026-10-02: after SetAutoNumberSeed(1002) the next
# matter got MAT-001002, while GetNextAutoNumberValue read 1003 until that create — it over-reads by ONE until the
# first number is issued after a (re)seed, and reads true after that. So a reading other than seed+1 is the truth. A
# reading of exactly seed+1 is the truth if a row holds a number at or above the seed (one was issued); otherwise it
# is ambiguous — nothing issued (truth = seed) or one issued and its row deleted (truth = seed+1). The lower value is
# used for the collision check (the conservative side) and the report says it is ambiguous.
function Get-TrueNext([hashtable]$T, [long[]]$InFormat) {
    $seedValue = Get-Seed $T
    $platform = Get-PlatformNext $T
    if ($platform -ne $seedValue + 1) { return @{ Value = $platform; Note = '' } }
    if (@($InFormat | Where-Object { $_ -ge $seedValue }).Count -gt 0) { return @{ Value = $platform; Note = '' } }
    return @{ Value = $seedValue; Note = " (or $(Format-Number $T.Prefix $platform): GetNextAutoNumberValue reads one high until a number is issued after a seed, so a number issued and then deleted cannot be told apart)" }
}

$IsDryRun = -not $Apply.IsPresent
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"

$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
$inSolution = if ($solution) {
    @((Get-AllPages "solutioncomponents?`$select=objectid&`$filter=_solutionid_value eq $($solution.solutionid)") | ForEach-Object { $_.objectid.ToString().ToLowerInvariant() })
} else { @() }

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
    $inFormat = @($values | Where-Object { $_ -match "^$($T.Prefix)-(\d{$Digits,})$" } | ForEach-Object { [long]($_ -replace "^$($T.Prefix)-", '') })
    $highest = if ($inFormat.Count -gt 0) { ($inFormat | Measure-Object -Maximum).Maximum } else { 0 }
    Report 'INFO' ("{0} rows; {1} blank; {2} already {3}-###### (highest {4}); {5} duplicate value(s)" -f `
        $rows.Count, $blank.Count, $inFormat.Count, $T.Prefix, $(if ($highest) { Format-Number $T.Prefix $highest } else { 'none' }), $dupes.Count)
    foreach ($d in $dupes) { Report 'FAIL' "duplicate number '$($d.Group[0])' on $($d.Count) rows — resolve before the key can be created" }

    # ── (b) FORMAT ────────────────────────────────────────────────────────────────────────────────────────
    $attr = Invoke-DvGet "$attrPath/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=AutoNumberFormat,MetadataId"
    $current = [string]$attr.AutoNumberFormat
    $formatReady = $false
    if ($current -eq $expected) { Report 'OK' "AutoNumberFormat is $expected"; $formatReady = $true }
    elseif ($current) { Report 'FAIL' "AutoNumberFormat is '$current', not $expected — another numbering format owns this column; not overwritten" }
    elseif ($Verify) { Report 'MISSING' "AutoNumberFormat $expected (the column has none, so a record created without a number has a blank name)" }
    elseif ($IsDryRun) { Report 'WOULD' "set AutoNumberFormat = $expected" }
    else {
        $typed = Invoke-DvGet "$attrPath/Microsoft.Dynamics.CRM.StringAttributeMetadata"
        $typed.PSObject.Properties.Remove('@odata.context')
        $typed | Add-Member -NotePropertyName '@odata.type' -NotePropertyValue 'Microsoft.Dynamics.CRM.StringAttributeMetadata' -Force
        $typed.AutoNumberFormat = $expected
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "set AutoNumberFormat = $expected"
        $formatReady = $true
    }
    if ($current -and $current -ne $expected) { continue }   # never seed or number under someone else's format

    # ── (c) SEED ──────────────────────────────────────────────────────────────────────────────────────────
    # Numbers come from the DATA: the first free number is one above the highest in-format value any row holds, and
    # the blank rows (d) take the next $blank.Count of them. The sequence is re-seeded only when needed:
    #   * blank rows to fill (their numbers are reserved by moving the seed past them);
    #   * the next number is already held by a row (typed in ahead of the sequence);
    #   * the run that APPLIES the format: start above the data (1 when nothing is in the format yet) rather than at
    #     the platform's default 1000 (the seed is per environment and not carried by a solution import).
    # A number issued and later deleted may be issued again; no row holds it, so that is harmless.
    $start = $highest + 1
    $seed = $start + $blank.Count
    $tn = if ($current -eq $expected) { Get-TrueNext $T $inFormat } else { $null }
    $trueNext = if ($tn) { $tn.Value } else { $null }
    $collision = ($null -ne $trueNext) -and ($trueNext -le $highest)
    if ($collision) {
        Report $(if ($Verify) { 'FAIL' } else { 'INFO' }) "the next number $(Format-Number $T.Prefix $trueNext) is already held by a row (highest $(Format-Number $T.Prefix $highest)) — the seed must move up"
    }
    $needSeed = ($null -eq $trueNext) -or ($blank.Count -gt 0) -or $collision
    if (-not $needSeed) { Report 'OK' "next number is $(Format-Number $T.Prefix $trueNext)$($tn.Note)" }
    elseif ($Verify) { if ($null -eq $trueNext) { Report 'MISSING' 'seed (no autonumber format yet)' } }
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
        Invoke-DvWrite PATCH "$($T.Set)($rowId)" @{ $T.Column = $value } @{ 'If-Match' = '*' } | Out-Null
        Report 'DONE' "numbered row $rowId as $value"
    }

    # ── (e) UNIQUE ────────────────────────────────────────────────────────────────────────────────────────
    $keys = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus,MetadataId").value)
    $key = $keys | Where-Object { @($_.KeyAttributes) -join ',' -eq $T.Column } | Select-Object -First 1
    if ($key) {
        if ($key.EntityKeyIndexStatus -eq 'Active') { Report 'OK' "alternate key $($key.SchemaName) is Active" }
        else { Report 'FAIL' "alternate key $($key.SchemaName) index status is $($key.EntityKeyIndexStatus) (not Active)" }
    } elseif ($Verify) { Report 'MISSING' "alternate key on $($T.Entity)($($T.Column))" }
    elseif ($dupes.Count -gt 0) { Report 'FAIL' "alternate key $($T.KeySchema) NOT created: $($dupes.Count) duplicate value(s) above" }
    elseif ($IsDryRun) { Report 'WOULD' "create alternate key $($T.KeySchema) on $($T.Entity)($($T.Column))" }
    else {
        Invoke-DvWrite POST "EntityDefinitions(LogicalName='$($T.Entity)')/Keys" @{
            SchemaName    = $T.KeySchema
            DisplayName   = (New-Label $T.KeyLabel)
            KeyAttributes = @($T.Column)
        } | Out-Null
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
    # A table added with ALL its subcomponents (rootcomponentbehavior 0) already carries every column and key — the
    # case for both tables in SpaarkeCore (dev, 2026-10-02). Only otherwise are the column and key added one by one.
    $entityMd = (Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')?`$select=MetadataId").MetadataId
    $root = @((Invoke-DvGet "solutioncomponents?`$select=rootcomponentbehavior&`$filter=_solutionid_value eq $($solution.solutionid) and objectid eq $entityMd and componenttype eq 1").value) | Select-Object -First 1
    if ($root -and $root.rootcomponentbehavior -eq 0) {
        Report 'OK' "$($T.Entity) is in $SolutionUniqueName with all subcomponents (carries the column and its key)"
        continue
    }
    $components = @(@{ Id = $attr.MetadataId; Type = 2; Label = "$($T.Entity).$($T.Column)" })
    $k = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($T.Entity)')/Keys?`$select=MetadataId,KeyAttributes").value) | Where-Object { @($_.KeyAttributes) -join ',' -eq $T.Column } | Select-Object -First 1
    if ($k) { $components += @{ Id = $k.MetadataId; Type = 14; Label = "key on $($T.Entity).$($T.Column)" } }
    foreach ($c in $components) {
        if ($inSolution -contains $c.Id.ToString().ToLowerInvariant()) { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $SolutionUniqueName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $($c.Label) to $SolutionUniqueName"
    }
}

# ── (g) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
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
