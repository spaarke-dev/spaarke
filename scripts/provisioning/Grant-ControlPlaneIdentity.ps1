<#
.SYNOPSIS
    Idempotently registers the L2 control-plane User-Assigned Managed Identity
    (UAMI) as a Dataverse Application User on the admin registry environment
    (Path X per DS-8), assigns it a scoped custom security role (NOT System
    Administrator), and grants the C5.8 Microsoft Graph app-role set on the
    same UAMI service principal. One script, two grant families, safe to
    re-run every provisioning cycle.

.DESCRIPTION
    customer-provisioning-orchestration-r1 task 111 — the one-shot grant
    script for the L2 control plane's own identity (per DS-8 §4 + §8 rollout
    plan). Consolidates the two operator ceremonies the control plane needs
    before it can write the registry:

      SECTION A — Path X Dataverse App User (admin env only)
        A.1  Ensure a scoped custom security role exists on the admin env
             (default name: "Spaarke Provisioning Registry"; org-level
             Read/Write/Create/Append on sprk_dataverseenvironment plus a
             minimal prvReadUser/BU/Role basics set). Explicitly NOT System
             Administrator per DS-8 §4 recommendation — least privilege.
             SKIPPABLE via -SkipRoleCreation when the operator prefers to
             ship the role in a managed solution.
        A.2  Idempotently register the L2 UAMI as a Dataverse Application
             User by find-by-applicationid, then create-if-absent, then
             ensure the role association (check-then-add). This mirrors the
             production idiom implemented in
             src/server/services/Sprk.Provisioning.ControlPlane/Handlers/
                 DataverseAppUserGraphParity/DataverseWebApiAppUserCreator.cs
             (lines 89-91 find-by-applicationid; 241-248 role association).
             The admin env is the same operation with a different URL — no
             new mechanics.
        A.3  WhoAmI verify against the admin env to confirm the systemuser
             resolves.

      SECTION B — C5.8 Microsoft Graph app-role grants (delegated)
        B.1  Delegate to the existing scripts/Grant-GraphAppRoles.ps1 helper
             (task 015 — already tested, PSScriptAnalyzer-clean, idempotent,
             reads the single-source-of-truth GraphAppRoles.cs catalog). We
             do NOT duplicate the GraphAppRoles.cs parser here — that would
             re-introduce the drift r3 task 062 was created to close and
             would violate CLAUDE.md §11 (reuse over duplicate).

    IDEMPOTENCY CONTRACT (POML acceptance criterion #2, DS-8 §4.3):
      - Fully-configured admin env + fully-granted UAMI: re-run reports "no
        changes needed" and exits 0. Zero net changes: no new roles, no new
        systemusers, no new role associations, no new appRoleAssignments.
      - Partially-configured: re-run applies only the missing delta,
        verifies, and exits 0.
      - Real errors (403 from the caller's Dataverse permissions, unreachable
        endpoint, malformed principal, missing role privilege): exits
        non-zero with a diagnostic. NEVER silently skips — silent skip
        would re-introduce trap T3 silent-fail per design.md §4B.

    BOOTSTRAP REQUIREMENT (POML escalation trigger — DS-8 §4.4):
      This script performs *data-plane* Dataverse writes (systemuser +
      security-role rows) and Graph app-role writes. It CANNOT create the
      identity that runs it — the operator invoking this script MUST hold:
        - Dataverse System Administrator (or an equivalent role granting
          prvCreate/prvWriteSystemUser + prvCreateRole) on the admin env,
          AND
        - Microsoft Graph AppRoleAssignment.ReadWrite.All (usually via
          Global Administrator, Privileged Role Administrator, or Cloud
          Application Administrator).
      This is the SAME bootstrap dependency Path Y (secret-seeding) would
      have had — someone with Dataverse admin rights + KV write access.
      It is a one-time operator ceremony per environment, not a scheduled
      job. Do NOT attempt to have the L2 UAMI grant itself (chicken-and-
      egg).

    AUTH IDIOM:
      Tokens are acquired via 'az account get-access-token --resource
      {url}' — the operator's own az CLI context is the caller. This is
      parity with scripts/Grant-GraphAppRoles.ps1 and
      scripts/Register-EntraAppRegistrations.ps1 (both use az CLI). The
      script does NOT run as the L2 UAMI itself (see BOOTSTRAP above); it
      runs as the operator provisioning the L2 UAMI's admin-env identity.

    OUT OF SCOPE (per DS-8 §5 tenant boundary):
      - Registering the L2 UAMI on a customer-owned-tenant Dataverse env
        (MI tokens are home-tenant-only; the L2 UAMI is in the Spaarke
        tenant; the registry lives only in the admin env).
      - Rotating any KV secrets (Path X is secretless by design).
      - Deleting the Dataverse-ClientSecret KV secret (BINDING never-delete
        rule per r3 handoff — BFF still consumes it; L2 removing its own
        dependency on it does NOT authorize deletion).

.PARAMETER TenantId
    Entra ID tenant ID hosting the admin Dataverse environment AND the L2
    UAMI service principal. MANDATORY (v3.3 tenant-isolation invariant I1
    per r1 design.md §4D / FR-28 — no hardcoded default; forcing the
    operator to pass -TenantId prevents cross-tenant grant accidents).

.PARAMETER AdminEnvironmentUrl
    URL of the admin Dataverse environment that hosts sprk_dataverseenvironment
    (e.g. 'https://spaarkedev1.crm.dynamics.com'). MANDATORY. Must be an
    absolute HTTPS URI.

.PARAMETER UamiClientId
    Application (client) ID of the L2 control-plane UAMI (e.g.
    '965a4a01-01e1-442b-97a6-6a98308018b3'). This is what Dataverse stores
    on the systemuser row's applicationid column. MANDATORY. Obtain via:
      # PREFERRED — deterministic ARM resource ID lookup (no name ambiguity):
      az identity show --ids /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<mi-name> --query clientId -o tsv

      # ⚠️ NAME-BASED LOOKUP (fragile — SF-1 decoy trap per remediation plan §5):
      # the dev subscription has 5 UAMIs; 'spaarke-bff-identity' is NOT the BFF's UAMI.
      # If you use this shape, confirm --resource-group matches the intended UAMI's RG.
      az identity show --name <mi-name> --resource-group <rg>
          --query clientId -o tsv

.PARAMETER UamiPrincipalId
    Service Principal object ID of the L2 control-plane UAMI (NOT the
    appId/clientId). Needed for the C5.8 Graph app-role grants (delegated
    to Grant-GraphAppRoles.ps1). MANDATORY unless -SkipGraphGrants is
    passed. Obtain via:
      # PREFERRED — deterministic ARM resource ID lookup (no name ambiguity):
      az identity show --ids /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<mi-name> --query principalId -o tsv

      # ⚠️ NAME-BASED LOOKUP (fragile — SF-1 decoy trap per remediation plan §5):
      # the dev subscription has 5 UAMIs; 'spaarke-bff-identity' is NOT the BFF's UAMI.
      # If you use this shape, confirm --resource-group matches the intended UAMI's RG.
      az identity show --name <mi-name> --resource-group <rg>
          --query principalId -o tsv

.PARAMETER SecurityRoleName
    Name of the scoped custom security role to ensure on the admin env.
    Default: 'Spaarke Provisioning Registry' (per DS-8 §4.1 recommendation).
    DO NOT change unless the deployment convention is being updated across
    the operator runbook + the L2 CustomerRunGuardOptions header simultaneously.

.PARAMETER SkipRoleCreation
    Skip Section A.1 (custom-role ensure). Use this when the security role
    is being managed via a managed solution import instead of via this
    script. Section A.2 (App User creation) still runs and REQUIRES the
    role to already exist in the admin env — a role-not-found error is
    surfaced clearly.

.PARAMETER SkipGraphGrants
    Skip Section B entirely (delegated Graph app-role grants). Use for
    step-by-step debugging, or when Section B has already been run
    successfully in a prior invocation. Section A still runs.

.PARAMETER GraphAppRolesPath
    Optional explicit path to GraphAppRoles.cs (passed through to
    Grant-GraphAppRoles.ps1). Default resolves to
    src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs
    relative to this script's directory.

.PARAMETER GrantGraphAppRolesScriptPath
    Optional explicit path to Grant-GraphAppRoles.ps1 (the helper this
    script delegates to for Section B). Default resolves to
    ../Grant-GraphAppRoles.ps1 relative to this script's directory.

.PARAMETER DryRun
    Preview mode. Reads current admin-env + UAMI state, reports the delta
    that WOULD be applied by BOTH sections, but writes nothing. Delegated
    to Grant-GraphAppRoles.ps1 -DryRun as well for Section B.

.EXAMPLE
    # First-time grant against the dev admin env
    .\Grant-ControlPlaneIdentity.ps1 `
        -TenantId "a221a95e-6abc-4434-aecc-e48338a1b2f2" `
        -AdminEnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -UamiClientId "965a4a01-01e1-442b-97a6-6a98308018b3" `
        -UamiPrincipalId "38f7693f-e6e2-4a3e-9acf-7f9e29dd4044"

.EXAMPLE
    # Preview what would be granted without applying
    .\Grant-ControlPlaneIdentity.ps1 `
        -TenantId "a221a95e-6abc-4434-aecc-e48338a1b2f2" `
        -AdminEnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -UamiClientId "965a4a01-01e1-442b-97a6-6a98308018b3" `
        -UamiPrincipalId "38f7693f-e6e2-4a3e-9acf-7f9e29dd4044" `
        -DryRun

.EXAMPLE
    # Ship the role via a managed solution; only ensure the App User + Graph grants
    .\Grant-ControlPlaneIdentity.ps1 `
        -TenantId "<tenant>" `
        -AdminEnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -UamiClientId "<uami-appid>" `
        -UamiPrincipalId "<uami-spid>" `
        -SkipRoleCreation

.EXAMPLE
    # Re-run against a fully-configured env — expected output ends with:
    #   [SUCCESS] No changes needed - identity already fully configured.
    # Exit code: 0
    .\Grant-ControlPlaneIdentity.ps1 `
        -TenantId "<tenant>" `
        -AdminEnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -UamiClientId "<uami-appid>" `
        -UamiPrincipalId "<uami-spid>"

.NOTES
    Project:      customer-provisioning-orchestration-r1
    Task:         111 - Author Grant-ControlPlaneIdentity.ps1 (Path X + C5.8)
    Depends on:   005 (GraphAppRoles.cs GUIDs populated), 015 (Grant-GraphAppRoles.ps1)
    Consumed by:  operator runbook, task 112 C1.4 registry client (must land first),
                  and every subsequent L2 registry write.
    Boundary:     Path X only (admin env; Spaarke tenant). DOES NOT grant on
                  customer-owned-tenant Dataverse (out of scope per DS-8 §5).
    Idempotent:   Yes — find-then-create/check-then-add throughout.
                  Section A: role-ensure + app-user-ensure + role-assoc-ensure
                             + WhoAmI verify.
                  Section B: delegated to Grant-GraphAppRoles.ps1 which
                             carries its own pre-check + delta apply +
                             re-read verify.

    ADR alignment:
      - ADR-028 §canonical (Spaarke Auth v2): L2 UAMI as Dataverse App User
        via DefaultAzureCredential is the MI-outbound MUST rule; this script
        realizes the admin-env systemuser row that authorizes it.
      - Path X (DS-8 recommendation): secretless. No new secrets generated,
        no KV writes, no rotation burden.
      - CLAUDE.md §11 (component justification): reuses H10 idiom
        (DataverseWebApiAppUserCreator) verbatim for Section A; reuses
        Grant-GraphAppRoles.ps1 verbatim for Section B — this script is
        composition + admin-env-targeting, not new mechanics.

    Justification (CLAUDE.md §11):
      Existing:   H10 DataverseWebApiAppUserCreator implements this exact
                  registration idiom for customer envs; Grant-GraphAppRoles.ps1
                  implements the Graph grant pattern. Both already tested.
      Extension:  This script reuses both verbatim, pointed at the admin env
                  + the control plane's own UAMI, rather than inventing new
                  mechanics. The scoped-role creation is the ONE piece not
                  pre-existing — it lives here rather than as a third helper
                  because (a) it is inextricably paired with the app-user
                  creation (chicken-and-egg without it) and (b) it is a
                  one-time metadata write, not a runtime pattern worth its
                  own file.
      Cost-of-doing-nothing:
                  Without this script, task 112's C1.4 L2 Dataverse registry
                  client has no authorized identity to write with; H0.5
                  registry swap (task 122), H13 Ready writer (task 184), and
                  the I5 concurrency guard all fail with 401/403 the moment
                  their DI-swaps land. This is a HARD blocker for every
                  downstream Wave G-1..G-7 task that writes the registry.

.LINK
    https://learn.microsoft.com/en-us/power-platform/admin/manage-application-users

.LINK
    https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-retrieve-update-delete-web-api

.LINK
    https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignments
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$AdminEnvironmentUrl,

    [Parameter(Mandatory = $true)]
    [string]$UamiClientId,

    [Parameter(Mandatory = $false)]
    [string]$UamiPrincipalId,

    [Parameter(Mandatory = $false)]
    [string]$SecurityRoleName = 'Spaarke Provisioning Registry',

    [Parameter(Mandatory = $false)]
    [switch]$SkipRoleCreation,

    [Parameter(Mandatory = $false)]
    [switch]$SkipGraphGrants,

    [Parameter(Mandatory = $false)]
    [string]$GraphAppRolesPath,

    [Parameter(Mandatory = $false)]
    [string]$GrantGraphAppRolesScriptPath,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# -----------------------------------------------------------------------------
# Console helpers (style parity with Grant-GraphAppRoles.ps1 + Register-EntraAppRegistrations.ps1)
# -----------------------------------------------------------------------------

function Write-Header {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host ''
}

function Write-Section  { param([Parameter(Mandatory)][string]$Title) Write-Host ''; Write-Host "  === $Title ===" -ForegroundColor Cyan }
function Write-Step     { param([int]$N, [string]$D) Write-Host "  [$N] $D" -ForegroundColor Yellow }
function Write-Success  { param([string]$M) Write-Host "  [OK] $M" -ForegroundColor Green }
function Write-Info     { param([string]$M) Write-Host "  [--] $M" -ForegroundColor Gray }
function Write-Warn     { param([string]$M) Write-Host "  [!!] $M" -ForegroundColor DarkYellow }
function Write-Skip     { param([string]$M) Write-Host "  [SKIP] $M" -ForegroundColor DarkCyan }
function Write-Change   { param([string]$M) Write-Host "  [CHANGE] $M" -ForegroundColor Green }
function Write-Fail     { param([string]$M) Write-Host "  [FAIL] $M" -ForegroundColor Red }

# -----------------------------------------------------------------------------
# Change tracking (idempotency reporter)
# -----------------------------------------------------------------------------

$script:ChangesApplied = New-Object System.Collections.Generic.List[string]
$script:AlreadyPresent = New-Object System.Collections.Generic.List[string]

# -----------------------------------------------------------------------------
# Token acquisition (delegates to operator's az CLI context)
# -----------------------------------------------------------------------------
# Parity with Grant-GraphAppRoles.ps1 (which uses `az rest`): the operator's
# own az CLI session is the caller. NOT the L2 UAMI (see BOOTSTRAP header).
# We prefer 'az account get-access-token' over 'Get-AzAccessToken' because
# every existing production script in this repo uses az CLI, so we do not
# require the operator to install the Az PowerShell module.

function Get-DataverseAccessToken {
    param(
        [Parameter(Mandatory)][string]$ResourceUrl,
        [Parameter(Mandatory)][string]$Tenant
    )

    # $ResourceUrl is the env URL WITHOUT trailing slash (e.g.
    # 'https://spaarkedev1.crm.dynamics.com'). Dataverse expects the resource
    # to match the env URL exactly.
    $normalized = $ResourceUrl.TrimEnd('/')

    $raw = az account get-access-token `
        --resource $normalized `
        --tenant $Tenant `
        --output json 2>&1

    if ($LASTEXITCODE -ne 0) {
        $err = ($raw | Out-String).Trim()
        throw "az account get-access-token failed for resource '$normalized' (exit $LASTEXITCODE): $err"
    }

    $joined = ($raw | Out-String).Trim()
    if (-not $joined) {
        throw "az account get-access-token returned empty output for resource '$normalized'."
    }

    $parsed = $joined | ConvertFrom-Json
    if (-not $parsed.accessToken) {
        throw "az account get-access-token output missing 'accessToken' field for resource '$normalized'."
    }

    return $parsed.accessToken
}

# -----------------------------------------------------------------------------
# Dataverse Web API helper (mirrors DataverseWebApiAppUserCreator.cs
# SendAndReadAsync + ApplyCommonHeaders idioms)
# -----------------------------------------------------------------------------

function Invoke-DataverseWebApi {
    param(
        [Parameter(Mandatory)][ValidateSet('GET','POST','PATCH','DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory = $false)][object]$Body,
        [Parameter(Mandatory = $false)][ValidateSet('Return','Silent','ReturnAll')][string]$ResponseMode = 'Return'
    )

    $headers = @{
        Authorization    = "Bearer $Token"
        Accept           = 'application/json'
        'OData-Version'  = '4.0'
        'OData-MaxVersion' = '4.0'
        Prefer           = 'return=representation'
    }

    $params = @{
        Method      = $Method
        Uri         = $Uri
        Headers     = $headers
        ErrorAction = 'Stop'
    }

    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }

    try {
        $response = Invoke-RestMethod @params
    }
    catch {
        # Surface the Dataverse error body if available — the OData error
        # message is much more useful than the raw HTTP status.
        $statusCode = $null
        $body = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                $reader.Close()
            } catch { $body = $null }
        }
        $msg = "Dataverse $Method $Uri failed"
        if ($statusCode) { $msg += " ($statusCode)" }
        if ($body) { $msg += ": $body" }
        else { $msg += ": $($_.Exception.Message)" }
        throw $msg
    }

    if ($ResponseMode -eq 'Silent') { return $null }
    return $response
}

# -----------------------------------------------------------------------------
# Section A helpers
# -----------------------------------------------------------------------------

function Get-RootBusinessUnitId {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token
    )
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/businessunits?`$filter=parentbusinessunitid eq null&`$select=businessunitid"
    $resp = Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
    if (-not $resp -or -not $resp.value -or $resp.value.Count -eq 0) {
        throw 'Root business unit (parentbusinessunitid eq null) not found on admin env.'
    }
    return $resp.value[0].businessunitid
}

function Get-SecurityRoleId {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$RoleName
    )
    # Dataverse creates 1 root role + N inherited copies (one per child BU).
    # AddPrivilegesRole ONLY works on the root (error 0x80044152 "Inherited roles
    # cannot be modified" otherwise). The root is the copy with parentroleid=null.
    # customer-provisioning-orchestration-r1 Wave H-3 fix-at-discovery 2026-08-21:
    # the previous BU-bound-function filter did not reliably select the root on
    # spaarkedev1 and returned an inherited child, causing AddPrivilegesRole to
    # 400. Explicit parentroleid-null filter is the reliable OData-native form.
    $encoded = [System.Uri]::EscapeDataString($RoleName)
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/roles?`$filter=name eq '$encoded' and _parentroleid_value eq null&`$select=roleid"
    $resp = Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
    if (-not $resp -or -not $resp.value -or $resp.value.Count -eq 0) { return $null }
    return $resp.value[0].roleid
}

# Minimal privilege set for a control-plane registry writer (per DS-8 §4.1).
# NOT System Administrator. Values: privilege *name* + Depth. Depth values:
#   Basic=0, Local=1, Deep=2, Global=3 (Dataverse enum).
# The privilege names below are the standard Dataverse privilege names
# generated for a custom entity (prv{Action}{schemaName}).
$script:RequiredPrivileges = @(
    # Registry table — org-level (Global) read/write/create/append
    @{ Name = 'prvReadsprk_dataverseenvironment';     Depth = 3 },
    @{ Name = 'prvWritesprk_dataverseenvironment';    Depth = 3 },
    @{ Name = 'prvCreatesprk_dataverseenvironment';   Depth = 3 },
    @{ Name = 'prvAppendsprk_dataverseenvironment';   Depth = 3 },
    @{ Name = 'prvAppendTosprk_dataverseenvironment'; Depth = 3 },

    # prvReadUser-class basics needed to resolve the caller's own identity
    # (WhoAmI + audit attribution) and traverse business unit / role
    # relationships. Depth = Local (1) — Dataverse REJECTS Basic (0) on
    # these three: error 0x8004140b "privilege X can't have depth = Basic"
    # (customer-provisioning-orchestration-r1 Wave H-3 fix-at-discovery
    # 2026-08-21; confirmed empirically against spaarkedev1). Local is the
    # minimum accepted depth and matches what OOB System-Administrator-
    # equivalent scoped roles use for these three basics.
    @{ Name = 'prvReadUser';         Depth = 1 },
    @{ Name = 'prvReadBusinessUnit'; Depth = 1 },
    @{ Name = 'prvReadRole';         Depth = 1 }
)

function Get-PrivilegeIdByName {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$PrivilegeName
    )
    $encoded = [System.Uri]::EscapeDataString($PrivilegeName)
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/privileges?`$filter=name eq '$encoded'&`$select=privilegeid,name"
    $resp = Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
    if (-not $resp -or -not $resp.value -or $resp.value.Count -eq 0) { return $null }
    return $resp.value[0].privilegeid
}

function Get-RolePrivilegeIdList {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$RoleId
    )
    # roleprivileges_association returns the privilege GUIDs currently bound
    # to the role (no depth info via this navigation — depth is a property
    # of the association, not fetched here; we only need presence for the
    # idempotency check-then-add).
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/roles($RoleId)/roleprivileges_association?`$select=privilegeid"
    $resp = Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
    if (-not $resp -or -not $resp.value) { return @() }
    return @($resp.value | ForEach-Object { $_.privilegeid })
}

function New-SecurityRole {
    # ShouldProcess gate is declared once at the script level (see the top
    # [CmdletBinding(SupportsShouldProcess=$true)]) and invoked at the CALLER
    # site in Section A.1 -- see the `if ($PSCmdlet.ShouldProcess(...))` block
    # that guards the call to this helper. Duplicating the gate here would
    # prompt twice for the same operation. Suppression is intentional +
    # localized.
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller (Section A.1) owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$RoleName,
        [Parameter(Mandatory)][string]$RootBuId
    )
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/roles"
    $body = @{
        name                              = $RoleName
        description                       = 'Scoped role for the Spaarke L2 control-plane UAMI. Grants org-level Read/Write/Create/Append on sprk_dataverseenvironment plus minimum basics for WhoAmI + audit attribution. Managed by scripts/provisioning/Grant-ControlPlaneIdentity.ps1 per DS-8 rollout plan. Do NOT elevate to System Administrator.'
        'businessunitid@odata.bind'       = "/businessunits($RootBuId)"
    }
    $resp = Invoke-DataverseWebApi -Method POST -Uri $uri -Token $Token -Body $body
    if (-not $resp -or -not $resp.roleid) {
        throw "POST /roles succeeded but response did not contain roleid."
    }
    return $resp.roleid
}

function Add-PrivilegesToRole {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$RoleId,
        [Parameter(Mandatory)][array]$Privileges  # [{PrivilegeId,Depth}]
    )
    # Bound action AddPrivilegesRole per Dataverse Web API:
    #   POST /roles({id})/Microsoft.Dynamics.CRM.AddPrivilegesRole
    #   Body: { "Privileges": [ { "PrivilegeId": "<guid>", "Depth": "Global" }, ... ] }
    # Depth is a string enum (Basic|Local|Deep|Global), NOT the int.
    $depthMap = @{ 0 = 'Basic'; 1 = 'Local'; 2 = 'Deep'; 3 = 'Global' }
    $privilegeArray = foreach ($p in $Privileges) {
        @{
            PrivilegeId = $p.PrivilegeId
            Depth       = $depthMap[[int]$p.Depth]
        }
    }
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/roles($RoleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole"
    $body = @{ Privileges = @($privilegeArray) }
    Invoke-DataverseWebApi -Method POST -Uri $uri -Token $Token -Body $body -ResponseMode Silent | Out-Null
}

function Get-SystemUserIdByApplicationId {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ApplicationId
    )
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/systemusers?`$filter=applicationid eq $ApplicationId&`$select=systemuserid,fullname,isdisabled"
    $resp = Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
    if (-not $resp -or -not $resp.value -or $resp.value.Count -eq 0) { return $null }
    return $resp.value[0]
}

function New-DataverseApplicationUser {
    # See New-SecurityRole note -- ShouldProcess gate lives at the caller
    # (Section A.2).
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller (Section A.2) owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ApplicationId,
        [Parameter(Mandatory)][string]$RootBuId
    )
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/systemusers"
    $body = @{
        applicationid              = $ApplicationId
        'businessunitid@odata.bind' = "/businessunits($RootBuId)"
    }
    $resp = Invoke-DataverseWebApi -Method POST -Uri $uri -Token $Token -Body $body
    if (-not $resp -or -not $resp.systemuserid) {
        throw "POST /systemusers succeeded but response did not contain systemuserid."
    }
    return $resp.systemuserid
}

function Test-RoleAssociatedWithSystemUser {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$SystemUserId,
        [Parameter(Mandatory)][string]$RoleId
    )
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/systemusers($SystemUserId)/systemuserroles_association?`$filter=roleid eq $RoleId&`$select=roleid"
    $resp = Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
    return ($resp -and $resp.value -and $resp.value.Count -gt 0)
}

function Add-RoleToSystemUser {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$SystemUserId,
        [Parameter(Mandatory)][string]$RoleId
    )
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/systemusers($SystemUserId)/systemuserroles_association/`$ref"
    $body = @{
        '@odata.id' = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/roles($RoleId)"
    }
    Invoke-DataverseWebApi -Method POST -Uri $uri -Token $Token -Body $body -ResponseMode Silent | Out-Null
}

function Invoke-WhoAmIVerification {
    param(
        [Parameter(Mandatory)][string]$EnvUrl,
        [Parameter(Mandatory)][string]$Token
    )
    # WhoAmI unbound function — returns UserId + BusinessUnitId + OrganizationId
    # of the caller. Used to verify token issuance and identity resolution.
    $uri = "$($EnvUrl.TrimEnd('/'))/api/data/v9.2/WhoAmI"
    return Invoke-DataverseWebApi -Method GET -Uri $uri -Token $Token
}

# -----------------------------------------------------------------------------
# Pre-flight
# -----------------------------------------------------------------------------

Write-Header 'GRANT CONTROL-PLANE IDENTITY - L2 UAMI Dataverse App User + C5.8 Graph grants (task 111)'

if ($DryRun) {
    Write-Host '  *** DRY RUN MODE - No changes will be made ***' -ForegroundColor Magenta
    Write-Host ''
}

Write-Step 0 'Pre-flight checks'

# Verify PowerShell version.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Fail "PowerShell 7.4+ required. Detected: $($PSVersionTable.PSVersion)."
    exit 2
}
Write-Success "PowerShell version: $($PSVersionTable.PSVersion)"

# Verify az CLI.
# NOTE: capture the native command's full output to a variable BEFORE piping
# through Select-Object -First -- chaining `az ... | Select-Object -First 1`
# directly can short-circuit the pipeline before the native process's exit
# code is captured, leaving $LASTEXITCODE stale/$null (see Deploy-ControlPlane.ps1
# for the reproduction notes). Capturing first avoids the early-pipeline-stop entirely.
$azProbeLines = az --version 2>$null
if ($LASTEXITCODE -ne 0 -or -not $azProbeLines) {
    Write-Fail 'Azure CLI (az) not found on PATH. Install: https://learn.microsoft.com/cli/azure/install-azure-cli'
    exit 2
}
$azProbe = $azProbeLines | Select-Object -First 1
Write-Success "Azure CLI available: $azProbe"

# Verify az login and target tenant match.
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Fail "Azure CLI is not authenticated. Run 'az login --tenant $TenantId --allow-no-subscriptions' first."
    exit 2
}
if ($account.tenantId -ne $TenantId) {
    Write-Fail "Current az context is tenant '$($account.tenantId)', but -TenantId is '$TenantId'. Switch: az login --tenant $TenantId --allow-no-subscriptions"
    exit 2
}
Write-Success "Authenticated as '$($account.user.name)' in tenant $TenantId"

# Verify AdminEnvironmentUrl parses as absolute URI.
try {
    $adminUri = [System.Uri]$AdminEnvironmentUrl
    if (-not $adminUri.IsAbsoluteUri -or $adminUri.Scheme -ne 'https') {
        throw 'not absolute HTTPS'
    }
} catch {
    Write-Fail "AdminEnvironmentUrl '$AdminEnvironmentUrl' is not a valid absolute HTTPS URI."
    exit 2
}
Write-Success "Admin environment URL: $AdminEnvironmentUrl"

# Validate UamiClientId is a GUID.
$parsedGuid = [System.Guid]::Empty
if (-not [System.Guid]::TryParse($UamiClientId, [ref]$parsedGuid)) {
    Write-Fail "UamiClientId '$UamiClientId' is not a valid GUID."
    exit 2
}
Write-Success "UAMI client id: $UamiClientId"

# UamiPrincipalId is only required when we will run Section B.
if (-not $SkipGraphGrants) {
    if ([string]::IsNullOrWhiteSpace($UamiPrincipalId)) {
        Write-Fail '-UamiPrincipalId is required unless -SkipGraphGrants is passed (Section B needs the UAMI SP object ID).'
        exit 2
    }
    $parsedGuid = [System.Guid]::Empty
    if (-not [System.Guid]::TryParse($UamiPrincipalId, [ref]$parsedGuid)) {
        Write-Fail "UamiPrincipalId '$UamiPrincipalId' is not a valid GUID."
        exit 2
    }
    Write-Success "UAMI principal id: $UamiPrincipalId"
}

# Resolve helper script path (only needed for Section B).
if (-not $SkipGraphGrants) {
    if (-not $GrantGraphAppRolesScriptPath) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
        $default = Join-Path $scriptDir '..\Grant-GraphAppRoles.ps1'
        $resolved = Resolve-Path -LiteralPath $default -ErrorAction SilentlyContinue
        if ($resolved) { $GrantGraphAppRolesScriptPath = $resolved.Path }
    }
    if (-not $GrantGraphAppRolesScriptPath -or -not (Test-Path -LiteralPath $GrantGraphAppRolesScriptPath)) {
        Write-Fail "Grant-GraphAppRoles.ps1 not resolved. Pass -GrantGraphAppRolesScriptPath explicitly or ensure the helper exists alongside this script's parent directory."
        exit 2
    }
    Write-Success "Grant-GraphAppRoles.ps1: $GrantGraphAppRolesScriptPath"
}

# -----------------------------------------------------------------------------
# SECTION A - Path X Dataverse App User (admin env)
# -----------------------------------------------------------------------------

Write-Section 'SECTION A - Path X Dataverse App User'

Write-Step 1 'Acquire admin-env Dataverse access token'
$dvToken = Get-DataverseAccessToken -ResourceUrl $AdminEnvironmentUrl -Tenant $TenantId
Write-Success 'Token acquired for admin env'

Write-Step 2 'Resolve root business unit'
$rootBuId = Get-RootBusinessUnitId -EnvUrl $AdminEnvironmentUrl -Token $dvToken
Write-Success "Root BU: $rootBuId"

# --- A.1 Ensure custom security role ---
if ($SkipRoleCreation) {
    Write-Step 3 'Ensure custom security role -- SKIPPED (-SkipRoleCreation)'
    Write-Skip "Role '$SecurityRoleName' assumed to exist (managed via solution or prior run)"
    $roleId = Get-SecurityRoleId -EnvUrl $AdminEnvironmentUrl -Token $dvToken -RoleName $SecurityRoleName
    if (-not $roleId) {
        Write-Fail "Role '$SecurityRoleName' NOT found on admin env. Either drop -SkipRoleCreation to create it, or import the managed solution containing the role first."
        exit 3
    }
    Write-Success "Role verified: $roleId"
}
else {
    Write-Step 3 "Ensure custom security role '$SecurityRoleName'"

    $roleId = Get-SecurityRoleId -EnvUrl $AdminEnvironmentUrl -Token $dvToken -RoleName $SecurityRoleName
    if ($roleId) {
        Write-Skip "Role '$SecurityRoleName' already exists: $roleId"
        $script:AlreadyPresent.Add("security-role:$SecurityRoleName") | Out-Null
    }
    else {
        if ($DryRun) {
            Write-Change "[DRY RUN] would create role '$SecurityRoleName' at root BU"
            $script:ChangesApplied.Add("security-role:$SecurityRoleName (dry-run)") | Out-Null
            # In dry-run we cannot proceed to privilege check because there's
            # no roleId to check against. Skip Section A.2 privilege loop.
            $roleId = $null
        }
        else {
            if ($PSCmdlet.ShouldProcess("admin env '$AdminEnvironmentUrl'", "Create security role '$SecurityRoleName'")) {
                $roleId = New-SecurityRole -EnvUrl $AdminEnvironmentUrl -Token $dvToken -RoleName $SecurityRoleName -RootBuId $rootBuId
                Write-Change "Created role '$SecurityRoleName': $roleId"
                $script:ChangesApplied.Add("security-role:$SecurityRoleName") | Out-Null
            }
        }
    }

    # --- A.1b Ensure the minimum privileges are attached to the role ---
    # Only meaningful when we have a real roleId (skipped in first-run dry-run
    # because we cannot check privileges on a role that has not been created yet).
    if ($roleId) {
        Write-Step 4 "Ensure minimum privileges attached to '$SecurityRoleName'"

        $existingPrivIds = Get-RolePrivilegeIdList -EnvUrl $AdminEnvironmentUrl -Token $dvToken -RoleId $roleId
        $existingSet = New-Object System.Collections.Generic.HashSet[string]
        foreach ($p in $existingPrivIds) { [void]$existingSet.Add($p) }

        $toAdd = New-Object System.Collections.Generic.List[hashtable]
        $notFoundNames = New-Object System.Collections.Generic.List[string]

        foreach ($req in $script:RequiredPrivileges) {
            $privId = Get-PrivilegeIdByName -EnvUrl $AdminEnvironmentUrl -Token $dvToken -PrivilegeName $req.Name
            if (-not $privId) {
                # Privilege not found -- either the sprk_dataverseenvironment
                # entity is not deployed yet, or the privilege naming diverges
                # from the standard (prv{Verb}{schema}). Surface loudly per
                # POML acceptance-criteria #3 -- silent skip would leave the
                # role under-privileged and mask a Dataverse-schema drift.
                $notFoundNames.Add($req.Name) | Out-Null
                continue
            }
            if ($existingSet.Contains($privId)) {
                Write-Skip "already granted: $($req.Name)"
                $script:AlreadyPresent.Add("privilege:$($req.Name)") | Out-Null
            }
            else {
                $toAdd.Add(@{ PrivilegeId = $privId; Depth = $req.Depth; Name = $req.Name }) | Out-Null
            }
        }

        if ($notFoundNames.Count -gt 0) {
            Write-Fail "The following privileges were NOT found in the admin env: $($notFoundNames -join ', ')"
            Write-Info 'This usually means the sprk_dataverseenvironment table is not yet deployed to the admin env, or its schema name diverges from convention. Import the registry solution first (task 023 output), then re-run.'
            exit 3
        }

        if ($toAdd.Count -eq 0) {
            Write-Success "All $($script:RequiredPrivileges.Count) required privileges already present"
        }
        elseif ($DryRun) {
            foreach ($t in $toAdd) {
                Write-Change "[DRY RUN] would add privilege: $($t.Name) (Depth=$($t.Depth))"
                $script:ChangesApplied.Add("privilege:$($t.Name) (dry-run)") | Out-Null
            }
        }
        else {
            if ($PSCmdlet.ShouldProcess("role '$SecurityRoleName' on admin env", "Add $($toAdd.Count) missing privilege(s)")) {
                Add-PrivilegesToRole -EnvUrl $AdminEnvironmentUrl -Token $dvToken -RoleId $roleId -Privileges $toAdd
                foreach ($t in $toAdd) {
                    Write-Change "Added privilege: $($t.Name) (Depth=$($t.Depth))"
                    $script:ChangesApplied.Add("privilege:$($t.Name)") | Out-Null
                }
            }
        }
    }
}

# --- A.2 Ensure Dataverse Application User ---
Write-Step 5 "Ensure Application User for UAMI $UamiClientId"
$existingUser = Get-SystemUserIdByApplicationId -EnvUrl $AdminEnvironmentUrl -Token $dvToken -ApplicationId $UamiClientId
if ($existingUser) {
    Write-Skip "App User already exists: systemuserid=$($existingUser.systemuserid) fullname='$($existingUser.fullname)'"
    if ($existingUser.isdisabled) {
        Write-Warn "App User is currently DISABLED. This script does NOT re-enable systemusers (that is an explicit operator decision). If this is unintentional, re-enable via PPAC or a targeted PATCH."
    }
    $systemUserId = $existingUser.systemuserid
    $script:AlreadyPresent.Add("app-user:$UamiClientId") | Out-Null
}
else {
    if ($DryRun) {
        Write-Change "[DRY RUN] would create Application User for UAMI $UamiClientId at root BU"
        $script:ChangesApplied.Add("app-user:$UamiClientId (dry-run)") | Out-Null
        $systemUserId = $null
    }
    else {
        if ($PSCmdlet.ShouldProcess("admin env '$AdminEnvironmentUrl'", "Create Application User for UAMI $UamiClientId")) {
            $systemUserId = New-DataverseApplicationUser -EnvUrl $AdminEnvironmentUrl -Token $dvToken -ApplicationId $UamiClientId -RootBuId $rootBuId
            Write-Change "Created App User: systemuserid=$systemUserId"
            $script:ChangesApplied.Add("app-user:$UamiClientId") | Out-Null
        }
    }
}

# --- A.2b Ensure role association ---
if ($systemUserId -and $roleId) {
    Write-Step 6 "Ensure role association: '$SecurityRoleName' on App User"
    $alreadyAssociated = Test-RoleAssociatedWithSystemUser `
        -EnvUrl $AdminEnvironmentUrl `
        -Token $dvToken `
        -SystemUserId $systemUserId `
        -RoleId $roleId
    if ($alreadyAssociated) {
        Write-Skip 'Role already associated with App User'
        $script:AlreadyPresent.Add("role-assoc:$SecurityRoleName") | Out-Null
    }
    else {
        if ($DryRun) {
            Write-Change "[DRY RUN] would associate role '$SecurityRoleName' with App User"
            $script:ChangesApplied.Add("role-assoc:$SecurityRoleName (dry-run)") | Out-Null
        }
        else {
            if ($PSCmdlet.ShouldProcess("App User $systemUserId", "Associate role '$SecurityRoleName'")) {
                Add-RoleToSystemUser `
                    -EnvUrl $AdminEnvironmentUrl `
                    -Token $dvToken `
                    -SystemUserId $systemUserId `
                    -RoleId $roleId
                Write-Change "Associated role '$SecurityRoleName' with App User"
                $script:ChangesApplied.Add("role-assoc:$SecurityRoleName") | Out-Null
            }
        }
    }
}
else {
    if ($DryRun) {
        Write-Info '[DRY RUN] role association verification skipped (missing dry-run role or user id)'
    }
    elseif (-not $roleId) {
        Write-Fail "Cannot associate role: roleId is null. (Role '$SecurityRoleName' was neither found nor created.)"
        exit 3
    }
}

# --- A.3 WhoAmI verification ---
Write-Step 7 'WhoAmI verification (admin env, as operator)'
try {
    $whoami = Invoke-WhoAmIVerification -EnvUrl $AdminEnvironmentUrl -Token $dvToken
    Write-Success "WhoAmI OK: UserId=$($whoami.UserId) BusinessUnitId=$($whoami.BusinessUnitId) OrganizationId=$($whoami.OrganizationId)"
} catch {
    Write-Fail "WhoAmI verification failed: $($_.Exception.Message)"
    exit 4
}

Write-Info 'Note: WhoAmI here runs under the OPERATOR identity (the caller of this script), not the UAMI. UAMI-identity WhoAmI happens when L2 runs against the admin env in-cluster; live verification of that path belongs to Wave G-7 acceptance probes (task 177 T2 pair probe).'

# -----------------------------------------------------------------------------
# SECTION B - C5.8 Graph app-role grants (delegated)
# -----------------------------------------------------------------------------

Write-Section 'SECTION B - C5.8 Graph app-role grants (delegated to Grant-GraphAppRoles.ps1)'

if ($SkipGraphGrants) {
    Write-Skip 'Section B skipped (-SkipGraphGrants)'
}
else {
    Write-Step 8 'Delegate to Grant-GraphAppRoles.ps1 for the 14-role catalog grant'

    $delegateArgs = @{
        TenantId        = $TenantId
        UamiPrincipalId = $UamiPrincipalId
    }
    if ($GraphAppRolesPath) { $delegateArgs.GraphAppRolesPath = $GraphAppRolesPath }
    if ($DryRun) { $delegateArgs.DryRun = $true }

    try {
        $delegateFlagPreview = ($delegateArgs.GetEnumerator() | ForEach-Object { "-$($_.Key)" }) -join ' '
        Write-Info "Invoking: $GrantGraphAppRolesScriptPath $delegateFlagPreview"
        & $GrantGraphAppRolesScriptPath @delegateArgs
        $delegateExit = $LASTEXITCODE
    } catch {
        Write-Fail "Grant-GraphAppRoles.ps1 threw: $($_.Exception.Message)"
        exit 5
    }

    if ($delegateExit -ne 0) {
        Write-Fail "Grant-GraphAppRoles.ps1 exited with code $delegateExit (Section B failed)"
        exit $delegateExit
    }
    Write-Success 'Grant-GraphAppRoles.ps1 completed (exit 0)'
    # Note: the delegate script emits its own change tracking; we surface that
    # via its own SUMMARY block above rather than re-summarizing it here.
}

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------

Write-Header 'SUMMARY'
Write-Host ("  Tenant:                {0}" -f $TenantId) -ForegroundColor Gray
Write-Host ("  Admin env URL:         {0}" -f $AdminEnvironmentUrl) -ForegroundColor Gray
Write-Host ("  UAMI client id:        {0}" -f $UamiClientId) -ForegroundColor Gray
if (-not $SkipGraphGrants) {
    Write-Host ("  UAMI principal id:     {0}" -f $UamiPrincipalId) -ForegroundColor Gray
}
Write-Host ("  Security role:         {0}" -f $SecurityRoleName) -ForegroundColor Gray
$roleCreationLabel = if ($SkipRoleCreation) { 'SKIPPED (managed solution assumed)' } else { 'in-script' }
$graphGrantsLabel  = if ($SkipGraphGrants)  { 'SKIPPED' } else { 'delegated to Grant-GraphAppRoles.ps1' }
Write-Host ("  Role creation:         {0}" -f $roleCreationLabel) -ForegroundColor Gray
Write-Host ("  Graph grants (C5.8):   {0}" -f $graphGrantsLabel) -ForegroundColor Gray
Write-Host ''
Write-Host ("  Section A -- Already present: {0}" -f $script:AlreadyPresent.Count) -ForegroundColor Gray
Write-Host ("  Section A -- Changes applied: {0}" -f $script:ChangesApplied.Count) -ForegroundColor `
    $(if ($script:ChangesApplied.Count -gt 0) { 'Green' } else { 'Gray' })
Write-Host ''

if ($DryRun) {
    Write-Info '*** DRY RUN complete -- no changes were applied. Re-run without -DryRun to apply. ***'
}
elseif ($script:ChangesApplied.Count -eq 0 -and (-not $SkipGraphGrants)) {
    # NOTE: Section B ("no changes") is reported by the delegate's own summary,
    # not by this script's counters. So a "no changes needed" line here only
    # makes sense when Section A had zero changes AND (Section B ran OR was
    # skipped). Grant-GraphAppRoles.ps1 emits its own equivalent line.
    Write-Success 'No Section-A changes needed - Dataverse App User already fully configured. Section B result reported above.'
}
elseif ($script:ChangesApplied.Count -eq 0 -and $SkipGraphGrants) {
    Write-Success 'No changes needed - identity already fully configured (Graph grants skipped).'
}

exit 0
