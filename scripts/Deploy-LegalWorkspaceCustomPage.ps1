<#
.SYNOPSIS
    Deploys the Legal Operations Workspace Custom Page to the Model-Driven App
    in the Dataverse dev environment.

.DESCRIPTION
    Automates the Custom Page deployment pipeline for the Legal Operations Workspace
    (home-corporate-workspace-r1 project). Performs:

      1. Authenticate to Dataverse via PAC CLI
      2. Import the SpaarkeLegalWorkspace solution (PCF control)
      3. Verify solution import via pac solution list
      4. Publish customizations
      5. Verify Custom Page registration in Dataverse (canvasapps query)
      6. Output MDA sitemap XML and next manual steps

    ADR-022: Solution is UNMANAGED for dev environment deployment.
    ADR-008: BFF endpoints use WorkspaceAuthorizationFilter — accepts MSAL token from Custom Page.

    NOTE: The Custom Page itself (sprk_LegalOperationsWorkspace) must be created once
    in Power Apps Maker Portal (make.powerapps.com). Subsequent PCF updates are deployed
    by importing the solution ZIP produced by Package-LegalWorkspace.ps1.

.PARAMETER Environment
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionZip
    Path to the solution ZIP to import. If omitted, script looks for the latest
    SpaarkeLegalWorkspace_v*.zip in src\client\pcf\LegalWorkspace\Solution\bin\.

.PARAMETER SkipAuth
    Skip pac auth create step (use existing active auth profile).

.PARAMETER SkipImport
    Skip solution import (use for publish-only runs after a prior import).

.PARAMETER SkipVerification
    Skip post-import Dataverse API verification queries.

.EXAMPLE
    .\Deploy-LegalWorkspaceCustomPage.ps1
    # Full pipeline: auth check, import latest ZIP, publish, verify.

.EXAMPLE
    .\Deploy-LegalWorkspaceCustomPage.ps1 -SkipAuth -SkipImport
    # Publish only — republish customizations after manual Power Apps Studio edit.

.EXAMPLE
    .\Deploy-LegalWorkspaceCustomPage.ps1 -SolutionZip "C:\path\to\SpaarkeLegalWorkspace_v1.0.1.zip"
    # Import a specific ZIP file.

.NOTES
    Prerequisites:
    - PAC CLI installed: pac --version
    - Authenticated to spaarkedev1: pac auth create --url https://spaarkedev1.crm.dynamics.com
    - Solution ZIP built via: scripts\Package-LegalWorkspace.ps1
    - Task 040 (Solution Packaging) must be complete before running this script.

    ADR Compliance:
    - ADR-022: Uses --environment flag (unmanaged, dev only)
    - ADR-008: Custom Page acquires MSAL token from MDA context for BFF calls
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Environment = "https://spaarkedev1.crm.dynamics.com",
    [string]$SolutionZip = "",
    [switch]$SkipAuth,
    [switch]$SkipImport,
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"

# ---- Configuration -----------------------------------------------------------

$ScriptDir    = Split-Path $MyInvocation.MyCommand.Path -Parent
$RepoRoot     = Split-Path $ScriptDir -Parent
$PcfDir       = Join-Path $RepoRoot "src\client\pcf\LegalWorkspace"
$SolutionDir  = Join-Path $PcfDir "Solution"
$SolutionBin  = Join-Path $SolutionDir "bin"
$CpmProps     = Join-Path $RepoRoot "Directory.Packages.props"

$SolutionName     = "SpaarkeLegalWorkspace"
$CustomPageName   = "sprk_LegalOperationsWorkspace"
$ControlName      = "sprk_Spaarke.Controls.LegalWorkspace"
$BffBaseUrl       = "https://spe-api-dev-67e2xz.azurewebsites.net"

# ---- Banner ------------------------------------------------------------------

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Legal Operations Workspace — Custom Page Deployment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Solution     : $SolutionName (unmanaged — ADR-022)" -ForegroundColor White
Write-Host "  Custom Page  : $CustomPageName" -ForegroundColor White
Write-Host "  Control      : $ControlName" -ForegroundColor White
Write-Host "  Environment  : $Environment" -ForegroundColor White
Write-Host "  BFF Base URL : $BffBaseUrl" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

$StepNum = 1

# ---- Step 1: Resolve solution ZIP -------------------------------------------

Write-Host "[$StepNum] Resolving solution ZIP..." -ForegroundColor Yellow
$StepNum++

if ($SolutionZip -ne "" -and (Test-Path $SolutionZip)) {
    Write-Host "      Using specified ZIP: $SolutionZip" -ForegroundColor Green
} elseif ($SolutionZip -ne "" -and -not (Test-Path $SolutionZip)) {
    Write-Host "  ERROR: Specified SolutionZip not found: $SolutionZip" -ForegroundColor Red
    exit 1
} else {
    # Auto-discover latest ZIP from Solution/bin/
    if (-not (Test-Path $SolutionBin)) {
        Write-Host "  ERROR: Solution bin directory not found: $SolutionBin" -ForegroundColor Red
        Write-Host "  Run scripts\Package-LegalWorkspace.ps1 first to build the solution ZIP." -ForegroundColor Yellow
        exit 1
    }

    $Zips = Get-ChildItem -Path $SolutionBin -Filter "${SolutionName}_v*.zip" | Sort-Object LastWriteTime -Descending
    if ($Zips.Count -eq 0) {
        Write-Host "  ERROR: No solution ZIP found in $SolutionBin" -ForegroundColor Red
        Write-Host "  Run scripts\Package-LegalWorkspace.ps1 first." -ForegroundColor Yellow
        exit 1
    }

    $SolutionZip = $Zips[0].FullName
    Write-Host "      Auto-discovered ZIP: $SolutionZip" -ForegroundColor Green
    if ($Zips.Count -gt 1) {
        Write-Host "      NOTE: Multiple ZIPs found — using most recent. Older ZIPs:" -ForegroundColor Gray
        $Zips[1..($Zips.Count-1)] | ForEach-Object { Write-Host "        $_" -ForegroundColor Gray }
    }
}

$ZipSizeKb = [math]::Round((Get-Item $SolutionZip).Length / 1KB, 1)
Write-Host "      ZIP size: $ZipSizeKb KB" -ForegroundColor Gray
Write-Host ""

# ---- Step 2: PAC CLI authentication check -----------------------------------

Write-Host "[$StepNum] Checking PAC CLI authentication..." -ForegroundColor Yellow
$StepNum++

# Disable CPM if present to avoid NU1008 during pac commands
$CpmDisabled = $false
if (Test-Path $CpmProps) {
    $CpmDisabledPath = "$CpmProps.disabled"
    Rename-Item $CpmProps $CpmDisabledPath -Force
    $CpmDisabled = $true
    Write-Host "      CPM disabled (Directory.Packages.props renamed)." -ForegroundColor Gray
}

try {
    # Check for active auth profile
    $AuthListJson = pac auth list --json 2>&1 | Out-String
    $AuthList = $AuthListJson | ConvertFrom-Json
    $ActiveAuth = $AuthList | Where-Object { $_.IsActive -eq $true } | Select-Object -First 1

    if ($ActiveAuth) {
        $ConnectedUrl = $ActiveAuth.Url
        Write-Host "      Active auth profile found." -ForegroundColor Green
        Write-Host "      Connected to: $ConnectedUrl" -ForegroundColor Green

        # Warn if wrong environment
        if ($ConnectedUrl -notlike "*spaarkedev1*") {
            Write-Host ""
            Write-Host "  WARNING: Active auth is for '$ConnectedUrl', not spaarkedev1." -ForegroundColor Yellow
            Write-Host "  Expected: $Environment" -ForegroundColor Yellow
            Write-Host "  To switch: pac auth select --index <N> after running pac auth list" -ForegroundColor Yellow
            Write-Host ""
        }
    } else {
        if ($SkipAuth) {
            Write-Host "  ERROR: No active PAC auth and -SkipAuth was set." -ForegroundColor Red
            Write-Host "  Run: pac auth create --url $Environment" -ForegroundColor Yellow
            exit 1
        }

        Write-Host "      No active auth profile. Creating new auth to $Environment..." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  INTERACTIVE STEP: Browser will open for Azure AD login." -ForegroundColor Cyan
        Write-Host "  Sign in with your Spaarke dev account." -ForegroundColor Cyan
        Write-Host ""

        pac auth create --url $Environment --name "SpaarkeDevWorkspace"

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ERROR: pac auth create failed." -ForegroundColor Red
            exit $LASTEXITCODE
        }

        Write-Host "      Authentication created." -ForegroundColor Green
    }
} catch {
    Write-Host "  ERROR: Failed to check PAC auth: $_" -ForegroundColor Red
    exit 1
} finally {
    if ($CpmDisabled -and (Test-Path "$CpmProps.disabled")) {
        Rename-Item "$CpmProps.disabled" $CpmProps -Force
        $CpmDisabled = $false
        Write-Host "      CPM restored." -ForegroundColor Gray
    }
}
Write-Host ""

# ---- Step 3: Import solution -------------------------------------------------

if ($SkipImport) {
    Write-Host "[$StepNum] Skipping solution import (-SkipImport)." -ForegroundColor Gray
    $StepNum++
} else {
    Write-Host "[$StepNum] Importing solution to Dataverse..." -ForegroundColor Yellow
    $StepNum++

    Write-Host "      ZIP: $SolutionZip" -ForegroundColor Gray
    Write-Host "      This may take 1-5 minutes for solution processing..." -ForegroundColor Gray
    Write-Host ""

    # Disable CPM again for pac solution import
    $CpmDisabled = $false
    if (Test-Path $CpmProps) {
        Rename-Item $CpmProps "$CpmProps.disabled" -Force
        $CpmDisabled = $true
    }

    try {
        pac solution import `
            --path $SolutionZip `
            --publish-changes `
            --force-overwrite `
            --environment $Environment

        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "  ERROR: pac solution import failed (exit code $LASTEXITCODE)." -ForegroundColor Red
            Write-Host ""
            Write-Host "  Common causes and fixes:" -ForegroundColor Yellow
            Write-Host "    - 'unexpected error': Check [Content_Types].xml has .js and .css entries" -ForegroundColor White
            Write-Host "    - NU1008 conflict: Should be fixed by CPM disable above" -ForegroundColor White
            Write-Host "    - Managed/unmanaged conflict: Check <Managed>0</Managed> in solution.xml" -ForegroundColor White
            Write-Host "    - Auth expired: Re-run pac auth create --url $Environment" -ForegroundColor White
            Write-Host ""
            Write-Host "  Solution ZIP troubleshooting:" -ForegroundColor Yellow
            Write-Host "    Refer to: projects\home-corporate-workspace-r1\notes\solution-packaging-checklist.md" -ForegroundColor White
            exit $LASTEXITCODE
        }

        Write-Host ""
        Write-Host "      Solution import completed." -ForegroundColor Green
    } finally {
        if ($CpmDisabled -and (Test-Path "$CpmProps.disabled")) {
            Rename-Item "$CpmProps.disabled" $CpmProps -Force
            Write-Host "      CPM restored." -ForegroundColor Gray
        }
    }
}
Write-Host ""

# ---- Step 4: Verify solution import -----------------------------------------

Write-Host "[$StepNum] Verifying solution in environment..." -ForegroundColor Yellow
$StepNum++

$CpmDisabled = $false
if (Test-Path $CpmProps) {
    Rename-Item $CpmProps "$CpmProps.disabled" -Force
    $CpmDisabled = $true
}

try {
    Write-Host "      Running: pac solution list | Select-String $SolutionName" -ForegroundColor Gray
    $SolutionList = pac solution list --environment $Environment 2>&1 | Out-String

    if ($SolutionList -match $SolutionName) {
        # Extract version from output
        $Match = [regex]::Match($SolutionList, "$SolutionName\s+([0-9.]+)")
        $ImportedVersion = if ($Match.Success) { $Match.Groups[1].Value } else { "(version not parsed)" }
        Write-Host "      Solution verified in environment: $SolutionName $ImportedVersion" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: '$SolutionName' not found in pac solution list output." -ForegroundColor Yellow
        Write-Host "  The import may still be processing. Wait 30 seconds and run:" -ForegroundColor Yellow
        Write-Host "    pac solution list --environment $Environment | Select-String $SolutionName" -ForegroundColor Gray
    }
} catch {
    Write-Host "  WARNING: Could not verify solution list: $_" -ForegroundColor Yellow
} finally {
    if ($CpmDisabled -and (Test-Path "$CpmProps.disabled")) {
        Rename-Item "$CpmProps.disabled" $CpmProps -Force
    }
}
Write-Host ""

# ---- Step 5: Verify PCF control registration via Dataverse Web API ----------

if (-not $SkipVerification) {
    Write-Host "[$StepNum] Verifying PCF control registration via Dataverse Web API..." -ForegroundColor Yellow
    $StepNum++

    # Get access token from pac
    $CpmDisabled = $false
    if (Test-Path $CpmProps) {
        Rename-Item $CpmProps "$CpmProps.disabled" -Force
        $CpmDisabled = $true
    }

    try {
        $TokenJson = pac auth token --json 2>&1 | Out-String
        $TokenObj = $TokenJson | ConvertFrom-Json
        $AccessToken = $TokenObj.Token

        if ([string]::IsNullOrEmpty($AccessToken)) {
            Write-Host "  WARNING: Could not get access token. Skipping Web API verification." -ForegroundColor Yellow
        } else {
            $ApiHeaders = @{
                "Authorization"  = "Bearer $AccessToken"
                "OData-MaxVersion" = "4.0"
                "OData-Version"  = "4.0"
                "Accept"         = "application/json"
            }
            $ApiBase = "$Environment/api/data/v9.2"

            # Verify custom control (PCF) is registered
            Write-Host "      Checking customcontrols for $ControlName..." -ForegroundColor Gray
            $ControlUrl = "$ApiBase/customcontrols?`$filter=name eq '$ControlName'&`$select=customcontrolid,name,version"
            try {
                $ControlResponse = Invoke-RestMethod -Uri $ControlUrl -Headers $ApiHeaders -Method Get -TimeoutSec 30
                if ($ControlResponse.value -and $ControlResponse.value.Count -gt 0) {
                    $Ctrl = $ControlResponse.value[0]
                    Write-Host "      PCF control registered:" -ForegroundColor Green
                    Write-Host "        Name   : $($Ctrl.name)" -ForegroundColor Green
                    Write-Host "        Version: $($Ctrl.version)" -ForegroundColor Green
                    Write-Host "        ID     : $($Ctrl.customcontrolid)" -ForegroundColor Green
                } else {
                    Write-Host "  WARNING: Custom control '$ControlName' not found via Web API." -ForegroundColor Yellow
                    Write-Host "  The control may still be processing. Check make.powerapps.com." -ForegroundColor Yellow
                }
            } catch {
                Write-Host "  WARNING: Custom control query failed: $_" -ForegroundColor Yellow
            }

            Write-Host ""

            # Check if Custom Page (canvas app) exists
            Write-Host "      Checking canvasapps for $CustomPageName..." -ForegroundColor Gray
            $CanvasUrl = "$ApiBase/canvasapps?`$filter=name eq '$CustomPageName'&`$select=canvasappid,name,displayname,canvasapptype"
            try {
                $CanvasResponse = Invoke-RestMethod -Uri $CanvasUrl -Headers $ApiHeaders -Method Get -TimeoutSec 30
                if ($CanvasResponse.value -and $CanvasResponse.value.Count -gt 0) {
                    $Page = $CanvasResponse.value[0]
                    Write-Host "      Custom Page found in Dataverse:" -ForegroundColor Green
                    Write-Host "        Name        : $($Page.name)" -ForegroundColor Green
                    Write-Host "        Display Name: $($Page.displayname)" -ForegroundColor Green
                    Write-Host "        Type        : $($Page.canvasapptype) (3 = Custom Page)" -ForegroundColor Green
                    Write-Host "        ID          : $($Page.canvasappid)" -ForegroundColor Green
                } else {
                    Write-Host ""
                    Write-Host "  IMPORTANT: Custom Page '$CustomPageName' not found in Dataverse." -ForegroundColor Yellow
                    Write-Host "  The Custom Page must be created manually in Power Apps Maker Portal." -ForegroundColor Yellow
                    Write-Host "  See: projects\home-corporate-workspace-r1\notes\custom-page-registration.md" -ForegroundColor Cyan
                    Write-Host ""
                }
            } catch {
                Write-Host "  WARNING: Canvas apps query failed: $_" -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "  WARNING: Token acquisition failed: $_" -ForegroundColor Yellow
    } finally {
        if ($CpmDisabled -and (Test-Path "$CpmProps.disabled")) {
            Rename-Item "$CpmProps.disabled" $CpmProps -Force
        }
    }
} else {
    Write-Host "[$StepNum] Skipping Web API verification (-SkipVerification)." -ForegroundColor Gray
    $StepNum++
}
Write-Host ""

# ---- Step 6: Publish customizations -----------------------------------------

Write-Host "[$StepNum] Publishing customizations..." -ForegroundColor Yellow
$StepNum++

$CpmDisabled = $false
if (Test-Path $CpmProps) {
    Rename-Item $CpmProps "$CpmProps.disabled" -Force
    $CpmDisabled = $true
}

try {
    Write-Host "      Running: pac solution publish-all --environment $Environment" -ForegroundColor Gray
    pac solution publish-all --environment $Environment

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  WARNING: pac solution publish-all returned exit code $LASTEXITCODE." -ForegroundColor Yellow
        Write-Host "  Customizations may still be publishing. Check make.powerapps.com." -ForegroundColor Yellow
    } else {
        Write-Host "      Customizations published." -ForegroundColor Green
    }
} catch {
    Write-Host "  WARNING: Could not publish customizations: $_" -ForegroundColor Yellow
    Write-Host "  Publish manually in Power Apps Maker Portal or run:" -ForegroundColor Yellow
    Write-Host "    pac solution publish-all --environment $Environment" -ForegroundColor Gray
} finally {
    if ($CpmDisabled -and (Test-Path "$CpmProps.disabled")) {
        Rename-Item "$CpmProps.disabled" $CpmProps -Force
    }
}
Write-Host ""

# ---- Step 7: Output MDA sitemap XML and next steps --------------------------

Write-Host "[$StepNum] Deployment complete. Manual steps required." -ForegroundColor Yellow
$StepNum++

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  REQUIRED: Register Custom Page in MDA Sitemap" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Add this SubArea to the MDA sitemap (via App Designer):" -ForegroundColor Cyan
Write-Host ""
Write-Host @'
  <SubArea Id="sprk_legal_workspace"
           Title="Legal Operations Workspace"
           Icon="$webresource:sprk_workspace_icon_16"
           Type="PageType"
           PageType="custom"
           CustomPage="sprk_LegalOperationsWorkspace"
           CheckSecurity="false">
    <Titles>
      <Title LCID="1033" Title="Legal Workspace" />
    </Titles>
    <Descriptions>
      <Description LCID="1033" Description="Home Corporate Legal Operations dashboard" />
    </Descriptions>
  </SubArea>
'@ -ForegroundColor White
Write-Host ""
Write-Host "  Place this SubArea inside:" -ForegroundColor Cyan
Write-Host @'
  <Area Id="sprk_legal_operations" ...>
    <Group Id="sprk_workspace_group" Title="Workspace">
      <!-- paste SubArea here -->
    </Group>
  </Area>
'@ -ForegroundColor Gray
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  POST-DEPLOYMENT MANUAL STEPS" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  1. OPEN POWER APPS MAKER PORTAL" -ForegroundColor Cyan
Write-Host "     https://make.powerapps.com" -ForegroundColor White
Write-Host "     Select environment: Spaarke Dev (spaarkedev1)" -ForegroundColor White
Write-Host ""
Write-Host "  2. CREATE CUSTOM PAGE (first time only)" -ForegroundColor Cyan
Write-Host "     Solutions > SpaarkeLegalWorkspace > + New > App > Page > Custom page" -ForegroundColor White
Write-Host "     Name: sprk_LegalOperationsWorkspace" -ForegroundColor White
Write-Host "     Width/Height: Flexible (fill viewport)" -ForegroundColor White
Write-Host "     Insert PCF: Spaarke.LegalWorkspace (from SpaarkeLegalWorkspace solution)" -ForegroundColor White
Write-Host "     Size PCF to fill entire page canvas" -ForegroundColor White
Write-Host "     File > Save > File > Publish" -ForegroundColor White
Write-Host ""
Write-Host "  3. UPDATE MDA SITEMAP" -ForegroundColor Cyan
Write-Host "     Apps > [Your MDA App] > Edit > Navigation > Add SubArea (see XML above)" -ForegroundColor White
Write-Host "     Set Type: URL, Page Type: Custom Page" -ForegroundColor White
Write-Host "     Select: sprk_LegalOperationsWorkspace" -ForegroundColor White
Write-Host "     Save and Publish the App" -ForegroundColor White
Write-Host ""
Write-Host "  4. VERIFY IN MDA" -ForegroundColor Cyan
Write-Host "     Hard refresh browser (Ctrl+Shift+R)" -ForegroundColor White
Write-Host "     Navigate to Legal Operations Workspace in MDA nav" -ForegroundColor White
Write-Host "     Verify all 7 blocks render" -ForegroundColor White
Write-Host "     Open DevTools (F12) > Network > confirm 200 from $BffBaseUrl/api/workspace/portfolio" -ForegroundColor White
Write-Host ""
Write-Host "  Full guide: projects\home-corporate-workspace-r1\notes\custom-page-registration.md" -ForegroundColor Gray
Write-Host "  Verification: projects\home-corporate-workspace-r1\notes\deployment-verification-checklist.md" -ForegroundColor Gray
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Deployment Script Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
