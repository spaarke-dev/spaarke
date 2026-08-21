<#
.SYNOPSIS
    Creates the production Entra ID app registration for the Spaarke BFF API.

.DESCRIPTION
    Creates the BFF API app registration in the shared Entra ID tenant (a221a95e-...):
      1. spaarke-bff-api-prod — BFF API production app (validates user tokens, OBO for Graph + Dataverse)

    NOTE (2026-08-14, code-quality-and-assurance-r3 task 060): the separate
    `spaarke-dataverse-s2s-*` app registration was REMOVED. It had zero code consumers —
    Dataverse server-to-server access consolidated onto the BFF app registration's
    credential (`API_CLIENT_SECRET`) on 2026-01-07 (see docs/architecture/auth-azure-resources.md).
    The BFF app registration is the single Dataverse Application User.

    For the registration:
      - Creates the app registration with correct API permissions
      - Generates a 24-month client secret
      - Stores the secret in the platform Key Vault (sprk-platform-prod-kv)
      - Configures redirect URIs, exposed API scopes, and known client applications

    The BFF API registration mirrors the dev registration (1e40baad) with production-specific
    redirect URIs and known client applications.

    Prerequisites:
      - Azure CLI authenticated with Entra ID admin permissions
      - Access to sprk-platform-prod-kv Key Vault
      - Tenant: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER TenantId
    Entra ID tenant ID. Default: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER KeyVaultName
    Key Vault name for storing secrets. Default: sprk-platform-prod-kv

.PARAMETER ProductionApiDomain
    Production API domain for redirect URIs. Default: api.spaarke.com

.PARAMETER DataverseOrgUrl
    Production Dataverse organization URL. Default: (empty, set when known)

.PARAMETER DryRun
    If specified, shows what would be created without making changes.

.PARAMETER SkipBffApi
    Skip BFF API app registration (if already created).

.EXAMPLE
    # Preview what will be created
    .\Register-EntraAppRegistrations.ps1 -DryRun

.EXAMPLE
    # Create the BFF API registration
    .\Register-EntraAppRegistrations.ps1

.PARAMETER CreateFederatedCredential
    Create (idempotently) a managed-identity federated credential — MI-FIC — on the app
    registration, then verify it by performing an actual token exchange. Requires
    -UamiResourceId. Inert unless specified: the script's behaviour without this flag is
    exactly as it was before FIC support was added.

.PARAMETER FicOnly
    Do only the federated-credential work and exit. Implies -SkipBffApi and skips the Key Vault
    pre-flight. This is the entry point customer-provisioning-orchestration-r1 invokes.

.PARAMETER UamiResourceId
    ARM resource ID of the user-assigned managed identity to federate, e.g.
    /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev
    Resource ID, never name: five UAMIs exist in the dev subscription and 'spaarke-bff-identity'
    is a decoy that is NOT attached to the BFF.

.PARAMETER FederatedCredentialAppId
    App registration (appId or object ID) to add the credential to. Defaults to the app
    registration this run created.

.PARAMETER FederatedCredentialName
    Name of the credential. Defaults to mi-<uami-name>-assertion.

.PARAMETER AssertionToken
    A managed-identity token for the exchange audience, used to verify the credential by real
    token exchange. Only needed off-Azure: a UAMI assertion can only be minted from inside
    Azure, so a workstation cannot produce one and must be handed one.

.PARAMETER AllowUnverified
    Exit 0 even when the credential could not be verified by token exchange. Off by default,
    deliberately — an unverified FIC is not evidence that anything works.

.EXAMPLE
    # Create + verify the dev FIC (re-running against an existing one is a no-op)
    .\Register-EntraAppRegistrations.ps1 -FicOnly `
      -FederatedCredentialAppId 1e40baad-e065-4aea-a8d4-4b7ab273458c `
      -UamiResourceId "/subscriptions/<sub>/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev"

.EXAMPLE
    # Import the FIC functions into another provisioning script without running anything
    . .\Register-EntraAppRegistrations.ps1 -ExportFunctionsOnly
    New-SpaarkeFederatedCredential -AppId $appId -UamiResourceId $uami -TenantId $tenant

.NOTES
    Project: production-environment-setup-r1
    Task: 021 — Create Entra ID app registrations
    Naming: FR-11 compliant (spaarke- prefix)
    Secrets: FR-08 compliant (Key Vault only)
#>

param(
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$KeyVaultName = "sprk-platform-prod-kv",
    [string]$ProductionApiDomain = "api.spaarke.com",
    [string]$DataverseOrgUrl = "",
    [switch]$DryRun,
    [switch]$SkipBffApi,

    # ── Federated identity credential (MI-FIC) — added 2026-08-21, task 030 / spec FR-C4 ──
    # All inert by default. Existing invocations behave exactly as before.
    [switch]$CreateFederatedCredential,
    [switch]$FicOnly,
    [switch]$ExportFunctionsOnly,
    [string]$UamiResourceId = "",
    [string]$FederatedCredentialAppId = "",
    [string]$FederatedCredentialName = "",
    [string]$FederatedCredentialAudience = "api://AzureADTokenExchange",
    [string]$AssertionToken = "",
    [string]$ExchangeScope = "https://graph.microsoft.com/.default",
    [int]$PropagationRetrySeconds = 600,
    [switch]$ForceFederatedCredentialUpdate,
    [switch]$AllowUnverified
)

$ErrorActionPreference = "Stop"

# -FicOnly is a mode, not an extra step: it turns this into a federated-credential-only run.
if ($FicOnly) {
    $SkipBffApi = $true
    $CreateFederatedCredential = $true
}

# ─────────────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────────────

$BffApiDisplayName = "spaarke-bff-api-prod"
$SecretExpiryMonths = 24
$SecretExpiryDate = (Get-Date).AddMonths($SecretExpiryMonths).ToString("yyyy-MM-ddTHH:mm:ssZ")

# Microsoft Graph API well-known IDs
#
# NOTE: these four IDs are DELEGATED scopes (OAuth2PermissionScopes) requested at app-registration
# creation time — a DIFFERENT concern from the app-only APPLICATION roles the BFF identity holds.
# Do NOT merge these with the application-role list. The canonical source of truth for the BFF's
# expected Graph APPLICATION (app-only) roles is:
#   src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs
$GraphApiId = "00000003-0000-0000-c000-000000000000"
$GraphFilesReadWriteAll = "75359482-378d-4052-8f01-80520e7db3cd"   # Files.ReadWrite.All (delegated)
$GraphSitesReadWriteAll = "89fe6a52-be36-487e-b7d8-d061c450a026"   # Sites.ReadWrite.All (delegated)
$GraphUserRead          = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"   # User.Read (delegated)
$GraphMailSend          = "e383f46e-2787-4529-855e-0e479a3ffac0"   # Mail.Send (delegated)

# Dynamics CRM API well-known ID
$DynamicsCrmApiId = "00000007-0000-0000-c000-000000000000"
$DynamicsCrmUserImpersonation = "78ce3f0f-a1ce-49c2-8cde-64b5c0896db4"  # user_impersonation (delegated)

# ─────────────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([int]$Number, [string]$Description)
    Write-Host "  [$Number] $Description" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  [--] $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [!!] $Message" -ForegroundColor DarkYellow
}

function Store-SecretInKeyVault {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$SecretValue,
        [string]$Description
    )

    if ($DryRun) {
        Write-Info "DRY RUN: Would store secret '$SecretName' in Key Vault '$VaultName'"
        return
    }

    Write-Info "Storing secret '$SecretName' in Key Vault '$VaultName'..."
    az keyvault secret set `
        --vault-name $VaultName `
        --name $SecretName `
        --value $SecretValue `
        --description $Description `
        --output none 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to store secret '$SecretName' in Key Vault '$VaultName'"
    }

    Write-Success "Secret '$SecretName' stored in Key Vault"
}

# ─────────────────────────────────────────────────────────────────────────────
# Federated Identity Credential (MI-FIC) — spaarke-auth-v4-dataverse-MI FR-C4
#
# Added 2026-08-21 (task 030). Before this, NO automation anywhere in the repo created a
# federated identity credential — the dev FIC (`mi-bff-api-dev-assertion`) and even the
# GitHub Actions OIDC FIC were both hand-run. See PROVISIONING-CHANGE-REQUEST.md §3.2.
#
# These functions are additive and inert unless -CreateFederatedCredential / -FicOnly /
# -ExportFunctionsOnly is passed. The script's pre-existing behaviour is unchanged.
# ─────────────────────────────────────────────────────────────────────────────

# Entra rejects any other value; it is NOT tenant- or app-specific. Exposed as a parameter because
# ADR-028 A4 calls out that sovereign clouds use a different exchange audience.
# HONEST SCOPE, so the parameter is not mistaken for cross-cloud support: the issuer and the token
# endpoint below both hard-code `login.microsoftonline.com`, and $ExchangeScope defaults to
# `graph.microsoft.com`. This is COMMERCIAL CLOUD ONLY, consistent with TENANCY-AND-CREDENTIALS.md §1
# ("cross-cloud is unsupported"). Making it genuinely cross-cloud means deriving all three from
# `az cloud show --query endpoints`, not just parameterising this one value.
$script:DefaultExchangeAudience = "api://AzureADTokenExchange"

# The ONLY error code that means "propagation delay". Deliberately narrow: AADSTS700211
# (unrecognised issuer) and AADSTS7000215 (invalid secret) are configuration faults that
# retrying only delays. See Test-SpaarkeFicTokenExchange for why this list must not grow.
$script:PropagationErrorCodes = @("AADSTS70021")

function Resolve-SpaarkeUserAssignedIdentity {
    <#
    .SYNOPSIS
        Resolves a user-assigned managed identity BY ARM RESOURCE ID and returns its
        clientId, principalId, tenantId and name.

    .DESCRIPTION
        Resolution is by resource ID, never by display name. Five UAMIs exist in the dev
        subscription and one of them — `spaarke-bff-identity` — reads like the BFF's identity
        but is NOT attached to spaarke-bff-dev (PHASE-0-LIVE-VERIFICATION.md §2). A name-based
        lookup silently selects the decoy and produces a FIC that creates cleanly and never
        works.

        THE TWO IDs THIS RETURNS ARE NOT INTERCHANGEABLE. Confusing them is the designated
        silent-failure mode of the entire MI-FIC mechanism:

          clientId     selects WHICH identity mints the assertion (used at mint time)
          principalId  is the FIC SUBJECT — the 'sub' claim Entra matches (used at create time)

        A FIC whose subject is the clientId creates successfully, returns HTTP 201, and fails
        only at token exchange — with the SAME generic AADSTS70021 that ordinary propagation
        delay produces. That collision is why Test-SpaarkeFederatedCredentialShape exists and
        why it must run before any retry loop.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ResourceId)

    if ($ResourceId -notmatch '(?i)/providers/Microsoft\.ManagedIdentity/userAssignedIdentities/[^/]+$') {
        throw "UamiResourceId is not a user-assigned managed identity resource ID: '$ResourceId'. Expected /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<name>. Resolve by resource ID, never by name — five UAMIs exist in the dev subscription and 'spaarke-bff-identity' is a decoy that is not attached to the BFF."
    }

    $identity = az identity show --ids $ResourceId --output json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $identity) {
        throw "Failed to read user-assigned managed identity '$ResourceId'. Check the resource ID and that the signed-in principal can read it."
    }

    foreach ($field in @("clientId", "principalId", "tenantId")) {
        if (-not $identity.$field) {
            throw "User-assigned managed identity '$ResourceId' returned no $field. Cannot build a federated credential without it."
        }
    }

    return [pscustomobject]@{
        Name        = $identity.name
        ResourceId  = $ResourceId
        ClientId    = $identity.clientId      # mint-time selector — NOT the FIC subject
        PrincipalId = $identity.principalId   # FIC subject — the 'sub' claim
        TenantId    = $identity.tenantId
    }
}

function Assert-SpaarkeFicTenancy {
    <#
    .SYNOPSIS
        Enforces the single hard platform rule: the app registration and the UAMI MUST live in
        the same Entra tenant.

    .DESCRIPTION
        Cross-tenant *resource* access is fully supported. A cross-tenant *FIC issuer* is not
        (TENANCY-AND-CREDENTIALS.md §1). This function refuses rather than parameterising,
        because the failure is silent: a cross-tenant FIC creates successfully and fails only
        at token exchange.

        This is also the open question raised back to customer-provisioning-orchestration-r1 in
        PROVISIONING-CHANGE-REQUEST.md §9.2 (Model 2's customer-tenant shape). Until that is
        answered, a cross-tenant pair is a hard stop here, not an option behind a flag.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AppRegistrationTenantId,
        [Parameter(Mandatory = $true)]$Identity
    )

    if ($AppRegistrationTenantId -ne $Identity.TenantId) {
        $msg = @"
CROSS-TENANT FEDERATED CREDENTIAL — REFUSED (not supported by Entra)

  App registration tenant       : $AppRegistrationTenantId
  UAMI '$($Identity.Name)' tenant : $($Identity.TenantId)

Entra requires the app registration and the user-assigned managed identity to be in the SAME
tenant. Cross-tenant resource access is supported; a cross-tenant FIC issuer is not
(TENANCY-AND-CREDENTIALS.md 1; ADR-028 A4).

This is refused rather than attempted because the failure mode is silent — the credential would
CREATE SUCCESSFULLY and fail only at token exchange with a generic error.

If you are provisioning a customer-tenant stamp (Model 2), it must use that stamp's OWN UAMI as
the FIC issuer. If it genuinely must trust the shared Spaarke UAMI, MI-FIC is structurally
impossible for that shape and it needs the ADR-028 A4 certificate alternative instead. This is
the unresolved question in PROVISIONING-CHANGE-REQUEST.md 9.2 — resolve it before Wave G-3
task 130 executes.
"@
        throw $msg
    }

    Write-Success "Tenancy verified: app registration and UAMI '$($Identity.Name)' are both in tenant $AppRegistrationTenantId"
}

function Get-SpaarkeFederatedCredential {
    <#
    .SYNOPSIS
        Returns the federated identity credential with the given name on the app registration,
        or $null when absent. Never throws on absence — absence is the create path.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AppId,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $existing = az ad app federated-credential list --id $AppId --output json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list federated credentials on app registration '$AppId'. Check the app ID and that the signed-in principal can read it."
    }
    if (-not $existing) { return $null }

    return ($existing | Where-Object { $_.name -eq $Name } | Select-Object -First 1)
}

function Find-SpaarkeEquivalentFederatedCredential {
    <#
    .SYNOPSIS
        Returns any federated credential on the app that already carries the given
        (issuer, subject, audience) triple, regardless of its name. $null when none does.

    .DESCRIPTION
        The NAME of a federated credential is a label. What Entra actually matches an incoming
        assertion against is the (issuer, subject, audience) triple. So a credential with the
        right triple under a different name ALREADY SATISFIES a create request, and adding a
        second one produces a redundant credential that nothing will ever read while consuming
        one of the app registration's 20 FIC slots.

        Idempotency therefore cannot be name-based. This was caught live rather than reasoned
        about: the first real run of this code derived the name 'mi-mi-bff-api-dev-assertion'
        (the UAMI is itself named 'mi-bff-api-dev'), found no match against the existing
        'mi-bff-api-dev-assertion', and proceeded to create.

        WHAT ACTUALLY HAPPENS THEN, verified against Entra 2026-08-21 rather than assumed:
        Entra enforces (issuer, subject) uniqueness per application itself and rejects the
        create with

            "The combination of issuer and subject must be unique for the application."

        So the duplicate is NOT silently created — the platform is a backstop. The defect is
        that a re-run against an already-correct, already-working credential FAILS instead of
        being a no-op, which is precisely what the idempotency requirement rules out. (An
        earlier version of this comment claimed the create would have succeeded silently and
        was stopped only by a missing role. That was wrong: the observed rejection is a
        validation error, not an authorization one.)

        Checking the triple ourselves converts that platform error into the correct answer —
        "already satisfied, nothing to do" — instead of surfacing a confusing failure from a
        credential that was fine all along.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AppId,
        [Parameter(Mandatory = $true)][string]$Issuer,
        [Parameter(Mandatory = $true)][string]$Subject,
        [Parameter(Mandatory = $true)][string]$Audience
    )

    $all = az ad app federated-credential list --id $AppId --output json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $all) { return $null }

    return ($all | Where-Object {
        $_.issuer -eq $Issuer -and
        $_.subject -eq $Subject -and
        (@($_.audiences).Count -eq 1) -and
        (@($_.audiences)[0] -eq $Audience)
    } | Select-Object -First 1)
}

function Test-SpaarkeFederatedCredentialShape {
    <#
    .SYNOPSIS
        Structural verification: does this FIC's issuer/subject/audience match what the resolved
        UAMI actually requires?

    .DESCRIPTION
        THIS IS NOT DECORATION AND IT IS NOT OPTIONAL. It exists to break a genuine collision
        between two requirements that look independent but are not:

          * a FIC whose subject is WRONG produces AADSTS70021 at exchange
          * a FIC that is merely still PROPAGATING produces AADSTS70021 at exchange

        Identical symptom. So a retry-on-70021 loop, on its own, cannot tell "wrong forever"
        from "right in thirty seconds" — it just makes a permanent misconfiguration look like
        slow propagation until the timeout expires, and then reports a timeout instead of the
        actual fault.

        Running this check FIRST resolves the ambiguity: it compares the subject against the
        principalId read from the identity resource itself, so by the time the exchange runs,
        AADSTS70021 has exactly one remaining explanation — propagation — and retrying it is
        correct rather than merely hopeful.

        It also catches the specific conflation (subject = clientId) by name, because a generic
        "subject mismatch" is not what an operator needs at 2am.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Credential,
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)][string]$ExpectedIssuer,
        [Parameter(Mandatory = $true)][string]$ExpectedAudience
    )

    $problems = @()

    if ($Credential.subject -ne $Identity.PrincipalId) {
        if ($Credential.subject -eq $Identity.ClientId) {
            # The named silent-failure mode (FR-B4). Report it as itself, not as a mismatch.
            $problems += @"
SUBJECT IS THE UAMI'S clientId, NOT ITS principalId — the designated silent failure.
    subject  : $($Credential.subject)   <- clientId of '$($Identity.Name)'
    expected : $($Identity.PrincipalId)   <- principalId (object ID) of '$($Identity.Name)'
  This credential created successfully and can never complete a token exchange. At exchange it
  returns AADSTS70021 — indistinguishable from propagation delay, which is why it is caught
  structurally here rather than left to the retry loop to time out on.
"@
        }
        else {
            $problems += "Subject mismatch: FIC subject is '$($Credential.subject)' but UAMI '$($Identity.Name)' has principalId '$($Identity.PrincipalId)'."
        }
    }

    if ($Credential.issuer -ne $ExpectedIssuer) {
        $problems += "Issuer mismatch: FIC issuer is '$($Credential.issuer)' but the hosting tenant's OIDC issuer is '$ExpectedIssuer'."
    }

    $audiences = @($Credential.audiences)
    if ($audiences.Count -ne 1 -or $audiences[0] -ne $ExpectedAudience) {
        $problems += "Audience mismatch: FIC audiences are [$($audiences -join ', ')] but must be exactly ['$ExpectedAudience']."
    }

    return [pscustomobject]@{
        IsValid  = ($problems.Count -eq 0)
        Problems = $problems
    }
}

function Get-SpaarkeManagedIdentityAssertion {
    <#
    .SYNOPSIS
        Mints a managed-identity token for the token-exchange audience, to be presented as the
        client_assertion. Returns $null when the host cannot mint one.

    .DESCRIPTION
        A UAMI assertion can only be minted from inside Azure, on compute that carries the
        identity — a developer workstation cannot produce one at all
        (PHASE-0-LIVE-VERIFICATION.md 4). Returning $null is therefore an ordinary, expected
        outcome, NOT an error: it means "verification must come from elsewhere", and the caller
        decides what to do about that.

        Note which ID is used here: the UAMI's clientId SELECTS the identity on multi-identity
        compute. The principalId is what ends up in the token's 'sub' claim, which is what the
        FIC matches on. Same two IDs, opposite roles.

        Timeouts are deliberately short. Off-Azure there is no IMDS endpoint and the request
        must fail fast rather than hang a provisioning run.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$UamiClientId,
        [Parameter(Mandatory = $true)][string]$Audience
    )

    # App Service / Container Apps / Functions
    if ($env:IDENTITY_ENDPOINT -and $env:IDENTITY_HEADER) {
        try {
            $uri = "$($env:IDENTITY_ENDPOINT)?resource=$([uri]::EscapeDataString($Audience))&client_id=$UamiClientId&api-version=2019-08-01"
            $resp = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ "X-IDENTITY-HEADER" = $env:IDENTITY_HEADER } -TimeoutSec 15 -ErrorAction Stop
            if ($resp.access_token) {
                Write-Info "Assertion minted from the App Service identity endpoint."
                return $resp.access_token
            }
        }
        catch {
            Write-Warn "App Service identity endpoint present but did not return an assertion: $($_.Exception.Message)"
        }
    }

    # VM / VMSS IMDS
    try {
        $uri = "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=$([uri]::EscapeDataString($Audience))&client_id=$UamiClientId"
        $resp = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ Metadata = "true" } -TimeoutSec 5 -ErrorAction Stop
        if ($resp.access_token) {
            Write-Info "Assertion minted from IMDS."
            return $resp.access_token
        }
    }
    catch {
        # Expected off-Azure. Not an error.
    }

    return $null
}

function Test-SpaarkeFicTokenExchange {
    <#
    .SYNOPSIS
        Performs a REAL token exchange against Entra using the managed-identity assertion as
        client_assertion. This is the authoritative proof that the FIC works.

    .DESCRIPTION
        A successful 'az ad app federated-credential create' proves nothing about whether the
        credential functions — a misconfigured FIC creates cleanly and fails only here. This
        function is the reason the create path is never allowed to self-report success.

        WHAT COUNTS AS PASSING. The question asked is "did Entra ACCEPT the assertion as this
        app registration's credential?" — not "does this app have permissions?". Those are
        different layers, and conflating them would make verification fail on every freshly
        provisioned app registration, before any grants exist. So:

          credential-layer failure (invalid_client, AADSTS70021, AADSTS700211, AADSTS7000215)
              -> FAIL. The assertion was rejected.
          authorization-layer error (e.g. AADSTS500011 resource principal not found)
              -> PASS. Entra evaluates the resource only AFTER accepting the client credential.
          token issued
              -> PASS.

        RETRY. AADSTS70021 is retried, because immediately after creation it means propagation
        (TENANCY-AND-CREDENTIALS.md 1; PHASE-0 7). It is safe to retry it here ONLY because the
        structural check has already ruled out the wrong-subject explanation for the same code.
        Do not widen $script:PropagationErrorCodes — every other code is a configuration fault
        that retrying merely delays.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$TenantId,
        [Parameter(Mandatory = $true)][string]$AppClientId,
        [Parameter(Mandatory = $true)][string]$Assertion,
        [Parameter(Mandatory = $true)][string]$Scope,
        [int]$MaxWaitSeconds = 600
    )

    $tokenUri = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
    $body = @{
        client_id             = $AppClientId
        scope                 = $Scope
        grant_type            = "client_credentials"
        client_assertion_type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
        client_assertion      = $Assertion
    }

    $started = Get-Date
    $delay = 5
    $attempt = 0

    while ($true) {
        $attempt++
        $errorBody = $null

        try {
            $null = Invoke-RestMethod -Method Post -Uri $tokenUri -Body $body `
                -ContentType "application/x-www-form-urlencoded" -ErrorAction Stop

            return [pscustomobject]@{
                Accepted = $true
                Attempts = $attempt
                Detail   = "Entra issued a token for scope '$Scope'. The federated credential is valid and working."
            }
        }
        catch {
            if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
                $errorBody = $_.ErrorDetails.Message
            }
            elseif ($_.Exception.Response) {
                try {
                    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                    $errorBody = $reader.ReadToEnd()
                    $reader.Dispose()
                }
                catch { $errorBody = $_.Exception.Message }
            }
            else {
                $errorBody = $_.Exception.Message
            }
        }

        $isPropagation = $false
        foreach ($code in $script:PropagationErrorCodes) {
            if ($errorBody -match $code) { $isPropagation = $true; break }
        }

        if (-not $isPropagation) {
            # Entra evaluates the resource only after accepting the client credential, so an
            # authorization-layer error is positive evidence the assertion was accepted.
            if ($errorBody -match "AADSTS500011") {
                return [pscustomobject]@{
                    Accepted = $true
                    Attempts = $attempt
                    Detail   = "Assertion ACCEPTED. Entra rejected the requested scope '$Scope' (AADSTS500011: resource principal not found), which it evaluates only after accepting the client credential. The federated credential itself is valid."
                }
            }

            return [pscustomobject]@{
                Accepted = $false
                Attempts = $attempt
                Detail   = "Entra REJECTED the assertion. This is a credential fault, not propagation — retrying would only delay the report.`n$errorBody"
            }
        }

        $elapsed = ((Get-Date) - $started).TotalSeconds
        if ($elapsed + $delay -gt $MaxWaitSeconds) {
            $timeoutDetail = @"
AADSTS70021 persisted for $([int]$elapsed)s across $attempt attempt(s) (limit ${MaxWaitSeconds}s).

The structural check already confirmed issuer, subject and audience match this UAMI, so the
usual explanation — subject set to the clientId instead of the principalId — is ruled out.
Remaining candidates, in order of likelihood:
  1. Propagation genuinely slower than the configured limit. Re-run with a larger
     -PropagationRetrySeconds before investigating anything else.
  2. The assertion was minted by a DIFFERENT identity than the one the FIC trusts. Check that
     the compute producing it carries the intended UAMI, and that the mint request passed the
     intended client_id.
  3. The federated credential was deleted or altered between creation and exchange.

Last response:
$errorBody
"@
            return [pscustomobject]@{
                Accepted = $false
                Attempts = $attempt
                Detail   = $timeoutDetail
            }
        }

        Write-Info "AADSTS70021 (propagation) — attempt $attempt, retrying in ${delay}s (elapsed $([int]$elapsed)s of ${MaxWaitSeconds}s)..."
        Start-Sleep -Seconds $delay
        $delay = [Math]::Min($delay * 2, 30)
    }
}

function New-SpaarkeFederatedCredential {
    <#
    .SYNOPSIS
        Idempotently creates a managed-identity federated credential on an app registration and
        verifies it by performing an actual token exchange.

    .DESCRIPTION
        Ordering is load-bearing:

          1. resolve the UAMI by resource ID          -> authoritative clientId + principalId
          2. enforce same-tenant                      -> refuse a structurally impossible FIC
          3. create, or detect the existing one       -> idempotent; drift reported, not overwritten
          4. STRUCTURAL check against the principalId -> disambiguates AADSTS70021 for step 5
          5. REAL token exchange                      -> the only proof the credential works

        Step 4 must precede step 5 (see Test-SpaarkeFederatedCredentialShape). Step 5 is never
        skipped silently: when no assertion can be minted, the result is Unverified and the
        caller is expected to fail unless it explicitly opted out.

    .OUTPUTS
        PSCustomObject: Name, Created, AlreadyExisted, StructurallyValid, ExchangeVerified,
        Verification ('Verified' | 'Unverified' | 'Failed'), Credential, Identity.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AppId,
        [Parameter(Mandatory = $true)][string]$UamiResourceId,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [string]$Name,
        [string]$Audience = $script:DefaultExchangeAudience,
        [string]$AssertionToken,
        [string]$ExchangeScope = "https://graph.microsoft.com/.default",
        [int]$PropagationRetrySeconds = 600,
        [switch]$Force,
        [switch]$WhatIfDryRun
    )

    Write-Step 1 "Resolving user-assigned managed identity by resource ID"
    $identity = Resolve-SpaarkeUserAssignedIdentity -ResourceId $UamiResourceId
    Write-Success "UAMI '$($identity.Name)' — clientId $($identity.ClientId) | principalId $($identity.PrincipalId)"
    Write-Info "FIC subject will be the principalId. The clientId is only used to mint the assertion."

    Write-Step 2 "Verifying tenancy"
    Assert-SpaarkeFicTenancy -AppRegistrationTenantId $TenantId -Identity $identity

    # The UAMI naming convention already carries the 'mi-' prefix (mi-bff-api-dev), so do NOT
    # add another one — that produced 'mi-mi-bff-api-dev-assertion' on the first live run.
    # Matches the hand-created dev credential's name exactly (PHASE-0-LIVE-VERIFICATION.md 3).
    if (-not $Name) { $Name = "$($identity.Name)-assertion" }
    $issuer = "https://login.microsoftonline.com/$TenantId/v2.0"

    $result = [pscustomobject]@{
        Name              = $Name
        Created           = $false
        AlreadyExisted    = $false
        StructurallyValid = $false
        ExchangeVerified  = $false
        Verification      = "Unverified"
        Credential        = $null
        Identity          = $identity
    }

    Write-Step 3 "Checking for an existing federated credential"
    $existing = Get-SpaarkeFederatedCredential -AppId $AppId -Name $Name

    $desired = @{
        name        = $Name
        issuer      = $issuer
        subject     = $identity.PrincipalId
        audiences   = @($Audience)
        description = "Managed-identity assertion for $($identity.Name) (ADR-028 A4 MI-FIC)"
    }

    # Name missed. Before concluding "absent", look for a credential that already carries the
    # required triple under a different name — see Find-SpaarkeEquivalentFederatedCredential
    # for why name-based idempotency is not idempotency.
    if (-not $existing) {
        $equivalent = Find-SpaarkeEquivalentFederatedCredential -AppId $AppId `
            -Issuer $issuer -Subject $identity.PrincipalId -Audience $Audience
        if ($equivalent) {
            Write-Success "A federated credential with the required issuer/subject/audience already exists under the name '$($equivalent.name)'."
            Write-Info "Not creating '$Name' — it would be a redundant duplicate of an already-working credential."
            $existing = $equivalent
            $Name = $equivalent.name
            $result.Name = $equivalent.name
        }
    }

    if ($existing) {
        $result.AlreadyExisted = $true
        $shape = Test-SpaarkeFederatedCredentialShape -Credential $existing -Identity $identity `
            -ExpectedIssuer $issuer -ExpectedAudience $Audience

        if ($shape.IsValid) {
            Write-Success "Federated credential '$Name' already exists and matches — no change made (idempotent)."
            $result.Credential = $existing
        }
        elseif ($Force) {
            Write-Warn "Existing credential '$Name' does not match the desired shape. -ForceFederatedCredentialUpdate specified; updating."
            foreach ($p in $shape.Problems) { Write-Warn "  $p" }
            if ($WhatIfDryRun) {
                Write-Info "DRY RUN: would update federated credential '$Name' to subject $($identity.PrincipalId)"
                $result.Credential = $existing
            }
            else {
                $paramPath = [System.IO.Path]::GetTempFileName()
                ($desired | ConvertTo-Json -Depth 4) | Out-File -FilePath $paramPath -Encoding utf8
                # Capture az's own stderr rather than discarding it — the CLI's message is the
                # only thing that distinguishes a permissions failure from a malformed payload,
                # and a caller debugging a provisioning run needs to see it verbatim.
                $azOutput = az ad app federated-credential update --id $AppId `
                    --federated-credential-id $existing.id --parameters "@$paramPath" 2>&1
                $updateExit = $LASTEXITCODE
                Remove-Item $paramPath -ErrorAction SilentlyContinue
                if ($updateExit -ne 0) {
                    throw "Failed to update federated credential '$Name' on app '$AppId'.`nAzure CLI reported:`n$($azOutput -join [Environment]::NewLine)"
                }
                Write-Success "Federated credential '$Name' updated."
                $result.Credential = Get-SpaarkeFederatedCredential -AppId $AppId -Name $Name
            }
        }
        else {
            # Drift is reported, never silently overwritten — the existing credential may be in
            # active use by a running service, and replacing it is an availability event.
            $driftMsg = @"
DRIFT: federated credential '$Name' exists on app '$AppId' but does not match this UAMI.

$($shape.Problems -join "`n")

Refusing to overwrite it — a credential in this position may be in active use, and replacing one
is an availability event, not a repair. Re-run with -ForceFederatedCredentialUpdate to update it
deliberately, or delete it first:
  az ad app federated-credential delete --id $AppId --federated-credential-id $($existing.id)
"@
            throw $driftMsg
        }
    }
    else {
        Write-Step 4 "Creating federated credential '$Name'"
        Write-Info "issuer   : $issuer"
        Write-Info "subject  : $($identity.PrincipalId)  (principalId of '$($identity.Name)')"
        Write-Info "audience : $Audience"

        if ($WhatIfDryRun) {
            Write-Info "DRY RUN: would create the federated credential above. No changes made."
            Write-Info "DRY RUN: token-exchange verification is not performed in dry-run mode."
            $result.Credential = [pscustomobject]$desired
            $result.Created = $true
            $result.StructurallyValid = $true
            $result.Verification = "Unverified"
            return $result
        }

        $paramPath = [System.IO.Path]::GetTempFileName()
        ($desired | ConvertTo-Json -Depth 4) | Out-File -FilePath $paramPath -Encoding utf8
        # Capture az's own stderr rather than discarding it. "Requires Application Administrator"
        # is the LIKELIEST cause but not the only one — a malformed payload, a missing app, or a
        # duplicate (issuer, subject) pair all land here too, and guessing on the caller's behalf
        # is what makes a provisioning failure take an afternoon instead of a minute.
        $azOutput = az ad app federated-credential create --id $AppId --parameters "@$paramPath" 2>&1
        $createExit = $LASTEXITCODE
        Remove-Item $paramPath -ErrorAction SilentlyContinue

        if ($createExit -ne 0) {
            throw "Failed to create federated credential '$Name' on app '$AppId'. Creating a FIC usually requires Application Administrator (or ownership of the app registration).`nAzure CLI reported:`n$($azOutput -join [Environment]::NewLine)"
        }

        Write-Success "Federated credential '$Name' created."
        Write-Warn "Creation success proves NOTHING about whether it works — a misconfigured FIC creates cleanly. Verifying."
        $result.Created = $true
        $result.Credential = Get-SpaarkeFederatedCredential -AppId $AppId -Name $Name
    }

    # ── Structural verification. Runs before the exchange, always. ────────────────────────────
    Write-Step 5 "Structural verification against the resolved principalId"
    $shape = Test-SpaarkeFederatedCredentialShape -Credential $result.Credential -Identity $identity `
        -ExpectedIssuer $issuer -ExpectedAudience $Audience

    if (-not $shape.IsValid) {
        foreach ($p in $shape.Problems) { Write-Warn $p }
        $result.Verification = "Failed"
        throw "Federated credential '$Name' is structurally invalid — see above. It would fail at token exchange with a generic AADSTS70021."
    }

    $result.StructurallyValid = $true
    Write-Success "Structure verified: issuer, subject (= principalId) and audience all match UAMI '$($identity.Name)'."

    # ── Exchange verification. The authoritative proof. ───────────────────────────────────────
    Write-Step 6 "Token-exchange verification"

    $assertion = $AssertionToken
    if (-not $assertion) {
        $assertion = Get-SpaarkeManagedIdentityAssertion -UamiClientId $identity.ClientId -Audience $Audience
    }

    if (-not $assertion) {
        $result.Verification = "Unverified"
        $unverifiedMsg = @"
CANNOT VERIFY BY TOKEN EXCHANGE FROM THIS HOST.

A managed-identity assertion can only be minted from inside Azure, on compute that carries the
identity — a workstation cannot produce one at all. Structure is verified; function is not.

To complete verification, mint an assertion on compute carrying UAMI '$($identity.Name)':

  az login --identity --username $($identity.ClientId)
  az account get-access-token --resource $Audience --query accessToken -o tsv

then re-run with -AssertionToken <token>. Nothing consumes this credential until the provider
seam ships, so an unverified credential is not a live risk — but it is also not evidence that
anything works.
"@
        Write-Warn $unverifiedMsg
        return $result
    }

    $exchange = Test-SpaarkeFicTokenExchange -TenantId $TenantId -AppClientId $AppId `
        -Assertion $assertion -Scope $ExchangeScope -MaxWaitSeconds $PropagationRetrySeconds

    if ($exchange.Accepted) {
        $result.ExchangeVerified = $true
        $result.Verification = "Verified"
        Write-Success "Token exchange succeeded after $($exchange.Attempts) attempt(s). $($exchange.Detail)"
    }
    else {
        $result.Verification = "Failed"
        Write-Warn $exchange.Detail
        throw "Federated credential '$Name' exists but FAILED token-exchange verification. Do not treat this credential as working."
    }

    return $result
}

# -ExportFunctionsOnly lets a provisioning script dot-source this file to obtain the FIC
# functions without executing any of the registration flow. Must come after the function
# definitions and before the first side effect.
if ($ExportFunctionsOnly) { return }

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight Checks
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "ENTRA ID APP REGISTRATION — PRODUCTION"

if ($DryRun) {
    Write-Host "  *** DRY RUN MODE — No changes will be made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Step 0 "Pre-flight checks"

# Verify Azure CLI is authenticated
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  [ERROR] Azure CLI not authenticated. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Success "Azure CLI authenticated as: $($account.user.name)"

# Verify correct tenant
$currentTenant = $account.tenantId
if ($currentTenant -ne $TenantId) {
    Write-Warn "Current tenant ($currentTenant) differs from target ($TenantId)"
    Write-Info "Switching tenant..."
    if (-not $DryRun) {
        az account set --subscription $TenantId 2>$null
    }
}
Write-Success "Target tenant: $TenantId"

# Verify Key Vault access (not relevant to a federated-credential-only run — a FIC replaces
# the secret rather than storing one)
if (-not $DryRun -and -not $FicOnly) {
    $kvCheck = az keyvault show --name $KeyVaultName --output json 2>$null | ConvertFrom-Json
    if (-not $kvCheck) {
        Write-Warn "Key Vault '$KeyVaultName' not accessible. Secrets will need manual storage."
    } else {
        Write-Success "Key Vault '$KeyVaultName' accessible"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 1: Create BFF API Production App Registration
# ─────────────────────────────────────────────────────────────────────────────

$BffApiAppId = $null
$BffApiObjectId = $null

if (-not $SkipBffApi) {
    Write-Header "STEP 1: BFF API Production App Registration"

    # Check if app already exists
    $existingBffApp = az ad app list --display-name $BffApiDisplayName --output json 2>$null | ConvertFrom-Json
    if ($existingBffApp -and $existingBffApp.Count -gt 0) {
        Write-Warn "App registration '$BffApiDisplayName' already exists (AppId: $($existingBffApp[0].appId))"
        Write-Info "Skipping creation. Use Azure Portal to modify if needed."
        $BffApiAppId = $existingBffApp[0].appId
        $BffApiObjectId = $existingBffApp[0].id
    } else {
        Write-Step 1 "Creating app registration: $BffApiDisplayName"

        if ($DryRun) {
            Write-Info "DRY RUN: Would create app '$BffApiDisplayName' with:"
            Write-Info "  - Platform: Web"
            Write-Info "  - Redirect URIs: https://$ProductionApiDomain/.auth/login/aad/callback"
            Write-Info "  - Graph permissions: Files.ReadWrite.All, Sites.ReadWrite.All, User.Read, Mail.Send (delegated)"
            Write-Info "  - Dynamics CRM permissions: user_impersonation (delegated)"
            Write-Info "  - Exposed API scope: user_impersonation"
            $BffApiAppId = "00000000-0000-0000-0000-000000000000"
        } else {
            # Create the app registration with required resource access
            $requiredResourceAccess = @(
                @{
                    resourceAppId = $GraphApiId
                    resourceAccess = @(
                        @{ id = $GraphFilesReadWriteAll; type = "Scope" }
                        @{ id = $GraphSitesReadWriteAll; type = "Scope" }
                        @{ id = $GraphUserRead; type = "Scope" }
                        @{ id = $GraphMailSend; type = "Scope" }
                    )
                },
                @{
                    resourceAppId = $DynamicsCrmApiId
                    resourceAccess = @(
                        @{ id = $DynamicsCrmUserImpersonation; type = "Scope" }
                    )
                }
            ) | ConvertTo-Json -Depth 4 -Compress

            $appManifest = @{
                displayName = $BffApiDisplayName
                signInAudience = "AzureADMyOrg"
                web = @{
                    redirectUris = @(
                        "https://$ProductionApiDomain/.auth/login/aad/callback"
                    )
                    implicitGrantSettings = @{
                        enableAccessTokenIssuance = $false
                        enableIdTokenIssuance = $true
                    }
                }
                requiredResourceAccess = @(
                    @{
                        resourceAppId = $GraphApiId
                        resourceAccess = @(
                            @{ id = $GraphFilesReadWriteAll; type = "Scope" }
                            @{ id = $GraphSitesReadWriteAll; type = "Scope" }
                            @{ id = $GraphUserRead; type = "Scope" }
                            @{ id = $GraphMailSend; type = "Scope" }
                        )
                    },
                    @{
                        resourceAppId = $DynamicsCrmApiId
                        resourceAccess = @(
                            @{ id = $DynamicsCrmUserImpersonation; type = "Scope" }
                        )
                    }
                )
            } | ConvertTo-Json -Depth 5

            # Write manifest to temp file (Azure CLI has limits on inline JSON)
            $manifestPath = [System.IO.Path]::GetTempFileName()
            $appManifest | Out-File -FilePath $manifestPath -Encoding utf8

            $createdApp = az ad app create --display-name $BffApiDisplayName `
                --sign-in-audience AzureADMyOrg `
                --web-redirect-uris "https://$ProductionApiDomain/.auth/login/aad/callback" `
                --enable-id-token-issuance true `
                --output json 2>&1 | ConvertFrom-Json

            if ($LASTEXITCODE -ne 0 -or -not $createdApp) {
                throw "Failed to create app registration '$BffApiDisplayName'"
            }

            $BffApiAppId = $createdApp.appId
            $BffApiObjectId = $createdApp.id
            Write-Success "Created app: $BffApiDisplayName (AppId: $BffApiAppId)"

            # Add required resource access (Graph + Dynamics CRM)
            Write-Step 2 "Adding API permissions..."

            # Add Graph permissions
            az ad app permission add --id $BffApiAppId `
                --api $GraphApiId `
                --api-permissions "$($GraphFilesReadWriteAll)=Scope $($GraphSitesReadWriteAll)=Scope $($GraphUserRead)=Scope $($GraphMailSend)=Scope" `
                --output none 2>&1

            # Add Dynamics CRM permission
            az ad app permission add --id $BffApiAppId `
                --api $DynamicsCrmApiId `
                --api-permissions "$($DynamicsCrmUserImpersonation)=Scope" `
                --output none 2>&1

            Write-Success "API permissions added"

            # Clean up temp file
            Remove-Item $manifestPath -ErrorAction SilentlyContinue
        }
    }

    # Step 2: Configure Application ID URI and exposed scope
    Write-Step 3 "Configuring Application ID URI and exposed scope"

    if ($BffApiAppId -and -not $DryRun) {
        $appIdUri = "api://$BffApiAppId"

        # Set Application ID URI
        az ad app update --id $BffApiAppId `
            --identifier-uris $appIdUri `
            --output none 2>&1

        Write-Success "Application ID URI set: $appIdUri"

        # Add exposed API scope: user_impersonation
        # This requires the Microsoft Graph API to update the app manifest
        $scopeId = [guid]::NewGuid().ToString()
        $apiDefinition = @{
            oauth2PermissionScopes = @(
                @{
                    adminConsentDescription = "Allow the application to access $BffApiDisplayName on behalf of the signed-in user."
                    adminConsentDisplayName = "Access $BffApiDisplayName"
                    id = $scopeId
                    isEnabled = $true
                    type = "User"
                    userConsentDescription = "Allow the application to access $BffApiDisplayName on your behalf."
                    userConsentDisplayName = "Access $BffApiDisplayName"
                    value = "user_impersonation"
                }
            )
        } | ConvertTo-Json -Depth 4

        $apiPath = [System.IO.Path]::GetTempFileName()
        $apiDefinition | Out-File -FilePath $apiPath -Encoding utf8

        az rest --method PATCH `
            --uri "https://graph.microsoft.com/v1.0/applications/$BffApiObjectId" `
            --headers "Content-Type=application/json" `
            --body "@$apiPath" `
            --output none 2>&1

        Remove-Item $apiPath -ErrorAction SilentlyContinue
        Write-Success "Exposed API scope: $appIdUri/user_impersonation"
    } elseif ($DryRun) {
        Write-Info "DRY RUN: Would set Application ID URI: api://<app-id>"
        Write-Info "DRY RUN: Would expose scope: api://<app-id>/user_impersonation"
    }

    # Step 3: Generate client secret
    Write-Step 4 "Generating client secret (valid $SecretExpiryMonths months)"

    if (-not $DryRun -and $BffApiAppId) {
        $secretResult = az ad app credential reset `
            --id $BffApiAppId `
            --append `
            --display-name "Production-$(Get-Date -Format 'yyyyMMdd')" `
            --end-date $SecretExpiryDate `
            --output json 2>&1 | ConvertFrom-Json

        if ($LASTEXITCODE -ne 0 -or -not $secretResult) {
            throw "Failed to create client secret for '$BffApiDisplayName'"
        }

        $bffApiSecret = $secretResult.password
        Write-Success "Client secret created (prefix: $($bffApiSecret.Substring(0, 5))...)"
        Write-Warn "IMPORTANT: This secret is shown only once. Storing in Key Vault now."

        # Store in Key Vault
        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "BFF-API-ClientSecret" `
            -SecretValue $bffApiSecret `
            -Description "BFF API production client secret ($BffApiDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "BFF-API-ClientId" `
            -SecretValue $BffApiAppId `
            -Description "BFF API production client ID ($BffApiDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "BFF-API-Audience" `
            -SecretValue "api://$BffApiAppId" `
            -Description "BFF API production audience URI ($BffApiDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "TenantId" `
            -SecretValue $TenantId `
            -Description "Entra ID tenant ID"
    } else {
        Write-Info "DRY RUN: Would generate 24-month client secret"
        Write-Info "DRY RUN: Would store secrets in Key Vault:"
        Write-Info "  - BFF-API-ClientSecret"
        Write-Info "  - BFF-API-ClientId"
        Write-Info "  - BFF-API-Audience"
        Write-Info "  - TenantId"
    }

    # Step 4: Create service principal
    Write-Step 5 "Creating service principal"

    if (-not $DryRun -and $BffApiAppId) {
        $sp = az ad sp create --id $BffApiAppId --output json 2>$null | ConvertFrom-Json
        if ($sp) {
            Write-Success "Service principal created (ObjectId: $($sp.id))"
        } else {
            Write-Info "Service principal may already exist"
        }
    } else {
        Write-Info "DRY RUN: Would create service principal for $BffApiDisplayName"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# NOTE: The separate "Dataverse S2S" app registration (spaarke-dataverse-s2s-*)
# was removed 2026-08-14 (code-quality-and-assurance-r3 task 060). It had zero
# code consumers; Dataverse S2S access consolidated onto the BFF app registration
# credential (API_CLIENT_SECRET / BFF-API-ClientSecret) on 2026-01-07.
# ─────────────────────────────────────────────────────────────────────────────

# ─────────────────────────────────────────────────────────────────────────────
# Step 1b: Federated Identity Credential (MI-FIC) — task 030 / spec FR-C4
#
# Runs only when -CreateFederatedCredential (or -FicOnly) is passed, so every pre-existing
# invocation of this script reaches Step 2 exactly as it did before.
#
# Exit codes when this section runs:
#   0  credential exists and was verified by a real token exchange (or -AllowUnverified)
#   1  a fault — creation failed, drift refused, structurally invalid, or exchange rejected
#   2  credential exists and is structurally correct, but could NOT be exchange-verified from
#      this host. Distinct from 1 on purpose: provisioning needs to tell "not proven" from
#      "proven broken", because they call for different follow-ups.
# ─────────────────────────────────────────────────────────────────────────────

# Set when a FIC cannot be exchange-verified in COMBINED mode. Applied at the very end so the
# registration summary still prints. See the -FicOnly / else branch below.
$script:DeferredExitCode = 0

if ($CreateFederatedCredential) {
    Write-Header "FEDERATED IDENTITY CREDENTIAL (MI-FIC)"

    $ficAppId = if ($FederatedCredentialAppId) { $FederatedCredentialAppId } else { $BffApiAppId }

    if (-not $ficAppId) {
        throw "No app registration to federate. Pass -FederatedCredentialAppId, or run without -SkipBffApi so the app registration is created first."
    }
    if (-not $UamiResourceId) {
        throw "-UamiResourceId is required with -CreateFederatedCredential. Pass the ARM resource ID of the user-assigned managed identity (resource ID, never name)."
    }

    $ficResult = New-SpaarkeFederatedCredential `
        -AppId $ficAppId `
        -UamiResourceId $UamiResourceId `
        -TenantId $TenantId `
        -Name $FederatedCredentialName `
        -Audience $FederatedCredentialAudience `
        -AssertionToken $AssertionToken `
        -ExchangeScope $ExchangeScope `
        -PropagationRetrySeconds $PropagationRetrySeconds `
        -Force:$ForceFederatedCredentialUpdate `
        -WhatIfDryRun:$DryRun

    Write-Host ""
    Write-Host "  Federated Credential:" -ForegroundColor Green
    Write-Host "    Name:          $($ficResult.Name)" -ForegroundColor White
    Write-Host "    App:           $ficAppId" -ForegroundColor White
    Write-Host "    UAMI:          $($ficResult.Identity.Name)" -ForegroundColor White
    Write-Host "    Subject:       $($ficResult.Identity.PrincipalId)  (principalId)" -ForegroundColor White
    Write-Host "    Audience:      $FederatedCredentialAudience" -ForegroundColor White
    Write-Host "    Created:       $($ficResult.Created)" -ForegroundColor White
    Write-Host "    Pre-existing:  $($ficResult.AlreadyExisted)" -ForegroundColor White
    Write-Host "    Verification:  $($ficResult.Verification)" -ForegroundColor White
    Write-Host ""

    if ($DryRun) {
        # Note the asymmetry, which is deliberate: a dry run never CREATES, but it does still
        # verify a credential that already exists. A token exchange writes nothing, and
        # "does the credential I already have actually work?" is exactly what an operator
        # wants a dry run to answer.
        Write-Info "DRY RUN: nothing was created. Verification of a pre-existing credential, if any, was still performed."
    }
    elseif ($ficResult.Verification -ne "Verified") {
        if ($AllowUnverified) {
            Write-Warn "Credential is UNVERIFIED and -AllowUnverified was passed. Continuing, but this run is not evidence the credential works."
        }
        elseif ($FicOnly) {
            Write-Warn "Credential is UNVERIFIED. Exiting 2 rather than reporting success — see the guidance above for minting an assertion. Pass -AllowUnverified to override."
            exit 2
        }
        else {
            # Combined mode: Step 1 has ALREADY created an app registration, a 24-month client secret
            # and four Key Vault entries. Exiting here would skip the REGISTRATION SUMMARY, which is the
            # operator's only record that those side effects happened — and since exit 2 is the EXPECTED
            # result on any host that cannot mint an assertion (i.e. every workstation), that truncation
            # would be the normal case, not an edge case. Defer the code to the very end instead.
            Write-Warn "Credential is UNVERIFIED. The run will finish and print the registration summary, then exit 2. Pass -AllowUnverified to exit 0."
            $script:DeferredExitCode = 2
        }
    }
}

if ($FicOnly) {
    Write-Header "FEDERATED CREDENTIAL RUN COMPLETE"
    Write-Info "-FicOnly: app-registration, Key Vault and consent steps were not run."
    exit 0
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 2: Configure Known Client Applications (BFF API)
# ─────────────────────────────────────────────────────────────────────────────

if ($BffApiAppId -and $BffApiObjectId -and -not $DryRun) {
    Write-Header "STEP 2: Configure Known Client Applications"

    Write-Info "Known client applications will need to be configured after"
    Write-Info "production PCF and Code Page client app registrations are created."
    Write-Info ""
    Write-Info "To add known clients later, run:"
    Write-Info "  az rest --method PATCH --uri 'https://graph.microsoft.com/v1.0/applications/$BffApiObjectId'"
    Write-Info "    --body '{""api"":{""knownClientApplications"":[""<pcf-client-id>"",""<codepage-client-id>""]}}'"
} elseif ($DryRun) {
    Write-Header "STEP 2: Configure Known Client Applications"
    Write-Info "DRY RUN: Would configure knownClientApplications on BFF API app"
    Write-Info "  (Requires PCF and Code Page client app IDs — set after those are created)"
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 3: Admin Consent
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 3: Admin Consent (Manual Step)"

Write-Info "Admin consent MUST be granted for the BFF API app registration."
Write-Info "This requires a Global Administrator or Privileged Role Administrator."
Write-Info ""

if ($BffApiAppId) {
    Write-Info "BFF API ($BffApiDisplayName):"
    Write-Info "  Portal: https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$BffApiAppId"
    Write-Info "  CLI:    az ad app permission admin-consent --id $BffApiAppId"
    Write-Info ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 4: Dataverse Application User Registration
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 4: Dataverse Application User (Manual Step)"

Write-Info "After Dataverse environment is provisioned, register the BFF API app registration"
Write-Info "as an Application User with appropriate security roles."
Write-Info ""

if ($BffApiAppId) {
    Write-Info "BFF API Application User:"
    Write-Info "  App ID: $BffApiAppId"
    Write-Info "  Security Role: System Administrator (or custom role)"
    Write-Info "  Command: pac admin assign-app-to-environment --environment <env-url> --app $BffApiAppId"
    Write-Info ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "REGISTRATION SUMMARY"

if ($DryRun) {
    Write-Host "  *** DRY RUN — No changes were made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Host "  Tenant ID:       $TenantId" -ForegroundColor White
Write-Host "  Key Vault:       $KeyVaultName" -ForegroundColor White
Write-Host ""

if ($BffApiAppId) {
    Write-Host "  BFF API App Registration:" -ForegroundColor Green
    Write-Host "    Display Name:    $BffApiDisplayName" -ForegroundColor White
    Write-Host "    Application ID:  $BffApiAppId" -ForegroundColor White
    Write-Host "    App ID URI:      api://$BffApiAppId" -ForegroundColor White
    Write-Host "    Redirect URI:    https://$ProductionApiDomain/.auth/login/aad/callback" -ForegroundColor White
    Write-Host "    Permissions:     Graph (Files.RW.All, Sites.RW.All, User.Read, Mail.Send)" -ForegroundColor White
    Write-Host "                     Dynamics CRM (user_impersonation)" -ForegroundColor White
    Write-Host "    Exposed Scope:   api://$BffApiAppId/user_impersonation" -ForegroundColor White
    Write-Host "    KV Secrets:      BFF-API-ClientSecret, BFF-API-ClientId, BFF-API-Audience, TenantId" -ForegroundColor White
    Write-Host ""
}

Write-Host "  Key Vault Secrets Stored:" -ForegroundColor Yellow
Write-Host "    sprk-platform-prod-kv:" -ForegroundColor White
Write-Host "      - TenantId" -ForegroundColor Gray
Write-Host "      - BFF-API-ClientId" -ForegroundColor Gray
Write-Host "      - BFF-API-ClientSecret" -ForegroundColor Gray
Write-Host "      - BFF-API-Audience" -ForegroundColor Gray
Write-Host ""

Write-Host "  NEXT STEPS:" -ForegroundColor Cyan
Write-Host "    1. Grant admin consent (see Step 3 above)" -ForegroundColor White
Write-Host "    2. Register the Application User in Dataverse (see Step 4 above)" -ForegroundColor White
Write-Host "    3. Configure knownClientApplications when PCF/CodePage clients are created" -ForegroundColor White
Write-Host "    4. Run Test-EntraAppRegistrations.ps1 to verify token acquisition" -ForegroundColor White
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Deferred exit (MI-FIC combined mode) — see the FIC section for why this is not an inline exit.
# ─────────────────────────────────────────────────────────────────────────────
if ($script:DeferredExitCode -ne 0) {
    Write-Warn "Exiting $($script:DeferredExitCode): the federated credential is structurally correct but was not verified by a token exchange from this host."
    exit $script:DeferredExitCode
}
