<#
.SYNOPSIS
    Seeds the synthetic reference data for the 120-invoice billing-guideline subset on dev (task 161, for 161's run and 162).
    WRITTEN, NEVER RUN, by the agent that authored it. Needs the owner's OK; run -WhatIf first.

.DESCRIPTION
    Reads evaluation-set/guidelines/reference-data/RS-0n.json (ontology-r1, PR #2545) and creates, per file:
      1. ONE sprk_organization (firm) per reference-data `key`  - never one per firm name, never shared across rule sets
         (six names occur in two rule sets with different caps and rosters; a shared record would merge rosters and rate cards).
         Name = "<Prefix><firm name> [RS-0n]" so a name that repeats across sets stays distinct and the cleanup is one prefix.
      2. ONE sprk_matter per subset invoice (open/close date, billing status Final where the guideline has a matter-dates rule, budget,
         assigned law firm 1 = that rule set's firm record).
      3. ONE rate-card workbook per firm key (build_ratecard_workbooks.py), imported Draft through POST /api/finance/ratecards/import and
         published. An unrostered timekeeper has no approved rate: production raises ApprovedRate "no approved rate" beside
         ApprovedTimekeeper, and the production key expects it (16 in the subset). Do not "fix" the rosters.
    Everything is named with -Prefix (default "zz-") and recorded in the manifest so -Remove can undo exactly what was created.

    Safety: -WhatIf prints the plan and writes nothing. A live run needs -OwnerApproval. A preflight reads table metadata and ABORTS if a
    required column would be left empty, and skips (and reports) optional columns that do not exist on the table, so a wrong column name
    cannot half-seed. Idempotent by name: a record whose name already exists is reused, not duplicated.

    Not covered here (separate owner-approved steps, see calibration-report.md section 9): publishing the six rule sets drafted from GL-0n,
    and submitting the PDFs (Submit-GuidelineSubset.ps1).

.EXAMPLE
    pwsh -File Seed-GuidelineReferenceData.ps1 -WhatIf
.EXAMPLE
    pwsh -File Seed-GuidelineReferenceData.ps1 -OwnerApproval 'Ralph, 2026-11-xx, <where recorded>' -DataverseUrl https://spaarkedev1.crm.dynamics.com -BffUrl https://spaarke-bff-dev.azurewebsites.net
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$BffUrl,
    [string]$ReferenceDataPath = (Join-Path $PSScriptRoot '../../evaluation-set/guidelines/reference-data'),
    [string]$WorkbookFolder = (Join-Path $PSScriptRoot '../../evaluation-set/local/guidelines/ratecards'),
    [string]$ManifestPath = (Join-Path $PSScriptRoot '../../evaluation-set/local/guidelines/seed-manifest.json'),
    [string]$Prefix = 'zz-',
    [string]$OwnerApproval,
    [switch]$SkipRateCards,
    [switch]$Remove
)
. (Join-Path $PSScriptRoot 'Calib-Common.ps1')

$sets = Read-ReferenceData -Path $ReferenceDataPath
$firmCount = ($sets | ForEach-Object { $_.firms.Count } | Measure-Object -Sum).Sum
$matterCount = ($sets | ForEach-Object { $_.matters.Count } | Measure-Object -Sum).Sum

Write-Host "PLAN (prefix '$Prefix')"
foreach ($s in $sets) { Write-Host ("  {0}: {1} firms (one record per key), {2} matters, {1} rate-card workbooks" -f $s.rule_set_id, $s.firms.Count, $s.matters.Count) }
Write-Host ("  total: {0} firm records, {1} matters, {2} rate cards (import Draft then publish)" -f $firmCount, $matterCount, $(if ($SkipRateCards) { 0 } else { $firmCount }))
$dupNames = $sets | ForEach-Object { $_.firms } | Group-Object name | Where-Object Count -gt 1
Write-Host ("  firm names that repeat across rule sets (kept as separate records): {0}" -f @($dupNames).Count)

if ($WhatIfPreference) { Write-Host 'WhatIf: nothing written, no token requested.'; return }
Assert-OwnerApproval -OwnerApproval $OwnerApproval
if ([string]::IsNullOrWhiteSpace($DataverseUrl)) { throw 'DataverseUrl is required (or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$dvToken = Get-DevToken -Resource $DataverseUrl

# ---- Remove mode: delete exactly what the manifest lists, children first -----------------------------------------------------------
if ($Remove) {
    $m = Get-Content -Raw -Encoding utf8 $ManifestPath | ConvertFrom-Json
    foreach ($id in $m.matters.PSObject.Properties.Value) { if ($PSCmdlet.ShouldProcess("sprk_matter $id", 'DELETE')) { Invoke-Dv -Method DELETE -Url "$DataverseUrl/api/data/v9.2/sprk_matters($id)" -Token $dvToken | Out-Null } }
    foreach ($id in $m.firms.PSObject.Properties.Value) { if ($PSCmdlet.ShouldProcess("sprk_organization $id", 'DELETE')) { Invoke-Dv -Method DELETE -Url "$DataverseUrl/api/data/v9.2/sprk_organizations($id)" -Token $dvToken | Out-Null } }
    Write-Host 'Removed matters and firms listed in the manifest. Rate cards cascade or are deleted from the rate card admin page.'
    return
}

# ---- Preflight ------------------------------------------------------------------------------------------------------------------------
$orgWanted = @('sprk_name', 'sprk_email', 'sprk_emailaddress', 'sprk_billingemail')
$matterWanted = @('sprk_name', 'sprk_openeddate', 'sprk_closeddate', 'sprk_billingstatus', 'sprk_totalbudget', 'sprk_ponumber')
$orgCols = Get-ColumnSet -DataverseUrl $DataverseUrl -Token $dvToken -LogicalName 'sprk_organization' -Wanted $orgWanted
$matterCols = Get-ColumnSet -DataverseUrl $DataverseUrl -Token $dvToken -LogicalName 'sprk_matter' -Wanted $matterWanted
Write-Host "Preflight sprk_organization: write $($orgCols.Present -join ', '); absent $($orgCols.Missing -join ', '); required-not-written $($orgCols.RequiredNotWritten -join ', ')"
Write-Host "Preflight sprk_matter: write $($matterCols.Present -join ', '); absent $($matterCols.Missing -join ', '); required-not-written $($matterCols.RequiredNotWritten -join ', ')"
if ($orgCols.Present -notcontains 'sprk_name' -or $matterCols.Present -notcontains 'sprk_name') { throw 'sprk_name missing on a target table.' }
$blocking = @($orgCols.RequiredNotWritten) + @($matterCols.RequiredNotWritten) | Where-Object { $_ }
if ($blocking.Count -gt 0) { throw "Required columns would be empty: $($blocking -join ', '). Extend this script's payload with owner-agreed values before running." }

# ---- Firms and matters ---------------------------------------------------------------------------------------------------------------
$manifest = [ordered]@{ approvedBy = $OwnerApproval; prefix = $Prefix; at = (Get-Date).ToUniversalTime().ToString('o'); firms = [ordered]@{}; matters = [ordered]@{}; rateCards = [ordered]@{} }
$emailCol = ($orgCols.Present | Where-Object { $_ -ne 'sprk_name' } | Select-Object -First 1)
foreach ($s in $sets) {
    foreach ($firm in $s.firms) {
        $name = "$Prefix$($firm.name) [$($s.rule_set_id)]"
        $existing = Invoke-Dv -Url ("$DataverseUrl/api/data/v9.2/sprk_organizations?`$select=sprk_organizationid&`$filter=sprk_name eq '{0}'" -f $name.Replace("'", "''")) -Token $dvToken
        if (@($existing.value).Count -gt 0) { $manifest.firms[$firm.key] = $existing.value[0].sprk_organizationid; continue }
        $body = @{ sprk_name = $name }
        if ($emailCol) { $body[$emailCol] = $firm.billing_contact_email }
        if ($PSCmdlet.ShouldProcess($name, 'POST sprk_organization')) {
            $r = Invoke-WebRequest -Method POST -Uri "$DataverseUrl/api/data/v9.2/sprk_organizations" -Headers @{ Authorization = "Bearer $dvToken"; 'OData-Version' = '4.0'; Prefer = 'return=representation' } -ContentType 'application/json' -Body ($body | ConvertTo-Json)
            $manifest.firms[$firm.key] = ($r.Content | ConvertFrom-Json).sprk_organizationid
        }
    }
    foreach ($mt in $s.matters) {
        $firmKey = ($s.firms | Where-Object { $_.name -eq $mt.assigned_law_firm_1 } | Select-Object -First 1).key   # same rule set only
        $name = "$Prefix$($mt.name) [$($s.rule_set_id)]"
        $body = @{ sprk_name = $name }
        if ($matterCols.Present -contains 'sprk_openeddate') { $body.sprk_openeddate = $mt.open_date }
        if ($matterCols.Present -contains 'sprk_closeddate' -and $mt.close_date) { $body.sprk_closeddate = $mt.close_date }
        if ($matterCols.Present -contains 'sprk_billingstatus') { $body.sprk_billingstatus = @{ Open = 100000000; Final = 100000001; Closed = 100000002 }[$mt.billing_status] }
        if ($matterCols.Present -contains 'sprk_totalbudget') { $body.sprk_totalbudget = $mt.budget }
        if ($matterCols.Present -contains 'sprk_ponumber') { $body.sprk_ponumber = $mt.po_number }
        if ($firmKey -and $manifest.firms.Contains($firmKey)) { $body['sprk_assignedlawfirm1@odata.bind'] = "/sprk_organizations($($manifest.firms[$firmKey]))" }
        if ($PSCmdlet.ShouldProcess($name, 'POST sprk_matter')) {
            $r = Invoke-WebRequest -Method POST -Uri "$DataverseUrl/api/data/v9.2/sprk_matters" -Headers @{ Authorization = "Bearer $dvToken"; 'OData-Version' = '4.0'; Prefer = 'return=representation' } -ContentType 'application/json' -Body ($body | ConvertTo-Json)
            $manifest.matters["$($s.rule_set_id)|$($mt.test_id)"] = ($r.Content | ConvertFrom-Json).sprk_matterid
        }
    }
}

# ---- Rate cards (BFF) ------------------------------------------------------------------------------------------------------------------
if (-not $SkipRateCards) {
    if ([string]::IsNullOrWhiteSpace($BffUrl)) { throw 'BffUrl is required for the rate-card import (or pass -SkipRateCards).' }
    & python -I (Join-Path $PSScriptRoot 'build_ratecard_workbooks.py') --reference-data $ReferenceDataPath --out $WorkbookFolder
    $bffToken = Get-DevToken -Resource $BffUrl
    foreach ($s in $sets) {
        $firstMatter = $manifest.matters.GetEnumerator() | Where-Object { $_.Key -like "$($s.rule_set_id)|*" } | Select-Object -First 1
        foreach ($firm in $s.firms) {
            $file = Join-Path $WorkbookFolder "$($firm.key).xlsx"
            $url = "$BffUrl/api/finance/ratecards/import?firmId=$($manifest.firms[$firm.key])&name=$([uri]::EscapeDataString("$Prefix$($firm.key)"))"
            if ($PSCmdlet.ShouldProcess($firm.key, 'POST /api/finance/ratecards/import then publish')) {
                $res = Invoke-RestMethod -Method POST -Uri $url -Headers @{ Authorization = "Bearer $bffToken" } -Form @{ file = Get-Item $file }
                $id = if ($res.rateCardId) { $res.rateCardId } else { $res.id }
                Invoke-RestMethod -Method POST -Uri "$BffUrl/api/finance/ratecards/$id/publish" -Headers @{ Authorization = "Bearer $bffToken" } | Out-Null
                $manifest.rateCards[$firm.key] = $id
            }
        }
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path $ManifestPath) | Out-Null
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $ManifestPath
Write-Host "Done. Manifest: $ManifestPath (firms $($manifest.firms.Count), matters $($manifest.matters.Count), rate cards $($manifest.rateCards.Count))"
