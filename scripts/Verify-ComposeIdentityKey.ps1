<#
.SYNOPSIS
    Post-deploy health check for the Compose save-identity alternate key.

.DESCRIPTION
    Prod-safety guard (#4, post-spaarkeai-compose-r7). The FR-07(d) atomic upsert
    (ComposeService.PromoteIfEphemeralAsync) keys on the `sprk_graphitemid_uk`
    alternate key of `sprk_document`. That key can ONLY be `Active` when the table's
    `sprk_graphitemid` values are unique — duplicate rows leave it `Failed`, and every
    Compose save then 500s (surfaced to users as a save error).

    This script asserts the key is `Active` in the target environment so the condition
    is caught at DEPLOY time (in a runbook / CI step) instead of by a user's second save.
    Run it after any Compose/BFF deploy, and after a solution import that (re)creates the key.

    Exit codes: 0 = Active (healthy); 1 = not Active (blocks Compose saves); 2 = check failed.

.PARAMETER DataverseUrl
    Target Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Verify-ComposeIdentityKey.ps1
    # Check dev

.EXAMPLE
    .\Verify-ComposeIdentityKey.ps1 -DataverseUrl 'https://spaarke-prod.crm.dynamics.com'
    # Check prod before declaring a deploy healthy
#>
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'

Write-Host '========================================='
Write-Host 'Compose save-identity key health check'
Write-Host "Target: $DataverseUrl"
Write-Host '========================================='

try {
    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if ([string]::IsNullOrEmpty($token)) { Write-Error 'Failed to get access token. Run: az login'; exit 2 }

    $uri = "$DataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Keys" +
           "?`$select=LogicalName,EntityKeyIndexStatus&`$filter=LogicalName eq 'sprk_graphitemid_uk'"
    $headers = @{ Authorization = "Bearer $token"; Accept = 'application/json' }
    $resp = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get

    $key = $resp.value | Select-Object -First 1
    if ($null -eq $key) {
        Write-Warning "sprk_graphitemid_uk alternate key NOT FOUND on sprk_document. Solution import may be incomplete."
        exit 1
    }

    $status = $key.EntityKeyIndexStatus
    if ($status -eq 'Active') {
        Write-Host "  sprk_graphitemid_uk = Active" -ForegroundColor Green
        Write-Host "OK - Compose save-identity upsert is functional." -ForegroundColor Green
        exit 0
    }

    Write-Warning "sprk_graphitemid_uk = $status (NOT Active) - Compose saves will FAIL (500)."
    Write-Host "Remediation:" -ForegroundColor Yellow
    Write-Host "  1. Dedupe sprk_document by sprk_graphitemid (a unique key cannot build over duplicates)." -ForegroundColor Yellow
    Write-Host "  2. Reactivate the key: POST $DataverseUrl/api/data/v9.2/ReactivateEntityKey" -ForegroundColor Yellow
    Write-Host "     body { EntityLogicalName: 'sprk_document', EntityKeyLogicalName: 'sprk_graphitemid_uk' }" -ForegroundColor Yellow
    Write-Host "  3. Re-run this script until it reports Active (rebuild is async)." -ForegroundColor Yellow
    exit 1
}
catch {
    Write-Warning "Key health check failed to run: $($_.Exception.Message)"
    exit 2
}
