# Export the ThemeMenuRibbons solution to check ribbon definition

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$solutionName = "ThemeMenuRibbons"
$outputPath = "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\ThemeMenuRibbons_current.zip"

Write-Host "====================================="
Write-Host "Exporting $solutionName Solution"
Write-Host "====================================="

# Get access token
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

$apiUrl = "$orgUrl/api/data/v9.2"

# Export solution
Write-Host "Exporting solution..."
$exportUrl = "$apiUrl/ExportSolution"
$exportBody = @{
    SolutionName = $solutionName
    Managed = $false
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri $exportUrl -Headers $headers -Method Post -Body $exportBody
    $solutionBytes = [Convert]::FromBase64String($response.ExportSolutionFile)
    [System.IO.File]::WriteAllBytes($outputPath, $solutionBytes)
    Write-Host "Solution exported to: $outputPath"

    # Extract and show customizations.xml
    Write-Host ""
    Write-Host "Extracting customizations.xml..."
    $extractPath = "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\ThemeMenuRibbons_current_extracted"
    if (Test-Path $extractPath) {
        Remove-Item -Recurse -Force $extractPath
    }
    Expand-Archive -Path $outputPath -DestinationPath $extractPath -Force

    $customizationsPath = Join-Path $extractPath "customizations.xml"
    if (Test-Path $customizationsPath) {
        Write-Host "Found customizations.xml"
        Write-Host ""
        Write-Host "Ribbon XML Content (searching for Image references):"
        Write-Host "====================================="
        $content = Get-Content $customizationsPath -Raw
        # Find lines with Image references
        $content -split "`n" | Where-Object { $_ -match "Image\d+by\d+" -or $_ -match "webresource:" } | ForEach-Object { Write-Host $_.Trim() }
    } else {
        Write-Host "customizations.xml not found in extracted solution"
    }
} catch {
    Write-Host "Error exporting solution: $_" -ForegroundColor Red
    exit 1
}
