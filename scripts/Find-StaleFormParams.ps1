#!/usr/bin/env pwsh
# Find and fix stale PCF control parameters in Dataverse forms
# These are static params like tenantId, apiBaseUrl that were removed during R2 migration

param(
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$Fix
)

$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json"; "OData-Version" = "4.0"; Prefer = "odata.maxpagesize=500" }

# Stale parameter names to look for
$staleParams = @("tenantId", "apiBaseUrl", "bffApiUrl", "apiScope")

# Get all main forms (type 2) for sprk_ entities
Write-Host "Scanning forms in $DataverseUrl..."
$query = "systemforms?`$filter=type eq 2&`$select=name,objecttypecode,formid&`$orderby=objecttypecode"
$r = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/$query" -Headers $headers

$sprk_forms = $r.value | Where-Object { $_.objecttypecode -like "sprk_*" }
Write-Host "Found $($sprk_forms.Count) sprk_ main forms to scan"
Write-Host ""

$issues = @()
$formCount = 0
foreach ($form in $sprk_forms) {
    $formCount++
    $detail = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($($form.formid))?`$select=formxml" -Headers $headers
    $xml = $detail.formxml

    foreach ($param in $staleParams) {
        if ($xml.Contains("<$param ")) {
            # Find which control has it
            $ctrlPattern = 'name="([^"]+)"'
            $ctrlMatches = [regex]::Matches($xml, "controlDescription.*?name=""([^""]+)"".*?<$param.*?</controlDescription>", "Singleline")
            foreach ($cm in $ctrlMatches) {
                $ctrlName = $cm.Groups[1].Value
                $issues += @{
                    Entity = $form.objecttypecode
                    Form = $form.name
                    FormId = $form.formid
                    Control = $ctrlName
                    Param = $param
                    FormXml = $xml
                }
                Write-Host "ISSUE: $($form.objecttypecode) | $($form.name) | $ctrlName | stale param: $param"
            }
        }
    }
}

Write-Host ""
Write-Host "Scan complete: $formCount forms checked, $($issues.Count) stale params found"

if ($Fix -and $issues.Count -gt 0) {
    Write-Host ""
    Write-Host "Fixing stale parameters..."

    # Group by form
    $formGroups = $issues | Group-Object { $_.FormId }

    foreach ($group in $formGroups) {
        $formId = $group.Name
        $formIssues = $group.Group
        $entity = $formIssues[0].Entity
        $formName = $formIssues[0].Form
        $xml = $formIssues[0].FormXml

        Write-Host "  Fixing: $entity | $formName"

        # Remove stale parameter elements from the XML
        foreach ($issue in $formIssues) {
            $param = $issue.Param
            # Remove the parameter element (all form factors)
            # Pattern: <paramName type="..." static="true">...</paramName>
            $xml = [regex]::Replace($xml, "<$param\s+type=""[^""]*""\s+static=""true"">[^<]*</$param>", "")
            Write-Host "    Removed: $param from $($issue.Control)"
        }

        # Update the form
        $updateBody = @{ formxml = $xml } | ConvertTo-Json -Depth 1
        $updateHeaders = @{
            Authorization = "Bearer $token"
            "OData-Version" = "4.0"
            "Content-Type" = "application/json"
            "If-Match" = "*"
        }

        try {
            Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/systemforms($formId)" -Headers $updateHeaders -Method Patch -Body ([System.Text.Encoding]::UTF8.GetBytes($updateBody))
            Write-Host "    Updated form successfully"
        } catch {
            Write-Host "    ERROR updating form: $($_.Exception.Message)"
        }
    }

    # Publish changes
    Write-Host ""
    Write-Host "Publishing customizations..."
    $publishBody = @{ ParameterXml = "<importexportxml><entities><entity>sprk_document</entity></entities></importexportxml>" } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/PublishXml" -Headers @{
            Authorization = "Bearer $token"
            "OData-Version" = "4.0"
            "Content-Type" = "application/json"
        } -Method Post -Body ([System.Text.Encoding]::UTF8.GetBytes($publishBody))
        Write-Host "Published successfully"
    } catch {
        Write-Host "Publish error (non-fatal): $($_.Exception.Message)"
    }
}
