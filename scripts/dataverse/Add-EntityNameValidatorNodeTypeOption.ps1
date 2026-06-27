<#
.SYNOPSIS
    Adds the `EntityNameValidator` (100000005) option to the
    `sprk_playbooknode.sprk_nodetype` choice column in Dataverse.

.DESCRIPTION
    spaarke-daily-update-service-r4 — Task 004 verification gap hotfix.

    R4 PR 1 introduced a new NodeType `EntityNameValidator` (FR-3, AC-3c) that
    pairs with server-side `ActionType.EntityNameValidator = 141` and the
    `EntityNameValidatorNodeExecutor`. Task 004 added the type to:
      - src/client/code-pages/PlaybookBuilder/src/types/playbook.ts (enum + maps)
      - src/client/code-pages/PlaybookBuilder/src/components/nodes/BaseNode.tsx (palette)
      - src/client/code-pages/PlaybookBuilder/src/components/properties/EntityNameValidatorForm.tsx
    But it did NOT:
      - Add the entry to the BuilderLayout palette array (closed in the same hotfix commit)
      - Add the matching option to the `sprk_nodetype` Dataverse choice column

    Without this option value, the MDA "Node Properties" form's "Node Type"
    dropdown does not show "EntityNameValidator" — UAT testers cannot
    distinguish the Tool from a generic Workflow.

    Value 100000005 was chosen as the next sequential value after the
    DeliverComposite (100000004) option added by chat-routing-redesign-r1.
    The matching code-page mapping in `types/playbook.ts` is updated alongside.

    Why this script exists: the MCP `update_table` tool can only ADD COLUMNS
    (not insert option-set values into an existing column). This script wraps
    the Dataverse Web API `InsertOptionValue` action so the schema delta is
    reproducible and idempotent. Same pattern as
    `Add-NodeTypeChoiceOption.ps1` (the DeliverComposite predecessor).

    Idempotency: the script first retrieves the current option list via
    `RetrieveAttributeRequest`. If `100000005` is already present, it logs
    "already exists" and exits 0 without making any state change. Re-runs
    are safe.

.PARAMETER Environment
    Target Dataverse environment label. Default: dev.

.PARAMETER DataverseUrl
    Base Dataverse URL (e.g. `https://spaarkedev1.crm.dynamics.com`).
    Defaults to the `DATAVERSE_URL` env var.

.PARAMETER DryRun
    Preview the operation without making the InsertOptionValue call.

.EXAMPLE
    # Apply to spaarkedev1
    .\Add-EntityNameValidatorNodeTypeOption.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

.NOTES
    Prerequisites:
      - PowerShell 7+
      - Azure CLI installed and authenticated (`az login`)
      - Access to the target Dataverse environment as System Customizer / Administrator
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',

    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$EntityLogicalName    = 'sprk_playbooknode'
$AttributeLogicalName = 'sprk_nodetype'
$OptionValue          = 100000005
$OptionLabel          = 'EntityNameValidator'
$LanguageCode         = 1033

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

Write-Host ''
Write-Host '=== Add NodeType Choice Option (FR-3 / R4 task 004 hotfix) ===' -ForegroundColor Cyan
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
Write-Host 'NEXT STEPS:' -ForegroundColor Cyan
Write-Host "  1. Publish customizations in the target environment (auto with InsertOptionValue, but verify in MDA)"
Write-Host '  2. Update src/client/code-pages/PlaybookBuilder/src/types/playbook.ts NodeTypeToDataverse map to point entityNameValidator -> EntityNameValidator (100000005)'
Write-Host '  3. Rebuild + redeploy sprk_playbookbuilder Code Page'
Write-Host ''
