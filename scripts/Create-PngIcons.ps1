# Create PNG icons from SVG and deploy to Dataverse
# PNG is more universally supported in Dataverse ribbons

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
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

Write-Host "====================================="
Write-Host "Creating PNG Icons"
Write-Host "====================================="

# Since we can't easily convert SVG to PNG without external tools,
# let's create simple colored PNG icons using .NET

Add-Type -AssemblyName System.Drawing

function Create-CircleIcon {
    param (
        [int]$size,
        [string]$fillColor,
        [string]$borderColor,
        [bool]$halfAndHalf = $false
    )

    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Clear with transparent
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $padding = [int]($size * 0.1)
    $circleSize = $size - (2 * $padding)

    if ($halfAndHalf) {
        # Draw half white, half gray circle
        $leftBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $rightBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(102, 102, 102))
        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(136, 136, 136), 1)

        # Left half (white)
        $graphics.FillPie($leftBrush, $padding, $padding, $circleSize, $circleSize, 90, 180)
        # Right half (gray)
        $graphics.FillPie($rightBrush, $padding, $padding, $circleSize, $circleSize, -90, 180)
        # Border
        $graphics.DrawEllipse($borderPen, $padding, $padding, $circleSize, $circleSize)

        $leftBrush.Dispose()
        $rightBrush.Dispose()
        $borderPen.Dispose()
    } else {
        # Solid color circle
        $fill = [System.Drawing.ColorTranslator]::FromHtml($fillColor)
        $border = [System.Drawing.ColorTranslator]::FromHtml($borderColor)

        $brush = New-Object System.Drawing.SolidBrush($fill)
        $pen = New-Object System.Drawing.Pen($border, 1)

        $graphics.FillEllipse($brush, $padding, $padding, $circleSize, $circleSize)
        $graphics.DrawEllipse($pen, $padding, $padding, $circleSize, $circleSize)

        $brush.Dispose()
        $pen.Dispose()
    }

    $graphics.Dispose()

    # Convert to byte array
    $ms = New-Object System.IO.MemoryStream
    $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $bitmap.Dispose()

    return $bytes
}

# Create icons
$icons = @(
    @{ name = "sprk_ThemeMenu16_png"; size = 16; halfAndHalf = $true },
    @{ name = "sprk_ThemeMenu32_png"; size = 32; halfAndHalf = $true },
    @{ name = "sprk_ThemeAuto16_png"; size = 16; halfAndHalf = $true },
    @{ name = "sprk_ThemeLight16_png"; size = 16; fill = "#FFFFFF"; border = "#888888" },
    @{ name = "sprk_ThemeDark16_png"; size = 16; fill = "#666666"; border = "#888888" }
)

$webResourceIds = @()

foreach ($icon in $icons) {
    Write-Host ""
    Write-Host "Creating: $($icon.name)"

    if ($icon.halfAndHalf) {
        $pngBytes = Create-CircleIcon -size $icon.size -halfAndHalf $true
    } else {
        $pngBytes = Create-CircleIcon -size $icon.size -fillColor $icon.fill -borderColor $icon.border
    }

    $pngContent = [Convert]::ToBase64String($pngBytes)
    Write-Host "  Generated ($($pngBytes.Length) bytes)"

    # Check if exists
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$($icon.name)'&`$select=webresourceid,name"
    $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($response.value.Count -gt 0) {
        $webResourceId = $response.value[0].webresourceid
        Write-Host "  Updating existing: $webResourceId"

        $updateUrl = "$apiUrl/webresourceset($webResourceId)"
        $updateBody = @{ content = $pngContent } | ConvertTo-Json
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
        Write-Host "  Updated" -ForegroundColor Green
        $webResourceIds += $webResourceId
    } else {
        Write-Host "  Creating new..."
        $createBody = @{
            name = $icon.name
            displayname = $icon.name
            webresourcetype = 5  # PNG
            content = $pngContent
        } | ConvertTo-Json
        $createUrl = "$apiUrl/webresourceset"

        try {
            Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $createBody | Out-Null
            Write-Host "  Created" -ForegroundColor Green

            # Get the ID
            $searchUrl2 = "$apiUrl/webresourceset?`$filter=name eq '$($icon.name)'&`$select=webresourceid"
            $response2 = Invoke-RestMethod -Uri $searchUrl2 -Headers $headers -Method Get
            if ($response2.value.Count -gt 0) {
                $webResourceIds += $response2.value[0].webresourceid
            }
        } catch {
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "====================================="
Write-Host "Publishing..."
Write-Host "====================================="

if ($webResourceIds.Count -gt 0) {
    $webResourcesXml = ""
    foreach ($id in $webResourceIds) {
        $webResourcesXml += "<webresource>{$id}</webresource>"
    }

    $publishXml = "<importexportxml><webresources>$webResourcesXml</webresources></importexportxml>"
    $publishUrl = "$apiUrl/PublishXml"
    $publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
        Write-Host "Published!" -ForegroundColor Green
    } catch {
        Write-Host "Error: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "PNG icons created. Update ribbon XML to use:"
Write-Host "  `$webresource:sprk_ThemeMenu16_png"
Write-Host "  `$webresource:sprk_ThemeMenu32_png"
Write-Host "  `$webresource:sprk_ThemeAuto16_png"
Write-Host "  `$webresource:sprk_ThemeLight16_png"
Write-Host "  `$webresource:sprk_ThemeDark16_png"
