<#
.SYNOPSIS
    Tests the admin workflow end-to-end for AI Chat Context Mappings.

.DESCRIPTION
    Verifies the complete admin lifecycle:
    1. Create a test mapping record in Dataverse
    2. Verify the record exists via Dataverse query
    3. Evict the BFF cache via DELETE /api/ai/chat/context-mappings/cache
    4. Verify BFF returns the mapping via GET /api/ai/chat/context-mappings
    5. Clean up the test record
    6. Report pass/fail for each step

    Uses Azure CLI for Dataverse auth and pac CLI for BFF API auth.
    Safe to run — always cleans up test data.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApiUrl
    BFF API base URL. Default: https://spe-api-dev-67e2xz.azurewebsites.net

.EXAMPLE
    .\scripts\Test-AdminWorkflow.ps1
    .\scripts\Test-AdminWorkflow.ps1 -EnvironmentUrl "https://myorg.crm.dynamics.com"

.NOTES
    Project: AI SprkChat Context Awareness
    Task: 043 - Admin workflow end-to-end test
#>
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$BffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Test Results Tracking
# ============================================================================

$testResults = [System.Collections.ArrayList]::new()

function Add-TestResult {
    param(
        [string]$Step,
        [string]$Description,
        [bool]$Passed,
        [string]$Detail = ""
    )
    $testResults.Add([PSCustomObject]@{
        Step        = $Step
        Description = $Description
        Passed      = $Passed
        Detail      = $Detail
    }) | Out-Null

    $icon = if ($Passed) { "PASS" } else { "FAIL" }
    $color = if ($Passed) { "Green" } else { "Red" }
    Write-Host "  [$icon] $Description" -ForegroundColor $color
    if ($Detail) {
        Write-Host "         $Detail" -ForegroundColor Gray
    }
}

# ============================================================================
# Helper Functions (same patterns as Create-AiChatContextMapEntity.ps1)
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get Dataverse token from Azure CLI. Run 'az login' first. Error: $tokenResult"
    }
    return $tokenResult.Trim()
}

function Get-BffApiToken {
    $tokenOutput = & pac auth token 2>&1
    $token = ($tokenOutput | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($token) -or $token.Contains("Error")) {
        throw "Failed to get BFF API token from pac CLI. Run 'pac auth create' first. Output: $tokenOutput"
    }
    return $token
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function Invoke-BffApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Path,
        [string]$Method = "GET"
    )

    $headers = @{
        "Authorization" = "Bearer $Token"
        "Accept"        = "application/json"
        "Content-Type"  = "application/json"
    }

    $uri = "$BaseUrl$Path"

    try {
        $response = Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers
        return $response
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorDetails = $_.ErrorDetails.Message
        }
        throw "BFF API Error ($Method $Path) [HTTP $statusCode]: $errorDetails"
    }
}

# ============================================================================
# Main Test Execution
# ============================================================================

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$testEntityType = "sprk_testentity_temp"
$testRecordName = "Admin Workflow Test - $timestamp"
$testRecordId = $null

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Admin Workflow End-to-End Test" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Dataverse:  $EnvironmentUrl" -ForegroundColor Yellow
Write-Host "BFF API:    $BffApiUrl" -ForegroundColor Yellow
Write-Host "Test Name:  $testRecordName" -ForegroundColor Yellow
Write-Host ""

try {

    # =========================================================================
    # Step 0: Authenticate
    # =========================================================================
    Write-Host "Step 0: Authentication" -ForegroundColor Cyan

    $dvToken = $null
    $bffToken = $null

    try {
        $dvToken = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
        Add-TestResult -Step "0a" -Description "Dataverse authentication (Azure CLI)" -Passed $true -Detail "Token length: $($dvToken.Length)"
    }
    catch {
        Add-TestResult -Step "0a" -Description "Dataverse authentication (Azure CLI)" -Passed $false -Detail $_.Exception.Message
        throw "Cannot continue without Dataverse auth."
    }

    try {
        $bffToken = Get-BffApiToken
        Add-TestResult -Step "0b" -Description "BFF API authentication (pac CLI)" -Passed $true -Detail "Token length: $($bffToken.Length)"
    }
    catch {
        Add-TestResult -Step "0b" -Description "BFF API authentication (pac CLI)" -Passed $false -Detail $_.Exception.Message
        throw "Cannot continue without BFF API auth."
    }

    # =========================================================================
    # Step 1: Resolve a playbook to use as lookup target
    # =========================================================================
    Write-Host ""
    Write-Host "Step 1: Resolve playbook for test mapping" -ForegroundColor Cyan

    $playbookId = $null
    $playbookName = $null

    try {
        $playbookQuery = Invoke-DataverseApi -Token $dvToken -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_analysisplaybooks?`$filter=statecode eq 0&`$select=sprk_analysisplaybookid,sprk_name&`$top=1&`$orderby=createdon desc"

        if ($playbookQuery.value.Count -eq 0) {
            throw "No active sprk_analysisplaybook records found. Cannot create test mapping without a playbook."
        }

        $playbookId = $playbookQuery.value[0].sprk_analysisplaybookid
        $playbookName = $playbookQuery.value[0].sprk_name
        Add-TestResult -Step "1" -Description "Resolve playbook lookup target" -Passed $true -Detail "Using '$playbookName' ($playbookId)"
    }
    catch {
        Add-TestResult -Step "1" -Description "Resolve playbook lookup target" -Passed $false -Detail $_.Exception.Message
        throw "Cannot continue without a playbook."
    }

    # =========================================================================
    # Step 2: Create test mapping record in Dataverse
    # =========================================================================
    Write-Host ""
    Write-Host "Step 2: Create test mapping record" -ForegroundColor Cyan

    try {
        $recordBody = @{
            "sprk_name"                  = $testRecordName
            "sprk_entitytype"            = $testEntityType
            "sprk_pagetype"              = 100000000  # entityrecord
            "sprk_sortorder"             = 100
            "sprk_isdefault"             = $true
            "sprk_isactive"              = $true
            "sprk_description"           = "Temporary record created by Test-AdminWorkflow.ps1 at $timestamp. Safe to delete."
            "sprk_playbookid@odata.bind" = "/sprk_analysisplaybooks($playbookId)"
        }

        # Use Invoke-RestMethod directly to capture the response headers (OData-EntityId)
        $createHeaders = @{
            "Authorization"    = "Bearer $dvToken"
            "OData-MaxVersion" = "4.0"
            "OData-Version"    = "4.0"
            "Accept"           = "application/json"
            "Content-Type"     = "application/json; charset=utf-8"
            "Prefer"           = "return=representation"
        }

        $createResponse = Invoke-RestMethod `
            -Uri "$EnvironmentUrl/api/data/v9.2/sprk_aichatcontextmaps" `
            -Method Post `
            -Headers $createHeaders `
            -Body ($recordBody | ConvertTo-Json -Depth 10)

        $testRecordId = $createResponse.sprk_aichatcontextmapid
        Add-TestResult -Step "2" -Description "Create test mapping in Dataverse" -Passed $true -Detail "Record ID: $testRecordId"
    }
    catch {
        Add-TestResult -Step "2" -Description "Create test mapping in Dataverse" -Passed $false -Detail $_.Exception.Message
        throw "Cannot continue without test record."
    }

    # =========================================================================
    # Step 3: Verify record exists via Dataverse query
    # =========================================================================
    Write-Host ""
    Write-Host "Step 3: Verify record via Dataverse query" -ForegroundColor Cyan

    try {
        $verifyQuery = Invoke-DataverseApi -Token $dvToken -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_aichatcontextmaps($testRecordId)?`$select=sprk_name,sprk_entitytype,sprk_pagetype,sprk_isdefault,sprk_isactive"

        $nameMatch = $verifyQuery.sprk_name -eq $testRecordName
        $entityTypeMatch = $verifyQuery.sprk_entitytype -eq $testEntityType
        $pageTypeMatch = $verifyQuery.sprk_pagetype -eq 100000000

        if ($nameMatch -and $entityTypeMatch -and $pageTypeMatch) {
            Add-TestResult -Step "3" -Description "Verify record exists in Dataverse" -Passed $true -Detail "Name, entityType, pageType all match"
        }
        else {
            Add-TestResult -Step "3" -Description "Verify record exists in Dataverse" -Passed $false `
                -Detail "Mismatch: name=$nameMatch, entityType=$entityTypeMatch, pageType=$pageTypeMatch"
        }
    }
    catch {
        Add-TestResult -Step "3" -Description "Verify record exists in Dataverse" -Passed $false -Detail $_.Exception.Message
    }

    # =========================================================================
    # Step 4: Evict BFF cache
    # =========================================================================
    Write-Host ""
    Write-Host "Step 4: Evict BFF context mapping cache" -ForegroundColor Cyan

    try {
        $evictResponse = Invoke-BffApi -Token $bffToken -BaseUrl $BffApiUrl `
            -Path "/api/ai/chat/context-mappings/cache" -Method "DELETE"

        Add-TestResult -Step "4" -Description "Evict BFF cache (DELETE /api/ai/chat/context-mappings/cache)" -Passed $true `
            -Detail "Cache eviction successful"
    }
    catch {
        # A 204 No Content may throw in some PS versions; treat non-error status as success
        if ($_.Exception.Message -match "204") {
            Add-TestResult -Step "4" -Description "Evict BFF cache (DELETE /api/ai/chat/context-mappings/cache)" -Passed $true `
                -Detail "Cache eviction returned 204 No Content"
        }
        else {
            Add-TestResult -Step "4" -Description "Evict BFF cache (DELETE /api/ai/chat/context-mappings/cache)" -Passed $false `
                -Detail $_.Exception.Message
        }
    }

    # =========================================================================
    # Step 5: Verify BFF returns the test mapping
    # =========================================================================
    Write-Host ""
    Write-Host "Step 5: Verify BFF returns test mapping" -ForegroundColor Cyan

    try {
        $bffResponse = Invoke-BffApi -Token $bffToken -BaseUrl $BffApiUrl `
            -Path "/api/ai/chat/context-mappings?entityType=$testEntityType&pageType=entityrecord"

        # Response should contain our test mapping
        $found = $false
        if ($bffResponse -is [System.Array]) {
            $found = ($bffResponse | Where-Object {
                $_.entityType -eq $testEntityType -or
                $_.name -like "*$testEntityType*"
            }).Count -gt 0
        }
        elseif ($bffResponse.mappings) {
            $found = ($bffResponse.mappings | Where-Object {
                $_.entityType -eq $testEntityType -or
                $_.name -like "*$testEntityType*"
            }).Count -gt 0
        }
        elseif ($bffResponse.entityType -eq $testEntityType) {
            $found = $true
        }
        else {
            # If the response is a single object or has a different shape, check for our entity type anywhere
            $responseJson = $bffResponse | ConvertTo-Json -Depth 5 -ErrorAction SilentlyContinue
            if ($responseJson -match $testEntityType) {
                $found = $true
            }
        }

        if ($found) {
            Add-TestResult -Step "5" -Description "BFF returns test mapping (GET /api/ai/chat/context-mappings)" -Passed $true `
                -Detail "Test mapping found in BFF response"
        }
        else {
            # Even if not found by field matching, the endpoint working at all is meaningful
            $responseJson = $bffResponse | ConvertTo-Json -Depth 3 -Compress -ErrorAction SilentlyContinue
            $truncated = if ($responseJson.Length -gt 200) { $responseJson.Substring(0, 200) + "..." } else { $responseJson }
            Add-TestResult -Step "5" -Description "BFF returns test mapping (GET /api/ai/chat/context-mappings)" -Passed $false `
                -Detail "Mapping not found in response. Response: $truncated"
        }
    }
    catch {
        Add-TestResult -Step "5" -Description "BFF returns test mapping (GET /api/ai/chat/context-mappings)" -Passed $false `
            -Detail $_.Exception.Message
    }

}
finally {

    # =========================================================================
    # Step 6: Cleanup — delete test record (ALWAYS runs)
    # =========================================================================
    Write-Host ""
    Write-Host "Step 6: Cleanup test data" -ForegroundColor Cyan

    if ($testRecordId) {
        try {
            Invoke-DataverseApi -Token $dvToken -BaseUrl $EnvironmentUrl `
                -Endpoint "sprk_aichatcontextmaps($testRecordId)" -Method "DELETE"

            Add-TestResult -Step "6" -Description "Delete test record from Dataverse" -Passed $true -Detail "Record $testRecordId deleted"
        }
        catch {
            Add-TestResult -Step "6" -Description "Delete test record from Dataverse" -Passed $false -Detail $_.Exception.Message
            Write-Host ""
            Write-Host "  WARNING: Test record may still exist. Manual cleanup needed:" -ForegroundColor Red
            Write-Host "  Record ID: $testRecordId" -ForegroundColor Yellow
            Write-Host "  Entity:    sprk_aichatcontextmaps" -ForegroundColor Yellow
        }
    }
    else {
        Add-TestResult -Step "6" -Description "Delete test record from Dataverse" -Passed $true -Detail "No record to clean up (creation failed or skipped)"
    }

    # Also clean up any orphaned test records from prior failed runs
    if ($dvToken) {
        try {
            $orphans = Invoke-DataverseApi -Token $dvToken -BaseUrl $EnvironmentUrl `
                -Endpoint "sprk_aichatcontextmaps?`$filter=sprk_entitytype eq '$testEntityType'&`$select=sprk_aichatcontextmapid,sprk_name"

            if ($orphans.value.Count -gt 0) {
                Write-Host ""
                Write-Host "  Found $($orphans.value.Count) orphaned test record(s), cleaning up..." -ForegroundColor Yellow
                foreach ($orphan in $orphans.value) {
                    try {
                        Invoke-DataverseApi -Token $dvToken -BaseUrl $EnvironmentUrl `
                            -Endpoint "sprk_aichatcontextmaps($($orphan.sprk_aichatcontextmapid))" -Method "DELETE"
                        Write-Host "    Deleted orphan: $($orphan.sprk_name)" -ForegroundColor Gray
                    }
                    catch {
                        Write-Host "    Failed to delete orphan $($orphan.sprk_aichatcontextmapid): $($_.Exception.Message)" -ForegroundColor Red
                    }
                }
            }
        }
        catch {
            # Non-fatal — orphan cleanup is best-effort
        }
    }

    # =========================================================================
    # Test Summary
    # =========================================================================
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host " Test Results Summary" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host ""

    $passed = ($testResults | Where-Object { $_.Passed }).Count
    $failed = ($testResults | Where-Object { -not $_.Passed }).Count
    $total = $testResults.Count

    foreach ($result in $testResults) {
        $icon = if ($result.Passed) { "PASS" } else { "FAIL" }
        $color = if ($result.Passed) { "Green" } else { "Red" }
        Write-Host "  [$icon] Step $($result.Step): $($result.Description)" -ForegroundColor $color
    }

    Write-Host ""

    if ($failed -eq 0) {
        Write-Host "  All $total tests passed!" -ForegroundColor Green
    }
    else {
        Write-Host "  $passed/$total passed, $failed failed" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host ""

    # Exit with non-zero code if any test failed
    if ($failed -gt 0) {
        exit 1
    }
}
