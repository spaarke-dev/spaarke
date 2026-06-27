<#
.SYNOPSIS
    Migrates the project-prefill action (ACT-024) + its playbook node for R6 Pillar 5
    schema-aware rendering (task 035 / D-B-06). NFR-07 BINDING.

.DESCRIPTION
    Two-part data migration on Spaarke Dev. Mirrors task 032 (SUM-CHAT@v1) but is
    NFR-07-binding: the project pre-fill flow MUST behave identically after this
    migration. This script ONLY touches the data fields `sprk_outputschemajson` (on the
    action row) and `destination` (on the playbook node `sprk_configjson` blob). It does
    NOT modify any of the protected surfaces:

      - IWorkspacePrefillAi (PublicContract facade)
      - ProjectPreFillService.cs (45s timeout, AiProjectPreFillResult DTO)
      - useAiPrefill.ts (React hook)
      - Workspace:ProjectPreFillPlaybookId config

    Two-part migration:

      (A) sprk_analysisaction row for sprk_actioncode = 'ACT-024'
          ('New Project Field Extraction', id=1e838114-7919-f111-8343-7ced8d1dc988).
          - VERIFIES that sprk_outputschemajson is populated with the canonical schema
            (projectName, projectDescription, projectTypeName, practiceAreaName,
             assignedAttorneyName, assignedParalegalName, assignedOutsideCounselName,
             confidence — declaration order matches the playbook extractionSchema).
          - If the column is NULL, PATCHes the canonical schema from
            infra/dataverse/outputschemas/project-prefill.schema.json (minified).
          - If the column is populated AND the SHAPE matches (same property set, same
            top-level type), SKIPs — idempotent no-op.
          - If the column is populated AND the SHAPE mismatches, FAILS loudly with a
            side-by-side diff. Production data is NEVER silently overwritten.

      (B) sprk_playbooknode row(s) belonging to the
          'Create New Project Pre-Fill' playbook
          (id=fc343e9c-3460-f111-ab0b-7c1e521b425f, node id=0893d69d-3460-f111-ab0b-70a8a59455f4)
          - Reads existing sprk_configjson blob (contains the rich extractionSchema +
            extractionRules + parameters per playbook-architecture canvas authoring).
          - Merges destination = 'form-prefill' into the blob without disturbing existing
            keys (extractionSchema, extractionRules, parameters, temperature,
            responseFormat). The pre-fill flow's text-extraction + 45s timeout +
            `Workspace:ProjectPreFillPlaybookId` resolution path is UNCHANGED — the
            destination field is additive metadata read by R6 Pillar 5 routing.
          - PATCHes the merged blob back to Dataverse if a change is required.
          - Idempotent: if destination is already 'form-prefill', skips with no PATCH.
          - Loudly FAILs if destination is already set to a non-'form-prefill' value.

    Wire format for destination matches NodeRoutingConfig.cs / NodeDestinationJsonConverter
    (kebab-case: "chat", "workspace", "form-prefill", "side-effect"). This is task 031's
    contract.

    Read-then-act with safety: every PATCH is guarded by a GET; every comparison is logged;
    re-runs produce no destructive effects.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL. Defaults to $env:DATAVERSE_URL or
    https://spaarkedev1.crm.dynamics.com (Spaarke Dev).

.PARAMETER SchemaFile
    Optional override for the canonical schema document path. Defaults to
    infra/dataverse/outputschemas/project-prefill.schema.json relative to the repo root.

.PARAMETER DryRun
    If specified, performs all reads + comparisons but no PATCH. Useful for verifying
    state without modifying Dataverse.

.EXAMPLE
    .\Migrate-ProjectPrefillActionOutputSchema.ps1
    # Default: migrate against Spaarke Dev; idempotent; PATCH if needed.

.EXAMPLE
    .\Migrate-ProjectPrefillActionOutputSchema.ps1 -DryRun
    # Dry-run mode — reports what WOULD be PATCHed without applying.

.NOTES
    Project: spaarke-ai-platform-unification-r6
    Task: 035 (D-B-06) — Migrate project-prefill action outputSchema + node destination = form-prefill
    Sibling tasks (parallel B-G2): 032 (SUM-CHAT@v1 — completed), 033 (SUM-WORKSPACE@v1), 034 (matter-prefill)
    NFR-07 BINDING: pre-fill hook signatures + 45s timeout + useAiPrefill UNCHANGED.
    Created: 2026-06-09

    ADR Compliance:
        ADR-013 (PublicContracts facade boundary): IWorkspacePrefillAi unchanged.
        ADR-027 (solution management): data PATCH via Web API; script committed; idempotent.
        ADR-029 (publish hygiene): no BFF code change — publish-size delta = 0 MB.
        ADR-010 (DI minimalism): N/A — no service registrations.

    Source of truth for the canonical schema:
        infra/dataverse/outputschemas/project-prefill.schema.json
        (mirrors the existing wizard consumer contract: AiProjectPreFillResult DTO in
         ProjectPreFillService.cs L414-442 + ProjectPreFillResponse + useAiPrefill hook)

    Consumer code (read-only reference; this script does NOT modify):
        src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs
        (45s timeout L275; AiProjectPreFillResult L414-442; ParseAiResponse L349-396;
         playbook-id config resolution L262-265)
        src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs
        (PublicContract facade — ExecutePlaybookAsync signature)
        src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts
        (React hook — 60s client timeout, multipart form post, fuzzy lookup resolution)
        src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs
        (Task 031 — per-node routing contract; this script writes the kebab-case
         "destination":"form-prefill" field into sprk_playbooknode.sprk_configjson.)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [string]$SchemaFile = (Join-Path $PSScriptRoot ".." "infra" "dataverse" "outputschemas" "project-prefill.schema.json"),

    [Parameter(Mandatory = $false, HelpMessage = "Dry-run: perform all reads + comparisons but no PATCH.")]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Constants
# -----------------------------------------------------------------------------

$ActionCode    = "ACT-024"
$PlaybookName  = "Create New Project Pre-Fill"
$ExpectedDestination = "form-prefill"
# Field set the pre-fill flow consumes (AiProjectPreFillResult DTO L414-442). Used to
# verify any pre-existing schema actually has these fields; the DTO is the load-bearing
# contract.
$ExpectedFieldSet = @(
    "projectName",
    "projectDescription",
    "projectTypeName",
    "practiceAreaName",
    "assignedAttorneyName",
    "assignedParalegalName",
    "assignedOutsideCounselName",
    "confidence"
)

# -----------------------------------------------------------------------------
# Authentication
# -----------------------------------------------------------------------------

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Run 'az login' first."
    }

    return $tokenResult.Trim()
}

# -----------------------------------------------------------------------------
# Web API helpers
# -----------------------------------------------------------------------------

function Invoke-DataverseGet {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    try {
        return Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error (GET $Endpoint): $errorDetails"
    }
}

function Invoke-DataversePatch {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [object]$Body
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json"
        "If-Match"         = "*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $jsonBody = ($Body | ConvertTo-Json -Depth 50 -Compress)

    try {
        Invoke-RestMethod -Uri $uri -Method PATCH -Headers $headers -Body $jsonBody | Out-Null
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error (PATCH $Endpoint): $errorDetails"
    }
}

# -----------------------------------------------------------------------------
# Schema-shape comparison
# -----------------------------------------------------------------------------

function Get-SchemaShape {
    <#
    .SYNOPSIS
        Extracts a normalized shape descriptor from a JSON Schema document for
        idempotency comparison. Returns a hashtable with property-name set + top-level
        type + additionalProperties. Ignores cosmetic differences (descriptions, $id,
        title, comments). Unlike task 032 (SUM-CHAT@v1), the project-prefill schema has
        NO required array (the wizard tolerates partial outputs); order is NOT
        load-bearing — Structured Outputs is not used for pre-fill.
    #>
    param([Parameter(Mandatory)] [string]$SchemaJson)

    try {
        $parsed = $SchemaJson | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return $null
    }

    $properties = @{}
    if ($null -ne $parsed.properties) {
        foreach ($name in $parsed.properties.PSObject.Properties.Name) {
            $prop = $parsed.properties.$name
            # Property type may be a single string or an array (e.g., ["string", "null"]).
            # Normalize to a sorted-comma-joined string for comparison.
            $typeVal = $prop.type
            if ($typeVal -is [array]) {
                $typeVal = ($typeVal | Sort-Object) -join ','
            }
            $properties[$name] = $typeVal
        }
    }

    return @{
        PropertyTypes   = $properties
        PropertyNames   = @($parsed.properties.PSObject.Properties.Name | Sort-Object)
        TopLevelType    = $parsed.type
        AdditionalProps = $parsed.additionalProperties
    }
}

function Compare-SchemaShape {
    <#
    .SYNOPSIS
        Returns $true when two schema shapes are structurally equivalent for the
        purpose of "is the stored schema already the right shape". For project-prefill,
        equivalence means: same top-level type, same additionalProperties, same property
        name set, and same per-property type (order-insensitive — pre-fill is consumed
        as a complete JSON object, not streamed).
    #>
    param(
        [Parameter(Mandatory)] [hashtable]$Existing,
        [Parameter(Mandatory)] [hashtable]$Canonical
    )

    if ($Existing.TopLevelType -ne $Canonical.TopLevelType) { return $false }
    if ($Existing.AdditionalProps -ne $Canonical.AdditionalProps) { return $false }

    if ($Existing.PropertyNames.Count -ne $Canonical.PropertyNames.Count) { return $false }
    for ($i = 0; $i -lt $Canonical.PropertyNames.Count; $i++) {
        if ($Existing.PropertyNames[$i] -ne $Canonical.PropertyNames[$i]) { return $false }
    }

    foreach ($field in $Canonical.PropertyNames) {
        if (-not $Existing.PropertyTypes.ContainsKey($field)) { return $false }
        if ($Existing.PropertyTypes[$field] -ne $Canonical.PropertyTypes[$field]) { return $false }
    }

    return $true
}

# -----------------------------------------------------------------------------
# Step A — Verify / populate sprk_outputschemajson on ACT-024
# -----------------------------------------------------------------------------

function Migrate-ActionOutputSchema {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$CanonicalSchemaJson,
        [bool]$IsWhatIf
    )

    Write-Host ""
    Write-Host "Step A: sprk_analysisaction sprk_actioncode='$ActionCode' ----" -ForegroundColor Cyan

    $select = "`$select=sprk_analysisactionid,sprk_actioncode,sprk_name,sprk_outputschemajson"
    $filter = "`$filter=sprk_actioncode eq '$ActionCode'"
    $endpoint = "sprk_analysisactions?$select&$filter"

    $resp = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $endpoint

    if ($resp.value.Count -eq 0) {
        throw "FATAL: sprk_analysisaction row for sprk_actioncode='$ActionCode' NOT FOUND on $BaseUrl. R6 task 035 cannot proceed; verify the project-prefill action seed has been deployed."
    }

    if ($resp.value.Count -gt 1) {
        throw "FATAL: $($resp.value.Count) rows found for sprk_actioncode='$ActionCode' — expected exactly 1. Manual deduplication required before re-running."
    }

    $row = $resp.value[0]
    $actionId = $row.sprk_analysisactionid
    $existing = $row.sprk_outputschemajson

    Write-Host "  Found action row id=$actionId name='$($row.sprk_name)'" -ForegroundColor Gray

    if ($null -eq $existing) {
        Write-Host "  Existing sprk_outputschemajson: NULL — will PATCH canonical schema." -ForegroundColor Yellow
        if ($IsWhatIf) {
            Write-Host "  [WhatIf] Would PATCH sprk_outputschemajson with $($CanonicalSchemaJson.Length)-char canonical schema." -ForegroundColor Magenta
            return @{ Action = "WouldPatch"; ActionId = $actionId }
        }
        Invoke-DataversePatch -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "sprk_analysisactions($actionId)" `
            -Body @{ "sprk_outputschemajson" = $CanonicalSchemaJson }
        Write-Host "  [PATCH] sprk_outputschemajson populated ($($CanonicalSchemaJson.Length) chars)." -ForegroundColor Green
        return @{ Action = "Patched"; ActionId = $actionId }
    }

    Write-Host "  Existing sprk_outputschemajson: POPULATED ($($existing.Length) chars) — comparing shape." -ForegroundColor Gray

    $existingShape = Get-SchemaShape -SchemaJson $existing
    $canonicalShape = Get-SchemaShape -SchemaJson $CanonicalSchemaJson

    if ($null -eq $existingShape) {
        throw "FATAL: existing sprk_outputschemajson is non-null but NOT VALID JSON. Manual cleanup required before re-running. Raw value (first 500 chars): $($existing.Substring(0, [Math]::Min(500, $existing.Length)))"
    }

    # Verify the expected field set is present (regardless of canonical comparison).
    # This is the NFR-07 binding check: the consumer DTO drives this set.
    foreach ($field in $ExpectedFieldSet) {
        if (-not $existingShape.PropertyTypes.ContainsKey($field)) {
            throw "FATAL: existing schema missing expected field '$field' (from AiProjectPreFillResult DTO). Properties found: $($existingShape.PropertyNames -join ', '). Manual investigation required — NFR-07 binding requires the action's outputSchema to mirror the wizard consumer contract."
        }
    }

    if (Compare-SchemaShape -Existing $existingShape -Canonical $canonicalShape) {
        Write-Host "  [SKIP] Existing schema SHAPE MATCHES canonical (properties=$($canonicalShape.PropertyNames -join ','), all types match). Idempotent no-op." -ForegroundColor Green
        return @{ Action = "Skipped"; ActionId = $actionId }
    }

    Write-Host ""
    Write-Host "  [FAIL] Existing schema SHAPE MISMATCHES canonical." -ForegroundColor Red
    Write-Host "  --- Canonical shape ---" -ForegroundColor Magenta
    Write-Host "    type:                $($canonicalShape.TopLevelType)"
    Write-Host "    additionalProps:     $($canonicalShape.AdditionalProps)"
    Write-Host "    property names:      $($canonicalShape.PropertyNames -join ', ')"
    foreach ($k in $canonicalShape.PropertyTypes.Keys) {
        Write-Host "    properties[$k].type: $($canonicalShape.PropertyTypes[$k])"
    }
    Write-Host "  --- Existing shape ---" -ForegroundColor Magenta
    Write-Host "    type:                $($existingShape.TopLevelType)"
    Write-Host "    additionalProps:     $($existingShape.AdditionalProps)"
    Write-Host "    property names:      $($existingShape.PropertyNames -join ', ')"
    foreach ($k in $existingShape.PropertyTypes.Keys) {
        Write-Host "    properties[$k].type: $($existingShape.PropertyTypes[$k])"
    }
    Write-Host ""
    throw "FATAL: existing sprk_outputschemajson shape mismatches canonical. Production data NOT overwritten. Manual investigation required — either bump the canonical schema in infra/dataverse/outputschemas/project-prefill.schema.json, or fix the Dataverse row, then re-run."
}

# -----------------------------------------------------------------------------
# Step B — Set destination = 'form-prefill' on the playbook node
# -----------------------------------------------------------------------------

function Migrate-PlaybookNodeDestination {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [bool]$IsWhatIf
    )

    Write-Host ""
    Write-Host "Step B: sprk_playbooknode for playbook '$PlaybookName' ----" -ForegroundColor Cyan

    # Find the playbook row
    $pbSelect = "`$select=sprk_analysisplaybookid,sprk_name"
    # Escape single quote in playbook name (none here, but defensive)
    $pbNameEscaped = $PlaybookName.Replace("'", "''")
    $pbFilter = "`$filter=sprk_name eq '$pbNameEscaped'"
    $pbEndpoint = "sprk_analysisplaybooks?$pbSelect&$pbFilter"

    $pb = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $pbEndpoint
    if ($pb.value.Count -eq 0) {
        throw "FATAL: sprk_analysisplaybook with name='$PlaybookName' NOT FOUND. R6 task 035 cannot proceed."
    }
    if ($pb.value.Count -gt 1) {
        throw "FATAL: $($pb.value.Count) playbooks found with name='$PlaybookName' — expected exactly 1."
    }

    $playbookId = $pb.value[0].sprk_analysisplaybookid
    Write-Host "  Found playbook id=$playbookId" -ForegroundColor Gray

    # Find the nodes
    $nodeSelect = "`$select=sprk_playbooknodeid,sprk_name,sprk_configjson"
    $nodeFilter = "`$filter=_sprk_playbookid_value eq $playbookId"
    $nodeEndpoint = "sprk_playbooknodes?$nodeSelect&$nodeFilter"

    $nodes = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $nodeEndpoint

    if ($nodes.value.Count -eq 0) {
        throw "FATAL: NO playbook nodes found for playbook '$PlaybookName' (id=$playbookId). Cannot apply destination."
    }

    $results = @()

    foreach ($node in $nodes.value) {
        $nodeId = $node.sprk_playbooknodeid
        Write-Host "  Node id=$nodeId name='$($node.sprk_name)'" -ForegroundColor Gray

        $configObj = $null
        if (-not [string]::IsNullOrWhiteSpace($node.sprk_configjson)) {
            try {
                $configObj = $node.sprk_configjson | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                throw "FATAL: existing sprk_configjson on node $nodeId is NOT VALID JSON. Manual cleanup required. Raw value: $($node.sprk_configjson)"
            }
        }

        if ($null -eq $configObj) {
            $configObj = [PSCustomObject]@{}
        }

        # Check existing destination value
        $hasDestProp = $false
        $existingDestValue = $null
        $configObj.PSObject.Properties | ForEach-Object {
            if ($_.Name -eq 'destination') {
                $hasDestProp = $true
                $existingDestValue = $_.Value
            }
        }

        if ($hasDestProp) {
            if ($existingDestValue -eq $ExpectedDestination) {
                Write-Host "    [SKIP] destination already '$ExpectedDestination'. Idempotent no-op." -ForegroundColor Green
                $results += @{ NodeId = $nodeId; Action = "Skipped" }
                continue
            }
            else {
                throw "FATAL: node $nodeId already has destination='$existingDestValue' (expected '$ExpectedDestination' or unset). Another migration may have written first. Manual investigation required."
            }
        }

        # Merge destination = 'form-prefill' into the config (preserve all other properties:
        # extractionSchema, extractionRules, parameters, temperature, responseFormat, etc.)
        $merged = [ordered]@{}
        foreach ($prop in $configObj.PSObject.Properties) {
            $merged[$prop.Name] = $prop.Value
        }
        $merged['destination'] = $ExpectedDestination

        $mergedJson = ($merged | ConvertTo-Json -Depth 50 -Compress)

        if ($IsWhatIf) {
            $preview = if ($mergedJson.Length -gt 200) { $mergedJson.Substring(0, 200) + "..." } else { $mergedJson }
            Write-Host "    [WhatIf] Would PATCH sprk_configjson (preview): $preview" -ForegroundColor Magenta
            $results += @{ NodeId = $nodeId; Action = "WouldPatch"; NewConfigLength = $mergedJson.Length }
            continue
        }

        Invoke-DataversePatch -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "sprk_playbooknodes($nodeId)" `
            -Body @{ "sprk_configjson" = $mergedJson }

        Write-Host "    [PATCH] sprk_configjson updated (destination=$ExpectedDestination merged, $($mergedJson.Length) chars total)." -ForegroundColor Green
        $results += @{ NodeId = $nodeId; Action = "Patched"; NewConfigLength = $mergedJson.Length }
    }

    return $results
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " Migrate project-prefill outputSchema + node destination (R6 D-B-06)" -ForegroundColor Cyan
    Write-Host " ** NFR-07 BINDING — data fields ONLY; pre-fill flow UNCHANGED **" -ForegroundColor Yellow
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment   : $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Schema file   : $SchemaFile" -ForegroundColor Yellow
    Write-Host "DryRun mode   : $($DryRun.IsPresent)" -ForegroundColor Yellow
    Write-Host ""

    if (-not (Test-Path $SchemaFile)) {
        throw "FATAL: canonical schema file not found at '$SchemaFile'."
    }

    $canonicalRaw = Get-Content -Raw -Path $SchemaFile -Encoding UTF8
    try {
        $canonicalParsed = $canonicalRaw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "FATAL: canonical schema file '$SchemaFile' is NOT VALID JSON: $($_.Exception.Message)"
    }
    $canonicalJson = ($canonicalParsed | ConvertTo-Json -Depth 50 -Compress)
    Write-Host "Canonical schema loaded ($($canonicalJson.Length) chars normalized)." -ForegroundColor Green

    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful." -ForegroundColor Green

    $isDryRun = [bool]$DryRun.IsPresent

    $stepA = Migrate-ActionOutputSchema -Token $token -BaseUrl $EnvironmentUrl `
        -CanonicalSchemaJson $canonicalJson -IsWhatIf $isDryRun

    $stepB = Migrate-PlaybookNodeDestination -Token $token -BaseUrl $EnvironmentUrl `
        -IsWhatIf $isDryRun

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " Migration summary" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "  Action row: $($stepA.Action) (id=$($stepA.ActionId))"
    foreach ($r in $stepB) {
        Write-Host "  Node $($r.NodeId): $($r.Action)"
    }
    Write-Host ""
    if ($isDryRun) {
        Write-Host " DRY-RUN COMPLETE — no Dataverse modifications performed." -ForegroundColor Yellow
    }
    else {
        Write-Host " MIGRATION COMPLETE." -ForegroundColor Green
    }
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""

    return @{
        Action  = $stepA
        Nodes   = $stepB
        DryRun  = $isDryRun
    }
}

Main
