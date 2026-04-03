<#
.SYNOPSIS
    Queries tenant license SKU IDs needed for demo user provisioning.

.DESCRIPTION
    Connects to Microsoft Graph and retrieves subscribedSkus, then filters for the
    three licenses required by the Spaarke demo provisioning pipeline:
      - Microsoft Power Apps Plan 2 Trial
      - Microsoft Fabric (Free)
      - Microsoft Power Automate Free

    Outputs a clean table and JSON-ready mapping of SkuPartNumber → SkuId.
    Use the output to populate BFF appsettings DemoProvisioning:LicenseSkuIds config.

    Prerequisites:
      - Microsoft.Graph PowerShell SDK installed (Install-Module Microsoft.Graph -Scope CurrentUser)
      - Entra ID user with at least Directory.Read.All or Organization.Read.All

.PARAMETER TenantId
    Entra ID tenant ID. Default: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER OutputFormat
    Output format: "Table" (default) or "Json".

.PARAMETER ShowAll
    If specified, shows all tenant SKUs (not just the three required ones).

.EXAMPLE
    # Show required license SKU IDs
    .\Get-LicenseSkuIds.ps1

.EXAMPLE
    # Output as JSON for config
    .\Get-LicenseSkuIds.ps1 -OutputFormat Json

.EXAMPLE
    # Show all tenant SKUs to find the right ones
    .\Get-LicenseSkuIds.ps1 -ShowAll

.NOTES
    Project: spaarke-self-service-registration-app
    Task: 013 — Entra ID Setup Scripts
    Idempotent: Yes — read-only operation
#>

[CmdletBinding()]
param(
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",

    [ValidateSet("Table", "Json")]
    [string]$OutputFormat = "Table",

    [switch]$ShowAll
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Configuration — SKU part numbers to search for
# ─────────────────────────────────────────────────────────────────────────────

# These are the SkuPartNumber values for the three required licenses.
# SkuPartNumber is stable across tenants; SkuId (GUID) varies by tenant.
$RequiredSkus = @(
    @{ SkuPartNumber = "POWERAPPS_VIRAL";                FriendlyName = "Microsoft Power Apps Plan 2 Trial" }
    @{ SkuPartNumber = "POWER_BI_STANDARD";              FriendlyName = "Microsoft Fabric (Free)" }
    @{ SkuPartNumber = "FLOW_FREE";                      FriendlyName = "Microsoft Power Automate Free" }
)

# Alternative SKU part numbers (in case tenant has different license plans)
$AlternativeSkus = @{
    "POWERAPPS_VIRAL"    = @("POWERAPPS_DEV", "POWERAPPS_PER_USER", "POWERAPPS_P2_VIRAL")
    "POWER_BI_STANDARD"  = @("POWER_BI_PRO", "PBI_PREMIUM_PER_USER")
    "FLOW_FREE"          = @("FLOW_PER_USER", "FLOW_P2_VIRAL")
}

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

Write-Header "LICENSE SKU ID LOOKUP"

# Verify Microsoft.Graph module is available
$graphModule = Get-Module -ListAvailable -Name Microsoft.Graph.Authentication
if (-not $graphModule) {
    Write-Host "  [ERROR] Microsoft.Graph PowerShell SDK not installed." -ForegroundColor Red
    Write-Host "  Install with: Install-Module Microsoft.Graph -Scope CurrentUser" -ForegroundColor Red
    exit 1
}
Write-Success "Microsoft.Graph PowerShell SDK found (v$($graphModule.Version))"

# Connect to Microsoft Graph
Write-Info "Connecting to Microsoft Graph..."
try {
    Connect-MgGraph -TenantId $TenantId -Scopes "Organization.Read.All" -ErrorAction Stop
}
catch {
    Write-Host "  [ERROR] Failed to connect to Microsoft Graph: $_" -ForegroundColor Red
    exit 1
}

$context = Get-MgContext
if (-not $context) {
    Write-Host "  [ERROR] Not connected to Microsoft Graph." -ForegroundColor Red
    exit 1
}
Write-Success "Connected as: $($context.Account)"
Write-Success "Tenant: $($context.TenantId)"

# ─────────────────────────────────────────────────────────────────────────────
# Query Subscribed SKUs
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "QUERYING SUBSCRIBED SKUs"

$allSkus = Get-MgSubscribedSku -All -ErrorAction Stop
Write-Success "Found $($allSkus.Count) subscribed SKUs in tenant"

if ($ShowAll) {
    Write-Host ""
    Write-Host "  ALL TENANT SKUs:" -ForegroundColor Yellow
    Write-Host "  $("-" * 66)" -ForegroundColor Gray

    $allSkus | Sort-Object SkuPartNumber | ForEach-Object {
        $consumed = $_.ConsumedUnits
        $total = if ($_.PrepaidUnits.Enabled) { $_.PrepaidUnits.Enabled } else { "N/A" }
        Write-Host ("  {0,-45} {1,-36} {2}/{3}" -f $_.SkuPartNumber, $_.SkuId, $consumed, $total) -ForegroundColor White
    }
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Filter Required SKUs
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "REQUIRED LICENSE SKUs"

$results = @()
$missing = @()

foreach ($required in $RequiredSkus) {
    $sku = $allSkus | Where-Object { $_.SkuPartNumber -eq $required.SkuPartNumber }

    if (-not $sku) {
        # Try alternative SKU names
        $altNames = $AlternativeSkus[$required.SkuPartNumber]
        if ($altNames) {
            foreach ($altName in $altNames) {
                $sku = $allSkus | Where-Object { $_.SkuPartNumber -eq $altName }
                if ($sku) {
                    Write-Warn "Primary SKU '$($required.SkuPartNumber)' not found; using alternative '$altName'"
                    break
                }
            }
        }
    }

    if ($sku) {
        $consumed = $sku.ConsumedUnits
        $total = if ($sku.PrepaidUnits.Enabled) { $sku.PrepaidUnits.Enabled } else { 0 }
        $available = $total - $consumed

        $results += [PSCustomObject]@{
            FriendlyName  = $required.FriendlyName
            SkuPartNumber = $sku.SkuPartNumber
            SkuId         = $sku.SkuId
            Consumed      = $consumed
            Total         = $total
            Available     = $available
        }

        $statusColor = if ($available -gt 0) { "Green" } else { "DarkYellow" }
        Write-Host "  [OK] $($required.FriendlyName)" -ForegroundColor Green
        Write-Host "       SkuPartNumber: $($sku.SkuPartNumber)" -ForegroundColor White
        Write-Host "       SkuId:         $($sku.SkuId)" -ForegroundColor White
        Write-Host "       Available:     $available / $total" -ForegroundColor $statusColor
        Write-Host ""
    } else {
        $missing += $required.FriendlyName
        Write-Host "  [!!] $($required.FriendlyName)" -ForegroundColor DarkYellow
        Write-Host "       SkuPartNumber: $($required.SkuPartNumber) — NOT FOUND" -ForegroundColor Red
        Write-Host "       Checked alternatives: $($AlternativeSkus[$required.SkuPartNumber] -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Output
# ─────────────────────────────────────────────────────────────────────────────

if ($missing.Count -gt 0) {
    Write-Warn "Missing $($missing.Count) required license(s): $($missing -join ', ')"
    Write-Info "These licenses must be added to the tenant before demo provisioning will work."
    Write-Info "Visit: https://admin.microsoft.com/Adminportal/Home#/catalog"
    Write-Host ""
}

if ($results.Count -gt 0) {
    Write-Header "OUTPUT — SKU ID MAPPING"

    if ($OutputFormat -eq "Json") {
        # JSON output for direct use in appsettings
        $jsonMap = @{}
        foreach ($r in $results) {
            $jsonMap[$r.SkuPartNumber] = $r.SkuId.ToString()
        }

        $jsonOutput = $jsonMap | ConvertTo-Json -Depth 2
        Write-Host $jsonOutput -ForegroundColor White
        Write-Host ""
        Write-Info "Copy the above into BFF appsettings DemoProvisioning:LicenseSkuIds"
    } else {
        # Table output
        $results | Format-Table -Property FriendlyName, SkuPartNumber, SkuId, Available -AutoSize
    }

    Write-Host ""
    Write-Info "appsettings.json usage:"
    Write-Host ""
    Write-Host '  "DemoProvisioning": {' -ForegroundColor Gray
    Write-Host '    "LicenseSkuIds": [' -ForegroundColor Gray
    foreach ($r in $results) {
        $comma = if ($r -ne $results[-1]) { "," } else { "" }
        Write-Host "      `"$($r.SkuId)`"$comma  // $($r.FriendlyName)" -ForegroundColor Gray
    }
    Write-Host '    ]' -ForegroundColor Gray
    Write-Host '  }' -ForegroundColor Gray
    Write-Host ""
}

# Return results as pipeline output for scripting
$results
