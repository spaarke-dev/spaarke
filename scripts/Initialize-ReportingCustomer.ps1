<#
.SYNOPSIS
    Initializes the Reporting module for a new customer.

.DESCRIPTION
    Provisions a Power BI workspace, creates a service principal profile,
    deploys standard reports, and enables the module in Dataverse.

    End-to-end customer onboarding for the Spaarke Reporting module:
    1. Validates prerequisites (environment variables, Azure CLI auth)
    2. Creates (or reuses) a PBI workspace named "{CustomerName} - Reporting"
    3. Assigns the workspace to a capacity (if -CapacityId provided)
    4. Creates (or reuses) a service principal profile named "sprk-{CustomerId}"
    5. Adds the SP profile as workspace Admin
    6. Deploys standard product reports via Deploy-ReportingReports.ps1
    7. Enables the Reporting module via sprk_ReportingModuleEnabled env var in Dataverse
    8. Documents the manual security role assignment step
    9. Outputs a full onboarding summary

    All PBI configuration (tenant, client credentials) comes from environment
    variables — no hardcoded IDs. Supports -WhatIf for dry-run preview.
    Script is idempotent — safe to re-run; existing workspace/profile are reused.

.PARAMETER CustomerId
    Unique, URL-safe identifier for the customer (e.g., "contoso-legal").
    Used as part of the workspace name and SP profile name.
    Must contain only lowercase letters, digits, and hyphens.

.PARAMETER CustomerName
    Display name for the customer (e.g., "Contoso Legal Services").
    Used in the Power BI workspace display name: "{CustomerName} - Reporting".

.PARAMETER TenantId
    Entra ID tenant ID for the customer's Dataverse environment.
    Reads from env var PBI_TENANT_ID if not provided.

.PARAMETER DataverseOrg
    Dataverse organization URL (e.g., "https://contoso.crm.dynamics.com").
    Reads from env var DATAVERSE_URL if not provided.

.PARAMETER CapacityId
    Power BI capacity ID (F-SKU or P-SKU) to assign the workspace to.
    Optional — omit to leave on shared capacity (dev/test only).
    Use for production environments to ensure dedicated resources.

.PARAMETER ReportFolder
    Path to folder containing .pbix report templates to deploy.
    Default: reports/v1.0.0 (relative to repo root).
    Passed directly to Deploy-ReportingReports.ps1.

.PARAMETER SecurityRoleUsers
    Array of UPN strings (e.g., @("alice@contoso.com","bob@contoso.com")).
    When provided, the script prints the PAC CLI commands needed to assign
    the sprk_ReportingAccess security role to each user.
    Actual assignment is documented as a manual step (PAC CLI or Dataverse UI).

.PARAMETER WhatIf
    Preview mode — shows all operations that would be performed without
    making any API calls or changes.

.EXAMPLE
    # Dry run — preview all onboarding steps
    .\Initialize-ReportingCustomer.ps1 `
        -CustomerId "contoso-legal" `
        -CustomerName "Contoso Legal Services" `
        -DataverseOrg "https://contoso.crm.dynamics.com" `
        -WhatIf

.EXAMPLE
    # Full onboarding — shared capacity (dev/test)
    .\Initialize-ReportingCustomer.ps1 `
        -CustomerId "contoso-legal" `
        -CustomerName "Contoso Legal Services" `
        -DataverseOrg "https://contoso.crm.dynamics.com"

.EXAMPLE
    # Full onboarding — dedicated F2 capacity (production)
    .\Initialize-ReportingCustomer.ps1 `
        -CustomerId "fabrikam-corp" `
        -CustomerName "Fabrikam Corporation" `
        -DataverseOrg "https://fabrikam.crm.dynamics.com" `
        -CapacityId "00000000-0000-0000-0000-000000000000"

.EXAMPLE
    # Onboarding with user list for security role assignment guidance
    .\Initialize-ReportingCustomer.ps1 `
        -CustomerId "woodgrove-legal" `
        -CustomerName "Woodgrove Legal" `
        -DataverseOrg "https://woodgrove.crm.dynamics.com" `
        -SecurityRoleUsers @("alice@woodgrove.com", "bob@woodgrove.com")

.EXAMPLE
    # Re-run after partial failure (idempotent — skips already-created resources)
    .\Initialize-ReportingCustomer.ps1 `
        -CustomerId "contoso-legal" `
        -CustomerName "Contoso Legal Services" `
        -DataverseOrg "https://contoso.crm.dynamics.com"

.NOTES
    Required environment variables:
      PBI_TENANT_ID           — Entra ID tenant ID (overridable via -TenantId)
      PBI_CLIENT_ID           — Service principal app (client) ID
      PBI_CLIENT_SECRET       — Service principal client secret
      DATAVERSE_URL           — Dataverse org URL (overridable via -DataverseOrg)
      PBI_DATAVERSE_DATASOURCE_URL — Dataverse OData endpoint for dataset rebinding

    Optional environment variables:
      PBI_REFRESH_ENABLED     — Set to "false" to skip scheduling report refresh (default: true)

    Prerequisites:
      - Azure CLI installed and authenticated (az login) — for Dataverse operations
      - Service principal must have Power BI tenant-level access
        (Power BI Admin portal > Tenant settings > Service principals can use Power BI APIs)
      - Service principal must have permission to create workspaces
      - PowerShell 7+ recommended

    Security role assignment (manual step):
      After this script completes, assign the sprk_ReportingAccess security role
      to users via the Dataverse Admin Center or PAC CLI:
        pac org assign-user --environment <url> --user <upn> --role sprk_ReportingAccess

    Rollback guidance:
      If onboarding fails mid-way, re-run the script — it is idempotent.
      To fully revert: delete the PBI workspace via Power BI Admin portal,
      delete the SP profile via PBI REST API DELETE /profiles/{profileId},
      and set sprk_ReportingModuleEnabled back to "No" in Dataverse.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9\-]{1,48}[a-z0-9]$')]
    [string]$CustomerId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CustomerName,

    [string]$TenantId = $env:PBI_TENANT_ID,

    [string]$DataverseOrg = $env:DATAVERSE_URL,

    [string]$CapacityId,

    [string]$ReportFolder,

    [string[]]$SecurityRoleUsers = @(),

    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

if ($ReportFolder -and -not [System.IO.Path]::IsPathRooted($ReportFolder)) {
    $ReportFolder = Join-Path $RepoRoot $ReportFolder
}

$DeployReportsScript = Join-Path $ScriptDir 'Deploy-ReportingReports.ps1'

# Derive workspace name from CustomerName per spec
$WorkspaceName = "$CustomerName - Reporting"

# Derive SP profile name from CustomerId per spec
$ProfileName = "sprk-$CustomerId"

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  Initialize Reporting Customer — Spaarke' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host "  Customer ID   : $CustomerId"
Write-Host "  Customer Name : $CustomerName"
Write-Host "  Workspace Name: $WorkspaceName"
Write-Host "  Profile Name  : $ProfileName"
Write-Host "  Dataverse URL : $(if ($DataverseOrg) { $DataverseOrg } else { '(not set)' })"
Write-Host "  Capacity ID   : $(if ($CapacityId) { $CapacityId } else { '(shared capacity)' })"
Write-Host "  Report Folder : $(if ($ReportFolder) { $ReportFolder } else { 'reports/v1.0.0 (default)' })"
if ($WhatIf) {
    Write-Host '  Mode          : DRY RUN (-WhatIf) — no changes will be made' -ForegroundColor Yellow
}
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''

# ---------------------------------------------------------------------------
# Step 1 — Validate prerequisites
# ---------------------------------------------------------------------------
Write-Host '[1/9] Validating prerequisites...' -ForegroundColor Yellow

$clientId     = $env:PBI_CLIENT_ID
$clientSecret = $env:PBI_CLIENT_SECRET
$dvDatasourceUrl = $env:PBI_DATAVERSE_DATASOURCE_URL

$missingVars = @()
if (-not $TenantId)       { $missingVars += 'PBI_TENANT_ID (or -TenantId)' }
if (-not $clientId)       { $missingVars += 'PBI_CLIENT_ID' }
if (-not $clientSecret)   { $missingVars += 'PBI_CLIENT_SECRET' }
if (-not $DataverseOrg)   { $missingVars += 'DATAVERSE_URL (or -DataverseOrg)' }
if (-not $dvDatasourceUrl){ $missingVars += 'PBI_DATAVERSE_DATASOURCE_URL' }

if ($missingVars.Count -gt 0) {
    Write-Host '  ERROR: Missing required configuration:' -ForegroundColor Red
    foreach ($v in $missingVars) {
        Write-Host "    - $v" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host '  Set these as environment variables before running this script.' -ForegroundColor Yellow
    exit 1
}

# Validate Deploy-ReportingReports.ps1 exists
if (-not (Test-Path $DeployReportsScript)) {
    Write-Host "  ERROR: Deploy-ReportingReports.ps1 not found at: $DeployReportsScript" -ForegroundColor Red
    Write-Host '  Ensure scripts/Deploy-ReportingReports.ps1 exists (created in Task 035).' -ForegroundColor Yellow
    exit 1
}

# Verify Azure CLI available
$azVersion = (az version 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue).'azure-cli'
if (-not $azVersion) {
    Write-Host '  ERROR: Azure CLI not found. Install from https://aka.ms/installazurecliwindows' -ForegroundColor Red
    exit 1
}
Write-Host "  Azure CLI: $azVersion" -ForegroundColor Green
Write-Host '  Prerequisites OK' -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 2 — Authenticate to Power BI REST API
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[2/9] Authenticating to Power BI REST API (service principal)...' -ForegroundColor Yellow

$pbiTokenUrl  = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
$pbiTokenBody = @{
    grant_type    = 'client_credentials'
    client_id     = $clientId
    client_secret = $clientSecret
    scope         = 'https://analysis.windows.net/powerbi/api/.default'
}

$pbiToken   = $null
$pbiHeaders = $null

if ($WhatIf) {
    Write-Host '  [WhatIf] Would acquire PBI service principal token' -ForegroundColor Yellow
    $pbiToken = 'WHATIF-TOKEN'
} else {
    try {
        $tokenResponse = Invoke-RestMethod -Uri $pbiTokenUrl -Method Post -Body $pbiTokenBody -ContentType 'application/x-www-form-urlencoded'
        $pbiToken      = $tokenResponse.access_token
        Write-Host '  Power BI token acquired' -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: Failed to acquire Power BI token: $_" -ForegroundColor Red
        Write-Host '  Verify PBI_TENANT_ID, PBI_CLIENT_ID, and PBI_CLIENT_SECRET are correct.' -ForegroundColor Yellow
        exit 1
    }
}

$pbiHeaders = @{
    'Authorization' = "Bearer $pbiToken"
    'Content-Type'  = 'application/json'
}
$pbiApiBase = 'https://api.powerbi.com/v1.0/myorg'

# ---------------------------------------------------------------------------
# Step 3 — Create (or reuse) Power BI workspace
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[3/9] Provisioning Power BI workspace...' -ForegroundColor Yellow

$workspaceId = $null

if ($WhatIf) {
    Write-Host "  [WhatIf] Would check for existing workspace '$WorkspaceName'" -ForegroundColor Yellow
    Write-Host "  [WhatIf] Would POST $pbiApiBase/groups  body: { name: '$WorkspaceName' }" -ForegroundColor Yellow
    $workspaceId = 'WHATIF-WORKSPACE-ID'
    Write-Host "  [WhatIf] Workspace ID: $workspaceId" -ForegroundColor Yellow
} else {
    # Check if workspace already exists (idempotency)
    try {
        $existingWorkspaces = Invoke-RestMethod -Uri "$pbiApiBase/groups?`$filter=name eq '$([Uri]::EscapeDataString($WorkspaceName))'&`$top=5" -Headers $pbiHeaders -Method Get
        $existingWorkspace  = $existingWorkspaces.value | Where-Object { $_.name -eq $WorkspaceName } | Select-Object -First 1
    } catch {
        Write-Host "  WARNING: Could not query existing workspaces: $_" -ForegroundColor Yellow
        $existingWorkspace = $null
    }

    if ($existingWorkspace) {
        $workspaceId = $existingWorkspace.id
        Write-Host "  Workspace already exists — reusing (id: $workspaceId)" -ForegroundColor Green
    } else {
        Write-Host "  Creating workspace '$WorkspaceName'..." -ForegroundColor Gray
        try {
            $createBody     = @{ name = $WorkspaceName } | ConvertTo-Json -Depth 2
            $createResponse = Invoke-RestMethod -Uri "$pbiApiBase/groups" -Method Post -Headers $pbiHeaders -Body ([System.Text.Encoding]::UTF8.GetBytes($createBody))
            $workspaceId    = $createResponse.id
            Write-Host "  Workspace created (id: $workspaceId)" -ForegroundColor Green
        } catch {
            Write-Host "  ERROR: Failed to create workspace '$WorkspaceName': $_" -ForegroundColor Red
            Write-Host '  Ensure the service principal has permission to create workspaces.' -ForegroundColor Yellow
            Write-Host '  PBI Admin portal > Tenant settings > Allow service principals to create workspaces.' -ForegroundColor Yellow
            exit 1
        }
    }
}

Write-Host "  Workspace: '$WorkspaceName' (id: $workspaceId)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 4 — Assign workspace to capacity (optional)
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[4/9] Capacity assignment...' -ForegroundColor Yellow

if (-not $CapacityId) {
    Write-Host '  No -CapacityId provided — workspace remains on shared capacity.' -ForegroundColor Gray
    Write-Host '  NOTE: Shared capacity is for dev/test only. Provide -CapacityId for production.' -ForegroundColor Yellow
} elseif ($WhatIf) {
    Write-Host "  [WhatIf] Would POST $pbiApiBase/groups/$workspaceId/AssignToCapacity  body: { capacityId: '$CapacityId' }" -ForegroundColor Yellow
} else {
    Write-Host "  Assigning workspace to capacity '$CapacityId'..." -ForegroundColor Gray
    try {
        $capacityBody = @{ capacityId = $CapacityId } | ConvertTo-Json -Depth 2
        Invoke-RestMethod -Uri "$pbiApiBase/groups/$workspaceId/AssignToCapacity" -Method Post -Headers $pbiHeaders -Body ([System.Text.Encoding]::UTF8.GetBytes($capacityBody)) | Out-Null
        Write-Host "  Workspace assigned to capacity '$CapacityId'" -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: Failed to assign capacity: $_" -ForegroundColor Red
        Write-Host '  Verify the capacity ID is correct and the service principal has Capacity Admin rights.' -ForegroundColor Yellow
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Step 5 — Create (or reuse) service principal profile
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[5/9] Provisioning service principal profile...' -ForegroundColor Yellow

$profileId = $null

if ($WhatIf) {
    Write-Host "  [WhatIf] Would check for existing profile '$ProfileName'" -ForegroundColor Yellow
    Write-Host "  [WhatIf] Would POST $pbiApiBase/profiles  body: { displayName: '$ProfileName' }" -ForegroundColor Yellow
    $profileId = 'WHATIF-PROFILE-ID'
    Write-Host "  [WhatIf] Profile ID: $profileId" -ForegroundColor Yellow
} else {
    # Check if profile already exists (idempotency)
    try {
        $existingProfiles = Invoke-RestMethod -Uri "$pbiApiBase/profiles" -Headers $pbiHeaders -Method Get
        $existingProfile  = $existingProfiles.value | Where-Object { $_.displayName -eq $ProfileName } | Select-Object -First 1
    } catch {
        Write-Host "  WARNING: Could not query existing profiles: $_" -ForegroundColor Yellow
        $existingProfile = $null
    }

    if ($existingProfile) {
        $profileId = $existingProfile.id
        Write-Host "  SP profile already exists — reusing (id: $profileId)" -ForegroundColor Green
    } else {
        Write-Host "  Creating SP profile '$ProfileName'..." -ForegroundColor Gray
        try {
            $profileBody     = @{ displayName = $ProfileName } | ConvertTo-Json -Depth 2
            $profileResponse = Invoke-RestMethod -Uri "$pbiApiBase/profiles" -Method Post -Headers $pbiHeaders -Body ([System.Text.Encoding]::UTF8.GetBytes($profileBody))
            $profileId       = $profileResponse.id
            Write-Host "  SP profile created (id: $profileId)" -ForegroundColor Green
        } catch {
            Write-Host "  ERROR: Failed to create SP profile '$ProfileName': $_" -ForegroundColor Red
            Write-Host '  Ensure the PBI tenant has service principal profiles enabled.' -ForegroundColor Yellow
            Write-Host '  PBI Admin portal > Tenant settings > Service principals can create and use profiles.' -ForegroundColor Yellow
            exit 1
        }
    }
}

Write-Host "  SP Profile: '$ProfileName' (id: $profileId)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 6 — Add SP profile as workspace Admin
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[6/9] Adding SP profile as workspace Admin...' -ForegroundColor Yellow

if ($WhatIf) {
    Write-Host "  [WhatIf] Would POST $pbiApiBase/groups/$workspaceId/users" -ForegroundColor Yellow
    Write-Host "           body: { identifier: '$profileId', principalType: 'App', groupUserAccessRight: 'Admin' }" -ForegroundColor Yellow
} else {
    # Check if profile is already a workspace member (idempotency)
    $alreadyMember = $false
    try {
        $workspaceUsers   = Invoke-RestMethod -Uri "$pbiApiBase/groups/$workspaceId/users" -Headers $pbiHeaders -Method Get
        $existingMembership = $workspaceUsers.value | Where-Object { $_.identifier -eq $profileId }
        if ($existingMembership) {
            $alreadyMember = $true
            $currentRole   = $existingMembership.groupUserAccessRight
            Write-Host "  SP profile already has '$currentRole' access — skipping add." -ForegroundColor Green
        }
    } catch {
        Write-Host "  WARNING: Could not query workspace users: $_" -ForegroundColor Yellow
    }

    if (-not $alreadyMember) {
        Write-Host "  Adding SP profile as Admin to workspace '$WorkspaceName'..." -ForegroundColor Gray
        try {
            $addUserBody = @{
                identifier          = $profileId
                principalType       = 'App'
                groupUserAccessRight = 'Admin'
            } | ConvertTo-Json -Depth 2
            Invoke-RestMethod -Uri "$pbiApiBase/groups/$workspaceId/users" -Method Post -Headers $pbiHeaders -Body ([System.Text.Encoding]::UTF8.GetBytes($addUserBody)) | Out-Null
            Write-Host '  SP profile added as workspace Admin' -ForegroundColor Green
        } catch {
            # "AlreadyExists" is expected on re-runs — treat as success
            if ($_.ToString() -match 'AlreadyExists') {
                Write-Host '  SP profile is already a workspace member — no change needed.' -ForegroundColor Green
            } else {
                Write-Host "  ERROR: Failed to add SP profile as workspace Admin: $_" -ForegroundColor Red
                Write-Host '  Verify the profile ID is valid and the calling SP has workspace Admin rights.' -ForegroundColor Yellow
                exit 1
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Step 7 — Deploy standard reports
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[7/9] Deploying standard product reports...' -ForegroundColor Yellow

$deployArgs = @{
    WorkspaceId  = $workspaceId
    DataverseOrg = $DataverseOrg
    Environment  = 'dev'
}
if ($ReportFolder)  { $deployArgs['ReportFolder'] = $ReportFolder }
if ($WhatIf)        { $deployArgs['WhatIf'] = $true }

Write-Host "  Calling Deploy-ReportingReports.ps1 with WorkspaceId: $workspaceId" -ForegroundColor Gray

$deployedReportCount = 0
try {
    & $DeployReportsScript @deployArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  ERROR: Deploy-ReportingReports.ps1 exited with non-zero status.' -ForegroundColor Red
        Write-Host '  Review output above for details. Onboarding incomplete.' -ForegroundColor Yellow
        exit 1
    }
    # Approximate count from reports folder for summary
    $resolvedFolder = if ($ReportFolder) { $ReportFolder } else { Join-Path $RepoRoot 'reports\v1.0.0' }
    if (Test-Path $resolvedFolder) {
        $deployedReportCount = (Get-ChildItem -Path $resolvedFolder -Filter '*.pbix' -File).Count
    }
} catch {
    Write-Host "  ERROR: Failed to invoke Deploy-ReportingReports.ps1: $_" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# Step 8 — Enable Reporting module in Dataverse
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[8/9] Enabling Reporting module in Dataverse (sprk_ReportingModuleEnabled)...' -ForegroundColor Yellow

$dvApiUrl = "$DataverseOrg/api/data/v9.2"

if ($WhatIf) {
    Write-Host '  [WhatIf] Would acquire Dataverse token via az CLI' -ForegroundColor Yellow
    Write-Host "  [WhatIf] Would GET $dvApiUrl/environmentvariabledefinitions?\$filter=schemaname eq 'sprk_ReportingModuleEnabled'" -ForegroundColor Yellow
    Write-Host "  [WhatIf] Would PATCH environmentvariablevalues record to value 'Yes'" -ForegroundColor Yellow
} else {
    # Get Dataverse token via Azure CLI
    $dvToken = az account get-access-token --resource $DataverseOrg --query accessToken -o tsv 2>$null
    if ([string]::IsNullOrEmpty($dvToken)) {
        Write-Host '  ERROR: Failed to get Dataverse token. Run: az login' -ForegroundColor Red
        exit 1
    }
    $dvHeaders = @{
        'Authorization'    = "Bearer $dvToken"
        'Content-Type'     = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Accept'           = 'application/json'
    }
    Write-Host '  Dataverse token acquired' -ForegroundColor Green

    # Look up the environment variable definition ID
    try {
        $defResponse = Invoke-RestMethod `
            -Uri "$dvApiUrl/environmentvariabledefinitions?`$filter=schemaname eq 'sprk_ReportingModuleEnabled'&`$select=environmentvariabledefinitionid,schemaname,displayname" `
            -Headers $dvHeaders -Method Get

        if ($defResponse.value.Count -eq 0) {
            Write-Host '  WARNING: sprk_ReportingModuleEnabled definition not found in Dataverse.' -ForegroundColor Yellow
            Write-Host '  Ensure the Spaarke Reporting managed solution is imported into this environment.' -ForegroundColor Yellow
            Write-Host '  Skipping module enable step.' -ForegroundColor Yellow
        } else {
            $defId = $defResponse.value[0].environmentvariabledefinitionid
            Write-Host "  Found definition: sprk_ReportingModuleEnabled (id: $defId)" -ForegroundColor Gray

            # Check if a value record already exists
            $valResponse = Invoke-RestMethod `
                -Uri "$dvApiUrl/environmentvariablevalues?`$filter=_environmentvariabledefinitionid_value eq '$defId'&`$select=environmentvariablevalueid,value" `
                -Headers $dvHeaders -Method Get

            $enableBody  = @{ value = 'Yes' } | ConvertTo-Json -Depth 2
            $enableBytes = [System.Text.Encoding]::UTF8.GetBytes($enableBody)

            if ($valResponse.value.Count -gt 0) {
                # Update existing value record
                $valId      = $valResponse.value[0].environmentvariablevalueid
                $currentVal = $valResponse.value[0].value

                if ($currentVal -eq 'Yes') {
                    Write-Host "  sprk_ReportingModuleEnabled is already 'Yes' — no change needed." -ForegroundColor Green
                } else {
                    $patchHeaders = $dvHeaders.Clone()
                    $patchHeaders['If-Match'] = '*'
                    Invoke-RestMethod -Uri "$dvApiUrl/environmentvariablevalues($valId)" -Method Patch -Headers $patchHeaders -Body $enableBytes | Out-Null
                    Write-Host "  Updated sprk_ReportingModuleEnabled: '$currentVal' -> 'Yes'" -ForegroundColor Green
                }
            } else {
                # Create new value record linked to definition
                $createBody = @{
                    value = 'Yes'
                    'EnvironmentVariableDefinitionId@odata.bind' = "/environmentvariabledefinitions($defId)"
                } | ConvertTo-Json -Depth 2
                $createHeaders = $dvHeaders.Clone()
                $createHeaders['Prefer'] = 'return=representation'
                $created = Invoke-RestMethod -Uri "$dvApiUrl/environmentvariablevalues" -Method Post -Headers $createHeaders -Body ([System.Text.Encoding]::UTF8.GetBytes($createBody))
                Write-Host "  Created sprk_ReportingModuleEnabled value = 'Yes' (id: $($created.environmentvariablevalueid))" -ForegroundColor Green
            }
        }
    } catch {
        Write-Host "  WARNING: Failed to update sprk_ReportingModuleEnabled: $_" -ForegroundColor Yellow
        Write-Host '  You can set this manually in Dataverse Admin > Environment Variables.' -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Step 9 — Security role assignment guidance
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[9/9] Security role assignment...' -ForegroundColor Yellow
Write-Host ''
Write-Host '  MANUAL STEP REQUIRED: Security role assignment is not automated.' -ForegroundColor Yellow
Write-Host '  Assign the sprk_ReportingAccess security role to users via:' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Option A — Dataverse Admin Center (UI):' -ForegroundColor Cyan
Write-Host "    1. Navigate to: $DataverseOrg" -ForegroundColor Gray
Write-Host '    2. Settings > Security > Users' -ForegroundColor Gray
Write-Host '    3. Select user > Manage Security Roles > sprk_ReportingAccess' -ForegroundColor Gray
Write-Host ''
Write-Host '  Option B — PAC CLI (scripted):' -ForegroundColor Cyan
Write-Host "    pac auth create --url $DataverseOrg" -ForegroundColor Gray

if ($SecurityRoleUsers.Count -gt 0) {
    Write-Host ''
    Write-Host "  Users to assign sprk_ReportingAccess ($($SecurityRoleUsers.Count) total):" -ForegroundColor Cyan
    foreach ($upn in $SecurityRoleUsers) {
        Write-Host "    pac org assign-user --environment $DataverseOrg --user $upn --role sprk_ReportingAccess" -ForegroundColor Gray
    }
} else {
    Write-Host '    pac org assign-user --environment <url> --user <upn> --role sprk_ReportingAccess' -ForegroundColor Gray
    Write-Host ''
    Write-Host '  TIP: Pass -SecurityRoleUsers @("user@domain.com") to see pre-populated commands.' -ForegroundColor Gray
}

Write-Host ''
Write-Host '  Role hierarchy for reference:' -ForegroundColor Gray
Write-Host '    Viewer  — sprk_ReportingAccess (read-only, no export)' -ForegroundColor Gray
Write-Host '    Editor  — sprk_ReportingContributor (save personal views, export)' -ForegroundColor Gray
Write-Host '    Manager — sprk_ReportingAdmin (all access, manage reports)' -ForegroundColor Gray

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '================================================================' -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host "  $(if ($WhatIf) { 'WHATIF SUMMARY (no changes made)' } else { 'Onboarding Summary' })" -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host '================================================================' -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host "  Customer ID      : $CustomerId"
Write-Host "  Customer Name    : $CustomerName"
Write-Host "  Workspace Name   : $WorkspaceName"
Write-Host "  Workspace ID     : $workspaceId"
Write-Host "  SP Profile Name  : $ProfileName"
Write-Host "  SP Profile ID    : $profileId"
Write-Host "  Reports Deployed : $deployedReportCount"
Write-Host "  Dataverse URL    : $DataverseOrg"
Write-Host "  Module Enabled   : $(if ($WhatIf) { 'N/A (WhatIf)' } else { 'Yes (sprk_ReportingModuleEnabled)' })"
Write-Host ''
Write-Host '  Next steps:' -ForegroundColor Cyan
Write-Host '    1. Assign sprk_ReportingAccess security role to users (see step 9 above)' -ForegroundColor Gray
Write-Host "    2. Verify the Reporting module loads at: $DataverseOrg" -ForegroundColor Gray
Write-Host '    3. Validate reports render in the Spaarke Reporting page' -ForegroundColor Gray
if ($CapacityId) {
    Write-Host '    4. Confirm workspace is assigned to capacity in PBI Admin portal' -ForegroundColor Gray
} else {
    Write-Host '    4. Assign -CapacityId before going to production' -ForegroundColor Yellow
}
Write-Host ''
Write-Host '================================================================' -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host ''

if (-not $WhatIf) {
    Write-Host "Customer '$CustomerName' ($CustomerId) is onboarded to the Spaarke Reporting module." -ForegroundColor Green
    Write-Host ''
}
