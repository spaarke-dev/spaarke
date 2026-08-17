# scripts/common/Get-SpeConfidentialClientToken.ps1
# ---------------------------------------------------------------------------
# Reusable helper: acquire a Microsoft Graph (or SharePoint) app-only token
# via confidential-client cert-based flow.
#
# Why this exists (SPE T6 fix, spec.md FR-11):
#   Microsoft Graph SPE container-type / container APIs reject public / delegated
#   clients with 403 "public client not allowed". Confidential-client with a
#   certificate is the required posture per Microsoft's SPE auth doc (updated
#   2026-07-13) and per project spec.md § MUST rules.
#
# Design (per task 011 Step 2 + ADR-028):
#   (a) Cert bootstrapped from Azure Key Vault (production) OR loaded from
#       CurrentUser cert store by thumbprint (dev fallback ONLY).
#   (b) Cert PFX downloaded to a scratch temp file that is DELETED in the
#       finally block on every code path (secret hygiene).
#   (c) JWT client assertion (RS256, x5t header) built + signed with the cert's
#       RSA private key per RFC 7523.
#   (d) client_credentials grant with client_assertion_type=jwt-bearer.
#   (e) Access token returned. NO secret material printed to stdout.
#
# The delegated flow (InteractiveBrowserCredential / DeviceCodeCredential /
# `az account get-access-token` under a user identity) and the client_secret
# flow are BOTH excluded from SPE code paths by this helper's contract.
# ---------------------------------------------------------------------------

Set-StrictMode -Version 3.0

function Get-SpeConfidentialClientToken {
    <#
    .SYNOPSIS
    Acquires a Graph/SharePoint app-only token via cert-based confidential-client
    flow (SPE T6 fix).

    .DESCRIPTION
    Bootstraps a certificate from Azure Key Vault (default) or the CurrentUser
    cert store (dev fallback), builds an RS256 JWT client assertion, and
    requests a client_credentials token from Entra ID. Scratch cert file is
    deleted on exit. No secret material is emitted to stdout.

    .PARAMETER TenantId
    Entra tenant ID that hosts the app registration.

    .PARAMETER ClientId
    App registration client ID (must be a confidential client).

    .PARAMETER Scope
    Token audience scope. Default: 'https://graph.microsoft.com/.default'.
    For SharePoint REST use: "https://{spDomain}/.default".

    .PARAMETER KeyVaultName
    Name of the Azure Key Vault containing the cert as a secret (base64 PFX).

    .PARAMETER CertSecretName
    Name of the KV secret holding the PFX-format cert. When a KV certificate
    is created, its associated secret is base64-encoded PFX under the same name.

    .PARAMETER CertThumbprint
    Alternative to KV: local CurrentUser cert store thumbprint. Dev fallback
    only; production MUST use -KeyVaultName / -CertSecretName.

    .OUTPUTS
    [string] Opaque JWT access token. Never a cert or secret.

    .EXAMPLE
    $token = Get-SpeConfidentialClientToken `
        -TenantId $env:TENANT_ID `
        -ClientId $env:API_APP_ID `
        -KeyVaultName $env:SPE_KV_NAME `
        -CertSecretName 'spe-owner-cert-pfx'

    .EXAMPLE
    $spToken = Get-SpeConfidentialClientToken `
        -TenantId  $env:TENANT_ID `
        -ClientId  $env:API_APP_ID `
        -Scope     "https://$env:SHAREPOINT_DOMAIN/.default" `
        -CertThumbprint $env:SPE_CERT_THUMBPRINT
    #>
    [CmdletBinding(DefaultParameterSetName = 'KeyVault')]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TenantId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ClientId,

        [Parameter()]
        [ValidateNotNullOrEmpty()]
        [string]$Scope = 'https://graph.microsoft.com/.default',

        [Parameter(Mandatory = $true, ParameterSetName = 'KeyVault')]
        [ValidateNotNullOrEmpty()]
        [string]$KeyVaultName,

        [Parameter(Mandatory = $true, ParameterSetName = 'KeyVault')]
        [ValidateNotNullOrEmpty()]
        [string]$CertSecretName,

        [Parameter(Mandatory = $true, ParameterSetName = 'Thumbprint')]
        [ValidateNotNullOrEmpty()]
        [string]$CertThumbprint
    )

    $ErrorActionPreference = 'Stop'

    function ConvertTo-Base64Url([byte[]]$bytes) {
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    $cert = $null
    $tempPfxPath = $null

    try {
        if ($PSCmdlet.ParameterSetName -eq 'KeyVault') {
            # Production path: bootstrap cert from KV as a base64-encoded PFX secret.
            $tempPfxPath = Join-Path ([IO.Path]::GetTempPath()) ("spe-cert-{0}.pfx" -f ([guid]::NewGuid()))

            # az keyvault secret download writes decoded bytes (base64 -> raw PFX) to --file.
            # We deliberately do NOT capture the secret value in a variable.
            $null = az keyvault secret download `
                --vault-name $KeyVaultName `
                --name $CertSecretName `
                --file $tempPfxPath `
                --encoding base64 2>&1
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tempPfxPath)) {
                throw "Failed to download cert from Key Vault '$KeyVaultName' secret '$CertSecretName'. Ensure az CLI is logged in and has 'Get secret' on the vault."
            }

            # EphemeralKeySet: private key stays in memory; never persisted to user profile.
            $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $tempPfxPath,
                [string]::Empty,
                [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
            )
        }
        else {
            # Dev fallback: cert must already be in CurrentUser\My.
            $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction Stop
        }

        if (-not $cert.HasPrivateKey) {
            throw "Certificate has no private key; cannot sign client assertion (thumbprint: $($cert.Thumbprint))."
        }

        # --- Build RS256 JWT client assertion (RFC 7523) ---
        $tokenEndpoint = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
        $nowUnix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

        $header = @{
            alg = 'RS256'
            typ = 'JWT'
            x5t = ConvertTo-Base64Url ($cert.GetCertHash())
        } | ConvertTo-Json -Compress

        $payload = @{
            aud = $tokenEndpoint
            iss = $ClientId
            sub = $ClientId
            jti = [guid]::NewGuid().ToString()
            nbf = $nowUnix
            exp = $nowUnix + 600  # 10-minute assertion lifetime
            iat = $nowUnix
        } | ConvertTo-Json -Compress

        $unsigned = (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))) + '.' + `
                    (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload)))

        $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
        $signatureBytes = $rsa.SignData(
            [Text.Encoding]::UTF8.GetBytes($unsigned),
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1
        )
        $assertion = "$unsigned." + (ConvertTo-Base64Url $signatureBytes)

        # --- Token request: client_credentials + client_assertion (NO client_secret) ---
        $tokenBody = @{
            client_id             = $ClientId
            scope                 = $Scope
            client_assertion_type = 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer'
            client_assertion      = $assertion
            grant_type            = 'client_credentials'
        }

        $tokenResponse = Invoke-RestMethod `
            -Uri $tokenEndpoint `
            -Method Post `
            -Body $tokenBody `
            -ErrorAction Stop

        if (-not $tokenResponse.access_token) {
            throw "Token endpoint returned no access_token (received token_type=$($tokenResponse.token_type))."
        }

        return [string]$tokenResponse.access_token
    }
    finally {
        # Scrub scratch cert file on every exit path.
        if ($tempPfxPath -and (Test-Path $tempPfxPath)) {
            Remove-Item -Path $tempPfxPath -Force -ErrorAction SilentlyContinue
        }
        # Dispose managed cert handle to release private key material.
        if ($null -ne $cert -and $cert -is [System.IDisposable]) {
            try { $cert.Dispose() } catch { }
        }
    }
}
