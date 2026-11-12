<#
.SYNOPSIS
    READ-ONLY export of the data the 161 scorers need from dev: (a) a pipeline run (per-field confidence, values, findings) and (b) the
    structured correction records. WRITTEN, NEVER RUN, by the agent that authored it. Reading dev also needs the owner's OK. -WhatIf prints
    the queries and touches nothing.

.DESCRIPTION
    -Mode Run         : for each item in a run manifest (Submit-GuidelineSubset.ps1) reads sprk_invoice -> sprk_billingevent lines
                        (sprk_extractionconfidence JSON: per field {confidence, combined, page, repair} and the provenance member with the
                        analyzer id + API version), the stored line values, and the invoice's findings (sprk_findingsjson). Writes
                        run-export.json (schema ebilling-run-export/1, see score_run.py) into the git-ignored evaluation-set/local/.
                        The stored JSON keeps analyzer confidence and combined confidence per field but NOT the arithmetic / reference /
                        self-report breakdown: the export fills `signals` only when a signals member exists (a later 049 change, or a
                        debug build); otherwise score_run.py reports per-signal contribution as unavailable and the weights/caps what-if
                        is skipped. See calibration-report.md section 7.
    -Mode Corrections : reads sprk_invoicecorrections (sprk_fieldname, sprk_aiconfidence, sprk_outcometype, sprk_islabel, sprk_sourcecode,
                        sprk_sourceversion). ONE query, NO filter on sprk_islabel, so bulk-accepted and unreviewed rows are COUNTED and
                        score_corrections.py can separate them. Writes corrections-export.json (ids and numbers only; no values).

    Column names come from the BFF constants (InvoiceSubmissionPersister, InvoiceTotalsService). A preflight lists any that do not exist on
    the table and stops, so a wrong name fails loudly rather than exporting nothing.

.EXAMPLE
    pwsh -File Export-CalibrationData.ps1 -Mode Corrections -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory)][ValidateSet('Run', 'Corrections')][string]$Mode,
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$RunManifestPath,
    [string]$OutPath,
    [string]$OwnerApproval
)
. (Join-Path $PSScriptRoot 'Calib-Common.ps1')

$lineCols = 'sprk_billingeventid,sprk_linesequence,sprk_eventdate,sprk_timekeeper,sprk_quantity,sprk_rate,sprk_amount,sprk_costtype,sprk_extractionconfidence,sprk_codingconfidence'
$corrCols = 'sprk_fieldname,sprk_aiconfidence,sprk_outcometype,sprk_islabel,sprk_sourcecode,sprk_sourceversion'
Write-Host "PLAN ($Mode)"
if ($Mode -eq 'Corrections') {
    Write-Host "  GET sprk_invoicecorrections?`$select=$corrCols   (paged, no filter on sprk_islabel)"
    Write-Host '  -> corrections-export.json {rows[], unreviewed[]}'
} else {
    Write-Host "  per run-manifest item: GET sprk_invoices(<invoiceId>) ; GET sprk_billingevents?`$select=$lineCols&`$filter=_sprk_invoice_value eq <invoiceId>"
    Write-Host '  per invoice: findings from sprk_findingsjson (rule, line refs, read fields, consequence)'
    Write-Host '  -> run-export.json {run, source, invoices[]}'
}
if ($WhatIfPreference) { Write-Host 'WhatIf: nothing read, no token requested.'; return }
Assert-OwnerApproval -OwnerApproval $OwnerApproval
if ([string]::IsNullOrWhiteSpace($DataverseUrl)) { throw 'DataverseUrl is required (or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$token = Get-DevToken -Resource $DataverseUrl
$localRoot = Join-Path $PSScriptRoot '../../evaluation-set/local/guidelines'
New-Item -ItemType Directory -Force -Path $localRoot | Out-Null

function Get-All([string]$url) {
    $rows = @()
    while ($url) { $r = Invoke-Dv -Url $url -Token $token -Headers @{ Prefer = 'odata.maxpagesize=500' }; $rows += $r.value; $url = $r.'@odata.nextLink' }
    return $rows
}

if ($Mode -eq 'Corrections') {
    $cols = Get-ColumnSet -DataverseUrl $DataverseUrl -Token $token -LogicalName 'sprk_invoicecorrection' -Wanted ($corrCols -split ',')
    if ($cols.Missing.Count -gt 0) { throw "Missing columns on sprk_invoicecorrection: $($cols.Missing -join ', ')" }
    $rows = Get-All "$DataverseUrl/api/data/v9.2/sprk_invoicecorrections?`$select=$corrCols"
    $out = [ordered]@{ schema = 'ebilling-corrections-export/1'; run = "corrections-$((Get-Date).ToString('yyyyMMdd-HHmm'))"; approvedBy = $OwnerApproval
        rows = @($rows | ForEach-Object { [ordered]@{ fieldName = $_.sprk_fieldname; aiConfidence = $_.sprk_aiconfidence; outcomeType = $_.sprk_outcometype; isLabel = [bool]$_.sprk_islabel; sourceCode = $_.sprk_sourcecode; sourceVersion = $_.sprk_sourceversion } })
        unreviewed = @() }
    if (-not $OutPath) { $OutPath = Join-Path $localRoot "$($out.run).json" }
    $out | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $OutPath
    Write-Host "Wrote $OutPath ($($out.rows.Count) rows). Score with: python -I score_corrections.py --export $OutPath"
    return
}

if (-not $RunManifestPath) { throw 'RunManifestPath is required for -Mode Run.' }
$run = Get-Content -Raw -Encoding utf8 $RunManifestPath | ConvertFrom-Json
$lc = Get-ColumnSet -DataverseUrl $DataverseUrl -Token $token -LogicalName 'sprk_billingevent' -Wanted ($lineCols -split ',' | Where-Object { $_ -ne 'sprk_billingeventid' })
if ($lc.Missing.Count -gt 0) { throw "Missing columns on sprk_billingevent: $($lc.Missing -join ', ')" }
$invoices = @()
$source = $null
foreach ($item in $run.items.PSObject.Properties) {
    $invId = $item.Value.invoiceId
    if (-not $invId) { continue }
    $lines = Get-All "$DataverseUrl/api/data/v9.2/sprk_billingevents?`$select=$lineCols&`$filter=_sprk_invoice_value eq $invId&`$orderby=sprk_linesequence"
    $outLines = @()
    foreach ($l in $lines) {
        $ext = if ($l.sprk_extractionconfidence) { $l.sprk_extractionconfidence | ConvertFrom-Json } else { $null }
        if ($ext -and $ext.provenance -and -not $source) { $source = @{ analyzerId = $ext.provenance.analyzerId; apiVersion = $ext.provenance.apiVersion } }
        $outLines += [ordered]@{
            n = $l.sprk_linesequence; type = $(if ($l.sprk_costtype -eq 100000001) { 'expense' } else { 'fee' })
            date = ("$($l.sprk_eventdate)").Substring(0, [Math]::Min(10, "$($l.sprk_eventdate)".Length)); timekeeper = "$($l.sprk_timekeeper)"
            hours = $l.sprk_quantity; rate = $l.sprk_rate; amount = $l.sprk_amount
            fields = $(if ($ext) { $ext.fields } else { @{} }); signals = $(if ($ext -and $ext.signals) { $ext.signals } else { $null })
        }
    }
    # UNVERIFIED MAPPING (K-3 in notes/161-calibration-report.md): header fields and findings are passed through from sprk_findingsjson as stored.
    # score_run.py / score_findings.py expect the export shape in their docstrings (ruleId = the production key's id, lineRefs, readFields), so the first
    # owner-approved export must be reconciled with a one-off mapping step before scoring. Nothing here has been run.
    $inv = Invoke-Dv -Url "$DataverseUrl/api/data/v9.2/sprk_invoices($invId)?`$select=sprk_findingsjson" -Token $token
    $f = if ($inv.sprk_findingsjson) { $inv.sprk_findingsjson | ConvertFrom-Json } else { $null }
    $invoices += [ordered]@{ id = $item.Name; header = $(if ($f -and $f.provenance) { $f.provenance.header } else { @{} }); lines = $outLines
        findings = $(if ($f -and $f.findings) { @($f.findings) } else { @() }) }
}
if (-not $source) { throw 'No line carried provenance (EB-D-170): cannot tell which analyzer read these invoices.' }
$out = [ordered]@{ schema = 'ebilling-run-export/1'; run = $run.run; approvedBy = $OwnerApproval; source = $source; invoices = $invoices }
if (-not $OutPath) { $OutPath = Join-Path $localRoot "run-export-$($run.run).json" }
$out | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 $OutPath
Write-Host "Wrote $OutPath ($($invoices.Count) invoices, analyzer $($source.analyzerId) $($source.apiVersion)). Score with: python -I score_run.py --export $OutPath"

