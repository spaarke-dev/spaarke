<#
.SYNOPSIS
    Exports the Tier 1 product-configuration reference tables from a Dataverse environment into
    source-controlled seed files that keep every row's primary key.

.DESCRIPTION
    Spaarke product code depends on rows in three reference tables (event types, agreement types,
    triage categories). Some of it hard-codes their row ids. A new customer stamp must therefore
    receive the SAME rows with the SAME primary keys. This script is the one-way bridge from the
    authoring environment (spaarkedev1) into git:

        Dataverse (read-only GET)  ->  scripts/seed-data/reference/<table>.json  ->  reviewed PR
        ->  embedded in the L2 Worker  ->  H12b ReferenceDataSeeder (create-if-missing by id)

    Provisioning NEVER reads dev. Re-running this script is a deliberate act whose diff the owner
    reviews; the output is deterministic (rows sorted by primary key, columns sorted by name, LF
    line endings, no BOM) so an unchanged environment produces a byte-identical file.

    SAFETY: only HTTP GET requests are issued. The token comes from the operator's own
    `az account get-access-token`. Nothing is written to Dataverse. This is an operator/dev tool;
    the L2 control plane never runs it (its main site never shells out).

    WHAT IS EXPORTED
      - Every ACTIVE row (statecode = 0).
      - The primary key plus every custom column that is valid for create, EXCEPT the per-table
        exclusions below. System/audit columns, owner, state/status and lookup navigation columns
        are never exported.

    WHAT IS EXCLUDED, AND WHY (environment-specific values must not travel to a stamp)
      - sprk_triagecategory.sprk_labelledsetsize / sprk_measuredrecall / sprk_recallmeasuredon:
        a recall measurement taken on the dev labelled set. A new stamp has measured nothing.

    PORTABILITY NOTES for the columns that ARE exported (verified 2026-11-24)
      - sprk_eventtype_ref.sprk_eventtypeguid: equals the row's own primary key (a self-reference),
        so it is portable because the seeder preserves primary keys.
      - sprk_eventtype_ref.sprk_sidepaneformguid: ids of sprk_event main forms. All four distinct
        ids ship in the SpaarkeMaster solution (src/dataverse/solutions/SpaarkeMaster/Entities/
        sprk_Event/FormXml/main/), and form ids are preserved on import, so they are portable.
      - sprk_eventtype_ref.sprk_eventtypecolor: an option-set value (ships in SpaarkeMaster).
      - sprk_agreementtype.sprk_knowledgepackref: a reference CODE (e.g. KNW-011), not a GUID. It
        resolves on a stamp only once the knowledge record with that code exists there (the AI
        content gap tracked as L56).

.PARAMETER EnvironmentUrl
    Dataverse environment to read. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER OutputDirectory
    Where the JSON files are written. Default: this script's folder.

.EXAMPLE
    pwsh scripts/seed-data/reference/Export-ReferenceData.ps1

.NOTES
    Requires `az login` as a user who can read the tables. PowerShell 7+.
#>
[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$OutputDirectory = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Columns never exported even though they are custom + valid for create (environment-specific).
$tables = @(
    @{ LogicalName = 'sprk_eventtype_ref'; Exclude = @() },
    @{ LogicalName = 'sprk_agreementtype'; Exclude = @() },
    @{ LogicalName = 'sprk_triagecategory'; Exclude = @('sprk_labelledsetsize', 'sprk_measuredrecall', 'sprk_recallmeasuredon') }
)

$baseUrl = $EnvironmentUrl.TrimEnd('/')
$api = "$baseUrl/api/data/v9.2"

$token = az account get-access-token --resource $baseUrl --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw "az account get-access-token failed for $baseUrl. Run 'az login' first."
}
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-Version'    = '4.0'
    'OData-MaxVersion' = '4.0'
}

function Get-DataverseJson([string]$Uri) {
    # READ-ONLY: every call in this script is a GET.
    return Invoke-RestMethod -Method Get -Headers $headers -Uri $Uri
}

$jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$jsonOptions.WriteIndented = $true
# Keep apostrophes, ampersands and angle brackets readable in the diff (field-config JSON, cues).
$jsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function ConvertTo-JsonScalar($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) { return $(if ($Value) { 'true' } else { 'false' }) }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) {
        return [System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    # Normalise Memo line endings so a re-export never diffs on CRLF vs LF.
    $text = ([string]$Value) -replace "`r`n", "`n"
    return [System.Text.Json.JsonSerializer]::Serialize($text, $jsonOptions)
}

foreach ($table in $tables) {
    $logicalName = $table.LogicalName

    $meta = Get-DataverseJson ("$api/EntityDefinitions(LogicalName='$logicalName')" +
        '?$select=LogicalName,EntitySetName,PrimaryIdAttribute' +
        '&$expand=Attributes($select=LogicalName,IsCustomAttribute,IsValidForCreate,AttributeOf,AttributeType)')

    $entitySet = $meta.EntitySetName
    $primaryKey = $meta.PrimaryIdAttribute

    $columns = @(
        $meta.Attributes |
            Where-Object {
                $_.IsCustomAttribute -and $_.IsValidForCreate -and -not $_.AttributeOf -and
                $_.AttributeType -notin @('Lookup', 'Owner', 'Customer', 'Virtual', 'State', 'Status') -and
                $_.LogicalName -notin $table.Exclude -and $_.LogicalName -ne $primaryKey
            } |
            ForEach-Object { $_.LogicalName } |
            Sort-Object { $_ } -CaseSensitive
    )

    $select = (@($primaryKey) + $columns) -join ','
    $uri = "$api/${entitySet}?`$select=$select&`$filter=statecode eq 0&`$orderby=$primaryKey"
    $rawRows = New-Object System.Collections.Generic.List[object]
    while ($uri) {
        $page = Get-DataverseJson $uri
        foreach ($row in $page.value) { $rawRows.Add($row) }
        $uri = $page.PSObject.Properties['@odata.nextLink'] ? $page.'@odata.nextLink' : $null
    }

    $rows = @($rawRows | Sort-Object { ([string]$_.$primaryKey).ToLowerInvariant() } -CaseSensitive)

    # Hand-written writer: PowerShell wraps values in PSObject, which System.Text.Json cannot
    # serialise as a whole document, so each scalar is serialised on its own as a plain .NET type.
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append("{`n")
    [void]$sb.Append("  `"table`": $(ConvertTo-JsonScalar $logicalName),`n")
    [void]$sb.Append("  `"entitySet`": $(ConvertTo-JsonScalar $entitySet),`n")
    [void]$sb.Append("  `"primaryKey`": $(ConvertTo-JsonScalar $primaryKey),`n")
    [void]$sb.Append("  `"columns`": [`n")
    [void]$sb.Append((($columns | ForEach-Object { "    $(ConvertTo-JsonScalar $_)" }) -join ",`n"))
    [void]$sb.Append("`n  ],`n  `"rows`": [`n")
    $rowTexts = foreach ($source in $rows) {
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add("      $(ConvertTo-JsonScalar $primaryKey): $(ConvertTo-JsonScalar ([string]$source.$primaryKey).ToLowerInvariant())")
        foreach ($column in $columns) {
            $lines.Add("      $(ConvertTo-JsonScalar $column): $(ConvertTo-JsonScalar $source.$column)")
        }
        "    {`n" + ($lines -join ",`n") + "`n    }"
    }
    [void]$sb.Append(($rowTexts -join ",`n"))
    [void]$sb.Append("`n  ]`n}`n")
    $text = $sb.ToString()

    $path = Join-Path $OutputDirectory "$logicalName.json"
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
    Write-Host ("{0,-22} {1,3} row(s), {2} column(s) -> {3}" -f $logicalName, @($rows).Count, $columns.Count, $path)
}
