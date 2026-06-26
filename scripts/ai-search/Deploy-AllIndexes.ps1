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
    `files-index, records-index, rag-references, insights-index,
    session-files, invoices-index, playbook-embeddings`. Default: deploy
    all 7.

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
    },
    @{
        Key        = 'playbook-embeddings'
        Name       = 'spaarke-playbook-embeddings'
        SchemaFile = 'infrastructure/ai-search/spaarke-playbook-embeddings.json'
        Invariants = @{
            VectorFields             = @('contentVector')
            RequiredFilterableFields = @()  # Playbook embeddings is global (no tenantId); playbook ID is the key
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
        Write-Error "No catalog entries matched -Indexes '$Indexes'. Valid keys: files-index, discovery-index, records-index, rag-references, insights-index, session-files, invoices-index, playbook-embeddings"
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
# Resolve admin key (live source-of-truth from search service itself; or KV)
# ---------------------------------------------------------------------------
Write-Host "Resolving admin key for $SearchServiceName..." -ForegroundColor Gray
if ($KeyVaultName) {
    $adminKey = az keyvault secret show `
        --vault-name $KeyVaultName `
        --name 'AiSearch--AdminKey' `
        --query value -o tsv 2>$null
    if (-not $adminKey) {
        Write-Host "  KV secret 'AiSearch--AdminKey' not found in $KeyVaultName — falling back to live admin key from search service." -ForegroundColor Yellow
        $adminKey = $null
    } else {
        Write-Host "  Admin key resolved from Key Vault." -ForegroundColor Gray
    }
}

if (-not $adminKey) {
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

$endpoint = "https://$SearchServiceName.search.windows.net"
$restHeaders = @{
    'api-key'      = $adminKey
    'Content-Type' = 'application/json'
}

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

foreach ($entry in $Catalog) {
    $name = $entry.Name
    $schemaPath = Join-Path $repoRoot $entry.SchemaFile
    $schemaJson = Get-Content $schemaPath -Raw

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
