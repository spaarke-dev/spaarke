<#
.SYNOPSIS
    Seed the sprk_playbookconsumer Dataverse table with the 6 initial
    consumer→playbook routing records used by the BFF IConsumerRoutingService
    (chat-routing-redesign-r1 Phase 1R, FR-1R-07).

.DESCRIPTION
    Idempotent UPSERT against the sprk_playbookconsumer table for the 6
    consumers being migrated off Workspace__*PlaybookId environment variables
    (FR-1R-05): matter-pre-fill, project-pre-fill, ai-summary, summarize-file,
    chat-summarize, email-analysis.

    Idempotency is guaranteed by UPSERT via the alternate key
    sprk_ConsumerTypeCodeEnvironment = (sprk_consumertype + sprk_consumercode +
    sprk_environment). Rerun is safe — existing records are updated (no-op if
    values unchanged); missing records are created.

    PRINTS all 6 rows BEFORE write for manual review (binding defense against
    the 2026-06-24 UAT-2 env-var-misconfigured failure mode that motivated
    this Phase 1R work).

    Playbook GUIDs are environment-specific. The default record set ships
    with the Dev GUIDs documented in spec.md FR-1R-05 + verified in task 028
    evidence. When seeding a NEW environment, the operator MUST update the
    $Records hashtable below with that environment's playbook GUIDs (look up
    via PAC CLI or Dataverse maker portal) before running.

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to DATAVERSE_URL environment variable.
    Example: https://spaarkedev1.crm.dynamics.com

.PARAMETER DryRun
    Print the records that would be written without actually calling Dataverse.

.PARAMETER SkipConfirm
    Skip the interactive confirmation prompt. Use in CI or non-interactive contexts.

.EXAMPLE
    .\Seed-PlaybookConsumers.ps1
    .\Seed-PlaybookConsumers.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
    .\Seed-PlaybookConsumers.ps1 -DryRun
    .\Seed-PlaybookConsumers.ps1 -SkipConfirm

.NOTES
    Author:           Spaarke AI Platform Team
    Created:          2026-06-24
    Task:             chat-routing-redesign-r1 / 028b (FR-1R-07)
    Entity:           sprk_playbookconsumer
    Entity set:       sprk_playbookconsumers
    Alternate key:    sprk_ConsumerTypeCodeEnvironment (consumertype + consumercode + environment)
    Auth:             Azure CLI (az account get-access-token). Run 'az login' first.
    Environment:      Dev = https://spaarkedev1.crm.dynamics.com
    Spec:             projects/spaarke-ai-platform-chat-routing-redesign-r1/spec.md § Phase 1R

    GUID provenance (Dev environment, as of 2026-06-24):
      matter-pre-fill     2d660cad-d418-f111-8343-7ced8d1dc988  (PB-008 Create New Matter Pre-Fill)
      project-pre-fill    fc343e9c-3460-f111-ab0b-7c1e521b425f  (Create New Project Pre-Fill)
      ai-summary          18cf3cc8-02ec-f011-8406-7c1e520aa4df  (PB-002 Document Profile)
      summarize-file      4a72f99c-a119-f111-8343-7ced8d1dc988  (PB-015 Summarize File)
      chat-summarize      44285d15-1360-f111-ab0b-70a8a59455f4  (summarize-document-for-chat@v1)
      email-analysis      bc71facf-6af1-f011-8406-7ced8d1dc988  (PB-003 Email Analysis)
      compose-summarize   47686eb1-9916-f111-8343-7c1e520aa4df  (Document Summary — added by spaarkeai-compose-r1 task 011)
#>

[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$DryRun,
    [switch]$SkipConfirm
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Records to seed — UPDATE GUIDs PER ENVIRONMENT before running.
# ---------------------------------------------------------------------------
# Schema notes:
#   sprk_consumertype:   stable consumer code (lowercase + hyphens)
#   sprk_consumercode:   sub-discriminator; "default" for canonical row
#   sprk_environment:    env scope ("*" = all envs)
#   sprk_priority:       0-1000; lower wins; 500 is the spec default
#   sprk_enabled:        true (soft-disable via Power Apps without delete)
#   sprk_matchconditions: null for default rows; JSON predicate per FR-1R-04 when scoping
#   sprk_playbookid:     lookup target — sprk_analysisplaybookid system PK GUID
#
# DEV GUIDS as of 2026-06-24; update for other environments.

$Records = @(
    @{
        Name              = 'Wizard New Matter Create'
        ConsumerType      = 'matter-pre-fill'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = '2d660cad-d418-f111-8343-7ced8d1dc988'
        PlaybookComment   = 'PB-008 Create New Matter Pre-Fill'
    },
    @{
        Name              = 'Wizard New Project Create'
        ConsumerType      = 'project-pre-fill'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = 'fc343e9c-3460-f111-ab0b-7c1e521b425f'
        PlaybookComment   = 'Create New Project Pre-Fill'
    },
    @{
        Name              = 'AI Summary (Document Profile)'
        ConsumerType      = 'ai-summary'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = '18cf3cc8-02ec-f011-8406-7c1e520aa4df'
        PlaybookComment   = 'PB-002 Document Profile'
    },
    @{
        Name              = 'Summarize File (Workspace)'
        ConsumerType      = 'summarize-file'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = '4a72f99c-a119-f111-8343-7ced8d1dc988'
        PlaybookComment   = 'PB-015 Summarize File'
    },
    @{
        Name              = 'Chat Summarize Document'
        ConsumerType      = 'chat-summarize'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = '44285d15-1360-f111-ab0b-70a8a59455f4'
        PlaybookComment   = 'summarize-document-for-chat@v1'
    },
    @{
        Name              = 'Email Analysis'
        ConsumerType      = 'email-analysis'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = 'bc71facf-6af1-f011-8406-7ced8d1dc988'
        PlaybookComment   = 'PB-003 Email Analysis'
    },
    @{
        Name              = 'Compose Whole-Document Summarize'
        ConsumerType      = 'compose-summarize'
        ConsumerCode      = 'default'
        Environment       = '*'
        Priority          = 500
        Enabled           = $true
        PlaybookId        = '47686eb1-9916-f111-8343-7c1e520aa4df'
        PlaybookComment   = 'Document Summary (PB-002 reused) — spaarkeai-compose-r1 task 011'
    }
)

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    $DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
    Write-Host "DataverseUrl not supplied - using default: $DataverseUrl" -ForegroundColor Yellow
}

$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase      = "$DataverseUrl/api/data/v9.2"
$EntitySet    = "sprk_playbookconsumers"

# ---------------------------------------------------------------------------
# Header banner
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Seed-PlaybookConsumers.ps1 ===" -ForegroundColor Cyan
Write-Host "Target:      $DataverseUrl"
Write-Host "Entity set:  $EntitySet"
Write-Host "Records:     $($Records.Count)"
if ($DryRun) { Write-Host "Mode:        DRY-RUN (no changes will be made)" -ForegroundColor Yellow }
else         { Write-Host "Mode:        LIVE" -ForegroundColor Green }
Write-Host ""

# ---------------------------------------------------------------------------
# Print all records BEFORE write (binding defense per FR-1R-07 + spec §1R notes)
# ---------------------------------------------------------------------------
Write-Host "[1/4] Records to seed:" -ForegroundColor Yellow
$Records | ForEach-Object {
    [PSCustomObject]@{
        Name         = $_.Name
        ConsumerType = $_.ConsumerType
        ConsumerCode = $_.ConsumerCode
        Environment  = $_.Environment
        Priority     = $_.Priority
        PlaybookId   = $_.PlaybookId
        Playbook     = $_.PlaybookComment
    }
} | Format-Table -AutoSize

if (-not ($DryRun -or $SkipConfirm)) {
    $response = Read-Host "Proceed with seeding? [y/N]"
    if ($response -notmatch '^[yY]$') {
        Write-Host "Aborted by operator." -ForegroundColor Yellow
        exit 0
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY-RUN: no Dataverse calls made. Exiting." -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------------------
# Auth
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/4] Authenticating with Azure CLI..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $DataverseUrl --query accessToken -o tsv 2>&1)
if (-not $token -or $token -match 'ERROR') {
    Write-Error "Failed to obtain access token. Run 'az login' and ensure you have access to $DataverseUrl."
    exit 1
}
Write-Host "  Token acquired." -ForegroundColor Green

$headers = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Prefer'           = 'return=representation'
}

# ---------------------------------------------------------------------------
# UPSERT loop via alternate-key URL — PATCH /sprk_playbookconsumers(altKey)
# Dataverse PATCH semantics on alternate-key URL = upsert: create when not
# found; update when found.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[3/4] UPSERT $($Records.Count) records via alternate key sprk_ConsumerTypeCodeEnvironment..." -ForegroundColor Yellow

$summary = @{
    Created       = 0
    Updated       = 0
    Failed        = 0
}

foreach ($r in $Records) {
    # Build the alternate-key URL fragment.
    # NOTE: Dataverse Web API alternate-key syntax allows multiple key=value
    # pairs comma-separated inside the parentheses. Values are URL-encoded.
    $ctEnc = [Uri]::EscapeDataString($r.ConsumerType)
    $ccEnc = [Uri]::EscapeDataString($r.ConsumerCode)
    $envEnc = [Uri]::EscapeDataString($r.Environment)
    $altKey = "sprk_consumertype='$ctEnc',sprk_consumercode='$ccEnc',sprk_environment='$envEnc'"
    $url = "$ApiBase/$EntitySet($altKey)"

    $body = @{
        'sprk_name'                  = $r.Name
        'sprk_consumertype'          = $r.ConsumerType
        'sprk_consumercode'          = $r.ConsumerCode
        'sprk_environment'           = $r.Environment
        'sprk_priority'              = $r.Priority
        'sprk_enabled'               = $r.Enabled
        'sprk_playbook@odata.bind'   = "/sprk_analysisplaybooks($($r.PlaybookId))"
    } | ConvertTo-Json -Depth 5

    try {
        $resp = Invoke-WebRequest -Uri $url -Headers $headers -Method Patch -Body $body -UseBasicParsing
        # 201 Created vs 204 No Content (updated). Some PATCHes return 200 OK when Prefer=return=representation.
        $status = [int]$resp.StatusCode
        if ($status -in 201) {
            $summary.Created++
            Write-Host "  CREATED  $($r.ConsumerType.PadRight(22)) -> $($r.PlaybookId)" -ForegroundColor Green
        } else {
            $summary.Updated++
            Write-Host "  UPDATED  $($r.ConsumerType.PadRight(22)) -> $($r.PlaybookId)" -ForegroundColor Cyan
        }
    }
    catch {
        $summary.Failed++
        $errMsg = $_.Exception.Message
        Write-Host "  FAILED   $($r.ConsumerType.PadRight(22)) -> $errMsg" -ForegroundColor Red
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/4] Summary:" -ForegroundColor Yellow
Write-Host "  Created: $($summary.Created)" -ForegroundColor Green
Write-Host "  Updated: $($summary.Updated)" -ForegroundColor Cyan
$failedColor = if ($summary.Failed -gt 0) { 'Red' } else { 'Gray' }
Write-Host "  Failed:  $($summary.Failed)" -ForegroundColor $failedColor
Write-Host ""

if ($summary.Failed -gt 0) {
    Write-Host "One or more records failed. Investigate above output." -ForegroundColor Red
    exit 1
}

Write-Host "Seed complete. Verify in Power Apps maker portal or via:" -ForegroundColor Green
Write-Host "  read_query('SELECT sprk_name, sprk_consumertype, sprk_playbook FROM sprk_playbookconsumer ORDER BY sprk_consumertype')" -ForegroundColor Gray
Write-Host ""
exit 0
