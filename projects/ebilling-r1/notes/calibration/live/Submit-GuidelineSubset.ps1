<#
.SYNOPSIS
    Submits the 120-invoice billing-guideline subset (and, with -ItemFile, any other evaluation item) through the internal submission route on
    dev, one run per analyzer (task 161 step 2). WRITTEN, NEVER RUN, by the agent that authored it. Costs money (Content Understanding + coding)
    and creates invoices on dev: needs the owner's OK and an agreed budget (calibration-report.md section 2). Run -WhatIf first.

.DESCRIPTION
    For each invoice id in guidelines/subset.json (15 seeded + 5 clean per rule set), POSTs the PDF from evaluation-set/local/synthetic/<id>.pdf
    (git-ignored; regenerate with the 002 generator) to
        POST {Bff}/api/finance/matters/{matterId}/invoice-submissions?firmId={firmId}      multipart: file, firmInvoiceNumber, poNumber
    with matterId and firmId taken from the seed manifest (Seed-GuidelineReferenceData.ps1) for THAT rule set: a firm key is never reused
    across rule sets. It records {itemId -> submissionId, statusUrl} in the run manifest and waits for a terminal status. It does NOT choose
    the analyzer: the run is tied to the deployed setting ContentUnderstanding__AnalyzerId / ApiVersion on spaarke-bff-dev, which the OWNER
    switches between runs (GA sprk_invoice_v1 2025-11-01 vs preview sprk_invoice_v1p 2026-06-01-preview, D-81). The export script reads the
    analyzer and API version back from each line's provenance (EB-D-170) and the scorer refuses a run that mixes two.

    Preconditions the script CHECKS and refuses on: the seed manifest exists; every subset id has a PDF; the six rule sets for RS-01..RS-06
    are published (checked by the owner before the run, see calibration-report.md section 9: the drafter is run on GL-0n and the proposals
    published; this script does not publish rules).

    Resubmitting the same firm invoice number to a matter is a resubmission (D-37): -RunLabel is appended to poNumber-independent state only;
    to repeat a run for the other analyzer use a fresh seed (Seed ... -Prefix zz2-) or -Remove first, so the two runs never share invoices.

.EXAMPLE
    pwsh -File Submit-GuidelineSubset.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$BffUrl,
    [string]$SubsetPath = (Join-Path $PSScriptRoot '../../evaluation-set/guidelines/subset.json'),
    [string]$ReferenceDataPath = (Join-Path $PSScriptRoot '../../evaluation-set/guidelines/reference-data'),
    [string]$SeedManifestPath = (Join-Path $PSScriptRoot '../../evaluation-set/local/guidelines/seed-manifest.json'),
    [string]$PdfFolder = (Join-Path $PSScriptRoot '../../evaluation-set/local/synthetic'),
    [string]$GroundTruthFolder = (Join-Path $PSScriptRoot '../../evaluation-set/ground-truth/compliance-invoices'),
    [Parameter(Mandatory)][string]$RunLabel,
    [string]$RunManifestPath,
    [string]$OwnerApproval,
    [int]$PollSeconds = 15,
    [int]$TimeoutMinutes = 20
)
. (Join-Path $PSScriptRoot 'Calib-Common.ps1')
if (-not $RunManifestPath) { $RunManifestPath = Join-Path $PSScriptRoot "../../evaluation-set/local/guidelines/run-$RunLabel.json" }

$subset = Get-Content -Raw -Encoding utf8 $SubsetPath | ConvertFrom-Json
$plan = @()
foreach ($rs in $subset.PSObject.Properties) {
    $ids = @($rs.Value.seeded) + @($rs.Value.clean)
    foreach ($id in $ids) { $plan += [pscustomobject]@{ RuleSet = $rs.Name; Id = $id; Clean = ($rs.Value.clean -contains $id) } }
}
Write-Host ("PLAN run '{0}': {1} invoices ({2} seeded, {3} clean) -> POST /api/finance/matters/{{id}}/invoice-submissions?firmId=" -f $RunLabel, $plan.Count, @($plan | Where-Object { -not $_.Clean }).Count, @($plan | Where-Object Clean).Count)
$missing = @($plan | Where-Object { -not (Test-Path (Join-Path $PdfFolder "$($_.Id).pdf")) })
Write-Host ("  PDFs present: {0} of {1} (missing are regenerated with the 002 generator into {2})" -f ($plan.Count - $missing.Count), $plan.Count, $PdfFolder)
if ($WhatIfPreference) { Write-Host 'WhatIf: nothing submitted, no token requested.'; return }
Assert-OwnerApproval -OwnerApproval $OwnerApproval
if ($missing.Count -gt 0) { throw "Missing PDFs: $(($missing | Select-Object -First 5 | ForEach-Object Id) -join ', ') ..." }
if (-not (Test-Path $SeedManifestPath)) { throw "Seed manifest not found: $SeedManifestPath. Run Seed-GuidelineReferenceData.ps1 first." }
if ([string]::IsNullOrWhiteSpace($BffUrl)) { throw 'BffUrl is required.' }

$seed = Get-Content -Raw -Encoding utf8 $SeedManifestPath | ConvertFrom-Json
$refs = @{}
foreach ($r in (Read-ReferenceData -Path $ReferenceDataPath)) { $refs[$r.rule_set_id] = $r }
$token = Get-DevToken -Resource $BffUrl
$run = [ordered]@{ run = $RunLabel; approvedBy = $OwnerApproval; at = (Get-Date).ToUniversalTime().ToString('o'); items = [ordered]@{} }

foreach ($p in $plan) {
    $gt = Get-Content -Raw -Encoding utf8 (Join-Path $GroundTruthFolder "$($p.Id).json") | ConvertFrom-Json
    $matter = $refs[$p.RuleSet].matters | Where-Object { $_.test_id -eq $p.Id } | Select-Object -First 1
    $firm = $refs[$p.RuleSet].firms | Where-Object { $_.name -eq $matter.assigned_law_firm_1 } | Select-Object -First 1      # this rule set only
    $matterId = $seed.matters."$($p.RuleSet)|$($p.Id)"
    $firmId = $seed.firms.($firm.key)
    if (-not $matterId -or -not $firmId) { throw "$($p.Id): not seeded (matter $matterId, firm $firmId)." }
    $url = "$BffUrl/api/finance/matters/$matterId/invoice-submissions?firmId=$firmId"
    if ($PSCmdlet.ShouldProcess($p.Id, "POST $url")) {
        $form = @{ file = Get-Item (Join-Path $PdfFolder "$($p.Id).pdf"); firmInvoiceNumber = $gt.header.firm_invoice_number; poNumber = "$($gt.header.po_number)" }
        $res = Invoke-RestMethod -Method POST -Uri $url -Headers @{ Authorization = "Bearer $token" } -Form $form
        $run.items[$p.Id] = [ordered]@{ ruleSet = $p.RuleSet; clean = $p.Clean; submissionId = $res.submissionId; statusUrl = $res.statusUrl; firmKey = $firm.key }
    }
}

# Wait for terminal statuses (status route from 044/049); counts only are printed.
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$pending = @($run.items.Keys)
while ($pending.Count -gt 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $PollSeconds
    $still = @()
    foreach ($id in $pending) {
        $st = Invoke-RestMethod -Method GET -Uri ("$BffUrl" + $run.items[$id].statusUrl) -Headers @{ Authorization = "Bearer $token" }
        $run.items[$id].status = "$($st.status)"
        $run.items[$id].invoiceId = "$($st.invoiceId)"
        if ("$($st.status)" -in 'Succeeded', 'Failed', 'Rejected', 'Blocked', 'Completed') { continue }
        $still += $id
    }
    $pending = $still
    Write-Host "  waiting: $($pending.Count) not terminal"
}
New-Item -ItemType Directory -Force -Path (Split-Path $RunManifestPath) | Out-Null
$run | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $RunManifestPath
Write-Host "Run manifest: $RunManifestPath ($($run.items.Count) submitted, $($pending.Count) not terminal)"
