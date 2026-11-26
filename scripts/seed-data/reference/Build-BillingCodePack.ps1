<#
.SYNOPSIS
    Generates the UTBMS billing-code files of the `legal-standard` reference pack (data-setup plan D5) from the
    eBilling seed files, so the pack and the standalone seed script cannot disagree.

.DESCRIPTION
    Reads scripts/seed-data/ebilling/utbms-*.csv (the codes) and utbms-codeset-tuning.json (the matching text of the
    six task code sets) and writes, into scripts/seed-data/reference/packs/legal-standard/:

      sprk_billingcodeset.json   one row per code set (name, body, version, source URL, tuning text)
      sprk_billingcode.json      one row per code, with its code set in the `sprk_codeset` lookup (the membership)

    These are the same rows scripts/ebilling/Seed-EbillingBillingCodes.ps1 creates; H12b seeds them create-if-missing
    on every new stamp (DataverseWebApiReferenceDataSeeder, starter taxonomy pack). Nothing here invents a code: a
    column comes from the CSV or the tuning file, and an empty phase / guidance becomes null (the seeder does not send
    nulls, as the script does not send empty values).

    Row ids are derived (UUID v5) from the set name and the code, so a re-generation never changes an id and no
    environment's id is copied. Product code resolves sets by name and codes by (set, code), never by id.

    The matter-type and practice-area files hold the default-code-set mapping (`sprk_defaultbillingcodeset`) by hand;
    this script does not touch them.

    Run it after editing a CSV or the tuning file, and commit the result. The pack unit tests fail when the pack and
    the CSVs disagree.

.PARAMETER CsvDirectory
    Folder holding utbms-*.csv and utbms-codeset-tuning.json. Default: scripts/seed-data/ebilling.

.PARAMETER OutDirectory
    The pack folder. Default: scripts/seed-data/reference/packs/legal-standard.

.PARAMETER Check
    Write nothing; exit 1 when a file on disk differs from what would be generated.

.EXAMPLE
    pwsh scripts/seed-data/reference/Build-BillingCodePack.ps1
    pwsh scripts/seed-data/reference/Build-BillingCodePack.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$CsvDirectory = (Join-Path $PSScriptRoot '..\ebilling'),
    [string]$OutDirectory = (Join-Path $PSScriptRoot 'packs\legal-standard'),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$RequiredColumns = 'set_name', 'body', 'version', 'source_url', 'download_date', 'code', 'code_type', 'phase', 'description', 'guidance_text'
$CodeTypeValue = @{ Task = 100000000; Activity = 100000001; Expense = 100000002 }   # sprk_billingcode.sprk_codetype
$IdNamespace = [guid]'b3a5e5c4-2d0f-4f0e-9a5b-7e3d1c8f6a21'                           # fixed: changing it changes every id

$jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$jsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function ConvertTo-JsonText($value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [int] -or $value -is [long]) { return [string]$value }
    return [System.Text.Json.JsonSerializer]::Serialize[string]([string]$value, $jsonOptions)
}

# RFC 4122 version-5 UUID of $Name in the fixed namespace (stable across runs and machines).
function Get-SeedGuid([string]$Name) {
    $ns = $IdNamespace.ToByteArray()
    [Array]::Reverse($ns, 0, 4); [Array]::Reverse($ns, 4, 2); [Array]::Reverse($ns, 6, 2)   # .NET little-endian -> network order
    $data = [System.Text.Encoding]::UTF8.GetBytes($Name)
    $sha = [System.Security.Cryptography.SHA1]::Create()
    $hash = $sha.ComputeHash([byte[]]($ns + $data))
    $b = [byte[]]$hash[0..15]
    $b[6] = ($b[6] -band 0x0F) -bor 0x50
    $b[8] = ($b[8] -band 0x3F) -bor 0x80
    [Array]::Reverse($b, 0, 4); [Array]::Reverse($b, 4, 2); [Array]::Reverse($b, 6, 2)       # network order -> .NET
    return ([guid]::new($b)).ToString()
}

function Get-NullIfEmpty([string]$s) {
    $t = $s.Trim()
    if ($t.Length -eq 0) { return $null }
    return $t
}

# ---- read and validate the sources ----------------------------------------------------------------------------------

$files = @(Get-ChildItem -Path $CsvDirectory -Filter 'utbms-*.csv' -File | Sort-Object Name)
if ($files.Count -eq 0) { throw "No utbms-*.csv files found in $CsvDirectory." }
$tuningPath = Join-Path $CsvDirectory 'utbms-codeset-tuning.json'
$tuning = @{}
foreach ($t in @((Get-Content -Path $tuningPath -Raw -Encoding utf8 | ConvertFrom-Json).sets)) { $tuning[([string]$t.name).Trim()] = $t }

$sets = [System.Collections.Generic.List[object]]::new()
foreach ($f in $files) {
    $rows = @(Import-Csv -Path $f.FullName -Encoding utf8)
    if ($rows.Count -eq 0) { throw "$($f.Name): no data rows" }
    $missing = @($RequiredColumns | Where-Object { $_ -notin $rows[0].PSObject.Properties.Name })
    if ($missing.Count -gt 0) { throw "$($f.Name): missing column(s) $($missing -join ', ')" }
    foreach ($c in 'set_name', 'body', 'version', 'source_url') {
        $vals = @($rows | ForEach-Object { $_.$c.Trim() } | Sort-Object -Unique)
        if ($vals.Count -ne 1 -or [string]::IsNullOrWhiteSpace($vals[0])) { throw "$($f.Name): column $c must hold one non-empty value on every row" }
    }
    $seen = @{}
    foreach ($r in $rows) {
        $code = $r.code.Trim()
        if ([string]::IsNullOrWhiteSpace($code)) { throw "$($f.Name): a row has an empty code" }
        if ($seen.ContainsKey($code)) { throw "$($f.Name): code '$code' is not unique within its set" }
        $seen[$code] = $true
        if (-not $CodeTypeValue.ContainsKey($r.code_type.Trim())) { throw "$($f.Name): code '$code' has code_type '$($r.code_type)' (Task / Activity / Expense)" }
    }
    $sets.Add([pscustomobject]@{ Name = $rows[0].set_name.Trim(); Body = $rows[0].body.Trim(); Version = $rows[0].version.Trim(); SourceUrl = $rows[0].source_url.Trim(); Rows = $rows })
}
foreach ($name in $tuning.Keys) {
    if ($name -notin @($sets | ForEach-Object { $_.Name })) { throw "The tuning file names '$name', which no utbms-*.csv defines." }
}

# ---- sprk_billingcodeset.json ---------------------------------------------------------------------------------------

$sb = [System.Text.StringBuilder]::new()
$null = $sb.Append("{`n")
$null = $sb.Append("  `"table`": `"sprk_billingcodeset`",`n  `"entitySet`": `"sprk_billingcodesets`",`n  `"primaryKey`": `"sprk_billingcodesetid`",`n")
$null = $sb.Append("  `"match`": `"naturalKey`",`n  `"nameColumn`": `"sprk_name`",`n")
$null = $sb.Append("  `"columns`": [`"sprk_name`", `"sprk_body`", `"sprk_version`", `"sprk_sourceurl`", `"sprk_description`", `"sprk_matchhints`"],`n")
$null = $sb.Append("  `"rows`": [`n")
$setLines = foreach ($s in $sets) {
    $tune = $tuning[$s.Name]
    $id = Get-SeedGuid "sprk_billingcodeset|$($s.Name)"
    $fields = @(
        "      `"sprk_billingcodesetid`": $(ConvertTo-JsonText $id)",
        "      `"sprk_name`": $(ConvertTo-JsonText $s.Name)",
        "      `"sprk_body`": $(ConvertTo-JsonText $s.Body)",
        "      `"sprk_version`": $(ConvertTo-JsonText $s.Version)",
        "      `"sprk_sourceurl`": $(ConvertTo-JsonText $s.SourceUrl)",
        "      `"sprk_description`": $(ConvertTo-JsonText $(if ($tune) { ([string]$tune.description).Trim() } else { $null }))",
        "      `"sprk_matchhints`": $(ConvertTo-JsonText $(if ($tune) { ([string]$tune.matchHints).Trim() } else { $null }))"
    )
    "    {`n" + ($fields -join ",`n") + "`n    }"
}
$null = $sb.Append(($setLines -join ",`n") + "`n  ]`n}`n")
$setJson = $sb.ToString()

# ---- sprk_billingcode.json ------------------------------------------------------------------------------------------

$sb = [System.Text.StringBuilder]::new()
$null = $sb.Append("{`n")
$null = $sb.Append("  `"table`": `"sprk_billingcode`",`n  `"entitySet`": `"sprk_billingcodes`",`n  `"primaryKey`": `"sprk_billingcodeid`",`n")
$null = $sb.Append("  `"match`": `"naturalKey`",`n  `"naturalKey`": `"sprk_code`",`n  `"nameColumn`": `"sprk_code`",`n")
$null = $sb.Append("  `"naturalKeyScope`": `"sprk_codeset`",`n")
# Laid out the way prettier (the pre-commit hook) formats it, so a committed file equals what this script writes.
$null = $sb.Append("  `"lookups`": [`n    {`n      `"column`": `"sprk_codeset`",`n      `"navigationProperty`": `"sprk_CodeSet`",`n      `"targetTable`": `"sprk_billingcodeset`",`n      `"targetEntitySet`": `"sprk_billingcodesets`"`n    }`n  ],`n")
$null = $sb.Append("  `"columns`": [`"sprk_code`", `"sprk_codetype`", `"sprk_phase`", `"sprk_description`", `"sprk_guidancetext`"],`n")
$null = $sb.Append("  `"rows`": [`n")
$codeLines = foreach ($s in $sets) {
    $setId = Get-SeedGuid "sprk_billingcodeset|$($s.Name)"
    foreach ($r in $s.Rows) {
        $code = $r.code.Trim()
        $fields = @(
            "      `"sprk_billingcodeid`": $(ConvertTo-JsonText (Get-SeedGuid "sprk_billingcode|$($s.Name)|$code"))",
            "      `"sprk_codeset`": $(ConvertTo-JsonText $setId)",
            "      `"sprk_code`": $(ConvertTo-JsonText $code)",
            "      `"sprk_codetype`": $($CodeTypeValue[$r.code_type.Trim()])",
            "      `"sprk_phase`": $(ConvertTo-JsonText (Get-NullIfEmpty $r.phase))",
            "      `"sprk_description`": $(ConvertTo-JsonText (Get-NullIfEmpty $r.description))",
            "      `"sprk_guidancetext`": $(ConvertTo-JsonText (Get-NullIfEmpty $r.guidance_text))"
        )
        "    {`n" + ($fields -join ",`n") + "`n    }"
    }
}
$null = $sb.Append(($codeLines -join ",`n") + "`n  ]`n}`n")
$codeJson = $sb.ToString()

# ---- write or check -------------------------------------------------------------------------------------------------

$outputs = [ordered]@{ 'sprk_billingcodeset.json' = $setJson; 'sprk_billingcode.json' = $codeJson }
$utf8 = [System.Text.UTF8Encoding]::new($false)
$differs = $false
foreach ($name in $outputs.Keys) {
    $path = Join-Path $OutDirectory $name
    if ($Check) {
        $onDisk = if (Test-Path $path) { [System.IO.File]::ReadAllText($path, $utf8).Replace("`r`n", "`n") } else { $null }
        if ($onDisk -ne $outputs[$name]) { Write-Host "DIFFERS: $path" -ForegroundColor Red; $differs = $true }
        else { Write-Host "up to date: $name" -ForegroundColor Green }
    } else {
        [System.IO.File]::WriteAllText($path, $outputs[$name], $utf8)
        Write-Host "wrote $path" -ForegroundColor Green
    }
}
Write-Host "$($sets.Count) code sets, $((($sets | ForEach-Object { $_.Rows.Count }) | Measure-Object -Sum).Sum) codes."
if ($Check -and $differs) { exit 1 }
