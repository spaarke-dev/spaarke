<#
.SYNOPSIS
    ebilling-r1 ISS-022 / #2844: queue the normal invoice re-index job for every invoice changed since a date. Dry run by default.
.DESCRIPTION
    From task 094 (merged 2026-10-27) until the #2844 fix (c88ed7105a), every invoice re-index failed: the job asked Dataverse for a column the
    invoice no longer has. An invoice changed in that window can have a stale row in the invoice search index. The index catches up only when
    the invoice next changes, so this sweep queues one re-index per invoice.

    It reuses the BFF's own path. Each message is the job InvoiceIndexingJobHandler.BuildJob makes (job type InvoiceIndexing, payload
    {InvoiceId, Change}), sent to the BFF's job queue in the format JobSubmissionService sends (camelCase body, MessageId = the idempotency key,
    Subject = the job type, the JobType / Attempt / CreatedAt properties). The BFF's job processor then re-indexes each invoice exactly as after
    any other change. Nothing is written to Dataverse or to the search index by this script.

    Dry run (the default): GET requests only (the invoice ids, paged), then prints the COUNT. No id or name is printed.
    -Apply: also sends the messages. Run it only AFTER the dev BFF carries the #2844 fix, or every job fails again. The caller needs the
    "Azure Service Bus Data Sender" role on the namespace; a 401 or 403 on the first send stops the run with nothing else sent.
    Re-running sends the same MessageIds; dev's sdap-jobs has no duplicate detection (checked 2026-11-15), so a re-run queues again. That is
    harmless: a re-index rewrites the same search row.
.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required.
.PARAMETER Since
    Invoices modified on or after this UTC date are queued. Default 2026-10-27 (task 094's merge).
.PARAMETER ServiceBusNamespace
    The Service Bus namespace name (no domain). Default spaarke-servicebus-dev (dev).
.PARAMETER QueueName
    The BFF's job queue (ServiceBus:QueueName). Default sdap-jobs.
.PARAMETER Change
    The idempotency suffix (the job's Change). Default sweep-2844.
.PARAMETER Apply
    Send the messages. Without it the script only counts.
.EXAMPLE
    pwsh scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
.EXAMPLE
    pwsh scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Apply
#>
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [datetime]$Since = '2026-10-27',
    [string]$ServiceBusNamespace = 'spaarke-servicebus-dev',
    [string]$QueueName = 'sdap-jobs',
    [string]$Change = 'sweep-2844',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

# The BFF's names (InvoiceIndexingJobHandler.JobTypeName and BuildJob).
$JobType = 'InvoiceIndexing'
$MaxAttempts = 3

function Get-SinceFilter([datetime]$Since) {
    # Dataverse OData compares modifiedon in UTC.
    $utc = [datetime]::SpecifyKind($Since.Date, [System.DateTimeKind]::Utc)
    return "modifiedon ge $($utc.ToString('yyyy-MM-ddTHH:mm:ssZ'))"
}

function New-InvoiceIndexJobMessage([Guid]$InvoiceId, [string]$Change, [datetimeoffset]$CreatedAt, [Guid]$JobId, [string]$CorrelationId) {
    if ($InvoiceId -eq [Guid]::Empty) { throw 'An invoice id is required.' }
    if ([string]::IsNullOrWhiteSpace($Change)) { throw 'A change is required.' }

    $key = "invoice-index-$($InvoiceId.ToString('N'))-$Change"
    $created = $CreatedAt.ToString('o')
    # The body JobSubmissionService sends: JobContract in camelCase; the payload as BuildJob serializes it (PascalCase, no naming policy).
    $body = [ordered]@{
        jobId          = $JobId.ToString('D')
        jobType        = $JobType
        subjectId      = $InvoiceId.ToString('D')
        correlationId  = $CorrelationId
        idempotencyKey = $key
        attempt        = 1
        maxAttempts    = $MaxAttempts
        payload        = [ordered]@{ InvoiceId = $InvoiceId.ToString('D'); Change = $Change }
        createdAt      = $created
    }
    return [pscustomobject]@{
        Body             = ($body | ConvertTo-Json -Compress -Depth 5)
        BrokerProperties = ([ordered]@{ MessageId = $key; CorrelationId = $CorrelationId; Label = $JobType } | ConvertTo-Json -Compress)
        Properties       = [ordered]@{ JobType = $JobType; Attempt = '1'; CreatedAt = $created }
    }
}

function Get-InvoiceIdList([string]$ApiBase, [hashtable]$Headers, [string]$Filter) {
    $ids = [System.Collections.Generic.List[Guid]]::new()
    $url = "$ApiBase/sprk_invoices?`$select=sprk_invoiceid&`$filter=$([uri]::EscapeDataString($Filter))"
    while ($url) {
        $page = Invoke-RestMethod -Method Get -Uri $url -Headers $Headers
        foreach ($row in $page.value) { $ids.Add([Guid]$row.sprk_invoiceid) }
        $url = $page.'@odata.nextLink'
    }
    return , $ids
}

function Send-QueueMessage([string]$Uri, [string]$Token, $Message) {
    $headers = @{
        Authorization      = "Bearer $Token"
        BrokerProperties   = $Message.BrokerProperties
    }
    foreach ($p in $Message.Properties.GetEnumerator()) { $headers[$p.Key] = $p.Value }
    Invoke-RestMethod -Method Post -Uri $Uri -Headers $headers -ContentType 'application/json' -Body $Message.Body | Out-Null
}

# ── Main ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced: functions only

if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"

$dvToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $dvToken) { throw "Could not get a token for $DataverseUrl (az login?)." }
$dvHeaders = @{
    Authorization      = "Bearer $dvToken"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Consistency        = 'Strong'
    Prefer             = 'odata.maxpagesize=5000'
}

$filter = Get-SinceFilter $Since
$ids = Get-InvoiceIdList $ApiBase $dvHeaders $filter
Write-Host "Invoices modified on or after $($Since.ToString('yyyy-MM-dd')) (UTC): $($ids.Count)"

if (-not $Apply) {
    Write-Host "Dry run: nothing sent. Re-run with -Apply, after the dev BFF carries the #2844 fix, to queue $($ids.Count) re-index job(s) on $ServiceBusNamespace/$QueueName."
    return
}

if ($ids.Count -eq 0) { Write-Host 'Nothing to queue.'; return }

$sbToken = az account get-access-token --resource 'https://servicebus.azure.net' --query accessToken -o tsv
if (-not $sbToken) { throw 'Could not get a Service Bus token (az login?).' }
$queueUri = "https://$ServiceBusNamespace.servicebus.windows.net/$QueueName/messages"
$correlation = "ebilling-iss022-sweep-$([datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"

$sent = 0; $failed = 0
foreach ($id in $ids) {
    $message = New-InvoiceIndexJobMessage -InvoiceId $id -Change $Change -CreatedAt ([datetimeoffset]::UtcNow) -JobId ([Guid]::NewGuid()) -CorrelationId $correlation
    try {
        Send-QueueMessage $queueUri $sbToken $message
        $sent++
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($sent -eq 0 -and $status -in 401, 403) {
            throw "The first send was refused ($status): the signed-in identity needs 'Azure Service Bus Data Sender' on $ServiceBusNamespace. Nothing was sent."
        }
        $failed++
    }
}

Write-Host "Queued: $sent. Failed: $failed. Correlation id: $correlation"
if ($failed -gt 0) { exit 1 }
