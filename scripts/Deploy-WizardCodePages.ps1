# Deploy all Wizard Code Page Web Resources to Dataverse
$ErrorActionPreference = 'Stop'
$orgUrl = 'https://spaarkedev1.crm.dynamics.com'
$solutionName = 'spaarke_core'

Write-Host '==========================================='
Write-Host 'Wizard Code Pages - Full Deployment'
Write-Host '==========================================='

# Get access token
Write-Host '[AUTH] Getting access token...'
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host 'Error: Failed to get access token' -ForegroundColor Red
    exit 1
}
Write-Host '       Token acquired' -ForegroundColor Green

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization' = "Bearer $accessToken"
    'Content-Type'  = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
    'Accept' = 'application/json'
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

# Define all web resources to deploy
$webResources = @(
    # New Code Pages (HTML)
    @{ Name = 'sprk_creatematterwizard';             DisplayName = 'Create New Matter';              Type = 1; Path = 'src\solutions\CreateMatterWizard\dist\index.html' }
    @{ Name = 'sprk_createprojectwizard';            DisplayName = 'Create New Project';             Type = 1; Path = 'src\solutions\CreateProjectWizard\dist\index.html' }
    @{ Name = 'sprk_createeventwizard';              DisplayName = 'Create New Event';               Type = 1; Path = 'src\solutions\CreateEventWizard\dist\index.html' }
    @{ Name = 'sprk_createtodowizard';               DisplayName = 'Create New To Do';               Type = 1; Path = 'src\solutions\CreateTodoWizard\dist\index.html' }
    @{ Name = 'sprk_createworkassignmentwizard';     DisplayName = 'Create Work Assignment';         Type = 1; Path = 'src\solutions\CreateWorkAssignmentWizard\dist\index.html' }
    @{ Name = 'sprk_summarizefileswizard';           DisplayName = 'Summarize Files';                Type = 1; Path = 'src\solutions\SummarizeFilesWizard\dist\index.html' }
    @{ Name = 'sprk_findsimilar';                    DisplayName = 'Find Similar Documents';         Type = 1; Path = 'src\solutions\FindSimilarCodePage\dist\index.html' }
    @{ Name = 'sprk_playbooklibrary';                DisplayName = 'Playbook Library';               Type = 1; Path = 'src\solutions\PlaybookLibrary\dist\index.html' }
    # JS command handler
    @{ Name = 'sprk_wizard_commands';                DisplayName = 'Wizard Command Handlers';        Type = 3; Path = 'src\client\webresources\js\sprk_wizard_commands.js' }
    # Updated existing
    @{ Name = 'sprk_documentuploadwizard';           DisplayName = 'Upload Documents';               Type = 1; Path = 'src\solutions\DocumentUploadWizard\dist\index.html' }
    @{ Name = 'sprk_corporateworkspace';             DisplayName = 'Corporate Workspace';            Type = 1; Path = 'src\solutions\LegalWorkspace\dist\corporateworkspace.html' }
    @{ Name = 'sprk_analysisbuilder';                DisplayName = 'Analysis Builder';               Type = 1; Path = 'src\solutions\AnalysisBuilder\dist\analysisbuilder.html' }
)

$publishIds = @()
$step = 0
$total = $webResources.Count

foreach ($wr in $webResources) {
    $step++
    $filePath = Join-Path $repoRoot $wr.Path

    if (-not (Test-Path $filePath)) {
        Write-Host "[$step/$total] SKIP $($wr.Name) - file not found: $($wr.Path)" -ForegroundColor Yellow
        continue
    }

    $fileBytes = [System.IO.File]::ReadAllBytes($filePath)
    $fileContent = [Convert]::ToBase64String($fileBytes)
    $fileSize = [math]::Round((Get-Item $filePath).Length / 1KB)

    # Check if web resource exists
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$($wr.Name)'"
    $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($searchResponse.value.Count -gt 0) {
        # UPDATE existing
        $webResourceId = $searchResponse.value[0].webresourceid
        Write-Host "[$step/$total] UPDATE $($wr.Name) ($fileSize KB)" -ForegroundColor Cyan
        $updateUrl = "$apiUrl/webresourceset($webResourceId)"
        $updateBody = @{ content = $fileContent } | ConvertTo-Json
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
    } else {
        # CREATE new
        Write-Host "[$step/$total] CREATE $($wr.Name) ($fileSize KB)" -ForegroundColor Green
        $createBody = @{
            name = $wr.Name
            displayname = $wr.DisplayName
            webresourcetype = $wr.Type
            content = $fileContent
        } | ConvertTo-Json
        $createHeaders = @{
            'Authorization' = "Bearer $accessToken"
            'Content-Type'  = 'application/json'
            'OData-MaxVersion' = '4.0'
            'OData-Version' = '4.0'
            'Accept' = 'application/json'
            'Prefer' = 'return=representation'
        }
        $createResponse = Invoke-RestMethod -Uri "$apiUrl/webresourceset" -Headers $createHeaders -Method Post -Body $createBody
        $webResourceId = $createResponse.webresourceid

        # Add to solution
        Write-Host "         Adding to $solutionName solution..."
        $addBody = @{
            ComponentId = $webResourceId
            ComponentType = 61  # WebResource
            SolutionUniqueName = $solutionName
            AddRequiredComponents = $false
        } | ConvertTo-Json
        Invoke-RestMethod -Uri "$apiUrl/AddSolutionComponent" -Headers $headers -Method Post -Body $addBody | Out-Null
    }

    $publishIds += $webResourceId
    Write-Host "         Done: $webResourceId" -ForegroundColor DarkGray
}

# Publish all at once
Write-Host ''
Write-Host "[PUBLISH] Publishing $($publishIds.Count) web resources..."
$wrXml = ($publishIds | ForEach-Object { "<webresource>{$_}</webresource>" }) -join ''
$publishXml = "<importexportxml><webresources>$wrXml</webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host '           Published!' -ForegroundColor Green

Write-Host ''
Write-Host '==========================================='
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host '==========================================='
