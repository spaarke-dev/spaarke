<#
.SYNOPSIS
    R2.2 one-off patch: adds `dueDate: "{{item.scheduledend}}"` to the
    itemNotification block of the "Create Notification" node in the two
    task-based Notification playbooks (Tasks Overdue + Tasks Due Soon).

.DESCRIPTION
    The full Deploy-Playbook.ps1 lint added by Insights Engine R2 (D-01,
    2026-06-02) requires `actionCode` on every node. The 7 Notification
    playbooks predate that lint and use the older `actionType` integer
    dispatch pattern — they cannot pass the lint without being rewritten.

    This targeted script bypasses the full deploy by patching ONLY the
    sprk_configjson field on the existing "Create Notification" nodes via
    Dataverse Web API. No other fields are touched. No new records created.

    Use only for this specific R2.2 hotfix (per user decision 2026-06-21).
    For all other playbook deploys, use Deploy-Playbook.ps1.

.PARAMETER DataverseUrl
    Target Dataverse environment URL.

.PARAMETER DryRun
    Preview the patch without writing to Dataverse.

.EXAMPLE
    .\Patch-NotificationPlaybookDueDate.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com" -DryRun
    .\Patch-NotificationPlaybookDueDate.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
#>
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

# Each tuple: playbook display-name + the node whose configjson gets patched.
$Targets = @(
    @{ PlaybookName = 'Tasks Overdue';  NodeName = 'Create Notification' },
    @{ PlaybookName = 'Tasks Due Soon'; NodeName = 'Create Notification' }
)

# CORRECTED 2026-06-22: was '{{item.scheduledend}}' (OOB task field).
# The DEPLOYED playbooks query `sprk_event`, which uses `sprk_duedate` for the
# due date. The earlier patch was based on stale repo JSON that referenced the
# OOB task entity. Using sprk_duedate makes the template render the actual
# due-date value into customData.dueDate on each created appnotification.
$DueDateTemplate = '{{item.sprk_duedate}}'

$ApiBase = "$DataverseUrl/api/data/v9.2"

Write-Host '======================================================'
Write-Host 'R2.2 Patch: dueDate plumbing for Notification playbooks'
Write-Host '======================================================'
Write-Host "Target environment : $DataverseUrl"
Write-Host "Mode               : $(if ($DryRun) { 'DRY RUN' } else { 'LIVE' })"
Write-Host ''

# 1) Acquire access token
Write-Host '[1/4] Acquiring access token via Azure CLI...'
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error 'Failed to acquire Dataverse access token.'
    exit 1
}
$headers = @{
    'Authorization'    = "Bearer $token"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}
Write-Host '       Token acquired.'

$patched = 0
$skipped = 0
$failed  = 0

foreach ($t in $Targets) {
    Write-Host ''
    Write-Host "[2/4] Patching: $($t.PlaybookName) / $($t.NodeName)"

    # 2a) Resolve playbook id
    $encName = [System.Web.HttpUtility]::UrlEncode($t.PlaybookName)
    $playbookFilter = "`$filter=sprk_name eq '$($t.PlaybookName -replace "'", "''")'&`$select=sprk_analysisplaybookid"
    $playbookUrl = "$ApiBase/sprk_analysisplaybooks?$playbookFilter"

    try {
        $playbookResp = Invoke-RestMethod -Uri $playbookUrl -Headers $headers -Method Get
    } catch {
        Write-Host "       FAIL: playbook query error: $($_.Exception.Message)" -ForegroundColor Red
        $failed++
        continue
    }
    if ($playbookResp.value.Count -eq 0) {
        Write-Host "       FAIL: playbook '$($t.PlaybookName)' not found." -ForegroundColor Red
        $failed++
        continue
    }
    $playbookId = $playbookResp.value[0].sprk_analysisplaybookid
    Write-Host "       Playbook id: $playbookId"

    # 2b) Resolve node id + read configjson
    # Lookup nav-property is `_sprk_playbookid_value` (per Deploy-Playbook.ps1:535);
    # GUIDs in OData filters must be single-quoted for sprk_* lookups.
    $nodeFilter = "`$filter=_sprk_playbookid_value eq '$playbookId' and sprk_name eq '$($t.NodeName -replace "'", "''")'&`$select=sprk_playbooknodeid,sprk_configjson"
    $nodeUrl = "$ApiBase/sprk_playbooknodes?$nodeFilter"

    try {
        $nodeResp = Invoke-RestMethod -Uri $nodeUrl -Headers $headers -Method Get
    } catch {
        Write-Host "       FAIL: node query error: $($_.Exception.Message)" -ForegroundColor Red
        $failed++
        continue
    }
    if ($nodeResp.value.Count -eq 0) {
        Write-Host "       FAIL: node '$($t.NodeName)' not found under playbook." -ForegroundColor Red
        $failed++
        continue
    }
    $nodeId = $nodeResp.value[0].sprk_playbooknodeid
    $rawConfig = $nodeResp.value[0].sprk_configjson
    Write-Host "       Node id    : $nodeId"

    if ([string]::IsNullOrWhiteSpace($rawConfig)) {
        Write-Host '       FAIL: node has empty sprk_configjson.' -ForegroundColor Red
        $failed++
        continue
    }

    # 2c) Parse, mutate, serialize
    try {
        $config = $rawConfig | ConvertFrom-Json -AsHashtable -Depth 50
    } catch {
        Write-Host "       FAIL: configjson parse error: $($_.Exception.Message)" -ForegroundColor Red
        $failed++
        continue
    }

    if (-not $config.ContainsKey('itemNotification')) {
        Write-Host "       SKIP: node has no itemNotification block (not iterate-items mode)." -ForegroundColor Yellow
        $skipped++
        continue
    }

    $item = $config['itemNotification']
    $current = if ($item.ContainsKey('dueDate')) { $item['dueDate'] } else { $null }

    if ($current -eq $DueDateTemplate) {
        Write-Host "       SKIP: dueDate already set to '$DueDateTemplate'." -ForegroundColor Yellow
        $skipped++
        continue
    }

    $item['dueDate'] = $DueDateTemplate
    $config['itemNotification'] = $item
    $newConfig = $config | ConvertTo-Json -Depth 50 -Compress

    Write-Host "       Old dueDate: $(if ($null -eq $current) { '(missing)' } else { $current })"
    Write-Host "       New dueDate: $DueDateTemplate"

    if ($DryRun) {
        Write-Host '       DRY RUN — would PATCH sprk_playbooknodes record now.' -ForegroundColor Cyan
        continue
    }

    # 2d) PATCH the node — only sprk_configjson
    $patchUrl = "$ApiBase/sprk_playbooknodes($nodeId)"
    $patchBody = @{ sprk_configjson = $newConfig } | ConvertTo-Json -Depth 10

    try {
        Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method Patch -Body $patchBody | Out-Null
        Write-Host '       PATCH succeeded.' -ForegroundColor Green
        $patched++
    } catch {
        Write-Host "       FAIL: PATCH error: $($_.Exception.Message)" -ForegroundColor Red
        $failed++
    }
}

Write-Host ''
Write-Host '[3/4] Summary'
Write-Host "       Patched : $patched"
Write-Host "       Skipped : $skipped"
Write-Host "       Failed  : $failed"

Write-Host ''
Write-Host '[4/4] Verification hint'
Write-Host '       Trigger a playbook tick (or wait for next scheduled run) and inspect'
Write-Host '       a newly-created appnotification record to confirm customData.dueDate'
Write-Host '       is now populated for task notifications.'
Write-Host ''

if ($failed -gt 0) { exit 1 } else { exit 0 }
