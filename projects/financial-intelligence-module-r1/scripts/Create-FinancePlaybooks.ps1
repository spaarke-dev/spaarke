<#
.SYNOPSIS
    Creates the two AI playbook records for Finance Intelligence Module R1.

.DESCRIPTION
    This script creates two playbook records in the sprk_analysisplaybook entity:
    1. Invoice Classification (gpt-4o-mini) - Classifies attachments as InvoiceCandidate/NotInvoice/Unknown
    2. Invoice Extraction (gpt-4o) - Extracts structured billing facts from confirmed invoices

    Prerequisites:
    - PAC CLI authenticated to Dataverse environment
    - Appropriate permissions to create records in sprk_analysisplaybook entity

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to 'https://spaarkedev1.crm.dynamics.com'.

.PARAMETER SkipExisting
    If set, skips creation if a playbook with the same playbookkey already exists.
    Default: $true (prevents duplicates).

.EXAMPLE
    .\Create-FinancePlaybooks.ps1

.EXAMPLE
    .\Create-FinancePlaybooks.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Finance Intelligence Module R1 - Task 090 (Step 4)
    Prompts loaded from: projects/financial-intelligence-module-r1/notes/prompts/
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [bool]$SkipExisting = $true
)

$ErrorActionPreference = "Stop"

# -------------------------------------------------------------------
# Banner
# -------------------------------------------------------------------
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Create Finance Intelligence Playbooks" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Environment: $EnvironmentUrl"
Write-Host "  Skip Existing: $SkipExisting"
Write-Host ""

# -------------------------------------------------------------------
# Step 1: Verify PAC CLI authentication
# -------------------------------------------------------------------
Write-Host "[1/6] Verifying PAC CLI authentication..." -ForegroundColor Yellow

try {
    $authList = pac auth list 2>&1 | Out-String
    if ($authList -notmatch $EnvironmentUrl.Replace("https://", "")) {
        Write-Host "       Not authenticated to target environment. Authenticating..." -ForegroundColor Yellow
        pac auth create --environment $EnvironmentUrl
    } else {
        Write-Host "       Already authenticated." -ForegroundColor Green
    }
} catch {
    Write-Error "Failed to authenticate to Dataverse. Ensure PAC CLI is installed and run 'pac auth create'."
    exit 1
}

Write-Host ""

# -------------------------------------------------------------------
# Step 2: Acquire bearer token
# -------------------------------------------------------------------
Write-Host "[2/6] Acquiring bearer token..." -ForegroundColor Yellow

try {
    # Use explicit tenant to avoid duplicate account issues
    $tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
    $tokenJson = az account get-access-token --resource "$EnvironmentUrl" --tenant $tenantId 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "       Failed with tenant-specific token. Trying without tenant..." -ForegroundColor Yellow
        $tokenJson = az account get-access-token --resource "$EnvironmentUrl" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to acquire bearer token. Run: az account clear && az login --tenant $tenantId"
            exit 1
        }
    }

    $tokenObj = $tokenJson | ConvertFrom-Json
    $bearerToken = $tokenObj.accessToken

    if (-not $bearerToken) {
        Write-Error "Bearer token is empty. Check Azure CLI authentication."
        exit 1
    }

    Write-Host "       Token acquired." -ForegroundColor Green
} catch {
    Write-Error "Failed to acquire token: $_"
    exit 1
}

Write-Host ""

# -------------------------------------------------------------------
# Step 3: Load prompt files
# -------------------------------------------------------------------
Write-Host "[3/6] Loading prompt templates..." -ForegroundColor Yellow

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Script is in: projects/financial-intelligence-module-r1/scripts/
# Project root is one level up
$projectRoot = Split-Path -Parent $scriptDir
$classificationPromptPath = Join-Path $projectRoot "notes\prompts\classification-prompt.md"
$extractionPromptPath = Join-Path $projectRoot "notes\prompts\extraction-prompt.md"

if (-not (Test-Path $classificationPromptPath)) {
    Write-Error "Classification prompt not found at: $classificationPromptPath"
    exit 1
}

if (-not (Test-Path $extractionPromptPath)) {
    Write-Error "Extraction prompt not found at: $extractionPromptPath"
    exit 1
}

# Read classification prompt (extract system prompt: lines 14-187, user prompt: lines 194-198)
$classificationLines = Get-Content $classificationPromptPath -Raw
$classificationSystemPrompt = ($classificationLines -split '## System Prompt')[1] -split '---' | Select-Object -First 1
$classificationSystemPrompt = $classificationSystemPrompt.Trim().TrimStart('```').TrimEnd('```').Trim()

$classificationUserPrompt = @"
Classify the following document and extract invoice hints if applicable.

Document text:
{{documentText}}
"@

# Read extraction prompt (extract system prompt: lines 14-287, user prompt: lines 294-301)
$extractionLines = Get-Content $extractionPromptPath -Raw
$extractionSystemPrompt = ($extractionLines -split '## System Prompt')[1] -split '---' | Select-Object -First 1
$extractionSystemPrompt = $extractionSystemPrompt.Trim().TrimStart('```').TrimEnd('```').Trim()

$extractionUserPrompt = @"
Extract billing facts from the following confirmed invoice document.

Reviewer-provided hints (use as ground truth when they conflict with document text):
{{reviewerHints}}

Document text:
{{documentText}}
"@

Write-Host "       Classification prompt loaded: $($classificationSystemPrompt.Length) chars" -ForegroundColor Green
Write-Host "       Extraction prompt loaded: $($extractionSystemPrompt.Length) chars" -ForegroundColor Green

Write-Host ""

# -------------------------------------------------------------------
# Step 4: Check for existing playbooks
# -------------------------------------------------------------------
Write-Host "[4/6] Checking for existing playbooks..." -ForegroundColor Yellow

$headers = @{
    "Authorization" = "Bearer $bearerToken"
    "Content-Type"  = "application/json"
    "Accept"        = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

# Check for existing playbooks
$fetchXml = @"
<fetch>
  <entity name='sprk_analysisplaybook'>
    <attribute name='sprk_analysisplaybookid' />
    <attribute name='sprk_name' />
    <attribute name='sprk_playbookkey' />
    <filter>
      <condition attribute='sprk_playbookkey' operator='in'>
        <value>invoice-classification</value>
        <value>invoice-extraction</value>
      </condition>
    </filter>
  </entity>
</fetch>
"@

$encodedFetchXml = [System.Web.HttpUtility]::UrlEncode($fetchXml)
$queryUrl = "$apiUrl/sprk_analysisplaybooks?fetchXml=$encodedFetchXml"

try {
    Add-Type -AssemblyName System.Web
    $existing = Invoke-RestMethod -Uri $queryUrl -Method Get -Headers $headers
    $existingKeys = $existing.value | ForEach-Object { $_.sprk_playbookkey }

    if ($existingKeys -contains "invoice-classification") {
        Write-Host "       'invoice-classification' playbook already exists." -ForegroundColor Yellow
        if ($SkipExisting) {
            Write-Host "       Skipping creation (SkipExisting=true)." -ForegroundColor Yellow
            $createClassification = $false
        } else {
            Write-Host "       Will attempt to update existing record." -ForegroundColor Yellow
            $createClassification = $true
        }
    } else {
        Write-Host "       'invoice-classification' playbook does not exist. Will create." -ForegroundColor Green
        $createClassification = $true
    }

    if ($existingKeys -contains "invoice-extraction") {
        Write-Host "       'invoice-extraction' playbook already exists." -ForegroundColor Yellow
        if ($SkipExisting) {
            Write-Host "       Skipping creation (SkipExisting=true)." -ForegroundColor Yellow
            $createExtraction = $false
        } else {
            Write-Host "       Will attempt to update existing record." -ForegroundColor Yellow
            $createExtraction = $true
        }
    } else {
        Write-Host "       'invoice-extraction' playbook does not exist. Will create." -ForegroundColor Green
        $createExtraction = $true
    }
} catch {
    Write-Warning "Failed to check existing playbooks: $_"
    Write-Host "       Proceeding with creation attempt..." -ForegroundColor Yellow
    $createClassification = $true
    $createExtraction = $true
}

Write-Host ""

# -------------------------------------------------------------------
# Step 5: Create playbook records
# -------------------------------------------------------------------
Write-Host "[5/6] Creating playbook records..." -ForegroundColor Yellow

$created = 0
$skipped = 0

# Playbook A: Invoice Classification
if ($createClassification) {
    Write-Host "       Creating 'Invoice Classification' playbook..." -ForegroundColor Yellow

    $classificationPlaybook = @{
        "sprk_name" = "Invoice Classification"
        "sprk_playbookkey" = "invoice-classification"
        "sprk_model" = "gpt-4o-mini"
        "sprk_systemprompt" = $classificationSystemPrompt
        "sprk_userprompttemplate" = $classificationUserPrompt
        "sprk_description" = "Classifies email attachments as InvoiceCandidate, NotInvoice, or Unknown with confidence scores and invoice hints."
        "sprk_isactive" = $true
        "sprk_temperature" = 0.0
        "sprk_maxtokens" = 2000
    }

    $classificationJson = $classificationPlaybook | ConvertTo-Json -Depth 10

    try {
        $response = Invoke-RestMethod -Uri "$apiUrl/sprk_analysisplaybooks" -Method Post -Headers $headers -Body $classificationJson
        Write-Host "       ✅ Created: Invoice Classification (ID: $($response.sprk_analysisplaybookid))" -ForegroundColor Green
        $created++
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.Value__
        if ($statusCode -eq 400) {
            Write-Warning "       Failed to create 'Invoice Classification': Validation error. Check field names and types."
        } else {
            Write-Warning "       Failed to create 'Invoice Classification': $_"
        }
    }
} else {
    Write-Host "       ⏭️ Skipped: Invoice Classification (already exists)" -ForegroundColor Yellow
    $skipped++
}

# Playbook B: Invoice Extraction
if ($createExtraction) {
    Write-Host "       Creating 'Invoice Extraction' playbook..." -ForegroundColor Yellow

    $extractionPlaybook = @{
        "sprk_name" = "Invoice Extraction"
        "sprk_playbookkey" = "invoice-extraction"
        "sprk_model" = "gpt-4o"
        "sprk_systemprompt" = $extractionSystemPrompt
        "sprk_userprompttemplate" = $extractionUserPrompt
        "sprk_description" = "Extracts structured billing facts from confirmed invoices: header fields, line items with cost types and role classes."
        "sprk_isactive" = $true
        "sprk_temperature" = 0.0
        "sprk_maxtokens" = 8000
    }

    $extractionJson = $extractionPlaybook | ConvertTo-Json -Depth 10

    try {
        $response = Invoke-RestMethod -Uri "$apiUrl/sprk_analysisplaybooks" -Method Post -Headers $headers -Body $extractionJson
        Write-Host "       ✅ Created: Invoice Extraction (ID: $($response.sprk_analysisplaybookid))" -ForegroundColor Green
        $created++
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.Value__
        if ($statusCode -eq 400) {
            Write-Warning "       Failed to create 'Invoice Extraction': Validation error. Check field names and types."
        } else {
            Write-Warning "       Failed to create 'Invoice Extraction': $_"
        }
    }
} else {
    Write-Host "       ⏭️ Skipped: Invoice Extraction (already exists)" -ForegroundColor Yellow
    $skipped++
}

Write-Host ""

# -------------------------------------------------------------------
# Step 6: Verify playbooks
# -------------------------------------------------------------------
Write-Host "[6/6] Verifying playbook records..." -ForegroundColor Yellow

try {
    $verified = Invoke-RestMethod -Uri $queryUrl -Method Get -Headers $headers
    $verifiedCount = ($verified.value | Measure-Object).Count

    Write-Host ""
    Write-Host "  --- Verification Results ---" -ForegroundColor Cyan
    Write-Host ""

    if ($verifiedCount -eq 0) {
        Write-Host "  ❌ No playbooks found. Creation may have failed." -ForegroundColor Red
    } else {
        $verified.value | ForEach-Object {
            Write-Host "  ✅ $($_.sprk_name)" -ForegroundColor Green
            Write-Host "     Key: $($_.sprk_playbookkey)"
            Write-Host "     ID: $($_.sprk_analysisplaybookid)"
            Write-Host ""
        }
    }

    if ($verifiedCount -eq 2) {
        Write-Host "  ✅ Both playbooks verified successfully!" -ForegroundColor Green
    } elseif ($verifiedCount -eq 1) {
        Write-Host "  ⚠️ Only 1 playbook found. Check creation logs above." -ForegroundColor Yellow
    }
} catch {
    Write-Warning "Failed to verify playbooks: $_"
}

Write-Host ""

# -------------------------------------------------------------------
# Summary
# -------------------------------------------------------------------
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Summary" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Created: $created playbook(s)"
Write-Host "  Skipped: $skipped playbook(s)"
Write-Host ""

if ($created -gt 0) {
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify playbooks in Dataverse UI: $EnvironmentUrl"
    Write-Host "  2. Continue to Step 5: Deploy BFF API to App Service"
    Write-Host ""
}

if ($created -eq 0 -and $skipped -eq 2) {
    Write-Host "✅ All playbooks already exist. No action taken." -ForegroundColor Green
    Write-Host ""
}
