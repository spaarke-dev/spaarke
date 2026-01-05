# Query entity set names from Dataverse metadata
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv

if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
}

# Query all entities and filter client-side (metadata doesn't support startswith)
$uri = "$EnvironmentUrl/api/data/v9.2/EntityDefinitions?`$select=LogicalName,EntitySetName"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers

    Write-Host "Entity Logical Name -> Entity Set Name" -ForegroundColor Cyan
    Write-Host "---------------------------------------"

    # Filter client-side to only show sprk_ entities
    $filtered = $result.value | Where-Object {
        $_.LogicalName -like "sprk_ai*" -or $_.LogicalName -like "sprk_analysis*"
    } | Sort-Object LogicalName

    $filtered | ForEach-Object {
        Write-Host "$($_.LogicalName) -> $($_.EntitySetName)"
    }
} catch {
    Write-Error "Error: $_"
}
