# Register a guest application (e.g. the BFF managed identity) on a SharePoint
# Embedded container TYPE, so it can do app-only file writes (archive .eml,
# attachment materialization) without 403 Access denied.
#
# WHY (UAT email-r4, 2026-07-19): the archive uploads as the BFF managed identity
# (mi-bff-api-dev / 5967251e) via GraphClientFactory.ForApp(). That identity was NOT
# in the container type's applicationPermissionGrants — only the owner app (170c98e1)
# and 1e40baad were — so its Graph PUT to write a file returned 403. Adding the MI as
# a grant fixes it. Confirmed working: after this + a BFF restart + a few minutes of
# SPE propagation, POST /api/communications/{id}/archive returns 200.
#
# WORKING METHOD (this tenant): the legacy `_api/v2.1/storageContainerTypes/{ct}/
# applicationPermissions` endpoint returns apiNotFound here. Use the Microsoft Graph
# beta endpoint instead:
#   PUT /beta/storage/fileStorage/containerTypeRegistrations/{ct}/applicationPermissionGrants/{appId}
# authenticated app-only as the container-type OWNER app using a CERTIFICATE
# (client-secret app-only tokens are rejected as "invalid token" by this tenant).
#
# PREREQS:
#  - The guest app (the MI) already holds the Graph app-role FileStorageContainer.Selected.
#  - The OWNER app (170c98e1) has a VALID certificate (its original expired 2026-03-14;
#    renew via `az ad app credential reset --id <owner> --cert @cert.cer --append`) that
#    is present in the CurrentUser cert store on this machine (thumbprint below).

param(
    [string]$TenantId        = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$OwnerAppId      = "170c98e1-d486-4355-bcbe-170454e0207c",   # container-type owner (auth as this)
    [string]$GuestAppId      = "5967251e-171c-46fe-a6c2-ef843c90309d",   # mi-bff-api-dev (the grantee)
    [Parameter(Mandatory=$true)]
    [string]$OwnerCertThumbprint,                                        # valid cert on the owner app, in Cert:\CurrentUser\My
    [string[]]$Permissions   = @("full")                                 # app-only perms for the guest app
)
$ErrorActionPreference = "Stop"
function B64Url([byte[]]$b){ [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_') }

# --- cert-based app-only Graph token for the OWNER app (JWT client assertion, RS256) ---
$cert = Get-Item "Cert:\CurrentUser\My\$OwnerCertThumbprint"
$tokUri = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$hdr = (@{ alg="RS256"; typ="JWT"; x5t=(B64Url ($cert.GetCertHash())) } | ConvertTo-Json -Compress)
$pl  = (@{ aud=$tokUri; iss=$OwnerAppId; sub=$OwnerAppId; jti=[guid]::NewGuid().ToString(); nbf=$now; exp=($now+600); iat=$now } | ConvertTo-Json -Compress)
$unsigned = (B64Url ([Text.Encoding]::UTF8.GetBytes($hdr))) + "." + (B64Url ([Text.Encoding]::UTF8.GetBytes($pl)))
$rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
$sig = $rsa.SignData([Text.Encoding]::UTF8.GetBytes($unsigned), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$assertion = "$unsigned." + (B64Url $sig)
$tok = (Invoke-RestMethod -Method Post -Uri $tokUri -Body @{
    client_id=$OwnerAppId; scope="https://graph.microsoft.com/.default"
    client_assertion_type="urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
    client_assertion=$assertion; grant_type="client_credentials"
}).access_token
$hdrs = @{ Authorization="Bearer $tok"; "Content-Type"="application/json" }
$base = "https://graph.microsoft.com/beta/storage/fileStorage/containerTypeRegistrations/$ContainerTypeId"

# --- upsert the single guest-app grant by appId (collection PATCH/POST are rejected; per-item PUT works) ---
Write-Host "Granting $GuestAppId [$($Permissions -join ',')] on container type $ContainerTypeId ..."
$body = @{ delegatedPermissions=$Permissions; applicationPermissions=$Permissions } | ConvertTo-Json
Invoke-RestMethod -Uri "$base/applicationPermissionGrants/$GuestAppId" -Method Put -Headers $hdrs -Body $body | Out-Null
Write-Host "Grant applied. Current grants:"
(Invoke-RestMethod -Uri "$base/applicationPermissionGrants" -Headers $hdrs -Method Get).value |
    ForEach-Object { Write-Host ("  {0}  appOnly=[{1}]" -f $_.appId, ($_.applicationPermissions -join ',')) }

Write-Host "`nNEXT: restart the BFF, then allow a few minutes for SPE propagation:"
Write-Host "  az webapp restart -g rg-spaarke-dev -n spaarke-bff-dev"
Write-Host "  # then POST /api/communications/{id}/archive should return 200 (not 403)."
