# Register the BFF managed identity (mi-bff-api-dev) as a GUEST app on the SPE
# container type so app-only file writes (email .eml archive + attachment
# materialization via GraphClientFactory.ForApp()) stop returning 403 Access denied.
#
# ROOT CAUSE (UAT email-r4, 2026-07-19): the archive .eml upload runs as the BFF
# managed identity (UploadSessionManager -> GraphClientFactory.ForApp() ->
# mi-bff-api-dev / 5967251e). That identity is NOT registered on SPE container type
# 8a6ce34c, so its Graph PUT to /drives/{container}/root:/.../file.eml:/content -> 403.
# Only the OWNING app (170c98e1) has implicit access. Per-container permission grants
# are user-scoped (they reject application grantees), so the MI must be registered at
# the CONTAINER TYPE level as a guest application. That is what this script does.
#
# PREREQUISITE (SharePoint-admin, one-time): the OWNING app (170c98e1) must have the
# Microsoft Graph application permission FileStorageContainer.Selected AND the
# SharePoint "Container.Selected" application permission, both admin-consented. If this
# script returns 401 "invalid token" or 403, that consent is missing — grant it in
# Azure Portal > App Registrations > (owning app) > API Permissions, then admin-consent.
#
# A PUT to /applicationPermissions REPLACES the whole grant list, so this script GETs
# the current list first and MERGES the MI in (never clobbers existing registrations).

param(
    [string]$TenantId          = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$ContainerTypeId   = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$OwningAppId       = "170c98e1-d486-4355-bcbe-170454e0207c",
    [string]$MiAppId           = "5967251e-171c-46fe-a6c2-ef843c90309d",   # mi-bff-api-dev
    [string]$SharePointDomain  = "spaarke.sharepoint.com",
    [string]$KeyVaultName      = "spaarke-spekvcert",
    [string]$OwningAppSecretName = "spe-owning-app-secret",
    # SPE container-type app-permission roles for the MI. "full" mirrors the owning app;
    # tighten later to @("readContent","writeContent","create","delete") if desired.
    [string[]]$MiAppOnlyRoles  = @("full"),
    [string[]]$MiDelegatedRoles = @("full")
)

$ErrorActionPreference = "Stop"
Write-Host "Registering MI $MiAppId as guest app on container type $ContainerTypeId" -ForegroundColor Cyan

# 1) Owning-app SharePoint token (client credentials)
$secret = az keyvault secret show --vault-name $KeyVaultName --name $OwningAppSecretName --query value -o tsv
$tok = (Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -Body @{
    client_id = $OwningAppId; client_secret = $secret
    scope = "https://$SharePointDomain/.default"; grant_type = "client_credentials"
}).access_token
$headers = @{ Authorization = "Bearer $tok"; "Content-Type" = "application/json"; Accept = "application/json" }
$uri = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

# 2) GET current registrations (so we merge, not clobber). If GET is unavailable,
#    fall back to the known-minimal set [owning app: full].
$existing = @()
try {
    $cur = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
    if ($cur.value) { $existing = @($cur.value | ForEach-Object { @{ appId = $_.appId; delegated = $_.delegated; appOnly = $_.appOnly } }) }
    Write-Host "Current registrations: $(($existing | ForEach-Object { $_.appId }) -join ', ')" -ForegroundColor Gray
} catch {
    Write-Host "GET failed ($($_.Exception.Message)); seeding with owning app only." -ForegroundColor Yellow
    $existing = @(@{ appId = $OwningAppId; delegated = @("full"); appOnly = @("full") })
}

# Ensure owning app present + full, then upsert the MI
if (-not ($existing | Where-Object { $_.appId -eq $OwningAppId })) {
    $existing += @{ appId = $OwningAppId; delegated = @("full"); appOnly = @("full") }
}
$existing = @($existing | Where-Object { $_.appId -ne $MiAppId })   # drop stale MI entry if any
$existing += @{ appId = $MiAppId; delegated = $MiDelegatedRoles; appOnly = $MiAppOnlyRoles }

# 3) PUT the merged list
$body = @{ value = $existing } | ConvertTo-Json -Depth 5
$resp = Invoke-RestMethod -Method Put -Uri $uri -Headers $headers -Body $body
Write-Host "REGISTRATION SUCCESSFUL. Apps now registered:" -ForegroundColor Green
$resp.value | ForEach-Object { Write-Host ("  {0}  delegated=[{1}] appOnly=[{2}]" -f $_.appId, ($_.delegated -join ','), ($_.appOnly -join ',')) }

Write-Host ""
Write-Host "NEXT: restart the BFF to clear its MSAL/app-client cache, then retest archive:" -ForegroundColor Cyan
Write-Host "  az webapp restart -g rg-spaarke-dev -n spaarke-bff-dev" -ForegroundColor Gray
Write-Host "  (then click 'Save to SharePoint' in the Actions PCF, or POST /api/communications/{id}/archive)" -ForegroundColor Gray
