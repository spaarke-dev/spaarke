<#
.SYNOPSIS
    Migrates the ACT-023 ("New Matter Field Extraction") action + the "Create New Matter
    Pre-Fill" playbook AI Analysis node for R6 Pillar 5 schema-aware metadata (task 034 /
    D-B-05).

.DESCRIPTION
    Two-part data migration on Spaarke Dev. NFR-07 BINDING — this is a DATA-ONLY change
    on existing rows + existing columns. The pre-fill flow signatures + 45s timeout
    (MatterPreFillService.ExtractFieldsViaPlaybookAsync line 311) + useAiPrefill React
    hook + AiPreFillResult DTO contract REMAIN UNCHANGED. The metadata declared here
    describes the SAME data shape that already flows through the pipeline today.

      (A) sprk_analysisaction row for sprk_actioncode = 'ACT-023'
          (GUID 89cc641a-df18-f111-8343-7c1e520aa4df; sprk_name = 'New Matter Field
          Extraction'; referenced by playbook PB-008 'Create New Matter Pre-Fill' GUID
          2d660cad-d418-f111-8343-7ced8d1dc988 — the Workspace:PreFillPlaybookId default)
          - VERIFIES that sprk_outputschemajson is populated with the canonical schema
            (matterTypeName / practiceAreaName / matterName / matterDescription /
            assignedAttorneyName / assignedParalegalName / assignedOutsideCounselName /
            confidence — declaration order matches the AiPreFillResult DTO in
            MatterPreFillService.cs lines 678-703).
          - If the column is NULL (current state per task 030 evidence), PATCHes the
            canonical schema from infra/dataverse/outputschemas/matter-prefill.schema.json
            (minified).
          - If the column is populated AND the SHAPE matches (same required-or-absent
            field set, same per-field types, same additionalProperties policy), SKIPs —
            idempotent no-op.
          - If the column is populated AND the SHAPE mismatches, FAILS loudly with a
            side-by-side diff. Production data is NEVER silently overwritten.

          NOTE on shape comparison vs SUM-CHAT@v1: SUM-CHAT@v1 is consumed via Azure OpenAI
          Structured Outputs (declaration order load-bearing for streaming UX per R5 task
          006 spike). Matter pre-fill is consumed as a single completed JSON object via
          PlaybookEventType.NodeCompleted (MatterPreFillService line 338-345) followed by
          System.Text.Json deserialization (line 417) — declaration order is NOT
          load-bearing. The shape comparator below treats property SET (not order) as the
          equivalence criterion; this is a deliberate divergence from the SUM-CHAT
          comparator and matches the actual consumer semantics.

      (B) sprk_playbooknode row for the 'AI Analysis' node of playbook
          'Create New Matter Pre-Fill' (node GUID 444b06d3-d418-f111-8343-7ced8d1dc988)
          - Reads existing sprk_configjson blob (currently __canvasNodeId / __actionType /
            modelDeploymentId / systemPrompt per the deployed seed).
          - Merges destination = 'form-prefill' into the blob without disturbing existing
            keys.
          - PATCHes the merged blob back to Dataverse if a change is required.
          - Idempotent: if destination is already 'form-prefill', skips with no PATCH.
          - Loudly FAILs if destination is already set to a non-'form-prefill' value (would
            mean another migration touched this node first).

    Wire format for destination matches NodeRoutingConfig.cs / NodeDestinationJsonConverter
    (kebab-case: "chat", "workspace", "form-prefill", "side-effect"). This is task 031's
    contract.

    NFR-07 inspection-based verification (does NOT exercise the live wizard pre-fill
    flow — that's task 048's integration scope). The script confirms data-only changes;
    the dispatch evidence note documents the consumer-code read-only inspection.

    Read-then-act with safety: every PATCH is guarded by a GET; every comparison is logged;
    re-runs produce no destructive effects.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL. Defaults to $env:DATAVERSE_URL or
    https://spaarkedev1.crm.dynamics.com (Spaarke Dev).

.PARAMETER SchemaFile
    Optional override for the canonical schema document path. Defaults to
    infra/dataverse/outputschemas/matter-prefill.schema.json relative to the repo root.

.PARAMETER DryRun
    If specified, performs all reads + comparisons but no PATCH. Useful for verifying
    state without modifying Dataverse.

.EXAMPLE
    .\Migrate-MatterPrefillActionOutputSchema.ps1
    # Default: migrate against Spaarke Dev; idempotent; PATCH if needed.

.EXAMPLE
    .\Migrate-MatterPrefillActionOutputSchema.ps1 -DryRun
    # Dry-run mode — reports what WOULD be PATCHed without applying.

.NOTES
    Project: spaarke-ai-platform-unification-r6
    Task: 034 (D-B-05) — Migrate ACT-023 matter-prefill outputSchema + node destination = form-prefill
    Sibling tasks (parallel B-G2): 032 (SUM-CHAT@v1; complete), 033 (workspace-summarize; stop-and-surface),
                                    035 (project-prefill; in flight)
    Downstream: 048 (Phase B integration test — exercises live wizard pre-fill end-to-end)
    Created: 2026-06-09
    Modeled on: scripts/Migrate-SumChatActionOutputSchema.ps1 (task 032)

    ADR Compliance:
        ADR-013 (PublicContracts facade boundary): N/A — no facade signature changed.
                                                    Read-only inspection confirms IWorkspacePrefillAi
                                                    UNCHANGED (task evidence note documents this).
        ADR-027 (solution management): data PATCH via Web API; script committed; idempotent.
        ADR-029 (publish hygiene): no BFF code change — publish-size delta = 0 MB.
        ADR-010 (DI minimalism): N/A — no service registrations.
        NFR-07 (pre-fill preservation): BINDING — read-only inspection confirms
                                        MatterPreFillService.cs, useAiPrefill.ts, 45s timeout,
                                        Workspace:PreFillPlaybookId config UNCHANGED. Data-only
                                        change describes the same shape already in production.

    Source of truth for the canonical schema:
        src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs lines 678-703
        (AiPreFillResult internal DTO — the wire contract the wizard's useAiPrefill consumer reads
         via JsonSerializer.Deserialize at line 417). Field names are the JsonPropertyName
         attributes; nullability is the 'string?' / 'double?' init-only declaration.
    Canonical schema file:
        infra/dataverse/outputschemas/matter-prefill.schema.json

    Consumer code (read-only reference; this script does NOT modify):
        src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs
            (lines 281-397 — ExtractFieldsViaPlaybookAsync: 45s timeout + playbook stream + parse)
        src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs
            (facade; ExecutePlaybookAsync method — 53-line file, signature unchanged)
        src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts
            (React hook; consumer of the JSON output via fieldExtractor — 300-line file,
             contract unchanged)
        src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs
            (PreFillPlaybookId config key; default falls back to ACT-023 playbook GUID)
        src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs
            (Task 031 — per-node routing contract; this script writes the kebab-case
             "destination":"form-prefill" field into sprk_playbooknode.sprk_configjson.)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [string]$SchemaFile = (Join-Path $PSScriptRoot ".." "infra" "dataverse" "outputschemas" "matter-prefill.schema.json"),

    [Parameter(Mandatory = $false, HelpMessage = "Dry-run: perform all reads + comparisons but no PATCH.")]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Constants
# -----------------------------------------------------------------------------

$ActionCode             = "ACT-023"
$ActionName             = "New Matter Field Extraction"
$PlaybookName           = "Create New Matter Pre-Fill"
$ExpectedDestination    = "form-prefill"
# AiPreFillResult DTO field set (MatterPreFillService.cs lines 678-703). All fields are
# nullable on the DTO; the JSON Schema treats them as optional with type ["string","null"] or
# ["number","null"]. The schema is shape-equivalent, NOT order-load-bearing (pre-fill is not
# streamed to the UI; it's a single completed JSON object).
$ExpectedFieldSet       = @(
    "matterTypeName", "practiceAreaName", "matterName", "matterDescription",
    "assignedAttorneyName", "assignedParalegalName", "assignedOutsideCounselName",
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
# Schema-shape comparison (non-order-sensitive vs SUM-CHAT; see header NOTE)
# -----------------------------------------------------------------------------

function Get-SchemaShape {
    <#
    .SYNOPSIS
        Extracts a normalized shape descriptor from a JSON Schema document for
        idempotency comparison. Returns a hashtable with property name set + per-field
        primary type. Ignores cosmetic differences (descriptions, $id, title, comments,
        declaration order). This comparator is SET-BASED (not order-based) per the
        AiPreFillResult consumer semantics — see script header NOTE.
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
            # type may be a string ("string") or an array (["string","null"]).
            # Normalize to a sorted, lowercase, pipe-joined token for comparison.
            $typeToken = $null
            if ($null -ne $prop.type) {
                if ($prop.type -is [System.Array]) {
                    $typeToken = (($prop.type | ForEach-Object { "$_".ToLowerInvariant() }) | Sort-Object) -join "|"
                } else {
                    $typeToken = "$($prop.type)".ToLowerInvariant()
                }
            }
            $properties[$name] = $typeToken
        }
    }

    return @{
        Properties      = $properties
        PropertyNameSet = ([System.Collections.Generic.HashSet[string]]([string[]]$properties.Keys))
        TopLevelType    = $parsed.type
        AdditionalProps = $parsed.additionalProperties
    }
}

function Compare-SchemaShape {
    <#
    .SYNOPSIS
        Returns $true when two schema shapes are structurally equivalent. Equivalence
        means: same top-level type, same additionalProperties policy, same property name
        SET (declaration order ignored), and same normalized per-property type token.
    #>
    param(
        [Parameter(Mandatory)] [hashtable]$Existing,
        [Parameter(Mandatory)] [hashtable]$Canonical
    )

    if ($Existing.TopLevelType -ne $Canonical.TopLevelType) { return $false }
    if ($Existing.AdditionalProps -ne $Canonical.AdditionalProps) { return $false }

    if ($Existing.Properties.Count -ne $Canonical.Properties.Count) { return $false }

    foreach ($field in $Canonical.Properties.Keys) {
        if (-not $Existing.Properties.ContainsKey($field)) { return $false }
        if ($Existing.Properties[$field] -ne $Canonical.Properties[$field]) { return $false }
    }

    return $true
}

# -----------------------------------------------------------------------------
# Step A — Verify / populate sprk_outputschemajson on ACT-023
# -----------------------------------------------------------------------------

function Update-ActionOutputSchema {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$CanonicalSchemaJson,
        [bool]$IsWhatIf
    )

    Write-Host ""
    Write-Host "Step A: sprk_analysisaction sprk_actioncode='$ActionCode' ----" -ForegroundColor Cyan

    # Idempotent GET — find row by actioncode (Dataverse-side filter)
    $select = "`$select=sprk_analysisactionid,sprk_actioncode,sprk_name,sprk_outputschemajson"
    $filter = "`$filter=sprk_actioncode eq '$ActionCode'"
    $endpoint = "sprk_analysisactions?$select&$filter"

    $resp = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $endpoint

    if ($resp.value.Count -eq 0) {
        throw "FATAL: sprk_analysisaction row for sprk_actioncode='$ActionCode' NOT FOUND on $BaseUrl. R6 task 034 cannot proceed; verify the matter pre-fill seed (sprk_name='$ActionName') has been deployed."
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

    # Existing non-null — compare shape
    Write-Host "  Existing sprk_outputschemajson: POPULATED ($($existing.Length) chars) — comparing shape." -ForegroundColor Gray

    $existingShape = Get-SchemaShape -SchemaJson $existing
    $canonicalShape = Get-SchemaShape -SchemaJson $CanonicalSchemaJson

    if ($null -eq $existingShape) {
        throw "FATAL: existing sprk_outputschemajson is non-null but NOT VALID JSON. Manual cleanup required before re-running. Raw value (first 500 chars): $($existing.Substring(0, [Math]::Min(500, $existing.Length)))"
    }

    # Verify expected field set is present (the AiPreFillResult DTO contract)
    foreach ($field in $ExpectedFieldSet) {
        if (-not $existingShape.Properties.ContainsKey($field)) {
            throw "FATAL: existing schema missing expected field '$field'. Present: $($existingShape.Properties.Keys -join ', '). Manual investigation required."
        }
    }

    if (Compare-SchemaShape -Existing $existingShape -Canonical $canonicalShape) {
        Write-Host "  [SKIP] Existing schema SHAPE MATCHES canonical (fields=$($canonicalShape.Properties.Count); all per-field types match; additionalProperties policy match). Idempotent no-op." -ForegroundColor Green
        return @{ Action = "Skipped"; ActionId = $actionId }
    }

    # Mismatch — surface diff and FAIL
    Write-Host ""
    Write-Host "  [FAIL] Existing schema SHAPE MISMATCHES canonical." -ForegroundColor Red
    Write-Host "  --- Canonical shape ---" -ForegroundColor Magenta
    Write-Host "    type:                $($canonicalShape.TopLevelType)"
    Write-Host "    additionalProps:     $($canonicalShape.AdditionalProps)"
    Write-Host "    propertyCount:       $($canonicalShape.Properties.Count)"
    foreach ($k in $canonicalShape.Properties.Keys) {
        Write-Host "    properties[$k].type: $($canonicalShape.Properties[$k])"
    }
    Write-Host "  --- Existing shape ---" -ForegroundColor Magenta
    Write-Host "    type:                $($existingShape.TopLevelType)"
    Write-Host "    additionalProps:     $($existingShape.AdditionalProps)"
    Write-Host "    propertyCount:       $($existingShape.Properties.Count)"
    foreach ($k in $existingShape.Properties.Keys) {
        Write-Host "    properties[$k].type: $($existingShape.Properties[$k])"
    }
    Write-Host ""
    throw "FATAL: existing sprk_outputschemajson shape mismatches canonical. Production data NOT overwritten. Manual investigation required — either bump the canonical schema in infra/dataverse/outputschemas/matter-prefill.schema.json, or fix the Dataverse row, then re-run."
}

# -----------------------------------------------------------------------------
# Step B — Set destination = 'form-prefill' on the playbook node
# -----------------------------------------------------------------------------

function Update-PlaybookNodeDestination {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [bool]$IsWhatIf
    )

    Write-Host ""
    Write-Host "Step B: sprk_playbooknode for playbook '$PlaybookName' ----" -ForegroundColor Cyan

    # Find the playbook row by name
    $pbSelect = "`$select=sprk_analysisplaybookid,sprk_name"
    $pbFilter = "`$filter=sprk_name eq '$PlaybookName'"
    $pbEndpoint = "sprk_analysisplaybooks?$pbSelect&$pbFilter"

    $pb = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $pbEndpoint
    if ($pb.value.Count -eq 0) {
        throw "FATAL: sprk_analysisplaybook with name='$PlaybookName' NOT FOUND. R6 task 034 cannot proceed."
    }
    if ($pb.value.Count -gt 1) {
        throw "FATAL: $($pb.value.Count) playbooks found with name='$PlaybookName' — expected exactly 1."
    }

    $playbookId = $pb.value[0].sprk_analysisplaybookid
    Write-Host "  Found playbook id=$playbookId" -ForegroundColor Gray

    # Find the nodes (we'll write destination to ALL nodes referencing the matter-prefill action;
    # in practice this is the single 'AI Analysis' node — the 'Start' node has no action FK
    # and is excluded by the inner filter).
    $nodeSelect = "`$select=sprk_playbooknodeid,sprk_name,sprk_configjson,_sprk_actionid_value"
    $nodeFilter = "`$filter=_sprk_playbookid_value eq $playbookId"
    $nodeEndpoint = "sprk_playbooknodes?$nodeSelect&$nodeFilter"

    $nodes = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $nodeEndpoint

    if ($nodes.value.Count -eq 0) {
        throw "FATAL: NO playbook nodes found for playbook '$PlaybookName' (id=$playbookId). Cannot apply destination."
    }

    $results = @()

    foreach ($node in $nodes.value) {
        $nodeId = $node.sprk_playbooknodeid
        $actionFk = $node._sprk_actionid_value

        # Only target nodes that reference an action (skip the Start node which has no FK)
        if ([string]::IsNullOrWhiteSpace($actionFk)) {
            Write-Host "  Node id=$nodeId name='$($node.sprk_name)' has no action FK (e.g., Start node) — skipping." -ForegroundColor DarkGray
            $results += @{ NodeId = $nodeId; Action = "SkippedNoActionFk" }
            continue
        }

        Write-Host "  Node id=$nodeId name='$($node.sprk_name)' actionFk=$actionFk" -ForegroundColor Gray

        # Parse existing config (default to empty object if null/blank)
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

        # Add destination = 'form-prefill' to the config (preserve all other properties)
        $merged = [ordered]@{}
        foreach ($prop in $configObj.PSObject.Properties) {
            $merged[$prop.Name] = $prop.Value
        }
        $merged['destination'] = $ExpectedDestination

        # Serialize compact JSON
        $mergedJson = ($merged | ConvertTo-Json -Depth 50 -Compress)

        if ($IsWhatIf) {
            Write-Host "    [WhatIf] Would PATCH sprk_configjson to: $mergedJson" -ForegroundColor Magenta
            $results += @{ NodeId = $nodeId; Action = "WouldPatch"; NewConfig = $mergedJson }
            continue
        }

        Invoke-DataversePatch -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "sprk_playbooknodes($nodeId)" `
            -Body @{ "sprk_configjson" = $mergedJson }

        Write-Host "    [PATCH] sprk_configjson updated (added destination='$ExpectedDestination'; other keys preserved)." -ForegroundColor Green
        $results += @{ NodeId = $nodeId; Action = "Patched"; NewConfig = $mergedJson }
    }

    return $results
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " Migrate matter-prefill (ACT-023) outputSchema + node destination" -ForegroundColor Cyan
    Write-Host " R6 D-B-05 (task 034)  NFR-07 BINDING (data-only, code UNCHANGED)" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment   : $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Schema file   : $SchemaFile" -ForegroundColor Yellow
    Write-Host "DryRun mode   : $($DryRun.IsPresent)" -ForegroundColor Yellow
    Write-Host ""

    # Load canonical schema
    if (-not (Test-Path $SchemaFile)) {
        throw "FATAL: canonical schema file not found at '$SchemaFile'."
    }

    $canonicalRaw = Get-Content -Raw -Path $SchemaFile -Encoding UTF8
    # Normalize: parse + re-serialize compact so we send the exact same bytes that the
    # consumer (PlaybookExecutionEngine -> Azure OpenAI Structured Outputs) receives.
    try {
        $canonicalParsed = $canonicalRaw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "FATAL: canonical schema file '$SchemaFile' is NOT VALID JSON: $($_.Exception.Message)"
    }
    $canonicalJson = ($canonicalParsed | ConvertTo-Json -Depth 50 -Compress)
    Write-Host "Canonical schema loaded ($($canonicalJson.Length) chars normalized)." -ForegroundColor Green

    # Auth
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful." -ForegroundColor Green

    $isDryRun = [bool]$DryRun.IsPresent

    # Step A
    $stepA = Update-ActionOutputSchema -Token $token -BaseUrl $EnvironmentUrl `
        -CanonicalSchemaJson $canonicalJson -IsWhatIf $isDryRun

    # Step B
    $stepB = Update-PlaybookNodeDestination -Token $token -BaseUrl $EnvironmentUrl `
        -IsWhatIf $isDryRun

    # Summary
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
