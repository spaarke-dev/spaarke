#!/usr/bin/env pwsh
# Fix Document form - find ALL remaining tenantId/apiBaseUrl references

param([string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com")

$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json"; "OData-Version" = "4.0" }

$formId = "9088d6a4-4cf2-f011-8406-7c1e520aa4df"
$r = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($formId)?`$select=formxml" -Headers $headers
[xml]$doc = $r.formxml

# Find ALL elements named tenantId, apiBaseUrl, bffApiUrl anywhere in the XML
foreach ($paramName in @("tenantId", "apiBaseUrl", "bffApiUrl")) {
    $nodes = $doc.SelectNodes("//$paramName")
    Write-Host "$paramName`: $($nodes.Count) occurrences"
    foreach ($node in $nodes) {
        $parent = $node.ParentNode
        $grandparent = $parent.ParentNode
        $ctrlName = "unknown"
        if ($grandparent -and $grandparent.GetAttribute) {
            $ctrlName = $grandparent.GetAttribute("name")
            if (-not $ctrlName) { $ctrlName = $grandparent.GetAttribute("id") }
        }
        $static = $node.GetAttribute("static")
        $type = $node.GetAttribute("type")
        Write-Host "  -> parent=$($parent.LocalName) control=$ctrlName static=$static type=$type value=$($node.InnerText.Substring(0, [Math]::Min(50, $node.InnerText.Length)))"
    }
}
