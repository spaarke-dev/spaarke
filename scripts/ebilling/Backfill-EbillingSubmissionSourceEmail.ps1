<#
.SYNOPSIS
    eBilling R1 task 095a - backfill sprk_invoicesubmission.sprk_sourceemaildocument for from-email submissions written before the column existed.

.DESCRIPTION
    GET /api/finance/invoice-submissions?internetMessageId=... finds a submission through this column, so a from-email submission created
    before the column existed (dev only in R1) is "not found" until it carries the link. This script sets it, from the SAME SOURCE the runtime
    write uses (SubmitInvoiceFromEmailOperation writes the id the stage recorded as the Stored Files' source document), and from nothing else:

      A  the staged blob's metadata: container invoice-files, blobs whose metadata "submissionid" is the submission and that carry
         "sourcedocument" (what StoredFileService.ReadStagedSourceAsync / ReadAttemptSourceAsync reads). Needs -StorageAccountName and a
         signed-in az (read only: `az storage blob list --include m`).
      B  the committed Stored Files of the submission's invoice: sprk_storedfile rows whose sprk_invoice is the submission's invoice and whose
         sprk_sourcedocument is set (the same id, written at the commit).

    DECISION per candidate row (a from-email submission: Internal channel, no sprk_document of its own, sprk_sourceemaildocument empty):
      set        the sources A and B together name EXACTLY ONE .eml, and that sprk_document exists.
      skipped    the sources name more than one .eml (ambiguous), or the one named no longer exists. Nothing is written.
      unmatched  neither source names an .eml (an upload-shaped stage, a purged blob and no committed file, or a row that never staged).
                 The row stays empty.
    It NEVER guesses: not from the Message-ID, not from a time window, not from the communication, not from the sender. A row with no match
    stays empty and the by-email route answers "not found" for it, as it did before.

    It writes ONLY sprk_sourceemaildocument (a PATCH with If-Match '*', so it never creates a row). It never overwrites a link already there
    (those rows are not candidates), never touches any other column, and publishes nothing.

    -WhatIf is READ ONLY (it reads Dataverse and the blob metadata) and prints every row as WOULD SET, skipped or unmatched with the source
    that decided it, then a summary. Run it first. A real run asks for confirmation (ConfirmImpact High) and needs the owner's OK: it writes
    live rows. This script is NOT run by the task that adds it.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER StorageAccountName
    The Finance file-store storage account (source A). Omit it to use the committed Stored Files only (source B); the summary says so.

.PARAMETER SubscriptionId
    Optional subscription passed to every az call (the script never runs 'az account set').

.PARAMETER Container
    The container holding the staged invoice files. Default invoice-files.

.EXAMPLE
    pwsh scripts/ebilling/Backfill-EbillingSubmissionSourceEmail.ps1 -StorageAccountName <account> -WhatIf
    pwsh scripts/ebilling/Backfill-EbillingSubmissionSourceEmail.ps1 -StorageAccountName <account>

.NOTES
    Project: ebilling-r1, task 095a. Dev only in R1. Needs the column (Deploy-EbillingSubmissionSourceEmailSchema.ps1 reports it present).
    Exit codes: 0 = done (including nothing to do), 1 = error.
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$StorageAccountName = '',
    [string]$SubscriptionId = '',
    [string]$Container = 'invoice-files'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$InternalChannel = 100000001   # InvoiceSubmissionChannel.Internal

$null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun -Entity 'sprk_invoicesubmission'

function Get-AllPages([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = $Path
    while ($next) {
        $r = Invoke-Dv -Path $next
        foreach ($v in @($r.value)) { $rows.Add($v) }
        $next = $r.'@odata.nextLink'
    }
    return $rows
}

# Source A: submissionId -> the distinct "sourcedocument" values of its staged blobs (read only).
function Get-StagedSources {
    $map = @{}
    if (-not $StorageAccountName) { return $map }
    $azArgs = @('storage', 'blob', 'list', '--account-name', $StorageAccountName, '--container-name', $Container, '--include', 'm',
        '--auth-mode', 'login', '--num-results', '*', '--query', '[].metadata', '-o', 'json')
    if ($SubscriptionId) { $azArgs += @('--subscription', $SubscriptionId) }
    $json = & az @azArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az storage blob list failed (is az signed in, and does the operator hold a blob-read role on $Container?): $json" }
    foreach ($m in @(($json | Out-String | ConvertFrom-Json))) {
        if ($null -eq $m) { continue }
        $submission = [string]$m.submissionid
        $document = [string]$m.sourcedocument
        $parsed = [guid]::Empty
        if (-not [guid]::TryParse($submission, [ref]$parsed) -or -not [guid]::TryParse($document, [ref]$parsed)) { continue }
        $key = ([guid]$submission).ToString('D')
        if (-not $map.ContainsKey($key)) { $map[$key] = [System.Collections.Generic.HashSet[string]]::new() }
        [void]$map[$key].Add(([guid]$document).ToString('D'))
    }
    return $map
}

# Source B: the distinct sprk_sourcedocument values of the Stored Files committed under one invoice (read only).
function Get-CommittedSources([string]$InvoiceId) {
    $set = [System.Collections.Generic.HashSet[string]]::new()
    if (-not $InvoiceId) { return $set }
    $files = Get-AllPages "sprk_storedfiles?`$select=_sprk_sourcedocument_value&`$filter=_sprk_invoice_value eq $InvoiceId and _sprk_sourcedocument_value ne null"
    foreach ($f in $files) { [void]$set.Add(([guid]$f._sprk_sourcedocument_value).ToString('D')) }
    return $set
}

Write-Host ""
Write-Host "eBilling task 095a - backfill sprk_invoicesubmission.sprk_sourceemaildocument" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if ($DryRun) { 'WHATIF (read only)' } else { 'APPLY' })   Staged-blob source: $(if ($StorageAccountName) { "$StorageAccountName/$Container" } else { 'not used (no -StorageAccountName)' })"
Write-Host ""

$set = 0; $skipped = 0; $unmatched = 0
try {
    if (-not (Get-Attr 'sprk_invoicesubmission' 'sprk_sourceemaildocument')) {
        throw "sprk_invoicesubmission.sprk_sourceemaildocument does not exist in $EnvironmentUrl (run Deploy-EbillingSubmissionSourceEmailSchema.ps1 first)."
    }

    $staged = Get-StagedSources
    Write-Host ("Staged blobs read: {0} submission(s) with a recorded source" -f $staged.Count)

    $candidates = Get-AllPages ("sprk_invoicesubmissions?`$select=sprk_invoicesubmissionid,sprk_trackingnumber,_sprk_invoice_value" +
        "&`$filter=_sprk_sourceemaildocument_value eq null and _sprk_document_value eq null and sprk_channel eq $InternalChannel&`$orderby=createdon asc")
    Write-Host ("Candidates (Internal, no document of its own, no link yet): {0}" -f $candidates.Count)
    Write-Host ""

    foreach ($row in $candidates) {
        $id = ([guid]$row.sprk_invoicesubmissionid).ToString('D')
        $label = "{0} ({1})" -f $row.sprk_trackingnumber, $id
        $sources = [System.Collections.Generic.HashSet[string]]::new()
        $from = [System.Collections.Generic.List[string]]::new()
        if ($staged.ContainsKey($id)) { foreach ($d in $staged[$id]) { [void]$sources.Add($d) }; $from.Add('staged blob') }
        $committed = Get-CommittedSources $row._sprk_invoice_value
        if ($committed.Count -gt 0) { foreach ($d in $committed) { [void]$sources.Add($d) }; $from.Add('committed stored file') }

        if ($sources.Count -eq 0) {
            $unmatched++
            Write-Host "  unmatched  $label - no staged or committed source names an .eml; left empty" -ForegroundColor Yellow
            continue
        }
        if ($sources.Count -gt 1) {
            $skipped++
            Write-Host "  skipped    $label - the source names $($sources.Count) different .eml documents ($($sources -join ', ')); left empty" -ForegroundColor Yellow
            continue
        }
        $eml = @($sources)[0]
        if (-not (Get-DvOrNull "sprk_documents($eml)?`$select=sprk_documentid" -Retries 2)) {
            $skipped++
            Write-Host "  skipped    $label - the source names .eml $eml, which no longer exists; left empty" -ForegroundColor Yellow
            continue
        }

        if ($DryRun) {
            $set++
            Write-Host "  WOULD SET  $label -> $eml (from $($from -join ' + '))" -ForegroundColor Cyan
            continue
        }
        if (-not $PSCmdlet.ShouldProcess("sprk_invoicesubmission $id", "set sprk_sourceemaildocument = $eml")) {
            $skipped++
            Write-Host "  skipped    $label - not confirmed; left empty" -ForegroundColor Yellow
            continue
        }
        Invoke-Dv -Path "sprk_invoicesubmissions($id)" -Method PATCH -ExtraHeaders @{ 'If-Match' = '*' } `
            -Body @{ 'sprk_SourceEmailDocument@odata.bind' = "/sprk_documents($eml)" } | Out-Null
        $set++
        Write-Host "  SET        $label -> $eml (from $($from -join ' + '))" -ForegroundColor Green
    }
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Rows already set stay (the script is re-runnable: a row with a link is no longer a candidate)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("{0} : {1}" -f $(if ($DryRun) { 'would set' } else { 'set' }), $set)
Write-Host ("skipped   : {0}" -f $skipped)
Write-Host ("unmatched : {0}" -f $unmatched)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
