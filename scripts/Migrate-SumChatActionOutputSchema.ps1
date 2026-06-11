<#
.SYNOPSIS
    Migrates the SUM-CHAT@v1 action + its playbook node for R6 Pillar 5 schema-aware
    rendering (task 032 / D-B-03).

.DESCRIPTION
    Two-part data migration on Spaarke Dev:

      (A) sprk_analysisaction row for sprk_actioncode = 'SUM-CHAT@v1'
          - VERIFIES that sprk_outputschemajson is populated with the canonical schema
            (tldr/summary/keywords/entities; declaration order load-bearing per R5 task 006
            spike + spec FR-02).
          - If the column is NULL, PATCHes the canonical schema from
            infra/dataverse/outputschemas/sum-chat-v1.schema.json (minified).
          - If the column is populated AND the SHAPE matches (same required fields, same
            property types, same declaration order), SKIPs — idempotent no-op.
          - If the column is populated AND the SHAPE mismatches, FAILS loudly with a
            side-by-side diff. Production data is NEVER silently overwritten.

      (B) sprk_playbooknode row(s) belonging to the
          'summarize-document-for-chat@v1' playbook
          - Reads existing sprk_configjson blob (currently a small object like
            {"__canvasNodeId":"...","__actionType":0} per R5 task 011 deployment).
          - Merges destination = 'chat' into the blob without disturbing existing keys.
          - PATCHes the merged blob back to Dataverse if a change is required.
          - Idempotent: if destination is already 'chat', skips with no PATCH.
          - Loudly FAILs if destination is already set to a non-'chat' value (would
            mean another migration touched this node first).

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
    infra/dataverse/outputschemas/sum-chat-v1.schema.json relative to the repo root.

.PARAMETER DryRun
    If specified, performs all reads + comparisons but no PATCH. Useful for verifying
    state without modifying Dataverse.

.EXAMPLE
    .\Migrate-SumChatActionOutputSchema.ps1
    # Default: migrate against Spaarke Dev; idempotent; PATCH if needed.

.EXAMPLE
    .\Migrate-SumChatActionOutputSchema.ps1 -DryRun
    # Dry-run mode — reports what WOULD be PATCHed without applying.

.NOTES
    Project: spaarke-ai-platform-unification-r6
    Task: 032 (D-B-03) — Migrate SUM-CHAT@v1 outputSchema + node destination = chat
    Sibling tasks (parallel B-G2): 033 (SUM-WORKSPACE@v1), 034 (matter-prefill), 035 (project-prefill)
    Downstream: 040/041 (StructuredOutputStreamWidget array+object rendering), 042 (CapabilityRouter dedup)
    Created: 2026-06-08

    ADR Compliance:
        ADR-027 (solution management): data PATCH via Web API; script committed; idempotent.
        ADR-029 (publish hygiene): no BFF code change — publish-size delta = 0 MB.
        ADR-010 (DI minimalism): N/A — no service registrations.

    Source of truth for the canonical schema:
        src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json
        (R5 D2-01 author surface; schema mirrored verbatim into the file below)
    Canonical schema file:
        infra/dataverse/outputschemas/sum-chat-v1.schema.json

    Consumer code (read-only reference; this script does NOT modify):
        src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:474-500
        (ResolveActionConfigViaFkChainAsync — feeds sprk_outputschemajson to
         _openAiClient.StreamStructuredCompletionAsync as the Structured Outputs schema).
        src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx
        (Task 040/041 — schema-aware array + object rendering; reads outputSchema via BFF.)
        src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs
        (Task 031 — per-node routing contract; this script writes the kebab-case
         "destination":"chat" field into sprk_playbooknode.sprk_configjson.)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [string]$SchemaFile = (Join-Path $PSScriptRoot ".." "infra" "dataverse" "outputschemas" "sum-chat-v1.schema.json"),

    [Parameter(Mandatory = $false, HelpMessage = "Dry-run: perform all reads + comparisons but no PATCH.")]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Constants
# -----------------------------------------------------------------------------

$ActionCode    = "SUM-CHAT@v1"
$PlaybookName  = "summarize-document-for-chat@v1"
$ExpectedDestination = "chat"
$RequiredOutputFields = @("tldr", "summary", "keywords", "entities")

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
        idempotency comparison. Returns a hashtable with required-field order +
        per-field type. Ignores cosmetic differences (descriptions, $id, title,
        comments).
    #>
    param([Parameter(Mandatory)] [string]$SchemaJson)

    try {
        $parsed = $SchemaJson | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return $null
    }

    $required = @($parsed.required)
    $properties = @{}
    if ($null -ne $parsed.properties) {
        foreach ($name in $parsed.properties.PSObject.Properties.Name) {
            $prop = $parsed.properties.$name
            $properties[$name] = $prop.type
        }
    }

    return @{
        Required        = $required
        PropertyTypes   = $properties
        TopLevelType    = $parsed.type
        AdditionalProps = $parsed.additionalProperties
    }
}

function Compare-SchemaShape {
    <#
    .SYNOPSIS
        Returns $true when two schema shapes are structurally equivalent for the
        purpose of "is the stored schema already the right shape". Equivalence
        means: same top-level type, same additionalProperties, same required
        field set (order matters — declaration order is load-bearing per R5
        task 006 spike), and same per-required-field type.
    #>
    param(
        [Parameter(Mandatory)] [hashtable]$Existing,
        [Parameter(Mandatory)] [hashtable]$Canonical
    )

    if ($Existing.TopLevelType -ne $Canonical.TopLevelType) { return $false }
    if ($Existing.AdditionalProps -ne $Canonical.AdditionalProps) { return $false }

    if ($Existing.Required.Count -ne $Canonical.Required.Count) { return $false }
    for ($i = 0; $i -lt $Canonical.Required.Count; $i++) {
        if ($Existing.Required[$i] -ne $Canonical.Required[$i]) { return $false }
    }

    foreach ($field in $Canonical.Required) {
        if (-not $Existing.PropertyTypes.ContainsKey($field)) { return $false }
        if ($Existing.PropertyTypes[$field] -ne $Canonical.PropertyTypes[$field]) { return $false }
    }

    return $true
}

# -----------------------------------------------------------------------------
# Step A — Verify / populate sprk_outputschemajson on SUM-CHAT@v1
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

    # Idempotent GET — find row by actioncode (Dataverse-side filter; not an alternate-key lookup)
    $select = "`$select=sprk_analysisactionid,sprk_actioncode,sprk_name,sprk_outputschemajson"
    $filter = "`$filter=sprk_actioncode eq '$ActionCode'"
    $endpoint = "sprk_analysisactions?$select&$filter"

    $resp = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $endpoint

    if ($resp.value.Count -eq 0) {
        throw "FATAL: sprk_analysisaction row for sprk_actioncode='$ActionCode' NOT FOUND on $BaseUrl. R6 task 032 cannot proceed; verify the R5 D2-01 action seed has been deployed (Deploy-Playbook.ps1 with summarize-document-for-chat.playbook.json)."
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

    # Verify required fields match what we expect (regardless of canonical comparison)
    foreach ($field in $RequiredOutputFields) {
        if ($existingShape.Required -notcontains $field) {
            throw "FATAL: existing schema missing required field '$field'. Required: $($existingShape.Required -join ', '). Manual investigation required."
        }
    }

    if (Compare-SchemaShape -Existing $existingShape -Canonical $canonicalShape) {
        Write-Host "  [SKIP] Existing schema SHAPE MATCHES canonical (required=$($canonicalShape.Required -join ','), all field types match). Idempotent no-op." -ForegroundColor Green
        return @{ Action = "Skipped"; ActionId = $actionId }
    }

    # Mismatch — surface diff and FAIL
    Write-Host ""
    Write-Host "  [FAIL] Existing schema SHAPE MISMATCHES canonical." -ForegroundColor Red
    Write-Host "  --- Canonical shape ---" -ForegroundColor Magenta
    Write-Host "    type:                $($canonicalShape.TopLevelType)"
    Write-Host "    additionalProps:     $($canonicalShape.AdditionalProps)"
    Write-Host "    required (order):    $($canonicalShape.Required -join ' -> ')"
    foreach ($k in $canonicalShape.PropertyTypes.Keys) {
        Write-Host "    properties[$k].type: $($canonicalShape.PropertyTypes[$k])"
    }
    Write-Host "  --- Existing shape ---" -ForegroundColor Magenta
    Write-Host "    type:                $($existingShape.TopLevelType)"
    Write-Host "    additionalProps:     $($existingShape.AdditionalProps)"
    Write-Host "    required (order):    $($existingShape.Required -join ' -> ')"
    foreach ($k in $existingShape.PropertyTypes.Keys) {
        Write-Host "    properties[$k].type: $($existingShape.PropertyTypes[$k])"
    }
    Write-Host ""
    throw "FATAL: existing sprk_outputschemajson shape mismatches canonical. Production data NOT overwritten. Manual investigation required — either bump the canonical schema in infra/dataverse/outputschemas/sum-chat-v1.schema.json, or fix the Dataverse row, then re-run."
}

# -----------------------------------------------------------------------------
# Step B — Set destination = 'chat' on the playbook node
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
    $pbFilter = "`$filter=sprk_name eq '$PlaybookName'"
    $pbEndpoint = "sprk_analysisplaybooks?$pbSelect&$pbFilter"

    $pb = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $pbEndpoint
    if ($pb.value.Count -eq 0) {
        throw "FATAL: sprk_analysisplaybook with name='$PlaybookName' NOT FOUND. R6 task 032 cannot proceed."
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

        # Add destination = 'chat' to the config (preserve all other properties)
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

        Write-Host "    [PATCH] sprk_configjson updated to: $mergedJson" -ForegroundColor Green
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
    Write-Host " Migrate SUM-CHAT@v1 outputSchema + node destination (R6 D-B-03)" -ForegroundColor Cyan
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
    $stepA = Migrate-ActionOutputSchema -Token $token -BaseUrl $EnvironmentUrl `
        -CanonicalSchemaJson $canonicalJson -IsWhatIf $isDryRun

    # Step B
    $stepB = Migrate-PlaybookNodeDestination -Token $token -BaseUrl $EnvironmentUrl `
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
