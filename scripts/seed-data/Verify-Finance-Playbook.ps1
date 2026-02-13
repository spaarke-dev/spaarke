$env:DATAVERSE_URL = 'https://spaarkedev1.crm.dynamics.com'
$token = az account get-access-token --resource $env:DATAVERSE_URL --query 'accessToken' -o tsv
$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$filter = "sprk_name eq 'Finance Invoice Processing'"
$expand = "sprk_playbook_tool(`$select=sprk_name),sprk_playbook_skill(`$select=sprk_name),sprk_analysisplaybook_action(`$select=sprk_name)"
$uri = "$env:DATAVERSE_URL/api/data/v9.2/sprk_analysisplaybooks?`$filter=$filter&`$expand=$expand"

try {
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get

    if ($response.value.Count -eq 0) {
        Write-Host "ERROR: Finance Invoice Processing playbook not found!" -ForegroundColor Red
        exit 1
    }

    $playbook = $response.value[0]

    Write-Host ""
    Write-Host "=== Finance Invoice Processing Playbook ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "ID: $($playbook.sprk_analysisplaybookid)" -ForegroundColor Green
    Write-Host "Name: $($playbook.sprk_name)"
    Write-Host "Description: $($playbook.sprk_description.Substring(0, [Math]::Min(100, $playbook.sprk_description.Length)))..."
    Write-Host "Public: $($playbook.sprk_ispublic)"
    Write-Host ""

    Write-Host "Skills ($($playbook.sprk_playbook_skill.Count)):" -ForegroundColor Yellow
    $playbook.sprk_playbook_skill | ForEach-Object { Write-Host "  ✓ $($_.sprk_name)" -ForegroundColor Green }
    Write-Host ""

    Write-Host "Actions ($($playbook.sprk_analysisplaybook_action.Count)):" -ForegroundColor Yellow
    $playbook.sprk_analysisplaybook_action | ForEach-Object { Write-Host "  ✓ $($_.sprk_name)" -ForegroundColor Green }
    Write-Host ""

    Write-Host "Tools ($($playbook.sprk_playbook_tool.Count)):" -ForegroundColor Yellow
    $playbook.sprk_playbook_tool | ForEach-Object { Write-Host "  ✓ $($_.sprk_name)" -ForegroundColor Green }
    Write-Host ""

    Write-Host "✅ Playbook verification PASSED" -ForegroundColor Green
    Write-Host ""

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}
