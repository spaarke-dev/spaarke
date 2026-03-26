#!/usr/bin/env pwsh
# Fix the Document main form - remove stale controlDescription parameters
# that reference properties the PCF controls no longer declare

param(
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com"
)

$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json"; "OData-Version" = "4.0" }

$formId = "9088d6a4-4cf2-f011-8406-7c1e520aa4df"

Write-Host "Loading Document main form ($formId)..."
$r = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($formId)?`$select=formxml,name" -Headers $headers
$xml = $r.formxml
Write-Host "Form: $($r.name)"
Write-Host "Original XML length: $($xml.Length)"

# Parse as XML for precise editing
[xml]$doc = $xml

# Find all controlDescription elements
$controlDescs = $doc.SelectNodes("//controlDescription")
Write-Host "Found $($controlDescs.Count) controlDescription elements"

$changes = 0
foreach ($cd in $controlDescs) {
    $customControls = $cd.SelectNodes(".//customControl")
    foreach ($cc in $customControls) {
        $ctrlName = $cc.GetAttribute("name")
        $params = $cc.SelectSingleNode("parameters")
        if (-not $params) { continue }

        # Remove stale parameter nodes
        foreach ($paramName in @("tenantId", "apiBaseUrl", "bffApiUrl")) {
            $paramNode = $params.SelectSingleNode($paramName)
            if ($paramNode) {
                # Check if it has static="true" (form-configured static value)
                $isStatic = $paramNode.GetAttribute("static") -eq "true"
                if ($isStatic) {
                    $params.RemoveChild($paramNode) | Out-Null
                    Write-Host "  Removed: $paramName (static) from $ctrlName"
                    $changes++
                }
            }
        }
    }
}

Write-Host ""
Write-Host "Total changes: $changes"

if ($changes -gt 0) {
    # Convert back to string
    $sw = [System.IO.StringWriter]::new()
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.OmitXmlDeclaration = $true
    $settings.Indent = $false
    $writer = [System.Xml.XmlWriter]::Create($sw, $settings)
    $doc.WriteTo($writer)
    $writer.Flush()
    $newXml = $sw.ToString()

    Write-Host "Updated XML length: $($newXml.Length)"

    # Update the form
    $body = @{ formxml = $newXml } | ConvertTo-Json -Depth 1 -Compress
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)

    try {
        Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($formId)" -Headers @{
            Authorization = "Bearer $token"
            "OData-Version" = "4.0"
            "Content-Type" = "application/json; charset=utf-8"
            "If-Match" = "*"
        } -Method Patch -Body $bodyBytes
        Write-Host "Form updated successfully!"
    } catch {
        Write-Host "Error: $($_.Exception.Message)"
        if ($_.ErrorDetails.Message) {
            $errJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            Write-Host "Detail: $($errJson.error.message.Substring(0, [Math]::Min(500, $errJson.error.message.Length)))"
        }
    }

    # Publish
    Write-Host "Publishing..."
    $pubBody = @{ ParameterXml = "<importexportxml><entities><entity>sprk_document</entity></entities></importexportxml>" } | ConvertTo-Json
    Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/PublishXml" -Headers @{
        Authorization = "Bearer $token"; "OData-Version" = "4.0"; "Content-Type" = "application/json"
    } -Method Post -Body ([System.Text.Encoding]::UTF8.GetBytes($pubBody))
    Write-Host "Published."
} else {
    Write-Host "No changes needed."
}
