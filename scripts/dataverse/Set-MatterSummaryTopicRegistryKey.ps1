<#
.SYNOPSIS
    Aligns the matter-summary sprk_aitopicregistry row's sprk_playbookname with the Binding's capability code.

.DESCRIPTION
    spaarkeai-mcp-bff-api-r2 task 012 (FR-10 prerequisite). On dev the matter-summary registry row
    (matter-summary/single, sprk_targetfield sprk_recordsummary, TTL 60 min) already exists, but its
    sprk_playbookname is "chat-summarize". TopicRegistryTtlLookup keys its snapshot on sprk_playbookname and
    resolves a playbook id through the Binding's ConsumerCode ("matter-summary"), so the TTL is found only when
    the two agree. This script sets sprk_playbookname = "matter-summary" on the row whose sprk_topicname is
    "matter-summary". It changes nothing else (the persister reads sprk_topicname, not sprk_playbookname).

    Idempotent: a row already at the wanted value reports "no change". Owner-gated: run only with the owner's go.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER WhatIf
    Print what would change; write nothing.

.EXAMPLE
    pwsh scripts/dataverse/Set-MatterSummaryTopicRegistryKey.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$BaseUrl = "$EnvironmentUrl/api/data/v9.2"
$Topic = "matter-summary"
$WantedKey = "matter-summary"

$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv).Trim()
if ($LASTEXITCODE -ne 0 -or -not $token) { throw "az account get-access-token failed (run 'az login')" }
$headers = @{
    Authorization = "Bearer $token"; "OData-MaxVersion" = "4.0"; "OData-Version" = "4.0"
    Accept = "application/json"; "Content-Type" = "application/json; charset=utf-8"
}

$uri = "$BaseUrl/sprk_aitopicregistries?`$select=sprk_aitopicregistryid,sprk_name,sprk_playbookname,sprk_targetfield,sprk_cachettlminutes" +
    "&`$filter=sprk_topicname eq '$Topic' and statecode eq 0"
$rows = @((Invoke-RestMethod -Headers $headers -Uri $uri).value)
if ($rows.Count -ne 1) { throw "Expected exactly one active registry row for topic '$Topic', found $($rows.Count). Resolve by hand." }
$row = $rows[0]
Write-Host "Row $($row.sprk_name) [$($row.sprk_aitopicregistryid)] target=$($row.sprk_targetfield) ttl=$($row.sprk_cachettlminutes) playbookname='$($row.sprk_playbookname)'" -ForegroundColor Cyan

if ($row.sprk_playbookname -eq $WantedKey) { Write-Host "no change (sprk_playbookname already '$WantedKey')" -ForegroundColor Yellow; return }
if ($WhatIf) { Write-Host "would set sprk_playbookname '$($row.sprk_playbookname)' -> '$WantedKey'" -ForegroundColor Gray; return }

Invoke-RestMethod -Method Patch -Headers $headers -Uri "$BaseUrl/sprk_aitopicregistries($($row.sprk_aitopicregistryid))" `
    -Body (@{ sprk_playbookname = $WantedKey } | ConvertTo-Json) | Out-Null
Write-Host "set sprk_playbookname -> '$WantedKey'" -ForegroundColor Green
