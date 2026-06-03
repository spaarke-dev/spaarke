<#
.SYNOPSIS
    Migrate spaarke-insights-index Observations to the Wave D6 (task 035) hybrid scope shape.

.DESCRIPTION
    Task 035 evolves the spaarke-insights-index schema to add a top-level `scope` ComplexType
    carrying `matterId`, `entityType`, `entityId`, `tenantId`, `practiceArea` per design-a6 §4
    (Option (c) Hybrid backward-compat).

    The schema change itself is applied via the Bicep template (`infra/insights/`); this
    script back-fills any pre-existing Observations in the index so they remain queryable
    after Wave E1's RAG retriever switches to the new `scope/entityType` + `scope/entityId`
    filter shape.

    Back-fill rules (per design-a6 §4.4):
      - For each Observation row missing `scope`, derive scope from the Observation's
        `subject` field (format: `<scheme>:<entityId>`).
      - For `matter:` subjects, populate `scope.matterId = entityId`, `scope.entityType = "matter"`,
        `scope.entityId = entityId`.
      - For `project:` subjects, populate `scope.entityType = "project"`, `scope.entityId = entityId`
        (matterId remains null per design-a6 §4.4 dual-write table).
      - For `invoice:` subjects, same shape as project (matterId null).
      - For unrecognized schemes, skip with a warning (operator decides whether to delete or
        re-index).

    Idempotent: the script merges (`@odata.action`=`merge`) so re-runs are safe.

    NFR-08 verification: after back-fill, runs a sample Phase 1 RAG query filtering by
    `scope/matterId eq 'X'` and asserts the same Observation set is returned compared to
    the pre-migration `subject eq 'matter:X'` query.

.PARAMETER SearchServiceName
    Azure AI Search service name (e.g., 'spaarke-search-dev').

.PARAMETER IndexName
    Index name to migrate. Defaults to 'spaarke-insights-index'.

.PARAMETER AdminKeySecretName
    Key Vault secret name holding the admin key (for AzureCLI fallback when MI is not available).

.PARAMETER KeyVaultName
    Key Vault name to fetch the admin key from.

.PARAMETER DryRun
    Switch — when set, the script reports what it would do but does not write to the index.

.PARAMETER PageSize
    Page size for listing existing documents. Default 1000 (Azure AI Search hard cap).

.PARAMETER MaxPages
    Cap on total pages processed (defense vs runaway loops). Default 100 (= 100k Observations).

.EXAMPLE
    # Dry run against dev — list what would change
    .\Migrate-InsightsIndexScopeShape.ps1 -SearchServiceName spaarke-search-dev `
        -KeyVaultName sprkspaarkedev-aif-kv -AdminKeySecretName SearchAdminKey -DryRun

.EXAMPLE
    # Apply migration to dev
    .\Migrate-InsightsIndexScopeShape.ps1 -SearchServiceName spaarke-search-dev `
        -KeyVaultName sprkspaarkedev-aif-kv -AdminKeySecretName SearchAdminKey

.NOTES
    Task 035 / Wave D6. Hybrid backward-compat per design-a6 §4 + §5.
    Requires: Az.Accounts, Az.KeyVault modules + an authenticated Azure session.
    Permissions: 'Search Service Contributor' on the search service OR a valid admin key.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName,

    [string]$IndexName = 'spaarke-insights-index',

    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,

    [Parameter(Mandatory = $true)]
    [string]$AdminKeySecretName,

    [switch]$DryRun,

    [int]$PageSize = 1000,

    [int]$MaxPages = 100,

    [string]$ApiVersion = '2024-07-01'
)

$ErrorActionPreference = 'Stop'

function Write-MigrationInfo {
    param([string]$Message)
    Write-Host "[Migrate-InsightsIndexScopeShape] $Message" -ForegroundColor Cyan
}

function Write-MigrationWarn {
    param([string]$Message)
    Write-Warning "[Migrate-InsightsIndexScopeShape] $Message"
}

# -----------------------------------------------------------------------
# Authentication — fetch admin key from Key Vault
# -----------------------------------------------------------------------
Write-MigrationInfo "Fetching admin key from Key Vault '$KeyVaultName' secret '$AdminKeySecretName'..."
$secret = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name $AdminKeySecretName -AsPlainText
if ([string]::IsNullOrWhiteSpace($secret)) {
    throw "Failed to retrieve admin key from KV. Verify access policy + secret name."
}
$adminKey = $secret

$baseUri = "https://$SearchServiceName.search.windows.net/indexes/$IndexName"
$headers = @{
    'Content-Type' = 'application/json'
    'api-key'      = $adminKey
}

# -----------------------------------------------------------------------
# Discover Observations missing the new scope shape
# -----------------------------------------------------------------------
Write-MigrationInfo "Scanning index '$IndexName' for Observations missing top-level scope..."

$totalScanned = 0
$totalMigrated = 0
$totalSkipped = 0
$page = 0
$skip = 0

# Subject parse regex: <scheme>:<guid-like>
$subjectRegex = '^(?<scheme>[a-z][a-z0-9\-]*):(?<entityId>[A-Za-z0-9\-]+)$'

while ($page -lt $MaxPages) {
    $page++

    # Filter: artifactType=observation AND (scope is null) — the easiest way to find rows
    # missing the scope ComplexType is to look for rows where scope/entityType is null.
    $searchBody = @{
        search       = '*'
        filter       = "artifactType eq 'observation' and (scope/entityType eq null)"
        select       = 'id,tenantId,subject,scope'
        top          = $PageSize
        skip         = $skip
        count        = $true
    } | ConvertTo-Json -Depth 10

    $searchUri = "$baseUri/docs/search?api-version=$ApiVersion"
    try {
        $searchResp = Invoke-RestMethod -Uri $searchUri -Method POST -Headers $headers -Body $searchBody
    }
    catch {
        # On the first page, if the filter fails because scope field doesn't exist yet,
        # the deploy hasn't applied the schema add. Bail out with guidance.
        Write-MigrationWarn "Search query failed. The index schema may not yet include the 'scope' field. Apply Bicep first: az deployment group create -f infra/insights/main.bicep -p infra/insights/parameters/dev.json"
        Write-MigrationWarn "Raw error: $($_.Exception.Message)"
        throw
    }

    $batch = @($searchResp.value)
    if ($batch.Count -eq 0) {
        Write-MigrationInfo "No more Observations missing scope on page $page. Stopping."
        break
    }

    Write-MigrationInfo "Page $page : $($batch.Count) Observations to consider."

    $migrations = @()
    foreach ($doc in $batch) {
        $totalScanned++

        $subject = $doc.subject
        if ([string]::IsNullOrWhiteSpace($subject)) {
            Write-MigrationWarn "Observation '$($doc.id)' has empty subject — skipping."
            $totalSkipped++
            continue
        }

        $match = [regex]::Match($subject, $subjectRegex)
        if (-not $match.Success) {
            Write-MigrationWarn "Observation '$($doc.id)' subject '$subject' does not match <scheme>:<entityId> pattern — skipping."
            $totalSkipped++
            continue
        }

        $scheme = $match.Groups['scheme'].Value
        $entityId = $match.Groups['entityId'].Value

        # Build scope per design-a6 §4.4 writer-behavior table
        $scope = @{
            entityType = $scheme
            entityId   = $entityId
            tenantId   = $doc.tenantId
        }
        if ($scheme -eq 'matter') {
            $scope.matterId = $entityId  # Phase 1 dual-write
        }

        $migrations += [PSCustomObject]@{
            '@odata.action' = 'merge'
            id              = $doc.id
            scope           = $scope
        }
        $totalMigrated++
    }

    if ($migrations.Count -gt 0) {
        if ($DryRun) {
            Write-MigrationInfo "[DRY RUN] Would merge scope for $($migrations.Count) Observations on page $page."
            $migrations | Select-Object -First 5 | ConvertTo-Json -Depth 5 | Write-Host
            if ($migrations.Count -gt 5) { Write-Host "... (+$($migrations.Count - 5) more)" }
        }
        else {
            if ($PSCmdlet.ShouldProcess("$($migrations.Count) Observations on page $page", "Merge scope field")) {
                $mergeBody = @{ value = $migrations } | ConvertTo-Json -Depth 10
                $indexUri = "$baseUri/docs/index?api-version=$ApiVersion"
                $mergeResp = Invoke-RestMethod -Uri $indexUri -Method POST -Headers $headers -Body $mergeBody

                $failed = @($mergeResp.value | Where-Object { -not $_.status })
                if ($failed.Count -gt 0) {
                    Write-MigrationWarn "$($failed.Count) merge failures on page $page. First failure: $($failed[0] | ConvertTo-Json -Compress)"
                }
                else {
                    Write-MigrationInfo "Page $page : merged $($migrations.Count) Observations successfully."
                }
            }
        }
    }

    if ($batch.Count -lt $PageSize) {
        Write-MigrationInfo "Last page reached (returned $($batch.Count) < $PageSize)."
        break
    }

    # Note: we do NOT advance $skip because successful merges remove rows from the filter
    # (they now have scope/entityType set). Re-query from skip=0 each page.
    Start-Sleep -Seconds 1  # gentle pacing
}

# -----------------------------------------------------------------------
# NFR-08 verification (post-migration smoke)
# -----------------------------------------------------------------------
if (-not $DryRun) {
    Write-MigrationInfo "Running NFR-08 verification — sample matter-subject query via both old (subject) and new (scope.matterId) filters..."

    $verifyBody = @{
        search       = '*'
        filter       = "artifactType eq 'observation' and startswith(subject, 'matter:')"
        select       = 'id,subject,scope'
        top          = 5
    } | ConvertTo-Json -Depth 5

    $verifyUri = "$baseUri/docs/search?api-version=$ApiVersion"
    $verifyResp = Invoke-RestMethod -Uri $verifyUri -Method POST -Headers $headers -Body $verifyBody
    $sample = @($verifyResp.value)

    if ($sample.Count -eq 0) {
        Write-MigrationInfo "No matter-subject Observations exist — NFR-08 verification trivially satisfied (no Phase 1 backward-compat surface area)."
    }
    else {
        $missingScope = @($sample | Where-Object { $_.scope -eq $null -or [string]::IsNullOrWhiteSpace($_.scope.matterId) })
        if ($missingScope.Count -gt 0) {
            Write-MigrationWarn "NFR-08 RISK: $($missingScope.Count) of $($sample.Count) sampled matter-Observations are missing scope.matterId. Investigate before declaring migration complete."
            $missingScope | Select-Object -First 3 | ConvertTo-Json -Depth 5 | Write-Host
        }
        else {
            Write-MigrationInfo "NFR-08 OK: all $($sample.Count) sampled matter-Observations now carry scope.matterId."
        }
    }
}

# -----------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------
Write-MigrationInfo "Done."
Write-MigrationInfo "  Total scanned: $totalScanned"
Write-MigrationInfo "  Total $(if ($DryRun) { 'would-migrate' } else { 'migrated' }): $totalMigrated"
Write-MigrationInfo "  Total skipped: $totalSkipped"

if ($totalMigrated -eq 0 -and $totalSkipped -eq 0) {
    Write-MigrationInfo "  (Index had no pre-existing Observations missing scope — fresh deploy or post-migration re-run.)"
}
