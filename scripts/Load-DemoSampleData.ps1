#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Load non-confidential sample data into a Spaarke demo environment.

.DESCRIPTION
    Populates a demo Dataverse environment with realistic, non-confidential test
    data that exercises all major Spaarke features:
      1. Contacts (standard Dataverse entity)
      2. Matters (sprk_matter)
      3. Projects (sprk_project)
      4. Events (sprk_event)
      5. Documents (sprk_document) — metadata only (SPE upload separate)
      6. Chart Definitions (sprk_chartdefinition)
      7. AI Seed Data (actions, tools, skills, knowledge, playbooks, output types)

    Optionally uploads sample text documents to SPE containers and triggers
    AI Search indexing.

    The script is idempotent — existing records are skipped (matched by name).
    Use -Force to recreate existing records.

    PREREQUISITE: Spaarke managed solutions must be imported into the target
    Dataverse environment before running this script. The script validates
    entity availability and exits with clear guidance if solutions are missing.

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL.
    Default: https://spaarke-demo.crm.dynamics.com

.PARAMETER DataFile
    Path to the JSON file containing demo record definitions.
    Default: scripts/demo-data/demo-records.json

.PARAMETER SampleDocsPath
    Path to directory containing sample document files for SPE upload.
    Default: scripts/demo-data/sample-documents/

.PARAMETER SkipContacts
    Skip contact record creation.

.PARAMETER SkipDocuments
    Skip document metadata record creation.

.PARAMETER SkipAiSeedData
    Skip AI seed data deployment (actions, tools, skills, knowledge, playbooks).

.PARAMETER SkipSpeUpload
    Skip uploading sample documents to SPE containers (requires Graph API auth).

.PARAMETER SkipAiIndexing
    Skip triggering AI Search indexing of records.

.PARAMETER Force
    Recreate existing records (delete + re-create).

.PARAMETER DryRun
    Preview what would be created without making changes.

.EXAMPLE
    .\Load-DemoSampleData.ps1 -DryRun
    # Preview all records that would be created

.EXAMPLE
    .\Load-DemoSampleData.ps1
    # Load all sample data into demo environment

.EXAMPLE
    .\Load-DemoSampleData.ps1 -SkipAiSeedData -SkipSpeUpload
    # Load only Dataverse records, skip AI seed data and SPE uploads

.EXAMPLE
    .\Load-DemoSampleData.ps1 -EnvironmentUrl "https://myenv.crm.dynamics.com"
    # Load into a different environment
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$EnvironmentUrl = "https://spaarke-demo.crm.dynamics.com",

    [string]$DataFile,

    [string]$SampleDocsPath,

    [switch]$SkipContacts,
    [switch]$SkipDocuments,
    [switch]$SkipAiSeedData,
    [switch]$SkipSpeUpload,
    [switch]$SkipAiIndexing,

    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ============================================================================
# CONFIGURATION
# ============================================================================

$ScriptRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path "$ScriptRoot\..").Path

if (-not $DataFile) {
    $DataFile = Join-Path $ScriptRoot "demo-data" "demo-records.json"
}
if (-not $SampleDocsPath) {
    $SampleDocsPath = Join-Path $ScriptRoot "demo-data" "sample-documents"
}

$SeedDataDir = Join-Path $ScriptRoot "seed-data"
$AiSearchDir = Join-Path $ScriptRoot "ai-search"

# ============================================================================
# BANNER
# ============================================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Spaarke Demo Sample Data Loader" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
Write-Host "Data File:   $DataFile"
if ($DryRun) {
    Write-Host "Mode:        DRY RUN (no changes)" -ForegroundColor Yellow
} else {
    Write-Host "Mode:        LIVE DEPLOYMENT" -ForegroundColor Green
}
if ($Force) {
    Write-Host "Force:       ENABLED (recreate existing)" -ForegroundColor Yellow
}
Write-Host ""

# ============================================================================
# PREREQUISITES
# ============================================================================

Write-Host "Checking prerequisites..." -ForegroundColor Gray

# 1. Check data file exists
if (-not (Test-Path $DataFile)) {
    Write-Error "Data file not found: $DataFile"
    exit 1
}
Write-Host "  [ok] Data file found" -ForegroundColor Green

# 2. Check Azure CLI authenticated
try {
    $account = az account show 2>&1 | ConvertFrom-Json
    Write-Host "  [ok] Azure CLI: $($account.user.name)" -ForegroundColor Green
} catch {
    Write-Error "Azure CLI not authenticated. Run 'az login' first."
    exit 1
}

# 3. Get Dataverse access token
Write-Host "  Getting Dataverse access token..." -ForegroundColor Gray
$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv 2>$null
if (-not $token) {
    Write-Error "Failed to get access token for $EnvironmentUrl. Ensure you have Dataverse access."
    exit 1
}
Write-Host "  [ok] Dataverse token acquired" -ForegroundColor Green

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept'        = 'application/json'
    'Content-Type'  = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
    'Prefer'        = 'return=representation'
}

# 4. Check if Spaarke solutions are imported (validate sprk_matters entity exists)
Write-Host "  Checking Spaarke solutions..." -ForegroundColor Gray
$solutionsImported = $true
try {
    $testUri = "$EnvironmentUrl/api/data/v9.2/sprk_matters?`$top=1&`$select=sprk_matterid"
    $null = Invoke-RestMethod -Uri $testUri -Headers $headers -Method Get
    Write-Host "  [ok] Spaarke entities available (solutions imported)" -ForegroundColor Green
} catch {
    $solutionsImported = $false
    Write-Host "  [!!] Spaarke entities NOT available" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Spaarke managed solutions have not been imported into this environment." -ForegroundColor Yellow
    Write-Host "  Custom entities (sprk_matter, sprk_project, etc.) do not exist yet." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  To fix:" -ForegroundColor Cyan
    Write-Host "    1. Build managed solution ZIPs from the main repo" -ForegroundColor Gray
    Write-Host "    2. Run: .\Deploy-DataverseSolutions.ps1 -EnvironmentUrl $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "    3. Re-run this script after solutions are imported" -ForegroundColor Gray
    Write-Host ""

    if (-not $DryRun) {
        Write-Host "  Proceeding with contacts only (standard Dataverse entity)." -ForegroundColor Yellow
        Write-Host "  Spaarke-specific records will be skipped." -ForegroundColor Yellow
        Write-Host ""
    }
}

# ============================================================================
# LOAD DATA FILE
# ============================================================================

$data = Get-Content $DataFile -Raw | ConvertFrom-Json

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

$script:summary = @{
    Created = 0
    Skipped = 0
    Errors  = 0
    DryRun  = 0
}

function Test-RecordExists {
    param(
        [string]$EntitySetName,
        [string]$NameField,
        [string]$NameValue
    )

    $filter = "`$filter=$NameField eq '$($NameValue -replace "'","''")'"
    $uri = "$EnvironmentUrl/api/data/v9.2/${EntitySetName}?${filter}&`$top=1&`$select=$NameField"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        return $result.value.Count -gt 0
    } catch {
        return $false
    }
}

function New-DataverseRecord {
    param(
        [string]$EntitySetName,
        [string]$NameField,
        [hashtable]$Record,
        [string]$DisplayLabel
    )

    $name = $Record[$NameField]
    if (-not $DisplayLabel) { $DisplayLabel = $name }

    # Check existing
    if (-not $Force -and (Test-RecordExists -EntitySetName $EntitySetName -NameField $NameField -NameValue $name)) {
        Write-Host "    SKIP: '$DisplayLabel' (exists)" -ForegroundColor Yellow
        $script:summary.Skipped++
        return
    }

    if ($DryRun) {
        Write-Host "    WOULD CREATE: '$DisplayLabel'" -ForegroundColor Gray
        $script:summary.DryRun++
        return
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/$EntitySetName"
    $body = $Record | ConvertTo-Json -Depth 5

    try {
        $null = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body
        Write-Host "    CREATED: '$DisplayLabel'" -ForegroundColor Green
        $script:summary.Created++
    } catch {
        Write-Host "    ERROR: '$DisplayLabel' - $($_.Exception.Message)" -ForegroundColor Red
        $script:summary.Errors++
    }
}

# ============================================================================
# STEP 1: CREATE CONTACTS (standard Dataverse entity - always available)
# ============================================================================

if (-not $SkipContacts) {
    Write-Host ""
    Write-Host "--- Step 1: Contacts ---" -ForegroundColor Cyan
    Write-Host "  Creating $($data.contacts.Count) contact records..." -ForegroundColor Gray

    foreach ($contact in $data.contacts) {
        $record = @{
            "firstname"     = $contact.firstname
            "lastname"      = $contact.lastname
            "emailaddress1" = $contact.emailaddress1
            "telephone1"    = $contact.telephone1
            "jobtitle"      = $contact.jobtitle
            "company"       = $contact.company
        }

        New-DataverseRecord `
            -EntitySetName "contacts" `
            -NameField "emailaddress1" `
            -Record $record `
            -DisplayLabel "$($contact.firstname) $($contact.lastname)"
    }
} else {
    Write-Host ""
    Write-Host "--- Step 1: Contacts --- SKIPPED" -ForegroundColor Yellow
}

# ============================================================================
# STEP 2: CREATE MATTERS (requires Spaarke solutions)
# ============================================================================

if ($solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 2: Matters ---" -ForegroundColor Cyan
    Write-Host "  Creating $($data.matters.Count) matter records..." -ForegroundColor Gray

    foreach ($matter in $data.matters) {
        $record = @{
            "sprk_mattername"        = $matter.sprk_mattername
            "sprk_matternumber"      = $matter.sprk_matternumber
            "sprk_matterdescription" = $matter.sprk_matterdescription
        }

        New-DataverseRecord `
            -EntitySetName "sprk_matters" `
            -NameField "sprk_mattername" `
            -Record $record
    }
} else {
    Write-Host ""
    Write-Host "--- Step 2: Matters --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
}

# ============================================================================
# STEP 3: CREATE PROJECTS (requires Spaarke solutions)
# ============================================================================

if ($solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 3: Projects ---" -ForegroundColor Cyan
    Write-Host "  Creating $($data.projects.Count) project records..." -ForegroundColor Gray

    foreach ($project in $data.projects) {
        $record = @{
            "sprk_projectname"        = $project.sprk_projectname
            "sprk_projectnumber"      = $project.sprk_projectnumber
            "sprk_projectdescription" = $project.sprk_projectdescription
        }

        New-DataverseRecord `
            -EntitySetName "sprk_projects" `
            -NameField "sprk_projectname" `
            -Record $record
    }
} else {
    Write-Host ""
    Write-Host "--- Step 3: Projects --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
}

# ============================================================================
# STEP 4: CREATE EVENTS (requires Spaarke solutions)
# ============================================================================

if ($solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 4: Events ---" -ForegroundColor Cyan
    Write-Host "  Creating $($data.events.Count) event records..." -ForegroundColor Gray

    foreach ($event in $data.events) {
        $record = @{
            "sprk_eventname"        = $event.sprk_eventname
            "sprk_eventdescription" = $event.sprk_eventdescription
            "sprk_eventstatus"      = $event.sprk_eventstatus
        }

        New-DataverseRecord `
            -EntitySetName "sprk_events" `
            -NameField "sprk_eventname" `
            -Record $record
    }
} else {
    Write-Host ""
    Write-Host "--- Step 4: Events --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
}

# ============================================================================
# STEP 5: CREATE DOCUMENT METADATA (requires Spaarke solutions)
# ============================================================================

if ($solutionsImported -and -not $SkipDocuments) {
    Write-Host ""
    Write-Host "--- Step 5: Documents ---" -ForegroundColor Cyan
    Write-Host "  Creating $($data.documents.Count) document metadata records..." -ForegroundColor Gray

    foreach ($doc in $data.documents) {
        $record = @{
            "sprk_documentname"        = $doc.sprk_documentname
            "sprk_documentdescription" = $doc.sprk_documentdescription
        }

        New-DataverseRecord `
            -EntitySetName "sprk_documents" `
            -NameField "sprk_documentname" `
            -Record $record
    }
} elseif (-not $solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 5: Documents --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "--- Step 5: Documents --- SKIPPED" -ForegroundColor Yellow
}

# ============================================================================
# STEP 6: CREATE CHART DEFINITIONS (requires Spaarke solutions)
# ============================================================================

if ($solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 6: Chart Definitions ---" -ForegroundColor Cyan
    Write-Host "  Creating $($data.chartDefinitions.Count) chart definition records..." -ForegroundColor Gray

    foreach ($chart in $data.chartDefinitions) {
        $record = @{
            "sprk_name"               = $chart.sprk_name
            "sprk_visualtype"         = $chart.sprk_visualtype
            "sprk_entitylogicalname"  = $chart.sprk_entitylogicalname
            "sprk_aggregationtype"    = $chart.sprk_aggregationtype
            "sprk_optionsjson"        = $chart.sprk_optionsjson
        }
        if ($chart.sprk_groupbyfield) {
            $record["sprk_groupbyfield"] = $chart.sprk_groupbyfield
        }

        New-DataverseRecord `
            -EntitySetName "sprk_chartdefinitions" `
            -NameField "sprk_name" `
            -Record $record
    }
} else {
    Write-Host ""
    Write-Host "--- Step 6: Chart Definitions --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
}

# ============================================================================
# STEP 7: DEPLOY AI SEED DATA (requires Spaarke solutions)
# ============================================================================

if ($solutionsImported -and -not $SkipAiSeedData) {
    Write-Host ""
    Write-Host "--- Step 7: AI Seed Data ---" -ForegroundColor Cyan

    $aiSeedScript = Join-Path $SeedDataDir "Deploy-All-AI-SeedData.ps1"
    if (Test-Path $aiSeedScript) {
        Write-Host "  Running Deploy-All-AI-SeedData.ps1..." -ForegroundColor Gray

        $aiParams = @{
            EnvironmentUrl   = $EnvironmentUrl
            SkipVerification = $true
        }
        if ($DryRun) { $aiParams['DryRun'] = $true }
        if ($Force) { $aiParams['Force'] = $true }

        try {
            & $aiSeedScript @aiParams
            Write-Host "  [ok] AI seed data deployed" -ForegroundColor Green
        } catch {
            Write-Host "  [!!] AI seed data deployment failed: $($_.Exception.Message)" -ForegroundColor Red
            $script:summary.Errors++
        }
    } else {
        Write-Host "  [!!] Deploy-All-AI-SeedData.ps1 not found at: $aiSeedScript" -ForegroundColor Yellow
        Write-Host "       AI seed data must be deployed manually." -ForegroundColor Yellow
    }
} elseif (-not $solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 7: AI Seed Data --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "--- Step 7: AI Seed Data --- SKIPPED" -ForegroundColor Yellow
}

# ============================================================================
# STEP 8: UPLOAD SAMPLE DOCUMENTS TO SPE (optional, requires Graph API auth)
# ============================================================================

if (-not $SkipSpeUpload) {
    Write-Host ""
    Write-Host "--- Step 8: SPE Document Upload ---" -ForegroundColor Cyan

    if (-not (Test-Path $SampleDocsPath)) {
        Write-Host "  [!!] Sample documents directory not found: $SampleDocsPath" -ForegroundColor Yellow
        Write-Host "       Skipping SPE upload." -ForegroundColor Yellow
    } else {
        $sampleFiles = Get-ChildItem -Path $SampleDocsPath -File
        Write-Host "  Found $($sampleFiles.Count) sample documents in $SampleDocsPath" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  NOTE: SPE document upload requires Graph API authentication" -ForegroundColor Yellow
        Write-Host "  configured for the demo tenant. This is typically handled" -ForegroundColor Yellow
        Write-Host "  by the BFF API during normal document upload operations." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  For demo setup, documents can be uploaded via:" -ForegroundColor Gray
        Write-Host "    1. The Spaarke UI (UniversalQuickCreate PCF control)" -ForegroundColor Gray
        Write-Host "    2. Graph API direct calls (requires app registration)" -ForegroundColor Gray
        Write-Host "    3. The BFF API /api/documents/upload endpoint" -ForegroundColor Gray
        Write-Host ""

        if ($DryRun) {
            foreach ($file in $sampleFiles) {
                Write-Host "    WOULD UPLOAD: $($file.Name) ($([math]::Round($file.Length / 1KB, 1)) KB)" -ForegroundColor Gray
            }
        } else {
            Write-Host "  SPE upload requires interactive authentication or BFF API access." -ForegroundColor Yellow
            Write-Host "  Listing documents for manual upload:" -ForegroundColor Yellow
            Write-Host ""
            foreach ($file in $sampleFiles) {
                Write-Host "    - $($file.Name) ($([math]::Round($file.Length / 1KB, 1)) KB)" -ForegroundColor Gray
            }
            Write-Host ""
            Write-Host "  Upload these documents through the Spaarke UI after solutions are deployed." -ForegroundColor Cyan
        }
    }
} else {
    Write-Host ""
    Write-Host "--- Step 8: SPE Document Upload --- SKIPPED" -ForegroundColor Yellow
}

# ============================================================================
# STEP 9: TRIGGER AI SEARCH INDEXING (optional)
# ============================================================================

if ($solutionsImported -and -not $SkipAiIndexing) {
    Write-Host ""
    Write-Host "--- Step 9: AI Search Indexing ---" -ForegroundColor Cyan

    $syncScript = Join-Path $AiSearchDir "Sync-RecordsToIndex.ps1"
    if (Test-Path $syncScript) {
        Write-Host "  Syncing records to AI Search index..." -ForegroundColor Gray

        if ($DryRun) {
            Write-Host "    WOULD RUN: Sync-RecordsToIndex.ps1 -EnvironmentUrl $EnvironmentUrl -DryRun" -ForegroundColor Gray
            Write-Host "    Records to index: matters, projects" -ForegroundColor Gray
        } else {
            try {
                & $syncScript `
                    -EnvironmentUrl $EnvironmentUrl `
                    -RecordTypes @("matter", "project") `
                    -SearchServiceName "spaarke-search-prod" `
                    -SearchIndexName "spaarke-records-index"

                Write-Host "  [ok] Records synced to AI Search" -ForegroundColor Green
            } catch {
                Write-Host "  [!!] AI Search sync failed: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "       This can be re-run later with:" -ForegroundColor Yellow
                Write-Host "       .\ai-search\Sync-RecordsToIndex.ps1 -EnvironmentUrl $EnvironmentUrl" -ForegroundColor Yellow
                $script:summary.Errors++
            }
        }
    } else {
        Write-Host "  [!!] Sync-RecordsToIndex.ps1 not found at: $syncScript" -ForegroundColor Yellow
        Write-Host "       AI Search indexing must be triggered manually." -ForegroundColor Yellow
    }
} elseif (-not $solutionsImported) {
    Write-Host ""
    Write-Host "--- Step 9: AI Search Indexing --- SKIPPED (solutions not imported)" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "--- Step 9: AI Search Indexing --- SKIPPED" -ForegroundColor Yellow
}

# ============================================================================
# SUMMARY
# ============================================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Created:  $($script:summary.Created)" -ForegroundColor Green
Write-Host "  Skipped:  $($script:summary.Skipped)" -ForegroundColor Yellow
Write-Host "  Errors:   $($script:summary.Errors)" -ForegroundColor $(if ($script:summary.Errors -gt 0) { "Red" } else { "Gray" })
if ($DryRun) {
    Write-Host "  DryRun:   $($script:summary.DryRun)" -ForegroundColor Gray
}
Write-Host ""

if (-not $solutionsImported) {
    Write-Host "  WARNING: Spaarke solutions not imported." -ForegroundColor Yellow
    Write-Host "  Only contacts (standard entity) were processed." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Next steps:" -ForegroundColor Cyan
    Write-Host "    1. Import managed solutions: .\Deploy-DataverseSolutions.ps1" -ForegroundColor Gray
    Write-Host "    2. Re-run this script:       .\Load-DemoSampleData.ps1" -ForegroundColor Gray
    Write-Host "    3. Upload documents via UI:  Open Spaarke in the demo environment" -ForegroundColor Gray
    Write-Host ""
}

if ($script:summary.Errors -gt 0) {
    Write-Host "  Some operations failed. Review errors above and re-run." -ForegroundColor Red
    exit 1
}

Write-Host "  Done!" -ForegroundColor Green
exit 0
