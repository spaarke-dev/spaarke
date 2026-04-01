<#
.SYNOPSIS
    Deploy Power BI report templates (.pbix) to a customer Power BI workspace and sync
    the Dataverse report catalog.

.DESCRIPTION
    End-to-end report deployment script for the Spaarke Reporting module:
    1. Authenticates to Power BI REST API using service principal credentials
    2. Imports each .pbix file in -ReportFolder into the target PBI workspace
    3. Rebinds each imported dataset to the customer's Dataverse instance
    4. Sets a scheduled refresh on each imported dataset
    5. Creates or updates sprk_report records in Dataverse for the deployed reports
    6. Outputs a summary of all deployed reports

    All PBI configuration (tenant, workspace, credentials) comes from environment
    variables — no hardcoded IDs. Supports -WhatIf for dry-run preview.

    Authentication uses service principal credentials (client credentials flow) for
    Power BI REST API. Dataverse operations use Azure CLI tokens.

.PARAMETER WorkspaceId
    Power BI workspace (group) ID to deploy reports into.
    Must match the customer's PBI workspace provisioned during onboarding.
    Reads from env var PBI_WORKSPACE_ID if not provided.

.PARAMETER DatasetId
    (Optional) Override dataset ID used for rebinding all imported datasets.
    When omitted, the dataset ID returned by each import operation is used.

.PARAMETER ReportFolder
    Path to folder containing .pbix files to deploy.
    Default: reports/v1.0.0 (relative to repo root)

.PARAMETER DataverseOrg
    Dataverse organization URL (e.g. https://myorg.crm.dynamics.com).
    Reads from env var DATAVERSE_URL if not provided.

.PARAMETER Environment
    Target environment name (dev, staging, production). Used for display and logging.
    Default: dev

.PARAMETER WhatIf
    Preview all operations without making any API calls or changes.

.EXAMPLE
    # Dry run — preview what would be deployed
    .\Deploy-ReportingReports.ps1 -WorkspaceId "00000000-0000-0000-0000-000000000000" -WhatIf

.EXAMPLE
    # Deploy standard product reports to dev workspace
    .\Deploy-ReportingReports.ps1

.EXAMPLE
    # Deploy from a specific version folder
    .\Deploy-ReportingReports.ps1 -ReportFolder "reports/v1.2.0" -WorkspaceId "abc-123"

.EXAMPLE
    # Deploy to staging environment
    .\Deploy-ReportingReports.ps1 -Environment staging -DataverseOrg "https://myorg-stg.crm.dynamics.com"

.NOTES
    Required environment variables:
      PBI_TENANT_ID           — Entra ID tenant ID
      PBI_CLIENT_ID           — Service principal app (client) ID
      PBI_CLIENT_SECRET       — Service principal client secret
      PBI_WORKSPACE_ID        — Target PBI workspace ID (can be overridden by -WorkspaceId)
      PBI_DATAVERSE_DATASOURCE_URL — Dataverse OData endpoint for dataset rebinding
                                     (e.g. https://myorg.crm.dynamics.com/api/data/v9.2/)
      DATAVERSE_URL           — Dataverse org URL for catalog sync

    Optional environment variables:
      PBI_DATASET_ID          — Default dataset ID override (can be overridden by -DatasetId)
      PBI_REFRESH_ENABLED     — Set to "false" to skip scheduling refresh (default: true)

    Prerequisites:
      - Azure CLI installed and authenticated (az login) — for Dataverse catalog sync
      - Service principal must have Power BI workspace Member or Admin role
      - Service principal must be added to PBI tenant allowlist (Admin > Tenant settings)
      - PowerShell 7+ recommended
#>

param(
    [string]$WorkspaceId = $env:PBI_WORKSPACE_ID,

    [string]$DatasetId = $env:PBI_DATASET_ID,

    [string]$ReportFolder,

    [string]$DataverseOrg = $env:DATAVERSE_URL,

    [ValidateSet('dev', 'staging', 'production')]
    [string]$Environment = 'dev',

    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot   = Split-Path -Parent $ScriptDir

if (-not $ReportFolder) {
    $ReportFolder = Join-Path $RepoRoot 'reports\v1.0.0'
} elseif (-not [System.IO.Path]::IsPathRooted($ReportFolder)) {
    $ReportFolder = Join-Path $RepoRoot $ReportFolder
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  Reporting Reports Deployment' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host "  Environment  : $Environment"
Write-Host "  Report Folder: $ReportFolder"
Write-Host "  Workspace ID : $(if ($WorkspaceId) { $WorkspaceId } else { '(not set)' })"
Write-Host "  Dataverse URL: $(if ($DataverseOrg) { $DataverseOrg } else { '(not set)' })"
if ($WhatIf) {
    Write-Host '  Mode         : DRY RUN (-WhatIf) — no changes will be made' -ForegroundColor Yellow
}
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''

# ---------------------------------------------------------------------------
# Step 1 — Validate prerequisites
# ---------------------------------------------------------------------------
Write-Host '[1/6] Validating prerequisites...' -ForegroundColor Yellow

# Required env vars
$tenantId     = $env:PBI_TENANT_ID
$clientId     = $env:PBI_CLIENT_ID
$clientSecret = $env:PBI_CLIENT_SECRET
$dvDatasourceUrl = $env:PBI_DATAVERSE_DATASOURCE_URL

$missingVars = @()
if (-not $tenantId)       { $missingVars += 'PBI_TENANT_ID' }
if (-not $clientId)       { $missingVars += 'PBI_CLIENT_ID' }
if (-not $clientSecret)   { $missingVars += 'PBI_CLIENT_SECRET' }
if (-not $WorkspaceId)    { $missingVars += 'PBI_WORKSPACE_ID (or -WorkspaceId)' }
if (-not $DataverseOrg)   { $missingVars += 'DATAVERSE_URL (or -DataverseOrg)' }
if (-not $dvDatasourceUrl){ $missingVars += 'PBI_DATAVERSE_DATASOURCE_URL' }

if ($missingVars.Count -gt 0) {
    Write-Host '  ERROR: Missing required configuration:' -ForegroundColor Red
    foreach ($v in $missingVars) {
        Write-Host "    - $v" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host '  Set these as environment variables before running this script.' -ForegroundColor Yellow
    exit 1
}

# Validate report folder exists
if (-not (Test-Path $ReportFolder)) {
    Write-Host "  ERROR: Report folder not found: $ReportFolder" -ForegroundColor Red
    Write-Host '  Create the folder and add .pbix files, or specify -ReportFolder.' -ForegroundColor Yellow
    exit 1
}

# Enumerate .pbix files
$pbixFiles = Get-ChildItem -Path $ReportFolder -Filter '*.pbix' -File
if ($pbixFiles.Count -eq 0) {
    Write-Host "  WARNING: No .pbix files found in $ReportFolder" -ForegroundColor Yellow
    Write-Host '  Nothing to deploy. Exiting.' -ForegroundColor Yellow
    exit 0
}

Write-Host "  Found $($pbixFiles.Count) .pbix file(s):" -ForegroundColor Green
foreach ($f in $pbixFiles) {
    Write-Host "    - $($f.Name)" -ForegroundColor Gray
}

# Verify az CLI available (needed for Dataverse catalog sync)
$azVersion = az version --query '"azure-cli"' -o tsv 2>$null
if (-not $azVersion) {
    Write-Host '  ERROR: Azure CLI not found. Install from https://aka.ms/installazurecliwindows' -ForegroundColor Red
    exit 1
}
Write-Host "  Azure CLI: $azVersion" -ForegroundColor Green
Write-Host '  Prerequisites OK' -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 2 — Authenticate to Power BI REST API
# ---------------------------------------------------------------------------
Write-Host '[2/6] Authenticating to Power BI REST API (service principal)...' -ForegroundColor Yellow

$pbiTokenUrl  = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"
$pbiTokenBody = @{
    grant_type    = 'client_credentials'
    client_id     = $clientId
    client_secret = $clientSecret
    scope         = 'https://analysis.windows.net/powerbi/api/.default'
}

if ($WhatIf) {
    Write-Host '  [WhatIf] Would acquire PBI service principal token' -ForegroundColor Yellow
    $pbiToken = 'WHATIF-TOKEN'
} else {
    try {
        $tokenResponse = Invoke-RestMethod -Uri $pbiTokenUrl -Method Post -Body $pbiTokenBody -ContentType 'application/x-www-form-urlencoded'
        $pbiToken = $tokenResponse.access_token
        Write-Host '  Power BI token acquired' -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: Failed to acquire Power BI token: $_" -ForegroundColor Red
        Write-Host '  Verify PBI_TENANT_ID, PBI_CLIENT_ID, and PBI_CLIENT_SECRET are correct.' -ForegroundColor Yellow
        exit 1
    }
}

$pbiHeaders = @{
    'Authorization' = "Bearer $pbiToken"
    'Content-Type'  = 'application/json'
}

# PBI REST API base URL
$pbiApiBase = "https://api.powerbi.com/v1.0/myorg/groups/$WorkspaceId"

# ---------------------------------------------------------------------------
# Step 3 — Authenticate to Dataverse (Azure CLI)
# ---------------------------------------------------------------------------
Write-Host '[3/6] Authenticating to Dataverse (Azure CLI)...' -ForegroundColor Yellow

$dvApiUrl = "$DataverseOrg/api/data/v9.2"
$dvHeaders = $null

if ($WhatIf) {
    Write-Host '  [WhatIf] Would acquire Dataverse token via az CLI' -ForegroundColor Yellow
} else {
    $dvToken = az account get-access-token --resource $DataverseOrg --query accessToken -o tsv 2>$null
    if ([string]::IsNullOrEmpty($dvToken)) {
        Write-Host '  ERROR: Failed to get Dataverse token. Run: az login' -ForegroundColor Red
        exit 1
    }
    $dvHeaders = @{
        'Authorization'    = "Bearer $dvToken"
        'Content-Type'     = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Accept'           = 'application/json'
    }
    Write-Host '  Dataverse token acquired' -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Helper: Wait for PBI import to complete
# ---------------------------------------------------------------------------
function Wait-PbiImport {
    param(
        [string]$ImportId,
        [hashtable]$Headers,
        [string]$ApiBase,
        [int]$MaxWaitSeconds = 120
    )

    $elapsed  = 0
    $interval = 5
    while ($elapsed -lt $MaxWaitSeconds) {
        Start-Sleep -Seconds $interval
        $elapsed += $interval

        $statusResponse = Invoke-RestMethod -Uri "$ApiBase/imports/$ImportId" -Headers $Headers -Method Get
        $state = $statusResponse.importState

        if ($state -eq 'Succeeded') {
            return $statusResponse
        } elseif ($state -eq 'Failed') {
            throw "Import '$ImportId' failed in Power BI."
        }

        Write-Host "    Still importing... ($elapsed`s elapsed, state: $state)" -ForegroundColor Gray
    }

    throw "Timed out waiting for import '$ImportId' after $MaxWaitSeconds seconds."
}

# ---------------------------------------------------------------------------
# Step 4 — Import .pbix files, rebind datasets, configure refresh
# ---------------------------------------------------------------------------
Write-Host '[4/6] Importing reports, rebinding datasets, configuring refresh...' -ForegroundColor Yellow

$deployedReports = @()
$refreshEnabled  = ($env:PBI_REFRESH_ENABLED -ne 'false')

foreach ($pbixFile in $pbixFiles) {
    $reportName = [System.IO.Path]::GetFileNameWithoutExtension($pbixFile.Name)
    Write-Host ''
    Write-Host "  --- $reportName ---" -ForegroundColor Cyan

    # ---- 4a. Import .pbix ----
    Write-Host "    [4a] Importing $($pbixFile.Name) to workspace..." -ForegroundColor Yellow

    if ($WhatIf) {
        Write-Host "    [WhatIf] Would POST $($pbixFile.FullName) to $pbiApiBase/imports?datasetDisplayName=$reportName&nameConflict=CreateOrOverwrite" -ForegroundColor Yellow
        $deployedReports += [PSCustomObject]@{
            Name      = $reportName
            FileName  = $pbixFile.Name
            ReportId  = "WHATIF-REPORT-ID-$reportName"
            DatasetId = "WHATIF-DATASET-ID-$reportName"
            Status    = 'WhatIf'
        }
        continue
    }

    try {
        # Multipart form upload using .NET HttpClient for binary .pbix support
        $fileBytes   = [System.IO.File]::ReadAllBytes($pbixFile.FullName)
        $boundary    = [System.Guid]::NewGuid().ToString()
        $contentType = "multipart/form-data; boundary=$boundary"

        $bodyLines = [System.Collections.Generic.List[byte]]::new()
        $nl        = [System.Text.Encoding]::UTF8.GetBytes("`r`n")

        # Part header
        $partHeader = [System.Text.Encoding]::UTF8.GetBytes(
            "--$boundary`r`nContent-Disposition: form-data; name=`"file`"; filename=`"$($pbixFile.Name)`"`r`nContent-Type: application/octet-stream`r`n`r`n"
        )
        $bodyLines.AddRange($partHeader)
        $bodyLines.AddRange($fileBytes)
        $bodyLines.AddRange($nl)

        # Closing boundary
        $closingBoundary = [System.Text.Encoding]::UTF8.GetBytes("--$boundary--`r`n")
        $bodyLines.AddRange($closingBoundary)

        $importUrl     = "$pbiApiBase/imports?datasetDisplayName=$([Uri]::EscapeDataString($reportName))&nameConflict=CreateOrOverwrite"
        $importHeaders = @{
            'Authorization' = "Bearer $pbiToken"
            'Content-Type'  = $contentType
        }

        $importResponse = Invoke-RestMethod -Uri $importUrl -Method Post -Headers $importHeaders -Body $bodyLines.ToArray()
        $importId       = $importResponse.id

        Write-Host "    Import submitted (id: $importId). Waiting for completion..." -ForegroundColor Gray

        $completedImport = Wait-PbiImport -ImportId $importId -Headers $pbiHeaders -ApiBase $pbiApiBase
        $importedReportId  = $completedImport.reports[0].id
        $importedDatasetId = if ($DatasetId) { $DatasetId } else { $completedImport.datasets[0].id }

        Write-Host "    Import succeeded. ReportId: $importedReportId  DatasetId: $importedDatasetId" -ForegroundColor Green
    } catch {
        Write-Host "    ERROR importing $($pbixFile.Name): $_" -ForegroundColor Red
        Write-Host '    Skipping this report.' -ForegroundColor Yellow
        continue
    }

    # ---- 4b. Rebind dataset to Dataverse ----
    Write-Host '    [4b] Rebinding dataset to Dataverse datasource...' -ForegroundColor Yellow

    try {
        $rebindUrl  = "$pbiApiBase/datasets/$importedDatasetId/Default.SetAllConnections"
        $rebindBody = @{
            connectionString = "Data Source=$dvDatasourceUrl;Integrated Security=False"
        } | ConvertTo-Json -Depth 2
        $rebindBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($rebindBody)

        Invoke-RestMethod -Uri $rebindUrl -Method Post -Headers $pbiHeaders -Body $rebindBodyBytes | Out-Null
        Write-Host "    Dataset rebound to: $dvDatasourceUrl" -ForegroundColor Green
    } catch {
        # SetAllConnections may 404/400 if the dataset has no OData source — warn but continue
        Write-Host "    WARNING: Rebind failed (non-fatal, dataset may use embedded credentials): $_" -ForegroundColor Yellow
    }

    # ---- 4c. Configure scheduled refresh ----
    if ($refreshEnabled) {
        Write-Host '    [4c] Configuring scheduled refresh...' -ForegroundColor Yellow
        try {
            $refreshUrl  = "$pbiApiBase/datasets/$importedDatasetId/refreshSchedule"
            $refreshBody = @{
                value = @{
                    enabled             = $true
                    days                = @('Monday','Tuesday','Wednesday','Thursday','Friday')
                    times               = @('06:00','12:00','18:00')
                    localTimeZoneId     = 'UTC'
                    notifyOption        = 'NoNotification'
                }
            } | ConvertTo-Json -Depth 4
            $refreshBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($refreshBody)

            Invoke-RestMethod -Uri $refreshUrl -Method Patch -Headers $pbiHeaders -Body $refreshBodyBytes | Out-Null
            Write-Host '    Refresh schedule set (Mon-Fri 06:00, 12:00, 18:00 UTC)' -ForegroundColor Green
        } catch {
            Write-Host "    WARNING: Could not set refresh schedule (requires PBI Premium or PPU): $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host '    [4c] Skipping refresh schedule (PBI_REFRESH_ENABLED=false)' -ForegroundColor Gray
    }

    $deployedReports += [PSCustomObject]@{
        Name      = $reportName
        FileName  = $pbixFile.Name
        ReportId  = $importedReportId
        DatasetId = $importedDatasetId
        Status    = 'Deployed'
    }
}

# ---------------------------------------------------------------------------
# Step 5 — Seed / update sprk_report records in Dataverse
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[5/6] Syncing Dataverse report catalog (sprk_report)...' -ForegroundColor Yellow

# Infer category from report name using known standard report name prefixes
function Get-ReportCategory {
    param([string]$Name)
    switch -Wildcard ($Name.ToLower()) {
        '*financial*' { return 'Financial' }
        '*document*'  { return 'Documents' }
        '*task*'      { return 'Operational' }
        '*compliance*'{ return 'Compliance' }
        '*matter*'    { return 'Operational' }
        '*pipeline*'  { return 'Operational' }
        default        { return 'Custom' }
    }
}

foreach ($report in $deployedReports) {
    if ($report.Status -eq 'WhatIf') {
        Write-Host "  [WhatIf] Would upsert sprk_report record for '$($report.Name)'" -ForegroundColor Yellow
        continue
    }

    $category = Get-ReportCategory -Name $report.Name

    # Check if record already exists (match by sprk_reportid = PBI report ID)
    $searchUrl      = "$dvApiUrl/sprk_reports?`$filter=sprk_reportid eq '$($report.ReportId)'&`$select=sprk_reportid,sprk_name,_ownerid_value"
    $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $dvHeaders -Method Get

    $recordBody = @{
        sprk_reportid   = $report.ReportId
        sprk_workspaceid = $WorkspaceId
        sprk_datasetid  = $report.DatasetId
        sprk_name       = $report.Name
        sprk_category   = $category
    } | ConvertTo-Json -Depth 2
    $recordBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($recordBody)

    try {
        if ($searchResponse.value.Count -gt 0) {
            # UPDATE existing record
            $existingId  = $searchResponse.value[0].sprk_reportid
            $updateUrl   = "$dvApiUrl/sprk_reports($existingId)"
            $updateHeaders = $dvHeaders.Clone()
            $updateHeaders['If-Match'] = '*'
            Invoke-RestMethod -Uri $updateUrl -Method Patch -Headers $updateHeaders -Body $recordBodyBytes | Out-Null
            Write-Host "  Updated sprk_report: '$($report.Name)' (category: $category)" -ForegroundColor Green
        } else {
            # CREATE new record
            $createHeaders = $dvHeaders.Clone()
            $createHeaders['Prefer'] = 'return=representation'
            $created = Invoke-RestMethod -Uri "$dvApiUrl/sprk_reports" -Method Post -Headers $createHeaders -Body $recordBodyBytes
            Write-Host "  Created sprk_report: '$($report.Name)' (category: $category, id: $($created.sprk_reportid))" -ForegroundColor Green
        }
    } catch {
        Write-Host "  WARNING: Failed to upsert sprk_report for '$($report.Name)': $_" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Step 6 — Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '[6/6] Deployment complete.' -ForegroundColor Yellow
Write-Host ''
Write-Host '================================================================' -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host "  $(if ($WhatIf) { 'WHATIF SUMMARY (no changes made)' } else { 'Deployment Summary' })" -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host '================================================================' -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host "  Environment  : $Environment"
Write-Host "  Workspace ID : $WorkspaceId"
Write-Host "  Report Folder: $ReportFolder"
Write-Host "  Total Reports: $($deployedReports.Count)"
Write-Host ''

if ($deployedReports.Count -gt 0) {
    Write-Host '  Report                        | Status     | Report ID' -ForegroundColor Gray
    Write-Host '  ------------------------------ | ---------- | ------------------------------------' -ForegroundColor Gray
    foreach ($r in $deployedReports) {
        $namePadded   = $r.Name.PadRight(30)
        $statusPadded = $r.Status.PadRight(10)
        Write-Host "  $namePadded | $statusPadded | $($r.ReportId)"
    }
}

Write-Host ''
Write-Host '================================================================' -ForegroundColor $(if ($WhatIf) { 'Yellow' } else { 'Green' })
Write-Host ''

if (-not $WhatIf) {
    Write-Host 'Reports are now available in the Spaarke Reporting module.' -ForegroundColor Gray
    Write-Host "Dataverse catalog: $DataverseOrg (sprk_report entity)" -ForegroundColor Gray
    Write-Host ''
}
