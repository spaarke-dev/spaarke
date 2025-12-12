<#
.SYNOPSIS
Diagnostic script to troubleshoot AI Summary Service issues in Spaarke

.DESCRIPTION
Checks configuration, service registration, Dataverse fields, and API endpoint availability
Outputs a detailed report of potential issues

.EXAMPLE
.\Diagnose-AiSummaryService.ps1
#>

param(
    [string]$Environment = "Development",
    [switch]$Verbose
)

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "   Spaarke AI Summary Service Diagnostics" -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

$ErrorCount = 0
$WarningCount = 0
$rootPath = Split-Path $PSScriptRoot -Parent

# ==============================================================================
# 1. CHECK CONFIGURATION FILES
# ==============================================================================
Write-Host "1. Checking Configuration Files..." -ForegroundColor Yellow
Write-Host ""

$appsettingsPath = Join-Path $rootPath "src\server\api\Sprk.Bff.Api\appsettings.json"
$appsettingsDevPath = Join-Path $rootPath "src\server\api\Sprk.Bff.Api\appsettings.Development.json"

if (-not (Test-Path $appsettingsPath)) {
    Write-Host "   ❌ ERROR: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
    $ErrorCount++
} else {
    Write-Host "   ✓ Found appsettings.json" -ForegroundColor Green

    $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json

    if ($appsettings.DocumentIntelligence) {
        $config = $appsettings.DocumentIntelligence
        Write-Host "   ✓ DocumentIntelligence section exists" -ForegroundColor Green
        Write-Host "      - Enabled: $($config.Enabled)" -ForegroundColor Gray
        Write-Host "      - Model: $($config.SummarizeModel)" -ForegroundColor Gray
        Write-Host "      - Structured Output: $($config.StructuredOutputEnabled)" -ForegroundColor Gray

        if (-not $config.OpenAiEndpoint -or $config.OpenAiEndpoint -like "*use-user-secrets*") {
            Write-Host "   ⚠ WARNING: OpenAiEndpoint not configured (uses Key Vault or user secrets)" -ForegroundColor Yellow
            $WarningCount++
        }

        if (-not $config.SupportedFileTypes) {
            Write-Host "   ❌ ERROR: SupportedFileTypes not configured" -ForegroundColor Red
            $ErrorCount++
        } else {
            Write-Host "   ✓ SupportedFileTypes configured ($($config.SupportedFileTypes.PSObject.Properties.Count) types)" -ForegroundColor Green
        }
    } else {
        Write-Host "   ❌ ERROR: DocumentIntelligence section missing from appsettings.json" -ForegroundColor Red
        $ErrorCount++
    }
}

Write-Host ""

if (Test-Path $appsettingsDevPath) {
    Write-Host "   ✓ Found appsettings.Development.json" -ForegroundColor Green

    $appsettingsDev = Get-Content $appsettingsDevPath | ConvertFrom-Json

    if ($appsettingsDev.DocumentIntelligence) {
        $configDev = $appsettingsDev.DocumentIntelligence

        if (-not $configDev.DocIntelEndpoint) {
            Write-Host "   ⚠ WARNING: DocIntelEndpoint not in Development config (PDF/DOCX analysis won't work)" -ForegroundColor Yellow
            $WarningCount++
        }

        if (-not $configDev.SupportedFileTypes) {
            Write-Host "   ⚠ WARNING: SupportedFileTypes not in Development config" -ForegroundColor Yellow
            $WarningCount++
        }
    }
}

Write-Host ""

# ==============================================================================
# 2. CHECK LOCAL SECRETS
# ==============================================================================
Write-Host "2. Checking Local Configuration..." -ForegroundColor Yellow
Write-Host ""

$localConfigPath = Join-Path $rootPath "config\ai-config.local.json"

if (Test-Path $localConfigPath) {
    Write-Host "   ✓ Found config\ai-config.local.json" -ForegroundColor Green

    $localConfig = Get-Content $localConfigPath | ConvertFrom-Json

    if ($localConfig.ai.azure_openai.endpoint) {
        Write-Host "   ✓ Azure OpenAI endpoint configured: $($localConfig.ai.azure_openai.endpoint)" -ForegroundColor Green
    } else {
        Write-Host "   ❌ ERROR: Azure OpenAI endpoint not configured" -ForegroundColor Red
        $ErrorCount++
    }

    if ($localConfig.ai.azure_openai.apiKey) {
        $keyPreview = $localConfig.ai.azure_openai.apiKey.Substring(0, [Math]::Min(8, $localConfig.ai.azure_openai.apiKey.Length)) + "..."
        Write-Host "   ✓ Azure OpenAI API key configured: $keyPreview" -ForegroundColor Green
    } else {
        Write-Host "   ❌ ERROR: Azure OpenAI API key not configured" -ForegroundColor Red
        $ErrorCount++
    }

    if ($localConfig.ai.models."gpt-4o-mini") {
        Write-Host "   ✓ Model 'gpt-4o-mini' configured" -ForegroundColor Green
    } else {
        Write-Host "   ⚠ WARNING: Model 'gpt-4o-mini' not found in config" -ForegroundColor Yellow
        $WarningCount++
    }
} else {
    Write-Host "   ⚠ WARNING: config\ai-config.local.json not found (may use user secrets)" -ForegroundColor Yellow
    $WarningCount++
}

Write-Host ""

# ==============================================================================
# 3. CHECK DATAVERSE MODELS
# ==============================================================================
Write-Host "3. Checking Dataverse Models..." -ForegroundColor Yellow
Write-Host ""

$modelsPath = Join-Path $rootPath "src\server\shared\Spaarke.Dataverse\Models.cs"

if (Test-Path $modelsPath) {
    $modelsContent = Get-Content $modelsPath -Raw

    $requiredFields = @(
        "Summary",
        "TlDr",
        "Keywords",
        "SummaryStatus",
        "ExtractOrganization",
        "ExtractPeople",
        "ExtractFees",
        "ExtractDates",
        "ExtractReference",
        "ExtractDocumentType",
        "DocumentType"
    )

    Write-Host "   Checking UpdateDocumentRequest fields..." -ForegroundColor Gray

    foreach ($field in $requiredFields) {
        if ($modelsContent -match "public\s+\w+\?\s+$field\s+\{\s*get;\s*set;\s*\}") {
            Write-Host "   ✓ Field '$field' exists" -ForegroundColor Green
        } else {
            Write-Host "   ❌ ERROR: Field '$field' missing or malformed" -ForegroundColor Red
            $ErrorCount++
        }
    }
} else {
    Write-Host "   ❌ ERROR: Models.cs not found at $modelsPath" -ForegroundColor Red
    $ErrorCount++
}

Write-Host ""

# ==============================================================================
# 4. CHECK DATAVERSE FIELD MAPPINGS
# ==============================================================================
Write-Host "4. Checking Dataverse Field Mappings..." -ForegroundColor Yellow
Write-Host ""

$webApiServicePath = Join-Path $rootPath "src\server\shared\Spaarke.Dataverse\DataverseWebApiService.cs"

if (Test-Path $webApiServicePath) {
    $webApiContent = Get-Content $webApiServicePath -Raw

    $fieldMappings = @{
        "Summary" = "sprk_filesummary"
        "TlDr" = "sprk_filetldr"
        "Keywords" = "sprk_filekeywords"
        "SummaryStatus" = "sprk_filesummarystatus"
        "ExtractOrganization" = "sprk_extractorganization"
        "ExtractPeople" = "sprk_extractpeople"
        "ExtractFees" = "sprk_extractfees"
        "ExtractDates" = "sprk_extractdates"
        "ExtractReference" = "sprk_extractreference"
        "ExtractDocumentType" = "sprk_extractdocumenttype"
    }

    Write-Host "   Checking field mappings in DataverseWebApiService..." -ForegroundColor Gray

    foreach ($mapping in $fieldMappings.GetEnumerator()) {
        if ($webApiContent -match """$($mapping.Value)""") {
            Write-Host "   ✓ $($mapping.Key) → $($mapping.Value)" -ForegroundColor Green
        } else {
            Write-Host "   ❌ ERROR: Missing mapping for $($mapping.Key) → $($mapping.Value)" -ForegroundColor Red
            $ErrorCount++
        }
    }
} else {
    Write-Host "   ❌ ERROR: DataverseWebApiService.cs not found" -ForegroundColor Red
    $ErrorCount++
}

Write-Host ""

# ==============================================================================
# 5. CHECK SERVICE REGISTRATION
# ==============================================================================
Write-Host "5. Checking Service Registration..." -ForegroundColor Yellow
Write-Host ""

$programPath = Join-Path $rootPath "src\server\api\Sprk.Bff.Api\Program.cs"

if (Test-Path $programPath) {
    $programContent = Get-Content $programPath -Raw

    if ($programContent -match "AddSingleton.*OpenAiClient") {
        Write-Host "   ✓ OpenAiClient registered" -ForegroundColor Green
    } else {
        Write-Host "   ❌ ERROR: OpenAiClient not registered" -ForegroundColor Red
        $ErrorCount++
    }

    if ($programContent -match "AddScoped.*DocumentIntelligenceService") {
        Write-Host "   ✓ DocumentIntelligenceService registered" -ForegroundColor Green
    } else {
        Write-Host "   ❌ ERROR: DocumentIntelligenceService not registered" -ForegroundColor Red
        $ErrorCount++
    }

    if ($programContent -match "MapDocumentIntelligenceEndpoints") {
        Write-Host "   ✓ DocumentIntelligenceEndpoints mapped" -ForegroundColor Green
    } else {
        Write-Host "   ❌ ERROR: DocumentIntelligenceEndpoints not mapped" -ForegroundColor Red
        $ErrorCount++
    }

    if ($programContent -match "DocumentAnalysisJobHandler") {
        Write-Host "   ✓ DocumentAnalysisJobHandler registered" -ForegroundColor Green
    } else {
        Write-Host "   ⚠ WARNING: DocumentAnalysisJobHandler not registered (background jobs won't work)" -ForegroundColor Yellow
        $WarningCount++
    }
} else {
    Write-Host "   ❌ ERROR: Program.cs not found" -ForegroundColor Red
    $ErrorCount++
}

Write-Host ""

# ==============================================================================
# 6. SUMMARY
# ==============================================================================
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "   Diagnostic Summary" -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

if ($ErrorCount -eq 0 -and $WarningCount -eq 0) {
    Write-Host "   ✅ ALL CHECKS PASSED!" -ForegroundColor Green
    Write-Host "   No configuration issues detected." -ForegroundColor Green
} else {
    Write-Host "   Errors:   $ErrorCount" -ForegroundColor $(if ($ErrorCount -gt 0) { "Red" } else { "Green" })
    Write-Host "   Warnings: $WarningCount" -ForegroundColor $(if ($WarningCount -gt 0) { "Yellow" } else { "Green" })
    Write-Host ""

    if ($ErrorCount -gt 0) {
        Write-Host "   ❌ Critical issues found. AI Summary Service may not work." -ForegroundColor Red
        Write-Host "   Please review errors above and fix configuration." -ForegroundColor Red
    } else {
        Write-Host "   ⚠ Minor issues found. AI Summary Service should work but may have limitations." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

# Return appropriate exit code
if ($ErrorCount -gt 0) {
    exit 1
} else {
    exit 0
}
