# Get-ContainerFiles.ps1
# Lists all files in a SharePoint Embedded container via SDAP BFF API

param(
    [string]$ApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",
    [string]$DriveId = "b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy",
    [string]$ItemId = "",
    [switch]$OutputJson
)

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "SDAP Container File Listing Utility" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "API URL: $ApiUrl" -ForegroundColor Gray
Write-Host "Drive/Container ID: $DriveId" -ForegroundColor Gray
Write-Host ""

try {
    # Build URL
    $url = "$ApiUrl/api/drives/$([Uri]::EscapeDataString($DriveId))/children"
    if ($ItemId) {
        $url += "?itemId=$([Uri]::EscapeDataString($ItemId))"
    }

    Write-Host "Fetching files from: $url" -ForegroundColor Gray
    Write-Host ""

    # Make request (uses current user credentials)
    $response = Invoke-RestMethod -Uri $url -Method Get -UseDefaultCredentials -ContentType "application/json"

    if (!$response -or $response.Count -eq 0) {
        Write-Host "No files found in this container." -ForegroundColor Yellow
        return
    }

    Write-Host "Found $($response.Count) file(s)" -ForegroundColor Green
    Write-Host ""

    if ($OutputJson) {
        # Output raw JSON
        $response | ConvertTo-Json -Depth 10
    } else {
        # Display formatted table
        $files = $response | ForEach-Object {
            [PSCustomObject]@{
                Name = $_.name
                ItemId = $_.id
                Size = if ($_.size) { "{0:N2} KB" -f ($_.size / 1KB) } else { "0 KB" }
                Modified = if ($_.lastModifiedDateTime) { (Get-Date $_.lastModifiedDateTime).ToString("yyyy-MM-dd HH:mm") } else { "-" }
                WebUrl = $_.webUrl
            }
        }

        $files | Format-Table -AutoSize

        Write-Host ""
        Write-Host "=====================================" -ForegroundColor Cyan
        Write-Host "Detailed Metadata for Dataverse" -ForegroundColor Cyan
        Write-Host "=====================================" -ForegroundColor Cyan
        Write-Host ""

        foreach ($file in $response) {
            Write-Host "File: " -NoNewline -ForegroundColor White
            Write-Host $file.name -ForegroundColor Yellow
            Write-Host "  Item ID (sprk_graphitemid): $($file.id)" -ForegroundColor Green
            Write-Host "  Web URL (sprk_filepath): $($file.webUrl)" -ForegroundColor Green
            Write-Host "  Size (sprk_filesize): $($file.size) bytes" -ForegroundColor Green
            Write-Host "  Created (sprk_createddatetime): $($file.createdDateTime)" -ForegroundColor Green
            Write-Host "  Modified (sprk_lastmodifieddatetime): $($file.lastModifiedDateTime)" -ForegroundColor Green
            Write-Host "  ETag (sprk_etag): $($file.eTag)" -ForegroundColor Green
            if ($file.parentId) {
                Write-Host "  Parent (sprk_parentfolderid): $($file.parentId)" -ForegroundColor Green
            }
            Write-Host ""
        }

        Write-Host ""
        Write-Host "For each Document record, set:" -ForegroundColor Cyan
        Write-Host "  sprk_graphdriveid = $DriveId" -ForegroundColor Yellow
        Write-Host "  sprk_hasfile = true" -ForegroundColor Yellow
        Write-Host "  + specific item metadata from above" -ForegroundColor Yellow
        Write-Host ""
    }

} catch {
    Write-Host ""
    Write-Host "ERROR: Failed to fetch files" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Cyan
    Write-Host "  1. Verify SDAP API is running" -ForegroundColor Gray
    Write-Host "  2. Check container/drive ID" -ForegroundColor Gray
    Write-Host "  3. Verify permissions" -ForegroundColor Gray
    Write-Host ""

    exit 1
}
