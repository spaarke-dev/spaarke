# Export all sprk_* entity ribbons to see what icons other buttons use

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
}
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Checking Ribbon Image References"
Write-Host "====================================="

# Get entity metadata to find ribbon customizations
$entities = @("sprk_matter", "sprk_project", "sprk_event", "sprk_document")

foreach ($entity in $entities) {
    Write-Host ""
    Write-Host "--- $entity ---"

    # Get ribbon metadata
    $ribbonUrl = "$apiUrl/RetrieveEntityRibbon(EntityName='$entity')"
    try {
        $ribbonResponse = Invoke-RestMethod -Uri $ribbonUrl -Headers $headers -Method Get

        if ($ribbonResponse.CompressedEntityXml) {
            # Decompress the ribbon XML
            $bytes = [Convert]::FromBase64String($ribbonResponse.CompressedEntityXml)
            $ms = New-Object System.IO.MemoryStream(,$bytes)
            $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
            $reader = New-Object System.IO.StreamReader($gz)
            $ribbonXml = $reader.ReadToEnd()
            $reader.Close()
            $gz.Close()
            $ms.Close()

            # Find Image16by16 and Image32by32 attributes
            $imageMatches = [regex]::Matches($ribbonXml, 'Image\d+by\d+="([^"]+)"')
            $uniqueImages = @{}
            foreach ($match in $imageMatches) {
                $imgRef = $match.Groups[1].Value
                if ($imgRef -like '*webresource*' -and $imgRef -notlike '*/_imgs/*') {
                    $uniqueImages[$imgRef] = $true
                }
            }

            if ($uniqueImages.Count -gt 0) {
                Write-Host "Custom web resource images:"
                foreach ($img in $uniqueImages.Keys) {
                    Write-Host "  $img"
                }
            } else {
                Write-Host "  No custom image references found"
            }
        }
    } catch {
        Write-Host "  Error: $($_.Exception.Message)"
    }
}
