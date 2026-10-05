#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Single canonical deployer for ALL 7 Spaarke AI Search indexes per FR-07.

.DESCRIPTION
    Catalog-driven deployer that PUTs all (or a subset of) the 7 canonical
    Spaarke AI Search index schemas declared in
    `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §4 against the target
    environment's search service (`spaarke-search-{env}`).

    Mirrors the validated structure of `scripts/Deploy-RedisCache.ps1` per
    the `spaarke-redis-cache-remediation-r1` 2026-06-26 handoff §6
    ("Bicep+PS hybrid is canonical — PS handles env-routing, KV secret,
    cross-resource wiring"). For AI Search, the schemas themselves are JSON
    (not Bicep modules), so this script is pure-PS — no Bicep deployment
    is invoked. The Insights index has a parallel Bicep authority at
    `infra/insights/modules/search-index.bicep` (per FR-11) — this script
    deploys the JSON form; the Insights team owns the Bicep parity for that
    one index.

    The script is idempotent (NFR-01) — Azure AI Search PUT verb creates or
    updates an index in place. Re-running against an already-deployed env is
    safe.

    Production and demo environments REJECT execution without an explicit
    `-Force` flag per NFR-05.

    The post-deploy invariant verifier (NFR-02) asserts the per-index
    invariants from the catalog §4 table after every deploy AND on
    `-VerifyOnly` runs. It fails fast (non-zero exit, logged diagnostic) on
    any violation.

.PARAMETER Environment
    Target environment: dev, staging, prod, or demo.

.PARAMETER ResourceGroup
    Override the resource group containing the AI Search service. Defaults
    by environment:
      dev     -> spe-infrastructure-westus2
      staging -> rg-spaarke-staging
      prod    -> rg-spaarke-prod
      demo    -> rg-spaarke-demo

.PARAMETER SearchServiceName
    Override the AI Search service name. Defaults to `spaarke-search-{env}`.

.PARAMETER KeyVaultName
    Key Vault holding the AI Search admin key under canonical secret name
    `AiSearch--AdminKey`. Required for `-CutoverBffSettings`; optional
    otherwise (script reads admin key directly from `az search admin-key`
    when KV is not provided).

.PARAMETER Indexes
    Optional subset filter — comma-separated short keys from the catalog:
    `files-index, discovery-index, records-index, rag-references,
    insights-index, session-files, invoices-index`. Default: deploy all.
    (The playbook-embeddings entry was retired by spaarke-ai-architecture-
    redesign-r1 task 035 / FR-P2-06 with the dispatcher stack.)

.PARAMETER DryRun
    Plan-only mode. Lists each index that would be deployed + invariants
    that would be asserted. No Azure resources are modified. Alias for
    `-WhatIf` ergonomics — both supported (NFR-06).

.PARAMETER VerifyOnly
    Skip deploy; run the post-deploy invariant verifier against the
    existing deployed indexes. Exits non-zero on any violation (NFR-02 /
    NFR-06).

.PARAMETER Force
    Required to target `prod` or `demo` environments per NFR-05. Without
    `-Force`, the script exits with code 2 and an NFR-05 message.

.PARAMETER CutoverBffSettings
    After successful deploy, update `spaarke-bff-{env}` App Service
    settings to use Key Vault references for AI Search admin key:
      AzureAISearchApiKey = @Microsoft.KeyVault(VaultName=...;SecretName=AiSearch--AdminKey)
      AiSearch__AdminKey  = @Microsoft.KeyVault(VaultName=...;SecretName=AiSearch--AdminKey)
    Requires `-KeyVaultName`.

.PARAMETER ApiVersion
    Azure AI Search REST API version. Defaults to `2024-07-01`.

.PARAMETER WhatIf
    Native PowerShell `-WhatIf` via `SupportsShouldProcess`. Shows planned
    actions only — no Azure resources are created or modified.

.EXAMPLE
    pwsh ./scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -DryRun

    Plan-only run against dev — prints which of the 7 indexes would deploy
    and which invariants would be asserted.

.EXAMPLE
    pwsh ./scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev

    Deploy all 7 schemas to dev, then run the post-deploy verifier.

.EXAMPLE
    pwsh ./scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev `
        -Indexes files-index,records-index,rag-references

    Deploy only the 3 named indexes; verifier runs against the same subset.

.EXAMPLE
    pwsh ./scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -VerifyOnly

    Skip deploy; run verifier against existing deployed indexes.

.EXAMPLE
    pwsh ./scripts/ai-search/Deploy-AllIndexes.ps1 -Environment prod -Force

    NFR-05 prod gate: -Force required.

.NOTES
    Project : spaarke-ai-azure-setup-dev-r1
    Version : 1.0.0
    Constraints:
      FR-07  — single unified deployer for all 7 indexes; per-index wrappers retired.
      NFR-01 — idempotent.
      NFR-02 — post-deploy verifier fails fast on policy violations.
      NFR-05 — reject prod/demo without `-Force`.
      NFR-06 — `-DryRun`/`-WhatIf` + `-VerifyOnly`.
      NFR-09 — schema property policy compliance verified per index.
      NFR-11 — vector dimensionality fixed at 3072 (text-embedding-3-large).
      NFR-12 — full 7-index deploy target runtime < 30 min.
      ADR-028 — KV references use @Microsoft.KeyVault(VaultName=...;SecretName=...) form.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod', 'demo')]
    [string]$Environment,

    [string]$ResourceGroup,

    [string]$SearchServiceName,

    [string]$KeyVaultName,

    [string]$Indexes,

    [switch]$DryRun,

    [switch]$VerifyOnly,

    [switch]$Force,

    [switch]$CutoverBffSettings,

    [string]$ApiVersion = '2024-07-01'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# ---------------------------------------------------------------------------
# Shared secret-free-marker detection (A38a canonical convention, reused by the
# A38c operator-script gates) — dot-sourced so this row (A43) does not
# reimplement marker-detection logic as a 4th independently-drifting copy
# (root CLAUDE.md §11 — extend, don't duplicate). Defines
# Test-SpaarkeSecretFreeMarker only; no top-level side effects.
# ---------------------------------------------------------------------------
. (Join-Path $PSScriptRoot '..' 'common' 'Assert-SpaarkeSecretFreeGate.ps1')

# ---------------------------------------------------------------------------
# NFR-05 prod/demo gate
# ---------------------------------------------------------------------------
if (($Environment -in @('prod', 'demo')) -and (-not $Force)) {
    Write-Host "NFR-05: -Environment $Environment requires -Force flag. This project must NOT touch prod/demo without explicit operator intent. Aborting." -ForegroundColor Red
    exit 2
}

# ---------------------------------------------------------------------------
# Resolve resource group + search service defaults by environment
# ---------------------------------------------------------------------------
if (-not $ResourceGroup) {
    $ResourceGroup = switch ($Environment) {
        'dev'     { 'spe-infrastructure-westus2' }
        'staging' { 'rg-spaarke-staging' }
        'prod'    { 'rg-spaarke-prod' }
        'demo'    { 'rg-spaarke-demo' }
    }
}

if (-not $SearchServiceName) {
    $SearchServiceName = "spaarke-search-$Environment"
}

# ---------------------------------------------------------------------------
# Index catalog (per AI-SEARCH-INDEX-CATALOG.md §4)
#
# Each entry declares:
#   - Key         : short selector key for the -Indexes param
#   - Name        : canonical index name (deployed to Azure AI Search)
#   - SchemaFile  : path to JSON schema relative to repo root
#   - Invariants  : verifier assertions (post-deploy)
#       - VectorFields            : names of fields that must be 3072-dim HNSW cosine
#       - RequiredFilterableFields: fields that MUST be filterable=true
#       - SemanticReferencesField : (optional) semantic config must reference this field name
#       - ForbiddenFieldNames     : (optional) field names that MUST NOT exist (e.g., 'domain' on rag-references — FR-17)
# ---------------------------------------------------------------------------
$Catalog = @(
    @{
        Key        = 'files-index'
        Name       = 'spaarke-files-index'
        SchemaFile = 'infrastructure/ai-search/spaarke-files-index.json'
        Invariants = @{
            VectorFields             = @('contentVector3072', 'documentVector3072')
            RequiredFilterableFields = @('tenantId', 'privilege_group_ids')
        }
    },
    @{
        Key        = 'discovery-index'
        Name       = 'spaarke-discovery-index'
        SchemaFile = 'infrastructure/ai-search/spaarke-discovery-index.json'
        Invariants = @{
            VectorFields             = @('contentVector3072', 'documentVector3072')
            RequiredFilterableFields = @('tenantId', 'privilege_group_ids')
        }
    },
    @{
        Key        = 'records-index'
        Name       = 'spaarke-records-index'
        SchemaFile = 'infrastructure/ai-search/spaarke-records-index.json'
        Invariants = @{
            VectorFields             = @('contentVector')
            RequiredFilterableFields = @('tenantId', 'recordType', 'dataverseRecordId', 'dataverseEntityName', 'privilege_group_ids')
        }
    },
    @{
        Key        = 'rag-references'
        Name       = 'spaarke-rag-references'
        SchemaFile = 'infrastructure/ai-search/spaarke-rag-references.json'
        Invariants = @{
            VectorFields             = @('contentVector3072')
            RequiredFilterableFields = @('tenantId', 'documentType', 'knowledgeSourceId')
            SemanticReferencesField  = 'documentType'   # FR-17: semantic config MUST reference documentType, not domain
            ForbiddenFieldNames      = @('domain')      # FR-17: 'domain' field renamed to 'documentType'
        }
    },
    @{
        Key        = 'insights-index'
        Name       = 'spaarke-insights-index'
        SchemaFile = 'infrastructure/ai-search/spaarke-insights-index.json'
        Invariants = @{
            VectorFields             = @('contentVector')
            RequiredFilterableFields = @('tenantId', 'artifactType')
        }
    },
    @{
        Key        = 'session-files'
        Name       = 'spaarke-session-files'
        SchemaFile = 'infrastructure/ai-search/spaarke-session-files.json'
        Invariants = @{
            VectorFields             = @('contentVector3072', 'documentVector3072')
            RequiredFilterableFields = @('tenantId', 'sessionId')   # ADR-014 canonical invariant — strict per-session tenant isolation
        }
    },
    @{
        Key        = 'invoices-index'
        Name       = 'spaarke-invoices-index'
        SchemaFile = 'infrastructure/ai-search/spaarke-invoices-index.json'
        Invariants = @{
            VectorFields             = @('contentVector')
            RequiredFilterableFields = @('tenantId', 'invoiceId', 'matterId', 'projectId')
        }
    }
)

# ---------------------------------------------------------------------------
# Filter catalog per -Indexes
# ---------------------------------------------------------------------------
if ($Indexes) {
    $selected = ($Indexes -split ',') | ForEach-Object { $_.Trim().ToLower() }
    $Catalog = $Catalog | Where-Object { $selected -contains $_.Key.ToLower() }
    if (-not $Catalog -or $Catalog.Count -eq 0) {
        Write-Error "No catalog entries matched -Indexes '$Indexes'. Valid keys: files-index, discovery-index, records-index, rag-references, insights-index, session-files, invoices-index"
        exit 3
    }
}

# ---------------------------------------------------------------------------
# Mode + banner
# ---------------------------------------------------------------------------
$planOnly = $DryRun -or $WhatIfPreference
$modeLabel = if ($VerifyOnly) {
    'verify-only'
} elseif ($planOnly) {
    'dry-run'
} else {
    'deploy'
}

Write-Host "Deploy-AllIndexes.ps1 starting" -ForegroundColor Cyan
Write-Host "  Environment       : $Environment"
Write-Host "  ResourceGroup     : $ResourceGroup"
Write-Host "  SearchService     : $SearchServiceName"
Write-Host "  KeyVault          : $(if ($KeyVaultName) { $KeyVaultName } else { '(not specified; reading admin key from az search)' })"
Write-Host "  Mode              : $modeLabel"
Write-Host "  Indexes selected  : $(($Catalog | ForEach-Object { $_.Name }) -join ', ')"
Write-Host "  ApiVersion        : $ApiVersion"
Write-Host ""

# ---------------------------------------------------------------------------
# Verify schema files exist locally (fail fast before touching Azure)
# ---------------------------------------------------------------------------
foreach ($entry in $Catalog) {
    $schemaPath = Join-Path $repoRoot $entry.SchemaFile
    if (-not (Test-Path $schemaPath)) {
        Write-Error "Schema file not found: $schemaPath (index '$($entry.Name)'). Cannot proceed."
        exit 4
    }
}

# ---------------------------------------------------------------------------
# Dry-run early exit (NFR-06)
# ---------------------------------------------------------------------------
if ($planOnly) {
    Write-Host "[DRY RUN] Plan:" -ForegroundColor Yellow
    foreach ($entry in $Catalog) {
        $schemaPath = Join-Path $repoRoot $entry.SchemaFile
        $required = $entry.Invariants.RequiredFilterableFields -join ', '
        $vectors  = $entry.Invariants.VectorFields -join ', '
        $semantic = if ($entry.Invariants.ContainsKey('SemanticReferencesField')) { $entry.Invariants.SemanticReferencesField } else { '(none)' }
        $forbidden = if ($entry.Invariants.ContainsKey('ForbiddenFieldNames')) { $entry.Invariants.ForbiddenFieldNames -join ', ' } else { '(none)' }
        Write-Host "  - PUT https://$SearchServiceName.search.windows.net/indexes/$($entry.Name)?api-version=$ApiVersion"
        Write-Host "      Schema    : $schemaPath"
        Write-Host "      Vectors   : $vectors (each must be 3072-dim, HNSW, cosine)"
        Write-Host "      Filterable: $(if ($required) { $required } else { '(none required)' })"
        Write-Host "      Semantic  : $semantic"
        Write-Host "      Forbidden : $forbidden"
    }
    Write-Host ""
    if ($CutoverBffSettings) {
        Write-Host "  - Would cut over App Settings on spaarke-bff-$Environment to KV refs:"
        Write-Host "      AzureAISearchApiKey = @Microsoft.KeyVault(VaultName=$KeyVaultName;SecretName=AiSearch--AdminKey)"
        Write-Host "      AiSearch__AdminKey  = @Microsoft.KeyVault(VaultName=$KeyVaultName;SecretName=AiSearch--AdminKey)"
    }
    Write-Host ""
    Write-Host "Dry run complete. No Azure resources modified." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# §10.5 trap 2 — silent admin-key re-mint gate (punch row A43)
#
# Precedent this extends: the -CutoverBffSettings switch further below already
# refuses when AiSearch__ManagedIdentity__Enabled=true on the BFF App Service,
# because cutting an App Setting over to an admin-key KV reference on a
# migrated env silently undoes the auth-v4 MI migration (task 053) while the
# run reports success. THIS gate applies that same shape to the GENERAL
# admin-key resolution path below, which runs on every invocation (deploy AND
# -VerifyOnly) — that resolution path, not the opt-in switch, is where the
# trap actually lives: a fresh `az search admin-key show` mint on a
# secret-free env silently re-introduces the key-based auth the migration
# removed, on every single deploy.
#
# Three-branch decision (punch row A43 / §10.5 trap 2):
#   Branch 1 — secret-free marker present (KV tag `spaarke-secret-free-identity`
#              = "true", the canonical convention landed by punch row A38a —
#              see `ISecretFreeMarkerApplier`/`ArmSecretFreeMarkerApplier`,
#              reconciled against A38c 2026-08-26) AND KV secret
#              'AiSearch--AdminKey' is missing -> FAIL LOUD, exit, and NEVER
#              call `az search admin-key show` — that call IS the silent Δ2
#              reversal this row exists to prevent.
#   Branch 2 — AiSearch__ManagedIdentity__Enabled=true on the BFF App Service
#              (same query shape as -CutoverBffSettings, for consistency) ->
#              acquire an AAD token for https://search.azure.com/ via
#              Get-AzAccessToken (Az.Accounts), falling back to
#              `az account get-access-token` when Az.Accounts is not loaded
#              in this pwsh session, and switch $restHeaders to a Bearer
#              token. No admin key is resolved in this branch at all.
#   Branch 3 — neither signal present (pre-migration env) -> current
#              KV-then-live-admin-key fallback, UNCHANGED, for backwards
#              compatibility.
#
# Model 1 (shared KV) vs Model 2 fleet consistency (§10.3): this gate checks
# exactly the ONE Key Vault it is given via -KeyVaultName and makes no
# assumption about vault topology — under Model 1 that is the single shared
# vault, under Model 2 the caller passes the ONE per-customer
# `kv-{customerId}-{secretsVer}` vault relevant to this invocation. It does
# NOT iterate an N-vault fleet itself (a missed marker on one vault in an
# N-vault Model 2 fleet is its own silent-skip failure per A38a's
# fleet-consistency note); fleet-level enumeration is A38a's proposed
# follow-up (SecretFreeMarkerConsistencyDetector, wired at the
# T8-probe/H13-aggregation layer — not this script).
#
# A38 tag-scheme status: FINALIZED, not a placeholder. Punch row A38a landed
# the canonical KV tag `spaarke-secret-free-identity=true`; this gate consumes
# it via the shared Test-SpaarkeSecretFreeMarker function dot-sourced above
# (scripts/common/Assert-SpaarkeSecretFreeGate.ps1) rather than inventing a
# parallel check.
#
# Exit-code note (deviation from the punch-row draft's illustrative "exit 6"):
# this script already uses exit code 6 for TWO distinct, pre-existing
# meanings (verify-only invariant violations; post-deploy invariant
# violations — see Invoke-PostDeployVerifier call sites below). Reusing 6 for
# a credential-security refusal would make one exit code mean three unrelated
# things, defeating this gate's own "FAIL LOUD, actionable" intent. Branch 1
# uses exit 10 and branch 2's own token-acquisition failure uses exit 11 —
# both otherwise unused in this script.
# ---------------------------------------------------------------------------
function Resolve-AiSearchAuthContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$Environment,
        [Parameter(Mandatory = $true)] [string]$SearchServiceName,
        [Parameter(Mandatory = $true)] [string]$ResourceGroup,
        [string]$KeyVaultName
    )

    $bffAppName = "spaarke-bff-$Environment"
    $bffRg      = "rg-spaarke-$Environment"

    # Branch 2 signal — mirrors the -CutoverBffSettings query shape exactly
    # (same App Service, same setting name, same query) for consistency.
    $miEnabled = az webapp config appsettings list --resource-group $bffRg --name $bffAppName `
        --query "[?name=='AiSearch__ManagedIdentity__Enabled'].value | [0]" -o tsv 2>$null

    if ($miEnabled -and "$miEnabled".Trim() -ieq 'true') {
        Write-Host "  '$bffAppName' has AiSearch__ManagedIdentity__Enabled=true — using AAD-token auth (no admin key resolved)." -ForegroundColor Gray

        $aadToken = $null
        if (Get-Command Get-AzAccessToken -ErrorAction SilentlyContinue) {
            try {
                $tokenResult = Get-AzAccessToken -ResourceUrl 'https://search.azure.com/'
                # Az.Accounts 3.0+ returns .Token as a SecureString by default (breaking change from
                # the earlier plain-string behavior); handle both so this doesn't silently degrade to
                # the literal string "System.Security.SecureString" as the Bearer token on newer
                # Az.Accounts installs (this environment: Az.Accounts 5.3.0, verified 2026-08-26).
                $aadToken = if ($tokenResult.Token -is [System.Security.SecureString]) {
                    [System.Net.NetworkCredential]::new('', $tokenResult.Token).Password
                } else {
                    $tokenResult.Token
                }
            } catch {
                Write-Host "  Get-AzAccessToken failed ($($_.Exception.Message)); falling back to 'az account get-access-token'." -ForegroundColor Yellow
                $aadToken = az account get-access-token --resource 'https://search.azure.com/' --query accessToken -o tsv
            }
        } else {
            $aadToken = az account get-access-token --resource 'https://search.azure.com/' --query accessToken -o tsv
        }

        if (-not $aadToken) {
            Write-Error "Failed to acquire an AAD access token for https://search.azure.com/ (tried Get-AzAccessToken, then 'az account get-access-token'). AiSearch__ManagedIdentity__Enabled=true on '$bffAppName' means this script MUST use the AAD-token path — it will NOT fall back to minting an admin key. Verify Az.Accounts / az CLI login, and that the caller's identity holds 'Search Index Data Contributor' (or 'Search Service Contributor') on '$SearchServiceName'."
            exit 11
        }

        return @{
            Headers      = @{ 'Authorization' = "Bearer $aadToken"; 'Content-Type' = 'application/json' }
            UsingAadAuth = $true
        }
    }

    Write-Host "Resolving admin key for $SearchServiceName..." -ForegroundColor Gray
    $adminKey = $null
    if ($KeyVaultName) {
        $adminKey = az keyvault secret show `
            --vault-name $KeyVaultName `
            --name 'AiSearch--AdminKey' `
            --query value -o tsv 2>$null
        if (-not $adminKey) {
            Write-Host "  KV secret 'AiSearch--AdminKey' not found in $KeyVaultName." -ForegroundColor Yellow

            # Branch 1 — refuse the silent re-mint on secret-free envs.
            if (Test-SpaarkeSecretFreeMarker -KeyVaultName $KeyVaultName) {
                Write-Error @"
REFUSED: 'AiSearch--AdminKey' not found in Key Vault '$KeyVaultName', and this vault carries the
auth-v4 secret-free migration marker (KV tag 'spaarke-secret-free-identity=true'). Minting a NEW
admin key via 'az search admin-key show' here would silently reverse the ADR-028 Amendment A4 /
Exception E-3 migration (closed 2026-08-24) while this run reports success — the exact §10.5
trap 2 this gate exists to close.

See the A38a omit contract:
  .claude/constraints/auth.md  (section: Server hardening — Exception E-3 CLOSED)
  projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-draft-punch-rows.md
    (row A38a)

If '$SearchServiceName' should authenticate with a managed identity instead, set
AiSearch__ManagedIdentity__Enabled=true on '$bffAppName' and re-run (this script's branch 2).
If this environment has NOT actually migrated, verify the KV tag before retrying — do not remove
this gate to "fix" the refusal.
"@
                exit 10
            }

            Write-Host "  Falling back to live admin key from search service." -ForegroundColor Yellow
        } else {
            Write-Host "  Admin key resolved from Key Vault." -ForegroundColor Gray
        }
    }

    if (-not $adminKey) {
        # Branch 3 — no secret-free marker, no MI flag: unchanged pre-migration fallback.
        $adminKey = az search admin-key show `
            --service-name $SearchServiceName `
            --resource-group $ResourceGroup `
            --query primaryKey -o tsv
        if (-not $adminKey) {
            Write-Error "Failed to resolve admin key for $SearchServiceName. Ensure you have Search Service Contributor (or Owner) role on the search service."
            exit 5
        }
        Write-Host "  Admin key resolved from live search service." -ForegroundColor Gray
    }

    return @{
        Headers      = @{ 'api-key' = $adminKey; 'Content-Type' = 'application/json' }
        UsingAadAuth = $false
    }
}

$authContext = Resolve-AiSearchAuthContext -Environment $Environment -SearchServiceName $SearchServiceName -ResourceGroup $ResourceGroup -KeyVaultName $KeyVaultName

$endpoint = "https://$SearchServiceName.search.windows.net"
$restHeaders = $authContext.Headers
$script:UsingAadAuth = $authContext.UsingAadAuth

# ---------------------------------------------------------------------------
# Post-deploy verifier — asserts per-index invariants from catalog §4
# ---------------------------------------------------------------------------
function Invoke-PostDeployVerifier {
    param(
        [Parameter(Mandatory = $true)] [hashtable]$Entry,
        [Parameter(Mandatory = $true)] [string]$Endpoint,
        [Parameter(Mandatory = $true)] [hashtable]$Headers,
        [Parameter(Mandatory = $true)] [string]$ApiVersion
    )

    $name = $Entry.Name
    $inv  = $Entry.Invariants
    $violations = @()

    Write-Host "  Verifier: $name..." -ForegroundColor Gray

    # Fetch deployed index definition
    try {
        $url = "$Endpoint/indexes/$name`?api-version=$ApiVersion"
        $deployed = Invoke-RestMethod -Uri $url -Method Get -Headers $Headers
    } catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
        return @("FETCH-FAILED (HTTP $statusCode): $($_.Exception.Message)")
    }

    # Invariant 1: index name matches expected
    if ($deployed.name -ne $name) {
        $violations += "name mismatch (expected '$name', got '$($deployed.name)')"
    }

    # Invariant 2: key field present
    $keyField = $deployed.fields | Where-Object { $_.key -eq $true }
    if (-not $keyField) {
        $violations += "no key field declared"
    }

    # Invariant 3: required-filterable fields exist + filterable=true
    foreach ($req in $inv.RequiredFilterableFields) {
        $f = $deployed.fields | Where-Object { $_.name -eq $req }
        if (-not $f) {
            $violations += "required field '$req' MISSING"
        } elseif (-not $f.filterable) {
            $violations += "required field '$req' exists but filterable=false (expected true)"
        }
    }

    # Invariant 4: vector fields exist + 3072-dim + HNSW + cosine
    foreach ($vname in $inv.VectorFields) {
        $vf = $deployed.fields | Where-Object { $_.name -eq $vname }
        if (-not $vf) {
            $violations += "vector field '$vname' MISSING"
            continue
        }
        if ($vf.dimensions -ne 3072) {
            $violations += "vector field '$vname' has dimensions=$($vf.dimensions) (expected 3072)"
        }
        $profileName = $vf.vectorSearchProfile
        if (-not $profileName) {
            $violations += "vector field '$vname' has no vectorSearchProfile"
            continue
        }
        $profile = $deployed.vectorSearch.profiles | Where-Object { $_.name -eq $profileName }
        if (-not $profile) {
            $violations += "vector field '$vname' references missing profile '$profileName'"
            continue
        }
        $algo = $deployed.vectorSearch.algorithms | Where-Object { $_.name -eq $profile.algorithm }
        if (-not $algo) {
            $violations += "profile '$profileName' references missing algorithm '$($profile.algorithm)'"
            continue
        }
        if ($algo.kind -ne 'hnsw') {
            $violations += "algorithm '$($algo.name)' kind='$($algo.kind)' (expected hnsw)"
        }
        if ($algo.hnswParameters -and $algo.hnswParameters.metric -ne 'cosine') {
            $violations += "algorithm '$($algo.name)' metric='$($algo.hnswParameters.metric)' (expected cosine)"
        }
    }

    # Invariant 5: forbidden field names (FR-17 etc.)
    if ($inv.ContainsKey('ForbiddenFieldNames')) {
        foreach ($forbidden in $inv.ForbiddenFieldNames) {
            $f = $deployed.fields | Where-Object { $_.name -eq $forbidden }
            if ($f) {
                $violations += "FORBIDDEN field '$forbidden' is present (catalog: this name was retired)"
            }
        }
    }

    # Invariant 6: semantic config references the canonical field name (FR-17 for rag-references)
    if ($inv.ContainsKey('SemanticReferencesField')) {
        $expectedField = $inv.SemanticReferencesField
        $semanticConfigs = $deployed.semantic.configurations
        if (-not $semanticConfigs) {
            $violations += "semantic config required (must reference '$expectedField') but no configurations declared"
        } else {
            $allReferenced = @()
            foreach ($cfg in $semanticConfigs) {
                if ($cfg.prioritizedFields.titleField) { $allReferenced += $cfg.prioritizedFields.titleField.fieldName }
                if ($cfg.prioritizedFields.prioritizedContentFields) { $allReferenced += ($cfg.prioritizedFields.prioritizedContentFields | ForEach-Object { $_.fieldName }) }
                if ($cfg.prioritizedFields.prioritizedKeywordsFields) { $allReferenced += ($cfg.prioritizedFields.prioritizedKeywordsFields | ForEach-Object { $_.fieldName }) }
            }
            if ($allReferenced -notcontains $expectedField) {
                $violations += "semantic config does not reference field '$expectedField' (referenced: $(($allReferenced | Sort-Object -Unique) -join ', '))"
            }
        }
    }

    return $violations
}

# ---------------------------------------------------------------------------
# Verify-only path (NFR-02 + NFR-06)
# ---------------------------------------------------------------------------
if ($VerifyOnly) {
    Write-Host "Verify-only mode: asserting invariants against deployed indexes..." -ForegroundColor Cyan
    $totalViolations = 0
    foreach ($entry in $Catalog) {
        $v = Invoke-PostDeployVerifier -Entry $entry -Endpoint $endpoint -Headers $restHeaders -ApiVersion $ApiVersion
        if ($v.Count -eq 0) {
            Write-Host "    [OK]  $($entry.Name)" -ForegroundColor Green
        } else {
            $totalViolations += $v.Count
            Write-Host "    [FAIL] $($entry.Name):" -ForegroundColor Red
            foreach ($violation in $v) {
                Write-Host "         - $violation" -ForegroundColor Red
            }
        }
    }
    Write-Host ""
    if ($totalViolations -gt 0) {
        Write-Host "Verify-only: $totalViolations invariant violation(s) found." -ForegroundColor Red
        exit 6
    }
    Write-Host "Verify-only: all invariants pass." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# Deploy each index (PUT is idempotent)
# ---------------------------------------------------------------------------
Write-Host "Deploying $($Catalog.Count) index(es)..." -ForegroundColor Cyan
$deployFailures = @()

function Remove-JsonCommentKeys {
    <#
    .SYNOPSIS
        Strips `"// rationale": "<text>",` documentation keys from a JSON schema
        file via regex (preserves array semantics).
    .DESCRIPTION
        Spaarke schemas use `"// rationale": "<documentation>"` keys to inline-comment
        per-field policy overrides (NFR-09 override discipline). Azure AI Search REST
        API rejects these as unknown field properties (HTTP 400).

        Regex-based strip (NOT JSON parse+serialize) chosen because PowerShell's
        ConvertTo-Json unwraps single-element arrays into objects — fatal for
        `vectorSearch.algorithms` and `.profiles` which are arrays.

        Pattern matches: `"//<anything>": "<anything>",` (with optional trailing
        comma) — only quoted-string-value comment fields. Other forms (numeric,
        nested-object comments) are NOT used in Spaarke schemas.
    #>
    param([Parameter(Mandatory)] [string]$JsonText)
    # Match: "//..." OR "_comment_..." keys with quoted string values.
    # Spaarke schemas use two comment-key conventions:
    #   1. `"// rationale": "<text>"` (most schemas — files, records, rag-references, etc.)
    #   2. `"_comment_": "<text>"` (insights-index — Bicep-influenced single underscore-wrapped key)
    # Both are stripped here as they're rejected by Azure REST API.
    $stripped = [regex]::Replace($JsonText, '"//[^"]*"\s*:\s*"[^"]*"\s*,?\s*', '')
    return [regex]::Replace($stripped, '"_comment_"\s*:\s*"[^"]*"\s*,?\s*', '')
}

foreach ($entry in $Catalog) {
    $name = $entry.Name
    $schemaPath = Join-Path $repoRoot $entry.SchemaFile
    $schemaRaw  = Get-Content $schemaPath -Raw
    $schemaJson = Remove-JsonCommentKeys $schemaRaw

    if ($PSCmdlet.ShouldProcess("index '$name' at $endpoint", "PUT schema from $($entry.SchemaFile)")) {
        try {
            $putUrl = "$endpoint/indexes/$name`?api-version=$ApiVersion"
            $null = Invoke-RestMethod -Uri $putUrl -Method Put -Headers $restHeaders -Body $schemaJson
            Write-Host "  [OK]  PUT $name" -ForegroundColor Green
        } catch {
            $statusCode = $null
            $body = $null
            if ($_.Exception.Response) {
                $statusCode = $_.Exception.Response.StatusCode.value__
                try { $body = $_.Exception.Response.Content.ReadAsStringAsync().Result } catch { }
            }
            Write-Host "  [FAIL] PUT $name (HTTP $statusCode): $($_.Exception.Message)" -ForegroundColor Red
            if ($body) { Write-Host "         body: $body" -ForegroundColor Red }
            $deployFailures += $name
            continue
        }
    }
}

if ($deployFailures.Count -gt 0) {
    Write-Host ""
    Write-Host "Deploy failed for $($deployFailures.Count) index(es): $($deployFailures -join ', ')" -ForegroundColor Red
    exit 7
}

# ---------------------------------------------------------------------------
# Post-deploy invariant verifier (NFR-02 — always runs after deploy)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Post-deploy invariant verifier:" -ForegroundColor Cyan
$totalViolations = 0
foreach ($entry in $Catalog) {
    $v = Invoke-PostDeployVerifier -Entry $entry -Endpoint $endpoint -Headers $restHeaders -ApiVersion $ApiVersion
    if ($v.Count -eq 0) {
        Write-Host "    [OK]  $($entry.Name)" -ForegroundColor Green
    } else {
        $totalViolations += $v.Count
        Write-Host "    [FAIL] $($entry.Name):" -ForegroundColor Red
        foreach ($violation in $v) {
            Write-Host "         - $violation" -ForegroundColor Red
        }
    }
}

if ($totalViolations -gt 0) {
    Write-Host ""
    Write-Host "Post-deploy verifier: $totalViolations invariant violation(s)." -ForegroundColor Red
    exit 6
}

# ---------------------------------------------------------------------------
# Optional: cut over BFF App Settings to Key Vault refs
# ---------------------------------------------------------------------------
if ($CutoverBffSettings) {
    if (-not $KeyVaultName) {
        Write-Error "-CutoverBffSettings requires -KeyVaultName."
        exit 8
    }
    $bffAppName = "spaarke-bff-$Environment"
    $bffRg      = "rg-spaarke-$Environment"
    $kvRef      = "@Microsoft.KeyVault(VaultName=$KeyVaultName;SecretName=AiSearch--AdminKey)"

    # ── GUARD (added 2026-08-25, spaarke-auth-v4-dataverse-MI task 090) ────────────────────────────
    # This switch writes AzureAISearchApiKey + AiSearch__AdminKey onto the BFF as Key Vault references.
    # Task 053 migrated the BFF's AI Search auth to the user-assigned managed identity and DELETED
    # AiSearch--AdminKey. Run against a migrated environment, this switch silently re-introduces the
    # key-based configuration that migration removed — and, because the secret is gone, points two live
    # app settings at a DANGLING Key Vault reference.
    #
    # NOTE: only this switch is affected. The script's index-management paths legitimately use an admin
    # key, which they READ (az search admin-key show) rather than regenerate.
    #
    # Two checks. The first is the one that matters — intent; the second catches the broken reference.
    $miEnabled = az webapp config appsettings list --resource-group $bffRg --name $bffAppName `
        --query "[?name=='AiSearch__ManagedIdentity__Enabled'].value | [0]" -o tsv 2>$null
    if ($miEnabled -and "$miEnabled".Trim() -ieq 'true') {
        Write-Host ""
        Write-Host "  '$bffAppName' already authenticates to AI Search with its managed identity" -ForegroundColor Yellow
        Write-Host "  (AiSearch__ManagedIdentity__Enabled=true). This switch would re-introduce key-based" -ForegroundColor Yellow
        Write-Host "  configuration and undo that migration (task 053)." -ForegroundColor Yellow
        Write-Host "  To roll back to key auth deliberately: recover AiSearch--AdminKey, set" -ForegroundColor Yellow
        Write-Host "  AiSearch__ManagedIdentity__Enabled=false, then re-run." -ForegroundColor Yellow
        Write-Host "  See docs/guides/auth-deployment-setup.md section 5.1." -ForegroundColor Yellow
        Write-Error "-CutoverBffSettings refused: '$bffAppName' is on managed-identity AI Search auth."
        exit 9
    }

    $kvSecretExists = az keyvault secret show --vault-name $KeyVaultName --name 'AiSearch--AdminKey' `
        --query id -o tsv 2>$null
    if (-not $kvSecretExists) {
        Write-Host ""
        Write-Host "  Key Vault secret 'AiSearch--AdminKey' does not exist in '$KeyVaultName'." -ForegroundColor Yellow
        Write-Host "  Both app settings would resolve to a dangling Key Vault reference." -ForegroundColor Yellow
        Write-Host "  It was deleted 2026-08-25 when the BFF moved to managed-identity AI Search auth." -ForegroundColor Yellow
        Write-Host "  See docs/guides/auth-deployment-setup.md section 4." -ForegroundColor Yellow
        Write-Error "-CutoverBffSettings refused: 'AiSearch--AdminKey' not found in '$KeyVaultName'."
        exit 9
    }

    if ($PSCmdlet.ShouldProcess("$bffAppName App Settings in $bffRg", "Cutover AI Search admin key settings to Key Vault reference")) {
        az webapp config appsettings set `
            --resource-group $bffRg `
            --name $bffAppName `
            --settings `
                "AzureAISearchApiKey=$kvRef" `
                "AiSearch__AdminKey=$kvRef" `
            --output none
        if ($LASTEXITCODE -ne 0) {
            Write-Error "BFF App Settings cutover failed (exit $LASTEXITCODE)"
            exit $LASTEXITCODE
        }
        Write-Host "  Cut over '$bffAppName' App Settings to KV references." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Deploy-AllIndexes.ps1 completed successfully." -ForegroundColor Green
exit 0
