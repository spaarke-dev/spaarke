param(
    [Parameter(Mandatory=$true)]
    [string]$Method,
    [Parameter(Mandatory=$true)]
    [string]$RelativeUrl,
    [string]$Body = $null,
    [string]$ContentType = "application/json"
)

$token = (az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to get access token"
    exit 1
}

$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"
$fullUrl = "$baseUrl/$RelativeUrl"

$headers = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = $ContentType
}

Write-Host "[$Method] $fullUrl"

if ($Method -eq "GET") {
    $response = Invoke-WebRequest -Uri $fullUrl -Headers $headers -Method Get -UseBasicParsing
} elseif ($Method -eq "POST") {
    $response = Invoke-WebRequest -Uri $fullUrl -Headers $headers -Method Post -Body $Body -UseBasicParsing
} elseif ($Method -eq "PATCH") {
    $headers['If-Match'] = '*'
    $response = Invoke-WebRequest -Uri $fullUrl -Headers $headers -Method Patch -Body $Body -UseBasicParsing
}

Write-Host "Status: $($response.StatusCode)"
$response.Content
