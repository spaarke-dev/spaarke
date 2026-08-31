#Requires -Version 7.0
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
    Entra ID tenant ID. REQUIRED — there is deliberately no default.

    A hardcoded Spaarke-tenant default lived here until 2026-08-26. Running the script for a
    customer without -TenantId would have created that customer's app registration in the SPAARKE
    tenant, giving Spaarke users access to the customer's Dataverse environment. That is the
    cross-tenant identity leak tenant-isolation invariant I1 exists to prevent
    (design.md §4D I1 / spec FR-28); it was specified as removed in design v3.3 and the code change
    was never applied. Enforced by Spaarke.ArchTests.TenantIsolation.I1_NoHardcodedTenantTests.

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

.NOTES
    Project: production-environment-setup-r1
    Task: 021 — Create Entra ID app registrations
    Naming: FR-11 compliant (spaarke- prefix)
    Secrets: FR-08 compliant (Key Vault only)
#>

param(
    # No default: see .PARAMETER TenantId above and §4D I1. Mandatory, so an omitted -TenantId
    # stops and asks rather than silently provisioning into whichever tenant was hardcoded.
    [Parameter(Mandatory = $true)][string]$TenantId,
    [string]$KeyVaultName = "sprk-platform-prod-kv",
    [string]$ProductionApiDomain = "api.spaarke.com",
    [string]$DataverseOrgUrl = "",
    [switch]$DryRun,
    [switch]$SkipBffApi,

    # ── Federated identity credential (MI-FIC) — added 2026-08-21, task 030 / spec FR-C4 ──
    # All inert by default. Existing invocations behave exactly as before.
    [switch]$CreateFederatedCredential,
    [switch]$FicOnly,
    [string]$UamiResourceId = "",
    [string]$FederatedCredentialAppId = "",
    [string]$FederatedCredentialName = "",
    [string]$FederatedCredentialAudience = "api://AzureADTokenExchange",
    [string]$AssertionToken = "",
    [string]$ExchangeScope = "https://graph.microsoft.com/.default",
    [ValidateRange(0, 3600)][int]$PropagationRetrySeconds = 600,
    [switch]$ForceFederatedCredentialUpdate,
    [switch]$AllowUnverified,

    # ── Secret-free registration — added 2026-08-24, task 033 / ADR-028 A4 ──
    # Suppresses the 24-month client-secret mint in Step 3 and the Key Vault write that follows it.
    #
    # Why this exists: before this switch, COMBINED mode (-CreateFederatedCredential WITHOUT -FicOnly)
    # minted a client secret unconditionally. After task 033 removed the BFF-identity secret, that would
    # have re-minted a per-customer secret on every customer-provisioning onboarding — which ADR-028
    # exception E-3 explicitly does not license ("E-3 is transitional and does not license expansion").
    #
    # Bucket B HIGH#4 update (customer-provisioning-orchestration-r1 SESSION 18, adversarial e2e verify
    # workflow wepdcb8we): the SESSION 18 constraint (.claude/constraints/provisioning.md § KV credential
    # lifecycle rule 1) closed the "silent absence = mint" branch — see the AllowClientSecretMint param
    # below. -SkipClientSecret is now REDUNDANT with the default (both go to the safe branch), but
    # remains for backward-compat scripting and for making operator intent explicit. Passing -SkipClientSecret
    # AND -AllowClientSecretMint together is contradictory and throws.
    [switch]$SkipClientSecret,

    # ── Bucket B HIGH#4 opt-in (customer-provisioning-orchestration-r1 SESSION 18) ──
    # Explicit opt-in to mint a NEW BFF-API-ClientSecret + write it to Key Vault. Required to reach the
    # mint branch — silent absence now defaults to skip (the SESSION 18 flip). Because auth-v4 task 033
    # (2026-08-24) DELETED both KV copies of BFF-API-ClientSecret and pinned Graph:Credentials:Order to
    # [ManagedIdentityFederated] with RequireSecretFreeIdentity=true, the ONLY legitimate reason to mint
    # is the prong-3-unmigrated exception (constraint doc rule 3): an environment still carrying
    # ClientSecret in its live credential order that has not yet cut over to FIC. That case requires a
    # documented -MintReason.
    [switch]$AllowClientSecretMint,

    # Free-form audit string. REQUIRED when -AllowClientSecretMint is passed. Recorded in the KV secret's
    # ContentType tag for post-hoc audit (which operator on which date opted into the prong-3 mint
    # exception, referencing which decision doc).
    [string]$MintReason = "",

    # ── SPE topology app-registrations — added 2026-08-30, task 213.4 ──
    # Creates the container-type OWNING app-reg for the named tier per
    # SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md §3A rows 1-3. Owning apps are permanent-1:1
    # with their container-type (topology §R1) — this switch is used ONCE per tier
    # during the operator's one-time SPE topology setup (runbook Step 1). Model 2's
    # owning app is the ONLY multi-tenant app-reg in Spaarke's topology (§3A row 3);
    # all other owning apps are single-tenant. When set: skips the prod BFF-API
    # flow (implicit -SkipBffApi) + skips secret minting (topology apps are secret-free
    # per ADR-028 A4). NOT combinable with -CreateFederatedCredential / -FicOnly.
    [ValidateSet('Trial1','Model1','Model2')]
    [string]$CreateOwningApp = "",

    # ── SPE topology BFF app-registrations — added 2026-08-30, task 213.4 ──
    # Creates the SHARED BFF app-reg for the named tier per topology doc §3A rows 4-5.
    # For Model 2 per-customer BFFs (§3A row 6, "Spaarke BFF — {Customer}"), pass
    # -CreateBffApp Model2 -CustomerName {name} to override the display name. All
    # BFF app-regs are single-tenant (`AzureADMyOrg`) per project CLAUDE.md §MUST rule
    # + topology doc §3A rows 4-6. Container access is granted separately at runbook
    # Step 6 (registration-level `applicationPermissionGrants` on the container-type
    # registration) — NOT via declared app-reg permissions. See topology doc §3A
    # "How a BFF gets container access without owning anything — VERIFIED".
    [ValidateSet('Trial1','Model1','Model2')]
    [string]$CreateBffApp = "",

    # Per-customer name override for Model 2 BFF app-regs. When passed with
    # -CreateBffApp Model2, the display name becomes "Spaarke BFF — {CustomerName}"
    # (topology doc §3A row 6). Ignored for Trial1 / Model1 (which use fixed
    # shared display names).
    [string]$CustomerName = ""
)

$ErrorActionPreference = "Stop"

# Supplying FIC parameters without the mode switch used to run the FULL app-registration path --
# creating an app registration, minting a 24-month client secret and writing four Key Vault
# entries -- then exit 0 with a summary that reads like success, having never created the FIC.
# Forgetting the switch is a plausible typo, so refuse rather than silently do something else.
$ficArgsSupplied = @($UamiResourceId, $FederatedCredentialAppId, $FederatedCredentialName, $AssertionToken) |
    Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if (($ficArgsSupplied -gt 0 -or $ForceFederatedCredentialUpdate -or $AllowUnverified) -and
    -not ($CreateFederatedCredential -or $FicOnly)) {
    throw "Federated-credential parameters were supplied without -CreateFederatedCredential or -FicOnly. Refusing to run the full app-registration path (which would mint a client secret and write to Key Vault) when a FIC run was clearly intended."
}

# -FicOnly is a mode, not an extra step: it turns this into a federated-credential-only run.
if ($FicOnly) {
    $SkipBffApi = $true
    $CreateFederatedCredential = $true
}

# ─────────────────────────────────────────────────────────────────────────────
# SPE topology mode (customer-provisioning-orchestration-r1 task 213.4, 2026-08-30) —
# -CreateOwningApp / -CreateBffApp create the topology app-regs per
# SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md §3A. Bypasses the prod BFF-API flow
# and forces secret-free (topology apps never receive minted secrets from this
# script — ADR-028 A4 for BFF identities; E-1 owning-app secrets, if ever
# needed, are managed via a separate operator flow, not auto-minted here).
# ─────────────────────────────────────────────────────────────────────────────
$TopologyMode = ([string]::IsNullOrEmpty($CreateOwningApp) -eq $false) -or `
                ([string]::IsNullOrEmpty($CreateBffApp) -eq $false)

if ($TopologyMode) {
    if ($CreateFederatedCredential -or $FicOnly) {
        throw "-CreateOwningApp / -CreateBffApp cannot be combined with -CreateFederatedCredential or -FicOnly in the same invocation. Run the script twice if both actions are needed: once to create the topology app-reg, again to add a FIC to it (passing -FederatedCredentialAppId with the newly-created app's ID)."
    }
    if ($AllowClientSecretMint) {
        throw "-AllowClientSecretMint cannot be combined with -CreateOwningApp / -CreateBffApp. Topology app-regs are created secret-free per ADR-028 A4 (BFF identities) + KV credential-lifecycle rules 1-2 (.claude/constraints/provisioning.md § KV credential lifecycle). E-1 container-type owning-app secrets, if ever required, are managed via a separate operator flow."
    }
    # Cannot pass both bare -CreateBffApp Model2 without a customer name AND expect the shared
    # display name — topology doc §3A row 6 explicitly says Model 2 BFFs are per-customer.
    # Bare `-CreateBffApp Model2` (no -CustomerName) creates a shared placeholder
    # `Spaarke BFF — Model 2`; this is fine as a one-time setup, but flag it so the
    # operator explicitly opts into it vs. per-customer.
    if ($CreateBffApp -eq "Model2" -and [string]::IsNullOrWhiteSpace($CustomerName)) {
        Write-Host ""
        Write-Host "  [!!] -CreateBffApp Model2 without -CustomerName will create the shared display name" -ForegroundColor DarkYellow
        Write-Host "       'Spaarke BFF - Model 2'. Per topology doc SS3A row 6, Model 2 BFFs are" -ForegroundColor DarkYellow
        Write-Host "       normally per-customer: 'Spaarke BFF - {Customer}'. If you meant" -ForegroundColor DarkYellow
        Write-Host "       per-customer, cancel now and pass -CustomerName '<name>'." -ForegroundColor DarkYellow
        Write-Host ""
    }
    $SkipBffApi = $true          # implicit — skip the prod BFF-API flow
    $SkipClientSecret = $true    # forced — topology apps are secret-free
}

# ─────────────────────────────────────────────────────────────────────────────
# Bucket B HIGH#4 (customer-provisioning-orchestration-r1 SESSION 18) —
# BFF-API-ClientSecret mint gate. Silent absence of BOTH flags = skip
# (the SESSION 18 default flip). Explicit -AllowClientSecretMint requires
# -MintReason. -SkipClientSecret + -AllowClientSecretMint is contradictory.
# See .claude/constraints/provisioning.md § KV credential lifecycle rule 1.
# ─────────────────────────────────────────────────────────────────────────────
if ($AllowClientSecretMint -and $SkipClientSecret) {
    throw "Contradictory: -SkipClientSecret and -AllowClientSecretMint cannot both be passed. Pick one. Silent absence of both = skip (safe default per Bucket B HIGH#4 SESSION 18)."
}
if ($AllowClientSecretMint -and [string]::IsNullOrWhiteSpace($MintReason)) {
    throw "-AllowClientSecretMint requires -MintReason '<audit string>'. Reason is recorded in KV secret tags for post-hoc audit. Example: -MintReason 'prong-3-unmigrated customer per ADR-028 A4 exception; documented at projects/xxx/notes/rollback-2026-09-15.md'."
}
if (-not $AllowClientSecretMint) {
    # Bucket B HIGH#4 SESSION 18: silent absence of -AllowClientSecretMint FORCES skip-mint.
    # This closes the pre-2026-08-27 default-mint window that reintroduced BFF-API-ClientSecret on
    # every customer onboarding after auth-v4 task 033 (2026-08-24) deleted both KV copies.
    if (-not $SkipClientSecret) {
        Write-Host "[Bucket B HIGH#4 SESSION 18] -AllowClientSecretMint not passed — forcing -SkipClientSecret (safe default per .claude/constraints/provisioning.md § KV credential lifecycle rule 1). To mint, pass -AllowClientSecretMint -MintReason '<audit string>'." -ForegroundColor Yellow
    }
    $SkipClientSecret = $true
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

# Microsoft Graph — SharePoint Embedded APPLICATION (app-only) role.
# Used by the container-type OWNING app (topology doc SS4 prerequisites) — added 2026-08-30
# task 213.4. `FileStorageContainerTypeReg.Selected` is deliberately NOT hardcoded here:
# it lives on a non-Graph API surface in some tenants and the operator adds it manually
# per SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md Step 1 fallback.
$GraphFileStorageContainerSelected = "085ca537-6565-41c2-aca7-db852babc212"  # FileStorageContainer.Selected (Application)

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

# Error codes that mean "the credential is fine, the directory has not caught up yet", as NUMBERS
# matched exactly against Entra's structured `error_codes` array.
#
# 70025 IS HERE BECAUSE IT WAS MEASURED, NOT BECAUSE IT WAS DOCUMENTED. On 2026-08-21 a FIC was
# deleted and recreated on a throwaway app registration while a container carrying the UAMI polled
# the token endpoint every 2 s. The propagation window produced **AADSTS70025**, and did so
# INTERMITTENTLY — eight failures scattered across ~130 s, flapping between success and failure as
# Entra replicas converged, not a clean fail-then-succeed. AADSTS70021 — the code this project's
# notes, this file's comments and the task's own acceptance criterion all named as "the propagation
# code" — was **never observed**. Had the list stayed at 70021 alone, a genuine post-creation
# propagation failure would have been classified as a credential fault and failed fast: the exact
# opposite of the required behaviour. 70021 is retained because Microsoft documents it and this is
# one tenant's observation, not a proof of the complete set.
#
# ⚠️ NUMBERS, MATCHED EXACTLY, FOR A REASON. An earlier version held these as the STRING
# "AADSTS70021" and tested with `-match` — a regex SUBSTRING test. That matched AADSTS700211
# (unrecognised issuer) and, worse, AADSTS700213, which is what a wrong SUBJECT actually returns
# (also measured 2026-08-21). The single most likely misconfiguration in this whole mechanism was
# therefore being retried for the full budget and then reported as "ruled out". Caught at task 030's
# code-review gate. Do not reintroduce substring matching here.
#
# Explicitly NOT retried: 700211 (unrecognised issuer), 700213 (no FIC matches the assertion's
# subject), 7000215 (invalid secret) — all configuration faults that retrying only delays.
$script:PropagationErrorCodes = @(70021, 70025)

# OAuth2 `error` values that mean the CLIENT CREDENTIAL itself was rejected.
$script:CredentialLayerErrors = @("invalid_client", "unauthorized_client")

# OAuth2 `error` values that mean the credential was ACCEPTED and Entra then objected to the
# requested resource/scope. Entra evaluates the resource only after accepting the credential,
# so these are positive evidence the FIC works. Classifying on this field rather than on a
# list of AADSTS numbers is what keeps verification working on a freshly provisioned app
# registration whose grants do not exist yet.
$script:AuthorizationLayerErrors = @("invalid_scope", "invalid_resource", "invalid_target", "access_denied", "insufficient_scope")

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
    # Throw on a FAILED read, exactly as Get-SpaarkeFederatedCredential does. Returning $null here
    # would report "no equivalent" on a throttle or token blip, degrade idempotency to name-based
    # for that run, and surface the resulting Entra uniqueness rejection as a permissions error.
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list federated credentials on app registration '$AppId' while checking for an equivalent credential. This is a read failure, not evidence that none exists -- not proceeding to create."
    }
    if (-not $all) { return $null }

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
        THIS IS NOT DECORATION AND IT IS NOT OPTIONAL, for three reasons — none of which is the
        reason an earlier version of this comment gave.

        1. IT IS THE ONLY VERIFICATION AVAILABLE OFF-AZURE. A managed-identity assertion can only
           be minted from inside Azure on compute carrying the identity, so on a workstation the
           token exchange cannot run at all. Without this check, a workstation run would have no
           verification whatsoever — only "create returned success", which proves nothing.

        2. IT NAMES THE FAULT. The exchange can tell you the credential was rejected; only this
           check can tell you the subject was set to the UAMI's clientId instead of its
           principalId, which is the specific mistake people actually make.

        3. IT DOES NOT DEPEND ON ENTRA'S ERROR CODES. Those are undocumented implementation
           detail and they are NOT what this project's own notes assumed — see below.

        ⚠️ CORRECTION, measured 2026-08-21 against the live tenant. An earlier version of this
        comment claimed a wrong subject and ordinary propagation both surface as AADSTS70021 —
        "identical symptom" — and that breaking that tie was this function's purpose. That is
        FALSE. A FIC whose subject is the clientId returns **AADSTS700213** (`invalid_client`),
        not AADSTS70021; verified by building exactly that credential on a throwaway app
        registration and exchanging against it from a container carrying the UAMI. The two cases
        are distinguishable at the exchange after all. The ordering below is still correct, for
        the three reasons above — but not for the reason originally given.
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

        RETRY. Propagation-class codes are retried because immediately after creation the directory
        has not converged. MEASURED 2026-08-21 rather than assumed: the real window produces
        **AADSTS70025**, INTERMITTENTLY, for roughly two minutes — it flaps between success and
        failure as Entra replicas catch up, so a single failure right after create means nothing and
        a single success does not prove the window has closed. AADSTS70021 was never observed despite
        being what every note in this project called "the propagation code"; it is retained on
        Microsoft's documentation, not on evidence.

        Do not widen $script:PropagationErrorCodes casually — 700211 (unrecognised issuer) and 700213
        (no FIC matches the subject) are configuration faults, and 700213 in particular is the
        wrong-subject signature that must fail fast. Widen it only on the basis 70025 was added: an
        observation, recorded.
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
            # -TimeoutSec is explicit: without it a blackholed endpoint adds the ~100 s default to
            # EVERY attempt, silently blowing past the caller's stated budget.
            $null = Invoke-RestMethod -Method Post -Uri $tokenUri -Body $body `
                -ContentType "application/x-www-form-urlencoded" -TimeoutSec 30 -ErrorAction Stop

            return [pscustomobject]@{
                Accepted = $true
                Attempts = $attempt
                Detail   = "Entra issued a token for scope '$Scope'. The federated credential is valid and working."
            }
        }
        catch {
            # Capture the outer error first -- inside the nested catch below, $_ is rebound to the
            # INNER failure, which would replace Entra's real response with a PowerShell message
            # about a missing method and get that classified as a credential rejection.
            $outer = $_
            if ($outer.ErrorDetails -and $outer.ErrorDetails.Message) {
                $errorBody = $outer.ErrorDetails.Message
            }
            elseif ($outer.Exception.Response -is [System.Net.HttpWebResponse]) {
                try {
                    $reader = New-Object System.IO.StreamReader($outer.Exception.Response.GetResponseStream())
                    $errorBody = $reader.ReadToEnd()
                    $reader.Dispose()
                }
                catch { $errorBody = $outer.Exception.Message }
            }
            else {
                $errorBody = $outer.Exception.Message
            }
        }

        # Parse ONCE and classify on structure. Entra returns a JSON body carrying `error` and a
        # numeric `error_codes` array; both are exact where substring matching is not.
        $parsed = $null
        try { $parsed = $errorBody | ConvertFrom-Json -ErrorAction Stop } catch { $parsed = $null }
        $errorCodes = if ($parsed -and $parsed.error_codes) { @($parsed.error_codes) } else { @() }
        $oauthError = if ($parsed) { $parsed.error } else { $null }

        $isPropagation = $false
        foreach ($code in $script:PropagationErrorCodes) {
            if ($errorCodes -contains $code) { $isPropagation = $true; break }
        }
        # Fallback for a non-JSON body (proxy error page, CLI wrapper text). The negative lookahead
        # is what stops AADSTS700211 from masquerading as AADSTS70021 — see the declaration comment.
        if (-not $isPropagation -and -not $parsed) {
            foreach ($code in $script:PropagationErrorCodes) {
                if ($errorBody -match "AADSTS$code(?![0-9])") { $isPropagation = $true; break }
            }
        }

        if (-not $isPropagation) {
            if ($oauthError -and ($script:AuthorizationLayerErrors -contains $oauthError)) {
                return [pscustomobject]@{
                    Accepted = $true
                    Attempts = $attempt
                    Detail   = "Assertion ACCEPTED. Entra accepted the client credential and then rejected the requested scope '$Scope' (OAuth2 error '$oauthError'), which it evaluates only afterwards. The federated credential itself is valid; the app registration simply lacks that grant."
                }
            }
            if ($errorCodes -contains 500011) {
                return [pscustomobject]@{
                    Accepted = $true
                    Attempts = $attempt
                    Detail   = "Assertion ACCEPTED. Entra rejected the requested scope '$Scope' (AADSTS500011: resource principal not found in tenant), which it evaluates only after accepting the client credential. The federated credential itself is valid."
                }
            }

            # AADSTS700213 is what Entra actually returns when no federated credential on this app
            # matches the assertion's subject — the wrong-subject case. Named explicitly because the
            # generic "credential rejected" message sends an operator looking in the wrong place.
            $layer = if ($errorCodes -contains 700213) {
                "AADSTS700213: no federated credential on this app matches the assertion's SUBJECT. The assertion was minted by a different identity than the one this credential trusts, or the credential's subject is not this UAMI's principalId."
            }
            elseif ($oauthError -and ($script:CredentialLayerErrors -contains $oauthError)) {
                "The OAuth2 error '$oauthError' means the credential itself was rejected."
            } else {
                "The OAuth2 error '$oauthError' is not a known propagation or authorization-layer code, so it is treated as a credential fault."
            }
            return [pscustomobject]@{
                Accepted = $false
                Attempts = $attempt
                Detail   = "Entra REJECTED the assertion. $layer This is not propagation — retrying would only delay the report.`n$errorBody"
            }
        }

        $elapsed = ((Get-Date) - $started).TotalSeconds
        if ($elapsed + $delay -gt $MaxWaitSeconds) {
            $timeoutDetail = @"
A propagation-class error (AADSTS70021 / 70025) persisted for $([int]$elapsed)s across $attempt attempt(s) (limit ${MaxWaitSeconds}s).

This is the propagation code specifically, and the structural check already confirmed issuer,
subject and audience match this UAMI. (A subject set to the clientId surfaces as AADSTS700213,
not this code — measured 2026-08-21 — so that mistake is doubly ruled out here.)
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

        Write-Info "Propagation-class error (codes: $($errorCodes -join ',')) — attempt $attempt, retrying in ${delay}s (elapsed $([int]$elapsed)s of ${MaxWaitSeconds}s)..."
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
        [ValidateRange(0, 3600)][int]$PropagationRetrySeconds = 600,
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
                # Report the DESIRED shape, mirroring the dry-run create branch. Reporting $existing
                # (the drifted object) would flow into the unconditional structural check below and
                # hard-fail the preview of an operation that would actually have succeeded.
                Write-Info "DRY RUN: would update federated credential '$Name' to subject $($identity.PrincipalId)"
                $result.Credential = [pscustomobject]$desired
                $result.StructurallyValid = $true
                $result.Verification = "Unverified"
                Write-Info "DRY RUN: nothing was changed and no exchange was attempted."
                return $result
            }
            else {
                $paramPath = [System.IO.Path]::GetTempFileName()
                try {
                    ($desired | ConvertTo-Json -Depth 4) | Out-File -FilePath $paramPath -Encoding utf8
                    # Capture az's own stderr rather than discarding it — the CLI's message is the
                    # only thing that distinguishes a permissions failure from a malformed payload,
                    # and a caller debugging a provisioning run needs to see it verbatim.
                    $azOutput = az ad app federated-credential update --id $AppId `
                        --federated-credential-id $existing.id --parameters "@$paramPath" 2>&1
                    $updateExit = $LASTEXITCODE
                }
                finally { Remove-Item $paramPath -ErrorAction SilentlyContinue }
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
        try {
            ($desired | ConvertTo-Json -Depth 4) | Out-File -FilePath $paramPath -Encoding utf8
            # Capture az's own stderr rather than discarding it. "Requires Application Administrator"
            # is the LIKELIEST cause but not the only one — a malformed payload, a missing app, or a
            # duplicate (issuer, subject) pair all land here too, and guessing on the caller's behalf
            # is what makes a provisioning failure take an afternoon instead of a minute.
            $azOutput = az ad app federated-credential create --id $AppId --parameters "@$paramPath" 2>&1
            $createExit = $LASTEXITCODE
        }
        finally { Remove-Item $paramPath -ErrorAction SilentlyContinue }

        if ($createExit -ne 0) {
            throw "Failed to create federated credential '$Name' on app '$AppId'. Creating a FIC usually requires Application Administrator (or ownership of the app registration).`nAzure CLI reported:`n$($azOutput -join [Environment]::NewLine)"
        }

        Write-Success "Federated credential '$Name' created."
        Write-Warn "Creation success proves NOTHING about whether it works — a misconfigured FIC creates cleanly. Verifying."
        $result.Created = $true
        $result.Credential = Get-SpaarkeFederatedCredential -AppId $AppId -Name $Name
        if (-not $result.Credential) {
            # Graph directory reads are eventually consistent, so a just-created credential can be
            # missing from the immediate read-back. Say that, rather than letting $null flow into
            # the structural check and produce a parameter-binding error that mentions no FIC at all.
            throw "Federated credential '$Name' was created successfully but is not yet readable back from the directory (Graph reads are eventually consistent). Nothing is wrong with the credential — re-run this command in a few seconds to verify it."
        }
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

  az login --identity --client-id $($identity.ClientId)
  az account get-access-token --resource $Audience --query accessToken -o tsv

then re-run with -AssertionToken <token>. Nothing consumes this credential until the provider
seam ships, so an unverified credential is not a live risk — but it is also not evidence that
anything works.
"@
        Write-Warn $unverifiedMsg
        return $result
    }

    # `az ad app federated-credential --id` accepts the appId OR the object ID, and this function's
    # contract advertises both. The TOKEN endpoint accepts the appId only. Passing an object ID
    # straight through as client_id returns AADSTS700016 ("application not found"), which is not a
    # propagation code — so a perfectly good credential would be declared broken, and the operator's
    # natural next move would be -ForceFederatedCredentialUpdate against something needing no repair.
    $appClientId = az ad app show --id $AppId --query appId -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $appClientId) {
        Write-Warn "Could not resolve '$AppId' to an application (client) ID; using it as-is for the exchange."
        $appClientId = $AppId
    }
    elseif ($appClientId -ne $AppId) {
        Write-Info "Resolved app object ID '$AppId' to application (client) ID '$appClientId' for the token exchange."
    }

    $exchange = Test-SpaarkeFicTokenExchange -TenantId $TenantId -AppClientId $appClientId `
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

# WARNING: THERE IS DELIBERATELY NO DOT-SOURCE / "export functions" MODE HERE (removed at task
# 030's code-review gate). Dot-sourcing a script that carries a param() block executes that block
# in the CALLER's scope, which silently overwrote the consumer's own $TenantId with this script's
# hard-coded production default -- and for federated-credential work a wrong tenant is a wrong
# issuer, i.e. a credential that creates cleanly and never works. It also flipped the caller's
# $ErrorActionPreference to Stop and replaced any same-named Write-* helpers they had.
#
# Consumers integrate by INVOKING this script with -FicOnly, which runs in its own child scope and
# leaks nothing. See the exit-code contract in the FIC section below.
#
# If an in-process import contract is ever genuinely needed, the correct form is a real module
# (scripts/lib/SpaarkeFic.psm1 + Import-Module) -- note that the Write-* output helpers would have
# to move with it, which is why it was not done inline here.

# ─────────────────────────────────────────────────────────────────────────────
# SPE Topology App Registration functions — task 213.4 (2026-08-30)
#
# Creates the 6 app-regs per SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md SS3A:
#   Rows 1-3: OWNING apps (permanent-1:1 with a container-type; SS3A row 3 Model 2 = multi-tenant)
#   Rows 4-6: BFF apps    (shared per tier for Trial 1 + Model 1; per-customer for Model 2)
#
# ORTHOGONAL to the FIC helpers above — these do not participate in the FIC flow.
# See docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md for the operator workflow
# that wraps these functions (Step 1 = New-SpaarkeSpeContainerTypeOwningApp,
# Step 6 = New-SpaarkeSpeBffApp).
# ─────────────────────────────────────────────────────────────────────────────

function Get-SpeTopologyOwningAppDisplayName {
    <#
    .SYNOPSIS
        Returns the exact display name for the container-type owning app-reg of a given tier,
        per SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md SS3A rows 1-3.
    .DESCRIPTION
        Display names are load-bearing here — they are what makes idempotency work
        (the script's existence-check is by display name) AND what the operator sees in
        the Entra portal. Do NOT abbreviate or change casing: 'Spaarke SPE Trial 1 Owner'
        MUST match topology doc SS3A row 1 verbatim.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateSet('Trial1','Model1','Model2')][string]$Tier)

    switch ($Tier) {
        'Trial1' { return "Spaarke SPE Trial 1 Owner" }
        'Model1' { return "Spaarke SPE Model 1 Owner" }
        'Model2' { return "Spaarke SPE Model 2 Owner" }
    }
}

function Get-SpeTopologyBffAppDisplayName {
    <#
    .SYNOPSIS
        Returns the exact display name for the BFF app-reg of a given tier, per topology
        doc SS3A rows 4-6.
    .DESCRIPTION
        Trial 1 and Model 1 use fixed shared names (rows 4-5). Model 2 defaults to the
        shared 'Spaarke BFF - Model 2' placeholder BUT topology doc SS3A row 6 says
        Model 2 is per-customer 'Spaarke BFF - {Customer}' — pass -CustomerName to
        opt into the per-customer name.

        Uses ASCII hyphen '-' (not em dash) to keep display names shell-safe across
        PowerShell / az CLI / portal rendering; topology doc SS3A shows em dashes for
        visual readability but the actual Entra display name uses ASCII throughout the
        codebase.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Trial1','Model1','Model2')][string]$Tier,
        [string]$CustomerName = ""
    )

    switch ($Tier) {
        'Trial1' { return "Spaarke BFF - Trial 1" }
        'Model1' { return "Spaarke BFF - Model 1" }
        'Model2' {
            if (-not [string]::IsNullOrWhiteSpace($CustomerName)) {
                return "Spaarke BFF - $CustomerName"
            }
            return "Spaarke BFF - Model 2"
        }
    }
}

function Get-SpeTopologyOwningAppSignInAudience {
    <#
    .SYNOPSIS
        Returns the required signInAudience for a container-type owning app-reg.
    .DESCRIPTION
        Topology doc SS3A row 3 + project CLAUDE.md SS MUST rule: Model 2's owning app is
        the ONLY multi-tenant app-reg in Spaarke's entire topology (customer admins in
        their tenants grant admin consent to it). Trial 1 + Model 1 owning apps are
        single-tenant because their container-types host containers only in the
        Spaarke tenant. Getting this wrong for Model 2 breaks the consent surface;
        getting it wrong for Trial 1 / Model 1 unnecessarily exposes the owning app
        to cross-tenant consent.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateSet('Trial1','Model1','Model2')][string]$Tier)

    if ($Tier -eq 'Model2') { return 'AzureADMultipleOrgs' }
    return 'AzureADMyOrg'
}

function New-SpaarkeSpeContainerTypeOwningApp {
    <#
    .SYNOPSIS
        Idempotently creates the container-type OWNING app-reg for a given tier
        (topology doc SS3A rows 1-3).
    .DESCRIPTION
        Creation order:
          1. Idempotency check by display name — skip if exists (returns existing appId).
          2. Create app-reg with correct signInAudience per tier (Model 2 = multi-tenant).
          3. Add Graph 'FileStorageContainer.Selected' (Application) role — known-safe GUID.
          4. Emit operator-actionable message for 'FileStorageContainerTypeReg.Selected'
             (not auto-added — API surface varies by tenant; runbook Step 1 fallback covers it).
          5. Create service principal.
          6. Print next-steps: admin consent + container-type creation via delegated flow.

        DELIBERATELY NOT DONE by this function (per constraints):
          - No client secret minted (KV credential-lifecycle rule 1 + ADR-028 A4).
          - No FIC added (topology owning apps are secret-free by default; if E-1 secrets
            are ever needed, they are managed via a separate operator flow).
          - No KV secret writes (that's H4's job during per-customer provisioning).
          - No AppId URI set (owning apps don't expose scopes; they are consumed).
          - No admin consent granted (delegated-only per topology SS4 + Portal blue button).

    .OUTPUTS
        PSCustomObject: AppId, ObjectId, DisplayName, SignInAudience, AlreadyExisted, Tier.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Trial1','Model1','Model2')][string]$Tier,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [switch]$DryRun
    )

    $displayName    = Get-SpeTopologyOwningAppDisplayName -Tier $Tier
    $signInAudience = Get-SpeTopologyOwningAppSignInAudience -Tier $Tier

    Write-Header "SPE OWNING APP — $displayName (topology doc SS3A row $(switch ($Tier) { 'Trial1' {1}; 'Model1' {2}; 'Model2' {3} }))"
    Write-Info "Tier            : $Tier"
    Write-Info "signInAudience  : $signInAudience$(if ($Tier -eq 'Model2') { '  <- MULTI-TENANT (only Model 2 owning app is multi-tenant per SS3A row 3)' })"
    Write-Info "Tenant          : $TenantId"

    $result = [pscustomobject]@{
        AppId          = $null
        ObjectId       = $null
        DisplayName    = $displayName
        SignInAudience = $signInAudience
        AlreadyExisted = $false
        Tier           = $Tier
        Role           = 'Owning'
    }

    # 1. Idempotency — check by display name (same pattern as spaarke-bff-api-prod flow)
    Write-Step 1 "Checking whether '$displayName' already exists"
    $existing = az ad app list --display-name $displayName --output json 2>$null | ConvertFrom-Json
    if ($existing -and $existing.Count -gt 0) {
        Write-Warn "App registration '$displayName' already exists (AppId: $($existing[0].appId))"
        Write-Info "Skipping creation — this is idempotent. To modify, use the Portal or Graph directly."
        $result.AppId          = $existing[0].appId
        $result.ObjectId       = $existing[0].id
        $result.AlreadyExisted = $true

        # Warn on signInAudience drift (deliberate — do NOT auto-remediate; this indicates
        # the operator or a prior run created the app-reg with a different audience,
        # which for Model 2 in particular is a security-relevant setting).
        if ($existing[0].signInAudience -ne $signInAudience) {
            Write-Warn "signInAudience DRIFT: existing='$($existing[0].signInAudience)' expected='$signInAudience'."
            Write-Warn "This may indicate the app-reg was created by a different flow. Verify manually."
            if ($Tier -eq 'Model2' -and $existing[0].signInAudience -ne 'AzureADMultipleOrgs') {
                Write-Warn "  Model 2 owning app MUST be multi-tenant. Fix via Portal -> Manifest -> signInAudience = 'AzureADMultipleOrgs'."
            }
        }
        return $result
    }

    if ($DryRun) {
        Write-Info "DRY RUN: Would create app '$displayName' with signInAudience='$signInAudience'"
        Write-Info "DRY RUN: Would add Graph 'FileStorageContainer.Selected' (Application) role"
        Write-Info "DRY RUN: Operator manual: Portal -> add Graph 'FileStorageContainerTypeReg.Selected' + grant admin consent"
        Write-Info "DRY RUN: Would create service principal"
        $result.AppId = "00000000-0000-0000-0000-000000000000"
        return $result
    }

    # 2. Create app-reg. No redirect URI (owning apps do not participate in OAuth code flow;
    #    they are consumed by consuming tenants that grant admin consent to them).
    Write-Step 2 "Creating app registration '$displayName' (signInAudience=$signInAudience)"
    $createdApp = az ad app create `
        --display-name $displayName `
        --sign-in-audience $signInAudience `
        --output json 2>&1 | ConvertFrom-Json

    if ($LASTEXITCODE -ne 0 -or -not $createdApp) {
        throw "Failed to create app registration '$displayName'. Azure CLI may require Application Administrator role."
    }

    $result.AppId    = $createdApp.appId
    $result.ObjectId = $createdApp.id
    Write-Success "Created app: $displayName (AppId: $($result.AppId))"

    # 3. Add Graph 'FileStorageContainer.Selected' (Application) — well-known GUID.
    Write-Step 3 "Adding Graph 'FileStorageContainer.Selected' (Application) API permission"
    az ad app permission add --id $result.AppId `
        --api $GraphApiId `
        --api-permissions "$($GraphFileStorageContainerSelected)=Role" `
        --output none 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Failed to add 'FileStorageContainer.Selected' permission automatically. Add manually via Portal."
    } else {
        Write-Success "Added Graph 'FileStorageContainer.Selected' (Application) permission"
    }

    # 4. Operator manual step for FileStorageContainerTypeReg.Selected (runbook Step 1 fallback).
    Write-Warn "Operator manual step: add 'FileStorageContainerTypeReg.Selected' (Application) permission via Portal."
    Write-Info "  Portal: https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$($result.AppId)"
    Write-Info "  Then: 'Grant admin consent for <tenant>' (blue button) — required per runbook Step 2."

    # 5. Service principal (required for the app to be usable in the tenant).
    Write-Step 4 "Creating service principal"
    $sp = az ad sp create --id $result.AppId --output json 2>$null | ConvertFrom-Json
    if ($sp) {
        Write-Success "Service principal created (ObjectId: $($sp.id))"
    } else {
        Write-Info "Service principal may already exist"
    }

    # 6. Next-steps for the operator (runbook Steps 2-5).
    Write-Host ""
    Write-Host "  NEXT STEPS FOR '$displayName' (per SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md):" -ForegroundColor Cyan
    Write-Host "    Step 2. Grant admin consent for the 2 API permissions (Portal blue button)." -ForegroundColor White
    Write-Host "    Step 3. Create the container-type via a DELEGATED flow (SPE Admin app / VS Code / SharePoint admin center) —" -ForegroundColor White
    Write-Host "            passing owningAppId='$($result.AppId)' + billingClassification='standard' (Trial1/Model1) or 'directToCustomer' (Model2)." -ForegroundColor White
    Write-Host "    Step 4. Attach the Azure billing profile (standard classification only)." -ForegroundColor White
    Write-Host "    Step 5. Wait for replication (~2 min empirical, 24h Microsoft SLO)." -ForegroundColor White
    Write-Host "    Step 6. Register the tier's BFF app-reg via: -CreateBffApp $Tier" -ForegroundColor White
    Write-Host ""

    return $result
}

function New-SpaarkeSpeBffApp {
    <#
    .SYNOPSIS
        Idempotently creates the BFF app-reg for a given tier per topology doc SS3A
        rows 4-6.
    .DESCRIPTION
        Creation order (mirrors the prod BFF-API flow but WITHOUT client-secret minting
        or Key Vault writes — topology BFF apps are secret-free per ADR-028 A4):
          1. Idempotency check by display name.
          2. Create app-reg (signInAudience=AzureADMyOrg — BFF apps are ALWAYS single-tenant
             per project CLAUDE.md SS MUST rule + topology doc SS3A rows 4-6).
          3. Add Graph delegated permissions (mirror prod: Files.ReadWrite.All, Sites.ReadWrite.All,
             User.Read, Mail.Send).
          4. Add Dynamics CRM delegated permission (mirror prod: user_impersonation).
          5. Set Application ID URI + expose 'user_impersonation' scope (mirror prod).
          6. Create service principal.

        DELIBERATELY NOT DONE (per constraints):
          - No client secret minted (KV credential-lifecycle rule 1 — BFF-*-ClientSecret
            in ANY casing is a plain ADR-028 A4 violation).
          - No FIC added (added via a separate script invocation with -CreateFederatedCredential).
          - No KV writes (H4 handler writes per-customer KV entries at provisioning time).
          - No redirect URIs (BFF is a confidential-client that mints tokens via FIC/MI;
            no OAuth code flow needed).
          - Container access is NOT declared here — it comes from Step 6 of the runbook
            (registration-level `applicationPermissionGrants` on the container-type
            registration, per topology doc SS3A "How a BFF gets container access without
            owning anything — VERIFIED").

    .OUTPUTS
        PSCustomObject: AppId, ObjectId, DisplayName, SignInAudience, AlreadyExisted, Tier.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Trial1','Model1','Model2')][string]$Tier,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [string]$CustomerName = "",
        [switch]$DryRun
    )

    $displayName = Get-SpeTopologyBffAppDisplayName -Tier $Tier -CustomerName $CustomerName

    Write-Header "SPE BFF APP — $displayName (topology doc SS3A row $(switch ($Tier) { 'Trial1' {4}; 'Model1' {5}; 'Model2' {6} }))"
    Write-Info "Tier            : $Tier$(if ($Tier -eq 'Model2' -and -not [string]::IsNullOrWhiteSpace($CustomerName)) { " (customer='$CustomerName' — per-customer BFF per SS3A row 6)" })"
    Write-Info "signInAudience  : AzureADMyOrg  <- BFF apps are ALWAYS single-tenant (project CLAUDE.md SS MUST rule)"
    Write-Info "Tenant          : $TenantId"

    $result = [pscustomobject]@{
        AppId          = $null
        ObjectId       = $null
        DisplayName    = $displayName
        SignInAudience = 'AzureADMyOrg'
        AlreadyExisted = $false
        Tier           = $Tier
        Role           = 'Bff'
        AppIdUri       = $null
    }

    # 1. Idempotency
    Write-Step 1 "Checking whether '$displayName' already exists"
    $existing = az ad app list --display-name $displayName --output json 2>$null | ConvertFrom-Json
    if ($existing -and $existing.Count -gt 0) {
        Write-Warn "App registration '$displayName' already exists (AppId: $($existing[0].appId))"
        Write-Info "Skipping creation — this is idempotent. To modify permissions, use the Portal."
        $result.AppId          = $existing[0].appId
        $result.ObjectId       = $existing[0].id
        $result.AlreadyExisted = $true
        $result.AppIdUri       = "api://$($result.AppId)"

        if ($existing[0].signInAudience -ne 'AzureADMyOrg') {
            Write-Warn "signInAudience DRIFT: existing='$($existing[0].signInAudience)' expected='AzureADMyOrg'."
            Write-Warn "  BFF apps MUST be single-tenant (project CLAUDE.md SS MUST rule)."
            Write-Warn "  Fix via Portal -> Manifest -> signInAudience = 'AzureADMyOrg'."
        }
        return $result
    }

    if ($DryRun) {
        Write-Info "DRY RUN: Would create app '$displayName' with signInAudience='AzureADMyOrg'"
        Write-Info "DRY RUN: Would add Graph delegated: Files.ReadWrite.All, Sites.ReadWrite.All, User.Read, Mail.Send"
        Write-Info "DRY RUN: Would add Dynamics CRM delegated: user_impersonation"
        Write-Info "DRY RUN: Would set Application ID URI: api://<app-id> + expose user_impersonation scope"
        Write-Info "DRY RUN: Would create service principal"
        Write-Info "DRY RUN: Would NOT mint client secret (secret-free per ADR-028 A4)"
        Write-Info "DRY RUN: Would NOT write to Key Vault (H4 handles per-customer at provisioning time)"
        $result.AppId    = "00000000-0000-0000-0000-000000000000"
        $result.AppIdUri = "api://00000000-0000-0000-0000-000000000000"
        return $result
    }

    # 2. Create app-reg. Single-tenant, no redirect URIs.
    Write-Step 2 "Creating app registration '$displayName' (signInAudience=AzureADMyOrg, secret-free)"
    $createdApp = az ad app create `
        --display-name $displayName `
        --sign-in-audience AzureADMyOrg `
        --output json 2>&1 | ConvertFrom-Json

    if ($LASTEXITCODE -ne 0 -or -not $createdApp) {
        throw "Failed to create app registration '$displayName'. Azure CLI may require Application Administrator role."
    }

    $result.AppId    = $createdApp.appId
    $result.ObjectId = $createdApp.id
    $result.AppIdUri = "api://$($result.AppId)"
    Write-Success "Created app: $displayName (AppId: $($result.AppId))"

    # 3. Add Graph delegated permissions (mirror prod BFF-API flow at lines 1259-1262).
    Write-Step 3 "Adding Graph delegated permissions (Files/Sites/User/Mail)"
    az ad app permission add --id $result.AppId `
        --api $GraphApiId `
        --api-permissions "$($GraphFilesReadWriteAll)=Scope $($GraphSitesReadWriteAll)=Scope $($GraphUserRead)=Scope $($GraphMailSend)=Scope" `
        --output none 2>&1

    # 4. Add Dynamics CRM delegated permission (mirror prod).
    Write-Step 4 "Adding Dynamics CRM delegated permission (user_impersonation)"
    az ad app permission add --id $result.AppId `
        --api $DynamicsCrmApiId `
        --api-permissions "$($DynamicsCrmUserImpersonation)=Scope" `
        --output none 2>&1

    Write-Success "API permissions added"

    # 5. Set Application ID URI + expose user_impersonation scope (mirror prod at 1281-1318).
    Write-Step 5 "Setting Application ID URI + exposing user_impersonation scope"
    az ad app update --id $result.AppId --identifier-uris $result.AppIdUri --output none 2>&1

    $scopeId = [guid]::NewGuid().ToString()
    $apiDefinition = @{
        oauth2PermissionScopes = @(
            @{
                adminConsentDescription = "Allow the application to access $displayName on behalf of the signed-in user."
                adminConsentDisplayName = "Access $displayName"
                id                      = $scopeId
                isEnabled               = $true
                type                    = "User"
                userConsentDescription  = "Allow the application to access $displayName on your behalf."
                userConsentDisplayName  = "Access $displayName"
                value                   = "user_impersonation"
            }
        )
    } | ConvertTo-Json -Depth 4

    $apiPath = [System.IO.Path]::GetTempFileName()
    try {
        $apiDefinition | Out-File -FilePath $apiPath -Encoding utf8
        az rest --method PATCH `
            --uri "https://graph.microsoft.com/v1.0/applications/$($result.ObjectId)" `
            --headers "Content-Type=application/json" `
            --body "@$apiPath" `
            --output none 2>&1
    } finally {
        Remove-Item $apiPath -ErrorAction SilentlyContinue
    }
    Write-Success "Application ID URI set: $($result.AppIdUri) + exposed scope: user_impersonation"

    # 6. Service principal.
    Write-Step 6 "Creating service principal"
    $sp = az ad sp create --id $result.AppId --output json 2>$null | ConvertFrom-Json
    if ($sp) {
        Write-Success "Service principal created (ObjectId: $($sp.id))"
    } else {
        Write-Info "Service principal may already exist"
    }

    # NEXT STEPS
    Write-Host ""
    Write-Host "  NEXT STEPS FOR '$displayName' (per SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md):" -ForegroundColor Cyan
    Write-Host "    Step 6b. Grant admin consent (Portal blue button):" -ForegroundColor White
    Write-Host "             https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$($result.AppId)" -ForegroundColor White
    Write-Host "    Step 6c. Register this BFF app on the container-type registration (grants container access without ownership):" -ForegroundColor White
    Write-Host "             POST /beta/storage/fileStorage/containerTypeRegistrations/<containerTypeId>/applicationPermissionGrants" -ForegroundColor White
    Write-Host "             Body: { appId: '$($result.AppId)', applicationPermissions: ['Full'], delegatedPermissions: ['Full'] }" -ForegroundColor White
    Write-Host "    Step 7.  Populate spaarke-constants.yaml per_env_constants.<env>.bffApiAppId = '$($result.AppId)'" -ForegroundColor White
    Write-Host "    Step 8.  Add a FIC for the BFF's UAMI (secret-free per ADR-028 A4):" -ForegroundColor White
    Write-Host "             ./Register-EntraAppRegistrations.ps1 -FicOnly -FederatedCredentialAppId $($result.AppId) -UamiResourceId <arm-id> -TenantId $TenantId" -ForegroundColor White
    Write-Host ""

    return $result
}

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
# the secret rather than storing one — nor to a topology-mode run, which does not write
# any KV entries per task 213.4).
if (-not $DryRun -and -not $FicOnly -and -not $TopologyMode) {
    $kvCheck = az keyvault show --name $KeyVaultName --output json 2>$null | ConvertFrom-Json
    if (-not $kvCheck) {
        Write-Warn "Key Vault '$KeyVaultName' not accessible. Secrets will need manual storage."
    } else {
        Write-Success "Key Vault '$KeyVaultName' accessible"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# SPE Topology mode — task 213.4 (2026-08-30)
#
# When -CreateOwningApp or -CreateBffApp is set, this block runs BEFORE the
# prod BFF-API flow and exits cleanly. The prod flow is skipped via the
# implicit -SkipBffApi set in the mode gate above.
#
# Ordering: OWNING app first (per runbook Step 1 — the container-type creation
# in Step 3 needs the owning-app's GUID), then BFF app (runbook Step 6).
# Both can be created in a single invocation for convenience, though the
# runbook's canonical flow calls them separately.
# ─────────────────────────────────────────────────────────────────────────────

if ($TopologyMode) {
    Write-Header "SPE TOPOLOGY APP REGISTRATIONS (per SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md SS3A)"
    Write-Info "This is the ONE-TIME operator setup — NOT a per-customer operation."
    Write-Info "Reference runbook: docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md"

    $topologyResults = @()

    if ($CreateOwningApp) {
        $topologyResults += (New-SpaarkeSpeContainerTypeOwningApp `
            -Tier $CreateOwningApp `
            -TenantId $TenantId `
            -DryRun:$DryRun)
    }

    if ($CreateBffApp) {
        $topologyResults += (New-SpaarkeSpeBffApp `
            -Tier $CreateBffApp `
            -CustomerName $CustomerName `
            -TenantId $TenantId `
            -DryRun:$DryRun)
    }

    Write-Header "TOPOLOGY SUMMARY"
    if ($DryRun) {
        Write-Host "  *** DRY RUN — No changes were made ***" -ForegroundColor Magenta
        Write-Host ""
    }
    Write-Host "  Tenant ID : $TenantId" -ForegroundColor White
    Write-Host ""
    foreach ($r in $topologyResults) {
        $roleTag = if ($r.Role -eq 'Owning') { 'OWNING app (SS3A)' } else { 'BFF app (SS3A)' }
        Write-Host "  [$($r.Tier)] $roleTag  '$($r.DisplayName)'" -ForegroundColor Green
        Write-Host "    AppId          : $($r.AppId)" -ForegroundColor White
        Write-Host "    ObjectId       : $($r.ObjectId)" -ForegroundColor White
        Write-Host "    signInAudience : $($r.SignInAudience)" -ForegroundColor White
        if ($r.AppIdUri) {
            Write-Host "    AppId URI      : $($r.AppIdUri)" -ForegroundColor White
        }
        Write-Host "    Pre-existing?  : $($r.AlreadyExisted)" -ForegroundColor White
        Write-Host ""
    }
    Write-Host "  NEXT STEPS: follow SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md steps 2-8." -ForegroundColor Cyan
    Write-Host "    (Admin consent + container-type creation via delegated flow + billing profile" -ForegroundColor Cyan
    Write-Host "     + replication wait + registration-level grant + constants population + FIC.)" -ForegroundColor Cyan
    Write-Host ""

    exit 0
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
    if ($SkipClientSecret) {
        Write-Step 4 "Client secret: SKIPPED (-SkipClientSecret) — ADR-028 A4, secret-free identity"
        Write-Info "  No client secret will be minted and none will be written to Key Vault."
        Write-Info "  Provision a federated credential instead: -CreateFederatedCredential -UamiResourceId <id>"
        Write-Info "  Then set on the app: Graph__Credentials__Order__0=ManagedIdentityFederated"
        Write-Info "                       Graph__Credentials__RequireSecretFreeIdentity=true"

        # The non-secret Key Vault entries are still required — they are identifiers, not credentials,
        # and downstream configuration resolves them by name. Only the secret is suppressed.
        if (-not $DryRun -and $BffApiAppId) {
            Store-SecretInKeyVault -VaultName $KeyVaultName `
                -SecretName "BFF-API-ClientId" `
                -SecretValue $BffApiAppId `
                -Description "BFF API client ID ($BffApiDisplayName)"

            Store-SecretInKeyVault -VaultName $KeyVaultName `
                -SecretName "BFF-API-Audience" `
                -SecretValue "api://$BffApiAppId" `
                -Description "BFF API audience URI ($BffApiDisplayName)"

            Store-SecretInKeyVault -VaultName $KeyVaultName `
                -SecretName "TenantId" `
                -SecretValue $TenantId `
                -Description "Entra ID tenant ID"
        } else {
            Write-Info "DRY RUN: Would store BFF-API-ClientId, BFF-API-Audience, TenantId (NO client secret)"
        }
    } else {

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

    }  # end if/else on -SkipClientSecret (task 033)

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
    # In dry-run, Step 1 hands back an all-zero placeholder when no app registration exists yet.
    # Querying Graph for it fails, which would make the flagship "preview a fresh production run"
    # invocation exit 1 from a mode whose whole purpose is to be side-effect-free and informative.
    $ficPreviewOnly = $false
    if ($DryRun -and $ficAppId -eq "00000000-0000-0000-0000-000000000000") {
        Write-Info "DRY RUN: no real app registration exists yet, so the credential cannot be previewed against Graph."
        Write-Info "DRY RUN: would create a FIC on the new app registration, subject = the principalId of the UAMI at $UamiResourceId."
        $ficPreviewOnly = $true
    }
    if (-not $UamiResourceId) {
        throw "-UamiResourceId is required with -CreateFederatedCredential. Pass the ARM resource ID of the user-assigned managed identity (resource ID, never name)."
    }

    if (-not $ficPreviewOnly) {
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

    }

    if ($ficPreviewOnly) {
        Write-Info "DRY RUN: preview only — nothing was queried, created or verified."
    }
    elseif ($DryRun) {
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
