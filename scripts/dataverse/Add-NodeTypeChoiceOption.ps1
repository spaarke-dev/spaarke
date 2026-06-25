<#
.SYNOPSIS
    Adds the `DeliverComposite` (100000004) option to the
    `sprk_playbooknode.sprk_nodetype` choice column in Dataverse.

.DESCRIPTION
    chat-routing-redesign-r1 / Track 1 — Production smoke unblocker.

    Phase 5R Wave 5-C introduced a new NodeType `DeliverComposite` (FR-52, ADR-037)
    that the C# `NodeType` enum exposes at ordinal `100000004`. The
    `sprk_playbooknode.sprk_nodetype` choice column in Dataverse must add the
    matching option value before the 118R migration (
    `infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json`)
    can be applied.

    Why this script exists: the MCP `update_table` tool can only add COLUMNS
    (not option-set values), and the matching `dataverse-create-schema` skill
    does not expose `InsertOptionValue`. This script wraps the Dataverse
    Web API `InsertOptionValue` action so the schema delta is reproducible
    and idempotent.

    Idempotency: the script first retrieves the current option list via
    `RetrieveAttributeRequest`. If `100000004` is already present, it logs
    "already exists" and exits 0 without making any state change. Re-runs
    are safe.

.PARAMETER Environment
    Target Dataverse environment label. Default: dev.

.PARAMETER DataverseUrl
    Base Dataverse URL (e.g. `https://contoso.crm.dynamics.com`). Defaults to
    the `DATAVERSE_URL` env var.

.PARAMETER DryRun
    Preview the operation without making the InsertOptionValue call.

.EXAMPLE
    # Dry run
    .\Add-NodeTypeChoiceOption.ps1 -DryRun

.EXAMPLE
    # Apply to dev (default)
    .\Add-NodeTypeChoiceOption.ps1

.NOTES
    Prerequisites:
      - PowerShell 7+
      - Azure CLI installed and authenticated (`az login`)
      - Access to the target Dataverse environment as System Customizer / System Administrator

    Related deliverables:
      - chat-routing-redesign-r1 FR-52 / ADR-037 (multi-node Output composition)
      - scripts/Deploy-Playbook.ps1 NodeTypeMap (must include 'DeliverComposite' = 100000004)
      - 118R migration: infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json

    ADR-015 telemetry: the script emits ONLY the EntityLogicalName / AttributeLogicalName
    / option-value / option-label (all deterministic schema identifiers). Bearer tokens
    are never logged.
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',

    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Constants — bound to ADR-037 / FR-52
# ---------------------------------------------------------------------------
$EntityLogicalName    = 'sprk_playbooknode'
$AttributeLogicalName = 'sprk_nodetype'
$OptionValue          = 100000004
$OptionLabel          = 'DeliverComposite'
$LanguageCode         = 1033  # en-US

# ---------------------------------------------------------------------------
# Environment URL resolution
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Add NodeType Choice Option (FR-52 / ADR-037) ===' -ForegroundColor Cyan
Write-Host "Environment      : $EnvironmentUrl"
Write-Host "Entity           : $EntityLogicalName"
Write-Host "Attribute        : $AttributeLogicalName"
Write-Host "Option Value     : $OptionValue"
Write-Host "Option Label     : $OptionLabel"
if ($DryRun) {
    Write-Host 'Mode             : DRY RUN' -ForegroundColor Yellow
} else {
    Write-Host 'Mode             : LIVE' -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Step 1: Authenticate via Azure CLI
# ---------------------------------------------------------------------------
Write-Host '=== Step 1: Authenticate ===' -ForegroundColor Cyan

try {
    $tokenJson = az account get-access-token --resource $EnvironmentUrl 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Error "az account get-access-token failed:`n$tokenJson"
        exit 1
    }
    $token = ($tokenJson | ConvertFrom-Json).accessToken
    if (-not $token) {
        Write-Error 'Failed to extract access token from az output.'
        exit 1
    }
    Write-Host '  OK - token acquired' -ForegroundColor Green
} catch {
    Write-Error "Authentication failed: $($_.Exception.Message)"
    exit 1
}

$headers = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

# ---------------------------------------------------------------------------
# Step 2: Retrieve current option set values
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 2: Read current options ===' -ForegroundColor Cyan

$retrieveUri = "$ApiBase/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet"

try {
    $attribute = Invoke-RestMethod -Uri $retrieveUri -Headers $headers -Method Get
} catch {
    Write-Error "Failed to retrieve attribute metadata for $EntityLogicalName.$AttributeLogicalName : $($_.Exception.Message)"
    exit 1
}

if (-not $attribute.OptionSet) {
    Write-Error "Attribute $EntityLogicalName.$AttributeLogicalName has no OptionSet (is it really a Picklist?)."
    exit 1
}

$currentOptions = $attribute.OptionSet.Options
Write-Host "  Found $($currentOptions.Count) existing option(s):"
foreach ($opt in $currentOptions) {
    $label = $null
    if ($opt.Label -and $opt.Label.UserLocalizedLabel) {
        $label = $opt.Label.UserLocalizedLabel.Label
    } elseif ($opt.Label -and $opt.Label.LocalizedLabels -and $opt.Label.LocalizedLabels.Count -gt 0) {
        $label = $opt.Label.LocalizedLabels[0].Label
    }
    Write-Host ("    {0,-12} -> {1}" -f $opt.Value, ($label ?? '(no label)'))
}

# ---------------------------------------------------------------------------
# Step 3: Idempotency check
# ---------------------------------------------------------------------------
$existing = $currentOptions | Where-Object { $_.Value -eq $OptionValue }
if ($existing) {
    $existingLabel = $null
    if ($existing.Label -and $existing.Label.UserLocalizedLabel) {
        $existingLabel = $existing.Label.UserLocalizedLabel.Label
    } elseif ($existing.Label -and $existing.Label.LocalizedLabels -and $existing.Label.LocalizedLabels.Count -gt 0) {
        $existingLabel = $existing.Label.LocalizedLabels[0].Label
    }
    Write-Host ''
    Write-Host "  IDEMPOTENT: option value $OptionValue already exists with label '$existingLabel'." -ForegroundColor Green
    Write-Host '  No change required. Exiting.' -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# Step 4: InsertOptionValue
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 3: Insert new option value ===' -ForegroundColor Cyan
Write-Host "  Will add: $OptionValue -> '$OptionLabel' (language $LanguageCode)"

if ($DryRun) {
    Write-Host '  [DRY RUN] InsertOptionValue NOT called.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '=== DRY RUN COMPLETE ===' -ForegroundColor Yellow
    exit 0
}

$insertUri = "$ApiBase/InsertOptionValue"
$body = @{
    EntityLogicalName    = $EntityLogicalName
    AttributeLogicalName = $AttributeLogicalName
    Value                = $OptionValue
    Label                = @{
        LocalizedLabels = @(
            @{
                Label        = $OptionLabel
                LanguageCode = $LanguageCode
            }
        )
    }
}
$jsonBody = $body | ConvertTo-Json -Depth 10 -Compress

try {
    $response = Invoke-RestMethod -Uri $insertUri -Headers $headers -Method Post -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
    Write-Host '  OK - InsertOptionValue succeeded.' -ForegroundColor Green
    if ($response.NewOptionValue) {
        Write-Host "  Returned NewOptionValue = $($response.NewOptionValue)"
    }
} catch {
    $errMsg = $_.ErrorDetails.Message
    if (-not $errMsg -and $_.Exception.Response) {
        try {
            $errMsg = $_.Exception.Response.Content.ReadAsStringAsync().Result
        } catch { $errMsg = $_.Exception.Message }
    }
    if (-not $errMsg) { $errMsg = $_.Exception.Message }
    Write-Error "InsertOptionValue failed: $errMsg"
    exit 1
}

# ---------------------------------------------------------------------------
# Step 5: Verify by re-reading the option set
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 4: Verify ===' -ForegroundColor Cyan

try {
    $verify = Invoke-RestMethod -Uri $retrieveUri -Headers $headers -Method Get
    $verifyOptions = $verify.OptionSet.Options
    Write-Host "  Re-read $($verifyOptions.Count) option(s):"
    foreach ($opt in $verifyOptions) {
        $label = $null
        if ($opt.Label -and $opt.Label.UserLocalizedLabel) {
            $label = $opt.Label.UserLocalizedLabel.Label
        } elseif ($opt.Label -and $opt.Label.LocalizedLabels -and $opt.Label.LocalizedLabels.Count -gt 0) {
            $label = $opt.Label.LocalizedLabels[0].Label
        }
        $marker = if ($opt.Value -eq $OptionValue) { '  <-- NEW' } else { '' }
        Write-Host ("    {0,-12} -> {1}{2}" -f $opt.Value, ($label ?? '(no label)'), $marker)
    }
    $confirmed = $verifyOptions | Where-Object { $_.Value -eq $OptionValue }
    if (-not $confirmed) {
        Write-Error "Verification failed: option $OptionValue not present after insert."
        exit 1
    }
} catch {
    Write-Warning "Verification step failed (option may still have been added): $($_.Exception.Message)"
}

Write-Host ''
Write-Host '=== DONE ===' -ForegroundColor Green
Write-Host "Option value $OptionValue ('$OptionLabel') added to $EntityLogicalName.$AttributeLogicalName."
Write-Host ''
Write-Host 'NEXT STEP: redeploy the 118R playbook:' -ForegroundColor Cyan
Write-Host '  .\scripts\Deploy-Playbook.ps1 -DefinitionFile infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json -Force'
Write-Host ''
