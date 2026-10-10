<#
.SYNOPSIS
    Idempotently grants the Microsoft Graph application (app-only) roles
    listed in a role-catalog source file onto a specified service principal
    (a managed identity). Re-run safe. ADD-ONLY: it never removes a role.

.DESCRIPTION
    Operator helper (customer-provisioning-orchestration-r1 task 015). Two
    catalogs, two identities (task 261 / G31 split them):
      - scripts/provisioning/Grant-ControlPlaneIdentity.ps1 passes
        src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/ControlPlaneGraphAppRoles.cs
        for the L2 control-plane Worker identity.
      - The default, src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs,
        is the CUSTOMER STAMP identity's set. Do NOT run this script against a
        customer stamp identity: H10 owns stamps (it grants the Entra role and
        removes everything else), and the mailbox rows in that catalog are
        granted through Exchange RBAC on stamps (H14a) — an Entra grant of
        them reaches every mailbox in the tenant. Its remaining use is
        Spaarke's own platform BFF identities (mi-bff-api-{env}), which use
        Entra mailbox grants today.

    IDEMPOTENCY CONTRACT (spec.md FR-13 + task 015 POML step 4):
      - Fully-granted UAMI: re-run emits "No changes needed" and exits 0.
      - Partially-granted UAMI: re-run applies only the missing delta,
        verifies via re-read, and exits 0.
      - Real errors (Graph 403, unreachable, malformed principal): exits
        non-zero with a diagnostic. NEVER silent-skips a role — silent skip
        re-introduces trap T3 silent-fail per design.md §4B.

    SOURCE-OF-TRUTH: the catalog file is the single canonical list. This
    script parses (Value, AppRoleId) pairs via regex; NO hardcoded role GUIDs
    live in this script (any hardcoded GUID would reintroduce the drift the
    constant was created to close — r3 task 062 rationale).

    GRANT TARGET: the specified UAMI service principal (per BFF Auth Surface
    Map §C GAP #4). NOT the BFF app registration — that surface is managed by
    scripts/Register-EntraAppRegistrations.ps1 (delegated OAuth2 scopes), a
    deliberately separate concern (delegated vs app-only, different Graph
    endpoints, different failure modes).

    Prerequisites:
      - Azure CLI authenticated as a user with AppRoleAssignment.ReadWrite.All
        on Microsoft Graph (Global Administrator, Privileged Role Administrator,
        or Cloud Application Administrator).
      - PowerShell 7.4+.
      - GraphAppRoles.cs must have all AppRoleId GUIDs populated (no nulls).
        The script fails fast if any are null (r1 task 005 escalation gate —
        wrong or missing GUID silently fails T3 in production).

.PARAMETER TenantId
    Entra ID tenant ID. MANDATORY (v3.3 tenant-isolation invariant I1 per r1
    design.md §4D / FR-28 — no hardcoded default; forcing the operator to
    pass -TenantId prevents cross-tenant grant accidents).

.PARAMETER UamiPrincipalId
    Object ID (NOT appId) of the UAMI service principal on which to grant the
    catalog's Graph app roles. MANDATORY. Obtain via:
      # PREFERRED — deterministic ARM resource ID lookup (no name ambiguity):
      az identity show --ids /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<mi-name> --query principalId -o tsv

      # ⚠️ NAME-BASED LOOKUP (fragile — SF-1 decoy trap per remediation plan §5):
      # the dev subscription has 5 UAMIs; 'spaarke-bff-identity' is NOT the BFF's UAMI.
      # If you use this shape, confirm --resource-group matches the intended UAMI's RG.
      az identity show --name <mi-name> --resource-group <rg> --query principalId -o tsv

.PARAMETER CatalogPath
    MANDATORY (task 261: there is no default - a default made the customer stamp catalog the answer to "grant the BFF's
    roles", which is wrong for every identity but a stamp). One of:
      - src/server/api/Sprk.Bff.Api/Infrastructure/Auth/PlatformBffGraphAppRoles.cs     (Spaarke's platform/demo BFF)
      - src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/ControlPlaneGraphAppRoles.cs (the L2 Worker)
      - src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs                (the stamp set - H10 owns it)
    The script refuses the stamp catalog for any principal that is not a customer stamp identity (mi-spaarke-*), and
    refuses any other catalog for a stamp identity. Alias: -GraphAppRolesPath.

.PARAMETER DryRun
    Preview mode. Reads current UAMI SP appRoleAssignments and reports the
    delta that WOULD be applied, without making any changes. Skips post-apply
    T3 verification (nothing was applied).

.EXAMPLE
    # Grant the platform BFF identity's roles (NOT a customer stamp — H10 owns those)
    .\Grant-GraphAppRoles.ps1 `
        -TenantId "<customer-tenant-guid>" `
        -UamiPrincipalId "<uami-object-id>"

.EXAMPLE
    # Preview delta without applying (dry-run)
    .\Grant-GraphAppRoles.ps1 `
        -TenantId "a221a95e-6abc-4434-aecc-e48338a1b2f2" `
        -UamiPrincipalId "<uami-object-id>" `
        -DryRun

.EXAMPLE
    # Re-run against a fully-granted UAMI — expected output ends with:
    #   [SUCCESS] No changes needed - UAMI SP already has every catalog grant.
    # Exit code: 0
    .\Grant-GraphAppRoles.ps1 `
        -TenantId "<tenant-guid>" `
        -UamiPrincipalId "<uami-object-id>"

.NOTES
    Project:      customer-provisioning-orchestration-r1
    Task:         015 — Author Grant-GraphAppRoles.ps1 helper
    Depends on:   005 (AppRoleId GUIDs populated); 261 (catalog split + evidence)
    Consumed by:  Grant-ControlPlaneIdentity.ps1 (L2 identity); operators for
                  platform BFF identities. H10 does NOT call it (it has its own
                  REST granter, which also removes extra roles).
    Boundary:     Does NOT touch the BFF app-registration grants (that is
                  Register-EntraAppRegistrations.ps1's surface).
    Idempotent:   Yes — pre-check + delta apply + verify pattern (matches
                  Register-EntraAppRegistrations.ps1 style).

    Form decision (task POML step 2): PowerShell regex parse was chosen over a
    C# helper. Regex reliability: GraphAppRoles.cs uses simple flat `public
    const string X = "Value";` + `private const string IdX = "GUID";` + `new
    GraphAppRole(NameConstant, "Display", IdConstant, ...)` array — all three
    parseable with unambiguous regex. Parser guards: if the parser matches
    zero roles OR references an unknown constant name, it fails
    fast with a "parser desync" diagnostic pointing at the file. No new .NET
    project needed; no scripts/GraphAppRolesReader/ created.

.LINK
    https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignments
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$UamiPrincipalId,

    [Parameter(Mandatory = $true)]
    [Alias('GraphAppRolesPath')]
    [string]$CatalogPath,

    [switch]$DryRun,

    # Task 261: a customer STAMP identity (mi-spaarke-{customerId}-{env}) is reconciled by H10 — it holds exactly
    # FileStorageContainer.Selected in Entra and H10 removes everything else. This script is add-only and its default
    # catalog carries the mailbox roles that are Exchange-scoped on stamps, so it refuses a stamp identity unless the
    # operator says so explicitly.
    [switch]$AllowStampPrincipal
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Console helpers (style parity with Register-EntraAppRegistrations.ps1)
# ─────────────────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step    { param([int]$N, [string]$D)  Write-Host "  [$N] $D"       -ForegroundColor Yellow }
function Write-Success { param([string]$M)           Write-Host "  [OK] $M"       -ForegroundColor Green }
function Write-Info    { param([string]$M)           Write-Host "  [--] $M"       -ForegroundColor Gray }
function Write-Warn    { param([string]$M)           Write-Host "  [!!] $M"       -ForegroundColor DarkYellow }
function Write-Skip    { param([string]$M)           Write-Host "  [SKIP] $M"     -ForegroundColor DarkCyan }
function Write-Change  { param([string]$M)           Write-Host "  [CHANGE] $M"   -ForegroundColor Green }
function Write-Fail    { param([string]$M)           Write-Host "  [FAIL] $M"     -ForegroundColor Red }

# ─────────────────────────────────────────────────────────────────────────────
# Change tracking (idempotency reporter — matches Register script style)
# ─────────────────────────────────────────────────────────────────────────────

$script:ChangesApplied = New-Object System.Collections.Generic.List[string]
$script:AlreadyPresent = New-Object System.Collections.Generic.List[string]

# ─────────────────────────────────────────────────────────────────────────────
# GraphAppRoles.cs parser
# ─────────────────────────────────────────────────────────────────────────────
# Parses three shapes from the constant file (r3 task 062):
#
#   public  const string <RoleValueName>  = "<StableStringValue>";  // one per role + GraphResourceAppId
#   private const string Id<Whatever>     = "<GUID>";               // one per role
#   new GraphAppRole(<RoleValueName>, "<Display>", Id<Whatever>, ...) // one row per role
#
# The third shape binds pairs (RoleValueName -> IdConstantName). We then
# resolve each side against the first two maps to yield (Value, AppRoleId).
#
# Parser is deliberately strict: unknown names or zero matches abort
# with a "parser desync" diagnostic. If GraphAppRoles.cs is refactored into a
# non-flat form (partial class, generated code, expression-bodied members),
# the parser will fail fast rather than silently produce a wrong grant set.

function Get-GraphAppRolesFromSource {
    param([string]$SourcePath)

    if (-not (Test-Path $SourcePath)) {
        throw "GraphAppRoles.cs not found at: $SourcePath"
    }

    $content = Get-Content -Raw -Path $SourcePath

    # 1. Well-known Microsoft Graph resource appId (top-level public const).
    $graphAppIdMatch = [regex]::Match($content,
        'public\s+const\s+string\s+GraphResourceAppId\s*=\s*"([0-9a-fA-F-]{36})"\s*;')
    if (-not $graphAppIdMatch.Success) {
        throw "Parser desync: could not locate 'public const string GraphResourceAppId = ...' in $SourcePath"
    }
    $graphResourceAppId = $graphAppIdMatch.Groups[1].Value

    # 2. All public role-name constants: `public const string {Name} = "{Value}";`
    #    Excludes GraphResourceAppId (special-cased above; not an app role).
    $roleNameMap = @{}
    foreach ($m in [regex]::Matches($content,
        'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;')) {
        $name = $m.Groups[1].Value
        if ($name -eq 'GraphResourceAppId') { continue }
        $roleNameMap[$name] = $m.Groups[2].Value
    }

    # 3. All private Id constants: `private const string Id{X} = "{GUID}";`
    $idMap = @{}
    foreach ($m in [regex]::Matches($content,
        'private\s+const\s+string\s+(Id\w+)\s*=\s*"([0-9a-fA-F-]{36})"\s*;')) {
        $idMap[$m.Groups[1].Value] = $m.Groups[2].Value
    }

    # 4. Each GraphAppRole row in the All[] array. Only the first three ctor
    #    args are of interest (Value ref, DisplayName literal, AppRoleId ref).
    #    Regex is non-greedy on the display literal and terminates before the
    #    third comma so trailing arguments don't matter.
    $roles = New-Object System.Collections.Generic.List[pscustomobject]
    foreach ($m in [regex]::Matches($content,
        'new\s+GraphAppRole\s*\(\s*(\w+)\s*,\s*"([^"]+)"\s*,\s*(\w+)')) {
        $nameConst = $m.Groups[1].Value
        $display   = $m.Groups[2].Value
        $idConst   = $m.Groups[3].Value

        if (-not $roleNameMap.ContainsKey($nameConst)) {
            throw "Parser desync: GraphAppRole row references unknown role-name constant '$nameConst' in $SourcePath. Update parser or check file integrity."
        }
        if (-not $idMap.ContainsKey($idConst)) {
            throw "Parser desync: GraphAppRole row references unknown Id constant '$idConst' in $SourcePath. Update parser or check file integrity."
        }

        $roles.Add([pscustomobject]@{
            Value       = $roleNameMap[$nameConst]
            DisplayName = $display
            AppRoleId   = $idMap[$idConst]
        }) | Out-Null
    }

    if ($roles.Count -eq 0) {
        throw "Parser desync: matched zero GraphAppRole rows in $SourcePath — file structure changed?"
    }

    return [pscustomobject]@{
        GraphResourceAppId = $graphResourceAppId
        Roles              = $roles
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Azure CLI + Microsoft Graph helpers
# ─────────────────────────────────────────────────────────────────────────────

# Body passed via temp file (matches Register-EntraAppRegistrations.ps1 pattern;
# avoids Windows/PowerShell double-escaping headaches with inline JSON).
function Invoke-GraphRest {
    param(
        [Parameter(Mandatory)] [ValidateSet('GET','POST','PATCH','DELETE')] [string]$Method,
        [Parameter(Mandatory)] [string]$Uri,
        [string]$Body
    )

    $tempFile = $null
    try {
        if ($Body) {
            $tempFile = [System.IO.Path]::GetTempFileName()
            $Body | Out-File -FilePath $tempFile -Encoding utf8
            $raw = az rest `
                --method $Method.ToLowerInvariant() `
                --uri $Uri `
                --headers "Content-Type=application/json" `
                --body "@$tempFile" `
                --output json 2>&1
        }
        else {
            $raw = az rest `
                --method $Method.ToLowerInvariant() `
                --uri $Uri `
                --output json 2>&1
        }

        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $err = ($raw | Out-String).Trim()
            throw "az rest $Method $Uri failed (exit $exitCode): $err"
        }

        if (-not $raw) { return $null }
        $joined = ($raw | Out-String).Trim()
        if (-not $joined) { return $null }
        return $joined | ConvertFrom-Json
    }
    finally {
        if ($tempFile -and (Test-Path $tempFile)) {
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-GraphSpObjectId {
    param([string]$GraphResourceAppId)

    # OData accepts literal `$filter` / `$select` (no URL encoding required); the
    # earlier `%24filter` form breaks on Windows when az.cmd shells to cmd.exe and
    # the `&%24select=...` fragment is interpreted as a compound command
    # ('%24select' not recognized as an internal or external command — exit 1).
    # customer-provisioning-orchestration-r1 Wave H-3 fix-at-discovery 2026-08-21.
    # PowerShell backtick-escapes `$` so it is not interpolated as a variable.
    $uri = "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=appId eq '$GraphResourceAppId'&`$select=id,displayName"
    $resp = Invoke-GraphRest -Method GET -Uri $uri

    if (-not $resp -or -not $resp.value -or $resp.value.Count -eq 0) {
        throw "Microsoft Graph resource service principal (appId=$GraphResourceAppId) not found in tenant $TenantId. This is Microsoft's tenant-wide SP and should always be present — verify the tenant ID is correct and the operator has ServicePrincipal.Read access."
    }
    return $resp.value[0].id
}

function Get-UamiCurrentGrants {
    param([string]$UamiPrincipalObjectId, [string]$GraphSpObjectId)

    $resp = Invoke-GraphRest -Method GET `
        -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$UamiPrincipalObjectId/appRoleAssignments"

    if (-not $resp -or -not $resp.value) { return @() }

    # Only grants targeted at the Graph resource SP — other grants (e.g., against
    # Dynamics CRM or SharePoint APIs) are irrelevant to this parity check.
    return @($resp.value | Where-Object { $_.resourceId -eq $GraphSpObjectId })
}

function Grant-AppRole {
    param(
        [string]$UamiPrincipalObjectId,
        [string]$GraphSpObjectId,
        [string]$AppRoleId
    )

    $body = @{
        principalId = $UamiPrincipalObjectId
        resourceId  = $GraphSpObjectId
        appRoleId   = $AppRoleId
    } | ConvertTo-Json -Compress

    Invoke-GraphRest -Method POST `
        -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$UamiPrincipalObjectId/appRoleAssignments" `
        -Body $body | Out-Null
}

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "GRANT GRAPH APP-ROLES - H10 UAMI parity (task 015)"

if ($DryRun) {
    Write-Host "  *** DRY RUN MODE - No changes will be made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Step 0 "Pre-flight checks"

# Verify az CLI is available.
$azProbe = az --version 2>$null | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or -not $azProbe) {
    Write-Fail "Azure CLI (az) not found on PATH. Install: https://learn.microsoft.com/cli/azure/install-azure-cli"
    exit 2
}
Write-Success "Azure CLI available: $azProbe"

# Verify az login and target tenant match.
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Fail "Azure CLI is not authenticated. Run 'az login --tenant $TenantId --allow-no-subscriptions' first."
    exit 2
}
if ($account.tenantId -ne $TenantId) {
    Write-Fail "Current az context is authenticated to tenant '$($account.tenantId)', but -TenantId is '$TenantId'. Switch context first: az login --tenant $TenantId --allow-no-subscriptions"
    exit 2
}
Write-Success "Authenticated as '$($account.user.name)' in tenant $TenantId"

# Resolve the catalog path (mandatory; no default).
if (-not (Test-Path -LiteralPath $CatalogPath)) {
    Write-Fail "Catalog not found: $CatalogPath"
    exit 2
}
$GraphAppRolesPath = (Resolve-Path -LiteralPath $CatalogPath).Path
$isStampCatalog = $GraphAppRolesPath -match 'Sprk\.Bff\.Api[\\/]Infrastructure[\\/]Auth[\\/]GraphAppRoles\.cs$'
Write-Success "Catalog source: $GraphAppRolesPath$(if ($isStampCatalog) { ' (customer STAMP set)' })"

# ─────────────────────────────────────────────────────────────────────────────
# Step 1 — Parse the catalog (source of truth for the role IDs)
# ─────────────────────────────────────────────────────────────────────────────

Write-Step 1 "Parse the role catalog (single source of truth for the role IDs)"

$parsed        = Get-GraphAppRolesFromSource -SourcePath $GraphAppRolesPath
$requiredRoles = $parsed.Roles

# Task 261 (G31): the catalogs are evidence-backed and their sizes differ (stamp set 4, L2 set 8) —
# no fixed count. The parser already fails on zero rows or an unknown constant; the role list is
# printed below for the operator to compare with the task 261 note.

# Fail fast on any null AppRoleId (r1 task 005 escalation gate — a null GUID silently
# fails T3 in production per design.md §4B trap catalog).
$nullGuids = @($requiredRoles | Where-Object { -not $_.AppRoleId })
if ($nullGuids.Count -gt 0) {
    Write-Fail "GraphAppRoles.cs has null AppRoleId(s) for: $(($nullGuids | ForEach-Object Value) -join ', '). Complete r1 task 005 (populate GUIDs via live 'az ad sp show' enumeration of Graph resource SP) before running this script."
    exit 3
}

Write-Success "Parsed $($requiredRoles.Count) roles; all AppRoleId GUIDs populated"
foreach ($r in $requiredRoles) {
    Write-Info ("{0,-40} -> {1}" -f $r.Value, $r.AppRoleId)
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 2 — Resolve Microsoft Graph resource SP object ID in this tenant
# ─────────────────────────────────────────────────────────────────────────────

Write-Step 2 "Resolve Microsoft Graph service principal object ID in tenant"
$graphSpObjectId = Get-GraphSpObjectId -GraphResourceAppId $parsed.GraphResourceAppId
Write-Success "Graph SP object ID: $graphSpObjectId (appId $($parsed.GraphResourceAppId))"

# ─────────────────────────────────────────────────────────────────────────────
# Step 3 — Verify the specified UAMI service principal exists
# ─────────────────────────────────────────────────────────────────────────────

Write-Step 3 "Verify UAMI service principal exists"
try {
    # `$select literal per H-3 Windows fix-at-discovery 2026-08-21 (see Get-GraphSpObjectId).
    $uamiSp = Invoke-GraphRest -Method GET `
        -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$UamiPrincipalId`?`$select=id,displayName,servicePrincipalType,appId"
} catch {
    Write-Fail "UAMI service principal '$UamiPrincipalId' not found in tenant $TenantId. Verify you passed the object ID (not the appId or the managed-identity clientId)."
    Write-Info $_.Exception.Message
    exit 3
}
Write-Success "UAMI SP: '$($uamiSp.displayName)' (type=$($uamiSp.servicePrincipalType), appId=$($uamiSp.appId))"

$isStampPrincipal = $uamiSp.displayName -like 'mi-spaarke-*'
if ($isStampCatalog -and -not $isStampPrincipal) {
    Write-Fail "The stamp catalog (GraphAppRoles.cs) is for customer stamp identities only; '$($uamiSp.displayName)' is not one (mi-spaarke-*). Use PlatformBffGraphAppRoles.cs for the platform BFF or ControlPlaneGraphAppRoles.cs for the L2 Worker."
    exit 6
}
if ($isStampPrincipal -and -not $isStampCatalog) {
    Write-Fail "'$($uamiSp.displayName)' is a customer stamp identity: it holds exactly the stamp set. Another catalog must never be granted to it."
    exit 6
}
if ($isStampPrincipal -and -not $AllowStampPrincipal) {
    Write-Fail "'$($uamiSp.displayName)' is a customer stamp identity. H10 owns it (grants FileStorageContainer.Selected, removes every other Graph role); this script is add-only. Re-run with -AllowStampPrincipal only if that is deliberate."
    exit 6
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 4 — Read current app-role assignments on UAMI SP (Graph-resource-scoped)
# ─────────────────────────────────────────────────────────────────────────────

Write-Step 4 "Read current app-role assignments on UAMI SP (Graph-scoped)"
$currentGrants     = Get-UamiCurrentGrants -UamiPrincipalObjectId $UamiPrincipalId -GraphSpObjectId $graphSpObjectId
$currentAppRoleIds = @($currentGrants | ForEach-Object { $_.appRoleId })
Write-Success "Current Graph-scoped grants on UAMI: $($currentGrants.Count)"

# ─────────────────────────────────────────────────────────────────────────────
# Step 5 — Compute delta + apply missing grants
# ─────────────────────────────────────────────────────────────────────────────

Write-Step 5 "Compute delta + apply missing grants"

$missing = @($requiredRoles | Where-Object { $currentAppRoleIds -notcontains $_.AppRoleId })
$present = @($requiredRoles | Where-Object { $currentAppRoleIds -contains  $_.AppRoleId })

foreach ($r in $present) {
    Write-Skip "already granted: $($r.Value)"
    $script:AlreadyPresent.Add($r.Value) | Out-Null
}

foreach ($r in $missing) {
    if ($DryRun) {
        Write-Change "[DRY RUN] would grant: $($r.Value) ($($r.AppRoleId))"
        $script:ChangesApplied.Add("$($r.Value) (dry-run)") | Out-Null
        continue
    }

    try {
        Grant-AppRole `
            -UamiPrincipalObjectId $UamiPrincipalId `
            -GraphSpObjectId       $graphSpObjectId `
            -AppRoleId             $r.AppRoleId
        Write-Change "granted: $($r.Value)"
        $script:ChangesApplied.Add($r.Value) | Out-Null
    } catch {
        # POML escalation trigger + design.md §4B trap T3: silent skip re-introduces
        # T3 silent-fail. Abort on any grant failure so the operator sees it.
        Write-Fail "grant FAILED: $($r.Value) ($($r.AppRoleId))"
        Write-Info $_.Exception.Message
        Write-Warn "Escalation (POML trigger + root CLAUDE.md §6): a failed grant cannot be silent-skipped without reintroducing trap T3 silent-fail. Aborting."
        exit 4
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 6 — T3 parity verification (re-read appRoleAssignments)
# ─────────────────────────────────────────────────────────────────────────────

Write-Step 6 "T3 parity verification (re-read appRoleAssignments)"

if ($DryRun) {
    Write-Info "Dry-run: skipping post-apply verification (nothing applied)"
}
else {
    $verifyGrants  = Get-UamiCurrentGrants -UamiPrincipalObjectId $UamiPrincipalId -GraphSpObjectId $graphSpObjectId
    $verifyIds     = @($verifyGrants | ForEach-Object { $_.appRoleId })
    $stillMissing  = @($requiredRoles | Where-Object { $verifyIds -notcontains $_.AppRoleId })

    if ($stillMissing.Count -gt 0) {
        Write-Fail "T3 verification FAILED - $($stillMissing.Count) role(s) still missing after apply loop:"
        foreach ($m in $stillMissing) {
            Write-Info "  MISSING: $($m.Value) ($($m.AppRoleId))"
        }
        exit 5
    }

    Write-Success "T3 cleared: $($requiredRoles.Count) grants confirmed on UAMI $UamiPrincipalId"
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "SUMMARY"
Write-Host ("  Tenant:            {0}" -f $TenantId) -ForegroundColor Gray
Write-Host ("  UAMI principal ID: {0}" -f $UamiPrincipalId) -ForegroundColor Gray
Write-Host ("  Required roles:    {0}" -f $requiredRoles.Count) -ForegroundColor Gray
Write-Host ("  Already present:   {0}" -f $script:AlreadyPresent.Count) -ForegroundColor Gray
Write-Host ("  Applied this run:  {0}" -f $script:ChangesApplied.Count) -ForegroundColor `
    $(if ($script:ChangesApplied.Count -gt 0) { 'Green' } else { 'Gray' })
Write-Host ""

if (-not $DryRun -and $script:ChangesApplied.Count -eq 0) {
    Write-Success "No changes needed - UAMI SP already has all $($requiredRoles.Count) Graph app-role grants."
}

exit 0
