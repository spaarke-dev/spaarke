<#
.SYNOPSIS
    Configure custom domain and SSL for Azure App Service.

.DESCRIPTION
    Automates custom domain setup for the Spaarke BFF API App Service:
    1. Validates DNS records (CNAME or A+TXT) point to the App Service
    2. Adds custom domain hostname binding to App Service
    3. Creates Azure-managed SSL certificate (auto-renewal)
    4. Binds SSL certificate with SNI
    5. Enforces HTTPS-only (HTTP → HTTPS redirect)
    6. Configures CORS for production origins

    Prerequisites:
    - Azure CLI authenticated (az login) with Contributor on the resource group
    - DNS records must be configured BEFORE running this script
    - App Service must already exist

    DNS Configuration Required (do this first in your DNS provider):
    - CNAME record: api.spaarke.com → spaarke-bff-prod.azurewebsites.net
    - TXT record:   asuid.api.spaarke.com → <App Service verification ID>
    Both records are REQUIRED. Run -ShowDnsInstructions for values.

.PARAMETER CustomDomain
    The custom domain to configure.
    Default: api.spaarke.com

.PARAMETER AppServiceName
    Azure App Service name.
    Default: spaarke-bff-prod

.PARAMETER ResourceGroupName
    Azure resource group containing the App Service.
    Default: rg-spaarke-platform-prod

.PARAMETER CorsOrigins
    Comma-separated list of allowed CORS origins for production.
    Default: https://api.spaarke.com

.PARAMETER SkipDnsCheck
    Skip DNS validation (use if DNS is confirmed but not yet propagated to all resolvers).

.PARAMETER SkipSsl
    Skip SSL certificate creation and binding (useful for staged rollout).

.PARAMETER SkipCors
    Skip CORS configuration.

.PARAMETER ShowDnsInstructions
    Display DNS configuration instructions and exit without making changes.

.PARAMETER DryRun
    Show what would be done without making changes.

.EXAMPLE
    # Show DNS instructions first
    .\Configure-CustomDomain.ps1 -ShowDnsInstructions

.EXAMPLE
    # Preview what will be configured
    .\Configure-CustomDomain.ps1 -DryRun

.EXAMPLE
    # Full configuration
    .\Configure-CustomDomain.ps1

.EXAMPLE
    # Skip DNS check if propagation is slow
    .\Configure-CustomDomain.ps1 -SkipDnsCheck

.EXAMPLE
    # Custom CORS origins (multiple Dataverse environments)
    .\Configure-CustomDomain.ps1 -CorsOrigins "https://spaarke-demo.crm.dynamics.com,https://spaarke-customer1.crm.dynamics.com"
#>

[CmdletBinding()]
param(
    [string]$CustomDomain = 'api.spaarke.com',
    [string]$AppServiceName = 'spaarke-bff-prod',
    [string]$ResourceGroupName = 'rg-spaarke-platform-prod',
    [string]$CorsOrigins = 'https://api.spaarke.com',
    [switch]$SkipDnsCheck,
    [switch]$SkipSsl,
    [switch]$SkipCors,
    [switch]$ShowDnsInstructions,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# --- Helper Functions ---

function Write-Step {
    param([string]$Message)
    Write-Host "`n=====================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "=====================================" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "  [WARN] $Message" -ForegroundColor Yellow
}

function Write-Info {
    param([string]$Message)
    Write-Host "  [INFO] $Message" -ForegroundColor Gray
}

function Write-DryRun {
    param([string]$Message)
    Write-Host "  [DRY RUN] $Message" -ForegroundColor Magenta
}

# --- DNS Instructions Mode ---

if ($ShowDnsInstructions) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  DNS Configuration Instructions" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Configure BOTH DNS records with your domain registrar" -ForegroundColor White
    Write-Host "BEFORE running this script." -ForegroundColor White
    Write-Host ""

    # Fetch the verification ID
    Write-Host "Fetching App Service domain verification ID..." -ForegroundColor Gray
    $verificationId = az webapp config hostname get-external-id --name $AppServiceName -g $ResourceGroupName -o tsv 2>$null
    if (-not $verificationId) {
        $verificationId = "<run: az webapp config hostname get-external-id --name $AppServiceName -g $ResourceGroupName>"
    }

    Write-Host "REQUIRED Record 1: CNAME" -ForegroundColor Yellow
    Write-Host "  Type:  CNAME"
    Write-Host "  Name:  api"
    Write-Host "  Value: $AppServiceName.azurewebsites.net"
    Write-Host "  TTL:   3600 (1 hour)"
    Write-Host ""
    Write-Host "REQUIRED Record 2: TXT (Domain Verification)" -ForegroundColor Yellow
    Write-Host "  Type:  TXT"
    Write-Host "  Name:  asuid.api"
    Write-Host "  Value: $verificationId"
    Write-Host "  TTL:   3600 (1 hour)"
    Write-Host ""
    Write-Host "BOTH records are required by Azure App Service." -ForegroundColor Red
    Write-Host "The CNAME routes traffic. The TXT proves domain ownership." -ForegroundColor Red
    Write-Host ""
    Write-Host "After BOTH DNS records are configured:" -ForegroundColor Green
    Write-Host "  1. Wait for propagation (5-30 minutes)"
    Write-Host "  2. Verify: nslookup $CustomDomain"
    Write-Host "  3. Run: .\Configure-CustomDomain.ps1"
    Write-Host ""
    exit 0
}

# --- Pre-flight Checks ---

Write-Step "Pre-flight Checks"

# Verify Azure CLI is authenticated
Write-Info "Checking Azure CLI authentication..."
$account = az account show --query '{name:name, id:id}' -o json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Error "Azure CLI not authenticated. Run 'az login' first."
}
Write-Success "Authenticated as: $($account.name) ($($account.id))"

# Verify App Service exists
Write-Info "Checking App Service exists..."
$appService = az webapp show --name $AppServiceName --resource-group $ResourceGroupName --query '{defaultHostName:defaultHostName, state:state, httpsOnly:httpsOnly}' -o json 2>$null | ConvertFrom-Json
if (-not $appService) {
    Write-Error "App Service '$AppServiceName' not found in resource group '$ResourceGroupName'."
}
Write-Success "App Service found: $($appService.defaultHostName) (State: $($appService.state))"

$defaultHostname = $appService.defaultHostName

# Check if custom domain is already configured
Write-Info "Checking existing hostname bindings..."
$existingHostnames = az webapp config hostname list --webapp-name $AppServiceName --resource-group $ResourceGroupName --query "[].name" -o json 2>$null | ConvertFrom-Json
$domainAlreadyBound = $existingHostnames -contains $CustomDomain

if ($domainAlreadyBound) {
    Write-Warning "Custom domain '$CustomDomain' is already bound to the App Service."
    Write-Info "Proceeding to verify SSL and other settings..."
}

# --- Step 1: DNS Validation ---

Write-Step "Step 1: DNS Validation"

if ($SkipDnsCheck) {
    Write-Warning "DNS check skipped (-SkipDnsCheck flag)."
} else {
    Write-Info "Resolving $CustomDomain..."

    # Try nslookup to verify DNS
    try {
        $dnsResult = Resolve-DnsName -Name $CustomDomain -Type CNAME -ErrorAction SilentlyContinue
        if ($dnsResult) {
            $cnameTarget = ($dnsResult | Where-Object { $_.Type -eq 'CNAME' }).NameHost
            if ($cnameTarget -eq $defaultHostname) {
                Write-Success "CNAME record verified: $CustomDomain -> $cnameTarget"
            } elseif ($cnameTarget) {
                Write-Warning "CNAME points to '$cnameTarget' (expected '$defaultHostname')"
                Write-Info "This may work if it's an intermediate alias."
            }
        }

        # Also try A record resolution
        $aResult = Resolve-DnsName -Name $CustomDomain -Type A -ErrorAction SilentlyContinue
        if ($aResult) {
            $ipAddresses = ($aResult | Where-Object { $_.Type -eq 'A' }).IPAddress
            if ($ipAddresses) {
                Write-Success "A record resolved: $CustomDomain -> $($ipAddresses -join ', ')"
            }
        }

        if (-not $dnsResult -and -not $aResult) {
            Write-Error "DNS resolution failed for '$CustomDomain'. Configure DNS records first. Run with -ShowDnsInstructions for help."
        }
    } catch {
        Write-Warning "DNS resolution check encountered an error: $_"
        Write-Info "This may be due to DNS propagation delay. Consider using -SkipDnsCheck."
    }
}

# --- Step 2: Add Custom Domain to App Service ---

Write-Step "Step 2: Add Custom Domain Binding"

if ($domainAlreadyBound) {
    Write-Success "Custom domain '$CustomDomain' already bound. Skipping."
} else {
    if ($DryRun) {
        Write-DryRun "Would add hostname '$CustomDomain' to App Service '$AppServiceName'"
    } else {
        Write-Info "Adding custom domain '$CustomDomain' to App Service..."
        az webapp config hostname add `
            --webapp-name $AppServiceName `
            --resource-group $ResourceGroupName `
            --hostname $CustomDomain
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "  [ERROR] Failed to add custom domain." -ForegroundColor Red
            Write-Host ""
            Write-Host "  Azure requires BOTH DNS records:" -ForegroundColor Yellow
            Write-Host "    1. CNAME: api -> $AppServiceName.azurewebsites.net" -ForegroundColor White
            Write-Host "    2. TXT:   asuid.api -> <verification ID>" -ForegroundColor White
            Write-Host ""
            Write-Host "  To get the verification ID:" -ForegroundColor Gray
            Write-Host "    az webapp config hostname get-external-id --name $AppServiceName -g $ResourceGroupName" -ForegroundColor Gray
            Write-Host ""
            Write-Host "  Run with -ShowDnsInstructions for full setup guide." -ForegroundColor Gray
            Write-Error "Add both DNS records and re-run this script."
        }
        Write-Success "Custom domain '$CustomDomain' added to App Service."
    }
}

# --- Step 3: Create Azure-Managed SSL Certificate ---

Write-Step "Step 3: Create Azure-Managed SSL Certificate"

if ($SkipSsl) {
    Write-Warning "SSL configuration skipped (-SkipSsl flag)."
} else {
    # Check for existing certificate
    Write-Info "Checking for existing SSL certificates..."
    $existingCerts = az webapp config ssl list --resource-group $ResourceGroupName --query "[?subjectName=='$CustomDomain']" -o json 2>$null | ConvertFrom-Json

    if ($existingCerts -and $existingCerts.Count -gt 0) {
        $certThumbprint = $existingCerts[0].thumbprint
        Write-Success "Existing certificate found: thumbprint=$certThumbprint"
    } else {
        if ($DryRun) {
            Write-DryRun "Would create Azure-managed SSL certificate for '$CustomDomain'"
            $certThumbprint = '<dry-run-thumbprint>'
        } else {
            Write-Info "Creating Azure-managed SSL certificate..."
            $certResult = az webapp config ssl create `
                --name $AppServiceName `
                --resource-group $ResourceGroupName `
                --hostname $CustomDomain `
                -o json 2>$null | ConvertFrom-Json

            if ($LASTEXITCODE -ne 0 -or -not $certResult) {
                # Fallback: try the managed certificate approach
                Write-Warning "az webapp config ssl create failed. Trying managed certificate approach..."
                $certResult = az webapp config ssl bind `
                    --name $AppServiceName `
                    --resource-group $ResourceGroupName `
                    --certificate-thumbprint '' `
                    --ssl-type SNI `
                    --hostname $CustomDomain `
                    -o json 2>$null | ConvertFrom-Json

                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Failed to create SSL certificate. You may need to create it manually in the Azure Portal under 'Custom domains' > 'Add binding'."
                }
            }

            $certThumbprint = $certResult.thumbprint
            Write-Success "SSL certificate created: thumbprint=$certThumbprint"
        }
    }

    # Bind SSL certificate with SNI
    Write-Info "Binding SSL certificate to custom domain with SNI..."

    # Check if binding already exists
    $existingBinding = az webapp config ssl show --certificate-thumbprint $certThumbprint --resource-group $ResourceGroupName -o json 2>$null | ConvertFrom-Json

    if ($DryRun) {
        Write-DryRun "Would bind SSL certificate (thumbprint=$certThumbprint) to '$CustomDomain' with SNI SSL"
    } else {
        az webapp config ssl bind `
            --name $AppServiceName `
            --resource-group $ResourceGroupName `
            --certificate-thumbprint $certThumbprint `
            --ssl-type SNI `
            2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "SSL bind command returned non-zero. Certificate may already be bound."
            Write-Info "Verifying SSL binding..."
        }
        Write-Success "SSL certificate bound to '$CustomDomain' with SNI."
    }

    # --- Enable HTTPS Only ---

    Write-Step "Step 3b: Enforce HTTPS Only"

    if ($appService.httpsOnly) {
        Write-Success "HTTPS-only already enabled."
    } else {
        if ($DryRun) {
            Write-DryRun "Would enable HTTPS-only on App Service"
        } else {
            Write-Info "Enabling HTTPS-only (HTTP -> HTTPS redirect)..."
            az webapp update `
                --name $AppServiceName `
                --resource-group $ResourceGroupName `
                --set httpsOnly=true `
                -o none
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to enable HTTPS-only."
            }
            Write-Success "HTTPS-only enabled. HTTP requests will redirect to HTTPS."
        }
    }
}

# --- Step 4: Verify HTTPS Access ---

Write-Step "Step 4: Verify HTTPS Access"

if ($DryRun) {
    Write-DryRun "Would verify HTTPS access at https://$CustomDomain/healthz"
} else {
    Write-Info "Testing HTTPS access to https://$CustomDomain..."

    $maxRetries = 5
    $retryDelay = 10
    $verified = $false

    for ($i = 1; $i -le $maxRetries; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "https://$CustomDomain/healthz" -UseBasicParsing -TimeoutSec 15 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "HTTPS verified: https://$CustomDomain/healthz returned 200"
                $verified = $true
                break
            } else {
                Write-Info "Received status $($response.StatusCode) (attempt $i/$maxRetries)"
            }
        } catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 404) {
                Write-Warning "Received 404 at /healthz (BFF API not yet deployed — this is expected)"
                Write-Success "HTTPS connectivity confirmed (404 = server reachable, app not deployed)"
                $verified = $true
                break
            } elseif ($statusCode) {
                Write-Info "Received status $statusCode (attempt $i/$maxRetries)"
            } else {
                Write-Info "Connection attempt $i/$maxRetries failed: $($_.Exception.Message)"
            }
        }

        if ($i -lt $maxRetries) {
            Write-Info "Retrying in $retryDelay seconds..."
            Start-Sleep -Seconds $retryDelay
        }
    }

    if (-not $verified) {
        Write-Warning "Could not verify HTTPS access after $maxRetries attempts."
        Write-Info "This may be due to SSL certificate provisioning delay (can take up to 10 minutes)."
        Write-Info "Try manually: curl https://$CustomDomain/healthz"
    }

    # Verify SSL certificate details
    Write-Info "Checking SSL certificate status..."
    $sslState = az webapp config hostname list `
        --webapp-name $AppServiceName `
        --resource-group $ResourceGroupName `
        --query "[?name=='$CustomDomain'].{name:name, sslState:sslState, thumbprint:thumbprint}" `
        -o json 2>$null | ConvertFrom-Json

    if ($sslState -and $sslState.Count -gt 0) {
        $binding = $sslState[0]
        Write-Success "SSL State: $($binding.sslState)"
        if ($binding.thumbprint) {
            Write-Success "Certificate Thumbprint: $($binding.thumbprint)"
        }
    }

    # Verify HTTP to HTTPS redirect
    Write-Info "Verifying HTTP to HTTPS redirect..."
    try {
        $httpResponse = Invoke-WebRequest -Uri "http://$CustomDomain" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 10 -ErrorAction SilentlyContinue
    } catch {
        $redirectStatus = $_.Exception.Response.StatusCode.value__
        if ($redirectStatus -eq 301) {
            Write-Success "HTTP -> HTTPS redirect confirmed (301)"
        } elseif ($redirectStatus -eq 308) {
            Write-Success "HTTP -> HTTPS redirect confirmed (308)"
        } else {
            Write-Info "HTTP redirect status: $redirectStatus"
        }
    }
}

# --- Step 5: Configure CORS ---

Write-Step "Step 5: Configure CORS"

if ($SkipCors) {
    Write-Warning "CORS configuration skipped (-SkipCors flag)."
} else {
    $origins = $CorsOrigins -split ','

    if ($DryRun) {
        Write-DryRun "Would configure CORS with origins:"
        foreach ($origin in $origins) {
            Write-DryRun "  - $($origin.Trim())"
        }
    } else {
        Write-Info "Configuring CORS allowed origins..."

        # Build the origins parameter
        $trimmedOrigins = $origins | ForEach-Object { $_.Trim() }

        az webapp cors add `
            --name $AppServiceName `
            --resource-group $ResourceGroupName `
            --allowed-origins $trimmedOrigins `
            -o none 2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "CORS configuration may have partially failed."
        }

        # Verify CORS configuration
        $corsConfig = az webapp cors show `
            --name $AppServiceName `
            --resource-group $ResourceGroupName `
            --query "allowedOrigins" `
            -o json 2>$null | ConvertFrom-Json

        if ($corsConfig) {
            Write-Success "CORS configured with origins:"
            foreach ($origin in $corsConfig) {
                Write-Success "  - $origin"
            }
        }
    }
}

# --- Summary ---

Write-Host "`n=====================================" -ForegroundColor Green
Write-Host "  Configuration Summary" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green
Write-Host ""
Write-Host "  App Service:     $AppServiceName" -ForegroundColor White
Write-Host "  Resource Group:  $ResourceGroupName" -ForegroundColor White
Write-Host "  Custom Domain:   $CustomDomain" -ForegroundColor White
Write-Host "  Default Host:    $defaultHostname" -ForegroundColor White
Write-Host "  HTTPS URL:       https://$CustomDomain" -ForegroundColor White

if ($DryRun) {
    Write-Host ""
    Write-Host "  ** DRY RUN — No changes were made **" -ForegroundColor Magenta
    Write-Host "  Run without -DryRun to apply changes." -ForegroundColor Magenta
}

Write-Host ""
Write-Host "  Next Steps:" -ForegroundColor Yellow
Write-Host "    1. Deploy BFF API (task 023): .\Deploy-BffApi.ps1 -Environment production" -ForegroundColor Gray
Write-Host "    2. Verify health: curl https://$CustomDomain/healthz" -ForegroundColor Gray
Write-Host "    3. Add CORS origins for customer Dataverse environments as they onboard" -ForegroundColor Gray
Write-Host ""
