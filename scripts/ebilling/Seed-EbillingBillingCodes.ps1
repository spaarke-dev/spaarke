<#
.SYNOPSIS
    eBilling R1 task 018 - seeds the UTBMS code sets (sprk_billingcodeset) and their codes (sprk_billingcode) from the
    CSV files in scripts/seed-data/ebilling/utbms-*.csv. Idempotent, create-only for existing rows.

.DESCRIPTION
    One CSV per code set (litigation, counseling, project, bankruptcy, IP patent prosecution, trademark, activity,
    expense). Columns: set_name, body, version, source_url, download_date, code, code_type (Task / Activity / Expense),
    phase, description, guidance_text. The set-level columns repeat on every row of a file and must agree.

    Idempotency contract (spec FR-08):
      * A code set is matched on its name; a missing set is created with body, version and source URL.
      * A code is matched on its alternate key (sprk_codeset, sprk_code); a missing code is created.
      * An EXISTING set or code is NEVER updated - not sprk_guidancetext (an admin edit wins) and not any other column.
        This is the documented choice for "other columns": none are updated, because a column that differs from the
        CSV is as likely an admin edit as a stale seed, and the script cannot tell them apart. Corrections to an
        existing row are made in Dataverse or by a one-off migration.
      * A re-run with unchanged CSVs and an unchanged environment performs ZERO writes.
      * Existing rows whose description / phase / code type / guidance differ from the CSV are only REPORTED (drift),
        so an operator can see what a re-seed deliberately left alone.

    Validation runs over every CSV BEFORE any write. Duplicate codes within a set, a missing required column, an
    unknown code type, or two files naming the same set STOP the script with exit code 2 and the offending rows.

    Not wired into scripts/seed-data/manifest.yaml (that manifest is the AI-seed chain). ADR-002: plain row writes
    through the Web API; no plugin or flow.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER CsvDirectory
    Folder holding the utbms-*.csv files. Defaults to scripts/seed-data/ebilling next to this script's repo root.

.EXAMPLE
    pwsh scripts/ebilling/Seed-EbillingBillingCodes.ps1 -WhatIf
    pwsh scripts/ebilling/Seed-EbillingBillingCodes.ps1

.NOTES
    Project: ebilling-r1, task 018. Run Deploy-EbillingBillingCodeSchema.ps1 first. Precedent: scripts/Seed-AiPersonaDefault.ps1.
    Exit codes: 0 = done, 1 = error, 2 = validation / escalation (duplicates, bad data, ambiguous match).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$CsvDirectory = (Join-Path $PSScriptRoot '..\seed-data\ebilling')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$ApiBase = "$EnvironmentUrl/api/data/v9.2"
$CodeSetTable = 'sprk_billingcodeset'
$CodeTable = 'sprk_billingcode'
$RequiredColumns = 'set_name', 'body', 'version', 'source_url', 'download_date', 'code', 'code_type', 'phase', 'description', 'guidance_text'
$script:Token = $null
$script:TokenAt = [datetime]::MinValue

class SeedValidationException : System.Exception {
    SeedValidationException([string]$message) : base($message) {}
}

# ---------------------------------------------------------------------------------------------------------------
# Web API plumbing (same behaviour as the task 010 / 012 / 018 schema scripts)
# ---------------------------------------------------------------------------------------------------------------

function Get-DataverseToken {
    if ($script:Token -and ((Get-Date) - $script:TokenAt).TotalMinutes -lt 40) { return $script:Token }
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($t)) {
        throw "Could not get a Dataverse token from Azure CLI ($t). Run 'az login'."
    }
    $script:Token = $t.Trim()
    $script:TokenAt = Get-Date
    return $script:Token
}

function Get-ErrorText($err) {
    $text = $err.Exception.Message
    if ($err.ErrorDetails -and $err.ErrorDetails.Message) {
        try {
            $j = $err.ErrorDetails.Message | ConvertFrom-Json -ErrorAction Stop
            if ($j.error.message) { $text = "$($j.error.code): $($j.error.message)" }
        } catch { $text = $err.ErrorDetails.Message }
    }
    return $text
}

function Get-StatusCode($err) {
    try { return [int]$err.Exception.Response.StatusCode } catch { return 0 }
}

function Test-Transient([int]$status, [string]$text) {
    if ($status -in 429, 500, 502, 503, 504) { return $true }
    return ($text -match '(?i)publish.*(in progress|running)|(in progress).*publish|customization.*(lock|in progress)|(lock|locked).*customiz|another .*import|0x80071151|0x80048d19|SqlException.*(timeout|deadlock)|timeout|timed out|unexpected error occurred')
}

function Invoke-Dv {
    param([string]$Path, [string]$Method = 'GET', $Body = $null, [hashtable]$ExtraHeaders = @{})
    $uri = if ($Path -like 'http*') { $Path } else { "$ApiBase/$Path" }
    $attempt = 0
    $max = if ($Method -eq 'GET') { 3 } else { 6 }
    while ($true) {
        $attempt++
        $headers = @{
            Authorization      = "Bearer $(Get-DataverseToken)"
            'OData-MaxVersion' = '4.0'
            'OData-Version'    = '4.0'
            Accept             = 'application/json'
        }
        foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
        $p = @{ Uri = $uri; Method = $Method; Headers = $headers }
        if ($null -ne $Body) {
            $p.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
            $p.ContentType = 'application/json; charset=utf-8'
        }
        try {
            return Invoke-RestMethod @p
        } catch {
            $status = Get-StatusCode $_
            $text = Get-ErrorText $_
            if ($status -eq 401 -and $attempt -eq 1) { $script:Token = $null; continue }
            if ($attempt -lt $max -and (Test-Transient $status $text)) {
                $delay = [Math]::Min(60, 5 * [Math]::Pow(2, $attempt - 1))
                Write-Host "    transient failure (HTTP $status), retry $attempt/$($max - 1) in ${delay}s: $text" -ForegroundColor DarkYellow
                Start-Sleep -Seconds $delay
                continue
            }
            $e = [System.Exception]::new("API error ($Method $Path) HTTP ${status}: $text")
            $e.Data['Status'] = $status
            throw $e
        }
    }
}

# Reads every page of a query (follows @odata.nextLink).
function Get-DvAll([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = $Path
    while ($next) {
        $r = Invoke-Dv $next
        foreach ($v in @($r.value)) { $rows.Add($v) }
        $next = $r.'@odata.nextLink'
    }
    return $rows
}

# ---------------------------------------------------------------------------------------------------------------
# CSV load + validation (everything is checked before any write)
# ---------------------------------------------------------------------------------------------------------------

function Read-SeedFiles {
    $files = @(Get-ChildItem -Path $CsvDirectory -Filter 'utbms-*.csv' -File | Sort-Object Name)
    if ($files.Count -eq 0) { throw [SeedValidationException]::new("No utbms-*.csv files found in $CsvDirectory.") }
    $problems = [System.Collections.Generic.List[string]]::new()
    $sets = [System.Collections.Generic.List[object]]::new()
    $seenNames = @{}
    foreach ($f in $files) {
        $rows = @(Import-Csv -Path $f.FullName -Encoding utf8)
        if ($rows.Count -eq 0) { $problems.Add("$($f.Name): no data rows"); continue }
        $cols = @($rows[0].PSObject.Properties.Name)
        $missing = @($RequiredColumns | Where-Object { $_ -notin $cols })
        if ($missing.Count -gt 0) { $problems.Add("$($f.Name): missing column(s) $($missing -join ', ')"); continue }
        $first = $rows[0]
        foreach ($c in 'set_name', 'body', 'version', 'source_url') {
            $vals = @($rows | ForEach-Object { $_.$c.Trim() } | Sort-Object -Unique)
            if ($vals.Count -ne 1 -or [string]::IsNullOrWhiteSpace($vals[0])) { $problems.Add("$($f.Name): column $c must hold one non-empty value on every row (found $($vals.Count) distinct)") }
        }
        $setName = $first.set_name.Trim()
        if ($seenNames.ContainsKey($setName)) { $problems.Add("$($f.Name): set '$setName' is also named in $($seenNames[$setName])") }
        $seenNames[$setName] = $f.Name
        $codes = [System.Collections.Generic.List[object]]::new()
        $line = 1
        foreach ($r in $rows) {
            $line++
            $code = $r.code.Trim()
            if ([string]::IsNullOrWhiteSpace($code)) { $problems.Add("$($f.Name) line ${line}: empty code"); continue }
            if ($r.code_type.Trim() -notin 'Task', 'Activity', 'Expense') { $problems.Add("$($f.Name) line ${line}: code_type '$($r.code_type)' is not Task / Activity / Expense") }
            if ($code.Length -gt 100) { $problems.Add("$($f.Name) line ${line}: code '$code' is longer than 100 characters") }
            if ($r.description.Length -gt 500) { $problems.Add("$($f.Name) line ${line}: description is longer than 500 characters") }
            if ($r.phase.Length -gt 20) { $problems.Add("$($f.Name) line ${line}: phase is longer than 20 characters") }
            if ($r.guidance_text.Length -gt 10000) { $problems.Add("$($f.Name) line ${line}: guidance_text is longer than 10000 characters") }
            $codes.Add([pscustomobject]@{
                    Code = $code; Type = $r.code_type.Trim(); Phase = $r.phase.Trim()
                    Description = $r.description.Trim(); Guidance = $r.guidance_text.Trim(); Line = $line
                })
        }
        $dups = @($codes | Group-Object Code | Where-Object { $_.Count -gt 1 })
        foreach ($d in $dups) {
            $problems.Add("$($f.Name): code '$($d.Name)' is not unique within set '$setName' (lines $((@($d.Group | ForEach-Object { $_.Line })) -join ', ')): " + (($d.Group | ForEach-Object { "'$($_.Description)'" }) -join ' / '))
        }
        $sets.Add([pscustomobject]@{
                File = $f.Name; Name = $setName; Body = $first.body.Trim(); Version = $first.version.Trim()
                SourceUrl = $first.source_url.Trim(); DownloadDate = $first.download_date.Trim(); Codes = $codes
            })
    }
    if ($problems.Count -gt 0) {
        throw [SeedValidationException]::new("The seed files failed validation; nothing was written:`n  - " + ($problems -join "`n  - "))
    }
    return $sets
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 018 - seed UTBMS billing codes" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   CSV: $((Resolve-Path $CsvDirectory).Path)   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

$summary = [System.Collections.Generic.List[object]]::new()
try {
    $sets = Read-SeedFiles
    Write-Host "Validated $($sets.Count) files, $((($sets | ForEach-Object { $_.Codes.Count } | Measure-Object -Sum).Sum)) codes, no duplicates." -ForegroundColor Green

    # Table and column metadata the writes depend on (never hard-code a navigation property or option value).
    $setMeta = Invoke-Dv "EntityDefinitions(LogicalName='$CodeSetTable')?`$select=EntitySetName"
    $codeMeta = Invoke-Dv "EntityDefinitions(LogicalName='$CodeTable')?`$select=EntitySetName"
    $nav = (Invoke-Dv "EntityDefinitions(LogicalName='$CodeTable')/ManyToOneRelationships?`$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName&`$filter=ReferencingAttribute eq 'sprk_codeset'").value |
        Select-Object -First 1
    if (-not $nav) { throw "sprk_billingcode.sprk_codeset relationship not found; run Deploy-EbillingBillingCodeSchema.ps1 first." }
    $pk = Invoke-Dv "EntityDefinitions(LogicalName='$CodeTable')/Attributes(LogicalName='sprk_codetype')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
    $typeValue = @{}
    foreach ($o in $pk.OptionSet.Options) { $typeValue[$o.Label.UserLocalizedLabel.Label] = [int]$o.Value }
    foreach ($t in 'Task', 'Activity', 'Expense') { if (-not $typeValue.ContainsKey($t)) { throw "sprk_codetype has no option '$t'." } }

    foreach ($set in $sets) {
        Write-Host "$($set.Name)  ($($set.File), $($set.Codes.Count) codes, $($set.Body) $($set.Version))" -ForegroundColor Cyan
        $row = [ordered]@{ Set = $set.Name; Csv = $set.Codes.Count; SetCreated = 0; Created = 0; Updated = 0; Unchanged = 0; Drift = 0; GuidanceEdited = 0; SetId = '' }

        # --- the set row ---------------------------------------------------------------------------------------
        $nameFilter = [uri]::EscapeDataString("sprk_name eq '" + $set.Name.Replace("'", "''") + "'")
        $found = @((Invoke-Dv "$($setMeta.EntitySetName)?`$select=sprk_billingcodesetid,sprk_name,sprk_version,sprk_body,sprk_sourceurl&`$filter=$nameFilter").value)
        if ($found.Count -gt 1) { throw [SeedValidationException]::new("$($found.Count) code set rows are named '$($set.Name)'; the match is ambiguous. Resolve the duplicates in Dataverse first.") }
        $setId = $null
        if ($found.Count -eq 1) {
            $setId = $found[0].sprk_billingcodesetid
            if ($found[0].sprk_version -ne $set.Version -or $found[0].sprk_body -ne $set.Body -or $found[0].sprk_sourceurl -ne $set.SourceUrl) {
                $row.Drift++; Write-Host "  drift (left alone): set row has version '$($found[0].sprk_version)' / body '$($found[0].sprk_body)' / source '$($found[0].sprk_sourceurl)'" -ForegroundColor DarkYellow
            }
            Write-Host "  set exists ($setId)" -ForegroundColor Yellow
        } elseif ($DryRun) {
            $row.SetCreated++; Write-Host "  WOULD CREATE set row" -ForegroundColor Cyan
        } else {
            $created = Invoke-Dv -Path $setMeta.EntitySetName -Method POST -ExtraHeaders @{ Prefer = 'return=representation' } -Body @{
                sprk_name = $set.Name; sprk_body = $set.Body; sprk_version = $set.Version; sprk_sourceurl = $set.SourceUrl
            }
            $setId = $created.sprk_billingcodesetid
            $row.SetCreated++; Write-Host "  CREATED set row ($setId)" -ForegroundColor Green
        }
        $row.SetId = $setId

        # --- the codes ----------------------------------------------------------------------------------------
        $existing = @{}
        if ($setId) {
            $q = "$($codeMeta.EntitySetName)?`$select=sprk_code,sprk_description,sprk_phase,sprk_guidancetext,sprk_codetype&`$filter=_sprk_codeset_value eq $setId"
            foreach ($e in (Get-DvAll $q)) { $existing[$e.sprk_code] = $e }
        }
        foreach ($c in $set.Codes) {
            $cur = $existing[$c.Code]
            if ($cur) {
                $row.Unchanged++
                $typeLabel = ($typeValue.GetEnumerator() | Where-Object { $_.Value -eq $cur.sprk_codetype } | Select-Object -First 1).Key
                if ($cur.sprk_description -ne $c.Description -or [string]$cur.sprk_phase -ne $c.Phase -or $typeLabel -ne $c.Type) { $row.Drift++ }
                if ([string]$cur.sprk_guidancetext -ne $c.Guidance) { $row.GuidanceEdited++ }
                continue
            }
            if ($DryRun) { $row.Created++; continue }
            $body = @{
                sprk_code                   = $c.Code
                sprk_codetype               = $typeValue[$c.Type]
                sprk_description            = $c.Description
                "$($nav.ReferencingEntityNavigationPropertyName)@odata.bind" = "/$($setMeta.EntitySetName)($setId)"
            }
            if ($c.Phase) { $body.sprk_phase = $c.Phase }
            if ($c.Guidance) { $body.sprk_guidancetext = $c.Guidance }
            try {
                Invoke-Dv -Path $codeMeta.EntitySetName -Method POST -Body $body | Out-Null
                $row.Created++
            } catch {
                # Two runs racing, or a stale read: the alternate key refuses the duplicate, which means the row is there.
                if ($_.Exception.Message -notmatch '(?i)duplicate|already exists|0x80060892|0x80040237') { throw }
                $row.Unchanged++
            }
        }
        Write-Host ("  codes: created {0}, updated {1} (never), unchanged {2}; left alone because they differ from the CSV: {3} description/phase/type, {4} guidance" -f $row.Created, $row.Updated, $row.Unchanged, $row.Drift, $row.GuidanceEdited)
        $summary.Add([pscustomobject]$row)
    }
}
catch [SeedValidationException] {
    Write-Host ""
    Write-Host "STOPPED (exit 2): $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Rows already written stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
$summary | Format-Table Set, Csv, SetCreated, Created, Updated, Unchanged, Drift, GuidanceEdited -AutoSize | Out-String | Write-Host
$totalCreated = ($summary | Measure-Object Created -Sum).Sum + ($summary | Measure-Object SetCreated -Sum).Sum
Write-Host ("sets created: {0}   codes created: {1}   updated: 0 (existing rows are never updated)   unchanged: {2}" -f ($summary | Measure-Object SetCreated -Sum).Sum, ($summary | Measure-Object Created -Sum).Sum, ($summary | Measure-Object Unchanged -Sum).Sum)
Write-Host ("writes this run: {0}" -f $(if ($DryRun) { "0 (WhatIf; $totalCreated would be made)" } else { $totalCreated }))
Write-Host "Set ids:"
$summary | ForEach-Object { Write-Host ("  {0}  {1}" -f $_.SetId, $_.Set) }
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
