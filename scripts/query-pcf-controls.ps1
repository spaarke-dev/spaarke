# Query PCF controls registered in Dataverse
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$Filter = "Spaarke"
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

$uri = "$EnvironmentUrl/api/data/v9.2/customcontrols?`$select=name,compatibledatatypes,version"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    $filtered = $result.value | Where-Object { $_.name -like "*$Filter*" }

    Write-Host "PCF Controls matching '$Filter':"
    Write-Host ""

    if ($filtered.Count -eq 0) {
        Write-Host "  No controls found matching '$Filter'"
        Write-Host ""
        Write-Host "All available controls:"
        $result.value | ForEach-Object {
            Write-Host "  - $($_.name) (v$($_.version))"
        }
    } else {
        $filtered | ForEach-Object {
            Write-Host "  Name: $($_.name)"
            Write-Host "  Version: $($_.version)"
            Write-Host "  Compatible Types: $($_.compatibledatatypes)"
            Write-Host ""
        }
    }
} catch {
    Write-Error "Error: $_"
}
