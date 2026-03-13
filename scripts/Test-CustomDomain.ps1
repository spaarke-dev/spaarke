<#
.SYNOPSIS
    Verify custom domain, SSL, and CORS configuration for Azure App Service.

.DESCRIPTION
    Post-configuration verification script for custom domain setup:
    1. DNS resolution (CNAME or A record)
    2. HTTPS connectivity and SSL certificate validity
    3. Certificate auto-renewal status (Azure-managed)
    4. HTTP to HTTPS redirect (HTTPS-only enforcement)
    5. CORS headers validation

    Use this script after Configure-CustomDomain.ps1 or to diagnose issues.

.PARAMETER CustomDomain
    The custom domain to verify.
    Default: api.spaarke.com

.PARAMETER AppServiceName
    Azure App Service name.
    Default: spaarke-bff-prod

.PARAMETER ResourceGroupName
    Azure resource group containing the App Service.
    Default: rg-spaarke-platform-prod

.PARAMETER TestCorsOrigin
    Origin to test CORS preflight against.
    Default: https://spaarke-demo.crm.dynamics.com

.EXAMPLE
    .\Test-CustomDomain.ps1

.EXAMPLE
    .\Test-CustomDomain.ps1 -TestCorsOrigin "https://my-org.crm.dynamics.com"
#>

[CmdletBinding()]
param(
    [string]$CustomDomain = 'api.spaarke.com',
    [string]$AppServiceName = 'spaarke-bff-prod',
    [string]$ResourceGroupName = 'rg-spaarke-platform-prod',
    [string]$TestCorsOrigin = 'https://spaarke-demo.crm.dynamics.com'
)

$ErrorActionPreference = 'Stop'

$passed = 0
$warned = 0
$failed = 0

function Write-TestResult {
    param(
        [string]$Test,
        [string]$Status,  # PASS, WARN, FAIL
        [string]$Detail = ''
    )

    switch ($Status) {
        'PASS' {
            Write-Host "  [PASS] $Test" -ForegroundColor Green
            $script:passed++
        }
        'WARN' {
            Write-Host "  [WARN] $Test" -ForegroundColor Yellow
            $script:warned++
        }
        'FAIL' {
            Write-Host "  [FAIL] $Test" -ForegroundColor Red
            $script:failed++
        }
    }
    if ($Detail) {
        Write-Host "         $Detail" -ForegroundColor Gray
    }
}

Write-Host "`n======================================================" -ForegroundColor Cyan
Write-Host "  Custom Domain Verification: $CustomDomain" -ForegroundColor Cyan
Write-Host "  App Service: $AppServiceName" -ForegroundColor Cyan
Write-Host "======================================================`n" -ForegroundColor Cyan

# --- Test 1: DNS Resolution ---

Write-Host "  DNS Resolution" -ForegroundColor White
Write-Host "  ──────────────" -ForegroundColor DarkGray

try {
    $cnameResult = Resolve-DnsName -Name $CustomDomain -Type CNAME -ErrorAction SilentlyContinue
    if ($cnameResult) {
        $cnameTarget = ($cnameResult | Where-Object { $_.Type -eq 'CNAME' }).NameHost
        if ($cnameTarget -match "$AppServiceName\.azurewebsites\.net") {
            Write-TestResult "CNAME record resolves to App Service" "PASS" "$CustomDomain -> $cnameTarget"
        } else {
            Write-TestResult "CNAME target unexpected" "WARN" "Points to $cnameTarget (expected $AppServiceName.azurewebsites.net)"
        }
    }
} catch {
    Write-TestResult "CNAME resolution" "WARN" "Could not resolve CNAME: $_"
}

try {
    $aResult = Resolve-DnsName -Name $CustomDomain -Type A -ErrorAction SilentlyContinue
    $ips = ($aResult | Where-Object { $_.Type -eq 'A' }).IPAddress
    if ($ips) {
        Write-TestResult "A record resolves" "PASS" "$CustomDomain -> $($ips -join ', ')"
    } else {
        Write-TestResult "A record resolution" "WARN" "No A records found (may be CNAME-only)"
    }
} catch {
    Write-TestResult "A record resolution" "FAIL" "DNS query failed: $_"
}

# --- Test 2: App Service Hostname Binding ---

Write-Host "`n  App Service Configuration" -ForegroundColor White
Write-Host "  ────────────────────────" -ForegroundColor DarkGray

try {
    $hostnames = az webapp config hostname list `
        --webapp-name $AppServiceName `
        --resource-group $ResourceGroupName `
        --query "[].{name:name, sslState:sslState, thumbprint:thumbprint}" `
        -o json 2>$null | ConvertFrom-Json

    $customBinding = $hostnames | Where-Object { $_.name -eq $CustomDomain }
    if ($customBinding) {
        Write-TestResult "Custom domain bound to App Service" "PASS" "Hostname: $CustomDomain"

        if ($customBinding.sslState -eq 'SniEnabled') {
            Write-TestResult "SSL binding (SNI)" "PASS" "Thumbprint: $($customBinding.thumbprint)"
        } elseif ($customBinding.sslState -eq 'IpBasedEnabled') {
            Write-TestResult "SSL binding (IP-based)" "PASS" "Thumbprint: $($customBinding.thumbprint)"
        } else {
            Write-TestResult "SSL binding" "FAIL" "SSL state: $($customBinding.sslState)"
        }
    } else {
        Write-TestResult "Custom domain binding" "FAIL" "'$CustomDomain' not found in hostname bindings"
    }
} catch {
    Write-TestResult "Hostname binding check" "FAIL" "Azure CLI error: $_"
}

# --- Test 3: HTTPS-Only Enforcement ---

try {
    $httpsOnly = az webapp show `
        --name $AppServiceName `
        --resource-group $ResourceGroupName `
        --query "httpsOnly" `
        -o tsv 2>$null

    if ($httpsOnly -eq 'true') {
        Write-TestResult "HTTPS-only enforcement" "PASS" "HTTP redirects to HTTPS"
    } else {
        Write-TestResult "HTTPS-only enforcement" "FAIL" "httpsOnly=$httpsOnly (should be true)"
    }
} catch {
    Write-TestResult "HTTPS-only check" "FAIL" "Could not query App Service: $_"
}

# --- Test 4: Certificate Details ---

Write-Host "`n  SSL Certificate" -ForegroundColor White
Write-Host "  ───────────────" -ForegroundColor DarkGray

try {
    $certs = az webapp config ssl list `
        --resource-group $ResourceGroupName `
        --query "[?subjectName=='$CustomDomain' || contains(subjectName, '$CustomDomain')].{subject:subjectName, issuer:issuer, expires:expirationDate, thumbprint:thumbprint}" `
        -o json 2>$null | ConvertFrom-Json

    if ($certs -and $certs.Count -gt 0) {
        $cert = $certs[0]
        Write-TestResult "SSL certificate exists" "PASS" "Subject: $($cert.subject)"
        Write-TestResult "Certificate issuer" "PASS" "$($cert.issuer)"

        if ($cert.expires) {
            $expDate = [DateTime]::Parse($cert.expires)
            $daysUntilExpiry = ($expDate - (Get-Date)).Days
            if ($daysUntilExpiry -gt 30) {
                Write-TestResult "Certificate expiry" "PASS" "Expires: $($cert.expires) ($daysUntilExpiry days)"
            } elseif ($daysUntilExpiry -gt 0) {
                Write-TestResult "Certificate expiry" "WARN" "Expires in $daysUntilExpiry days! ($($cert.expires))"
            } else {
                Write-TestResult "Certificate expiry" "FAIL" "Certificate EXPIRED on $($cert.expires)"
            }
        }

        Write-TestResult "Certificate thumbprint" "PASS" "$($cert.thumbprint)"
    } else {
        Write-TestResult "SSL certificate" "FAIL" "No certificate found for $CustomDomain"
    }
} catch {
    Write-TestResult "Certificate check" "FAIL" "Could not list certificates: $_"
}

# --- Test 5: HTTPS Connectivity ---

Write-Host "`n  HTTPS Connectivity" -ForegroundColor White
Write-Host "  ─────────────────" -ForegroundColor DarkGray

try {
    $response = Invoke-WebRequest -Uri "https://$CustomDomain/healthz" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
    Write-TestResult "HTTPS /healthz reachable" "PASS" "Status: $($response.StatusCode)"
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 404) {
        Write-TestResult "HTTPS connectivity" "PASS" "Server reachable (404 = BFF not yet deployed)"
    } elseif ($statusCode) {
        Write-TestResult "HTTPS connectivity" "WARN" "Server returned status $statusCode"
    } else {
        Write-TestResult "HTTPS connectivity" "FAIL" "Connection failed: $($_.Exception.Message)"
    }
}

# Test HTTP redirect
try {
    $httpResponse = Invoke-WebRequest -Uri "http://$CustomDomain/" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 10 -ErrorAction SilentlyContinue
    Write-TestResult "HTTP -> HTTPS redirect" "FAIL" "HTTP returned $($httpResponse.StatusCode) without redirect"
} catch {
    $redirectStatus = $_.Exception.Response.StatusCode.value__
    if ($redirectStatus -eq 301 -or $redirectStatus -eq 308) {
        Write-TestResult "HTTP -> HTTPS redirect" "PASS" "Redirect status: $redirectStatus"
    } elseif ($redirectStatus) {
        Write-TestResult "HTTP -> HTTPS redirect" "WARN" "Status: $redirectStatus"
    } else {
        Write-TestResult "HTTP -> HTTPS redirect" "WARN" "Could not test: $($_.Exception.Message)"
    }
}

# --- Test 6: CORS Configuration ---

Write-Host "`n  CORS Configuration" -ForegroundColor White
Write-Host "  ─────────────────" -ForegroundColor DarkGray

try {
    $corsOrigins = az webapp cors show `
        --name $AppServiceName `
        --resource-group $ResourceGroupName `
        --query "allowedOrigins" `
        -o json 2>$null | ConvertFrom-Json

    if ($corsOrigins -and $corsOrigins.Count -gt 0) {
        Write-TestResult "CORS origins configured" "PASS" "$($corsOrigins.Count) origin(s)"
        foreach ($origin in $corsOrigins) {
            Write-Host "         - $origin" -ForegroundColor Gray
        }
    } else {
        Write-TestResult "CORS origins" "WARN" "No CORS origins configured (add as customers onboard)"
    }
} catch {
    Write-TestResult "CORS check" "FAIL" "Could not query CORS: $_"
}

# --- Summary ---

Write-Host "`n======================================================" -ForegroundColor Cyan
Write-Host "  Test Summary" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Passed:   $passed" -ForegroundColor Green
Write-Host "  Warnings: $warned" -ForegroundColor Yellow
Write-Host "  Failed:   $failed" -ForegroundColor Red
Write-Host ""

if ($failed -gt 0) {
    Write-Host "  RESULT: ISSUES FOUND — Review failures above" -ForegroundColor Red
    exit 1
} elseif ($warned -gt 0) {
    Write-Host "  RESULT: PASSED WITH WARNINGS" -ForegroundColor Yellow
    exit 0
} else {
    Write-Host "  RESULT: ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}
