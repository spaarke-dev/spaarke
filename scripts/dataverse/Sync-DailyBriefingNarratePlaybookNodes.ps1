<#
.SYNOPSIS
    Idempotent sync of sprk_configjson on the 6 sprk_playbooknode rows of the
    DAILY-BRIEFING-NARRATE playbook from the canonical source JSON.

.DESCRIPTION
    R7 Wave 11 task 115 (Option B closure).

    The 2026-06-29 R7 hotfix sequence (during Wave 10 task 101 UAT) PATCHed the
    ValidateEntityNames node's sprk_configjson with a smoke-test value
    `{"candidateText":"smoke test text","allowList":[]}` to get /narrate past the
    executor's validator while diagnosing the orchestrator gap. That smoke value
    still lives in spaarkedev1. Wave 11 tasks 111+112+113+114 closed the
    orchestrator gap (Layer 1 template resolution + custom helpers + flatMap +
    fan-out iteration). This script restores the source-correct template
    expressions which now resolve cleanly at runtime.

    The script reads the canonical playbook JSON at:
      projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json

    For each of the 6 nodes:
      - Locates the source node by name
      - Serializes its `configJson` block as a compact JSON string
      - PATCHes the corresponding sprk_playbooknode row by GUID

    Node GUIDs are pinned from the R4 deploy + Wave 5 backfill (MCP read_query
    2026-06-29 verified):
      Start                     32371fa5-a171-f111-ab0d-7ced8ddc4a05
      LoadKnowledge             0c895da7-a171-f111-ab0d-7ced8ddc4cc6
      GenerateTldr              0d895da7-a171-f111-ab0d-7ced8ddc4cc6
      GenerateChannelNarratives 10895da7-a171-f111-ab0d-7ced8ddc4cc6
      ValidateEntityNames       11895da7-a171-f111-ab0d-7ced8ddc4cc6
      ReturnResponse            12895da7-a171-f111-ab0d-7ced8ddc4cc6

    Idempotent — re-running with no source changes is functionally a no-op
    (Dataverse OData PATCH is a write, but the rendered JSON is byte-for-byte
    equal to the last sync).

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL or spaarkedev1.

.PARAMETER DryRun
    Print what would be PATCHed without applying.

.EXAMPLE
    ./Sync-DailyBriefingNarratePlaybookNodes.ps1 -DryRun
    Show derived configJson for all 6 nodes without writing.

.EXAMPLE
    ./Sync-DailyBriefingNarratePlaybookNodes.ps1
    Live sync on spaarkedev1.
#>
param(
    [string]$DataverseUrl = ($env:DATAVERSE_URL ?? 'https://spaarkedev1.crm.dynamics.com'),
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ApiBase = "$DataverseUrl/api/data/v9.2"

# Canonical node name → playbook-node GUID map (resolved 2026-06-29 via MCP read_query).
$Nodes = @(
    @{ Name = 'Start';                     Id = '32371fa5-a171-f111-ab0d-7ced8ddc4a05' },
    @{ Name = 'LoadKnowledge';             Id = '0c895da7-a171-f111-ab0d-7ced8ddc4cc6' },
    @{ Name = 'GenerateTldr';              Id = '0d895da7-a171-f111-ab0d-7ced8ddc4cc6' },
    @{ Name = 'GenerateChannelNarratives'; Id = '10895da7-a171-f111-ab0d-7ced8ddc4cc6' },
    @{ Name = 'ValidateEntityNames';       Id = '11895da7-a171-f111-ab0d-7ced8ddc4cc6' },
    @{ Name = 'ReturnResponse';            Id = '12895da7-a171-f111-ab0d-7ced8ddc4cc6' }
)

function Get-Headers {
    $AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
    $token = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if (-not $token) { throw "Failed to acquire access token. Run 'az login' first." }
    return @{
        'Authorization'    = "Bearer $token"
        'Content-Type'     = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Accept'           = 'application/json'
        'If-Match'         = '*'
    }
}

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$SourcePath = Join-Path $RepoRoot 'projects\spaarke-daily-update-service\notes\playbooks\daily-briefing-narrate.json'
if (-not (Test-Path $SourcePath)) {
    throw "Source playbook JSON not found: $SourcePath"
}

Write-Host ""
Write-Host "Sync-DailyBriefingNarratePlaybookNodes — $DataverseUrl" -ForegroundColor Cyan
Write-Host "Source: $SourcePath" -ForegroundColor DarkGray
if ($DryRun) { Write-Host "(DryRun — no writes)" -ForegroundColor Yellow }
Write-Host ""

# Parse source playbook JSON once. -AsHashtable so we can match node names case-sensitively
# AND preserve property order when serializing back.
$playbook = Get-Content $SourcePath -Raw | ConvertFrom-Json -AsHashtable

if (-not $playbook.nodes) {
    throw "Source playbook has no 'nodes' array"
}

# Build name → source node map
$sourceNodes = @{}
foreach ($n in $playbook.nodes) {
    if ($n.name) {
        $sourceNodes[$n.name] = $n
    }
}

$headers = if (-not $DryRun) { Get-Headers } else { $null }
$ok = 0
$err = 0

foreach ($n in $Nodes) {
    $sourceNode = $sourceNodes[$n.Name]
    if (-not $sourceNode) {
        Write-Host "  SKIP $($n.Name) ($($n.Id)) — not found in source JSON" -ForegroundColor Yellow
        continue
    }
    if (-not $sourceNode.configJson) {
        Write-Host "  SKIP $($n.Name) ($($n.Id)) — source node has no configJson block" -ForegroundColor Yellow
        continue
    }

    # Serialize the configJson block as a compact JSON string. -Depth 30 covers nested
    # template expressions + inputBinding objects.
    $configJson = $sourceNode.configJson | ConvertTo-Json -Depth 30 -Compress

    Write-Host "  $($n.Name) ($($n.Id)) — $($configJson.Length) chars"
    if ($DryRun) {
        Write-Host "    [DryRun] Would PATCH sprk_configjson" -ForegroundColor Yellow
        # Show first 200 chars so DryRun is informative without dumping huge blobs
        $preview = if ($configJson.Length -gt 200) { $configJson.Substring(0, 200) + '…' } else { $configJson }
        Write-Host "    $preview" -ForegroundColor DarkGray
        continue
    }

    try {
        $body = @{ sprk_configjson = $configJson } | ConvertTo-Json -Compress
        Invoke-RestMethod -Uri "$ApiBase/sprk_playbooknodes($($n.Id))" -Headers $headers -Method Patch -Body $body | Out-Null
        Write-Host "    PATCHED" -ForegroundColor Green
        $ok++
    } catch {
        Write-Host "    FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $err++
    }
}

Write-Host ""
Write-Host "Done. PATCHed: $ok  Errors: $err" -ForegroundColor Cyan
if ($err -gt 0) { exit 1 }
