<#
.SYNOPSIS
    Removes builder-specific scope records and fixes the ACT--015 typo.

.DESCRIPTION
    1. Deletes all ACT-BUILDER-*, SKL-BUILDER-*, TL-BUILDER-* records
    2. Deletes uncoded duplicate tools (legacy seeding)
    3. Deletes uncoded "Summarize Content" action (duplicate of ACT-008)
    4. Fixes ACT--015 typo -> ACT-015

.PARAMETER Environment
    Target environment. Default: dev

.PARAMETER DryRun
    Preview changes without modifying Dataverse.
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$envMap = @{ 'dev' = 'https://spaarkedev1.crm.dynamics.com' }
$EnvironmentUrl = $envMap[$Environment]
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

Write-Host ''
Write-Host '=== Cleanup Builder Scopes ===' -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
if ($DryRun) { Write-Host 'Mode       : DRY RUN' -ForegroundColor Yellow }
else         { Write-Host 'Mode       : LIVE' -ForegroundColor Green }
Write-Host ''

# Authenticate
Write-Host '[1/3] Authenticating...' -ForegroundColor Yellow
$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv)
if (-not $token) { throw "Failed to get token. Run 'az login' first." }
Write-Host '  Token acquired.' -ForegroundColor Green

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
}

# ---------------------------------------------------------------------------
# Records to DELETE
# ---------------------------------------------------------------------------
$deletes = @(
    # Builder actions (no action code, builder-specific)
    @{ EntitySet = 'sprk_analysisactions'; Id = '47811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'ACT-BUILDER-001: Intent Classification' }
    @{ EntitySet = 'sprk_analysisactions'; Id = '77f5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'ACT-BUILDER-002: Node Configuration' }
    @{ EntitySet = 'sprk_analysisactions'; Id = '48811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'ACT-BUILDER-003: Scope Selection' }
    @{ EntitySet = 'sprk_analysisactions'; Id = '49811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'ACT-BUILDER-004: Scope Creation' }
    @{ EntitySet = 'sprk_analysisactions'; Id = '7bf5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'ACT-BUILDER-005: Build Plan Generation' }

    # Uncoded "Summarize Content" action (duplicate/legacy)
    @{ EntitySet = 'sprk_analysisactions'; Id = '96b1ca31-ae18-f111-8343-7c1e520aa4df'; Name = 'Summarize Content (uncoded, legacy)' }

    # Builder skills
    @{ EntitySet = 'sprk_analysisskills'; Id = '7cf5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'SKL-BUILDER-001: Lease Analysis Pattern' }
    @{ EntitySet = 'sprk_analysisskills'; Id = '7ff5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'SKL-BUILDER-002: Contract Review Pattern' }
    @{ EntitySet = 'sprk_analysisskills'; Id = '4a811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'SKL-BUILDER-003: Risk Assessment Pattern' }
    @{ EntitySet = 'sprk_analysisskills'; Id = '4b811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'SKL-BUILDER-004: Node Type Guide' }
    @{ EntitySet = 'sprk_analysisskills'; Id = '4c811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'SKL-BUILDER-005: Scope Matching' }

    # Builder tools
    @{ EntitySet = 'sprk_analysistools'; Id = '4d811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'TL-BUILDER-001: addNode' }
    @{ EntitySet = 'sprk_analysistools'; Id = '4e811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'TL-BUILDER-002: removeNode' }
    @{ EntitySet = 'sprk_analysistools'; Id = '4f811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'TL-BUILDER-003: createEdge' }
    @{ EntitySet = 'sprk_analysistools'; Id = '8bf5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'TL-BUILDER-004: updateNodeConfig' }
    @{ EntitySet = 'sprk_analysistools'; Id = '8cf5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'TL-BUILDER-005: linkScope' }
    @{ EntitySet = 'sprk_analysistools'; Id = '50811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'TL-BUILDER-006: createScope' }
    @{ EntitySet = 'sprk_analysistools'; Id = '91f5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'TL-BUILDER-007: searchScopes' }
    @{ EntitySet = 'sprk_analysistools'; Id = '51811a31-60f3-f011-8406-7c1e520aa4df'; Name = 'TL-BUILDER-008: autoLayout' }
    @{ EntitySet = 'sprk_analysistools'; Id = '93f5242f-60f3-f011-8406-7ced8d1dc988'; Name = 'TL-BUILDER-009: validateCanvas' }

    # Uncoded legacy tools (duplicates of coded TL-* tools)
    @{ EntitySet = 'sprk_analysistools'; Id = '5941674c-ece9-f011-8406-7ced8d1dc988'; Name = 'Clause Analyzer (uncoded, duplicate of TL-005 area)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '6d16974a-ece9-f011-8406-7c1e520aa4df'; Name = 'Clause Comparator (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '4d1a3045-9308-f111-8407-7c1e520aa4df'; Name = 'Dataverse Update Tool (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '5a41674c-ece9-f011-8406-7ced8d1dc988'; Name = 'Date Extractor (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '6916974a-ece9-f011-8406-7c1e520aa4df'; Name = 'Document Classifier (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '5841674c-ece9-f011-8406-7ced8d1dc988'; Name = 'Entity Extractor (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '4f1a3045-9308-f111-8407-7c1e520aa4df'; Name = 'Financial Calculation Tool (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '5b41674c-ece9-f011-8406-7ced8d1dc988'; Name = 'Financial Calculator (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '867d4aca-190e-f111-8342-7c1e520aa4df'; Name = 'Generic Analysis - Entity Extraction (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '8f61b244-9308-f111-8407-7ced8d1dc988'; Name = 'Invoice Extraction Tool (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '6b16974a-ece9-f011-8406-7c1e520aa4df'; Name = 'Risk Detector (uncoded, legacy)' }
    @{ EntitySet = 'sprk_analysistools'; Id = '6a16974a-ece9-f011-8406-7c1e520aa4df'; Name = 'Summary Generator (uncoded, legacy)' }
)

# ---------------------------------------------------------------------------
# Step 2: Delete records
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "[2/3] Deleting $($deletes.Count) builder/legacy records..." -ForegroundColor Yellow

$deleteCount = 0
foreach ($rec in $deletes) {
    if ($DryRun) {
        Write-Host "  WOULD DELETE: $($rec.Name)" -ForegroundColor Gray
        $deleteCount++
    } else {
        try {
            Invoke-RestMethod -Uri "$ApiBase/$($rec.EntitySet)($($rec.Id))" -Headers $headers -Method Delete
            Write-Host "  Deleted: $($rec.Name)" -ForegroundColor Red
            $deleteCount++
        } catch {
            Write-Warning "  Failed to delete $($rec.Name): $($_.Exception.Message)"
        }
    }
}
Write-Host "  $deleteCount record(s) deleted." -ForegroundColor White

# ---------------------------------------------------------------------------
# Step 3: Fix ACT--015 typo -> ACT-015
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[3/3] Fixing ACT--015 typo...' -ForegroundColor Yellow

$typoId = '5dc91c65-ebe9-f011-8406-7c1e520aa4df'
$fixBody = @{ sprk_actioncode = 'ACT-015' }

if ($DryRun) {
    Write-Host "  WOULD FIX: ACT--015 -> ACT-015 (Calculate Values)" -ForegroundColor Gray
} else {
    try {
        $jsonBody = $fixBody | ConvertTo-Json -Compress
        Invoke-RestMethod -Uri "$ApiBase/sprk_analysisactions($typoId)" -Headers $headers -Method Patch `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
        Write-Host "  Fixed: ACT--015 -> ACT-015 (Calculate Values)" -ForegroundColor Green
    } catch {
        Write-Warning "  Failed to fix typo: $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host 'Summary' -ForegroundColor Yellow
Write-Host "  Deleted: $deleteCount records (builder + legacy uncoded)" -ForegroundColor White
Write-Host "  Fixed  : ACT--015 -> ACT-015" -ForegroundColor White

if ($DryRun) {
    Write-Host "`n=== DRY RUN COMPLETE ===" -ForegroundColor Yellow
} else {
    Write-Host "`n=== Cleanup complete ===" -ForegroundColor Green
}
