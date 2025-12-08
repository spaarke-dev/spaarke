# Export entity ribbon to see built-in icon format

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
}
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Exporting Entity Ribbon"
Write-Host "====================================="

# Use RetrieveEntityRibbon action
$body = @{
    EntityName = "account"
    RibbonLocationFilter = 7  # All locations
} | ConvertTo-Json

$ribbonUrl = "$apiUrl/RetrieveEntityRibbon"
try {
    $response = Invoke-RestMethod -Uri $ribbonUrl -Headers $headers -Method Post -Body $body

    if ($response.CompressedEntityXml) {
        # Decompress
        $bytes = [Convert]::FromBase64String($response.CompressedEntityXml)
        $ms = New-Object System.IO.MemoryStream(,$bytes)
        $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
        $reader = New-Object System.IO.StreamReader($gz)
        $ribbonXml = $reader.ReadToEnd()
        $reader.Close()
        $gz.Close()
        $ms.Close()

        # Save to file
        $ribbonXml | Out-File "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\account_ribbon.xml" -Encoding UTF8
        Write-Host "Saved to account_ribbon.xml"

        # Search for Image references
        Write-Host ""
        Write-Host "Sample Image references (first 30):"
        $lines = $ribbonXml -split "`n"
        $imageLines = $lines | Where-Object { $_ -match 'Image\d+by\d+' } | Select-Object -First 30
        foreach ($line in $imageLines) {
            # Extract just the image attribute
            if ($line -match 'Image\d+by\d+="([^"]+)"') {
                Write-Host "  $($matches[0])"
            }
        }
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
