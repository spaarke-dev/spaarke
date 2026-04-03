<#
.SYNOPSIS
    One-time Entra ID infrastructure setup for the Spaarke Self-Service Registration system.

.DESCRIPTION
    Configures Entra ID infrastructure required by the demo user provisioning pipeline:
      1. Creates "Spaarke Demo Users" security group (if not exists)
      2. Creates/modifies a Conditional Access policy to exclude the demo group from MFA
      3. Adds Graph API application permissions to the BFF API app registration:
         - User.ReadWrite.All
         - GroupMember.ReadWrite.All
         - Directory.ReadWrite.All
      4. Grants admin consent for the added permissions

    All operations are idempotent — safe to re-run. Existing resources are detected and
    skipped with informational messages.

    Prerequisites:
      - Microsoft.Graph PowerShell SDK installed (Install-Module Microsoft.Graph -Scope CurrentUser)
      - Entra ID Global Administrator or Privileged Role Administrator
      - Tenant: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER TenantId
    Entra ID tenant ID. Default: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER BffApiAppId
    Application (client) ID of the BFF API app registration.
    Default: 1e40baad-e065-4aea-a8d4-4b7ab273458c

.PARAMETER GroupDisplayName
    Display name for the demo users security group.
    Default: Spaarke Demo Users

.PARAMETER ConditionalAccessPolicyName
    Display name for the Conditional Access policy.
    Default: Exclude Spaarke Demo Users from MFA

.PARAMETER DryRun
    If specified, shows what would be created without making changes.

.EXAMPLE
    # Preview what will be created
    .\Setup-EntraInfrastructure.ps1 -DryRun

.EXAMPLE
    # Run full setup
    .\Setup-EntraInfrastructure.ps1

.EXAMPLE
    # Run with custom group name
    .\Setup-EntraInfrastructure.ps1 -GroupDisplayName "My Demo Users"

.NOTES
    Project: spaarke-self-service-registration-app
    Task: 013 — Entra ID Setup Scripts
    Idempotent: Yes — all operations check existence before creating
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$BffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$GroupDisplayName = "Spaarke Demo Users",
    [string]$ConditionalAccessPolicyName = "Exclude Spaarke Demo Users from MFA",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Configuration — Microsoft Graph well-known IDs
# ─────────────────────────────────────────────────────────────────────────────

$GraphServicePrincipalId = "00000003-0000-0000-c000-000000000000"

# Application permission IDs (from Microsoft Graph permission reference)
$PermissionsToAdd = @(
    @{ Name = "User.ReadWrite.All";        Id = "741f803b-c850-494e-b5df-cde7c675a1ca"; Type = "Role" }
    @{ Name = "GroupMember.ReadWrite.All";  Id = "dbaae8cf-10b5-4b86-a4a1-f871c94c6571"; Type = "Role" }
    @{ Name = "Directory.ReadWrite.All";    Id = "19dbc75e-c2e2-444c-a770-ec69d8559fc7"; Type = "Role" }
)

# ─────────────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([int]$Number, [string]$Description)
    Write-Host "  [$Number] $Description" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  [--] $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [!!] $Message" -ForegroundColor DarkYellow
}

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight Checks
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "ENTRA ID INFRASTRUCTURE SETUP — SELF-SERVICE REGISTRATION"

if ($DryRun) {
    Write-Host "  *** DRY RUN MODE — No changes will be made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Step 0 "Pre-flight checks"

# Verify Microsoft.Graph module is available
$graphModule = Get-Module -ListAvailable -Name Microsoft.Graph.Authentication
if (-not $graphModule) {
    Write-Host "  [ERROR] Microsoft.Graph PowerShell SDK not installed." -ForegroundColor Red
    Write-Host "  Install with: Install-Module Microsoft.Graph -Scope CurrentUser" -ForegroundColor Red
    exit 1
}
Write-Success "Microsoft.Graph PowerShell SDK found (v$($graphModule.Version))"

# Connect to Microsoft Graph with required scopes
$requiredScopes = @(
    "Group.ReadWrite.All",
    "Application.ReadWrite.All",
    "Policy.ReadWrite.ConditionalAccess",
    "AppRoleAssignment.ReadWrite.All"
)

Write-Info "Connecting to Microsoft Graph..."
if (-not $DryRun) {
    try {
        Connect-MgGraph -TenantId $TenantId -Scopes $requiredScopes -ErrorAction Stop
    }
    catch {
        Write-Host "  [ERROR] Failed to connect to Microsoft Graph: $_" -ForegroundColor Red
        Write-Host "  Ensure you have Global Administrator or Privileged Role Administrator role." -ForegroundColor Red
        exit 1
    }

    $context = Get-MgContext
    if (-not $context) {
        Write-Host "  [ERROR] Not connected to Microsoft Graph. Run Connect-MgGraph first." -ForegroundColor Red
        exit 1
    }
    Write-Success "Connected to Microsoft Graph as: $($context.Account)"
    Write-Success "Tenant: $($context.TenantId)"

    if ($context.TenantId -ne $TenantId) {
        Write-Warn "Connected tenant ($($context.TenantId)) differs from target ($TenantId)"
        Write-Host "  [ERROR] Tenant mismatch. Disconnect and reconnect to the correct tenant." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Info "DRY RUN: Would connect to Microsoft Graph with scopes: $($requiredScopes -join ', ')"
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 1: Create "Spaarke Demo Users" Security Group
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 1: Security Group — $GroupDisplayName"

$groupId = $null

if ($DryRun) {
    Write-Info "DRY RUN: Would create security group '$GroupDisplayName' with:"
    Write-Info "  - Type: Security (non-mail-enabled)"
    Write-Info "  - Description: Security group for demo user accounts. Members are excluded from MFA via Conditional Access."
    $groupId = "00000000-0000-0000-0000-000000000000"
} else {
    Write-Step 1 "Checking if security group already exists..."

    $existingGroup = Get-MgGroup -Filter "displayName eq '$GroupDisplayName'" -ErrorAction SilentlyContinue
    if ($existingGroup) {
        $groupId = $existingGroup.Id
        Write-Success "Security group already exists (Id: $groupId)"
        Write-Info "Skipping creation."
    } else {
        Write-Step 2 "Creating security group..."

        if ($PSCmdlet.ShouldProcess($GroupDisplayName, "Create security group")) {
            $newGroup = New-MgGroup -DisplayName $GroupDisplayName `
                -Description "Security group for demo user accounts. Members are excluded from MFA via Conditional Access." `
                -MailEnabled:$false `
                -MailNickname "SpaarkeDemoUsers" `
                -SecurityEnabled:$true `
                -ErrorAction Stop

            $groupId = $newGroup.Id
            Write-Success "Security group created (Id: $groupId)"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 2: Conditional Access Policy — Exclude Demo Group from MFA
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 2: Conditional Access Policy — MFA Exclusion"

if ($DryRun) {
    Write-Info "DRY RUN: Would create/update Conditional Access policy:"
    Write-Info "  - Name: $ConditionalAccessPolicyName"
    Write-Info "  - Target: All users"
    Write-Info "  - Exclude: Group '$GroupDisplayName'"
    Write-Info "  - Grant: Require MFA (excluded group bypasses)"
    Write-Info "  - State: enabledForReportingButNotEnforced (safe to review first)"
} else {
    Write-Step 1 "Checking for existing Conditional Access policy..."

    # Import the Identity.SignIns module for Conditional Access cmdlets
    Import-Module Microsoft.Graph.Identity.SignIns -ErrorAction SilentlyContinue

    $existingPolicy = Get-MgIdentityConditionalAccessPolicy -Filter "displayName eq '$ConditionalAccessPolicyName'" -ErrorAction SilentlyContinue

    if ($existingPolicy) {
        Write-Success "Conditional Access policy already exists (Id: $($existingPolicy.Id))"

        # Verify the exclusion group is present
        $excludedGroups = $existingPolicy.Conditions.Users.ExcludeGroups
        if ($excludedGroups -contains $groupId) {
            Write-Success "Demo group is already excluded from this policy"
            Write-Info "Skipping update."
        } else {
            Write-Step 2 "Adding demo group to policy exclusions..."

            if ($PSCmdlet.ShouldProcess($ConditionalAccessPolicyName, "Add group exclusion")) {
                $updatedExclusions = @($excludedGroups) + @($groupId)

                $params = @{
                    Conditions = @{
                        Users = @{
                            ExcludeGroups = $updatedExclusions
                        }
                    }
                }

                Update-MgIdentityConditionalAccessPolicy -ConditionalAccessPolicyId $existingPolicy.Id -BodyParameter $params -ErrorAction Stop
                Write-Success "Demo group added to policy exclusions"
            }
        }
    } else {
        Write-Step 2 "Creating Conditional Access policy..."

        if ($PSCmdlet.ShouldProcess($ConditionalAccessPolicyName, "Create Conditional Access policy")) {
            $policyParams = @{
                DisplayName = $ConditionalAccessPolicyName
                State       = "enabledForReportingButNotEnforced"
                Conditions  = @{
                    Applications = @{
                        IncludeApplications = @("All")
                    }
                    Users = @{
                        IncludeUsers  = @("All")
                        ExcludeGroups = @($groupId)
                    }
                }
                GrantControls = @{
                    Operator        = "OR"
                    BuiltInControls = @("mfa")
                }
            }

            $newPolicy = New-MgIdentityConditionalAccessPolicy -BodyParameter $policyParams -ErrorAction Stop
            Write-Success "Conditional Access policy created (Id: $($newPolicy.Id))"
            Write-Warn "Policy is in REPORT-ONLY mode. Review in Azure Portal before enabling."
            Write-Info "To enforce: Set State to 'enabled' in Azure Portal > Security > Conditional Access"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 3: Add Graph API Permissions to BFF API App Registration
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 3: Graph API Permissions — BFF API App Registration"

if ($DryRun) {
    Write-Info "DRY RUN: Would add application permissions to app $BffApiAppId:"
    foreach ($perm in $PermissionsToAdd) {
        Write-Info "  - $($perm.Name) ($($perm.Id)) [Application]"
    }
} else {
    Write-Step 1 "Looking up BFF API app registration..."

    $bffApp = Get-MgApplication -Filter "appId eq '$BffApiAppId'" -ErrorAction SilentlyContinue
    if (-not $bffApp) {
        Write-Host "  [ERROR] BFF API app registration not found: $BffApiAppId" -ForegroundColor Red
        Write-Host "  Ensure the app registration exists in tenant $TenantId" -ForegroundColor Red
        exit 1
    }
    Write-Success "Found app: $($bffApp.DisplayName) (ObjectId: $($bffApp.Id))"

    Write-Step 2 "Checking existing permissions..."

    # Get current required resource access
    $currentAccess = $bffApp.RequiredResourceAccess
    $graphAccess = $currentAccess | Where-Object { $_.ResourceAppId -eq $GraphServicePrincipalId }

    # Build the updated permission list (keep existing, add new)
    $existingPermissions = @()
    if ($graphAccess) {
        $existingPermissions = @($graphAccess.ResourceAccess)
    }

    $permissionsAdded = @()
    foreach ($perm in $PermissionsToAdd) {
        $alreadyExists = $existingPermissions | Where-Object { $_.Id -eq $perm.Id }
        if ($alreadyExists) {
            Write-Info "Permission already exists: $($perm.Name)"
        } else {
            $existingPermissions += @(
                @{
                    Id   = $perm.Id
                    Type = $perm.Type
                }
            )
            $permissionsAdded += $perm.Name
        }
    }

    if ($permissionsAdded.Count -eq 0) {
        Write-Success "All required permissions already present"
    } else {
        Write-Step 3 "Adding permissions: $($permissionsAdded -join ', ')"

        if ($PSCmdlet.ShouldProcess($bffApp.DisplayName, "Add Graph API permissions")) {
            # Rebuild the full required resource access array
            $updatedResourceAccess = @($currentAccess | Where-Object { $_.ResourceAppId -ne $GraphServicePrincipalId })
            $updatedResourceAccess += @(
                @{
                    ResourceAppId  = $GraphServicePrincipalId
                    ResourceAccess = $existingPermissions
                }
            )

            Update-MgApplication -ApplicationId $bffApp.Id -RequiredResourceAccess $updatedResourceAccess -ErrorAction Stop
            Write-Success "Permissions added: $($permissionsAdded -join ', ')"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 4: Grant Admin Consent
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 4: Grant Admin Consent"

if ($DryRun) {
    Write-Info "DRY RUN: Would grant admin consent for:"
    foreach ($perm in $PermissionsToAdd) {
        Write-Info "  - $($perm.Name) [Application]"
    }
} else {
    Write-Step 1 "Looking up service principals..."

    # Get the BFF API service principal
    $bffSp = Get-MgServicePrincipal -Filter "appId eq '$BffApiAppId'" -ErrorAction SilentlyContinue
    if (-not $bffSp) {
        Write-Warn "Service principal not found for BFF API. Creating..."
        if ($PSCmdlet.ShouldProcess($BffApiAppId, "Create service principal")) {
            $bffSp = New-MgServicePrincipal -AppId $BffApiAppId -ErrorAction Stop
            Write-Success "Service principal created (Id: $($bffSp.Id))"
        }
    } else {
        Write-Success "BFF API service principal found (Id: $($bffSp.Id))"
    }

    # Get the Microsoft Graph service principal
    $graphSp = Get-MgServicePrincipal -Filter "appId eq '$GraphServicePrincipalId'" -ErrorAction SilentlyContinue
    if (-not $graphSp) {
        Write-Host "  [ERROR] Microsoft Graph service principal not found in tenant." -ForegroundColor Red
        exit 1
    }
    Write-Success "Microsoft Graph service principal found (Id: $($graphSp.Id))"

    Write-Step 2 "Granting admin consent (app role assignments)..."

    # Get existing app role assignments
    $existingAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $bffSp.Id -ErrorAction SilentlyContinue

    foreach ($perm in $PermissionsToAdd) {
        $alreadyGranted = $existingAssignments | Where-Object {
            $_.AppRoleId -eq $perm.Id -and $_.ResourceId -eq $graphSp.Id
        }

        if ($alreadyGranted) {
            Write-Info "Admin consent already granted: $($perm.Name)"
        } else {
            if ($PSCmdlet.ShouldProcess($perm.Name, "Grant admin consent")) {
                $params = @{
                    PrincipalId = $bffSp.Id
                    ResourceId  = $graphSp.Id
                    AppRoleId   = $perm.Id
                }

                New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $bffSp.Id -BodyParameter $params -ErrorAction Stop
                Write-Success "Admin consent granted: $($perm.Name)"
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "SETUP SUMMARY"

if ($DryRun) {
    Write-Host "  *** DRY RUN — No changes were made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Host "  Tenant ID:          $TenantId" -ForegroundColor White
Write-Host "  BFF API App ID:     $BffApiAppId" -ForegroundColor White
Write-Host ""

Write-Host "  Security Group:" -ForegroundColor Green
Write-Host "    Name:             $GroupDisplayName" -ForegroundColor White
if ($groupId) {
    Write-Host "    ID:               $groupId" -ForegroundColor White
}
Write-Host ""

Write-Host "  Conditional Access Policy:" -ForegroundColor Green
Write-Host "    Name:             $ConditionalAccessPolicyName" -ForegroundColor White
Write-Host "    State:            Report-only (review before enforcing)" -ForegroundColor White
Write-Host "    Exclusion:        $GroupDisplayName" -ForegroundColor White
Write-Host ""

Write-Host "  Graph API Permissions (Application):" -ForegroundColor Green
foreach ($perm in $PermissionsToAdd) {
    Write-Host "    - $($perm.Name)" -ForegroundColor White
}
Write-Host ""

Write-Host "  NEXT STEPS:" -ForegroundColor Cyan
Write-Host "    1. Review Conditional Access policy in Azure Portal" -ForegroundColor White
Write-Host "       https://portal.azure.com/#view/Microsoft_AAD_ConditionalAccess/ConditionalAccessBlade/~/Overview" -ForegroundColor Gray
Write-Host "    2. Change policy state from 'Report-only' to 'On' when ready" -ForegroundColor White
Write-Host "    3. Run Get-LicenseSkuIds.ps1 to capture license SKU IDs for config" -ForegroundColor White
Write-Host "    4. Update BFF appsettings with group ID and SKU IDs" -ForegroundColor White
Write-Host ""
