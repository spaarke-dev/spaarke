# Export account entity ribbon to see built-in icon format

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
}
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "Exporting account entity ribbon..."
$body = @{
    EntityName = "account"
    RibbonLocationFilter = 7
} | ConvertTo-Json

$ribbonUrl = "$apiUrl/RetrieveEntityRibbon"
$response = Invoke-RestMethod -Uri $ribbonUrl -Headers $headers -Method Post -Body $body

if ($response.CompressedEntityXml) {
    $bytes = [Convert]::FromBase64String($response.CompressedEntityXml)
    $ms = New-Object System.IO.MemoryStream(,$bytes)
    $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
    $reader = New-Object System.IO.StreamReader($gz)
    $ribbonXml = $reader.ReadToEnd()
    $reader.Close()

    # Save to file
    $ribbonXml | Out-File "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\account_ribbon.xml" -Encoding UTF8
    Write-Host "Saved to account_ribbon.xml"

    # Find Image references - sample unique ones
    Write-Host ""
    Write-Host "Sample Image16by16 references (first 25 unique):"
    $imageMatches = [regex]::Matches($ribbonXml, 'Image16by16="([^"]+)"')
    $uniqueImages = @{}
    foreach ($match in $imageMatches) {
        $img = $match.Groups[1].Value
        if ($uniqueImages.Count -lt 25 -and -not $uniqueImages.ContainsKey($img)) {
            $uniqueImages[$img] = $true
            Write-Host "  $img"
        }
    }

    Write-Host ""
    Write-Host "Sample ModernImage references (first 25 unique):"
    $modernMatches = [regex]::Matches($ribbonXml, 'ModernImage="([^"]+)"')
    $uniqueModern = @{}
    foreach ($match in $modernMatches) {
        $img = $match.Groups[1].Value
        if ($uniqueModern.Count -lt 25 -and -not $uniqueModern.ContainsKey($img)) {
            $uniqueModern[$img] = $true
            Write-Host "  $img"
        }
    }
}
