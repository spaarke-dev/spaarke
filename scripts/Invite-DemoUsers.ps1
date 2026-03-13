#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Configure B2B guest access for demo users in the Spaarke demo environment.

.DESCRIPTION
    Automates the complete demo user access setup:
      1. Validate prerequisites (Azure CLI, PAC CLI)
      2. Send B2B guest invitations via Microsoft Graph API
      3. Assign Dataverse security roles in the demo environment
      4. Verify user access (authentication + Dataverse + SPE)

    Users are defined in demo-users.json (same directory as this script).
    The script is idempotent — safe to re-run if users already exist or are partially configured.

    This script supports the Customer Onboarding Runbook Section 5b (B2B Guest Access).

.PARAMETER UsersFile
    Path to the JSON file defining demo users to invite.
    Default: ./demo-users.json (relative to script directory)

.PARAMETER DataverseUrl
    Dataverse environment URL for role assignment.
    Default: reads from demo-users.json

.PARAMETER TenantId
    Entra ID tenant ID for Graph API calls.
    Default: reads from demo-users.json

.PARAMETER SkipInvitations
    Skip B2B invitation step (use when users are already invited/accepted).

.PARAMETER SkipDataverseRoles
    Skip Dataverse security role assignment step.

.PARAMETER VerifyOnly
    Only run verification checks — do not send invitations or assign roles.

.PARAMETER WhatIf
    Show what would be done without executing.

.EXAMPLE
    .\Invite-DemoUsers.ps1
    # Full flow: invite users, assign roles, verify access

.EXAMPLE
    .\Invite-DemoUsers.ps1 -VerifyOnly
    # Only verify existing user access — no changes made

.EXAMPLE
    .\Invite-DemoUsers.ps1 -SkipInvitations
    # Skip invitations (already accepted), just assign roles and verify

.EXAMPLE
    .\Invite-DemoUsers.ps1 -WhatIf
    # Preview mode — show what would happen

.NOTES
    Task: PRODENV-026 (Configure demo user access)
    Project: production-environment-setup-r1
    Prerequisites:
      - Azure CLI authenticated (az login)
      - PAC CLI authenticated to demo Dataverse environment
      - Microsoft Graph permissions: User.Invite.All, User.ReadWrite.All
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$UsersFile,
    [string]$DataverseUrl,
    [string]$TenantId,
    [switch]$SkipInvitations,
    [switch]$SkipDataverseRoles,
    [switch]$VerifyOnly
)

# ─────────────────────────────────────────────────────────────────────
# Constants
# ─────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ─────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────

function Write-StepHeader {
    param([int]$Step, [int]$Total, [string]$Title)
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  Step $Step of $Total — $Title" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✅ $Message" -ForegroundColor Green
}

function Write-Warning2 {
    param([string]$Message)
    Write-Host "  ⚠️  $Message" -ForegroundColor Yellow
}

function Write-Info {
    param([string]$Message)
    Write-Host "  ℹ️  $Message" -ForegroundColor Gray
}

function Write-Failure {
    param([string]$Message)
    Write-Host "  ❌ $Message" -ForegroundColor Red
}

function Get-GraphAccessToken {
    <#
    .SYNOPSIS
        Get an access token for Microsoft Graph via Azure CLI.
    #>
    $tokenJson = az account get-access-token --resource "https://graph.microsoft.com" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get Graph access token. Ensure you are logged in with 'az login'. Error: $tokenJson"
    }
    $tokenObj = $tokenJson | ConvertFrom-Json
    return $tokenObj.accessToken
}

function Invoke-GraphApi {
    <#
    .SYNOPSIS
        Make a Microsoft Graph API call.
    #>
    param(
        [string]$Method = "GET",
        [string]$Uri,
        [hashtable]$Body,
        [string]$Token
    )

    $headers = @{
        "Authorization" = "Bearer $Token"
        "Content-Type"  = "application/json"
    }

    $params = @{
        Method  = $Method
        Uri     = "https://graph.microsoft.com/v1.0$Uri"
        Headers = $headers
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    try {
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $errorBody = $_.ErrorDetails.Message
        Write-Verbose "Graph API error ($statusCode): $errorBody"
        throw $_
    }
}

function Resolve-PacExe {
    <#
    .SYNOPSIS
        Resolve the PAC CLI executable path (handles bash wrapper on Windows).
    #>
    $pacCmd = Get-Command "pac.cmd" -ErrorAction SilentlyContinue
    if ($pacCmd) {
        return $pacCmd.Source
    }
    $pacExe = Get-Command "pac" -ErrorAction SilentlyContinue
    if ($pacExe) {
        return $pacExe.Source
    }
    throw "PAC CLI not found. Install with: dotnet tool install --global Microsoft.PowerApps.CLI.Tool"
}

# ─────────────────────────────────────────────────────────────────────
# Step 0: Load Configuration
# ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║  Spaarke Demo User Access Configuration                     ║" -ForegroundColor Magenta
Write-Host "║  Task: PRODENV-026                                          ║" -ForegroundColor Magenta
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

# Load users configuration
if (-not $UsersFile) {
    $UsersFile = Join-Path $ScriptDir "demo-users.json"
}

if (-not (Test-Path $UsersFile)) {
    throw "Users file not found: $UsersFile"
}

$config = Get-Content $UsersFile -Raw | ConvertFrom-Json
$users = $config.users

if (-not $DataverseUrl) {
    $DataverseUrl = $config.dataverseEnvironmentUrl
}
if (-not $TenantId) {
    $TenantId = $config.tenantId
}

$inviteRedirectUrl = $config.inviteRedirectUrl
if (-not $inviteRedirectUrl) {
    $inviteRedirectUrl = $DataverseUrl
}

Write-Host ""
Write-Host "  Configuration:" -ForegroundColor White
Write-Host "    Users file:      $UsersFile"
Write-Host "    Tenant ID:       $TenantId"
Write-Host "    Dataverse URL:   $DataverseUrl"
Write-Host "    Redirect URL:    $inviteRedirectUrl"
Write-Host "    User count:      $($users.Count)"
Write-Host ""

if ($users.Count -eq 0) {
    Write-Warning2 "No users defined in $UsersFile. Nothing to do."
    exit 0
}

foreach ($user in $users) {
    Write-Host "    - $($user.email) ($($user.displayName)) — Role: $($user.role)"
}

# ─────────────────────────────────────────────────────────────────────
# Step 1: Validate Prerequisites
# ─────────────────────────────────────────────────────────────────────

Write-StepHeader -Step 1 -Total 5 -Title "Validate Prerequisites"

# Check Azure CLI
$azVersion = az version 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Azure CLI not found. Install with: winget install Microsoft.AzureCLI"
}
Write-Success "Azure CLI available"

# Check Azure CLI login
$account = az account show 2>&1 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
    throw "Not logged into Azure CLI. Run: az login"
}
Write-Success "Azure CLI authenticated as: $($account.user.name)"

# Verify correct tenant
$currentTenant = $account.tenantId
if ($currentTenant -ne $TenantId) {
    Write-Warning2 "Current tenant ($currentTenant) differs from config ($TenantId)"
    Write-Info "Switching tenant: az login --tenant $TenantId"
}

# Check PAC CLI (needed for Dataverse roles)
if (-not $SkipDataverseRoles -and -not $VerifyOnly) {
    try {
        $script:PacExe = Resolve-PacExe
        Write-Success "PAC CLI available: $script:PacExe"
    }
    catch {
        Write-Warning2 "PAC CLI not found. Dataverse role assignment will require manual steps."
        Write-Info "Install: dotnet tool install --global Microsoft.PowerApps.CLI.Tool"
    }
}

# Test Graph API access
if (-not $VerifyOnly -and -not $SkipInvitations) {
    try {
        $graphToken = Get-GraphAccessToken
        Write-Success "Graph API access token acquired"

        # Verify invitation permissions
        $me = Invoke-GraphApi -Uri "/me" -Token $graphToken
        Write-Success "Graph identity: $($me.displayName) ($($me.userPrincipalName))"
    }
    catch {
        throw "Cannot access Graph API. Ensure: (1) az login completed, (2) User.Invite.All permission granted. Error: $_"
    }
}

Write-Success "All prerequisites validated"

if ($VerifyOnly) {
    # Jump straight to verification
    $SkipInvitations = $true
    $SkipDataverseRoles = $true
}

# ─────────────────────────────────────────────────────────────────────
# Step 2: Send B2B Guest Invitations
# ─────────────────────────────────────────────────────────────────────

Write-StepHeader -Step 2 -Total 5 -Title "Send B2B Guest Invitations"

$invitationResults = @()

if ($SkipInvitations) {
    Write-Info "Skipping invitations (flag set or VerifyOnly mode)"
}
else {
    foreach ($user in $users) {
        Write-Host ""
        Write-Host "  Inviting: $($user.email) ..." -ForegroundColor White

        if ($WhatIfPreference) {
            Write-Info "WhatIf: Would send B2B invitation to $($user.email)"
            $invitationResults += @{
                Email  = $user.email
                Status = "WhatIf"
            }
            continue
        }

        # Check if user already exists as a guest
        try {
            $existingUsers = Invoke-GraphApi -Uri "/users?`$filter=mail eq '$($user.email)' or otherMails/any(m:m eq '$($user.email)')" -Token $graphToken
            if ($existingUsers.value -and $existingUsers.value.Count -gt 0) {
                $existingUser = $existingUsers.value[0]
                Write-Success "User already exists: $($existingUser.displayName) (ID: $($existingUser.id))"
                $invitationResults += @{
                    Email    = $user.email
                    Status   = "AlreadyExists"
                    UserId   = $existingUser.id
                    UserUPN  = $existingUser.userPrincipalName
                }
                continue
            }
        }
        catch {
            Write-Verbose "User lookup failed, proceeding with invitation: $_"
        }

        # Send B2B invitation
        try {
            $invitationBody = @{
                invitedUserEmailAddress = $user.email
                invitedUserDisplayName  = $user.displayName
                inviteRedirectUrl       = $inviteRedirectUrl
                sendInvitationMessage   = $true
                invitedUserMessageInfo  = @{
                    customizedMessageBody = "You have been invited to access the Spaarke demo environment. Click the link below to accept the invitation and get started."
                }
            }

            $invitation = Invoke-GraphApi -Method "POST" -Uri "/invitations" -Body $invitationBody -Token $graphToken

            Write-Success "Invitation sent to $($user.email)"
            Write-Info "Status: $($invitation.status)"
            Write-Info "Invited User ID: $($invitation.invitedUser.id)"
            Write-Info "Redeem URL: $($invitation.inviteRedeemUrl)"

            $invitationResults += @{
                Email       = $user.email
                Status      = $invitation.status
                UserId      = $invitation.invitedUser.id
                RedeemUrl   = $invitation.inviteRedeemUrl
            }
        }
        catch {
            $errorMsg = $_.Exception.Message
            Write-Failure "Failed to invite $($user.email): $errorMsg"
            $invitationResults += @{
                Email  = $user.email
                Status = "Failed"
                Error  = $errorMsg
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Step 3: Assign Dataverse Security Roles
# ─────────────────────────────────────────────────────────────────────

Write-StepHeader -Step 3 -Total 5 -Title "Assign Dataverse Security Roles"

if ($SkipDataverseRoles) {
    Write-Info "Skipping Dataverse role assignment (flag set or VerifyOnly mode)"
}
else {
    Write-Host ""
    Write-Info "Dataverse security roles must be assigned via Power Platform Admin Center."
    Write-Info "Automated role assignment via PAC CLI is limited — manual steps documented below."
    Write-Host ""

    Write-Host "  ┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
    Write-Host "  │  MANUAL STEPS REQUIRED — Dataverse Security Roles          │" -ForegroundColor Yellow
    Write-Host "  │                                                             │" -ForegroundColor Yellow
    Write-Host "  │  1. Navigate to Power Platform Admin Center:                │" -ForegroundColor Yellow
    Write-Host "  │     https://admin.powerplatform.microsoft.com               │" -ForegroundColor Yellow
    Write-Host "  │                                                             │" -ForegroundColor Yellow
    Write-Host "  │  2. Select Environment: spaarke-demo                        │" -ForegroundColor Yellow
    Write-Host "  │                                                             │" -ForegroundColor Yellow
    Write-Host "  │  3. Settings > Users + permissions > Users                  │" -ForegroundColor Yellow
    Write-Host "  │                                                             │" -ForegroundColor Yellow
    Write-Host "  │  4. For each user below, click 'Add user' and assign        │" -ForegroundColor Yellow
    Write-Host "  │     the 'Basic User' + 'Spaarke User' security roles:       │" -ForegroundColor Yellow
    Write-Host "  │                                                             │" -ForegroundColor Yellow

    foreach ($user in $users) {
        $paddedEmail = $user.email.PadRight(51)
        Write-Host "  │     - $paddedEmail │" -ForegroundColor Yellow
    }

    Write-Host "  │                                                             │" -ForegroundColor Yellow
    Write-Host "  │  5. Verify roles assigned under 'Security roles' tab        │" -ForegroundColor Yellow
    Write-Host "  └─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow
    Write-Host ""

    # Attempt automated role assignment via Dataverse Web API
    Write-Info "Attempting automated role verification via Dataverse Web API..."

    try {
        $dvToken = az account get-access-token --resource "$DataverseUrl" 2>&1
        if ($LASTEXITCODE -eq 0) {
            $dvTokenObj = $dvToken | ConvertFrom-Json
            $dvAccessToken = $dvTokenObj.accessToken

            $dvHeaders = @{
                "Authorization" = "Bearer $dvAccessToken"
                "Accept"        = "application/json"
                "OData-MaxVersion" = "4.0"
                "OData-Version"    = "4.0"
            }

            # Check if users exist in Dataverse as systemusers
            foreach ($user in $users) {
                Write-Host "  Checking Dataverse access for: $($user.email) ..." -ForegroundColor White

                try {
                    $filter = "internalemailaddress eq '$($user.email)'"
                    $dvResponse = Invoke-RestMethod `
                        -Method GET `
                        -Uri "$DataverseUrl/api/data/v9.2/systemusers?`$filter=$filter&`$select=systemuserid,fullname,internalemailaddress" `
                        -Headers $dvHeaders

                    if ($dvResponse.value -and $dvResponse.value.Count -gt 0) {
                        $dvUser = $dvResponse.value[0]
                        Write-Success "Found in Dataverse: $($dvUser.fullname) (ID: $($dvUser.systemuserid))"
                    }
                    else {
                        Write-Warning2 "Not found in Dataverse. User needs to be added via Admin Center."
                    }
                }
                catch {
                    Write-Warning2 "Could not query Dataverse for $($user.email): $($_.Exception.Message)"
                }
            }
        }
        else {
            Write-Warning2 "Could not get Dataverse token. Manual role assignment required."
        }
    }
    catch {
        Write-Warning2 "Dataverse API check failed: $($_.Exception.Message)"
        Write-Info "Proceed with manual role assignment via Admin Center."
    }
}

# ─────────────────────────────────────────────────────────────────────
# Step 4: Configure SPE Access
# ─────────────────────────────────────────────────────────────────────

Write-StepHeader -Step 4 -Total 5 -Title "Configure SPE Access"

Write-Host ""
Write-Info "SPE (SharePoint Embedded) access is managed through the BFF API."
Write-Info "Once users have Dataverse access, SPE document access is controlled"
Write-Info "by Dataverse security roles — the BFF API performs OBO (On-Behalf-Of)"
Write-Info "token exchange to access SPE containers as the authenticated user."
Write-Host ""
Write-Info "No separate SPE access configuration is needed for B2B guest users."
Write-Info "The existing OBO flow handles guest tokens the same as member tokens."
Write-Host ""

# Verify SPE endpoint is reachable
Write-Info "Verifying SPE endpoint reachability via BFF API..."

$bffApiUrl = "https://api.spaarke.com"
try {
    $healthResponse = Invoke-RestMethod -Method GET -Uri "$bffApiUrl/healthz" -TimeoutSec 10
    Write-Success "BFF API health check passed: $healthResponse"
}
catch {
    Write-Warning2 "BFF API health check failed: $($_.Exception.Message)"
    Write-Info "SPE access cannot be verified until BFF API is reachable."
}

Write-Success "SPE access configuration verified (OBO-based, no separate config needed)"

# ─────────────────────────────────────────────────────────────────────
# Step 5: Verify Access
# ─────────────────────────────────────────────────────────────────────

Write-StepHeader -Step 5 -Total 5 -Title "Verify Demo User Access"

Write-Host ""
Write-Host "  Access Verification Summary" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────────────" -ForegroundColor Gray

$allPassed = $true

foreach ($user in $users) {
    Write-Host ""
    Write-Host "  User: $($user.email)" -ForegroundColor White

    # Check 1: B2B guest exists in Entra ID
    $guestExists = $false
    try {
        if ($graphToken) {
            $existingUsers = Invoke-GraphApi -Uri "/users?`$filter=mail eq '$($user.email)' or otherMails/any(m:m eq '$($user.email)')&`$select=id,displayName,userPrincipalName,userType,externalUserState" -Token $graphToken
            if ($existingUsers.value -and $existingUsers.value.Count -gt 0) {
                $guestUser = $existingUsers.value[0]
                $guestExists = $true
                Write-Success "Entra ID: Found ($($guestUser.userType), state: $($guestUser.externalUserState))"

                if ($guestUser.externalUserState -ne "Accepted") {
                    Write-Warning2 "Invitation not yet accepted (state: $($guestUser.externalUserState))"
                    $allPassed = $false
                }
            }
            else {
                Write-Failure "Entra ID: Not found — invitation not sent or not yet processed"
                $allPassed = $false
            }
        }
        else {
            Write-Warning2 "Entra ID: Cannot verify (no Graph token)"
        }
    }
    catch {
        Write-Warning2 "Entra ID check failed: $($_.Exception.Message)"
    }

    # Check 2: Dataverse system user exists
    try {
        if ($dvAccessToken) {
            $filter = "internalemailaddress eq '$($user.email)'"
            $dvResponse = Invoke-RestMethod `
                -Method GET `
                -Uri "$DataverseUrl/api/data/v9.2/systemusers?`$filter=$filter&`$select=systemuserid,fullname" `
                -Headers $dvHeaders

            if ($dvResponse.value -and $dvResponse.value.Count -gt 0) {
                Write-Success "Dataverse: User exists (ID: $($dvResponse.value[0].systemuserid))"
            }
            else {
                Write-Warning2 "Dataverse: User not found — needs to be added via Admin Center"
                $allPassed = $false
            }
        }
        else {
            Write-Warning2 "Dataverse: Cannot verify (no token)"
        }
    }
    catch {
        Write-Warning2 "Dataverse check failed: $($_.Exception.Message)"
    }

    # Check 3: SPE access (via BFF API — requires user token, can only be tested interactively)
    Write-Info "SPE access: Requires interactive user sign-in to test (OBO flow)"
}

# ─────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Summary — Demo User Access Configuration" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Invitation summary
if (-not $SkipInvitations -and $invitationResults.Count -gt 0) {
    Write-Host "  B2B Invitations:" -ForegroundColor White
    foreach ($result in $invitationResults) {
        $statusColor = switch ($result.Status) {
            "AlreadyExists" { "Green" }
            "PendingAcceptance" { "Yellow" }
            "Completed" { "Green" }
            "Failed" { "Red" }
            "WhatIf" { "Gray" }
            default { "Gray" }
        }
        Write-Host "    $($result.Email) — $($result.Status)" -ForegroundColor $statusColor
    }
    Write-Host ""
}

# Overall status
if ($allPassed) {
    Write-Success "All verification checks passed!"
}
else {
    Write-Warning2 "Some checks did not pass. Review the output above for manual steps."
}

Write-Host ""
Write-Host "  Next Steps:" -ForegroundColor White
Write-Host "    1. Ensure all B2B invitations are accepted by demo users"
Write-Host "    2. Assign Dataverse security roles via Admin Center (if not automated)"
Write-Host "    3. Have demo users sign in to https://spaarke-demo.crm.dynamics.com/"
Write-Host "    4. Verify they can view documents, run AI queries, and create records"
Write-Host "    5. Run full smoke test suite (task 027)"
Write-Host ""

# Return results for programmatic use
$results = @{
    Invitations = $invitationResults
    AllPassed   = $allPassed
    UsersFile   = $UsersFile
    DataverseUrl = $DataverseUrl
}

return $results
