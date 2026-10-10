<#
.SYNOPSIS
    Renders a customer's Spaarke Copilot agent package (spaarke-copilot-{customerId}-{version}.zip) from the
    CI-published template (T257).

.DESCRIPTION
    The package is what the customer's IT uploads in THEIR tenant (Microsoft 365 admin center → Agents → Upload custom
    agent). It differs per customer in four values (design note t257-copilot-agent-design.md §3):
      - manifest id         UUIDv5 of the customerId (stable across versions: an upload updates, never duplicates)
      - servers[0].url      the customer's BFF
      - OAuth scope         api://{customerBffAppId}/user_impersonation
      - auth.reference_id   the customer's auth config (registry sprk_copilotauthconfigid)
    The authorize/token URLs are Spaarke's tenant (Model 1).

    Three ways to supply the values:
      -CustomerId + -BffBaseUrl/-BffAppId/-AuthConfigId/-SpaarkeTenantId   explicit (post-Ready gate, from the run)
      -CustomerId + -AdminEnvironmentUrl                                   that customer's registry row
      -AllActive  + -AdminEnvironmentUrl                                   every active, Ready registry row that has an
                                                                           auth config id (the release loop)
    Registry reads are read-only GETs with the operator's own token (az account get-access-token). Nothing is written
    anywhere except the packages in -OutputFolder.

    Get the template from the provisioning-artifacts store first (read-only):
      az storage blob download --account-name <store> --container-name provisioning-artifacts --auth-mode login `
        --name copilot-agent-template-latest.json --file latest.json
      # then the blob named in latest.json → copilotAgentTemplate.blobName, and compare its SHA-256 with .sha256
    or pass -TemplateManifestPath latest.json and this script checks the SHA-256 for you.

.PARAMETER TemplatePath
    The template zip (copilot-agent-template-{version}.zip).

.PARAMETER TemplateManifestPath
    Optional: copilot-agent-template-latest.json. When given, the template's SHA-256 and version must match it.

.PARAMETER OutputFolder
    Where the packages are written. A file of the same name is replaced.

.EXAMPLE
    ./Render-CopilotAgentPackage.ps1 -TemplatePath ./copilot-agent-template-1.1.0.zip -OutputFolder ./out `
        -CustomerId acme -BffBaseUrl https://sprk-acme-prod-api.azurewebsites.net `
        -BffAppId <bffAppRegId> -AuthConfigId <reference id> -SpaarkeTenantId <Spaarke tenant id>

.EXAMPLE
    ./Render-CopilotAgentPackage.ps1 -TemplatePath ./copilot-agent-template-1.2.0.zip -OutputFolder ./out `
        -AllActive -AdminEnvironmentUrl https://spaarke-admin.crm.dynamics.com

.NOTES
    customer-provisioning-orchestration-r1 T257. Module: CopilotAgentPackage.psm1. Tests:
    tests/scripts/CopilotAgentPackage.Tests.ps1.
#>
#Requires -Version 7.3
[CmdletBinding(DefaultParameterSetName = 'Explicit')]
param(
    [Parameter(Mandatory)] [string]$TemplatePath,
    [string]$TemplateManifestPath,
    [Parameter(Mandatory)] [string]$OutputFolder,

    [Parameter(Mandatory, ParameterSetName = 'Explicit')]
    [Parameter(Mandatory, ParameterSetName = 'Registry')]
    [string]$CustomerId,

    [Parameter(Mandatory, ParameterSetName = 'Explicit')] [string]$BffBaseUrl,
    [Parameter(Mandatory, ParameterSetName = 'Explicit')] [string]$BffAppId,
    [Parameter(Mandatory, ParameterSetName = 'Explicit')] [string]$AuthConfigId,
    [Parameter(Mandatory, ParameterSetName = 'Explicit')] [string]$SpaarkeTenantId,

    [Parameter(Mandatory, ParameterSetName = 'Registry')]
    [Parameter(Mandatory, ParameterSetName = 'AllActive')]
    [string]$AdminEnvironmentUrl,

    [Parameter(Mandatory, ParameterSetName = 'AllActive')] [switch]$AllActive
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CopilotAgentPackage.psm1') -Force
$script:SkippedRows = 0

$template = Read-ZipEntries -Path $TemplatePath
$version = Get-CopilotAgentTemplateVersion -Template $template
if ($TemplateManifestPath) {
    $m = (Get-Content -LiteralPath $TemplateManifestPath -Raw | ConvertFrom-Json).copilotAgentTemplate
    $sha = (Get-FileHash -LiteralPath $TemplatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($m.sha256 -ne $sha) { throw "Template SHA-256 $sha does not match the store manifest's $($m.sha256)." }
    if ($m.version -ne $version) { throw "Template version $version does not match the store manifest's $($m.version)." }
}

function Get-RegistryRows([string]$Url, [string]$Filter) {
    $res = $Url.TrimEnd('/')
    $token = az account get-access-token --resource $res --query accessToken -o tsv
    if (-not $token) { throw "No Dataverse token for $res (az login)." }
    $select = 'sprk_customerid,sprk_appservicename,sprk_bffappid,sprk_copilotauthconfigid,sprk_tenantid'
    $uri = "$res/api/data/v9.2/sprk_dataverseenvironments?`$select=$select&`$filter=$([uri]::EscapeDataString($Filter))"
    $r = Invoke-RestMethod -Method GET -Uri $uri -Headers @{ Authorization = "Bearer $token"; Accept = 'application/json' }
    return @($r.value)
}

$inputs = switch ($PSCmdlet.ParameterSetName) {
    'Explicit' {
        [PSCustomObject]@{ CustomerId = $CustomerId; BffBaseUrl = $BffBaseUrl; BffAppId = $BffAppId; AuthConfigId = $AuthConfigId; SpaarkeTenantId = $SpaarkeTenantId }
    }
    'Registry' {
        if ($CustomerId -cnotmatch '^[a-z][a-z0-9]{2,7}$') { throw "CustomerId '$CustomerId' does not match the customerId standard." }
        $rows = Get-RegistryRows $AdminEnvironmentUrl "sprk_customerid eq '$CustomerId'"
        if ($rows.Count -ne 1) { throw "Expected one registry row with sprk_customerid '$CustomerId'; found $($rows.Count)." }
        ConvertFrom-CopilotAgentRegistryRow -Row $rows[0]
    }
    'AllActive' {
        # Active, Ready, Model 1, and the post-Ready gate has recorded an auth config (Get-CopilotAgentRegistryFilter).
        $rows = Get-RegistryRows $AdminEnvironmentUrl (Get-CopilotAgentRegistryFilter)
        if ($rows.Count -eq 0) { Write-Warning 'No active, Ready registry row has a Copilot auth config id.' }
        # One incomplete row (e.g. a stamp from before T257 with no sprk_bffappid) must not stop every other customer's
        # package: report it, render the rest, exit non-zero at the end.
        foreach ($row in $rows) {
            try { ConvertFrom-CopilotAgentRegistryRow -Row $row }
            catch { Write-Warning $_.Exception.Message; $script:SkippedRows++ }
        }
    }
}

$results = foreach ($i in @($inputs)) {
    $values = Get-CopilotAgentRenderValues -ManifestId (Get-CopilotAgentManifestId -CustomerId $i.CustomerId) `
        -BffBaseUrl $i.BffBaseUrl -BffAppId $i.BffAppId -AuthConfigId $i.AuthConfigId -SpaarkeTenantId $i.SpaarkeTenantId
    $r = Invoke-CopilotAgentRender -Template $template -Values $values `
        -OutputPath (Join-Path $OutputFolder "spaarke-copilot-$($i.CustomerId)-$version.zip")
    [PSCustomObject]@{
        CustomerId = $i.CustomerId
        Package    = $r.ZipPath
        Version    = $r.Version
        ManifestId = $r.ManifestId
        Sha256     = $r.Sha256
        BffBaseUrl = $values.SPAARKE_BFF_BASE_URL
        Scope      = $values.SPAARKE_BFF_SCOPE
    }
}
$results
if ($script:SkippedRows -gt 0) {
    Write-Error "$($script:SkippedRows) registry row(s) were skipped (see the warnings). Fix them (guide §7.12 step 7) and re-run."
    exit 1
}
