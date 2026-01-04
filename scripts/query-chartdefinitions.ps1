# Query sprk_chartdefinition records from Dataverse
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$ShowSchema
)

$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv

if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

if ($ShowSchema) {
    Write-Host "Querying entity schema..."
    $uri = "$EnvironmentUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_chartdefinition')/Attributes?`$select=LogicalName,AttributeType"
    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers
        Write-Host "sprk_chartdefinition attributes:"
        $result.value | ForEach-Object {
            Write-Host "  $($_.LogicalName) ($($_.AttributeType))"
        }
    } catch {
        Write-Error "Error querying schema: $_"
    }
    exit 0
}

# Query records - only select fields we know exist
$uri = "$EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions?`$top=10"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    Write-Host "Found $($result.value.Count) chart definition records:"
    $result.value | ForEach-Object {
        Write-Host "  - Name: $($_.sprk_name)"
        Write-Host "    ID: $($_.sprk_chartdefinitionid)"
        Write-Host "    Visual Type: $($_.sprk_visualtype)"
        Write-Host ""
    }
    if ($result.value.Count -eq 0) {
        Write-Host "No records found. Entity exists but is empty."
    }
} catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        Write-Host "Entity 'sprk_chartdefinition' not found in Dataverse."
    } else {
        Write-Error "Error: $_"
    }
}
