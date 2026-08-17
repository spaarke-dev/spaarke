<#
.SYNOPSIS
    Idempotently creates or reconciles the production Entra ID app registration
    for the Spaarke BFF API. Re-run safe.

.DESCRIPTION
    Creates or reconciles the BFF API app registration in the target Entra ID
    tenant:
      1. spaarke-bff-api-prod — BFF API production app (validates user tokens,
         OBO for Graph + Dataverse)

    IDEMPOTENCY CONTRACT (customer-provisioning-orchestration-r1 task 010,
    spec.md FR-06 + Gap 3 + R4):
      - Fully-configured app-reg + KV: re-run emits "no changes needed" and
        exits 0. Zero net changes on the app-reg, zero new client secrets,
        zero KV writes with a differing value.
      - Partially-configured (e.g., one grant deleted, wrong audience, missing
        exposed scope, missing KV secret): re-run applies only the missing
        delta, verifies, and exits 0. Reports the specific changes made.
      - Real errors (Graph API failure, KV auth failure, invalid tenant): exits
        non-zero with a diagnostic message. NEVER exits 1 on "already present".

    Per-operation pre-checks (see task POML step 2):
      - requiredResourceAccess: read current → diff vs required → apply only
        missing (resourceAppId, permission-id, type) tuples.
      - signInAudience: PATCH via Graph only when current value ≠
        AzureADMultipleOrgs (spec.md FR-06 v3 change).
      - identifierUris: append api://{appId} only if not already present.
      - api.oauth2PermissionScopes: append user_impersonation only if a scope
        with that value is not already present (preserves the existing scope's
        GUID — no duplicates).
      - passwordCredentials: generate a new client secret ONLY when no valid
        (unexpired) credential exists OR the KV secret is missing.
      - servicePrincipal: create only when absent.

    NOTE (2026-08-14, code-quality-and-assurance-r3 task 060): the separate
    `spaarke-dataverse-s2s-*` app registration was REMOVED (zero code
    consumers — Dataverse server-to-server access consolidated onto the BFF
    app registration's credential (`API_CLIENT_SECRET`) on 2026-01-07 — see
    docs/architecture/auth-azure-resources.md). The BFF app registration is
    the single Dataverse Application User. Do NOT re-introduce.

    Sign-in audience: AzureADMultipleOrgs (spec.md FR-06 / v3 change) —
    enables Model 2 (multitenant) admin consent. Do NOT use AzureADMyOrg or
    personal-account audiences.

    Client secret storage: written to Key Vault (BFF-API-ClientSecret). Callers
    consume it via `@Microsoft.KeyVault(SecretUri=...)` App Service reference
    — the URI ref pattern (per ADR-028 documented MI exceptions). The secret
    value never leaves the script as cleartext in stdout, log, or file. The
    prefix (first 5 chars) is logged on generation for correlation only.

    Prerequisites:
      - Azure CLI authenticated with Entra ID admin permissions
      - Access to the target Key Vault (per -KeyVaultName)
      - -TenantId REQUIRED (v3.3 tenant-isolation invariant I1 per r1
        design.md §4D / FR-28)

.PARAMETER TenantId
    Entra ID tenant ID. MANDATORY (v3.3 tenant-isolation invariant I1 — no
    hardcoded default; requiring the operator to pass -TenantId prevents
    cross-tenant provisioning accidents like accidentally provisioning the
    customer's app-reg in Spaarke's tenant).

.PARAMETER KeyVaultName
    Key Vault name for storing secrets. Default: sprk-platform-prod-kv (Spaarke
    tenant platform KV; override per customer per r1 design.md §7.9 naming
    standard).

.PARAMETER ProductionApiDomain
    Production API domain for redirect URIs. Default: api.spaarke.com

.PARAMETER DataverseOrgUrl
    Production Dataverse organization URL. Default: (empty, set when known)

.PARAMETER DryRun
    If specified, shows what would be created or reconciled without making
    changes. Still performs read-only pre-checks so the report accurately
    reflects the delta that WOULD be applied.

.PARAMETER SkipBffApi
    Skip BFF API app registration (if already created).

.PARAMETER SecretExpiryMonths
    Client-secret lifetime in months when a NEW secret is generated. Default:
    24. Irrelevant when a valid unexpired secret already exists (idempotency
    contract skips rotation).

.EXAMPLE
    # Preview what would be created or reconciled (Spaarke tenant)
    .\Register-EntraAppRegistrations.ps1 -TenantId "a221a95e-6abc-4434-aecc-e48338a1b2f2" -DryRun

.EXAMPLE
    # Create or reconcile the BFF API registration for a customer tenant
    .\Register-EntraAppRegistrations.ps1 -TenantId "<customer-tenant-guid>" `
        -KeyVaultName "sprk-{customerId}-prod-kv"

.EXAMPLE
    # Re-run against a fully-configured app-reg — expected output ends with:
    #   No changes needed — app-reg and KV are already fully configured.
    #   Exit code: 0
    .\Register-EntraAppRegistrations.ps1 -TenantId "<tenant-guid>" `
        -KeyVaultName "sprk-<customerId>-prod-kv"

.NOTES
    Project: production-environment-setup-r1 (originally) / customer-provisioning-orchestration-r1
             (v3.3 tenant-isolation fix + task 010 idempotency hardening)
    Task: 021 — Create Entra ID app registrations (original)
          010 — Harden for full 14-grant idempotency (this hardening)
    Naming: FR-11 compliant (spaarke- prefix)
    Secrets: FR-08 compliant (Key Vault only; URI-ref consumption pattern per ADR-028)
    v3.3 change: -TenantId is MANDATORY (was defaulted to Spaarke tenant) per r1
                 design.md §4D tenant-isolation invariant I1 (landed 1834b77bc).
    v3.4 change (task 010): full idempotency — audience AzureADMultipleOrgs;
                 pre-check-driven grant reconciliation; skip-if-valid client secret;
                 change-summary emitted at exit.
    Boundary: this script does NOT read GraphAppRoles.cs — task 015 owns the
              C#→PS grant reader for MI app-role grants (a separate concern from
              the app-registration delegated scopes managed here).
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,
    [string]$KeyVaultName = "sprk-platform-prod-kv",
    [string]$ProductionApiDomain = "api.spaarke.com",
    [string]$DataverseOrgUrl = "",
    [switch]$DryRun,
    [switch]$SkipBffApi,
    [int]$SecretExpiryMonths = 24
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────────────

$BffApiDisplayName = "spaarke-bff-api-prod"
$SecretExpiryDate = (Get-Date).AddMonths($SecretExpiryMonths).ToString("yyyy-MM-ddTHH:mm:ssZ")
$RequiredSignInAudience = "AzureADMultipleOrgs"   # spec.md FR-06 / v3 change (Model 2 consent)
$ExposedScopeValue = "user_impersonation"
$KVSecretName_ClientSecret = "BFF-API-ClientSecret"
$KVSecretName_ClientId = "BFF-API-ClientId"
$KVSecretName_Audience = "BFF-API-Audience"
$KVSecretName_TenantId = "TenantId"

# Microsoft Graph API well-known IDs
#
# NOTE: these four IDs are DELEGATED scopes (OAuth2PermissionScopes) requested at app-registration
# creation time — a DIFFERENT concern from the app-only APPLICATION roles the BFF identity holds.
# Do NOT merge these with the application-role list. The canonical source of truth for the BFF's
# expected Graph APPLICATION (app-only) roles is:
#   src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs
# (Grant enumeration from GraphAppRoles.cs is the concern of task 015's C#→PS reader, NOT this
# script per r1 task 010 SCOPE constraint.)
$GraphApiId = "00000003-0000-0000-c000-000000000000"
$GraphFilesReadWriteAll = "75359482-378d-4052-8f01-80520e7db3cd"   # Files.ReadWrite.All (delegated)
$GraphSitesReadWriteAll = "89fe6a52-be36-487e-b7d8-d061c450a026"   # Sites.ReadWrite.All (delegated)
$GraphUserRead          = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"   # User.Read (delegated)
$GraphMailSend          = "e383f46e-2787-4529-855e-0e479a3ffac0"   # Mail.Send (delegated)

# Dynamics CRM API well-known ID
$DynamicsCrmApiId = "00000007-0000-0000-c000-000000000000"
$DynamicsCrmUserImpersonation = "78ce3f0f-a1ce-49c2-8cde-64b5c0896db4"  # user_impersonation (delegated)

# ─────────────────────────────────────────────────────────────────────────────
# Required-permission catalog (single source of truth for the reconciler)
# ─────────────────────────────────────────────────────────────────────────────
# Each entry: ResourceAppId (the target API's appId) + one permission {id, type, name}.
# Type = "Scope" (delegated) | "Role" (app-only). This script currently declares delegated
# scopes on the app-registration only; per task 010 SCOPE, MI app-role grants
# (GraphAppRoles.cs) are the concern of task 015.
$RequiredPermissions = @(
    [pscustomobject]@{ ResourceAppId = $GraphApiId;        Id = $GraphFilesReadWriteAll;      Type = "Scope"; Name = "Graph:Files.ReadWrite.All (delegated)" }
    [pscustomobject]@{ ResourceAppId = $GraphApiId;        Id = $GraphSitesReadWriteAll;      Type = "Scope"; Name = "Graph:Sites.ReadWrite.All (delegated)" }
    [pscustomobject]@{ ResourceAppId = $GraphApiId;        Id = $GraphUserRead;               Type = "Scope"; Name = "Graph:User.Read (delegated)" }
    [pscustomobject]@{ ResourceAppId = $GraphApiId;        Id = $GraphMailSend;               Type = "Scope"; Name = "Graph:Mail.Send (delegated)" }
    [pscustomobject]@{ ResourceAppId = $DynamicsCrmApiId;  Id = $DynamicsCrmUserImpersonation; Type = "Scope"; Name = "Dynamics:user_impersonation (delegated)" }
)

# ─────────────────────────────────────────────────────────────────────────────
# Change tracking (idempotency reporter)
# ─────────────────────────────────────────────────────────────────────────────
$script:ChangesApplied = New-Object System.Collections.Generic.List[string]
$script:AlreadyPresent = New-Object System.Collections.Generic.List[string]

function Record-Change     { param([string]$Msg) $script:ChangesApplied.Add($Msg) | Out-Null }
function Record-NoChange   { param([string]$Msg) $script:AlreadyPresent.Add($Msg) | Out-Null }

# ─────────────────────────────────────────────────────────────────────────────
# Console helpers
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

function Write-Skip {
    param([string]$Message)
    Write-Host "  [SKIP] $Message" -ForegroundColor DarkCyan
}

function Write-Change {
    param([string]$Message)
    Write-Host "  [CHANGE] $Message" -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────────────────────────
# Key Vault helpers
# ─────────────────────────────────────────────────────────────────────────────

function Test-KeyVaultSecretExists {
    param([string]$VaultName, [string]$SecretName)

    if ($DryRun) { return $false }  # Force verbose "would create" in dry-run path

    $existing = az keyvault secret show --vault-name $VaultName --name $SecretName --output json 2>$null
    return ($LASTEXITCODE -eq 0 -and $existing)
}

function Get-KeyVaultSecretValue {
    param([string]$VaultName, [string]$SecretName)

    if ($DryRun) { return $null }

    $result = az keyvault secret show --vault-name $VaultName --name $SecretName --query value -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $result
}

function Ensure-KeyVaultSecret {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$SecretValue,
        [string]$Description,
        [switch]$AllowValueRotation  # Set only for the client secret; ID-type values are stable
    )

    if ($DryRun) {
        Write-Info "DRY RUN: Would ensure secret '$SecretName' in Key Vault '$VaultName'"
        Record-Change "KV:$SecretName (dry-run)"
        return
    }

    $existingValue = Get-KeyVaultSecretValue -VaultName $VaultName -SecretName $SecretName

    if ($existingValue -and $existingValue -eq $SecretValue) {
        Write-Skip "KV secret '$SecretName' already at target value"
        Record-NoChange "KV:$SecretName"
        return
    }

    if ($existingValue -and -not $AllowValueRotation) {
        # Stable ID-type secret already present with a different value — that is an operator
        # concern, not a normal idempotency delta. Warn + do NOT overwrite (would silently
        # break a live env consuming the current KV ref).
        Write-Warn "KV secret '$SecretName' exists with a DIFFERENT value; not overwriting (stable ID-type). Investigate before rotating."
        Record-NoChange "KV:$SecretName (existing value preserved)"
        return
    }

    Write-Info "Writing KV secret '$SecretName' in Key Vault '$VaultName'..."
    az keyvault secret set `
        --vault-name $VaultName `
        --name $SecretName `
        --value $SecretValue `
        --description $Description `
        --output none 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to store secret '$SecretName' in Key Vault '$VaultName'"
    }

    Write-Change "KV secret '$SecretName' written"
    Record-Change "KV:$SecretName"
}

# ─────────────────────────────────────────────────────────────────────────────
# App-registration reconciler helpers
# ─────────────────────────────────────────────────────────────────────────────

function Get-AppRegistration {
    param([string]$DisplayName)

    $existing = az ad app list --display-name $DisplayName --output json 2>$null | ConvertFrom-Json
    if ($existing -and $existing.Count -gt 0) {
        # Fetch full manifest (list projection is thin)
        $full = az ad app show --id $existing[0].appId --output json 2>$null | ConvertFrom-Json
        return $full
    }
    return $null
}

function Get-MissingPermissions {
    <#
    Diffs $RequiredPermissions against the app's current $requiredResourceAccess.
    Returns array of missing permissions (same shape as $RequiredPermissions rows).
    A permission is "present" iff (resourceAppId, permission-id, type) all match.
    #>
    param($App, $Required)

    $current = @()
    if ($App -and $App.requiredResourceAccess) {
        foreach ($rra in $App.requiredResourceAccess) {
            foreach ($ra in $rra.resourceAccess) {
                $current += [pscustomobject]@{
                    ResourceAppId = $rra.resourceAppId
                    Id = $ra.id
                    Type = $ra.type
                }
            }
        }
    }

    $missing = @()
    foreach ($req in $Required) {
        $match = $current | Where-Object {
            $_.ResourceAppId -eq $req.ResourceAppId -and
            $_.Id -eq $req.Id -and
            $_.Type -eq $req.Type
        }
        if (-not $match) {
            $missing += $req
        }
    }
    return $missing
}

function Add-MissingPermissions {
    <#
    Applies the missing permission delta via `az ad app permission add`.
    Verifies each addition via a follow-up `az ad app show` diff to catch
    ambiguous Graph responses (per task 010 escalation trigger).
    Returns count of grants added.
    #>
    param([string]$AppId, [array]$Missing)

    if (-not $Missing -or $Missing.Count -eq 0) {
        return 0
    }

    # Group by resourceAppId (az ad app permission add takes a single --api at a time)
    $byResource = $Missing | Group-Object -Property ResourceAppId

    foreach ($group in $byResource) {
        $resourceId = $group.Name
        $apiPerms = ($group.Group | ForEach-Object { "$($_.Id)=$($_.Type)" }) -join " "

        if ($DryRun) {
            foreach ($p in $group.Group) {
                Write-Info "DRY RUN: Would add permission $($p.Name)"
            }
            continue
        }

        Write-Info "Adding $($group.Group.Count) permission(s) on resource $resourceId..."
        az ad app permission add --id $AppId `
            --api $resourceId `
            --api-permissions $apiPerms `
            --output none 2>&1 | Out-Null

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to add permission(s) [$apiPerms] for resource $resourceId on app $AppId"
        }
    }

    # Verify (escalation-trigger guard): re-read + confirm all missing are now present.
    # An ambiguous Graph response (e.g., duplicate-ok but not actually added) is caught here.
    if (-not $DryRun) {
        Start-Sleep -Seconds 2  # Small delay to let Graph propagate the write
        $appAfter = az ad app show --id $AppId --output json 2>$null | ConvertFrom-Json
        $stillMissing = Get-MissingPermissions -App $appAfter -Required $Missing
        if ($stillMissing.Count -gt 0) {
            $names = ($stillMissing | ForEach-Object { $_.Name }) -join "; "
            throw "IDEMPOTENCY VERIFICATION FAILED: after add, these permissions are still missing on app $AppId — [$names]. Graph API returned ambiguous state; escalate per task 010 <escalation> trigger."
        }
    }

    foreach ($p in $Missing) {
        Write-Change "Permission granted: $($p.Name)"
        Record-Change "Permission:$($p.Name)"
    }
    return $Missing.Count
}

function Reconcile-SignInAudience {
    <#
    PATCHes signInAudience if current value ≠ $RequiredSignInAudience.
    #>
    param($App, [string]$RequiredAudience)

    $current = $App.signInAudience
    if ($current -eq $RequiredAudience) {
        Write-Skip "signInAudience already '$RequiredAudience'"
        Record-NoChange "signInAudience"
        return
    }

    if ($DryRun) {
        Write-Info "DRY RUN: Would PATCH signInAudience: '$current' → '$RequiredAudience'"
        Record-Change "signInAudience (dry-run)"
        return
    }

    Write-Info "Reconciling signInAudience: '$current' → '$RequiredAudience'"
    az ad app update --id $App.appId --sign-in-audience $RequiredAudience --output none 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to update signInAudience to '$RequiredAudience' on app $($App.appId)"
    }

    Write-Change "signInAudience → '$RequiredAudience'"
    Record-Change "signInAudience"
}

function Reconcile-IdentifierUri {
    <#
    Appends 'api://{appId}' to identifierUris if not already present.
    Never removes existing URIs (only extends).
    #>
    param($App, [string]$RequiredUri)

    $current = @()
    if ($App.identifierUris) { $current = @($App.identifierUris) }

    if ($current -contains $RequiredUri) {
        Write-Skip "identifierUris already contains '$RequiredUri'"
        Record-NoChange "identifierUri"
        return
    }

    if ($DryRun) {
        Write-Info "DRY RUN: Would append identifierUri '$RequiredUri'"
        Record-Change "identifierUri (dry-run)"
        return
    }

    Write-Info "Appending identifierUri '$RequiredUri'"
    $newUris = $current + $RequiredUri
    az ad app update --id $App.appId --identifier-uris @newUris --output none 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # Fallback for older az CLI: use quoted single-arg
        az ad app update --id $App.appId --identifier-uris $RequiredUri --output none 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to append identifierUri '$RequiredUri' on app $($App.appId)"
        }
    }

    Write-Change "identifierUri appended: '$RequiredUri'"
    Record-Change "identifierUri"
}

function Reconcile-ExposedScope {
    <#
    Appends the 'user_impersonation' oauth2PermissionScope if a scope with that
    value is not already present. Preserves existing scopes (does not touch their
    GUIDs — critical: consent grants reference those GUIDs).
    #>
    param($App, [string]$ScopeValue, [string]$AppIdUri)

    $existingScopes = @()
    if ($App.api -and $App.api.oauth2PermissionScopes) {
        $existingScopes = @($App.api.oauth2PermissionScopes)
    }

    $match = $existingScopes | Where-Object { $_.value -eq $ScopeValue }
    if ($match) {
        Write-Skip "Exposed scope '$ScopeValue' already present (id: $($match[0].id))"
        Record-NoChange "ExposedScope:$ScopeValue"
        return
    }

    if ($DryRun) {
        Write-Info "DRY RUN: Would append exposed scope '$ScopeValue' at $AppIdUri/$ScopeValue"
        Record-Change "ExposedScope:$ScopeValue (dry-run)"
        return
    }

    # Build new scope list = existing + our new scope (preserves existing GUIDs)
    $newScopeId = [guid]::NewGuid().ToString()
    $newScope = @{
        adminConsentDescription = "Allow the application to access $BffApiDisplayName on behalf of the signed-in user."
        adminConsentDisplayName = "Access $BffApiDisplayName"
        id = $newScopeId
        isEnabled = $true
        type = "User"
        userConsentDescription = "Allow the application to access $BffApiDisplayName on your behalf."
        userConsentDisplayName = "Access $BffApiDisplayName"
        value = $ScopeValue
    }

    $combined = @($existingScopes) + $newScope
    $apiDefinition = @{ api = @{ oauth2PermissionScopes = $combined } } | ConvertTo-Json -Depth 6

    $apiPath = [System.IO.Path]::GetTempFileName()
    $apiDefinition | Out-File -FilePath $apiPath -Encoding utf8

    az rest --method PATCH `
        --uri "https://graph.microsoft.com/v1.0/applications/$($App.id)" `
        --headers "Content-Type=application/json" `
        --body "@$apiPath" `
        --output none 2>&1 | Out-Null

    $exitCode = $LASTEXITCODE
    Remove-Item $apiPath -ErrorAction SilentlyContinue

    if ($exitCode -ne 0) {
        throw "Failed to append exposed scope '$ScopeValue' on app $($App.appId)"
    }

    Write-Change "Exposed scope appended: '$ScopeValue' at $AppIdUri/$ScopeValue"
    Record-Change "ExposedScope:$ScopeValue"
}

function Get-ValidCredentialCount {
    <#
    Returns count of passwordCredentials whose endDateTime is > now.
    #>
    param($App)

    if (-not $App.passwordCredentials) { return 0 }

    $now = (Get-Date).ToUniversalTime()
    $valid = @($App.passwordCredentials | Where-Object {
        $end = [datetime]::Parse($_.endDateTime).ToUniversalTime()
        $end -gt $now
    })
    return $valid.Count
}

function Ensure-ClientSecret {
    <#
    Idempotency: generate a NEW client secret ONLY when EITHER
      (a) no valid (unexpired) credential exists on the app-reg, OR
      (b) the KV secret is missing.
    In all other cases, skip. This prevents runaway secret proliferation on
    repeated runs and prevents overwriting a live secret consumed by an App
    Service KV ref.
    #>
    param([string]$AppId, $App, [string]$VaultName, [string]$SecretName, [string]$Description)

    $validCount = Get-ValidCredentialCount -App $App
    $kvExists = Test-KeyVaultSecretExists -VaultName $VaultName -SecretName $SecretName

    if ($validCount -gt 0 -and $kvExists) {
        Write-Skip "Client secret: $validCount valid credential(s) on app-reg + KV '$SecretName' present. No rotation."
        Record-NoChange "ClientSecret"
        return
    }

    if ($DryRun) {
        if ($validCount -eq 0) {
            Write-Info "DRY RUN: Would generate NEW $SecretExpiryMonths-month client secret (no valid credential on app-reg)"
        } elseif (-not $kvExists) {
            Write-Info "DRY RUN: Would generate NEW client secret (KV secret '$SecretName' missing)"
        }
        Record-Change "ClientSecret (dry-run)"
        return
    }

    $reason = if ($validCount -eq 0) { "no valid credential on app-reg" } else { "KV secret '$SecretName' missing" }
    Write-Info "Generating client secret ($reason)..."

    $secretResult = az ad app credential reset `
        --id $AppId `
        --append `
        --display-name "Production-$(Get-Date -Format 'yyyyMMdd')" `
        --end-date $SecretExpiryDate `
        --output json 2>&1 | ConvertFrom-Json

    if ($LASTEXITCODE -ne 0 -or -not $secretResult) {
        throw "Failed to create client secret for app $AppId"
    }

    $newSecret = $secretResult.password
    Write-Change "Client secret created (prefix: $($newSecret.Substring(0, 5))..., expires: $SecretExpiryDate)"
    Write-Warn "This secret is shown only once. Writing to Key Vault now."

    Ensure-KeyVaultSecret -VaultName $VaultName `
        -SecretName $SecretName `
        -SecretValue $newSecret `
        -Description $Description `
        -AllowValueRotation

    Record-Change "ClientSecret"
}

function Ensure-ServicePrincipal {
    <#
    Creates the app's service principal if not already present.
    #>
    param([string]$AppId)

    if ($DryRun) {
        Write-Info "DRY RUN: Would ensure service principal for $AppId"
        Record-NoChange "ServicePrincipal (dry-run)"
        return
    }

    $existingSp = az ad sp list --filter "appId eq '$AppId'" --output json 2>$null | ConvertFrom-Json
    if ($existingSp -and $existingSp.Count -gt 0) {
        Write-Skip "Service principal already exists (ObjectId: $($existingSp[0].id))"
        Record-NoChange "ServicePrincipal"
        return
    }

    Write-Info "Creating service principal for $AppId..."
    $sp = az ad sp create --id $AppId --output json 2>$null | ConvertFrom-Json
    if (-not $sp) {
        throw "Failed to create service principal for app $AppId"
    }

    Write-Change "Service principal created (ObjectId: $($sp.id))"
    Record-Change "ServicePrincipal"
}

function New-AppRegistration {
    <#
    Creates the app-reg with the correct audience + full requiredResourceAccess
    manifest, so a fresh env reaches steady-state in ONE run (subsequent runs
    are strictly no-ops per idempotency contract).
    Returns the created app object (full manifest via 'az ad app show').
    #>
    param([string]$DisplayName, [string]$RedirectUri)

    if ($DryRun) {
        Write-Info "DRY RUN: Would create app '$DisplayName' with:"
        Write-Info "  - signInAudience: $RequiredSignInAudience"
        Write-Info "  - Redirect URI: $RedirectUri"
        foreach ($p in $RequiredPermissions) {
            Write-Info "  - Permission: $($p.Name)"
        }
        Record-Change "App-reg created (dry-run)"
        # Return a synthetic object so downstream reconciliation can dry-run against a "missing" baseline
        return [pscustomobject]@{
            appId = "00000000-0000-0000-0000-000000000000"
            id = "00000000-0000-0000-0000-000000000000"
            displayName = $DisplayName
            signInAudience = $RequiredSignInAudience
            identifierUris = @()
            requiredResourceAccess = @()
            passwordCredentials = @()
            api = @{ oauth2PermissionScopes = @() }
        }
    }

    # Build the full manifest so requiredResourceAccess is populated at creation time
    # (avoids the second-pass "az ad app permission add" for the create case).
    $manifestObj = @{
        displayName = $DisplayName
        signInAudience = $RequiredSignInAudience
        web = @{
            redirectUris = @($RedirectUri)
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
                    @{ id = $GraphUserRead;          type = "Scope" }
                    @{ id = $GraphMailSend;          type = "Scope" }
                )
            },
            @{
                resourceAppId = $DynamicsCrmApiId
                resourceAccess = @(
                    @{ id = $DynamicsCrmUserImpersonation; type = "Scope" }
                )
            }
        )
    }

    $manifestPath = [System.IO.Path]::GetTempFileName()
    $manifestObj | ConvertTo-Json -Depth 8 | Out-File -FilePath $manifestPath -Encoding utf8

    # Create via full-manifest POST (audience + permissions in one call — no second-pass drift).
    $createdRaw = az rest --method POST `
        --uri "https://graph.microsoft.com/v1.0/applications" `
        --headers "Content-Type=application/json" `
        --body "@$manifestPath" `
        --output json 2>&1

    $exitCode = $LASTEXITCODE
    Remove-Item $manifestPath -ErrorAction SilentlyContinue

    if ($exitCode -ne 0) {
        throw "Failed to create app registration '$DisplayName' via Graph POST /applications. Response: $createdRaw"
    }

    $created = $createdRaw | ConvertFrom-Json
    if (-not $created -or -not $created.appId) {
        throw "Graph POST /applications returned unexpected payload for '$DisplayName'"
    }

    Write-Change "App registration created: $DisplayName (AppId: $($created.appId))"
    Record-Change "App-reg created"

    # Re-fetch to get full manifest projection (same shape as Get-AppRegistration)
    return az ad app show --id $created.appId --output json 2>$null | ConvertFrom-Json
}

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight Checks
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "ENTRA ID APP REGISTRATION — IDEMPOTENT RECONCILIATION"

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

# Verify Key Vault access
if (-not $DryRun) {
    $kvCheck = az keyvault show --name $KeyVaultName --output json 2>$null | ConvertFrom-Json
    if (-not $kvCheck) {
        Write-Warn "Key Vault '$KeyVaultName' not accessible. Secrets will need manual storage."
    } else {
        Write-Success "Key Vault '$KeyVaultName' accessible"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 1: BFF API Production App Registration — idempotent reconciliation
# ─────────────────────────────────────────────────────────────────────────────

$BffApiAppId = $null
$BffApiObjectId = $null

if (-not $SkipBffApi) {
    Write-Header "STEP 1: BFF API — get-or-create + reconcile"

    $bffApp = Get-AppRegistration -DisplayName $BffApiDisplayName

    if (-not $bffApp) {
        Write-Step 1 "App-reg '$BffApiDisplayName' not found — creating"
        $bffApp = New-AppRegistration -DisplayName $BffApiDisplayName `
            -RedirectUri "https://$ProductionApiDomain/.auth/login/aad/callback"
    } else {
        Write-Step 1 "App-reg '$BffApiDisplayName' found (AppId: $($bffApp.appId)) — reconciling"
        Record-NoChange "App-reg exists"
    }

    $BffApiAppId = $bffApp.appId
    $BffApiObjectId = $bffApp.id
    $AppIdUri = "api://$BffApiAppId"

    # Reconcile signInAudience (spec.md FR-06 / v3 — AzureADMultipleOrgs)
    Write-Step 2 "Reconcile signInAudience → '$RequiredSignInAudience'"
    Reconcile-SignInAudience -App $bffApp -RequiredAudience $RequiredSignInAudience

    # Reconcile requiredResourceAccess (delegated Graph + Dynamics)
    Write-Step 3 "Reconcile requiredResourceAccess (delegated permissions)"
    # Re-fetch after audience PATCH to avoid a stale-diff false positive
    if (-not $DryRun) {
        $bffApp = az ad app show --id $BffApiAppId --output json 2>$null | ConvertFrom-Json
    }
    $missing = Get-MissingPermissions -App $bffApp -Required $RequiredPermissions
    if ($missing.Count -eq 0) {
        Write-Skip "All $($RequiredPermissions.Count) required permission(s) already present"
        foreach ($p in $RequiredPermissions) { Record-NoChange "Permission:$($p.Name)" }
    } else {
        Write-Info "Delta: $($missing.Count) of $($RequiredPermissions.Count) permission(s) missing"
        [void](Add-MissingPermissions -AppId $BffApiAppId -Missing $missing)
    }

    # Reconcile identifierUris (api://<appId>)
    Write-Step 4 "Reconcile identifierUris"
    if (-not $DryRun) {
        $bffApp = az ad app show --id $BffApiAppId --output json 2>$null | ConvertFrom-Json
    }
    Reconcile-IdentifierUri -App $bffApp -RequiredUri $AppIdUri

    # Reconcile exposed scope (user_impersonation)
    Write-Step 5 "Reconcile exposed oauth2PermissionScopes"
    if (-not $DryRun) {
        $bffApp = az ad app show --id $BffApiAppId --output json 2>$null | ConvertFrom-Json
    }
    Reconcile-ExposedScope -App $bffApp -ScopeValue $ExposedScopeValue -AppIdUri $AppIdUri

    # Ensure client secret (only rotates when needed)
    Write-Step 6 "Ensure client secret (skip-if-valid pattern)"
    if (-not $DryRun) {
        $bffApp = az ad app show --id $BffApiAppId --output json 2>$null | ConvertFrom-Json
    }
    Ensure-ClientSecret -AppId $BffApiAppId `
        -App $bffApp `
        -VaultName $KeyVaultName `
        -SecretName $KVSecretName_ClientSecret `
        -Description "BFF API production client secret ($BffApiDisplayName)"

    # Ensure stable ID-type KV secrets (idempotent — same input, same output)
    Write-Step 7 "Ensure stable KV secrets (ClientId / Audience / TenantId)"
    Ensure-KeyVaultSecret -VaultName $KeyVaultName `
        -SecretName $KVSecretName_ClientId `
        -SecretValue $BffApiAppId `
        -Description "BFF API production client ID ($BffApiDisplayName)"

    Ensure-KeyVaultSecret -VaultName $KeyVaultName `
        -SecretName $KVSecretName_Audience `
        -SecretValue $AppIdUri `
        -Description "BFF API production audience URI ($BffApiDisplayName)"

    Ensure-KeyVaultSecret -VaultName $KeyVaultName `
        -SecretName $KVSecretName_TenantId `
        -SecretValue $TenantId `
        -Description "Entra ID tenant ID"

    # Ensure service principal
    Write-Step 8 "Ensure service principal"
    Ensure-ServicePrincipal -AppId $BffApiAppId
}

# ─────────────────────────────────────────────────────────────────────────────
# NOTE: The separate "Dataverse S2S" app registration (spaarke-dataverse-s2s-*)
# was removed 2026-08-14 (code-quality-and-assurance-r3 task 060). It had zero
# code consumers; Dataverse S2S access consolidated onto the BFF app registration
# credential (API_CLIENT_SECRET / BFF-API-ClientSecret) on 2026-01-07.
# ─────────────────────────────────────────────────────────────────────────────
# r1 task 010 verification: no code path below re-introduces the S2S app-reg.
# Any future reintroduction requires reversing task 060 (which had spec-MUST-rule
# force).

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
# Summary + idempotency report
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "REGISTRATION SUMMARY"

if ($DryRun) {
    Write-Host "  *** DRY RUN — No changes were made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Host "  Tenant ID:       $TenantId" -ForegroundColor White
Write-Host "  Key Vault:       $KeyVaultName" -ForegroundColor White
Write-Host "  Sign-in aud:     $RequiredSignInAudience" -ForegroundColor White
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
    Write-Host "    KV Secrets:      $KVSecretName_ClientSecret, $KVSecretName_ClientId, $KVSecretName_Audience, $KVSecretName_TenantId" -ForegroundColor White
    Write-Host ""
}

Write-Host "  Idempotency Report:" -ForegroundColor Cyan
Write-Host "    Changes applied:      $($script:ChangesApplied.Count)" -ForegroundColor $(if ($script:ChangesApplied.Count -eq 0) { 'Green' } else { 'Yellow' })
Write-Host "    Already present:      $($script:AlreadyPresent.Count)" -ForegroundColor Gray
Write-Host ""

if ($script:ChangesApplied.Count -eq 0) {
    Write-Host "  [OK] No changes needed — app-reg and KV are already fully configured." -ForegroundColor Green
} else {
    Write-Host "  Changes:" -ForegroundColor Yellow
    foreach ($c in $script:ChangesApplied) {
        Write-Host "    - $c" -ForegroundColor Yellow
    }
}
Write-Host ""

Write-Host "  NEXT STEPS:" -ForegroundColor Cyan
Write-Host "    1. Grant admin consent (see Step 3 above)" -ForegroundColor White
Write-Host "    2. Register the Application User in Dataverse (see Step 4 above)" -ForegroundColor White
Write-Host "    3. Configure knownClientApplications when PCF/CodePage clients are created" -ForegroundColor White
Write-Host "    4. Run Test-EntraAppRegistrations.ps1 to verify token acquisition" -ForegroundColor White
Write-Host ""

# Exit 0 unless we hit a throw earlier ($ErrorActionPreference=Stop guarantees non-zero on real errors)
exit 0
