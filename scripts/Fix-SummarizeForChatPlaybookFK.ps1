#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Idempotent migration: wire the `summarize-document-for-chat@v1` playbook node's
    action FK (sprk_actionid) → `SUM-CHAT@v1` action.

.DESCRIPTION
    Project: spaarke-ai-platform-unification-r6
    Task: 024 — Pillar 4 (Playbook FK fix; gates task 025 orchestrator refactor)
    Spec: FR-25 (FK chain validates at startup; summarize-document-for-chat@v1 → SUM-CHAT@v1 link populated)
    ADR-027 (unmanaged solution conventions for data fix)

    Closes the R5 closeout limitation (Gap B): the chat /summarize path currently
    uses `IGenericEntityService.RetrieveByAlternateKeyAsync` on `sprk_analysisaction`
    keyed by `sprk_actioncode = "SUM-CHAT@v1"` because the playbook → node → action FK
    chain has a NULL `sprk_actionid` on the node row. This script populates that FK so
    task 025 can refactor `SessionSummarizeOrchestrator` to invoke
    `PlaybookExecutionEngine.ExecuteAsync(playbookId)` instead.

.NOTES
    Idempotency contract:
      1. Verifies the playbook exists by sprk_name = 'summarize-document-for-chat@v1'.
      2. Verifies the SUM-CHAT@v1 action exists by sprk_actioncode = 'SUM-CHAT@v1'.
      3. Reads the playbook's node row and inspects sprk_actionid.
      4. If sprk_actionid already points at SUM-CHAT@v1 → NO-OP (logs + exits 0).
      5. Otherwise PATCHes the node to set sprk_actionid → SUM-CHAT@v1.
      6. Re-reads the node row and asserts the FK chain is valid; non-zero exit on mismatch.

    Rollback note (manual):
      Pre-fix state (captured 2026-06-08 during task 024 execution):
        - Node ID:    66b90f98-1b61-f111-ab0b-7c1e521b425f
        - sprk_name:  "summarize"
        - sprk_playbookid: 44285d15-1360-f111-ab0b-70a8a59455f4 (summarize-document-for-chat@v1)
        - sprk_actionid:   NULL  ← the broken FK
      Target action SUM-CHAT@v1:
        - sprk_analysisactionid: eeb05bfd-1260-f111-ab0b-70a8a59455f4
        - sprk_actioncode:       "SUM-CHAT@v1"
      To revert: PATCH the node row to set sprk_actionid back to NULL
      (Web API: PATCH sprk_playbooknodes(66b90f98-1b61-f111-ab0b-7c1e521b425f) with
      DELETE on the sprk_actionid@odata.bind navigation property).

    NFR-08 binding: no .cs changed; this is a pure data fix on existing rows.
    NFR-02 binding: BFF publish-size delta = 0 MB (no BFF code change).

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to spaarkedev1.

.PARAMETER WhatIf
    Preview-only mode: prints planned action without executing PATCH.

.EXAMPLE
    pwsh ./scripts/Fix-SummarizeForChatPlaybookFK.ps1

.EXAMPLE
    pwsh ./scripts/Fix-SummarizeForChatPlaybookFK.ps1 -WhatIf
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = 'Stop'

# Constants — anchor values per R6 task 024 spec.
$PlaybookName  = 'summarize-document-for-chat@v1'
$ActionCode    = 'SUM-CHAT@v1'

Write-Host "===== Fix-SummarizeForChatPlaybookFK =====" -ForegroundColor Cyan
Write-Host "Target env: $DataverseUrl"
Write-Host ""

# --- Authenticate ---
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) {
    throw "Failed to acquire token via 'az account get-access-token'. Run 'az login' first."
}
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-Version'    = '4.0'
    'OData-MaxVersion' = '4.0'
    'Content-Type'     = 'application/json'
    'If-Match'         = '*'
}
$apiBase = "$DataverseUrl/api/data/v9.2"

# --- Step 1: verify playbook exists ---
$playbookQuery = "$apiBase/sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_name&`$filter=sprk_name eq '$PlaybookName'"
$playbookResp = Invoke-RestMethod -Uri $playbookQuery -Headers $headers -Method GET
if (-not $playbookResp.value -or $playbookResp.value.Count -eq 0) {
    throw "Playbook '$PlaybookName' not found. Cannot proceed — task 011 deployment is the prerequisite."
}
$playbookId = $playbookResp.value[0].sprk_analysisplaybookid
Write-Host "[OK] Playbook '$PlaybookName' = $playbookId" -ForegroundColor Green

# --- Step 2: verify SUM-CHAT@v1 action exists ---
$actionQuery = "$apiBase/sprk_analysisactions?`$select=sprk_analysisactionid,sprk_actioncode,sprk_name&`$filter=sprk_actioncode eq '$ActionCode'"
$actionResp = Invoke-RestMethod -Uri $actionQuery -Headers $headers -Method GET
if (-not $actionResp.value -or $actionResp.value.Count -eq 0) {
    throw "Action '$ActionCode' not found. Cannot proceed — task 010 deployment is the prerequisite."
}
$actionId = $actionResp.value[0].sprk_analysisactionid
$actionName = $actionResp.value[0].sprk_name
Write-Host "[OK] Action  '$ActionCode' = $actionId  ($actionName)" -ForegroundColor Green

# --- Step 3: locate the playbook's node row ---
$nodeQuery = "$apiBase/sprk_playbooknodes?`$select=sprk_playbooknodeid,sprk_name,_sprk_actionid_value,_sprk_playbookid_value&`$filter=_sprk_playbookid_value eq $playbookId"
$nodeResp = Invoke-RestMethod -Uri $nodeQuery -Headers $headers -Method GET
if (-not $nodeResp.value -or $nodeResp.value.Count -eq 0) {
    throw "No playbook node rows found for playbook $playbookId. Cannot proceed — task 011 deployment is the prerequisite."
}
if ($nodeResp.value.Count -gt 1) {
    Write-Warning "Multiple nodes found for playbook (count=$($nodeResp.value.Count)). Filtering to the AI Analysis node by name='summarize'."
    $node = $nodeResp.value | Where-Object { $_.sprk_name -eq 'summarize' } | Select-Object -First 1
    if (-not $node) { throw "Multiple nodes but none named 'summarize'. Manual intervention required." }
} else {
    $node = $nodeResp.value[0]
}
$nodeId      = $node.sprk_playbooknodeid
$currentFk   = $node._sprk_actionid_value
Write-Host "[OK] Node    '$($node.sprk_name)' = $nodeId" -ForegroundColor Green
Write-Host "     Current sprk_actionid = $(if ($currentFk) { $currentFk } else { '<NULL>' })"

# --- Step 4: idempotency gate ---
if ($currentFk -eq $actionId) {
    Write-Host ""
    Write-Host "[NO-OP] Node FK already points at SUM-CHAT@v1. Nothing to do." -ForegroundColor Yellow
    Write-Host "FK chain: playbook($playbookId) -> node($nodeId) -> action($actionId)  ✅"
    exit 0
}

# --- Step 5: PATCH the node's action FK ---
$patchUri = "$apiBase/sprk_playbooknodes($nodeId)"
$body = @{ 'sprk_actionid@odata.bind' = "/sprk_analysisactions($actionId)" } | ConvertTo-Json -Compress

Write-Host ""
Write-Host "[PATCH] $patchUri"
Write-Host "        Body: $body"

if ($PSCmdlet.ShouldProcess($patchUri, "PATCH sprk_actionid = $actionId")) {
    Invoke-RestMethod -Uri $patchUri -Headers $headers -Method PATCH -Body $body | Out-Null
    Write-Host "[OK] PATCH complete." -ForegroundColor Green
} else {
    Write-Host "[WHATIF] Skipping PATCH (preview mode)." -ForegroundColor Yellow
    exit 0
}

# --- Step 6: re-verify FK chain ---
$verifyResp = Invoke-RestMethod -Uri "$apiBase/sprk_playbooknodes($nodeId)?`$select=sprk_playbooknodeid,sprk_name,_sprk_actionid_value,_sprk_playbookid_value" -Headers $headers -Method GET
$newFk = $verifyResp._sprk_actionid_value
if ($newFk -ne $actionId) {
    throw "Verification FAILED: node FK is '$newFk', expected '$actionId'. Manual investigation required."
}

Write-Host ""
Write-Host "===== FK chain valid =====" -ForegroundColor Cyan
Write-Host "  playbook  : $playbookId  ($PlaybookName)"
Write-Host "  node      : $nodeId  ($($verifyResp.sprk_name))"
Write-Host "  action    : $actionId  ($ActionCode)"
Write-Host ""
Write-Host "Task 025 (SessionSummarizeOrchestrator refactor) is now unblocked." -ForegroundColor Green
