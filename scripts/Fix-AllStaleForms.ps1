#!/usr/bin/env pwsh
# Fix ALL forms with stale static PCF parameters using string replacement
# Removes tenantId, apiBaseUrl, bffApiUrl static parameter elements from form XML

param(
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun
)

$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json"; "OData-Version" = "4.0"; Prefer = "odata.maxpagesize=500" }

# Get all main forms (type 2) for sprk_ entities
Write-Host "Scanning all sprk_ main forms..."
$query = "systemforms?`$filter=type eq 2&`$select=name,objecttypecode,formid&`$orderby=objecttypecode"
$r = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/$query" -Headers $headers
$sprk_forms = $r.value | Where-Object { $_.objecttypecode -like "sprk_*" }
Write-Host "Found $($sprk_forms.Count) sprk_ main forms"

$totalFixed = 0
$entitiesToPublish = @()

foreach ($form in $sprk_forms) {
    $detail = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($($form.formid))?`$select=formxml" -Headers $headers
    $xml = $detail.formxml
    $originalLength = $xml.Length
    $changes = 0

    # Remove static tenantId params: <tenantId type="SingleLine.Text" static="true">...</tenantId>
    $pattern1 = '<tenantId\s+type="[^"]*"\s+static="true">[^<]*</tenantId>'
    $newXml = [regex]::Replace($xml, $pattern1, '')
    if ($newXml.Length -ne $xml.Length) { $changes += ($xml.Length - $newXml.Length); $xml = $newXml }

    # Remove static apiBaseUrl params
    $pattern2 = '<apiBaseUrl\s+type="[^"]*"\s+static="true">[^<]*</apiBaseUrl>'
    $newXml = [regex]::Replace($xml, $pattern2, '')
    if ($newXml.Length -ne $xml.Length) { $changes += ($xml.Length - $newXml.Length); $xml = $newXml }

    # Remove static bffApiUrl params
    $pattern3 = '<bffApiUrl\s+type="[^"]*"\s+static="true">[^<]*</bffApiUrl>'
    $newXml = [regex]::Replace($xml, $pattern3, '')
    if ($newXml.Length -ne $xml.Length) { $changes += ($xml.Length - $newXml.Length); $xml = $newXml }

    if ($changes -gt 0) {
        Write-Host "FIX: $($form.objecttypecode) | $($form.name) | removed $changes chars of stale params"

        if (-not $DryRun) {
            $body = @{ formxml = $xml } | ConvertTo-Json -Depth 1
            $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            try {
                Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($($form.formid))" -Headers @{
                    Authorization = "Bearer $token"; "OData-Version" = "4.0"
                    "Content-Type" = "application/json; charset=utf-8"; "If-Match" = "*"
                } -Method Patch -Body $bodyBytes
                Write-Host "  Updated successfully"
                $totalFixed++
                if ($form.objecttypecode -notin $entitiesToPublish) {
                    $entitiesToPublish += $form.objecttypecode
                }
            } catch {
                $err = $_.ErrorDetails.Message
                Write-Host "  ERROR: $($err.Substring(0, [Math]::Min(200, $err.Length)))"
            }
        } else {
            Write-Host "  (dry run - not saving)"
            $totalFixed++
        }
    }
}

Write-Host ""
Write-Host "Fixed $totalFixed forms"

if ($entitiesToPublish.Count -gt 0 -and -not $DryRun) {
    Write-Host "Publishing $($entitiesToPublish.Count) entities..."
    $entityXml = ($entitiesToPublish | ForEach-Object { "<entity>$_</entity>" }) -join ""
    $pubBody = @{ ParameterXml = "<importexportxml><entities>$entityXml</entities></importexportxml>" } | ConvertTo-Json
    Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/PublishXml" -Headers @{
        Authorization = "Bearer $token"; "OData-Version" = "4.0"; "Content-Type" = "application/json"
    } -Method Post -Body ([System.Text.Encoding]::UTF8.GetBytes($pubBody))
    Write-Host "Published: $($entitiesToPublish -join ', ')"
}
