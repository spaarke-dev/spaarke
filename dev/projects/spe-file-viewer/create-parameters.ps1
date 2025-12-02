# Helper script to create all 6 parameters
# Usage: .\create-parameters.ps1 -CustomApiId "your-guid-here"

param(
    [Parameter(Mandatory=$true)]
    [string]$CustomApiId
)

Write-Host "Creating 6 output parameters for Custom API: $CustomApiId" -ForegroundColor Cyan
Write-Host ""

# Update parameter files with the Custom API ID
$paramFiles = @(
    "param1-previewurl.json",
    "param2-filename.json",
    "param3-filesize.json",
    "param4-contenttype.json",
    "param5-expiresat.json",
    "param6-correlationid.json"
)

foreach ($file in $paramFiles) {
    $content = Get-Content $file -Raw
    $content = $content -replace "REPLACE_WITH_CUSTOM_API_ID", $CustomApiId
    $content | Set-Content $file
}

Write-Host "✅ Updated all parameter files with Custom API ID" -ForegroundColor Green
Write-Host ""

# Create each parameter
Write-Host "Creating parameters..." -ForegroundColor Yellow

pac data create --entity customapiresponseproperty --data-file param1-previewurl.json
Write-Host "  ✅ Parameter 1/6: PreviewUrl" -ForegroundColor Green

pac data create --entity customapiresponseproperty --data-file param2-filename.json
Write-Host "  ✅ Parameter 2/6: FileName" -ForegroundColor Green

pac data create --entity customapiresponseproperty --data-file param3-filesize.json
Write-Host "  ✅ Parameter 3/6: FileSize" -ForegroundColor Green

pac data create --entity customapiresponseproperty --data-file param4-contenttype.json
Write-Host "  ✅ Parameter 4/6: ContentType" -ForegroundColor Green

pac data create --entity customapiresponseproperty --data-file param5-expiresat.json
Write-Host "  ✅ Parameter 5/6: ExpiresAt" -ForegroundColor Green

pac data create --entity customapiresponseproperty --data-file param6-correlationid.json
Write-Host "  ✅ Parameter 6/6: CorrelationId" -ForegroundColor Green

Write-Host ""
Write-Host "🎉 All 6 parameters created successfully!" -ForegroundColor Green
